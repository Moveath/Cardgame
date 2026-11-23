using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private const string FountainId = "fountain";
        private const float FountainWalkSlowdown = 1.5f;
        private const float FountainWalkIn = 1.05f * FountainWalkSlowdown, FountainWalkOut = .8f * FountainWalkSlowdown;
        private Image _fountainClose;
        private bool _fountainMissing;

        private bool FountainVisit => _visit >= 0 && _visit < CityPlaces.Length && CityPlaces[_visit].Id == FountainId;
        private bool FountainReduced => _state != null && _state.Settings.ReducedMotion;
        private float VisitInTime => FountainVisit ? (FountainReduced ? .18f : FountainWalkIn) : VisitIn;
        private float VisitOutTime => FountainVisit ? (FountainReduced ? .18f : FountainWalkOut) : VisitOut;

        private bool BuildFountainClose()
        {
            if (_fountainClose != null) return true;
            if (_fountainMissing || _cityArea == null || _cityRig == null) return false;
            var sprite = Resources.Load<Sprite>("UI/Hub/fountain-close");
            if (sprite == null) { _fountainMissing = true; return false; }
            // A separate background, above the city but below its controls. Nothing is pasted into the street.
            var rect = MakeRect(_cityArea, "Fountain close-up", 0, 0, 0, 0);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.SetSiblingIndex(_cityRig.GetSiblingIndex() + 1);
            _fountainClose = rect.gameObject.AddComponent<Image>();
            _fountainClose.sprite = sprite;
            _fountainClose.raycastTarget = false;
            HubScenery.Apply(_fountainClose);
            _fountainClose.color = new Color(1, 1, 1, 0);
            rect.gameObject.SetActive(false);
            return true;
        }

        private void HideFountain()
        {
            if (_fountainClose == null) return;
            _fountainClose.color = new Color(1, 1, 1, 0);
            _fountainClose.gameObject.SetActive(false);
        }

        private void UpdateFountain()
        {
            if (!FountainVisit || _visitPhase == 0) { HideFountain(); return; }
            if (_visitPhase == 2) FrameFountainWalk(1);
        }

        /// <summary>A short approach followed by a soft scene dissolve, like the news kiosk. Retreat reverses it.</summary>
        private void FrameFountainWalk(float t)
        {
            if (_cityArea == null || _cityRig == null) return;
            if (_diveShade != null) _diveShade.gameObject.SetActive(false);
            if (_diveVignette != null) _diveVignette.gameObject.SetActive(false);
            t = Mathf.Clamp01(t);
            var progress = Mathf.SmoothStep(0, 1, t);
            var motion = FountainReduced ? 0 : progress;
            AimCity(1 + .3f * motion, .35f * motion, VisitFocus());
            if (!BuildFountainClose()) return;

            var area = _cityArea.rect.size;
            var source = _fountainClose.sprite.rect.size;
            var cover = Mathf.Max(area.x / source.x, area.y / source.y);
            var rect = _fountainClose.rectTransform;
            rect.sizeDelta = source * cover;
            // Keep the entire viewport covered during the dissolve, with only a very gentle final push.
            rect.localScale = Vector3.one * (1 + .025f * motion);
            rect.anchoredPosition = Vector2.zero;
            // Extra time belongs to camera travel: before the dissolve on entry, after it on exit.
            // Remap to the original clock so the dissolve itself keeps its original duration.
            var fadeTime = 1 - (1 - t) * FountainWalkSlowdown;
            var fade = Mathf.SmoothStep(0, 1, FountainReduced ? t : Mathf.InverseLerp(.32f, .75f, fadeTime));
            _fountainClose.color = new Color(1, 1, 1, fade);
            _fountainClose.gameObject.SetActive(fade > 0);
        }
    }
}
