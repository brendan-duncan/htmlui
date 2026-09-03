using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WebUI.Html.Editor.Cdp
{
    /// <summary>
    /// A DevTools Protocol connection: JSON-RPC over a single web socket, with flat sessions
    /// (every target attaches onto this one socket and is addressed by <c>sessionId</c>).
    /// </summary>
    /// <remarks>
    /// Sends may come from the Unity main thread; receiving runs on a background task. Command replies
    /// complete their <see cref="Task"/> wherever the receive loop is running, so callers must not block
    /// the main thread on them. Protocol events are queued and drained by the owner in
    /// <see cref="TryDequeueEvent"/> so they are handled on the main thread.
    /// </remarks>
    internal sealed class CdpClient : IDisposable
    {
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> _pending
            = new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly ConcurrentQueue<Dictionary<string, object>> _events
            = new ConcurrentQueue<Dictionary<string, object>>();

        private int _nextId;
        private Task _receiveLoop;
        private volatile bool _closed;

        /// <summary>Set when the receive loop stops unexpectedly.</summary>
        public Exception Fault { get; private set; }
        public bool IsOpen => !_closed && _socket.State == WebSocketState.Open;

        public static async Task<CdpClient> ConnectAsync(string webSocketUrl, CancellationToken ct)
        {
            var client = new CdpClient();
            await client._socket.ConnectAsync(new Uri(webSocketUrl), ct).ConfigureAwait(false);
            client._receiveLoop = Task.Run(client.ReceiveLoopAsync);
            return client;
        }

        // ------------------------------------------------------------------ sending

        /// <summary>Sends a command and awaits its reply. <paramref name="paramsJson"/> is a raw JSON object body.</summary>
        public Task<Dictionary<string, object>> SendAsync(string method, string paramsJson = null, string sessionId = null)
        {
            int id = Interlocked.Increment(ref _nextId);
            var tcs = new TaskCompletionSource<Dictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending[id] = tcs;

            var frame = BuildFrame(id, method, paramsJson, sessionId);
            _ = SendRawAsync(frame).ContinueWith(t =>
            {
                if (t.IsFaulted && _pending.TryRemove(id, out var pending))
                    pending.TrySetException(t.Exception ?? new Exception("send failed"));
            }, TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>Sends a command without waiting for the reply. Failures surface through <see cref="Fault"/>.</summary>
        public void Send(string method, string paramsJson = null, string sessionId = null)
        {
            int id = Interlocked.Increment(ref _nextId);
            _ = SendRawAsync(BuildFrame(id, method, paramsJson, sessionId));
        }

        private static string BuildFrame(int id, string method, string paramsJson, string sessionId)
        {
            var sb = new StringBuilder(128 + (paramsJson?.Length ?? 0));
            sb.Append("{\"id\":").Append(id).Append(",\"method\":");
            Json.Quote(method, sb);
            if (!string.IsNullOrEmpty(paramsJson)) sb.Append(",\"params\":").Append(paramsJson);
            if (!string.IsNullOrEmpty(sessionId))
            {
                sb.Append(",\"sessionId\":");
                Json.Quote(sessionId, sb);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private async Task SendRawAsync(string text)
        {
            if (_closed) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            try { await _sendLock.WaitAsync(_cancel.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            try
            {
                if (_socket.State != WebSocketState.Open) return;
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancel.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                Fault ??= e;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ------------------------------------------------------------------ receiving

        private async Task ReceiveLoopAsync()
        {
            // Screencast frames arrive base64-encoded, so single messages routinely run to hundreds of KB.
            var buffer = new byte[64 * 1024];
            var message = new MemoryStream(256 * 1024);

            try
            {
                while (!_cancel.IsCancellationRequested && _socket.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancel.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                    Dispatch(text);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Fault ??= e;
            }
            finally
            {
                FailPending(Fault ?? new Exception("DevTools connection closed"));
            }
        }

        private void Dispatch(string text)
        {
            if (!(Json.Parse(text) is Dictionary<string, object> msg)) return;

            if (msg.TryGetValue("id", out var rawId) && rawId is double idNum)
            {
                if (_pending.TryRemove((int)idNum, out var tcs))
                {
                    var error = Json.Dict(msg, "error");
                    if (error != null) tcs.TrySetException(new CdpException(Json.Str(error, "message", "CDP error")));
                    else tcs.TrySetResult(Json.Dict(msg, "result") ?? new Dictionary<string, object>());
                }
                return;
            }

            if (msg.ContainsKey("method")) _events.Enqueue(msg);
        }

        /// <summary>Takes the next protocol event, if any. Call from the main thread.</summary>
        public bool TryDequeueEvent(out Dictionary<string, object> evt) => _events.TryDequeue(out evt);

        private void FailPending(Exception e)
        {
            foreach (var key in new List<int>(_pending.Keys))
                if (_pending.TryRemove(key, out var tcs)) tcs.TrySetException(e);
        }

        // ------------------------------------------------------------------ teardown

        public void Dispose()
        {
            if (_closed) return;
            _closed = true;
            try { _cancel.Cancel(); } catch { /* already gone */ }
            try
            {
                if (_socket.State == WebSocketState.Open)
                    _socket.Abort();   // CloseAsync would need a round trip we are not going to wait for
            }
            catch { /* nothing useful to do while tearing down */ }

            FailPending(new ObjectDisposedException(nameof(CdpClient)));
            try { _socket.Dispose(); } catch { }
            try { _cancel.Dispose(); } catch { }
        }
    }

    internal sealed class CdpException : Exception
    {
        public CdpException(string message) : base(message) { }
    }
}
