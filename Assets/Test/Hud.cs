using UnityEngine;
using HtmlUI;

public class Hud : MonoBehaviour
{
    [SerializeField] HtmlDocument doc;   // left empty: uses the HtmlDocument on this GameObject
    int score;

    void OnEnable()
    {
        if (doc == null) doc = GetComponent<HtmlDocument>();

        // Event handlers can be registered at any time; they are kept until the panel exists.
        doc.OnAction("pause", OnPause);

        // Element access (Q, QAll, Eval) needs the browser-side panel, which HtmlDocument creates in its own
        // OnEnable. Component OnEnable order is not guaranteed, so wait for Created if it is not there yet.
        if (doc.IsCreated) Refresh(doc);
        else doc.Created += Refresh;
    }

    void OnDisable()
    {
        doc.Created -= Refresh;
        doc.OffAction("pause", OnPause);
    }

    void Update()
    {
        AddScore(1);
    }

    // Called by your game code, e.g. from a pickup's OnTriggerEnter.
    public void AddScore(int amount)
    {
        score += amount;
        if (doc.IsCreated) Refresh(doc);
    }

    void OnPause(HtmlEvent e) => Time.timeScale = 0f;

    void Refresh(HtmlDocument d) => d.Q("#score").Text = score.ToString();
}
