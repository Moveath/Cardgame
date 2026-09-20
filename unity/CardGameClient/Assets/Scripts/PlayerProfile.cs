using UnityEngine;

namespace CardGameClient
{
    /// <summary>
    /// Что игрок выбрал про себя. Пока это только аватар, позже сюда же переедет имя,
    /// и всё это будет храниться в аккаунте, а не в настройках устройства.
    /// </summary>
    public static class PlayerProfile
    {
        private const string AvatarKey = "player.avatar";

        /// <summary>Имя спрайта, а не индекс: список аватаров растёт, и индекс бы съезжал.</summary>
        public static string AvatarName
        {
            get => PlayerPrefs.GetString(AvatarKey, string.Empty);
            set
            {
                PlayerPrefs.SetString(AvatarKey, value ?? string.Empty);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Выбранный аватар или null, если игрок ещё не выбирал (тогда дадим случайный).</summary>
        public static Sprite Avatar => AvatarCatalog.Find(AvatarName);
    }
}
