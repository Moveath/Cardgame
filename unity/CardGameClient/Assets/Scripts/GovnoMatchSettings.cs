namespace CardGameClient
{
    public struct GovnoMatchSettings
    {
        public int PlayerCount;
        public bool Chaos;
        public double PickSeconds => Chaos ? CardGame.Core.Games.Govno.GovnoGame.ChaosPickSeconds
            : CardGame.Core.Games.Govno.GovnoGame.PickSeconds;

        /// <summary>«2,5» и «1,5» — с запятой при любом языке системы.</summary>
        public string PickSecondsText => PickSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        public static GovnoMatchSettings Default => new GovnoMatchSettings { PlayerCount = 4, Chaos = false };
    }
}
