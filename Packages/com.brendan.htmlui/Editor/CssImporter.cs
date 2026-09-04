using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace HtmlUI.Editor
{
    /// <summary>Imports .css files as TextAssets so they can be assigned to HtmlDocument.StyleSheets (.html is handled by Unity itself).</summary>
    [ScriptedImporter(1, "css")]
    public class CssImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = new TextAsset(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("main", text);
            ctx.SetMainObject(text);
        }
    }
}
