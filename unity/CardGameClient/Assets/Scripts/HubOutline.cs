using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>
    /// Обводка картинки одной копией вместо четырёх (2026-10-10, оптимизация для телефона). Обычный Outline рисует
    /// плашку пять раз — четыре копии со сдвигом по диагоналям и её саму; на телефоне обводки плашек, кнопок и карточек
    /// стоили видеокарте 13 мс кадра (окно друзей: 57 кадров, без них — 60). Здесь позади — одна копия, раздвинутая
    /// на ту же величину во все стороны, того же цвета: край той же толщины, рисуется в 2,5 раза меньше. Текст
    /// обводится как прежде — его площадь мала, а буквы раздвигать нельзя.
    /// </summary>
    public sealed class HubOutline : Outline
    {
        private static readonly List<UIVertex> Vertices = new List<UIVertex>();

        public override void ModifyMesh(VertexHelper mesh)
        {
            if (!IsActive()) return;
            if (graphic is Text) { base.ModifyMesh(mesh); return; }
            Vertices.Clear();
            mesh.GetUIVertexStream(Vertices);
            var count = Vertices.Count;
            if (count == 0) return;
            Vector3 min = Vertices[0].position, max = min;
            for (var i = 1; i < count; i++)
            {
                min = Vector3.Min(min, Vertices[i].position);
                max = Vector3.Max(max, Vertices[i].position);
            }
            var size = max - min;
            var center = (min + max) / 2;
            var grow = new Vector3(size.x > 0 ? (size.x + 2 * Mathf.Abs(effectDistance.x)) / size.x : 1,
                size.y > 0 ? (size.y + 2 * Mathf.Abs(effectDistance.y)) / size.y : 1, 1);
            Color32 rim = effectColor;
            for (var i = 0; i < count; i++)
            {
                var vertex = Vertices[i];
                vertex.position = center + Vector3.Scale(vertex.position - center, grow);
                var color = rim;
                if (useGraphicAlpha) color.a = (byte)(color.a * Vertices[i].color.a / 255);
                vertex.color = color;
                Vertices.Add(vertex);
            }
            // Копия — первой, под самой картинкой.
            for (var i = 0; i < count; i++)
            {
                var original = Vertices[i];
                Vertices[i] = Vertices[count + i];
                Vertices[count + i] = original;
            }
            mesh.Clear();
            mesh.AddUIVertexTriangleStream(Vertices);
        }
    }
}
