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
            camera.backgroundColor = new Color(.03f, .02f, .016f);
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
            // Зал тёмный: всё, что не под лампой, держит только слабый рассеянный свет, поэтому у рук и карт видны свет и тень.
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.3f, .27f, .25f);
            RenderSettings.ambientEquatorColor = new Color(.2f, .16f, .13f);
            RenderSettings.ambientGroundColor = new Color(.07f, .05f, .04f);

            // Модели выгружаются с осью вперёд в -Z: разворот ставит верх стола гербом к игроку.
            var table = Table3DArt.Spawn("table", null);
            table.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            Table3DArt.Block(null, new Vector3(0f, -.05f, 0f), new Vector3(18f, .1f, 18f), Table3DArt.Lit("carpet", new Color(.2f, .04f, .05f), .05f));
            BuildTableSurface();
            BuildHallBackdrop();

            // Главный свет сверху из-за левого плеча: лица карт в руке освещены, пальцы лепятся светом сбоку,
            // а тени рук и карт ложатся на сукно и на карты перед ними. Тени считаются только в 4,5 м вокруг камеры (настройки URP),
            // поэтому тень пальца на карте видна.
            var key = new GameObject("Table lamp").AddComponent<Light>();
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(50f, 35f, 0f);
            key.color = new Color(1f, .92f, .82f);
            key.intensity = 1.6f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = .9f;

            // Тёплое пятно лампы на середине стола: края стола и зал темнее, взгляд собирается на картах.
            var pool = new GameObject("Lamp pool").AddComponent<Light>();
            pool.type = LightType.Point;
            pool.transform.position = new Vector3(0f, Table3DLayout.TableY + .95f, .1f);
            pool.range = 2.4f;
            pool.color = new Color(1f, .8f, .55f);
            pool.intensity = 1.3f;
            pool.shadows = LightShadows.None;
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

        /// <summary>Половина цилиндра перед игроком с картинкой клуба: камера не сдвигается с места, поэтому глубины хватает.</summary>
        private static void BuildHallBackdrop()
        {
            const int steps = 96;
            const float radius = 5.2f, height = 6.7f, bottom = -2.1f, centreZ = -.6f;
            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                var angle = Mathf.PI * (1f - t);
                for (var v = 0; v < 2; v++)
                {
                    vertices[i * 2 + v] = new Vector3(radius * Mathf.Cos(angle), bottom + v * height, centreZ + radius * Mathf.Sin(angle));
                    uvs[i * 2 + v] = new Vector2(t, v);
                }
                if (i == steps) continue;
                var at = i * 6;
                var quad = i * 2;
                triangles[at] = quad;
                triangles[at + 1] = quad + 1;
                triangles[at + 2] = quad + 3;
                triangles[at + 3] = quad;
                triangles[at + 4] = quad + 3;
                triangles[at + 5] = quad + 2;
            }
            var mesh = new Mesh { name = "Hall backdrop", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateBounds();
            var hall = new GameObject("Club hall backdrop", typeof(MeshFilter), typeof(MeshRenderer));
            hall.GetComponent<MeshFilter>().sharedMesh = mesh;
            hall.GetComponent<MeshRenderer>().sharedMaterial = Table3DArt.Unlit("club hall", new Color(.62f, .6f, .58f), Resources.Load<Sprite>("UI/Hub/club-hall").texture);
        }
    }
}
