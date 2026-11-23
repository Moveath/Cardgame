using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CardGameClient.ThreeD
{
    /// <summary>
    /// Материалы и заготовки 3D-стола. Модели лежат в Resources/ThreeD, их выгружает scripts/3d/export_table.py.
    /// Материалы делаются копией шаблонов из Resources/ThreeD/Materials: по ним сборка для телефона знает, какие шейдеры оставить.
    /// </summary>
    internal static class Table3DArt
    {
        private const string Templates = "ThreeD/Materials/";
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        /// <summary>Шаблон из Resources/ThreeD/Materials, их собирает scripts/3d/Table3DMaterialsBuilder.cs.</summary>
        public static Material Template(string name)
        {
            var template = Resources.Load<Material>(Templates + name);
            if (template == null) throw new InvalidOperationException("Нет шаблона материала " + Templates + name);
            return template;
        }

        public static Material Unlit(string key, Color color, Texture map = null)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(Template("Unlit")) { name = key, color = color };
            material.SetColor("_BaseColor", color);
            if (map != null) material.SetTexture("_BaseMap", map);
            Cache[key] = material;
            return material;
        }

        public static Material Lit(string key, Color color, float smoothness = .2f, float metallic = 0f, Texture map = null)
        {
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var material = new Material(Template("Lit")) { name = key, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (map != null) material.SetTexture("_BaseMap", map);
            Cache[key] = material;
            return material;
        }

        public static Material Sweater => Lit("sweater", new Color(.09f, .24f, .28f), 0f); // Шерсть не блестит: полностью матовая.

        /// <summary>Модель из Resources/ThreeD с материалами проекта: те, что приехали в FBX, — только имена слотов.</summary>
        public static GameObject Spawn(string model, Transform parent)
        {
            var go = Object.Instantiate(Resources.Load<GameObject>("ThreeD/" + model), parent, false);
            go.name = model;
            foreach (var renderer in go.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++) materials[i] = Slot(materials[i].name);
                renderer.sharedMaterials = materials;
            }
            return go;
        }

        private static Material Slot(string name)
        {
            if (name.Contains("gold")) return Lit("gold", new Color(.95f, .66f, .24f), .62f, .35f);
            if (name.Contains("wood")) return Lit("wood", new Color(.27f, .12f, .07f), .5f);
            if (name.Contains("velvet")) return Lit("velvet", new Color(.46f, .05f, .09f), .12f);
            return Lit("table top", Color.white, .12f, 0f, Resources.Load<Texture2D>("ThreeD/table-top"));
        }

        public static GameObject Ball(Transform parent, Vector3 at, Vector3 size, Material material) =>
            Primitive(PrimitiveType.Sphere, parent, at, Quaternion.identity, size, material);

        public static GameObject Block(Transform parent, Vector3 at, Vector3 size, Material material) =>
            Primitive(PrimitiveType.Cube, parent, at, Quaternion.identity, size, material);

        /// <summary>Цилиндр от точки до точки: руки и шея манекенов.</summary>
        public static GameObject Limb(Transform parent, Vector3 from, Vector3 to, float radius, Material material)
        {
            var along = to - from;
            return Primitive(PrimitiveType.Cylinder, parent, (from + to) / 2, Quaternion.FromToRotation(Vector3.up, along.normalized),
                new Vector3(radius * 2, along.magnitude / 2, radius * 2), material);
        }

        private static GameObject Primitive(PrimitiveType type, Transform parent, Vector3 at, Quaternion rotation, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localRotation = rotation;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }
    }
}
