using UnityEditor;
using UnityEngine;

namespace CardGameClient.EditorTools
{
    /// <summary>
    /// Автоматически настраивает импорт для картинок карт, брошенных в Resources/Cards —
    /// чтобы не объяснять на словах, какие галочки ставить в инспекторе для каждого из 37 файлов.
    /// </summary>
    public sealed class CardArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var normalizedPath = assetPath.Replace('\\', '/');
            if (!normalizedPath.Contains("/Resources/Cards/"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;
        }
    }
}
