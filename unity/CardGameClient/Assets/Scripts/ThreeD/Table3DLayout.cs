using System;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>Размеры стола и посадки в метрах: те же числа, что в scripts/3d/table_model.py и blockout_table.py.</summary>
    internal static class Table3DLayout
    {
        public const float TableA = .92f, TableB = .65f, TableY = .76f; // Полуоси овала и высота сукна.
        public const float SeatGap = .32f;                              // От края стола до середины сидящего.
        public const float EyeY = 1.30f, EyeZ = -1.01f;                 // Глаза: выше настоящего роста сидящего, иначе карты на столе плоские.
        public const float CardHeight = .088f, CardAspect = .714f;
        // Настоящего размера карта с телефона не читается: на столе она в 1,4 раза больше, в руке — в 1,35.
        public const float TableCardScale = 1.4f, HandCardScale = 1.35f;

        // Колода с козырем справа: левую половину стола закрывает веер в руке, там лежит бита.
        public static readonly Vector3 DeckAt = new Vector3(.66f, TableY, .3f);
        public static readonly Vector3 DiscardAt = new Vector3(-.66f, TableY, .3f);
        // Сторона веера: -1 — веер в левой руке, +1 — в правой (OwnArms3DView.FanSide). По ней отражаются числа веера ниже.
        private static readonly float FanX = OwnArms3DView.FanSide == "_R" ? 1f : -1f;
        // Пары сдвинуты от веера к другой стороне: крайнюю пару иначе задевал бы край веера.
        public static readonly float PairShift = -.06f * FanX;

        // Веер руки в осях тела (глаза при взгляде по умолчанию), как в Liar's Bar: в 32 см от глаз, на стороне руки с веером
        // (OwnArms3DView.FanSide, сейчас левая), поднят к середине экрана, кисть под ним видна целиком. Поза рук рассчитана под этот хват: при смене
        // этих чисел надо менять и scripts/3d/hands/in_game.py (там раскладка «веер слева», FAN_HAND её отражает).
        public static readonly Vector3 HandPivot = new Vector3(.154f * FanX, -.031f, .32f);
        // Карты вращаются вокруг точки в 6 мм над нижним краем, как в настоящем веере: там их прижимает большой палец.
        // Веер смотрит лицом на глаза: не наклонён к взгляду и повёрнут на 15° к середине. Шаг даёт видимую полоску около 2,5 см
        // у верхнего края; при многих картах веер раскрывается до 100°.
        public static readonly float HandYaw = 15f * FanX;
        public const float HandTilt = 0f, HandFanStep = 13.5f, HandFanLimit = 100f, HandFanRadius = .0530f, HandLift = .022f;

        /// <summary>Углы мест по овалу, 0° — своё место: все соперники на дальней части стола, в кадре без поворота головы.</summary>
        public static float[] SeatAngles(int opponents)
        {
            switch (opponents)
            {
                case 1: return new[] { 180f };
                case 2: return new[] { 140f, 220f };
                case 3: return new[] { 126f, 180f, 234f };
                case 4: return new[] { 118f, 160f, 200f, 242f };
                case 5: return new[] { 114f, 147f, 180f, 213f, 246f };
                default: throw new ArgumentOutOfRangeException(nameof(opponents), opponents, "Стол рассчитан на шестерых");
            }
        }

        public static Vector3 SeatPosition(float angle)
        {
            var t = angle * Mathf.Deg2Rad;
            return new Vector3((TableA + SeatGap) * Mathf.Sin(t), 0f, -(TableB + SeatGap) * Mathf.Cos(t));
        }

        /// <summary>Пары лежат одним рядом, с четвёртой — двумя: поперёк стола шесть пар упёрлись бы в колоду и биту.</summary>
        public static Vector3 PairPosition(int slot, int count)
        {
            const float stepX = .24f, stepZ = .26f;
            var rows = count > 3 ? 2 : 1;
            var column = slot % 3;
            var row = slot / 3;
            var inRow = Mathf.Min(3, rows == 1 ? count : row == 0 ? 3 : count - 3);
            var z = rows == 1 ? .03f : row == 0 ? .03f + stepZ / 2 : .03f - stepZ / 2;
            return new Vector3((column - (inRow - 1) / 2f) * stepX + PairShift, TableY + .002f, z);
        }

        /// <summary>Угол по вертикали: на 16:9 — 62°, на 20:9 — 52°. По горизонтали в обоих около 94°: шире кисти по углам вытягиваются.</summary>
        public static float FieldOfView(float aspect) => Mathf.Lerp(62f, 52f, Mathf.InverseLerp(1.78f, 2.22f, aspect));
    }
}
