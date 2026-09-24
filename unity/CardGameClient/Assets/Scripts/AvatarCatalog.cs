using System.Collections.Generic;
using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Все аватары, разложенные по наборам: общий пул выдуманных персонажей и лица
    /// реальных людей (по набору на человека). Наборы нужны, чтобы в галерее не было
    /// стены из двенадцати одинаковых лиц подряд — каждый человек показан отдельной
    /// строкой, и видно, что это костюмы одного и того же.
    /// </summary>
    public static class AvatarCatalog
    {
        public const string CommonGroupId = "common";

        /// <summary>Человеческие имена наборов. Незнакомая папка покажется как есть, ничего не сломав.</summary>
        private static readonly Dictionary<string, string> GroupTitles = new Dictionary<string, string>
        {
            { CommonGroupId, "Персонажи" },
            { "yegor", "Егор" },
            { "yelnur", "Ельнур" },
            { "marat", "Марат" },
            { "ali", "Али" },
            { "aslan", "Аслан" },
            { "danil", "Данил" }
        };

        private static List<AvatarGroup> _groups;

        public sealed class AvatarGroup
        {
            public string Id;
            public string Title;
            public List<Sprite> Sprites = new List<Sprite>();
        }

        public static IReadOnlyList<AvatarGroup> Groups
        {
            get
            {
                Load();
                return _groups;
            }
        }

        /// <summary>Общий пул выдуманных персонажей.</summary>
        public static IReadOnlyList<Sprite> CommonAvatars
        {
            get
            {
                Load();
                foreach (var group in _groups)
                {
                    if (group.Id == CommonGroupId)
                    {
                        return group.Sprites;
                    }
                }

                return new List<Sprite>();
            }
        }

        /// <summary>
        /// Портреты для соперников, по одному на бота. Сначала разбираются наборы реальных
        /// людей — по одному образу с каждого, чтобы за столом сидели разные друзья, а не
        /// один и тот же в разной одежде. Если друзей меньше, чем мест, кто-то приходит
        /// вторым образом. Общий пул выдуманных персонажей — уже на самый крайний случай:
        /// сейчас смысл в том, чтобы играть будто с друзьями. Набор, из которого игрок
        /// выбрал себя, исключён целиком — иначе играешь против собственного лица.
        /// </summary>
        public static List<Sprite> OpponentPortraits(Sprite own, int count)
        {
            Load();

            var result = new List<Sprite>();
            if (count <= 0)
            {
                return result;
            }

            var ownGroup = GroupIdOf(own);
            var personal = new List<AvatarGroup>();
            foreach (var group in _groups)
            {
                if (group.Id != CommonGroupId && group.Id != ownGroup && group.Sprites.Count > 0)
                {
                    personal.Add(group);
                }
            }

            Shuffle(personal);

            foreach (var group in personal)
            {
                if (result.Count >= count)
                {
                    break;
                }

                result.Add(group.Sprites[Random.Range(0, group.Sprites.Count)]);
            }

            // Друзей не хватило — тот же человек садится ещё раз, но в другом образе.
            for (var guard = 0; result.Count < count && personal.Count > 0 && guard < 12; guard++)
            {
                foreach (var group in personal)
                {
                    if (result.Count >= count)
                    {
                        break;
                    }

                    var spare = FirstUnused(group.Sprites, result);
                    if (spare != null)
                    {
                        result.Add(spare);
                    }
                }
            }

            if (result.Count >= count)
            {
                return result;
            }

            var common = new List<Sprite>();
            foreach (var sprite in CommonAvatars)
            {
                if (sprite != own)
                {
                    common.Add(sprite);
                }
            }

            Shuffle(common);
            foreach (var sprite in common)
            {
                if (result.Count >= count)
                {
                    break;
                }

                result.Add(sprite);
            }

            return result;
        }

        private static Sprite FirstUnused(List<Sprite> sprites, List<Sprite> taken)
        {
            var offset = Random.Range(0, sprites.Count);
            for (var i = 0; i < sprites.Count; i++)
            {
                var sprite = sprites[(offset + i) % sprites.Count];
                if (!taken.Contains(sprite))
                {
                    return sprite;
                }
            }

            return null;
        }

        private static void Shuffle<T>(List<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = Random.Range(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        /// <summary>
        /// Подпись набора, если портрет — лицо реального человека. Для выдуманных
        /// персонажей возвращает null: у них своего имени нет.
        /// </summary>
        public static string PersonalTitleOf(Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            Load();
            foreach (var group in _groups)
            {
                if (group.Id != CommonGroupId && group.Sprites.Contains(sprite))
                {
                    return group.Title;
                }
            }

            return null;
        }

        public static string GroupIdOf(Sprite sprite)
        {
            if (sprite == null)
            {
                return null;
            }

            Load();
            foreach (var group in _groups)
            {
                if (group.Sprites.Contains(sprite))
                {
                    return group.Id;
                }
            }

            return null;
        }

        public static Sprite Find(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return null;
            }

            Load();
            foreach (var group in _groups)
            {
                foreach (var sprite in group.Sprites)
                {
                    if (sprite.name == spriteName)
                    {
                        return sprite;
                    }
                }
            }

            return null;
        }

        private static void Load()
        {
            if (_groups != null)
            {
                return;
            }

            _groups = new List<AvatarGroup>();

            var common = new AvatarGroup { Id = CommonGroupId, Title = TitleFor(CommonGroupId) };
            common.Sprites.AddRange(Resources.LoadAll<Sprite>("Avatars"));
            if (common.Sprites.Count > 0)
            {
                _groups.Add(common);
            }

            // Набор определяется по имени файла до подчёркивания (yegor_03 -> yegor),
            // поэтому новый человек добавляется просто папкой с картинками — без правок кода.
            var byPerson = new Dictionary<string, AvatarGroup>();
            foreach (var sprite in Resources.LoadAll<Sprite>("PlayerAvatars"))
            {
                var id = PersonIdFrom(sprite.name);
                if (!byPerson.TryGetValue(id, out var group))
                {
                    group = new AvatarGroup { Id = id, Title = TitleFor(id) };
                    byPerson[id] = group;
                    _groups.Add(group);
                }

                group.Sprites.Add(sprite);
            }
        }

        private static string PersonIdFrom(string spriteName)
        {
            var underscore = spriteName.LastIndexOf('_');
            return underscore > 0 ? spriteName.Substring(0, underscore) : spriteName;
        }

        private static string TitleFor(string id)
        {
            if (GroupTitles.TryGetValue(id, out var title))
            {
                return title;
            }

            return id.Length > 0 ? char.ToUpperInvariant(id[0]) + id.Substring(1) : id;
        }
    }
}
