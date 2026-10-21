using System;
using System.Collections.Generic;
using CardGame.Bots;
using CardGame.Core.Games.Govno;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Playable local table. Rules and losses live in Core, bots see only their hand.</summary>
    public sealed class GovnoTableController : MonoBehaviour
    {
        private const double ExchangeSeconds = 0.8;
        private const float CountdownSeconds = 3f;
        private const float SeatFigureScale = 0.70f;
        private const float LossZoomSeconds = 0.4f;
        private const float LossHoldSeconds = 1.1f;
        private const float LossCloseScale = 1.6f;
        private const float TimerBarWidth = 190f;
        private static readonly Color Paper = new Color(0.97f, 0.94f, 0.85f);
        private static readonly Color Gold = new Color(1f, 0.79f, 0.30f);
        private static readonly Color Danger = new Color(1f, 0.40f, 0.31f);
        private static readonly Color[] Skin = {
            new Color(0.94f, 0.72f, 0.51f), new Color(0.69f, 0.44f, 0.27f),
            new Color(0.99f, 0.83f, 0.66f), new Color(0.82f, 0.57f, 0.36f),
            new Color(0.58f, 0.36f, 0.23f), new Color(0.92f, 0.66f, 0.46f) };
        private static readonly Vector2 OpponentHandOffset = new Vector2(98, -10);
        private static readonly Vector2 OpponentIncomingOffset = new Vector2(98, -78);
        private static readonly string[] Depth = { "Утонул", "По шею", "По пояс", "По колено", "На суше" };

        private GovnoMatchSettings _settings;
        private Action _onExit;
        private GovnoGame _game;
        private SimpleGovnoBot[] _bots;
        private Sprite[] _portraits;
        private Font _font;
        private RectTransform _board, _seatLayer, _flyLayer, _handLayer, _handNames, _reactionZone;
        private RectTransform _intro, _result, _pause;
        private RectTransform _presentationPanel;
        private Image _overlayShade;
        private CanvasGroup _navigationGroup;
        private RectTransform _pauseButton;
        private RectTransform _activeForeground;
        private readonly List<ForegroundPanel> _foregroundPanels = new List<ForegroundPanel>();
        private Text _presentationTitle, _presentationNumber, _presentationDetail;
        private Text _status, _message, _timer, _hint, _resultText, _blockText;
        private RectTransform _timerGroup;
        private Image _timerFill;
        private GovnoTapTarget _zoneTap;
        private GovnoHandGesture _handGesture;
        private RectTransform _handInputArea;
        private Image _reactionHalo;
        private GovnoReactionEffects _effects;
        private readonly CardView[] _cards = new CardView[4];
        private readonly List<BotAction> _actions = new List<BotAction>();
        private readonly List<Flight> _flights = new List<Flight>();
        private readonly List<HandFlight> _hands = new List<HandFlight>();
        private Vector2[] _seatPositions;
        private RectTransform[] _seatAnchors;
        private GovnoLifeSticker[] _seatStickers;
        private Text[] _seatLabels, _seatDepth, _seatReady;
        private CardView[][] _backs;
        private bool[] _shownPassed;
        private int[] _shownWarnings;
        private double _clock, _visualClock;
        private bool _paused;
        private bool _hubMatchReported;
        private bool _presentationActive, _countingDown, _countdownAfterExchange;
        private float _presentationRemaining;
        private Action _afterPresentation;
        private GovnoPhase? _shownPhase;
        private Vector2 _lastViewport;
        private int _shownRevision = -1, _shownRound = -1, _shownExchange = -1, _shownReaction = -1;
        private int _scheduledExchange = -1, _scheduledReaction = -1, _shownHands;
        private int _thrownCaller = -1;
        private float _stackShift;
        private RectTransform _lossLayer;
        private CanvasGroup _lossGroup, _cardGroup;
        private Text _lossCaption;
        private int _lossPlayer = -1;
        private float _lossElapsed, _lossLeadIn;
        private bool _lossZoomed, _lossSinking;

        private sealed class BotAction { public int Player, Token; public bool Slap; public double At; }
        private sealed class Flight { public CardView View; public Vector2 From, To; public int Sender, Receiver; public double At; }
        private sealed class HandFlight { public RectTransform Rect; public Text Name; public Vector2 From, To; public int Player; public float Rotation; public double At; }
        private sealed class ForegroundPanel { public RectTransform Rect; public CanvasGroup Group; public float Reveal; }

        public void Configure(GovnoMatchSettings settings, Action onExit) { _settings = settings; _onExit = onExit; }

        private void Start()
        {
            _font = UiFonts.Body;
            BuildUi();
            Resize();
            RefreshForeground(0);
        }

        private void StartMatch()
        {
            _hubMatchReported = false;
            EndLossCutscene();
            _seatAnchors = null;
            _clock = 0;
            _visualClock = 0;
            _paused = false;
            _presentationActive = _countingDown = _countdownAfterExchange = false;
            _presentationPanel.gameObject.SetActive(false);
            _afterPresentation = null;
            _shownPhase = null;
            _pause.gameObject.SetActive(false);
            _intro.gameObject.SetActive(false);
            _result.gameObject.SetActive(false);
            _shownRevision = _shownRound = _shownExchange = _shownReaction = -1;
            _scheduledExchange = _scheduledReaction = -1;
            _shownHands = 0;
            _actions.Clear();
            _handGesture.CancelGesture();
            _effects.ResetEffects();
            ClearFlights();
            ClearHands();
            var count = Mathf.Clamp(_settings.PlayerCount, 3, 6);
            var seed = UnityEngine.Random.Range(0, int.MaxValue);
            _portraits = new Sprite[count];
            _portraits[0] = PlayerProfile.Avatar;
            if (_portraits[0] == null)
            {
                foreach (var group in AvatarCatalog.Groups)
                    if (group.Id == "yegor" && group.Sprites.Count > 0)
                    {
                        _portraits[0] = group.Sprites[UnityEngine.Random.Range(0, group.Sprites.Count)];
                        break;
                    }
                if (_portraits[0] == null && AvatarCatalog.CommonAvatars.Count > 0)
                    _portraits[0] = AvatarCatalog.CommonAvatars[0];
            }
            var pool = AvatarCatalog.OpponentPortraits(_portraits[0], count - 1);
            var names = new List<string> { PlayerProfile.DisplayName };
            var fallback = new[] { "Борис", "Вера", "Игорь", "Ольга", "Пётр" };
            _bots = new SimpleGovnoBot[count];
            for (var i = 1; i < count; i++)
            {
                if (pool.Count > 0) _portraits[i] = pool[(i - 1) % pool.Count];
                var title = AvatarCatalog.PersonalTitleOf(_portraits[i]) ?? fallback[i - 1];
                var name = title;
                for (var suffix = 2; names.Contains(name); suffix++) name = title + " " + suffix;
                names.Add(name);
                _bots[i] = new SimpleGovnoBot(unchecked(seed + i * 7919));
            }
            HubRoster.TryApply("govno", names, _portraits);
            _game = new GovnoGame(names, seed, _settings.Chaos);
            _shownPassed = new bool[count];
            _shownWarnings = new int[count];
            Render();
            BeginCountdown("Приготовься", "Посмотри свои карты. На передачу будет " + _settings.PickSecondsText + " секунды.");
        }

        private void Update()
        {
            Resize();
            // Menu transitions use real time even when the game or countdown is paused.
            RefreshForeground(Time.unscaledDeltaTime);
            if (_game == null || _paused) return;
            _visualClock += Time.unscaledDeltaTime;
            if (_presentationActive)
            {
                UpdatePresentation(Time.unscaledDeltaTime);
                Animate();
                return;
            }
            _clock += Time.unscaledDeltaTime;
            ScheduleBots();
            // Process independent bot decisions chronologically, not by seat index or frame rate.
            while (_actions.Count > 0 && _actions[0].At <= _clock)
            {
                var action = _actions[0];
                _actions.RemoveAt(0);
                _game.AdvanceTo(Math.Max(_game.Now, action.At));
                if (action.Slap && _game.Phase == GovnoPhase.Reacting && action.Token == _game.ReactionNumber)
                    _game.TrySlap(action.Player);
                else if (!action.Slap && _game.Phase == GovnoPhase.Picking && action.Token == _game.ExchangeNumber)
                {
                    if (_game.Players[action.Player].HasFourOfAKind)
                    {
                        if (!_game.TryCall(action.Player) && _game.Players[action.Player].BlockedUntil > _game.Now)
                        {
                            action.At = _game.Players[action.Player].BlockedUntil + 0.02;
                            _actions.Add(action);
                        }
                    }
                    else _game.TryPass(action.Player, _bots[action.Player].ChooseSlot(_game.Players[action.Player]));
                }
                if (_shownRevision != _game.Revision) Render();
                if (_presentationActive) { Animate(); return; }
                ScheduleBots();
            }
            _game.AdvanceTo(Math.Max(_game.Now, _clock));
            if (_shownRevision != _game.Revision) Render();
            if (_presentationActive) { Animate(); return; }
            if (_game.Phase == GovnoPhase.Exchanging && _clock - _game.PhaseStartedAt >= ExchangeSeconds)
            {
                FinishExchange();
                if (_presentationActive) { Animate(); return; }
            }
            if (_shownRevision != _game.Revision) Render();
            ScheduleBots();
            Animate();
            RefreshClock();
        }

        private void FinishExchange()
        {
            if (_presentationActive || _game.Phase != GovnoPhase.Exchanging) return;
            if (_countdownAfterExchange)
            {
                // Leave outgoing cards face-down at their receivers' incoming spots throughout
                // 3-2-1. Receiving/shuffling earlier reveals a completed set before play resumes.
                BeginCountdown("Продолжаем", "Карты откроются после отсчёта.", () =>
                {
                    _countdownAfterExchange = false;
                    _game.CompleteExchange();
                    Render();
                });
            }
            else
            {
                _game.CompleteExchange();
                Render();
            }
        }

        private void ScheduleBots()
        {
            if (_presentationActive) return;
            if (_game.Phase == GovnoPhase.Picking && _scheduledExchange != _game.ExchangeNumber)
            {
                _scheduledExchange = _game.ExchangeNumber;
                _actions.Clear();
                foreach (var id in _game.ActivePlayers)
                {
                    if (id == 0) continue;
                    var delay = _game.Players[id].HasFourOfAKind ? _bots[id].CallDelay()
                        : _bots[id].PickDelay(_game.PickDurationSeconds);
                    _actions.Add(new BotAction { Player = id, Token = _game.ExchangeNumber, At = _game.Now + delay });
                }
            }
            else if (_game.Phase == GovnoPhase.Reacting && _scheduledReaction != _game.ReactionNumber)
            {
                _scheduledReaction = _game.ReactionNumber;
                _actions.Clear();
                foreach (var id in _game.ReactionPlayers)
                {
                    if (id == 0 || Contains(_game.HandOrder, id)) continue;
                    _actions.Add(new BotAction { Player = id, Slap = true, Token = _game.ReactionNumber,
                        At = Math.Max(_game.Now + _bots[id].ReactionDelay(), _game.Players[id].BlockedUntil + 0.02) });
                }
            }
            else if (_game.Phase != GovnoPhase.Picking && _game.Phase != GovnoPhase.Reacting) _actions.Clear();
            _actions.Sort((a, b) => a.At.CompareTo(b.At));
        }

        private bool CanUseHand() => _game != null && !_paused && !_presentationActive
            && _game.Phase == GovnoPhase.Picking && _game.Players[0].IsAlive && !_game.Players[0].HasPassed;

        private void PassCard(int slot)
        {
            if (_paused || _presentationActive || _game == null) return;
            if (_game.TryPass(0, slot)) Render();
        }

        private void PressCentre()
        {
            if (_paused || _presentationActive || _game == null) return;
            if (_game.Now < _game.Players[0].BlockedUntil) return;
            if (_game.Phase == GovnoPhase.Reacting) _game.TrySlap(0);
            else
            {
                var blocked = _game.Players[0].BlockedUntil;
                if (_game.Phase == GovnoPhase.Picking) _game.TryCall(0);
                else _game.FalseStart(0);
                if (_game.Players[0].BlockedUntil > blocked) _effects.FalseStart(_visualClock);
            }
            Render();
            ScheduleBots();
        }

        private void Render()
        {
            if (!_hubMatchReported && _game.IsFinished)
            {
                _hubMatchReported = true;
                var won = false;
                foreach (var winner in _game.Winners) if (winner == 0) won = true;
                HubMatchReporter.ReportMatchCompleted("govno", won);
            }
            var enteredPhase = _shownPhase != _game.Phase;
            var startingLoss = enteredPhase && (_game.Phase == GovnoPhase.RoundOver || _game.IsFinished);
            _shownPhase = _game.Phase;
            var missedNames = new List<string>();
            _shownRevision = _game.Revision;
            if (_shownRound != _game.RoundNumber)
            {
                _shownRound = _game.RoundNumber;
                _thrownCaller = -1;
                _effects.ResetEffects();
                // Keep each player's place (and drowned player's pool) for the whole match.
                if (_seatAnchors == null) BuildSeats();
                ClearHands();
                _shownHands = 0;
            }
            if (_shownExchange != _game.ExchangeNumber)
            {
                _shownExchange = _game.ExchangeNumber;
                ClearFlights();
                Array.Clear(_shownPassed, 0, _shownPassed.Length);
            }
            for (var id = 0; id < _game.Players.Count; id++)
            {
                var p = _game.Players[id];
                if (p.Warnings > _shownWarnings[id]) missedNames.Add(p.Name);
                _shownWarnings[id] = p.Warnings;
                if (p.HasPassed && !_shownPassed[id] && p.IsAlive)
                {
                    var receiver = _game.LeftOf(id);
                    var view = CardView.Create(_flyLayer, _font, 0.45f);
                    view.ShowBack(); view.SetPlain();
                    var from = HandPosition(id);
                    var to = IncomingPosition(receiver);
                    view.Rect.anchoredPosition = from;
                    _flights.Add(new Flight { View = view, From = from, To = to, Sender = id, Receiver = receiver, At = _visualClock });
                    _shownPassed[id] = true;
                }
                if (_seatStickers[id] == null) continue;
                // The losing player sinks only after the camera has reached the close-up.
                if (!(startingLoss && id == _game.LastLoser) && id != _lossPlayer)
                    _seatStickers[id].SetLives(p.Lives, true);
                _seatLabels[id].text = p.Name + (id == 0 ? " · ты" : "");
                _seatDepth[id].text = Depth[p.Lives];
                _seatDepth[id].color = !p.IsAlive ? Danger : p.Lives < 4 ? Gold : Paper;
                _seatReady[id].text = p.IsAlive && p.Warnings > 0 ? "штраф 1/2" : "";
                _seatReady[id].color = p.Warnings > 0 ? Danger : Paper;
                RefreshSeatPose(id);
                if (_backs[id] != null)
                    for (var n = 0; n < 4; n++) _backs[id][n].gameObject.SetActive(p.IsAlive && id != _thrownCaller && n < p.Hand.Count);
            }
            for (var slot = 0; slot < 4; slot++)
            {
                var card = _game.Players[0].Slots[slot];
                _cards[slot].gameObject.SetActive(card.HasValue && _game.Players[0].IsAlive && _thrownCaller != 0);
                if (card.HasValue) _cards[slot].ShowFace(card.Value);
            }
            var picking = _game.Phase == GovnoPhase.Picking;
            var reacting = _game.Phase == GovnoPhase.Reacting;
            _zoneTap.Enabled = (picking || reacting || _game.Phase == GovnoPhase.Exchanging
                || _game.Phase == GovnoPhase.ReactionCountdown) && _game.Players[0].IsAlive;
            _reactionHalo.gameObject.SetActive(reacting);
            if (_shownReaction != _game.ReactionNumber && reacting)
            {
                _shownReaction = _game.ReactionNumber;
                // A slow frame must not spend reaction time before the signal is drawn.
                _clock = _game.Now;
                _handGesture.CancelGesture();
                var firstCall = _game.Caller >= 0 && _thrownCaller < 0;
                _effects.ShowSignal(firstCall ? _game.Players[_game.Caller].Name : "Внимание", firstCall, _visualClock);
                if (firstCall)
                {
                    _thrownCaller = _game.Caller;
                    _effects.ThrowCards(_game.Players[_thrownCaller].Hand, HandPosition(_thrownCaller), _thrownCaller == 0, _visualClock);
                    if (_thrownCaller == 0) foreach (var card in _cards) card.gameObject.SetActive(false);
                    else foreach (var back in _backs[_thrownCaller]) back.gameObject.SetActive(false);
                }
            }
            while (_shownHands < _game.HandOrder.Count)
            {
                AddHand(_game.HandOrder[_shownHands], _shownHands);
                _shownHands++;
            }
            _status.color = reacting ? Gold : Paper;
            if (picking || _game.Phase == GovnoPhase.Exchanging)
            {
                _status.text = "";
                _message.text = "";
            }
            else if (_game.Phase == GovnoPhase.ReactionCountdown)
            { _status.text = "Приготовься к сигналу"; _message.text = _game.IsPenaltyReaction ? "Два штрафа у нескольких игроков. Последний теряет жизнь." : "Несколько игроков не нажали. Повторный сигнал."; }
            else if (reacting)
            {
                _status.text = "";
                _message.text = "";
            }
            foreach (var hand in _hands)
                hand.Name.color = ShowLossHands && hand.Player == _game.LastLoser ? Danger : Paper;
            if (_game.Phase == GovnoPhase.RoundOver || _game.IsFinished)
            {
                var loser = _game.Players[_game.LastLoser];
                var reason = _game.LossReason == GovnoLossReason.SecondTimeout ? "два опоздания"
                    : _game.LossReason == GovnoLossReason.NoReaction ? "не положил руку" : "последняя рука";
                _status.text = loser.Name + (loser.IsAlive ? " погружается" : " утонул");
                _status.color = Danger;
                _message.text = reason + " · " + Depth[loser.Lives] + (loser.IsAlive ? ". Новая раздача…" : ".");
                if (_game.IsFinished)
                {
                    var a = _game.Players[_game.Winners[0]];
                    var b = _game.Players[_game.Winners[1]];
                    _resultText.text = "ДВА ПОБЕДИТЕЛЯ\n\n" + a.Name + " · " + Depth[a.Lives] + "\n"
                        + b.Name + " · " + Depth[b.Lives] + "\n\n" + loser.Name + " утонул";
                }
            }
            _hint.text = !_game.Players[0].IsAlive ? "Ты утонул. Можно смотреть до конца или вернуться в меню."
                : "";
            RefreshClock();
            if (_presentationActive) { SetPresentationVisibility(true); return; }
            if (enteredPhase && (_game.Phase == GovnoPhase.RoundOver || _game.IsFinished))
            {
                var loser = _game.Players[_game.LastLoser];
                var reason = _game.LossReason == GovnoLossReason.SecondTimeout ? "Два опоздания за раунд"
                    : _game.LossReason == GovnoLossReason.NoReaction ? "Не положил руку" : "Положил руку последним";
                _lossPlayer = _game.LastLoser;
                _lossElapsed = 0;
                _lossLeadIn = ShowLossHands ? 1.4f : 0.15f;
                _lossZoomed = _lossSinking = false;
                _lossCaption.text = loser.Name + " · " + Depth[loser.Lives] + "\n" + reason;
                BeginNotice(loser.Name + " погружается", reason,
                    _lossLeadIn + LossZoomSeconds * 2 + GovnoLifeSticker.SinkSeconds + LossHoldSeconds, () =>
                    {
                        if (_game.IsFinished) { _result.gameObject.SetActive(true); RefreshForeground(0); return; }
                        _game.StartNextRound();
                        Render();
                        BeginCountdown("Новый раунд", "Погружение сохранено. Карты розданы заново.");
                    });
            }
            else if (enteredPhase && _game.Phase == GovnoPhase.ReactionCountdown)
            {
                BeginNotice("Приготовьтесь", _game.IsPenaltyReaction
                    ? "Несколько игроков получили второй штраф.\nСейчас они разыграют потерю жизни на реакцию."
                    : "Несколько игроков не нажали.\nСейчас будет повторный сигнал.", 2f,
                    () => BeginCountdown("Ждите сигнал", "После сигнала хлопните по центру стола.", () =>
                    {
                        _clock = _game.Deadline;
                        _game.AdvanceTo(_clock);
                        Render();
                    }));
            }
            else if (_game.Phase == GovnoPhase.Exchanging && missedNames.Count > 0)
            {
                _countdownAfterExchange = true;
                BeginNotice("Не успели передать карту", string.Join(", ", missedNames)
                    + "\nШтраф 1/2. Карта выбрана автоматически.", 2f, null);
            }
        }

        // Presentation time is separate from the rules clock: no bot, deadline or click
        // can consume a player's choice window while a result or 3–2–1 is on screen.
        private void BeginNotice(string title, string detail, float seconds, Action after)
        {
            _handGesture.CancelGesture();
            _presentationActive = true;
            _countingDown = false;
            _presentationRemaining = seconds;
            _afterPresentation = after;
            _clock = _game.Now;
            _presentationTitle.text = title;
            _presentationDetail.text = detail;
            _presentationTitle.gameObject.SetActive(true);
            _presentationDetail.gameObject.SetActive(true);
            _presentationNumber.text = "";
            var showStack = ShowLossHands;
            _presentationTitle.rectTransform.sizeDelta = new Vector2(showStack ? 460 : 620, showStack ? 90 : 65);
            _presentationDetail.rectTransform.sizeDelta = new Vector2(showStack ? 460 : 620, showStack ? 95 : 70);
            _presentationTitle.rectTransform.anchoredPosition = showStack ? new Vector2(-250, 25) : Vector2.zero;
            _presentationDetail.rectTransform.anchoredPosition = showStack ? new Vector2(-250, -65) : new Vector2(0, -72);
            if (showStack) _effects.ClearCards();
            SetPresentationVisibility(true);
            _presentationPanel.gameObject.SetActive(true);
            RefreshForeground(0);
            RefreshClock();
        }

        private void BeginCountdown(string title, string detail, Action after = null)
        {
            BeginNotice(title, detail, CountdownSeconds, after);
            _countingDown = true;
            _presentationTitle.rectTransform.anchoredPosition = new Vector2(0, 42);
            _presentationDetail.rectTransform.anchoredPosition = new Vector2(0, -125);
            _presentationNumber.text = "3";
        }

        private void UpdatePresentation(float deltaTime)
        {
            if (_lossPlayer >= 0) TickLossCutscene(deltaTime);
            _presentationRemaining = Mathf.Max(0, _presentationRemaining - deltaTime);
            if (_countingDown) _presentationNumber.text = Mathf.Max(1, Mathf.CeilToInt(_presentationRemaining)).ToString();
            if (_presentationRemaining > 0) return;
            var after = _afterPresentation;
            _afterPresentation = null;
            EndLossCutscene();
            _presentationActive = false;
            _presentationPanel.gameObject.SetActive(false);
            SetPresentationVisibility(false);
            after?.Invoke();
            if (!_presentationActive)
            {
                Render();
                ScheduleBots();
            }
            RefreshForeground(0);
        }

        private void SetPresentationVisibility(bool presenting)
        {
            // Keep players visible and reserve the centre for one message at a time.
            _status.gameObject.SetActive(!presenting);
            _message.gameObject.SetActive(!presenting);
            _blockText.gameObject.SetActive(!presenting);
            _handLayer.gameObject.SetActive((!presenting || ShowLossHands) && !_lossZoomed);
            _reactionHalo.gameObject.SetActive(!presenting && _game.Phase == GovnoPhase.Reacting);
            _effects.SetCovered(presenting);
        }

        private bool ShowLossHands => _game != null && _game.HandOrder.Count > 0
            && (_game.Phase == GovnoPhase.RoundOver || _game.IsFinished);

        private void TickLossCutscene(float deltaTime)
        {
            if (_lossPlayer < 0 || deltaTime <= 0) return;
            var previous = _lossElapsed;
            _lossElapsed += deltaTime;
            if (_lossElapsed < _lossLeadIn) return;
            var sticker = _seatStickers[_lossPlayer];
            if (!_lossZoomed)
            {
                _lossZoomed = true;
                _lossLayer.gameObject.SetActive(true);
                sticker.Root.SetParent(_lossLayer, false);
                _lossCaption.transform.SetAsLastSibling();
                _presentationTitle.gameObject.SetActive(false);
                _presentationDetail.gameObject.SetActive(false);
                _handLayer.gameObject.SetActive(false);
            }
            var sinkAt = _lossLeadIn + LossZoomSeconds;
            if (_lossElapsed >= sinkAt)
            {
                if (!_lossSinking)
                {
                    _lossSinking = true;
                    sticker.SetLives(_game.Players[_lossPlayer].Lives, true);
                }
                // Tick only the portion after zoom-in, including when a frame crosses the boundary.
                sticker.Tick(Mathf.Max(0, _lossElapsed - sinkAt) - Mathf.Max(0, previous - sinkAt));
            }
            var returnAt = sinkAt + GovnoLifeSticker.SinkSeconds + LossHoldSeconds;
            var zoom = _lossElapsed < returnAt
                ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((_lossElapsed - _lossLeadIn) / LossZoomSeconds))
                : 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((_lossElapsed - returnAt) / LossZoomSeconds));
            sticker.Root.anchoredPosition = Vector2.Lerp(_seatPositions[_lossPlayer] + SeatStickerOffset(_lossPlayer),
                new Vector2(0, 30), zoom);
            sticker.Root.localScale = Vector3.one * Mathf.Lerp(SeatFigureScale, LossCloseScale, zoom);
            RefreshSeatPose(_lossPlayer);
            _cardGroup.alpha = 1 - zoom;
            _lossCaption.color = new Color(Paper.r, Paper.g, Paper.b, zoom);
        }

        private void EndLossCutscene()
        {
            if (_lossPlayer >= 0 && _seatStickers != null && _seatStickers[_lossPlayer] != null)
            {
                var root = _seatStickers[_lossPlayer].Root;
                root.SetParent(_seatAnchors[_lossPlayer], false);
                root.anchoredPosition = SeatStickerOffset(_lossPlayer);
                root.localScale = Vector3.one * SeatFigureScale;
                RefreshSeatPose(_lossPlayer);
            }
            _lossPlayer = -1;
            _lossZoomed = _lossSinking = false;
            if (_lossLayer != null) _lossLayer.gameObject.SetActive(false);
            if (_cardGroup != null) _cardGroup.alpha = 1;
        }

        private void RefreshClock()
        {
            RefreshTimerVisibility();
            if (_game == null) return;
            if (_presentationActive)
            {
                _zoneTap.Enabled = false;
                return;
            }
            var picking = _game.Phase == GovnoPhase.Picking;
            var remaining = Math.Max(0, _game.Deadline - _game.Now);
            _timer.text = picking ? remaining.ToString("0.0") + " с" : "";
            _timerFill.rectTransform.sizeDelta = new Vector2(picking
                ? TimerBarWidth * Mathf.Clamp01((float)(remaining / _game.PickDurationSeconds)) : 0, 5);
            _timer.color = picking && remaining < 0.6 ? Danger : Paper;
            _timerFill.color = picking && remaining < 0.6 ? Danger : Gold;
            var block = Math.Max(0, _game.Players[0].BlockedUntil - _game.Now);
            _blockText.text = block > 0 ? "Фальстарт · ещё " + block.ToString("0.0") + " с" : "";
        }

        private void RefreshTimerVisibility()
        {
            if (_timerGroup == null) return;
            // The centre belongs to one event at a time: picking, clap, countdown or close-up.
            _timerGroup.gameObject.SetActive(_game != null && _game.Phase == GovnoPhase.Picking
                && !_paused && !_presentationActive && !_intro.gameObject.activeSelf
                && !_pause.gameObject.activeSelf && !_result.gameObject.activeSelf);
        }

        private void BuildSeats()
        {
            Clear(_seatLayer);
            var count = _game.Players.Count;
            _seatPositions = new Vector2[count];
            _seatAnchors = new RectTransform[count];
            _seatStickers = new GovnoLifeSticker[count];
            _seatLabels = new Text[count]; _seatDepth = new Text[count]; _seatReady = new Text[count];
            _backs = new CardView[count][];
            for (var id = 0; id < count; id++)
            {
                var anchor = Rect(_seatLayer, "Seat" + id, Vector2.zero, new Vector2(230, 270));
                _seatAnchors[id] = anchor;
                _seatStickers[id] = GovnoLifeSticker.Create(anchor, _portraits[id], _font, Skin[id]);
                _seatStickers[id].Root.localScale = Vector3.one * SeatFigureScale;
                _seatStickers[id].SetLives(_game.Players[id].Lives, false);
                // Text keeps its readable size while the entire figure, including the head, shrinks.
                _seatLabels[id] = Label(anchor, "", 21, new Vector2(0, -142), new Vector2(id == 0 ? 200 : 180, 30));
                if (id != 0)
                {
                    _seatLabels[id].resizeTextForBestFit = true;
                    _seatLabels[id].resizeTextMinSize = 17;
                    _seatLabels[id].resizeTextMaxSize = MobileUi.TextSize(21);
                }
                _seatDepth[id] = Label(anchor, "", 18, new Vector2(0, -168), new Vector2(180, 26));
                _seatReady[id] = Label(anchor, "", 17, new Vector2(0, -191), new Vector2(id == 0 ? 200 : 180, 25));
                if (MobileUi.Phone)
                {
                    _seatLabels[id].rectTransform.sizeDelta = new Vector2(id == 0 ? 220 : 200, 34);
                    _seatDepth[id].rectTransform.sizeDelta = new Vector2(180, 30);
                    _seatReady[id].rectTransform.sizeDelta = new Vector2(id == 0 ? 220 : 180, 30);
                }
                if (id == 0) continue;
                _backs[id] = new CardView[4];
                for (var n = 0; n < 4; n++)
                {
                    var view = CardView.Create(anchor, _font, 0.28f);
                    view.ShowBack(); view.SetPlain();
                    view.Rect.anchoredPosition = OpponentHandOffset + new Vector2((n - 1.5f) * 17, 0);
                    _backs[id][n] = view;
                }
            }
            RepositionSeats();
        }

        private void RepositionSeats()
        {
            if (_seatAnchors == null) return;
            // Reflow the seats already on screen. Recreating stickers here would finish their
            // current plunge and discard particles, including while a resize occurs on pause.
            var opponents = new List<int>();
            for (var id = 1; id < _seatAnchors.Length; id++)
                if (_seatAnchors[id] != null) opponents.Add(id);
            var halfWidth = _board.rect.width / 2;
            var halfHeight = _board.rect.height / 2;
            // Ряд соперников — от края до края стола. Карты лежат справа от фигуры, поэтому место
            // несимметрично: слева от середины ~75 (фигура), справа ~170 (карты) — и ряд сдвинут
            // влево, а не держит пустой запас у левого края. При шести игроках так просторнее.
            var left = -halfWidth + 110;
            var right = Mathf.Max(left + 1, halfWidth - 185);
            // Свой персонаж стоит вплотную слева от своих четырёх карт, а не в углу стола:
            // в узком окне угол занят соседом слева.
            var handHalfWidth = 1.5f * HandCardSpacing + CardView.Width * 1.10f / 2;
            _seatPositions[0] = new Vector2(-Mathf.Min(handHalfWidth + 110, halfWidth - 100),
                -halfHeight + (MobileUi.Phone ? 269 : 245));
            if (_seatAnchors[0] != null) _seatAnchors[0].anchoredPosition = _seatPositions[0];
            for (var n = 0; n < opponents.Count; n++)
            {
                var id = opponents[n];
                var t = opponents.Count == 1 ? 0.5f : n / (float)(opponents.Count - 1);
                var x = opponents.Count == 1 ? 0 : Mathf.Lerp(left, right, t);
                var fromMiddle = 2 * t - 1;
                // Lift the upper seats away from the centre timer; side seats retain their
                // clearance below the navigation buttons, including in a six-player match.
                _seatPositions[id] = new Vector2(x, halfHeight - 150 - 160 * fromMiddle * fromMiddle);
                _seatAnchors[id].anchoredPosition = _seatPositions[id];
            }
            for (var id = 0; id < _seatStickers.Length; id++) RefreshSeatPose(id);
        }

        private Vector2 SeatStickerOffset(int id) => Vector2.up * (_seatStickers[id].SeatedLift * SeatFigureScale);

        private void RefreshSeatPose(int id)
        {
            var sticker = _seatStickers[id];
            if (sticker == null) return;
            var offset = SeatStickerOffset(id);
            // Cards remain at their fixed seat coordinates. Only the miniature scene and its
            // captions rise; the cinematic layer owns the actor's position during a close-up.
            if (sticker.Root.parent == _seatAnchors[id]) sticker.Root.anchoredPosition = offset;
            _seatLabels[id].rectTransform.anchoredPosition = offset + new Vector2(0, -142);
            _seatDepth[id].rectTransform.anchoredPosition = offset + new Vector2(0, MobileUi.Phone ? -176 : -168);
            _seatReady[id].rectTransform.anchoredPosition = offset + new Vector2(0, MobileUi.Phone ? -207 : -191);
        }

        private const float HandCardSpacing = 161;
        private float HandY => -_board.rect.height / 2 + 135;
        private Vector2 HandPosition(int id) => id == 0 ? new Vector2(0, HandY)
            : _seatPositions[id] + OpponentHandOffset;
        private Vector2 IncomingPosition(int id) => id == 0 ? new Vector2(_board.rect.width / 2 - 290, HandY + 30)
            : _seatPositions[id] + OpponentIncomingOffset;

        private void Animate()
        {
            _effects.Tick(_visualClock);
            _stackShift = Mathf.MoveTowards(_stackShift, ShowLossHands ? 120 : 0, Time.unscaledDeltaTime * 400);
            _handNames.anchoredPosition = new Vector2(280 + _stackShift * (100f / 120), -5);
            for (var id = 0; id < _seatStickers.Length; id++)
            {
                if (_seatStickers[id] != null && id != _lossPlayer) _seatStickers[id].Tick(Time.unscaledDeltaTime);
                RefreshSeatPose(id);
            }
            foreach (var flight in _flights)
            {
                var t = Mathf.Clamp01((float)(_visualClock - flight.At) / 0.3f);
                var pos = Vector2.Lerp(flight.From, flight.To, Mathf.SmoothStep(0, 1, t));
                if (_game.Phase == GovnoPhase.Exchanging && !_countdownAfterExchange)
                {
                    var receive = Mathf.Clamp01((float)(_clock - _game.PhaseStartedAt - 0.4) / 0.4f);
                    var target = HandPosition(flight.Receiver);
                    pos = Vector2.Lerp(pos, target, receive);
                }
                flight.View.Rect.anchoredPosition = pos;
            }
            foreach (var hand in _hands)
            {
                var t = Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)(_visualClock - hand.At) / 0.35f));
                hand.Rect.anchoredPosition = Vector2.Lerp(hand.From, hand.To + new Vector2(_stackShift, 0), t);
                hand.Rect.localRotation = Quaternion.Euler(0, 0, hand.Rotation * t);
            }
        }

        private void AddHand(int id, int order)
        {
            var root = GovnoReactionEffects.CreatePalm(_handLayer, "Hand" + id, Skin[id]);
            root.anchoredPosition = _seatPositions[id];
            var nameplate = Box(_handNames, "Clap" + (order + 1), new Vector2(0, 80 - order * 36),
                new Vector2(220, 32), new Color(0.10f, 0.10f, 0.11f, 0.88f));
            Box(nameplate.transform, "HandColour", new Vector2(-102, 0), new Vector2(6, 24), Skin[id]);
            var name = Label(nameplate.transform, (order + 1) + ". " + _game.Players[id].Name + (id == 0 ? " · ты" : ""),
                22, new Vector2(5, 0), new Vector2(194, 30));
            name.alignment = TextAnchor.MiddleLeft;
            name.resizeTextForBestFit = true;
            name.resizeTextMinSize = 18;
            name.resizeTextMaxSize = 22;
            _handNames.SetAsLastSibling();
            _hands.Add(new HandFlight { Rect = root, From = _seatPositions[id], To = new Vector2((order - 2) * 15, -45 + order * 6),
                Name = name, Player = id, At = _visualClock, Rotation = (id % 2 == 0 ? 1 : -1) * (6 + order * 4) });
            _effects.Slap();
        }

        private void BuildUi()
        {
            var background = Box(transform, "Room", Vector2.zero, Vector2.zero, new Color(0.09f, 0.07f, 0.055f));
            Stretch(background.rectTransform);
            var wood = Box(transform, "Wood", Vector2.zero, Vector2.zero, new Color(0.32f, 0.20f, 0.11f));
            Stretch(wood.rectTransform);
            wood.rectTransform.offsetMin = new Vector2(24, 24);
            wood.rectTransform.offsetMax = new Vector2(-24, -24);
            var felt = Box(transform, "Felt", Vector2.zero, Vector2.zero, new Color(0.075f, 0.25f, 0.18f));
            Stretch(felt.rectTransform);
            felt.rectTransform.offsetMin = new Vector2(38, 38);
            felt.rectTransform.offsetMax = new Vector2(-38, -38);
            _board = Rect(transform, "GovnoTable", Vector2.zero, new Vector2(1600, 900));
            _seatLayer = Rect(_board, "Seats", Vector2.zero, new Vector2(1600, 900));
            _reactionZone = Box(_board, "ReactionZone", new Vector2(0, -35), new Vector2(1216, 340), Color.clear).rectTransform;
            _reactionZone.GetComponent<Image>().raycastTarget = true;
            _zoneTap = _reactionZone.gameObject.AddComponent<GovnoTapTarget>();
            _zoneTap.Press = PressCentre;
            _reactionHalo = Rect(_reactionZone, "ReactionGlow", Vector2.zero, new Vector2(650, 330)).gameObject.AddComponent<Image>();
            _reactionHalo.sprite = UiTextures.RadialGlow(128, new Color(1f, 0.82f, 0.35f));
            _reactionHalo.color = new Color(1, 1, 1, 0.16f);
            _reactionHalo.raycastTarget = false;
            _reactionHalo.gameObject.SetActive(false);
            _timerGroup = Rect(_board, "CentreTimer", Vector2.zero, new Vector2(280, 112));
            // All timer graphics pass pointer events through to the full-size clap zone.
            _timer = Label(_timerGroup, "", 76, new Vector2(0, 10), new Vector2(280, 90));
            _timer.fontStyle = FontStyle.Bold;
            var timerShadow = _timer.gameObject.AddComponent<Shadow>();
            timerShadow.effectColor = new Color(0, 0, 0, 0.32f);
            timerShadow.effectDistance = new Vector2(0, -2);
            Box(_timerGroup, "TimerTrack", new Vector2(0, -44), new Vector2(TimerBarWidth, 5),
                new Color(Paper.r, Paper.g, Paper.b, 0.12f));
            _timerFill = Box(_timerGroup, "TimerFill", new Vector2(-TimerBarWidth / 2, -44),
                new Vector2(0, 5), Gold);
            _timerFill.rectTransform.pivot = new Vector2(0, 0.5f);
            _timerGroup.gameObject.SetActive(false);
            _status = Label(_board, "", 26, new Vector2(0, 35), new Vector2(1050, 36));
            _message = Label(_board, "", 20, new Vector2(0, -198), new Vector2(1180, 28));
            _blockText = Label(_board, "", 20, new Vector2(0, -120), new Vector2(400, 35));
            _blockText.color = Danger;
            _effects = GovnoReactionEffects.Create(_board, _font);
            _handLayer = Rect(_board, "Hands", Vector2.zero, new Vector2(1600, 900));
            _handNames = Rect(_handLayer, "ClapOrder", new Vector2(280, -5), new Vector2(220, 216));
            var cardsRoot = Rect(_board, "PlayerCards", Vector2.zero, Vector2.zero);
            _cardGroup = cardsRoot.gameObject.AddComponent<CanvasGroup>();
            for (var slot = 0; slot < 4; slot++)
            {
                var card = CardView.Create(cardsRoot, _font, 1.10f);
                card.ShowBack(); card.SetPlain();
                card.Rect.anchoredPosition = new Vector2((slot - 1.5f) * 161, -315);
                _cards[slot] = card;
            }
            _flyLayer = Rect(_board, "PassingCards", Vector2.zero, new Vector2(1600, 900));
            _flyLayer.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            _handInputArea = Box(_board, "WholeHandInput", new Vector2(0, -315), new Vector2(760, 210), Color.clear).rectTransform;
            _handInputArea.GetComponent<Image>().raycastTarget = true;
            _handGesture = _handInputArea.gameObject.AddComponent<GovnoHandGesture>();
            _handGesture.Cards = new[] { _cards[0].Rect, _cards[1].Rect, _cards[2].Rect, _cards[3].Rect };
            _handGesture.Tap = PassCard;
            // Dragging cards never calls or passes; the centre tap is the only announcement.
            _handGesture.CanInteract = CanUseHand;
            _handGesture.CurrentToken = () => _game == null ? -1 : unchecked(_game.ExchangeNumber * 8 + (int)_game.Phase);
            _hint = Label(_board, "", 17, new Vector2(0, -429), new Vector2(1000, 26));
            _overlayShade = Rect(_board, "ForegroundShade", Vector2.zero, Vector2.zero).gameObject.AddComponent<Image>();
            Stretch(_overlayShade.rectTransform);
            _overlayShade.color = Color.clear;
            _overlayShade.raycastTarget = false;
            var navigation = Rect(_board, "Navigation", Vector2.zero, Vector2.zero);
            Stretch(navigation);
            _navigationGroup = navigation.gameObject.AddComponent<CanvasGroup>();
            var menu = Button(navigation, "В меню", Vector2.zero, new Vector2(180, MobileUi.Phone ? 84 : 60), () => _onExit?.Invoke());
            Anchor(menu, new Vector2(0, 1), new Vector2(130, MobileUi.Phone ? -66 : -60));
            _pauseButton = Button(navigation, "Пауза", Vector2.zero, new Vector2(160, MobileUi.Phone ? 84 : 54), () => SetPaused(true));
            Anchor(_pauseButton, new Vector2(1, 1), new Vector2(-120, MobileUi.Phone ? -66 : -60));
            _intro = Panel("Intro", new Vector2(760, 510));
            Label(_intro, "Перед началом", 34, new Vector2(0, 192), new Vector2(670, 52)).color = Gold;
            Label(_intro, "Тап по карте — передать влево за " + _settings.PickSecondsText + " секунды.\nСобрал каре — нажми на центр стола.\nКрик и первая ладонь сработают вместе.\n\nУслышал «ГОВНО!» — тоже хлопни по центру.\nРанний хлопок: блокировка на 1 секунду.\nПромах мимо центра не штрафуется.\n\nПоследний теряет жизнь. Остались двое — оба победили.\nЖизни между раундами сохраняются.",
                21, new Vector2(0, 3), new Vector2(690, 292));
            Button(_intro, "Начать", new Vector2(155, -192), new Vector2(260, 60), StartMatch, true, true);
            Button(_intro, "В меню", new Vector2(-155, -192), new Vector2(260, 60), () => _onExit?.Invoke(), true);
            _result = Panel("Results", new Vector2(720, 440));
            _resultText = Label(_result, "", 28, new Vector2(0, 55), new Vector2(650, 245));
            HubQuestResultLine.Attach(_resultText, () => _game != null && _game.IsFinished, 640);
            Button(_result, "Ещё партия", new Vector2(-155, -152), new Vector2(270, 60), StartMatch, true, true);
            Button(_result, "В меню", new Vector2(155, -152), new Vector2(270, 60), () => _onExit?.Invoke(), true);
            _result.gameObject.SetActive(false);
            _presentationPanel = Panel("RoundPresentation", new Vector2(620, 300), false);
            _presentationTitle = Label(_presentationPanel, "", 31, Vector2.zero, new Vector2(620, 65));
            _presentationTitle.color = Gold;
            _presentationNumber = Label(_presentationPanel, "", 92, new Vector2(0, -39), new Vector2(160, 106));
            _presentationNumber.fontStyle = FontStyle.Bold;
            _presentationDetail = Label(_presentationPanel, "", 22, new Vector2(0, -125), new Vector2(620, 70));
            foreach (var text in new[] { _presentationTitle, _presentationNumber, _presentationDetail })
            {
                var shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.65f);
                shadow.effectDistance = new Vector2(0, -2);
            }
            _presentationPanel.gameObject.SetActive(false);
            // Above the table dimmer, below the pause menu. The actual seat figure moves here.
            _lossLayer = Rect(_board, "LossCloseUp", Vector2.zero, Vector2.zero);
            _lossGroup = _lossLayer.gameObject.AddComponent<CanvasGroup>();
            _lossGroup.blocksRaycasts = _lossGroup.interactable = false;
            _lossCaption = Label(_lossLayer, "", 26, new Vector2(0, -275), new Vector2(720, 90));
            var captionShadow = _lossCaption.gameObject.AddComponent<Shadow>();
            captionShadow.effectColor = new Color(0, 0, 0, 0.75f);
            captionShadow.effectDistance = new Vector2(0, -2);
            _lossLayer.gameObject.SetActive(false);
            _pause = Panel("Pause", new Vector2(520, 360));
            Label(_pause, "Пауза", 36, new Vector2(0, 114), new Vector2(450, 54)).color = Gold;
            Label(_pause, "Таймеры остановлены", 22, new Vector2(0, 55), new Vector2(450, 38));
            Button(_pause, "Продолжить", new Vector2(0, -30), new Vector2(320, 60), () => SetPaused(false), true, true);
            Button(_pause, "В меню", new Vector2(0, -110), new Vector2(320, 54), () => _onExit?.Invoke(), true);
            _pause.gameObject.SetActive(false);
        }

        private void SetPaused(bool paused)
        {
            if (_game == null || _game.IsFinished) return;
            _paused = paused;
            _handGesture.CancelGesture();
            _effects.SetPaused(paused);
            _pause.gameObject.SetActive(paused);
            RefreshForeground(0);
        }
        private void OnApplicationFocus(bool focus) { if (!focus && _game != null) SetPaused(true); }
        private void Resize()
        {
            if (_board == null) return;
            var size = ((RectTransform)transform).rect.size;
            if (size.x <= 0 || size.y <= 0 || (size - _lastViewport).sqrMagnitude < 0.1f) return;
            _lastViewport = size;
            var scale = Mathf.Max(.01f, Mathf.Min(size.x / 1600, size.y / (MobileUi.Phone ? 820 : 900)));
            _board.localScale = Vector3.one * scale;
            // Keep readable proportions but let the logical table grow with the window.
            // The felt/frame stretch independently, so wide windows have no side bars.
            _board.sizeDelta = size / scale;
            // About a third of the viewport; stop just above the separate hand gesture area.
            _reactionZone.sizeDelta = new Vector2(_board.rect.width * 0.76f,
                Mathf.Min(_board.rect.height * 0.38f, -2 * (HandY + 145)));
            _handInputArea.anchoredPosition = new Vector2(0, HandY);
            _handGesture.CancelGesture();
            for (var slot = 0; slot < 4; slot++)
                _cards[slot].Rect.anchoredPosition = new Vector2((slot - 1.5f) * HandCardSpacing, HandY);
            _hint.rectTransform.anchoredPosition = new Vector2(0, -_board.rect.height / 2 + 28);
            if (_game != null)
            {
                RepositionSeats();
                foreach (var flight in _flights)
                {
                    flight.From = HandPosition(flight.Sender);
                    flight.To = IncomingPosition(flight.Receiver);
                }
            }
        }
        private void ClearFlights() { foreach (var flight in _flights) Destroy(flight.View.gameObject); _flights.Clear(); }
        private void ClearHands()
        {
            foreach (var hand in _hands) { hand.Rect.gameObject.SetActive(false); Destroy(hand.Rect.gameObject); }
            _hands.Clear();
            Clear(_handNames);
            _stackShift = 0;
        }
        private static bool Contains(IReadOnlyList<int> items, int id) { foreach (var item in items) if (item == id) return true; return false; }
        private static void Clear(Transform parent) { foreach (Transform child in parent) { child.gameObject.SetActive(false); Destroy(child.gameObject); } }

        private RectTransform Panel(string name, Vector2 size, bool surface = true)
        {
            var panel = Rect(_board, name, Vector2.zero, size);
            if (surface)
            {
                Box(panel, "Shadow", new Vector2(0, -9), size + new Vector2(12, 12), new Color(0, 0, 0, 0.20f));
                var background = Box(panel, "Surface", Vector2.zero, size, new Color(0.12f, 0.12f, 0.13f, 0.98f));
                background.raycastTarget = true;
            }
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
            _foregroundPanels.Add(new ForegroundPanel { Rect = panel, Group = group });
            return panel;
        }

        private void RefreshForeground(float deltaTime)
        {
            if (_overlayShade == null) return;
            // One dim layer, even when pause temporarily takes over a countdown.
            var foreground = _pause.gameObject.activeSelf ? _pause
                : _result.gameObject.activeSelf ? _result
                : _intro.gameObject.activeSelf ? _intro
                : _presentationPanel.gameObject.activeSelf ? _presentationPanel : null;
            var menuOpen = foreground != null && foreground != _presentationPanel;
            var dim = foreground == null ? 0f : menuOpen ? 0.52f : _lossZoomed ? 0.60f : 0.36f;
            var alpha = Mathf.MoveTowards(_overlayShade.color.a, dim, deltaTime * 3f);
            _overlayShade.color = new Color(0.015f, 0.018f, 0.025f, alpha);
            // Block the table immediately, independently of the visual fade.
            _overlayShade.raycastTarget = foreground != null;
            _navigationGroup.alpha = menuOpen ? 0 : 1;
            _navigationGroup.interactable = _navigationGroup.blocksRaycasts = !menuOpen;
            _lossGroup.alpha = menuOpen ? 0.35f : 1f;
            _pauseButton.gameObject.SetActive(_game != null && !_game.IsFinished);
            foreach (var panel in _foregroundPanels)
            {
                var visible = panel.Rect == foreground;
                panel.Reveal = visible ? Mathf.MoveTowards(panel.Reveal, 1, deltaTime / 0.18f) : 0;
                var ease = Mathf.SmoothStep(0, 1, panel.Reveal);
                panel.Group.alpha = ease;
                panel.Group.interactable = panel.Group.blocksRaycasts = visible;
                panel.Rect.localScale = Vector3.one * Mathf.Lerp(0.97f, 1, ease);
            }
            var events = EventSystem.current;
            if (events != null && foreground != null)
            {
                var selected = events.currentSelectedGameObject;
                var outside = selected != null && !selected.transform.IsChildOf(foreground)
                    && (menuOpen || !selected.transform.IsChildOf(_navigationGroup.transform));
                if (_activeForeground != foreground || outside)
                {
                    var button = foreground.GetComponentInChildren<UnityEngine.UI.Button>();
                    events.SetSelectedGameObject(button == null ? null : button.gameObject);
                }
            }
            _activeForeground = foreground;
            RefreshTimerVisibility();
        }
        private RectTransform Button(Transform parent, string text, Vector2 position, Vector2 size, Action action,
            bool foreground = false, bool primary = false)
        {
            var image = Box(parent, text, position, size, primary ? Gold
                : foreground ? new Color(0.24f, 0.24f, 0.26f) : new Color(0.30f, 0.37f, 0.25f));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => action());
            var label = Label(image.transform, text, 23, Vector2.zero, size);
            if (primary) { label.color = new Color(0.12f, 0.12f, 0.13f); label.fontStyle = FontStyle.Bold; }
            return image.rectTransform;
        }
        private Text Label(Transform parent, string content, int size, Vector2 position, Vector2 bounds)
        {
            var rect = Rect(parent, "Text", position, bounds);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = _font; label.text = content; label.fontSize = MobileUi.TextSize(size);
            label.color = Paper; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }
        private static Image Box(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = UiTextures.RoundedRect(64, 64, 14, Color.white);
            image.type = Image.Type.Sliced; image.color = color; image.raycastTarget = false;
            return image;
        }
        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 position)
        { rect.anchorMin = rect.anchorMax = anchor; rect.anchoredPosition = position; }
    }
}
