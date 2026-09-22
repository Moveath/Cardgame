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
            if (!normalizedPath.Contains("/Resources/Cards/")
                && !normalizedPath.Contains("/Resources/Avatars/")
                && !normalizedPath.Contains("/Resources/PlayerAvatars/")
                && !normalizedPath.Contains("/Resources/Badges/"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            // Мипмапы нужны, чтобы GPU аккуратно уменьшал мелкий орнамент рубашки
            // (колода/бита/карты в руках соперников показывают карту мельче оригинала) —
            // без них плотный узор превращается в шум вместо гладкого уменьшения.
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
            importer.spritePixelsPerUnit = 100f;

            // Без этого Unity по умолчанию жмёт спрайты блочным сжатием (DXT/BC) —
            // на плоской векторной графике с тонкими линиями это даёт заметные "пиксельные"
            // артефакты. Карт немного, лишние пара МБ на прототипе не критичны.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.crunchedCompression = false;
        }
    }
}
