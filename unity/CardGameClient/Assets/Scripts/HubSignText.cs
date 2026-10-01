using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Надпись вывески: по центру своего прямоугольника стоят сами буквы, а не строка с её запасом
    /// сверху и снизу (иначе заглавные съезжают с таблички), и цвет идёт сверху вниз — у золота
    /// верх светлее, как металл под фонарём. Ставится на Text раньше тени и обводки.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class HubSignText : BaseMeshEffect
    {
        public Color Top = Color.white, Bottom = Color.white;

        // Координаты безопасного поля вывески в локальном пространстве Text. У наклонной доски
        // четыре угла; у выпуклой вывески дополнительно приподнята середина верхнего и нижнего края.
        public bool FitToSurface;
        public Vector2 TopLeft, TopRight, BottomLeft, BottomRight;
        public float TopRise, BottomRise;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            var vertex = new UIVertex();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                min = Vector2.Min(min, vertex.position);
                max = Vector2.Max(max, vertex.position);
            }
            var shift = (Vector3)(graphic.rectTransform.rect.center - (min + max) / 2);
            var center = (min + max) / 2;
            var surfaceWidth = Vector2.Distance((TopLeft + BottomLeft) / 2, (TopRight + BottomRight) / 2);
            var surfaceHeight = Vector2.Distance((TopLeft + TopRight) / 2 + Vector2.up * TopRise,
                (BottomLeft + BottomRight) / 2 + Vector2.up * BottomRise);
            var project = FitToSurface && surfaceWidth > .001f && surfaceHeight > .001f;
            var fit = project ? Mathf.Min(surfaceWidth / Mathf.Max(.001f, max.x - min.x),
                surfaceHeight / Mathf.Max(.001f, max.y - min.y)) : 1;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref vertex, i);
                var tint = Color.Lerp(Bottom, Top, Mathf.InverseLerp(min.y, max.y, vertex.position.y));
                vertex.color = (Color)vertex.color * tint;
                if (project)
                {
                    // Подгоняем видимые буквы, а не межстрочный интервал шрифта. Пропорции слова
                    // сохраняются в центре вывески, края следуют её перспективе и изгибу.
                    var u = .5f + (vertex.position.x - center.x) * fit / surfaceWidth;
                    var v = .5f + (vertex.position.y - center.y) * fit / surfaceHeight;
                    var arch = 4 * u * (1 - u);
                    var top = Vector2.LerpUnclamped(TopLeft, TopRight, u) + Vector2.up * (TopRise * arch);
                    var bottom = Vector2.LerpUnclamped(BottomLeft, BottomRight, u) + Vector2.up * (BottomRise * arch);
                    var position = Vector2.LerpUnclamped(bottom, top, v);
                    vertex.position = new Vector3(position.x, position.y, vertex.position.z);
                }
                else vertex.position += shift;
                vh.SetUIVertex(vertex, i);
            }
        }
    }
}
