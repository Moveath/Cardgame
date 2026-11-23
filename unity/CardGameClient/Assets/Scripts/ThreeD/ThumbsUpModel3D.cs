using UnityEngine;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// «Класс» автора — статичная 3D-модель (2026-11-23): Tripo3D по картинке Gemini, одна сетка без скелета со своей
    /// текстурой кожи и вязаной манжеты (Resources/ThreeD/Hands/ThumbsUpModel, исходник — references/hands/жесты/класс-модель).
    /// Висит на камере и двигается только целиком — localPosition и localRotation корня: Place(0) — под нижним краем кадра,
    /// Place(1) — в правом нижнем углу. Глаз видит её так же, как камера модели на картинке автора: луч от глаза к модели —
    /// ось взгляда картинки, верх кадра — её верх. Плоский срез рукава модели всегда за нижним краем кадра.
    /// </summary>
    internal sealed class ThumbsUpModel3D : MonoBehaviour
    {
        private const string ModelPath = "ThreeD/Hands/ThumbsUpModel/Hand_Right_ThumbsUp";
        private const string TexturePath = "ThreeD/Hands/ThumbsUpModel/Hand_Right_ThumbsUp_basecolor";
        // Модель (кисть с манжетой) — FrameShare высоты кадра, её середина — на CornerX ширины кадра. По высоте её ставит
        // срез рукава: на картинке модели его верх на CuffEndTop её высоты ниже середины, он уходит на CuffBelow под кадр.
        // При кадре 1822×820 модель видна как кисть в натуральную величину (17 см, scripts/3d/hands/thumbs_model.py) в 29 см
        // от глаз, большой палец доходит почти до середины кадра.
        private const float FrameShare = .6f, CornerX = .76f, CuffEndTop = .3f, CuffBelow = .02f;
        // Сама модель в Distance от глаза (размер подогнан под кадр): так близко, что при наклоне к столу и приближении
        // она не входит в сукно и борт.
        private const float Distance = .3f;
        // Откуда выезжает: ниже на долю высоты кадра и с наклоном верхом от себя, как поднимаемая из-под стола рука.
        private const float Below = .75f, Tilt = 30f;

        private Camera _camera;
        private Transform _model;
        private Vector3 _center;
        private float _height;

        public static ThumbsUpModel3D Create(Camera camera, uint renderingLayers)
        {
            var root = new GameObject("Thumbs up model").AddComponent<ThumbsUpModel3D>();
            root._camera = camera;
            root.transform.SetParent(camera.transform, false);
            var model = Instantiate(Resources.Load<GameObject>(ModelPath), root.transform, false);
            model.name = "Hand_Right_ThumbsUp";
            var bounds = model.GetComponentInChildren<MeshFilter>().sharedMesh.bounds;
            root._model = model.transform;
            root._center = bounds.center;
            root._height = bounds.size.y;
            var renderer = model.GetComponentInChildren<MeshRenderer>();
            // Шерсть и кожа почти без бликов: на текстуре Tripo свет уже нарисован.
            renderer.sharedMaterial = Table3DArt.Lit("thumbs up model", Color.white, .15f, 0f, Resources.Load<Texture2D>(TexturePath));
            renderer.renderingLayerMask |= renderingLayers;
            root.gameObject.SetActive(false);
            return root;
        }

        /// <summary>Модель в пути: 0 — под нижним краем кадра, 1 — в правом нижнем углу; между ними плавно.</summary>
        public void Place(float k)
        {
            var tan = Mathf.Tan(_camera.fieldOfView * .5f * Mathf.Deg2Rad);
            // Размер — от кадра: при другом соотношении сторон (другом угле обзора) модель занимает ту же долю высоты.
            var scale = FrameShare * 2f * Distance * tan / _height;
            _model.localScale = Vector3.one * scale;
            _model.localPosition = -_center * scale;
            var ray = _camera.ViewportPointToRay(new Vector3(CornerX, FrameShare * CuffEndTop - CuffBelow, 0f));
            var along = _camera.transform.InverseTransformDirection(ray.direction).normalized;
            var position = along * Distance;
            // Модель смотрит на глаз своей лицевой стороной (+Z файла), верх модели (+Y) — к верху кадра.
            var rotation = Quaternion.LookRotation(along, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
            var e = Mathf.SmoothStep(0f, 1f, k);
            var below = position + Vector3.down * (2f * Distance * tan * Below);
            transform.localPosition = Vector3.Lerp(below, position, e);
            transform.localRotation = Quaternion.Slerp(Quaternion.AngleAxis(Tilt, Vector3.right) * rotation, rotation, e);
        }

        /// <summary>Сетка модели: для проверки.</summary>
        public Renderer Renderer => _model.GetComponentInChildren<Renderer>();
    }
}
