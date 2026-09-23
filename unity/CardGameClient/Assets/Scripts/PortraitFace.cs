using System;
using System.Collections.Generic;

namespace CardGameClient
{
    /// <summary>
    /// Где у портрета лицо, и как стереть с него всё, что выше лба. Портреты рисует
    /// нейросеть, и каждый лист она кадрирует по-своему: где-то виден длинный торс, где-то
    /// почти одна голова. Поэтому глаза за столом стоят на высоте от 82 до 124 пикселей,
    /// и общей «линии бровей» на все портреты нет. Ищем сами глаза: два белых пятна белков
    /// по бокам от переносицы.
    ///
    /// UnityEngine здесь нет нарочно: так поиск проверяется на всех картинках разом вне
    /// редактора, а в игре работает ровно тот же код. Размеры — в пикселях стола при
    /// масштабе 1, пиксели картинки — по четыре байта RGBA, строки снизу вверх, как их
    /// отдаёт текстура.
    /// </summary>
    public static class PortraitFace
    {
        // Глаза ищем только в этой полосе высот: ниже — воротники, выше — шляпы.
        private const float LowestEyes = 80f;
        private const float HighestEyes = 134f;

        // Белки лежат не дальше шестнадцати пикселей от середины лица. Шире не берём:
        // у парика по краю лица белые локоны, и они сходили за глаза.
        private const float SearchHalfWidth = 17.5f;

        private const float BridgeHalfWidth = 2.5f;
        private const float MaxEyeWidth = 8f;
        private const float SkinAround = 5f;
        private const float NoseBelow = 6f;
        private const float BandHalfHeight = 4f;

        /// <summary>
        /// Высота глаз над низом фигуры и сдвиг середины между ними от центра картинки.
        /// Глаз — белое пятно не шире восьми пикселей, рядом с ним кожа, между глазами —
        /// кожа переносицы, а под ними — кожа носа. Воротник с галстуком, белая фуражка и
        /// парик на глаза так не похожи: посередине у них ткань или волосы. Если белок
        /// нашёлся только с одной стороны (повязка пирата), высота верная, а середина нет —
        /// об этом говорит bothEyes.
        /// </summary>
        /// <param name="scale">Пикселей стола на пиксель картинки.</param>
        public static bool TryFindEyes(byte[] rgba, int width, int height, float scale,
            out float eyeHeight, out float faceOffset, out bool bothEyes)
        {
            eyeHeight = 0f;
            faceOffset = 0f;
            bothEyes = false;

            var cx = width / 2.0;
            var half = Math.Round(SearchHalfWidth / scale, MidpointRounding.ToEven);
            var x0 = (int)Math.Max(0, cx - half);
            var x1 = (int)Math.Min(width, cx + half);
            var count = x1 - x0;
            if (count <= 0 || height <= 0)
            {
                return false;
            }

            var bridge = BridgeHalfWidth / scale;
            var eyeMax = MaxEyeWidth / scale;
            var around = (int)Math.Round(SkinAround / scale, MidpointRounding.ToEven);
            var noseStep = NoseBelow / scale;

            var white = new bool[count];
            var skin = new bool[count];
            var pairs = new List<Candidate>();
            var singles = new List<Candidate>();

            // Строки сверху вниз: так «ниже» — это больше, как у картинки на экране.
            for (var top = 0; top < height; top++)
            {
                var row = height - 1 - top;
                var rise = (row + 0.5) * scale;
                if (rise < LowestEyes || rise > HighestEyes)
                {
                    continue;
                }

                var anyWhite = false;
                for (var i = 0; i < count; i++)
                {
                    var p = (row * width + x0 + i) * 4;
                    white[i] = IsWhite(rgba, p);
                    skin[i] = IsSkin(rgba, p);
                    anyWhite |= white[i];
                }

                if (!anyWhite || !BridgeIsSkin(skin, x0, cx, bridge))
                {
                    continue;
                }

                var noseTop = (int)Math.Round(top + noseStep, MidpointRounding.ToEven);
                if (noseTop >= height || !BridgeIsSkin(RowSkin(rgba, width, height - 1 - noseTop, x0, count), x0, cx, bridge))
                {
                    continue;
                }

                var left = default(Run);
                var right = default(Run);
                var hasLeft = false;
                var hasRight = false;

                var start = -1;
                for (var i = 0; i <= count; i++)
                {
                    var on = i < count && white[i];
                    if (on && start < 0)
                    {
                        start = i;
                    }
                    else if (!on && start >= 0)
                    {
                        var run = new Run(start, i - 1, x0);
                        start = -1;

                        if (run.Width > eyeMax)
                        {
                            continue;
                        }

                        var sideLeft = run.Mid < cx;
                        if (Math.Abs(run.Mid - cx) <= bridge || !SkinOutside(skin, run, sideLeft, around))
                        {
                            continue;
                        }

                        if (sideLeft)
                        {
                            if (!hasLeft || run.Beats(left))
                            {
                                left = run;
                            }

                            hasLeft = true;
                        }
                        else
                        {
                            if (!hasRight || run.Beats(right))
                            {
                                right = run;
                            }

                            hasRight = true;
                        }
                    }
                }

                if (hasLeft && hasRight)
                {
                    pairs.Add(new Candidate(top, left.Width + right.Width, (left.Mid + right.Mid) / 2.0, true));
                }
                else if (hasLeft || hasRight)
                {
                    singles.Add(new Candidate(top, hasLeft ? left.Width : right.Width, 0.0, false));
                }
            }

            var rows = pairs.Count >= 2 ? pairs : singles;
            if (rows.Count == 0)
            {
                return false;
            }

            // Самая плотная полоса строк: глаз — это несколько соседних строк подряд, а
            // случайное белое пятно — одна-две.
            var band = BandHalfHeight / scale;
            var best = rows[0].Top;
            var bestScore = -1.0;
            foreach (var candidate in rows)
            {
                var score = 0.0;
                foreach (var other in rows)
                {
                    if (Math.Abs(other.Top - candidate.Top) <= band)
                    {
                        score += other.Weight;
                    }
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate.Top;
                }
            }

            var weight = 0.0;
            var sum = 0.0;
            var midSum = 0.0;
            var midCount = 0;
            foreach (var candidate in rows)
            {
                if (Math.Abs(candidate.Top - best) > band)
                {
                    continue;
                }

                weight += candidate.Weight;
                sum += candidate.Top * candidate.Weight;
                if (candidate.HasMid)
                {
                    midSum += candidate.Mid;
                    midCount++;
                }
            }

            var eyeTop = sum / weight;
            var mid = midCount > 0 ? midSum / midCount : cx;

            eyeHeight = (float)((height - eyeTop - 0.5) * scale);
            faceOffset = (float)((mid - cx) * scale);
            bothEyes = pairs.Count >= 2;
            return true;
        }

        /// <summary>
        /// Стирает всё, что выше линии среза. Над лицом, до halfWidth от его середины,
        /// линия идёт на высоте crown; дальше опускается до side, плавно на ширине blend.
        /// Край мягкий, на soft пикселей — иначе на волосах вышла бы ступенька.
        /// </summary>
        public static void CutAbove(byte[] rgba, int width, int height, float scale,
            float centerX, float crown, float halfWidth, float side, float blend, float soft)
        {
            for (var row = 0; row < height; row++)
            {
                var rise = (row + 0.5f) * scale;
                if (rise <= Math.Min(crown, side) - soft)
                {
                    continue;
                }

                for (var x = 0; x < width; x++)
                {
                    var fromFace = Math.Abs((x + 0.5f - width * 0.5f) * scale - centerX);
                    var t = Clamp01((fromFace - (halfWidth - blend)) / blend);
                    t = t * t * (3f - 2f * t);
                    var line = crown + (side - crown) * t;
                    var keep = Clamp01((line - rise) / soft);
                    if (keep >= 1f)
                    {
                        continue;
                    }

                    var p = (row * width + x) * 4 + 3;
                    rgba[p] = (byte)Math.Round(rgba[p] * keep);
                }
            }
        }

        private static bool IsWhite(byte[] rgba, int p)
        {
            int r = rgba[p], g = rgba[p + 1], b = rgba[p + 2], a = rgba[p + 3];
            var hi = Math.Max(r, Math.Max(g, b));
            var lo = Math.Min(r, Math.Min(g, b));
            return a > 200 && lo > 170 && hi - lo < 50;
        }

        /// <summary>
        /// Кожа — тёплая, красный не меньше зелёного, зелёный не меньше синего. Золото
        /// кокарды и короны тоже тёплое, но синего в нём почти нет: без последней
        /// проверки кокарда посередине фуражки сходила за переносицу.
        /// </summary>
        private static bool IsSkin(byte[] rgba, int p)
        {
            int r = rgba[p], g = rgba[p + 1], b = rgba[p + 2], a = rgba[p + 3];
            return a > 200 && r > 80 && r >= g && g >= b - 8 && r - b > 22 && r - g < 90 && b * 100 > r * 38;
        }

        private static bool[] RowSkin(byte[] rgba, int width, int row, int x0, int count)
        {
            var skin = new bool[count];
            for (var i = 0; i < count; i++)
            {
                skin[i] = IsSkin(rgba, (row * width + x0 + i) * 4);
            }

            return skin;
        }

        private static bool BridgeIsSkin(bool[] skin, int x0, double cx, double bridge)
        {
            var total = 0;
            var hits = 0;
            for (var i = 0; i < skin.Length; i++)
            {
                if (Math.Abs(x0 + i - cx) <= bridge)
                {
                    total++;
                    if (skin[i])
                    {
                        hits++;
                    }
                }
            }

            return total == 0 || (double)hits / total >= 0.6;
        }

        private static bool SkinOutside(bool[] skin, Run run, bool sideLeft, int around)
        {
            for (var k = 1; k <= around; k++)
            {
                var i = sideLeft ? run.Start - k : run.End + k;
                if (i >= 0 && i < skin.Length && skin[i])
                {
                    return true;
                }
            }

            return false;
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private readonly struct Run
        {
            public Run(int start, int end, int x0)
            {
                Start = start;
                End = end;
                Width = end - start + 1;
                Mid = (x0 + start + x0 + end) / 2.0;
            }

            public int Start { get; }
            public int End { get; }
            public int Width { get; }
            public double Mid { get; }

            /// <summary>Шире — лучше; при равной ширине — правее, как max по паре в прототипе.</summary>
            public bool Beats(Run other) => Width > other.Width || (Width == other.Width && Mid > other.Mid);
        }

        private readonly struct Candidate
        {
            public Candidate(int top, int weight, double mid, bool hasMid)
            {
                Top = top;
                Weight = weight;
                Mid = mid;
                HasMid = hasMid;
            }

            public int Top { get; }
            public int Weight { get; }
            public double Mid { get; }
            public bool HasMid { get; }
        }
    }
}
