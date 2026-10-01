using System.Collections.Generic;
using CardGame.Core.Cards;
using CardGame.Core.Games.Govno;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Local audiovisual feedback; never changes cards, deadlines or reaction order.</summary>
    public sealed class GovnoReactionEffects : MonoBehaviour
    {
        private RectTransform _board, _cardLayer, _banner, _tremble;
        private Text _shout, _speaker;
        private Image _flash;
        private CanvasGroup _bannerGroup, _trembleGroup;
        private AudioSource _voice, _sounds;
        private AudioClip _call, _signal, _slap;
        private Font _font;
        private double _signalAt = -100, _trembleAt = -100;
        private bool _covered;
        private readonly List<ThrownCard> _cards = new List<ThrownCard>();

        private sealed class ThrownCard
        {
            public CardView View;
            public Vector2 From, To;
            public float Angle;
            public double At;
        }

        public static GovnoReactionEffects Create(RectTransform board, Font font)
        {
            var go = new GameObject("ReactionEffects", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(board, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.GetComponent<CanvasGroup>().blocksRaycasts = false;
            var effects = go.AddComponent<GovnoReactionEffects>();
            effects._board = board;
            effects._font = font;
            effects.Build();
            return effects;
        }

        private void Build()
        {
            _cardLayer = Rect(transform, "ThrownCards", Vector2.zero, Vector2.zero);
            _flash = Rect(transform, "SignalFlash", Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            _flash.rectTransform.anchorMin = Vector2.zero; _flash.rectTransform.anchorMax = Vector2.one;
            _flash.rectTransform.offsetMin = _flash.rectTransform.offsetMax = Vector2.zero;
            _flash.raycastTarget = false;
            _flash.color = Color.clear;
            _banner = Rect(transform, "Shout", new Vector2(0, 94), new Vector2(850, 120));
            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            _speaker = Text(_banner, "", 22, new Vector2(0, 43), new Vector2(800, 32));
            _shout = Text(_banner, "", 60, new Vector2(0, -8), new Vector2(850, 82));
            _shout.fontStyle = FontStyle.Bold;
            var shadow = _shout.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.7f);
            shadow.effectDistance = new Vector2(0, -3);
            _tremble = CreatePalm(transform, "FalseStartHand", new Color(0.94f, 0.72f, 0.51f));
            _trembleGroup = _tremble.gameObject.AddComponent<CanvasGroup>();
            _voice = gameObject.AddComponent<AudioSource>();
            _sounds = gameObject.AddComponent<AudioSource>();
            _voice.playOnAwake = _sounds.playOnAwake = false;
            _call = Resources.Load<AudioClip>("Govno/Audio/call");
            _signal = Tone("ReactionSignal", 0.20f, false);
            _slap = Tone("PalmSlap", 0.10f, true);
            ResetEffects();
        }

        public void ShowSignal(string speaker, bool realCall, double now)
        {
            _signalAt = now;
            _speaker.text = speaker;
            _shout.text = realCall ? "ГОВНО!" : "ХЛОПОК!";
            _shout.color = new Color(1f, 0.83f, 0.35f);
            _voice.Stop();
            if (realCall && _call != null) _voice.PlayOneShot(_call);
            _sounds.PlayOneShot(_signal, 0.6f);
#if UNITY_ANDROID || UNITY_IOS
            if(PlayerHubRuntime.Current==null || PlayerHubRuntime.Current.Settings.Vibration)Handheld.Vibrate();
#endif
        }

        public void ThrowCards(IReadOnlyList<Card> cards, Vector2 from, bool local, double now)
        {
            ClearCards();
            for (var i = 0; i < cards.Count; i++)
            {
                var view = CardView.Create(_cardLayer, _font, 0.70f);
                view.ShowFace(cards[i]); view.SetPlain();
                var spread = i - (cards.Count - 1) / 2f;
                var origin = from + new Vector2(spread * (local ? 161 : 17), 0);
                view.Rect.anchoredPosition = origin;
                _cards.Add(new ThrownCard { View = view, From = origin,
                    To = new Vector2(spread * 62, -36 - Mathf.Abs(spread) * 8),
                    Angle = -spread * 12, At = now });
            }
        }

        public void FalseStart(double now) { _trembleAt = now; }
        public void Slap() { _sounds.PlayOneShot(_slap, 0.75f); }
        public void SetPaused(bool paused)
        {
            _banner.gameObject.SetActive(!paused);
            if (paused) { _voice.Pause(); _sounds.Pause(); _board.anchoredPosition = Vector2.zero; _flash.color = Color.clear; }
            else { _voice.UnPause(); _sounds.UnPause(); }
        }
        public void SetCovered(bool covered)
        {
            _covered = covered;
            if (covered)
            {
                _bannerGroup.alpha = 0;
                _flash.color = Color.clear;
            }
        }
        public void ClearCards()
        {
            foreach (var card in _cards) { card.View.gameObject.SetActive(false); Destroy(card.View.gameObject); }
            _cards.Clear();
        }
        public void ResetEffects()
        {
            ClearCards();
            _signalAt = _trembleAt = -100;
            _covered = false;
            _banner.gameObject.SetActive(true);
            _board.anchoredPosition = Vector2.zero;
            _bannerGroup.alpha = _trembleGroup.alpha = 0;
            _flash.color = Color.clear;
            _voice.Stop(); _sounds.Stop();
        }

        public void Tick(double now)
        {
            var elapsed = (float)(now - _signalAt);
            const float duration = 1.15f;
            _bannerGroup.alpha = _covered ? 0 : 1 - Mathf.Clamp01((elapsed - duration + 0.25f) / 0.25f);
            _banner.localScale = Vector3.one * (1 + 0.12f * Mathf.Max(0, 1 - elapsed / 0.22f));
            var pulse = !_covered ? Mathf.Max(0, 1 - elapsed / 0.20f) : 0;
            _flash.color = new Color(1f, 0.88f, 0.57f, pulse * 0.14f);
            _board.anchoredPosition = new Vector2(Mathf.Sin(elapsed * 95), Mathf.Cos(elapsed * 79)) * (pulse * 4);
            var tremble = (float)(now - _trembleAt);
            _trembleGroup.alpha = _covered ? 0 : Mathf.Clamp01(1 - tremble / (float)GovnoGame.FalseStartSeconds) * 0.8f;
            _tremble.anchoredPosition = new Vector2(Mathf.Sin(tremble * 85) * 7, -135 - tremble * 100);
            _tremble.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(tremble * 65) * 9);
            foreach (var card in _cards)
            {
                var t = Mathf.Clamp01((float)(now - card.At) / 0.36f);
                var ease = 1 - Mathf.Pow(1 - t, 3);
                card.View.Rect.anchoredPosition = Vector2.Lerp(card.From, card.To, ease)
                    + Vector2.up * Mathf.Sin(t * Mathf.PI) * 55;
                card.View.Rect.localRotation = Quaternion.Euler(0, 0, card.Angle * ease);
            }
        }

        public static RectTransform CreatePalm(Transform parent, string name, Color color)
        {
            var root = Rect(parent, name, Vector2.zero, new Vector2(90, 130));
            Box(root, "Wrist", new Vector2(0, -43), new Vector2(47, 45), color);
            Box(root, "Palm", new Vector2(0, -6), new Vector2(72, 68), color);
            var lengths = new[] { 39f, 55f, 62f, 48f };
            for (var i = 0; i < 4; i++)
                Box(root, "Finger", new Vector2(-27 + i * 18, 20 + lengths[i] / 2), new Vector2(16, lengths[i]), color);
            var thumb = Box(root, "Thumb", new Vector2(-43, 1), new Vector2(21, 49), color);
            thumb.localRotation = Quaternion.Euler(0, 0, 37);
            return root;
        }

        private static AudioClip Tone(string name, float seconds, bool noise)
        {
            const int rate = 22050;
            var samples = new float[Mathf.RoundToInt(rate * seconds)];
            var random = new System.Random(712);
            for (var i = 0; i < samples.Length; i++)
            {
                var envelope = Mathf.Pow(1 - i / (float)samples.Length, noise ? 3 : 1);
                samples[i] = (noise ? (float)random.NextDouble() * 2 - 1 : Mathf.Sin(i * 2 * Mathf.PI * 740 / rate))
                    * envelope * (noise ? 0.4f : 0.18f);
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private Text Text(Transform parent, string content, int size, Vector2 position, Vector2 bounds)
        {
            var label = Rect(parent, "Text", position, bounds).gameObject.AddComponent<Text>();
            label.font = _font; label.fontSize = size; label.text = content;
            label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.raycastTarget = false;
            return label;
        }
        private static RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, pos, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
            image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
            return rect;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos; rect.sizeDelta = size;
            return rect;
        }
        private void OnDestroy() { if (_signal != null) Destroy(_signal); if (_slap != null) Destroy(_slap); }
    }
}
