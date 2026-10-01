using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Name and avatar from the shared local profile service, used by every game.
    /// </summary>
    public static class PlayerProfile
    {
        public static string DisplayName => PlayerHubRuntime.DisplayName;

        /// <summary>Имя спрайта, а не индекс: список аватаров растёт, и индекс бы съезжал.</summary>
        public static string AvatarName
        {
            get => PlayerHubRuntime.AvatarName;
            set { _ = PlayerHubRuntime.SelectAvatarAsync(value); }
        }

        public static System.Threading.Tasks.Task<bool> SetAvatarAsync(string value)
            => PlayerHubRuntime.SelectAvatarAsync(value);

        /// <summary>
        /// Выбранный аватар. У каждого человека пока один образ, поэтому прежний выбор
        /// (например, yegor_07 из старых наборов) ведёт к образу того же человека; без выбора — Егор.
        /// </summary>
        public static Sprite Avatar
        {
            get
            {
                var name = AvatarName;
                var sprite = AvatarCatalog.Find(name);
                if (sprite != null) return sprite;
                var underscore = string.IsNullOrEmpty(name) ? -1 : name.LastIndexOf('_');
                return FirstOf(underscore > 0 ? name.Substring(0, underscore) : null) ?? FirstOf(DefaultPerson);
            }
        }

        private const string DefaultPerson = "yegor";

        private static Sprite FirstOf(string groupId)
        {
            if (string.IsNullOrEmpty(groupId)) return null;
            foreach (var group in AvatarCatalog.Groups)
                if (group.Id == groupId && group.Sprites.Count > 0)
                    return group.Sprites[0];
            return null;
        }
    }
}
