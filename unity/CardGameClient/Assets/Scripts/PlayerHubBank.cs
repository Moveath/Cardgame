namespace CardGameClient
{
    /// <summary>
    /// Банк Тузяры изнутри (2026-10-08) — третье здание после Клуба и Дома Шестёрки: этаж <see cref="BankLevel"/>,
    /// картинка `UI/Hub/bank-hall` (2196×941, 21:9 — на широком экране зал виден по высоте целиком, не приближен;
    /// исходник — `screens/references/city/bank-hall-v5-wide.png`: зал `bank-hall-v4.png` (Тузяра поправлен отдельно,
    /// `scripts/bank/boss_patch.py`), дорисованный по 262 px с боков, `scripts/six-house/widen_room.py bank`). Мраморный зал, полуоткрытый
    /// сейф с горой тун-тун коинов, Тузяра за столом, по бокам — вальты бубен, его коллекторы. Вход — нырок в дверь
    /// банка в районе Тузяры (и с карты), выход — «Выйти», «Назад», Esc или S. Тузяра за столом нажимается и
    /// открывает стол крупным планом и договор (PlayerHubBankLoan.cs); табличка долга ведёт в прежний кабинет для погашения.
    /// </summary>
    public sealed partial class PlayerHubController
    {
        private const string BankHall = "bank-hall";
        private const int BankLevel = 4;

        private void GoToBankDesk()
        {
            if (_busy) return;
            GoHome();
            _mapTripDistrict = -1;
            _mapTripPlace = System.Array.FindIndex(CityPlaces, place => place.Id == "bank-boss");
            ContinueMapTrip();
        }
    }
}
