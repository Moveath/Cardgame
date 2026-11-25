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

        /// <summary>Стартовые женские аватары (чётные avatar_02..avatar_12) — им никогда не достаются мужские имена.</summary>
        private static readonly HashSet<string> FemaleAvatarIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "avatar_02", "avatar_04", "avatar_06", "avatar_08", "avatar_10", "avatar_12",
            "Head_Avatar02", "Head_Avatar04", "Head_Avatar06", "Head_Avatar08", "Head_Avatar10", "Head_Avatar12"
        };

        /// <summary>Тип телосложения 3D-персонажа (обычное мужское, плотное, женское, пожилое мужское/женское).</summary>
        public enum BodyArchetype { Male, MaleHeavy, Female, MaleElder, FemaleElder }

        /// <summary>Оттенок и фактура кожи кистей рук персонажа (соперника и вида от первого лица).</summary>
        public enum HandSkinTone { Default, Fair, Aged, Dark }

        public static BodyArchetype BodyArchetypeOf(string avatarOrHeadId)
        {
            if (string.IsNullOrEmpty(avatarOrHeadId)) return BodyArchetype.Male;
            var id = avatarOrHeadId.Trim();
            if (string.Equals(id, "avatar_05", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar05", System.StringComparison.OrdinalIgnoreCase))
                return BodyArchetype.MaleHeavy;
            if (string.Equals(id, "avatar_03", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar03", System.StringComparison.OrdinalIgnoreCase))
                return BodyArchetype.MaleElder;
            if (string.Equals(id, "avatar_06", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar06", System.StringComparison.OrdinalIgnoreCase))
                return BodyArchetype.FemaleElder;
            if (IsFemale(id)) return BodyArchetype.Female;
            return BodyArchetype.Male;
        }

        public static HandSkinTone HandSkinToneOf(string avatarOrHeadId)
        {
            if (string.IsNullOrEmpty(avatarOrHeadId)) return HandSkinTone.Default;
            var id = avatarOrHeadId.Trim();
            if (string.Equals(id, "avatar_08", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar08", System.StringComparison.OrdinalIgnoreCase))
                return HandSkinTone.Dark;
            if (string.Equals(id, "avatar_03", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar03", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "avatar_06", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, "Head_Avatar06", System.StringComparison.OrdinalIgnoreCase))
                return HandSkinTone.Aged;
            if (IsFemale(id)) return HandSkinTone.Fair;
            return HandSkinTone.Default;
        }

        public static string HeadlessBodyResourceFor(BodyArchetype archetype)
        {
            switch (archetype)
            {
                case BodyArchetype.Female:
                case BodyArchetype.FemaleElder:
                    return "ThreeD/Characters/BaseFemale_Headless";
                case BodyArchetype.MaleHeavy:
                    return "ThreeD/Characters/BaseMaleHeavy_Headless";
                default:
                    return "ThreeD/Characters/BaseMale_Headless";
            }
        }

        public static string FpsBodyResourceFor(BodyArchetype archetype)
        {
            switch (archetype)
            {
                case BodyArchetype.Female:
                case BodyArchetype.FemaleElder:
                    return "ThreeD/Characters/BaseFemale_FpsBody";
                case BodyArchetype.MaleHeavy:
                    return "ThreeD/Characters/BaseMaleHeavy_FpsBody";
                default:
                    return "ThreeD/Characters/BaseMale_FpsBody";
            }
        }

        public static string OpponentHandsMaterialFor(HandSkinTone tone)
        {
            switch (tone)
            {
                case HandSkinTone.Dark: return "ThreeD/Materials/OpponentHands_Dark";
                case HandSkinTone.Aged: return "ThreeD/Materials/OpponentHands_Aged";
                case HandSkinTone.Fair: return "ThreeD/Materials/OpponentHands_Fair";
                default: return "ThreeD/Materials/OpponentHands";
            }
        }

        public static string HandSkinTextureResourceFor(HandSkinTone tone)
        {
            switch (tone)
            {
                case HandSkinTone.Dark: return "ThreeD/Hands/TripoHands_Dark_BaseColor";
                case HandSkinTone.Aged: return "ThreeD/Hands/TripoHands_Aged_BaseColor";
                case HandSkinTone.Fair: return "ThreeD/Hands/TripoHands_Fair_BaseColor";
                default: return "ThreeD/Hands/TripoHands_BaseColor";
            }
        }

        private static readonly string[] MaleBotNames =
        {
            "Борис", "Игорь", "Пётр", "Сергей", "Алексей", "Максим", "Тимур", "Артур", "Виктор", "Роман"
        };

        private static readonly string[] FemaleBotNames =
        {
            "Вера", "Ольга", "Марина", "Лера", "Соня", "Ника", "Алина", "Елена", "Дарья", "Ирина"
        };

        /// <summary>Связь ID друга/аватара с именем его 3D-головы в Resources/ThreeD/Characters/Heads/.</summary>
        private static readonly (string PersonId, string AvatarId, string HeadId)[] KnownFriendHeads =
        {
            ("marat", "marat_01", "Head_Marat"),
            ("ali", "ali_01", "Head_Ali"),
            ("aslan", "aslan_01", "Head_Aslan"),
            ("danil", "danil_01", "Head_Danil"),
            ("yegor", "yegor_01", "Head_Yegor"),
            ("yelnur", "yelnur_01", "Head_Yelnur"),
        };

        private static readonly Dictionary<string, bool> HeadExistsCache = new Dictionary<string, bool>(System.StringComparer.OrdinalIgnoreCase);

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

        /// <summary>Возвращает имя друга по строковому avatarId (например "marat_01" или "Head_Marat" -> "Марат"), либо null для выдуманных персонажей.</summary>
        public static string PersonalTitleOfId(string avatarOrHeadId)
        {
            if (string.IsNullOrEmpty(avatarOrHeadId)) return null;
            for (var i = 0; i < KnownFriendHeads.Length; i++)
            {
                var entry = KnownFriendHeads[i];
                if (string.Equals(avatarOrHeadId, entry.HeadId, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(avatarOrHeadId, entry.PersonId, System.StringComparison.OrdinalIgnoreCase)
                    || avatarOrHeadId.StartsWith(entry.PersonId + "_", System.StringComparison.OrdinalIgnoreCase))
                {
                    return TitleFor(entry.PersonId);
                }
            }

            var personId = PersonIdFrom(avatarOrHeadId);
            if (!string.IsNullOrEmpty(personId) && !string.Equals(personId, "avatar", System.StringComparison.OrdinalIgnoreCase)
                && GroupTitles.TryGetValue(personId, out var title) && !string.Equals(personId, CommonGroupId, System.StringComparison.OrdinalIgnoreCase))
            {
                return title;
            }

            return null;
        }

        /// <summary>True для женских стартовых персонажей (avatar_02, 04, 06, 08, 10, 12).</summary>
        public static bool IsFemale(string avatarOrHeadId) =>
            !string.IsNullOrEmpty(avatarOrHeadId) && FemaleAvatarIds.Contains(avatarOrHeadId);

        public static bool IsFemale(Sprite sprite) => sprite != null && IsFemale(sprite.name);

        /// <summary>
        /// Подбирает точное имя под аватар бота:
        /// - для друзей (Марат, Аслан, Али, Данил, Егор, Ельнур) ВСЕГДА возвращает имя этого друга;
        /// - для стартовых персонажей — случайное незанятое имя СТРОГО соответствующего пола.
        /// </summary>
        public static string NameForAvatar(Sprite sprite, ICollection<string> usedNames)
        {
            var personal = PersonalTitleOf(sprite) ?? (sprite != null ? PersonalTitleOfId(sprite.name) : null);
            return NameForIdInternal(personal, sprite != null && IsFemale(sprite), usedNames);
        }

        public static string NameForAvatar(string avatarOrHeadId, ICollection<string> usedNames)
        {
            var personal = PersonalTitleOfId(avatarOrHeadId);
            return NameForIdInternal(personal, IsFemale(avatarOrHeadId), usedNames);
        }

        private static string NameForIdInternal(string personalTitle, bool female, ICollection<string> usedNames)
        {
            if (!string.IsNullOrEmpty(personalTitle))
            {
                var candidate = personalTitle;
                for (var repeat = 2; usedNames != null && usedNames.Contains(candidate); repeat++)
                    candidate = personalTitle + " " + repeat;
                return candidate;
            }

            var pool = female ? FemaleBotNames : MaleBotNames;
            var available = new List<string>(pool.Length);
            for (var i = 0; i < pool.Length; i++)
            {
                if (usedNames == null || !usedNames.Contains(pool[i]))
                    available.Add(pool[i]);
            }

            if (available.Count > 0)
                return available[Random.Range(0, available.Count)];

            var fallback = pool[Random.Range(0, pool.Length)];
            var unique = fallback;
            for (var repeat = 2; usedNames != null && usedNames.Contains(unique); repeat++)
                unique = fallback + " " + repeat;
            return unique;
        }

        /// <summary>Преобразует avatarId (например "marat_01" или "avatar_05") в имя 3D-головы ("Head_Marat", "Head_Avatar05").</summary>
        public static string HeadIdForAvatar(string avatarId)
        {
            if (string.IsNullOrEmpty(avatarId)) return null;
            if (avatarId.StartsWith("Head_", System.StringComparison.OrdinalIgnoreCase)) return avatarId;
            for (var i = 0; i < KnownFriendHeads.Length; i++)
            {
                var entry = KnownFriendHeads[i];
                if (string.Equals(avatarId, entry.PersonId, System.StringComparison.OrdinalIgnoreCase)
                    || avatarId.StartsWith(entry.PersonId + "_", System.StringComparison.OrdinalIgnoreCase))
                    return entry.HeadId;
            }

            if (avatarId.StartsWith("avatar_", System.StringComparison.OrdinalIgnoreCase) && avatarId.Length >= 9)
                return "Head_Avatar" + avatarId.Substring(7);
            return null;
        }

        /// <summary>Возвращает имя 3D-головы только если её FBX уже реально добавлен в Resources/ThreeD/Characters/Heads/.</summary>
        public static string Available3DHeadForAvatar(string avatarId)
        {
            var headId = HeadIdForAvatar(avatarId);
            return !string.IsNullOrEmpty(headId) && Has3DHead(headId) ? headId : null;
        }

        public static bool Has3DHead(string headId)
        {
            if (string.IsNullOrEmpty(headId)) return false;
            if (HeadExistsCache.TryGetValue(headId, out var exists)) return exists;
            exists = Resources.Load<GameObject>("ThreeD/Characters/Heads/" + headId) != null;
            HeadExistsCache[headId] = exists;
            return exists;
        }

        /// <summary>Список всех друзей, для которых уже готовы 3D-головы (исключая выбранного самим игроком).</summary>
        public static List<(string AvatarId, string HeadId)> AvailableFriend3DHeads(string excludeAvatarId = null)
        {
            var excludeTitle = PersonalTitleOfId(excludeAvatarId);
            var list = new List<(string AvatarId, string HeadId)>();
            for (var i = 0; i < KnownFriendHeads.Length; i++)
            {
                var entry = KnownFriendHeads[i];
                if (!string.IsNullOrEmpty(excludeTitle)
                    && string.Equals(TitleFor(entry.PersonId), excludeTitle, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (Has3DHead(entry.HeadId))
                    list.Add((entry.AvatarId, entry.HeadId));
            }
            return list;
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
