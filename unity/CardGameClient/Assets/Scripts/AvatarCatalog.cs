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
            { "alisher", "Алишер" },
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
        /// Портреты для соперников: по одному человеку на набор. Из набора реального
        /// человека берётся один случайный костюм, потому что два бота с одним лицом в
        /// разной одежде за столом читаются как ошибка, а не как разнообразие. Набор,
        /// из которого игрок выбрал себя, исключается целиком — иначе играешь против себя.
        /// </summary>
        public static List<Sprite> OpponentPool(Sprite own)
        {
            Load();

            var ownGroup = GroupIdOf(own);
            var pool = new List<Sprite>();

            foreach (var group in _groups)
            {
                if (group.Sprites.Count == 0)
                {
                    continue;
                }

                if (group.Id == CommonGroupId)
                {
                    // В общем наборе каждая картинка — отдельный персонаж, а не костюм,
                    // поэтому здесь вычёркивается только сам выбранный портрет.
                    foreach (var sprite in group.Sprites)
                    {
                        if (sprite != own)
                        {
                            pool.Add(sprite);
                        }
                    }

                    continue;
                }

                if (group.Id == ownGroup)
                {
                    continue;
                }

                pool.Add(group.Sprites[Random.Range(0, group.Sprites.Count)]);
            }

            return pool;
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
