using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CardGameClient
{
    /// <summary>
    /// Ролики жителя поверх нарисованного зала Клуба (пианистка за роялем, бармен). Первый кадр петли «играет»
    /// совпадает с картинкой зала, поэтому до первого кадра видна картинка, а ролик проявляется поверх неё.
    /// У жителя с позой покоя (<see cref="Setup"/> с idle) четыре ролика: play и idle — петли, stop (play → idle)
    /// и start (idle → play) — переходы; переходов может не быть, тогда петли сменяют друг друга плавно.
    /// Житель «играет», пока его не остановили нажатием (<see cref="Stopped"/>) и <see cref="Allowed"/> разрешает
    /// (у пианистки — включённая музыка), иначе сидит. Без позы покоя остановленный ролик гаснет до картинки.
    /// Для жителя без idle <see cref="SetupVariation"/> добавляет редкий одиночный жест между обычными петлями.
    /// Ролики крутятся, только пока житель виден на экране. Два слоя: новый ролик проявляется поверх старого.
    /// </summary>
    public sealed class HubClubVideo : MonoBehaviour
    {
        private const float FadeSeconds = .3f;
        // Сначала руки заканчивают жест, затем игра набирает темп. Длинное наложение разных поз двоит карту.
        private const float PlayLandingSeconds = .22f;
        private const float PlayEntrySpeed = .55f;
        private const float PlaySpeedupSeconds = .75f;

        private enum Clip { Play, Idle, Stop, Start, Action }

        private sealed class Layer
        {
            public RawImage Image;
            public VideoPlayer Player;
            public RenderTexture Frames;
            public Clip Clip;
            public bool Ended;
            // В текстуре уже кадр нынешнего ролика (а не последний кадр прошлого — он давал вспышку чужой позы).
            public bool Fresh;
            public bool Cueing;
            public long CueFrame;
            public bool SpeedingUp;
            public float SpeedupTime;
            public long ObservedFrame = -1;
            public float HeldSeconds;
        }

        public bool Stopped;
        public Func<bool> Allowed;
        public RectTransform Viewport;

        private readonly VideoClip[] _clips = new VideoClip[5];
        // С какого кадра петля начинается после перехода (подобран по позе и движению, `club-<житель>-entry`).
        private readonly long[] _entries = new long[5];
        private Layer _front, _back;
        private Material _material;
        private bool _started, _switching;
        private float _alpha;
        private float _blendTime, _blendSeconds;
        private bool _bakedLanding;
        private readonly System.Random _variationRandom = new System.Random();
        private float _variationWait, _variationLastDelay = -1;
        private double _variationLastPeriod = -1;
        private float _variationMinDelay, _variationMaxDelay, _variationBlendSeconds;
        private readonly Vector3[] _corners = new Vector3[4], _view = new Vector3[4];

        /// <summary>Плеер ролика, что сейчас на экране.</summary>
        public VideoPlayer Player => _front?.Player;
        public Material Material => _material;
        /// <summary>Житель должен играть (виден, не остановлен, разрешён).</summary>
        public bool Wanted => !Stopped && (Allowed == null || Allowed()) && Visible();
        /// <summary>Какой ролик сейчас на экране или проявляется: play, idle, stop, start, action.</summary>
        public string Showing => (_switching ? _back : _front).Clip.ToString().ToLowerInvariant();
        public bool HasIdle => _clips[(int)Clip.Idle] != null;
        public bool HasVariation => _clips[(int)Clip.Action] != null;
        public bool IsBlending => _switching;
        /// <summary>До разрешения следующего жеста; затем ждём совпадающую фазу конца play.</summary>
        public float VariationSecondsRemaining => _variationWait;
        public float VariationMinDelay => _variationMinDelay;
        public float VariationMaxDelay => _variationMaxDelay;
        public float VariationBlendSeconds => _variationBlendSeconds;

        /// <summary>
        /// Редкий жест между обычными петлями. Начало action должно повторять последние blendSeconds петли play,
        /// а конец action — первые blendSeconds play. Переключаемся только в этих совпадающих фазах.
        /// Отсчёт идёт при видимом, работающем жителе; новый интервал выбирается после возвращения к play.
        /// </summary>
        public void SetupVariation(VideoClip action, float minDelay = 8, float maxDelay = 28, float blendSeconds = .3f)
        {
            if (_front == null) throw new InvalidOperationException("Call Setup before SetupVariation.");
            if (HasIdle) throw new InvalidOperationException("An occasional action requires a resident without idle/start/stop clips.");
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.width != _clips[(int)Clip.Play].width || action.height != _clips[(int)Clip.Play].height)
                throw new ArgumentException("Play and action clips must share the same crop and resolution.", nameof(action));
            if (minDelay < 0 || maxDelay < minDelay || float.IsNaN(minDelay) || float.IsNaN(maxDelay) ||
                float.IsInfinity(minDelay) || float.IsInfinity(maxDelay))
                throw new ArgumentOutOfRangeException(nameof(minDelay), "Variation intervals must be finite and ordered.");
            if (blendSeconds <= 0 || float.IsNaN(blendSeconds) || float.IsInfinity(blendSeconds) ||
                blendSeconds * 2 >= action.length || blendSeconds * 2 >= _clips[(int)Clip.Play].length)
                throw new ArgumentOutOfRangeException(nameof(blendSeconds), "The overlap must fit within both clips.");
            _clips[(int)Clip.Action] = action;
            _variationMinDelay = minDelay;
            _variationMaxDelay = maxDelay;
            _variationBlendSeconds = blendSeconds;
            ScheduleVariation();
        }

        private void ScheduleVariation()
        {
            var range = _variationMaxDelay - _variationMinDelay;
            var clip = _clips[(int)Clip.Play];
            var duration = clip.frameCount / clip.frameRate;
            var phase = _front.Clip == Clip.Play && _front.Player.isPrepared
                ? Math.Max(0, _front.Player.frame) / clip.frameRate : 0;
            var untilExit = Math.Max(0, duration - _variationBlendSeconds - phase);
            double Period(float wait) => Math.Ceiling(Math.Max(0, wait - untilExit) / duration);
            var canVaryPeriod = Period(_variationMinDelay) != _variationLastPeriod ||
                Period(_variationMaxDelay) != _variationLastPeriod;
            float delay;
            var tries = 0;
            do
            {
                delay = _variationMinDelay + (float)_variationRandom.NextDouble() * range;
            }
            while (++tries < 32 && (Mathf.Abs(delay - _variationLastDelay) < range * .15f ||
                canVaryPeriod && Period(delay) == _variationLastPeriod));
            // Разные секунды могут попасть в один и тот же конец петли. Избегаем повтора и видимого интервала.
            if (canVaryPeriod && Period(delay) == _variationLastPeriod)
                delay = Period(_variationMinDelay) != _variationLastPeriod ? _variationMinDelay : _variationMaxDelay;
            _variationWait = _variationLastDelay = delay;
            _variationLastPeriod = Period(delay);
        }

        public void Setup(Texture mask, VideoClip play, VideoClip idle = null, VideoClip stop = null, VideoClip start = null,
            string entries = null)
        {
            foreach (var line in (entries ?? "").Split('\n'))
            {
                if (line.Trim() == "landing baked") { _bakedLanding = true; continue; }
                var parts = line.Trim().Split(' ');
                if (parts.Length == 2 && Enum.TryParse(parts[0], true, out Clip kind) && long.TryParse(parts[1], out var frame))
                    _entries[(int)kind] = frame;
            }
            _clips[(int)Clip.Play] = play;
            _clips[(int)Clip.Idle] = idle;
            _clips[(int)Clip.Stop] = idle != null ? stop : null;
            _clips[(int)Clip.Start] = idle != null ? start : null;
            var shader = Resources.Load<Shader>("Shaders/HubVideoMask");
            if (shader != null)
            {
                _material = new Material(shader);
                _material.SetTexture("_MaskTex", mask);
            }
            _front = MakeLayer("Video A", play);
            _back = MakeLayer("Video B", play);
            _front.Clip = _back.Clip = Clip.Play;
        }

        private Layer MakeLayer(string name, VideoClip size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var layer = new Layer { Image = rect.gameObject.AddComponent<RawImage>() };
            layer.Image.raycastTarget = false;
            layer.Image.material = _material;
            layer.Image.color = new Color(1, 1, 1, 0);
            layer.Frames = new RenderTexture((int)size.width, (int)size.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            layer.Image.texture = layer.Frames;
            layer.Player = rect.gameObject.AddComponent<VideoPlayer>();
            layer.Player.playOnAwake = false;
            layer.Player.source = VideoSource.VideoClip;
            layer.Player.skipOnDrop = true;
            layer.Player.waitForFirstFrame = true;
            layer.Player.audioOutputMode = VideoAudioOutputMode.None;
            layer.Player.renderMode = VideoRenderMode.RenderTexture;
            layer.Player.targetTexture = layer.Frames;
            layer.Player.loopPointReached += player => layer.Ended = !player.isLooping;
            layer.Player.frameReady += (player, frame) =>
            {
                // Seek может сначала сообщить нулевой кадр: ждём именно заказанную позу.
                if (layer.Cueing && frame < layer.CueFrame) return;
                layer.Fresh = true;
                player.sendFrameReadyEvents = false;
                if (layer.Cueing)
                {
                    player.Pause();
                    layer.Cueing = false;
                }
            };
            Clear(layer);
            return layer;
        }

        private bool Visible()
        {
            if (!isActiveAndEnabled) return false;
            if (Viewport == null) return true;
            ((RectTransform)transform).GetWorldCorners(_corners);
            Viewport.GetWorldCorners(_view);
            return _corners[2].x > _view[0].x && _corners[0].x < _view[2].x && _corners[2].y > _view[0].y && _corners[0].y < _view[2].y;
        }

        /// <summary>Пустая прозрачная текстура слоя: пока нет нового кадра, слой ничего не показывает.</summary>
        private static void Clear(Layer layer)
        {
            var active = RenderTexture.active;
            RenderTexture.active = layer.Frames;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = active;
            layer.Fresh = false;
        }

        private static void Load(Layer layer, VideoClip clip, Clip kind)
        {
            layer.Clip = kind;
            layer.Ended = false;
            ResetSpeed(layer);
            layer.Player.clip = clip;
            layer.Player.isLooping = kind == Clip.Play || kind == Clip.Idle;
            layer.Player.frame = 0;
            layer.Player.Play();
        }

        private static void ResetSpeed(Layer layer)
        {
            layer.ObservedFrame = -1;
            layer.HeldSeconds = 0;
            layer.SpeedingUp = false;
            layer.SpeedupTime = 0;
            if (layer.Player.canSetPlaybackSpeed) layer.Player.playbackSpeed = 1;
        }

        private static void AdvanceSpeed(Layer layer)
        {
            if (!layer.SpeedingUp || !layer.Player.isPlaying) return;
            layer.SpeedupTime = Mathf.Min(PlaySpeedupSeconds, layer.SpeedupTime + Time.unscaledDeltaTime);
            layer.Player.playbackSpeed = Mathf.Lerp(PlayEntrySpeed, 1,
                Mathf.SmoothStep(0, 1, layer.SpeedupTime / PlaySpeedupSeconds));
            if (layer.SpeedupTime >= PlaySpeedupSeconds) layer.SpeedingUp = false;
        }

        // A native decoder can stay isPlaying=true after a seek/loop while its texture is frozen.
        // Re-seek without clearing the texture; normal pauses and completed transitions are excluded.
        private static void RecoverHeldFrame(Layer layer)
        {
            var player = layer.Player;
            if (!player.isPrepared || !player.isPlaying || layer.Ended || layer.Cueing)
            {
                layer.HeldSeconds = 0;
                return;
            }
            var frame = player.frame;
            if (frame != layer.ObservedFrame)
            {
                layer.ObservedFrame = frame;
                layer.HeldSeconds = 0;
                return;
            }
            layer.HeldSeconds += Time.unscaledDeltaTime;
            var limit = Mathf.Max(.35f, 4f / Mathf.Max(1, (float)player.frameRate * player.playbackSpeed));
            if (layer.HeldSeconds < limit || player.frameCount == 0) return;
            layer.HeldSeconds = 0;
            player.Pause();
            player.frame = (Math.Max(0, frame) + 1) % (long)player.frameCount;
            player.Play();
        }

        /// <summary>Какой ролик пойдёт после нынешнего.</summary>
        private Clip Next(Clip now) => now switch
        {
            Clip.Play => _clips[(int)Clip.Stop] != null ? Clip.Stop : Clip.Idle,
            Clip.Idle => _clips[(int)Clip.Start] != null ? Clip.Start : Clip.Play,
            Clip.Stop => Clip.Idle,
            _ => Clip.Play,
        };

        /// <summary>
        /// Следующий ролик заранее загружен в задний слой и стоит на первом кадре: по команде он трогается сразу,
        /// без паузы на загрузку (из-за неё житель «доигрывал, замирал и рывком шёл дальше»).
        /// </summary>
        private void Ready(Clip kind)
        {
            var clip = _clips[(int)kind];
            if (_back.Clip == kind && _back.Player.clip == clip)
            {
                if (_back.Player.isPrepared && !_back.Fresh && !_back.Cueing)
                {
                    // Prepare загружает ролик, но не декодирует кадр после seek. Делаем это заранее,
                    // невидимо, и frameReady ставит плеер на паузу в нужной позе.
                    _back.CueFrame = EntryFrame();
                    _back.Cueing = true;
                    _back.Player.sendFrameReadyEvents = true;
                    _back.Player.frame = _back.CueFrame;
                    _back.Player.Play();
                }
                return;
            }
            _back.Clip = kind;
            _back.Ended = false;
            _back.Image.color = new Color(1, 1, 1, 0);
            _back.Player.Stop();
            ResetSpeed(_back);
            Clear(_back);
            _back.Cueing = false;
            _back.Player.clip = clip;
            _back.Player.isLooping = kind == Clip.Play || kind == Clip.Idle;
            _back.Player.Prepare();
        }

        /// <summary>Тронуть следующий ролик: он проявляется поверх нынешнего, пока тот ещё идёт.</summary>
        private void Begin()
        {
            var startingPlay = _front.Clip == Clip.Start && _back.Clip == Clip.Play;
            _blendTime = 0;
            _blendSeconds = _front.Clip == Clip.Action || _back.Clip == Clip.Action
                ? _variationBlendSeconds : startingPlay ? PlayLandingSeconds : FadeSeconds;
            if (startingPlay && !_bakedLanding)
            {
                var speed = _back.Player.canSetPlaybackSpeed ? PlayEntrySpeed : 1;
                if (_back.Player.canSetPlaybackSpeed) _back.Player.playbackSpeed = speed;
                _back.SpeedingUp = speed < 1;
                _back.SpeedupTime = 0;
            }
            // Кадр уже декодирован в Ready: не очищаем его и не делаем второй seek на самом стыке.
            _back.Player.Play();
            _back.Image.transform.SetAsLastSibling();
            _switching = true;
        }

        private long EntryFrame()
        {
            var fromTransition = _front.Clip == Clip.Stop || _front.Clip == Clip.Start;
            var entry = fromTransition ? _entries[(int)_back.Clip] : 0;
            if (_front.Clip == Clip.Start && _back.Clip == Clip.Play)
            {
                if (_bakedLanding)
                {
                    var overlap = (long)Mathf.Ceil(PlayLandingSeconds * (float)_back.Player.clip.frameRate);
                    var frames = (long)_back.Player.clip.frameCount;
                    return frames > 0 ? ((entry - overlap) % frames + frames) % frames : 0;
                }
                // entry подобран к КОНЦУ жеста. За время наложения петля должна прийти к нему,
                // а не уже уйти вперёд (раньше ещё и успевала перескочить через конец петли).
                var speed = _back.Player.canSetPlaybackSpeed ? PlayEntrySpeed : 1;
                var advance = (long)Mathf.Round(PlayLandingSeconds * (float)_back.Player.clip.frameRate * speed);
                var count = (long)_back.Player.clip.frameCount;
                if (count > 0) entry = ((entry - advance) % count + count) % count;
            }
            return entry;
        }

        private void Update()
        {
            if (_front == null) return;
            if (!Visible())
            {
                _front.HeldSeconds = _back.HeldSeconds = 0;
                _front.Player.Pause();
                if (_switching) _back.Player.Pause();
                return;
            }
            var play = !Stopped && (Allowed == null || Allowed());
            var rest = HasIdle ? Clip.Idle : Clip.Play;
            if (!_started)
            {
                // Первый показ: сразу нужная петля, проявляется поверх картинки зала.
                _started = true;
                Load(_front, _clips[(int)(play ? Clip.Play : rest)], play ? Clip.Play : rest);
                _alpha = 0;
            }
            if (HasVariation)
            {
                UpdateVariation(play);
                return;
            }
            if (_switching)
            {
                RecoverHeldFrame(_back);
                RecoverHeldFrame(_front);
                // Оба ролика идут, новый проявляется поверх: движение не прерывается.
                if (!_back.Player.isPlaying) _back.Player.Play();
                if (!_front.Player.isPlaying && !_front.Ended) _front.Player.Play();
                if (_back.Fresh)
                {
                    _blendTime = Mathf.Min(_blendSeconds, _blendTime + Time.unscaledDeltaTime);
                    var a = Mathf.SmoothStep(0, 1, _blendTime / _blendSeconds);
                    _back.Image.color = new Color(1, 1, 1, a);
                    if (a >= 1)
                    {
                        _front.Player.Stop();
                        _front.Image.color = new Color(1, 1, 1, 0);
                        (_front, _back) = (_back, _front);
                        _switching = false;
                        _alpha = 1;
                    }
                }
                return;
            }
            // Разгон начинается после сведения поз: во время него скорость фиксирована для точного совпадения фаз.
            AdvanceSpeed(_front);
            if (HasIdle || play) RecoverHeldFrame(_front);
            if (!HasIdle)
            {
                // Без позы покоя: остановленный житель гаснет до картинки зала и ждёт на первом кадре.
                if (play && !_front.Player.isPlaying) _front.Player.Play();
                // Проявившийся ролик на перезапуске петли не гаснет (там frame на миг 0 — сквозь ролик мелькала картинка).
                _alpha = Mathf.MoveTowards(_alpha, play && (_front.Player.frame >= 1 || _alpha > 0) ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
                _front.Image.color = new Color(1, 1, 1, _alpha);
                if (!play && _alpha <= 0 && _front.Player.isPlaying)
                {
                    _front.Player.Pause();
                    _front.Player.frame = 0;
                }
                return;
            }
            if (!_front.Player.isPlaying && !_front.Ended) _front.Player.Play();
            // Проявившийся ролик на перезапуске петли не гаснет: там frame на миг 0, и сквозь ролик мелькала картинка
            // зала (пианистка с руками на клавишах — «будто другая анимация»).
            _alpha = Mathf.MoveTowards(_alpha, _front.Player.frame >= 1 || _alpha > 0 ? 1 : 0, Time.unscaledDeltaTime / FadeSeconds);
            _front.Image.color = new Color(1, 1, 1, _alpha);
            var next = Next(_front.Clip);
            Ready(next);
            if (!_back.Player.isPrepared || !_back.Fresh) return;
            // Переход трогает следующую петлю чуть раньше своего конца — они перетекают друг в друга на ходу.
            var left = (long)_front.Player.frameCount - _front.Player.frame;
            var overlapSeconds = _front.Clip == Clip.Start ? PlayLandingSeconds : FadeSeconds;
            var overlap = (long)Mathf.Ceil(overlapSeconds * (float)_front.Player.frameRate);
            var go = _front.Clip switch
            {
                Clip.Play => !play,
                Clip.Idle => play,
                _ => _front.Ended || _front.Player.frame >= 1 && left <= overlap,
            };
            if (go) Begin();
        }

        private void UpdateVariation(bool play)
        {
            if (!play)
            {
                // Нажатие останавливает и жест, и его часы. Возобновление не перескакивает к другой позе.
                _front.Player.Pause();
                if (_switching) _back.Player.Pause();
                _front.HeldSeconds = _back.HeldSeconds = 0;
                _alpha = Mathf.MoveTowards(_alpha, 0, Time.unscaledDeltaTime / FadeSeconds);
                PaintVariation();
                return;
            }

            if (!_front.Player.isPlaying && !_front.Ended) _front.Player.Play();
            RecoverHeldFrame(_front);
            _alpha = Mathf.MoveTowards(_alpha, _front.Player.frame >= 1 || _alpha > 0 ? 1 : 0,
                Time.unscaledDeltaTime / FadeSeconds);
            if (_switching)
            {
                if (!_back.Player.isPlaying && !_back.Ended) _back.Player.Play();
                RecoverHeldFrame(_back);
                if (_back.Fresh)
                {
                    _blendTime = Mathf.Min(_blendSeconds, _blendTime + Time.unscaledDeltaTime);
                    if (_blendTime >= _blendSeconds)
                    {
                        _front.Player.Stop();
                        _front.Image.color = new Color(1, 1, 1, 0);
                        // Остановленный плеер выгрузил ролик: пусть Ready загрузит его заново, а не ждёт кадра,
                        // которого не будет (бармен застывал в конце жеста).
                        _front.Player.clip = null;
                        _front.Fresh = _front.Cueing = false;
                        (_front, _back) = (_back, _front);
                        _switching = false;
                        if (_front.Clip == Clip.Play) ScheduleVariation();
                    }
                }
                PaintVariation();
                return;
            }

            var returning = _front.Clip == Clip.Action;
            if (!returning && _alpha >= .999f)
                _variationWait = Mathf.Max(0, _variationWait - Time.unscaledDeltaTime);

            // Задний слой уже хранит декодированный кадр: на видимом стыке не нужен ни Prepare, ни seek.
            Ready(returning ? Clip.Play : Clip.Action);
            if (_back.Player.isPrepared && _back.Fresh && (returning || _variationWait <= 0))
            {
                var overlap = (long)Mathf.Ceil(_variationBlendSeconds * (float)_front.Player.frameRate);
                var remaining = (long)_front.Player.frameCount - _front.Player.frame;
                if (_front.Ended || _front.Player.frame >= 1 && remaining <= overlap)
                    Begin();
            }
            PaintVariation();
        }

        private void PaintVariation()
        {
            _front.Image.color = new Color(1, 1, 1, _alpha);
            if (_switching)
            {
                var a = Mathf.SmoothStep(0, 1, _blendTime / _blendSeconds);
                _back.Image.color = new Color(1, 1, 1, a * _alpha);
            }
        }

        private void OnDestroy()
        {
            foreach (var layer in new[] { _front, _back })
                if (layer?.Frames != null) layer.Frames.Release();
            if (_material != null) Destroy(_material);
        }
    }
}
