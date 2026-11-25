using System;
using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>Размеры стола и посадки в метрах: те же числа, что в scripts/3d/table_model.py и blockout_table.py.</summary>
    internal static class Table3DLayout
    {
        // Просторный VIP-стол 2,60 × 1,61 м (1,12x в плане при неизменной высоте 0,76 м):
        // друзья сидят заметно свободнее друг от друга, без тесноты и скученности!
        public const float TableA = 1.30f, TableB = .806f, TableY = .76f;
        public const float TableStraight = TableA - TableB;             // Половина прямой части стола (0,494 м).
        public const float SeatGap = .35f;                              // От края стола до середины сидящего.
        // Золотая середина посадки: тело привязано к краю стола (-1,166 м -> отдыхающая кисть точно на сукне),
        // а глаза на естественной высоте сидящего человека (EyeY = 1,42, EyeZ = -1,25) — ни ощущения «великана» (1,50), ни плоского взгляда (1,37)!
        public const float SeatBodyY = 1.30f, SeatBodyZ = -1.166f, SeatBodyPitch = 32f;
        public const float EyeY = 1.42f, EyeZ = -1.25f;
        public const float CardHeight = .088f, CardAspect = .714f;
        // Сбалансированный масштаб карт под золотой FOV (62°/57°): карты в центре крупные, а веер (1,00x) чёткий и не закрывает соседа.
        public const float TableCardScale = 2.75f, TableCrowdedCardScale = 2.48f, DeckCardScale = 2.02f, DiscardCardScale = 1.65f, HandCardScale = 1.00f;

        // Колода с козырем справа и бита слева — сдвинуты пропорционально расширенному столу 2,60 м.
        public static readonly Vector3 DeckAt = new Vector3(.96f, TableY, -.03f);
        public static readonly Vector3 DiscardAt = new Vector3(-.84f, TableY, .14f);
        // Сторона веера: -1 — веер в левой руке, +1 — в правой (OwnArms3DView.FanSide). По ней отражаются числа веера ниже.
        private static readonly float FanX = OwnArms3DView.FanSide == "_R" ? 1f : -1f;
        // Пары сдвинуты от веера к свободной середине стола, чтобы даже крайняя левая из 6 крупных пар не задевала веер.
        public static readonly float PairShift = -.105f * FanX;

        // Веер руки в осях тела (глаза при взгляде по умолчанию), как в Liar's Bar: в 32 см от глаз, на стороне руки с веером
        // (OwnArms3DView.FanSide, сейчас левая), поднят к середине экрана, кисть под ним видна целиком. Поза рук рассчитана под этот хват: при смене
        // этих чисел надо менять и scripts/3d/hands/in_game.py (там раскладка «веер слева», FAN_HAND её отражает).
        public static readonly Vector3 HandPivot = new Vector3(.154f * FanX, -.031f, .32f);
        // Карты вращаются вокруг точки в 6 мм над нижним краем, как в настоящем веере: там их прижимает большой палец.
        // Веер смотрит лицом на глаза: не наклонён к взгляду и повёрнут на 15° к середине. Шаг даёт видимую полоску около 2,5 см
        // у верхнего края; при многих картах веер раскрывается до 100°.
        // HandFanRadius = CardHeight * HandCardScale / 2 - 0.0064: нижний край карт всегда лежит ровно под большим пальцем.
        // Расширенный предел веера (122°) сохраняет широкую полоску номинала и масти даже при 12–14 картах в руке.
        public static readonly float HandYaw = 15f * FanX;
        public const float HandTilt = 0f, HandFanStep = 14.2f, HandFanLimit = 122f, HandFanRadius = .0411f, HandLift = .018f;

        /// <summary>
        /// Строго равномерная рассадка по всему периметру стадиона (0° — своё место в центре ближней стороны):
        /// между любыми двумя соседними игроками (включая тебя!) одинаковый шаг по периметру стола (~1,05–1,35 м при 6 игроках).
        /// </summary>
        public static float[] SeatAngles(int opponents)
        {
            switch (opponents)
            {
                case 1: return new[] { 180f };
                case 2: return new[] { 120f, 240f };
                case 3: return new[] { 90f, 180f, 270f };
                case 4: return new[] { 72f, 144f, 216f, 288f };
                case 5: return new[] { 60f, 120f, 180f, 240f, 300f };
                default: throw new ArgumentOutOfRangeException(nameof(opponents), opponents, "Стол рассчитан на шестерых");
            }
        }

        /// <summary>Точка на внешнем контуре стадиона и её внешняя нормаль по углу периметра (0° = (0, -B), 90° = (+A, 0), 180° = (0, +B)).</summary>
        private static void StadiumEdge(float angleDeg, out Vector3 edge, out Vector3 outward)
        {
            const float l = TableStraight;
            const float b = TableB;
            const float perim = 4f * l + 2f * Mathf.PI * b;
            var deg = ((angleDeg % 360f) + 360f) % 360f;
            var s = (deg / 360f) * perim;
            if (s <= l)
            {
                edge = new Vector3(s, 0f, -b);
                outward = Vector3.back;
                return;
            }
            s -= l;
            if (s <= Mathf.PI * b)
            {
                var th = -.5f * Mathf.PI + s / b;
                var nx = Mathf.Cos(th);
                var nz = Mathf.Sin(th);
                edge = new Vector3(l + b * nx, 0f, b * nz);
                outward = new Vector3(nx, 0f, nz);
                return;
            }
            s -= Mathf.PI * b;
            if (s <= 2f * l)
            {
                edge = new Vector3(l - s, 0f, b);
                outward = Vector3.forward;
                return;
            }
            s -= 2f * l;
            if (s <= Mathf.PI * b)
            {
                var th = .5f * Mathf.PI + s / b;
                var nx = Mathf.Cos(th);
                var nz = Mathf.Sin(th);
                edge = new Vector3(-l + b * nx, 0f, b * nz);
                outward = new Vector3(nx, 0f, nz);
                return;
            }
            s -= Mathf.PI * b;
            edge = new Vector3(-l + s, 0f, -b);
            outward = Vector3.back;
        }

        public static Vector3 SeatPosition(float angle)
        {
            StadiumEdge(angle, out var edge, out var outward);
            return edge + outward * SeatGap;
        }

        /// <summary>Поворот стула и соперника: 60 % по нормали к борту + 40 % к центру карт, чтобы стулья естественно стояли вдоль прямых и торцов.</summary>
        public static Quaternion SeatRotation(float angle)
        {
            StadiumEdge(angle, out var edge, out var outward);
            var seat = edge + outward * SeatGap;
            var toCenter = new Vector3(-seat.x, 0f, -seat.z).normalized;
            var fwd = (-outward * .6f + toCenter * .4f).normalized;
            return Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>Стакан игрока стоит на ровном сукне справа от него, не залезая на золотой обод, строчку, колоду, козырь, биту и кисть руки.</summary>
        public static Vector3 GlassPosition(float seatAngle)
        {
            // Внутренний стадион чистого сукна: отступ 19,5 см от внешнего края (8 см борт + 2,5 см строчка + 5 см поля + 4 см радиус дна),
            // чтобы стаканы соперников стояли уверенно на сукне, не касаясь золотой строчки.
            const float feltA = TableA - .195f, feltB = TableB - .195f;
            // Зазор 0,8 мм над плоскостью стола убирает Z-fighting плоского дна стакана с сукном.
            const float glassY = TableY + .0008f;
            if (Mathf.Abs(seatAngle) < 1f)
            {
                // Свой стакан — на ровной прямой стороне справа от отдыхающей правой кисти, сдвинут синхронно с краем стола.
                return new Vector3(.54f, glassY, -.55f);
            }

            var seat = SeatPosition(seatAngle);
            var forward = SeatRotation(seatAngle) * Vector3.forward;
            var right = Vector3.Cross(Vector3.up, forward);
            var at = seat + right * .23f + forward * .56f;
            at = ClampToFelt(at, feltA, feltB);

            // Обходим колоду, выглядывающий козырь и стопку биты, оставаясь на сукне справа от игрока.
            var obstacles = new[]
            {
                (new Vector2(DeckAt.x, DeckAt.z), .175f),
                (new Vector2(DeckAt.x - .118f, DeckAt.z - .012f), .165f),
                (new Vector2(DiscardAt.x, DiscardAt.z), .185f)
            };
            for (var step = 0; step < 12; step++)
            {
                var moved = false;
                for (var i = 0; i < obstacles.Length; i++)
                {
                    var delta = new Vector2(at.x, at.z) - obstacles[i].Item1;
                    var dist = delta.magnitude;
                    if (dist >= obstacles[i].Item2) continue;
                    var dir = dist > 1e-4f ? delta / dist : Vector2.up;
                    var push = obstacles[i].Item2 - dist + .005f;
                    at.x += dir.x * push;
                    at.z += dir.y * push;
                    at = ClampToFelt(at, feltA, feltB);
                    moved = true;
                }
                if (!moved) break;
            }
            return new Vector3(at.x, glassY, at.z);
        }

        /// <summary>Поворот стакана: гравировка трефы (+Z модели) смотрит к играющему / к середине стола.</summary>
        public static Quaternion GlassRotation(float seatAngle, Vector3 at)
        {
            var target = Mathf.Abs(seatAngle) < 1f ? new Vector3(0f, TableY, EyeZ) : new Vector3(0f, TableY, -.45f);
            var look = new Vector3(target.x - at.x, 0f, target.z - at.z);
            return look.sqrMagnitude > 1e-5f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity;
        }

        /// <summary>Ограничение точки внутренним стадионом сукна (прямая середина ±(a - b) + полукруги радиуса b).</summary>
        private static Vector3 ClampToFelt(Vector3 p, float a, float b)
        {
            var straight = Mathf.Max(0f, a - b);
            var xc = Mathf.Clamp(p.x, -straight, straight);
            var dx = p.x - xc;
            var dz = p.z;
            var r = Mathf.Sqrt(dx * dx + dz * dz);
            if (r <= b) return p;
            var s = b / r;
            return new Vector3(xc + dx * s, p.y, dz * s);
        }

        /// <summary>Размер карт в центре: до 3 пар — крупный (2,75x), при 4–6 парах — 2,48x, чтобы все 6 пар легли свободно и чётко читались.</summary>
        public static float TablePairScale(int count) => count > 3 ? TableCrowdedCardScale : TableCardScale;

        /// <summary>Пары лежат одним рядом ближе к игроку, с четвёртой — двумя: все карты крупные, чёткие и не задевают веер, руку, колоду и стаканы.</summary>
        public static Vector3 PairPosition(int slot, int count)
        {
            var rows = count > 3 ? 2 : 1;
            var stepX = rows == 1 ? .372f : .340f;
            const float stepZ = .335f;
            var column = slot % 3;
            var row = slot / 3;
            var inRow = Mathf.Min(3, rows == 1 ? count : row == 0 ? 3 : count - 3);
            var z = rows == 1 ? -.05f : row == 0 ? -.015f + stepZ / 2 : -.015f - stepZ / 2;
            return new Vector3((column - (inRow - 1) / 2f) * stepX + PairShift, TableY + .002f, z);
        }

        /// <summary>Золотая середина угла обзора (≈94° по горизонтали на 16:9 / ≈103° на 21:9+): без «острого туннеля» (50°) и без «великана» (70°)!</summary>
        public static float FieldOfView(float aspect) => Mathf.Lerp(62f, 57f, Mathf.InverseLerp(1.78f, 2.35f, aspect));
    }
}
