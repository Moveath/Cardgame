using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        /// <summary>
        /// Миниатюра района содержит ту же картинку и надписи на нарисованных вывесках улицы. Масштабируется и
        /// обрезается весь слой вместе: буквы сохраняют перспективу досок, а пустые щиты не появляются.
        /// Окно вызывающего кода с Mask обрезает и фон, и надписи по общему скруглённому контуру.
        /// </summary>
        private void AddMapPreview(RectTransform window, MapDistrict district, float width, float height)
        {
            var sprite = Resources.Load<Sprite>(district.Art);
            if (sprite == null || width <= 0 || height <= 0) return;

            // Геометрия совпадает с CityPage: высота района 941, ширина следует исходной картинке.
            // Сверху убираем часть неба, сохраняя нижнюю часть площади, как в прежнем MapCrop.
            var pictureWidth = sprite.rect.width * CityHeight / sprite.rect.height;
            var cropHeight = CityHeight * .84f;
            var cropWidth = Mathf.Min(pictureWidth, cropHeight * width / height);
            var focus = .5f;
            var biggest = -1f;
            foreach (var index in district.Places)
            {
                var place = CityPlaces[index];
                if (place.Area <= biggest) continue;
                biggest = place.Area;
                focus = place.Door.x / PictureSize(place.Art).x;
            }

            var left = Mathf.Clamp(focus * pictureWidth - cropWidth / 2, 0, pictureWidth - cropWidth);
            var top = CityHeight - cropHeight;
            var scale = new Vector2(width / cropWidth, height / cropHeight);
            var page = MakeRect(window, "District art", -left * scale.x, -top * scale.y,
                pictureWidth, CityHeight);
            page.localScale = new Vector3(scale.x, scale.y, 1);
            var image = page.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;

            // Только надписи на досках: добавленные поверх города плашки здесь не нужны.
            // Превью не регистрирует столы и не меняет их анимацию в зале.
            AddSigns(page, sprite.name, pictureWidth, registerTableSigns: false, paintedOnly: true);
        }
    }
}
