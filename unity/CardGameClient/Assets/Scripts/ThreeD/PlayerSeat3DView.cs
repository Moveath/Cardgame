using CardGame.Core.Cards;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>Место соперника: настоящий стул и манекен-заглушка, пока нет общего тела и голов.</summary>
    public sealed class PlayerSeat3DView : MonoBehaviour
    {
        // Веер всегда одного размера: сколько карт у соперника, не видно (автор, 2026-11-05).
        private const int FanCards = 5;
        private static readonly Color[] Skin =
        {
            new Color(.82f, .62f, .48f), new Color(.74f, .54f, .41f), new Color(.86f, .68f, .56f),
            new Color(.66f, .48f, .36f), new Color(.8f, .6f, .5f)
        };
        private static readonly Color[] Hair =
        {
            new Color(.07f, .05f, .04f), new Color(.12f, .08f, .06f), new Color(.05f, .05f, .05f),
            new Color(.22f, .15f, .09f), new Color(.09f, .07f, .06f)
        };

        private GameObject _fan;

        public int PlayerIndex { get; private set; }
        public Transform HandAnchor { get; private set; }
        // Плашка на груди: над головой она упиралась бы в табличку хода, а лицо закрывать нельзя.
        public Vector3 LabelPoint => transform.position + Vector3.up * 1.03f;

        public static PlayerSeat3DView Create(Transform parent, int playerIndex, float angle)
        {
            var at = Table3DLayout.SeatPosition(angle);
            var root = new GameObject("Seat " + playerIndex);
            root.transform.SetParent(parent, false);
            // Вперёд — к середине стола.
            root.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-at, Vector3.up));
            var seat = root.AddComponent<PlayerSeat3DView>();
            seat.PlayerIndex = playerIndex;
            seat.BuildFigure(playerIndex % Skin.Length);
            return seat;
        }

        public void SetHolding(bool holding) => _fan.SetActive(holding);

        private void BuildFigure(int look)
        {
            var chair = Table3DArt.Spawn("chair", transform);
            chair.transform.localPosition = new Vector3(0f, 0f, -.04f);
            chair.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var sweater = Table3DArt.Sweater;
            var skin = Table3DArt.Lit("skin " + look, Skin[look], .25f);
            var hair = Table3DArt.Lit("hair " + look, Hair[look], .15f);
            var root = transform;
            Table3DArt.Ball(root, new Vector3(0f, .9f, 0f), new Vector3(.4f, .56f, .26f), sweater);
            Table3DArt.Limb(root, new Vector3(0f, 1.08f, 0f), new Vector3(0f, 1.16f, 0f), .05f, skin);
            Table3DArt.Ball(root, new Vector3(0f, 1.24f, 0f), new Vector3(.19f, .236f, .21f), skin);
            Table3DArt.Ball(root, new Vector3(0f, 1.27f, -.018f), new Vector3(.2f, .21f, .208f), hair);
            var hands = new Vector3(0f, .9f, .3f);
            for (var side = -1; side <= 1; side += 2)
            {
                var shoulder = new Vector3(.2f * side, 1.03f, 0f);
                var elbow = new Vector3(.25f * side, .84f, .1f);
                var wrist = hands + new Vector3(.06f * side, 0f, -.03f);
                Table3DArt.Limb(root, shoulder, elbow, .05f, sweater);
                Table3DArt.Limb(root, elbow, wrist, .042f, sweater);
                Table3DArt.Ball(root, wrist + new Vector3(0f, 0f, .03f), new Vector3(.084f, .1f, .084f), skin);
            }

            var anchor = new GameObject("Opponent hand anchor").transform;
            anchor.SetParent(transform, false);
            anchor.localPosition = hands + new Vector3(0f, .02f, .03f);
            HandAnchor = anchor;
            _fan = new GameObject("Opponent fan");
            _fan.transform.SetParent(anchor, false);
            for (var i = 0; i < FanCards; i++)
            {
                var card = Card3DView.Create(_fan.transform, new Card(Rank.Nine, Suit.Clubs), false, false);
                var spin = Quaternion.Euler(0f, 0f, (FanCards - 1) / 2f * 9f - i * 9f);
                card.Place(_fan.transform, spin * new Vector3(0f, .05f, 0f) + new Vector3(0f, 0f, i * .001f), spin, 1.1f);
            }
        }
    }
}
