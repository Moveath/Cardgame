using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Gentler painted lighting. Shared single-pass material; source art and UI retain their detail.</summary>
    internal static class HubScenery
    {
        private static Material _material;
        private static Material _streetMaterial;
        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                var shader = Resources.Load<Shader>("Shaders/HubScenery");
                if (shader != null) _material = new Material(shader) { name = "Soft scenery", hideFlags = HideFlags.HideAndDontSave };
                return _material;
            }
        }

        private static Material StreetMaterial
        {
            get
            {
                if (_streetMaterial != null) return _streetMaterial;
                if (Material == null) return null;
                _streetMaterial = new Material(Material) { name = "Soft street reflections", hideFlags = HideFlags.HideAndDontSave };
                _streetMaterial.SetFloat("_RoadReflections", .32f);
                return _streetMaterial;
            }
        }

        public static void Apply(Graphic image, bool street = false) => image.material = street ? StreetMaterial : Material;

        // Full-screen views that duplicate the city/interior painting must use the same colour treatment.
        // Documents, cards, coins, signs, controls and text deliberately use their own regular UI materials.
        public static bool IsBackdrop(string resource) => resource == "bank-desk" || resource == "fortune-tent"
            || resource == "tavern-hall";
    }
}
