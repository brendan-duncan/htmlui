using UnityEditor.AssetImporters;

namespace HtmlUI.Editor
{
    /// <summary>Imports .css files as TextAssets, with the style-sheet icon, so they can be assigned to HtmlDocument.StyleSheets.</summary>
    [ScriptedImporter(2, "css")]
    public class CssImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx) => HtmlAssetImport.ImportText(ctx, HtmlAssetImport.CssIcon);
    }
}
