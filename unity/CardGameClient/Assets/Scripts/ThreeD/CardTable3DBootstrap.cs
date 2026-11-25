using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Собирает стол в метрах: настоящие стол и стулья из Blender, камера на месте игрока.
    /// Зал пока — картинка клуба на изогнутой стене, соперники — манекены до общего тела (Documentation/3D_PROTOTYPE_PROGRESS.md).
    /// </summary>
    public sealed class CardTable3DBootstrap : MonoBehaviour
    {
        private void Start()
        {
            BuildRoom();
            var eye = new GameObject("Seated first person camera");
            eye.transform.position = new Vector3(0f, Table3DLayout.EyeY, Table3DLayout.EyeZ);
            var camera = eye.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 30f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .024f, .022f);
            DressCamera(camera);
            eye.AddComponent<AudioListener>();
            var look = eye.AddComponent<TableCameraController>();
            look.Configure(camera);
            // Руки принадлежат телу на стуле, веер — левой кисти: голова поворачивается, а руки и карты остаются на месте.
            var arms = OwnArms3DView.Create(eye.transform);

            var hud = new GameObject("CardTable3D HUD").AddComponent<CardTable3DHud>();
            hud.Build();
            hud.BuildGestures(OwnArms3DView.Gestures, arms.PlayGesture);
            gameObject.AddComponent<CardGame3DPresenter>().Configure(camera, look, hud, transform, arms);
        }

        private static void BuildRoom()
        {
            // Временного 2D-задника нет: чистый глубокий тёплый полумрак за пределами стола до будущего 3D-интерьера.
            // До 3,0 м (весь стол, друзья и стулья) туман = 0%, дальше пол плавно растворяется без жёсткого горизонта.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.035f, .024f, .022f);
            RenderSettings.fogStartDistance = 3.0f;
            RenderSettings.fogEndDistance = 8.8f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.38f, .34f, .31f);
            RenderSettings.ambientEquatorColor = new Color(.31f, .26f, .23f);
            RenderSettings.ambientGroundColor = new Color(.18f, .14f, .12f);

            // Модели выгружаются с осью вперёд в -Z: разворот ставит верх стола гербом к игроку.
            // Увеличиваем стол в плане на 12% (2,60 × 1,61 м, высота сукна TableY = 0,76 м неизменна),
            // чтобы 6 друзей сидели просторнее, не плечом к плечу!
            var table = Table3DArt.Spawn("table", null);
            table.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            table.transform.localScale = new Vector3(1.12f, 1f, 1.12f);
            Table3DArt.Block(null, new Vector3(0f, -.05f, 0f), new Vector3(22f, .1f, 22f), Table3DArt.Lit("carpet", new Color(.14f, .035f, .042f), .05f));
            BuildTableSurface();

            // Главный свет сверху из-за левого плеча: лица карт в руке освещены, пальцы лепятся светом сбоку,
            // а мягкая тень (0.56 вместо 0.90) даёт глубину на сукне, не превращая кисти бокового соседа слева в тёмно-смуглые!
            var key = new GameObject("Table lamp").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(52f, 32f, 0f);
            key.color = new Color(1f, .93f, .85f);
            key.intensity = 1.48f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .56f;

            // Тёплое пятно лампы над серединой стола: увеличенный радиус 4,6 м ровно освещает всех 5 друзей по кругу (включая крайних соседей).
            var pool = new GameObject("Lamp pool").AddComponent<Light>();
            pool.type = LightType.Point;
            pool.transform.position = new Vector3(0f, Table3DLayout.TableY + 1.02f, .05f);
            pool.range = 4.6f;
            pool.color = new Color(1f, .88f, .74f);
            pool.intensity = 1.50f;
            pool.shadows = LightShadows.None;

            // Мягкий отражённый свет от стола на лица и руки боковых соседей (без теней): убирает «смуглый» провал в тени у левого соседа.
            var bounce = new GameObject("Table bounce fill").AddComponent<Light>();
            bounce.type = LightType.Point;
            bounce.transform.position = new Vector3(-.25f, Table3DLayout.TableY + .95f, -.35f);
            bounce.range = 4.8f;
            bounce.color = new Color(1f, .94f, .88f);
            bounce.intensity = .62f;
            bounce.shadows = LightShadows.None;
        }

        /// <summary>Постобработка из профиля Resources/ThreeD/TableLook и сглаживание краёв: пальцы и рёбра карт без лесенки.</summary>
        private static void DressCamera(Camera camera)
        {
            var mobile = Application.isMobilePlatform;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = mobile ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            var look = new GameObject("Table look").AddComponent<Volume>();
            look.isGlobal = true;
            look.sharedProfile = Resources.Load<VolumeProfile>("ThreeD/TableLook");
            // Свечение ярких бликов на телефоне стоит дороже, чем даёт.
            if (mobile && look.sharedProfile != null && look.profile.TryGet<Bloom>(out var bloom)) bloom.active = false;
        }

        /// <summary>Сукно для падающих карт: толстая плита под ним, чтобы быстрая карта не проскочила. Края овала не нужны — карты падают в середину.</summary>
        private static void BuildTableSurface()
        {
            const float depth = .04f;
            var surface = new GameObject("Table surface").AddComponent<BoxCollider>();
            surface.transform.position = new Vector3(0f, Table3DLayout.TableY - depth / 2, 0f);
            surface.size = new Vector3(Table3DLayout.TableA * 2, depth, Table3DLayout.TableB * 2);
            surface.contactOffset = .0008f;
            surface.sharedMaterial = new PhysicsMaterial("cloth") { dynamicFriction = .55f, staticFriction = .65f, bounciness = 0f, bounceCombine = PhysicsMaterialCombine.Minimum };
        }
    }
}
