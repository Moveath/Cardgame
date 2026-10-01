using System.Collections.Generic;
using CardGame.Core.Cards;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>Shared chest textures. A skin is reused by every pack; no per-player pixel copies.</summary>
    public static class ChestArt
    {
        private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();
        private static readonly Dictionary<(Rank, Suit), Sprite> Faces = new Dictionary<(Rank, Suit), Sprite>();
        private static Sprite _greenBack;
        private static Sprite _inspectionLid;
        private static Sprite _inspectionHinge;

        /// <summary>The existing lid's metal fitting, shared by both inspection hinges.</summary>
        public static Sprite InspectionHinge
        {
            get
            {
                if (_inspectionHinge != null) return _inspectionHinge;
                var source = Load("table/lids/lid-03-open");
                if (source == null) return null;
                var texture = source.texture;
                var scaleX = texture.width / 512f;
                var scaleY = texture.height / 512f;
                // Both leaves and the pin, from the left hinge of the original artwork.
                _inspectionHinge = Sprite.Create(texture,
                    new Rect(122f * scaleX, 286f * scaleY, 54f * scaleX, 27f * scaleY),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                // The source has wood beside the narrow upper leaf. A pair of fitted
                // quads samples only metal instead of pasting a rectangular wood patch.
                var pixels = new[]
                {
                    new Vector2(1f, 1f), new Vector2(53f, 1f),
                    new Vector2(53f, 15f), new Vector2(1f, 15f),
                    new Vector2(11f, 15f), new Vector2(43f, 15f),
                    new Vector2(42f, 26f), new Vector2(14f, 26f)
                };
                // OverrideGeometry takes rect-relative pixels; Unity applies pivot and PPU.
                for (var i = 0; i < pixels.Length; i++)
                    pixels[i] = new Vector2(pixels[i].x * scaleX, pixels[i].y * scaleY);
                _inspectionHinge.OverrideGeometry(pixels, new ushort[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 });
                _inspectionHinge.name = "Inspection hinge";
                return _inspectionHinge;
            }
        }

        /// <summary>Reuse the table lid texture without allocating another bitmap.</summary>
        public static Sprite InspectionLid
        {
            get
            {
                if (_inspectionLid != null) return _inspectionLid;
                var source = Load("table/lids/lid-02-half");
                if (source == null) return null;
                var texture = source.texture;
                var scaleX = texture.width / 1254f;
                var scaleY = texture.height / 1254f;
                _inspectionLid = Sprite.Create(texture,
                    new Rect(123f * scaleX, 709f * scaleY, 1008f * scaleX, 315f * scaleY),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                _inspectionLid.name = "Inspection lid";
                return _inspectionLid;
            }
        }

        public static Sprite Load(string relativePathWithoutExt)
        {
            if (!Cache.TryGetValue(relativePathWithoutExt, out var sprite) || sprite == null)
            {
                sprite = Resources.Load<Sprite>("Chests/" + relativePathWithoutExt);
                Cache[relativePathWithoutExt] = sprite;
            }
            return sprite;
        }

        /// <summary>A completed set contains all suits; one face identifies its rank in the inspector.</summary>
        public static Sprite Face(Rank rank, Suit suit = Suit.Spades)
        {
            var key = (rank, suit);
            if (!Faces.TryGetValue(key, out var sprite) || sprite == null)
            {
                var token = rank switch
                {
                    Rank.Jack => "j",
                    Rank.Queen => "q",
                    Rank.King => "k",
                    Rank.Ace => "a",
                    _ => ((int)rank).ToString()
                };
                sprite = Resources.Load<Sprite>("Cards/" + token + "_" + suit.ToString().ToLowerInvariant());
                Faces[key] = sprite;
            }
            return sprite;
        }

        public static Sprite GreenBack
        {
            get
            {
                if (_greenBack != null) return _greenBack;
                var texture = Resources.Load<Texture2D>("Cards/back");
                if (texture == null) return Resources.Load<Sprite>("Cards/back");
                // The existing artwork has gray margins. Coordinates are from its 469 x 667 source.
                var scaleX = texture.width / 469f;
                var scaleY = texture.height / 667f;
                var crop = new Rect(20f * scaleX, 12f * scaleY, 435f * scaleX, 649f * scaleY);
                _greenBack = Sprite.Create(texture, crop, new Vector2(0.5f, 0.5f), 100f,
                    0, SpriteMeshType.FullRect);
                _greenBack.name = "Chest green card back";
                return _greenBack;
            }
        }
    }
}
