using System;
using System.Collections.Generic;
using CardGame.Bots;
using CardGame.Core.Cards;
using CardGame.Core.Games.Poker;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>A local no-limit table. Core owns cards, legal bets and every chip of the payout.</summary>
    public sealed class PokerTableController : MonoBehaviour
    {
        private PokerMatchSettings _settings;
        private Action _onExit;
        private Font _font;
        private PokerGame _game;
        private SimplePokerBot[] _bots;
        private Sprite[] _portraits;
        private RectTransform _stage,_seatLayer,_fxLayer,_controls,_intro,_pause;
        private PokerHelpOverlay _help;
        private Text _roundText,_potText,_potsText,_actionText,_turnText,_handText,_resultText;
        private Button _foldButton,_callButton,_raiseButton,_allInButton,_nextButton;
        private Text _callText,_raiseText,_nextText;
        private Slider _raiseSlider;
        private InputField _raiseInput;
        private RectTransform _raiseArea;
        private readonly List<Button> _raiseQuick=new List<Button>();
        private PokerChipPile _potChips,_humanChips;
        private readonly CardView[] _boardCards=new CardView[5];
        private readonly CardView[] _humanCards=new CardView[2];
        private Seat[] _seats;
        private Vector2[] _seatPositions,_betPositions;
        private readonly List<ChipFlight> _chipFlights=new List<ChipFlight>();
        private float _clock,_busyUntil,_thinkAt,_revealAt,_settleAt;
        private int _displayBoardCount,_scheduledPlayer=-1,_scheduledRevision=-1;
        private bool _paused,_settled,_editingRaise;
        private int _hubReportedHand=-1,_humanHandStartingStack;
        private Vector2 _lastSize;
        private System.Random _random;

        private sealed class Seat
        {
            public RectTransform Root,Marker;
            public Image Glow,Portrait;
            public Text Name,Stack,Status,MarkerText,BetText;
            public CardView[] Cards;
            public PokerChipPile Chips,Bet;
            public CanvasGroup Group;
        }
        private sealed class ChipFlight
        {
            public PokerChipPile Pile;
            public Vector2 From,To;
            public float Started,Duration;
        }

        public void Configure(PokerMatchSettings settings,Action onExit) { _settings=settings;_onExit=onExit; }
        private bool Complete => _game!=null && (_game.Phase==PokerPhase.HandComplete || _game.Phase==PokerPhase.MatchComplete);
        private bool Covered => _paused || _help.IsOpen || _intro.gameObject.activeSelf;
        private bool Ready => _game!=null && !Covered && _clock>=_busyUntil && !Complete;

        private void Start()
        {
            _font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUi();Resize();StartMatch();_intro.gameObject.SetActive(true);Render();
        }

        private void StartMatch()
        {
            _hubReportedHand=-1;_humanHandStartingStack=_settings.StartingStack;
            _clock=0;_busyUntil=.5f;_paused=false;_pause.gameObject.SetActive(false);
            _intro.gameObject.SetActive(false);_help.Hide();ClearFlights();
            var count=Mathf.Clamp(_settings.PlayerCount,2,6);
            var seed=UnityEngine.Random.Range(0,int.MaxValue);
            _random=new System.Random(seed);_portraits=new Sprite[count];_bots=new SimplePokerBot[count];
            _portraits[0]=PlayerProfile.Avatar;
            if(_portraits[0]==null) _portraits[0]=Resources.Load<Sprite>("PlayerAvatars/yegor/yegor_01");
            var pool=AvatarCatalog.OpponentPortraits(_portraits[0],count-1);
            var names=new List<string>{PlayerProfile.DisplayName};
            var fallback=new[]{"Алексей","Вера","Борис","Ольга","Пётр"};
            for(var i=1;i<count;i++)
            {
                if(pool.Count>0)_portraits[i]=pool[(i-1)%pool.Count];
                var title=AvatarCatalog.PersonalTitleOf(_portraits[i])??fallback[i-1];
                var name=title;
                for(var suffix=2;names.Contains(name);suffix++)name=title+" "+suffix;
                names.Add(name);_bots[i]=new SimplePokerBot(unchecked(seed+i*7919));
            }
            HubRoster.TryApply("poker",names,_portraits);
            _game=new PokerGame(names,seed,_settings.StartingStack,_settings.SmallBlind,_settings.BigBlind);
            BuildSeats();ResetHandPresentation();Render();
        }

        private void ResetHandPresentation()
        {
            _displayBoardCount=0;_settled=false;_scheduledPlayer=-1;_scheduledRevision=-1;
            _busyUntil=_clock+.8f;_revealAt=_clock+.6f;_settleAt=float.PositiveInfinity;
            _actionText.text="Блайнды поставлены. Карты розданы.";
            _resultText.text="";
            foreach(var seat in _seats)seat.Status.text="";
            if(Complete)_settleAt=_clock+1.8f;
            ResetRaise();
        }

        private void Update()
        {
            Resize();Tick(Time.unscaledDeltaTime);
        }

        // One pausable clock owns thinking, chip movement and the staggered board reveal.
        private void Tick(float deltaTime)
        {
            if(_game==null || Covered)return;
            _clock+=deltaTime;
            for(var i=_chipFlights.Count-1;i>=0;i--)
            {
                var f=_chipFlights[i];var t=Mathf.Clamp01((_clock-f.Started)/f.Duration);
                f.Pile.Root.anchoredPosition=Vector2.Lerp(f.From,f.To,Mathf.SmoothStep(0,1,t))
                    +Vector2.up*(Mathf.Sin(t*Mathf.PI)*20);
                if(t>=1){f.Pile.Root.gameObject.SetActive(false);Destroy(f.Pile.Root.gameObject);_chipFlights.RemoveAt(i);}
            }
            if(_displayBoardCount<_game.Board.Count && _clock>=_revealAt)
            {
                // Flop is three together, then turn and river individually, including an all-in runout.
                _displayBoardCount=Mathf.Min(_game.Board.Count,_displayBoardCount<3?3:_displayBoardCount+1);
                _revealAt=_clock+.85f;
                _busyUntil=_clock+.75f;
                if(Complete && _displayBoardCount==_game.Board.Count)_settleAt=_clock+1.4f;
                Render();
            }
            if(Complete && !_settled && _displayBoardCount==_game.Board.Count && _clock>=_settleAt)
            {
                _settled=true;
                for(var i=0;i<_game.Players.Count;i++)
                    if(_game.Winnings[i]>0)Fly(_game.Winnings[i],new Vector2(0,130),StackPosition(i),.75f);
                _busyUntil=_clock+.8f;Render();
            }
            RefreshControls();
            if(!Ready || _displayBoardCount!=_game.Board.Count || _game.CurrentPlayer<=0)return;
            if(_scheduledRevision!=_game.Revision || _scheduledPlayer!=_game.CurrentPlayer)
            {
                _scheduledPlayer=_game.CurrentPlayer;_scheduledRevision=_game.Revision;
                _thinkAt=_clock+.85f+(float)_random.NextDouble()*.85f;
            }
            if(_clock<_thinkAt)return;
            var player=_game.CurrentPlayer;var move=_bots[player].Choose(_game,player);
            if(!PlayAction(player,move.Action,move.RaiseTo))
                PlayAction(player,_game.ToCall(player)==0?PokerAction.Check:PokerAction.Call,0);
        }

        private bool PlayAction(int player,PokerAction action,int raiseTo=0)
        {
            if(!Ready || _game.CurrentPlayer!=player || _displayBoardCount!=_game.Board.Count)return false;
            var previousStack=_game.Players[player].Stack;
            var oldBets=new int[_seats.Length];
            for(var i=0;i<oldBets.Length;i++)oldBets[i]=_game.Players[i].RoundBet;
            var oldPhase=_game.Phase;
            if(!_game.TryAct(player,action,raiseTo))return false;
            var paid=Math.Max(0,previousStack-_game.Players[player].Stack+(Complete?_game.Winnings[player]:0));
            var prefix=_game.Players[player].Name+": ";
            _seats[player].Status.text=_game.LastAction.StartsWith(prefix,StringComparison.Ordinal)
                ?_game.LastAction.Substring(prefix.Length):_game.LastAction;
            _actionText.text=_game.LastAction;
            _busyUntil=_clock+.55f;
            if(_game.Phase!=oldPhase)
            {
                oldBets[player]+=paid;
                for(var i=0;i<oldBets.Length;i++)
                {
                    var collected=Complete?_game.Players[i].RoundBet:oldBets[i];
                    if(collected>0)Fly(collected,_betPositions[i],new Vector2(0,130),.6f);
                    if(Complete && oldBets[i]>collected)Fly(oldBets[i]-collected,_betPositions[i],StackPosition(i),.6f);
                }
                _busyUntil=_clock+.9f;_revealAt=_clock+.7f;
                if(Complete)_settleAt=_game.Board.Count==_displayBoardCount?_clock+1.7f:float.PositiveInfinity;
            }
            else if(paid>0)Fly(paid,StackPosition(player),_betPositions[player],.48f);
            ResetRaise();Render();return true;
        }

        private void HumanAction(PokerAction action)
        { PlayAction(0,action,action==PokerAction.Raise?Mathf.RoundToInt(_raiseSlider.value):0); }

        private void NextHand()
        {
            if(!Complete || !_settled || Covered || _clock<_busyUntil)return;
            if(_game.Phase==PokerPhase.MatchComplete){StartMatch();return;}
            var startingStack=_game.Players[0].Stack;
            if(!_game.StartNextHand())return;
            _humanHandStartingStack=startingStack;
            ClearFlights();ResetHandPresentation();Render();
        }

        private void SetPaused(bool paused)
        { _paused=paused;_pause.gameObject.SetActive(paused);RefreshControls(); }

        private void Render()
        {
            if(_game==null)return;
            if(Complete && _hubReportedHand!=_game.HandNumber)
            {
                _hubReportedHand=_game.HandNumber;
                if(_game.Players[0].InHand)
                    HubMatchReporter.ReportMatchCompleted("poker",_game.Players[0].Stack>_humanHandStartingStack,
                        draw:_game.Players[0].Stack==_humanHandStartingStack);
            }
            _roundText.text="РАЗДАЧА "+_game.HandNumber+"     •     БЛАЙНДЫ "+_game.SmallBlind+" / "+_game.BigBlind;
            var bank=_game.Pot;
            if(Complete && !_settled){bank=0;foreach(var win in _game.Winnings)bank+=win;}
            _potText.text=bank>0?"БАНК  "+PokerUi.Money(bank):"TEXAS HOLD’EM";
            var uncollected=0;
            if(!Complete)foreach(var p in _game.Players)uncollected+=p.RoundBet;
            _potChips.SetAmount(Math.Max(0,bank-uncollected));
            var pots=new List<string>();
            if(_game.Pots.Count>1)
                for(var i=0;i<_game.Pots.Count;i++)pots.Add((i==0?"Основной ":"Боковой "+i+": ")+PokerUi.Money(_game.Pots[i].Amount));
            _potsText.text=string.Join("   ·   ",pots);
            var showdown=Complete && Contenders()>1;
            for(var id=0;id<_seats.Length;id++)
            {
                var p=_game.Players[id];var seat=_seats[id];
                var acting=_game.CurrentPlayer==id && !Complete;
                seat.Glow.color=acting?PokerUi.Gold:Color.clear;
                seat.Name.color=acting?PokerUi.Gold:PokerUi.Paper;
                var stack=p.Stack-(Complete && !_settled?_game.Winnings[id]:0);
                seat.Stack.text=PokerUi.Money(stack);
                seat.Chips?.SetAmount(stack);
                seat.Group.alpha=!p.InHand ? .3f : p.Folded ? .53f : 1f;
                if(!p.InHand)seat.Status.text="Нет фишек";
                else if(p.Folded)seat.Status.text="Фолд";
                else if(p.AllIn && !Complete)seat.Status.text="ОЛЛ-ИН";
                if(Complete && _settled && _game.Winnings[id]>0)
                {
                    seat.Status.text="+"+PokerUi.Money(_game.Winnings[id]);
                    if(showdown)
                    {
                        var winningHand=new List<Card>(p.HoleCards);winningHand.AddRange(_game.Board);
                        seat.Status.text+=" · "+PokerHandEvaluator.Evaluate(winningHand).Name;
                    }
                }
                seat.Status.color=Complete && _settled && _game.Winnings[id]>0?PokerUi.Gold:PokerUi.Muted;
                seat.Marker.gameObject.SetActive(id==_game.Dealer || (p.InHand && (id==_game.SmallBlindPlayer || id==_game.BigBlindPlayer)));
                seat.MarkerText.text=id==_game.Dealer?(id==_game.SmallBlindPlayer?"D/S":"D"):id==_game.SmallBlindPlayer?"SB":"BB";
                seat.Bet.SetAmount(Complete?0:p.RoundBet);
                seat.BetText.text=!Complete && p.RoundBet>0?PokerUi.Money(p.RoundBet):"";
                if(id==0)continue;
                for(var n=0;n<2;n++)
                {
                    var visible=p.InHand && p.HoleCards.Count==2 && !p.Folded;
                    seat.Cards[n].gameObject.SetActive(visible);
                    if(!visible)continue;
                    if(showdown)seat.Cards[n].ShowFace(p.HoleCards[n]);else seat.Cards[n].ShowBack();
                }
            }
            var human=_game.Players[0];
            _humanChips.SetAmount(human.Stack-(Complete && !_settled?_game.Winnings[0]:0));
            for(var i=0;i<2;i++)
            {
                _humanCards[i].gameObject.SetActive(human.InHand && human.HoleCards.Count==2);
                if(human.HoleCards.Count==2)_humanCards[i].ShowFace(human.HoleCards[i]);
                _humanCards[i].GetComponent<CanvasGroup>().alpha=human.Folded ? .4f : 1f;
            }
            for(var i=0;i<5;i++)
            {
                _boardCards[i].gameObject.SetActive(i<_displayBoardCount);
                if(i<_displayBoardCount)_boardCards[i].ShowFace(_game.Board[i]);
            }
            _handText.text=!human.InHand?"Ты выбыл · можно смотреть дальше":human.Folded?"Ты сбросил карты":"ТВОИ КАРТЫ";
            if(human.InHand && !human.Folded && _displayBoardCount>=3)
            {
                var known=new List<Card>(human.HoleCards);
                for(var i=0;i<_displayBoardCount;i++)known.Add(_game.Board[i]);
                _handText.text=PokerHandEvaluator.Evaluate(known).Name;
            }
            _resultText.text="";
            if(Complete && _settled)
            {
                var wins=new List<string>();
                var winnerCount=0;
                foreach(var payout in _game.Winnings)if(payout>0)winnerCount++;
                for(var i=0;i<_seats.Length;i++)if(_game.Winnings[i]>0)
                {
                    var category="";
                    if(showdown && winnerCount<=2)
                    {
                        var known=new List<Card>(_game.Players[i].HoleCards);known.AddRange(_game.Board);
                        category=" · "+PokerHandEvaluator.Evaluate(known).Name;
                    }
                    wins.Add(_game.Players[i].Name+" +"+PokerUi.Money(_game.Winnings[i])+category);
                }
                _resultText.text=string.Join(winnerCount>2?"    ·    ":"\n",wins);
                _actionText.text=showdown?"ВСКРЫТИЕ":"Все остальные сбросили карты";
            }
            RefreshControls();
        }

        private int Contenders()
        { var n=0;foreach(var p in _game.Players)if(p.InHand && !p.Folded)n++;return n; }

        private void RefreshControls()
        {
            if(_game==null)return;
            var can=Ready && _game.CurrentPlayer==0 && _displayBoardCount==_game.Board.Count;
            var owed=_game.ToCall(0);var p=_game.Players[0];
            _foldButton.interactable=can;_callButton.interactable=can;
            _callText.text=owed==0?"Чек":p.Stack<=owed?"Колл · олл-ин\n"+PokerUi.Money(p.Stack):"Колл  "+PokerUi.Money(owed);
            var canRaise=can && _game.CanRaise(0);
            _raiseButton.interactable=canRaise && p.Stack+p.RoundBet>=_game.MinimumRaiseTo;
            _raiseText.text=_game.CurrentBet==0?"Ставка":"Рейз";
            _allInButton.interactable=can && (p.Stack<=owed || canRaise);
            _raiseSlider.interactable=_raiseInput.interactable=_raiseButton.interactable;
            foreach(var button in _raiseQuick)button.interactable=_raiseButton.interactable;
            var finished=Complete && _settled;
            _nextButton.gameObject.SetActive(finished);
            _nextButton.interactable=finished && _clock>=_busyUntil && !Covered;
            _nextText.text=_game.Phase==PokerPhase.MatchComplete?"Новая партия":p.Stack==0?"Смотреть дальше":"Следующая раздача";
            _foldButton.gameObject.SetActive(!finished);_callButton.gameObject.SetActive(!finished);
            _raiseButton.gameObject.SetActive(!finished);_allInButton.gameObject.SetActive(!finished);_raiseArea.gameObject.SetActive(!finished);
            _turnText.text=Complete?(_settled?(_game.Phase==PokerPhase.MatchComplete?"ПАРТИЯ ЗАВЕРШЕНА":"РАЗДАЧА ЗАВЕРШЕНА"):
                _displayBoardCount<_game.Board.Count?"ОТКРЫВАЕМ СТОЛ":"ВСКРЫТИЕ"):
                _displayBoardCount<_game.Board.Count?"ОТКРЫВАЕМ "+PhaseName():
                _game.CurrentPlayer==0?"ТВОЙ ХОД":_game.CurrentPlayer>=0?"ХОД: "+_game.Players[_game.CurrentPlayer].Name.ToUpperInvariant():"";
            _turnText.color=can?PokerUi.Gold:PokerUi.Paper;
        }

        private string PhaseName()=>_game.Phase==PokerPhase.Preflop?"ПРЕФЛОП":_game.Phase==PokerPhase.Flop?"ФЛОП":
            _game.Phase==PokerPhase.Turn?"ТЁРН":_game.Phase==PokerPhase.River?"РИВЕР":"СТОЛ";

        private void ResetRaise()
        {
            _editingRaise=true;
            var max=_game.Players[0].Stack+_game.Players[0].RoundBet;
            var min=Math.Min(max,_game.MinimumRaiseTo);
            _raiseSlider.minValue=min;_raiseSlider.maxValue=Math.Max(min,max);
            _raiseSlider.value=min;_raiseInput.text=min.ToString();_editingRaise=false;
        }
        private void SetRaise(int value)
        {
            _raiseSlider.value=Mathf.Clamp(value,_raiseSlider.minValue,_raiseSlider.maxValue);
            _raiseInput.text=Mathf.RoundToInt(_raiseSlider.value).ToString();
        }
        private Vector2 StackPosition(int id)=>id==0
            ?new Vector2(255,170-_stage.rect.height*.5f)
            :_seatPositions[id]+new Vector2(98,25);
        private void Fly(int amount,Vector2 from,Vector2 to,float duration)
        {
            if(amount<=0)return;
            var pile=PokerChipPile.Create(_fxLayer,_font,from,.58f,false);pile.SetAmount(amount);
            _chipFlights.Add(new ChipFlight{Pile=pile,From=from,To=to,Started=_clock,Duration=duration});
        }
        private void ClearFlights()
        {
            foreach(var f in _chipFlights){f.Pile.Root.gameObject.SetActive(false);Destroy(f.Pile.Root.gameObject);}
            _chipFlights.Clear();
        }

        private void BuildSeats()
        {
            for(var i=_seatLayer.childCount-1;i>=0;i--){var child=_seatLayer.GetChild(i).gameObject;child.SetActive(false);Destroy(child);}
            var count=_game.Players.Count;_seats=new Seat[count];_seatPositions=new Vector2[count];_betPositions=new Vector2[count];
            LayoutSeats();
            for(var id=0;id<count;id++)
            {
                var seat=new Seat();_seats[id]=seat;
                seat.Root=PokerUi.Rect(_seatLayer,"Player "+id,_seatPositions[id],new Vector2(220,220));
                seat.Group=seat.Root.gameObject.AddComponent<CanvasGroup>();seat.Group.blocksRaycasts=false;
                seat.Glow=PokerUi.Box(seat.Root,"Turn underline",new Vector2(0,-61),new Vector2(80,3),Color.clear,1);
                var portrait=PokerUi.Rect(seat.Root,"Portrait",new Vector2(0,47),new Vector2(100,98)).gameObject.AddComponent<Image>();
                portrait.sprite=_portraits[id];portrait.preserveAspect=true;portrait.raycastTarget=false;
                if(portrait.sprite==null)portrait.color=new Color(.4f,.55f,.43f);
                seat.Portrait=portrait;
                var name=_game.Players[id].Name;
                if(id==0 && !string.Equals(name,"Ты",StringComparison.OrdinalIgnoreCase))name+=" · ты";
                seat.Name=PokerUi.Label(seat.Root,_font,name,20,new Vector2(0,-18),new Vector2(200,27),PokerUi.Paper);
                seat.Stack=PokerUi.Label(seat.Root,_font,"",20,new Vector2(0,-43),new Vector2(170,25),PokerUi.Gold);
                seat.Status=PokerUi.Label(seat.Root,_font,"",14,new Vector2(0,id==0?-78:-137),new Vector2(230,30),PokerUi.Muted);
                if(id>0 && Mathf.Abs(_seatPositions[id].x)<50)
                    seat.Status.rectTransform.anchoredPosition=new Vector2(-155,-72);
                seat.Marker=PokerUi.Box(seat.Root,"Dealer or blind",new Vector2(-88,-36),new Vector2(30,30),PokerUi.Paper,30).rectTransform;
                seat.MarkerText=PokerUi.Label(seat.Marker,_font,"",13,Vector2.zero,new Vector2(30,30),PokerUi.Dark);
                if(id>0)
                {
                    seat.Chips=PokerChipPile.Create(seat.Root,_font,new Vector2(98,25),.6f,false);
                    seat.Cards=new CardView[2];
                    for(var n=0;n<2;n++)
                    {
                        seat.Cards[n]=CardView.Create(seat.Root,_font,.37f);seat.Cards[n].ShowBack();seat.Cards[n].SetPlain();
                        seat.Cards[n].Rect.anchoredPosition=new Vector2((n-.5f)*36,-91);
                        seat.Cards[n].Rect.localRotation=Quaternion.Euler(0,0,n==0?7:-7);
                    }
                }
                seat.Bet=PokerChipPile.Create(_seatLayer,_font,_betPositions[id],.56f,false);
                seat.BetText=PokerUi.Label(_seatLayer,_font,"",15,_betPositions[id]+new Vector2(0,-21),new Vector2(130,22),PokerUi.Gold);
            }
        }

        private void LayoutSeats()
        {
            if(_seats==null)return;
            var count=_seats.Length;
            var extra=(_stage.rect.size-new Vector2(1600,900))*.5f;
            _seatPositions[0]=new Vector2(-255-extra.x*.35f,-260-extra.y);
            _betPositions[0]=new Vector2(0,-137-extra.y*.5f);
            Vector2[] positions;
            if(count==2)positions=new[]{new Vector2(0,280)};
            else if(count==3)positions=new[]{new Vector2(-475,155),new Vector2(475,155)};
            else if(count==4)positions=new[]{new Vector2(-610,10),new Vector2(0,280),new Vector2(610,10)};
            else if(count==5)positions=new[]{new Vector2(-620,-25),new Vector2(-360,250),new Vector2(360,250),new Vector2(620,-25)};
            else positions=new[]{new Vector2(-620,-25),new Vector2(-440,220),new Vector2(0,280),new Vector2(440,220),new Vector2(620,-25)};
            for(var i=1;i<count;i++)
            {
                _seatPositions[i]=positions[i-1];
                var p=_seatPositions[i];
                var outer=Mathf.Abs(p.x)>550;
                p.x+=Mathf.Sign(p.x)*(Mathf.Abs(p.x)<50?0:extra.x*(outer?1:.55f));
                p.y+=p.y>100?extra.y:0;
                _seatPositions[i]=p;
                _betPositions[i]=Mathf.Abs(p.x)<50?new Vector2(195,218+extra.y):
                    new Vector2(p.x*.71f,p.y*.52f-15);
            }
            for(var id=0;id<count;id++)
            {
                var seat=_seats[id];if(seat==null)continue;
                seat.Root.anchoredPosition=_seatPositions[id];
                seat.Bet.Root.anchoredPosition=_betPositions[id];
                seat.BetText.rectTransform.anchoredPosition=_betPositions[id]+new Vector2(0,-21);
            }
        }

        private void BuildUi()
        {
            var background=PokerUi.Rect(transform,"Full-screen poker felt",Vector2.zero,Vector2.zero).gameObject.AddComponent<PokerTableArt>();
            background.raycastTarget=false;
            PokerUi.Stretch(background.rectTransform);
            _stage=PokerUi.Rect(transform,"Poker stage",Vector2.zero,new Vector2(1600,900));
            PokerUi.Label(_stage,_font,"NO LIMIT",13,new Vector2(0,-71),new Vector2(150,22),new Color(.7f,.78f,.65f,.23f));
            Anchor((RectTransform)PokerUi.Button(_stage,_font,"В меню",Vector2.zero,new Vector2(146,48),PokerUi.Dark,()=>_onExit?.Invoke()).transform,
                new Vector2(0,1),new Vector2(111,-51));
            Anchor(PokerUi.Label(_stage,_font,"TEXAS HOLD’EM",25,Vector2.zero,new Vector2(330,44),PokerUi.Gold).rectTransform,
                new Vector2(0,1),new Vector2(370,-50));
            _roundText=PokerUi.Label(_stage,_font,"",15,new Vector2(20,400),new Vector2(480,35),PokerUi.Muted);
            Anchor(_roundText.rectTransform,new Vector2(.5f,1),new Vector2(20,-50));
            Anchor((RectTransform)PokerUi.Button(_stage,_font,"Комбинации",Vector2.zero,new Vector2(172,48),PokerUi.Dark,()=>{_help.Show();RefreshControls();}).transform,
                Vector2.one,new Vector2(-290,-51));
            Anchor((RectTransform)PokerUi.Button(_stage,_font,"Пауза",Vector2.zero,new Vector2(126,48),PokerUi.Dark,()=>SetPaused(true)).transform,
                Vector2.one,new Vector2(-103,-51));
            _seatLayer=PokerUi.Rect(_stage,"Seats",Vector2.zero,Vector2.zero);
            for(var i=0;i<5;i++)
            {
                var pos=new Vector2((i-2)*109,20);
                PokerUi.Box(_stage,"Board mark "+i,pos,new Vector2(96,134),new Color(.76f,.72f,.49f,.23f),7);
                PokerUi.Box(_stage,"Empty card slot",pos,new Vector2(93,131),new Color(.055f,.22f,.165f),6);
                _boardCards[i]=CardView.Create(_stage,_font,.74f);_boardCards[i].Rect.anchoredPosition=pos;_boardCards[i].SetPlain();
            }
            PokerUi.Label(_stage,_font,"ФЛОП",11,new Vector2(-109,-59),new Vector2(300,18),PokerUi.Muted);
            PokerUi.Label(_stage,_font,"ТЁРН",11,new Vector2(109,-59),new Vector2(96,18),PokerUi.Muted);
            PokerUi.Label(_stage,_font,"РИВЕР",11,new Vector2(218,-59),new Vector2(96,18),PokerUi.Muted);
            _potChips=PokerChipPile.Create(_stage,_font,new Vector2(-82,132),.72f,false);
            _potText=PokerUi.Label(_stage,_font,"",22,new Vector2(85,132),new Vector2(240,34),PokerUi.Gold);
            _potsText=PokerUi.Label(_stage,_font,"",12,new Vector2(0,106),new Vector2(580,28),PokerUi.Muted);
            _actionText=PokerUi.Label(_stage,_font,"",16,new Vector2(0,-96),new Vector2(690,33),PokerUi.Muted);
            _resultText=PokerUi.Label(_stage,_font,"",19,new Vector2(0,-141),new Vector2(680,82),PokerUi.Gold);
            _resultText.resizeTextForBestFit=true;_resultText.resizeTextMinSize=13;_resultText.resizeTextMaxSize=19;
            _turnText=PokerUi.Label(_stage,_font,"",15,new Vector2(-430,350),new Vector2(400,30),PokerUi.Paper);
            Anchor(_turnText.rectTransform,new Vector2(0,1),new Vector2(370,-100));
            for(var i=0;i<2;i++)
            {
                _humanCards[i]=CardView.Create(_stage,_font,.84f);_humanCards[i].SetPlain();
                Anchor(_humanCards[i].Rect,new Vector2(.5f,0),new Vector2((i-.5f)*115,187));
                _humanCards[i].Rect.localRotation=Quaternion.Euler(0,0,i==0?3:-3);
            }
            _handText=PokerUi.Label(_stage,_font,"ТВОИ КАРТЫ",14,new Vector2(0,-181),new Vector2(360,24),PokerUi.Gold);
            Anchor(_handText.rectTransform,new Vector2(.5f,0),new Vector2(0,269));
            _humanChips=PokerChipPile.Create(_stage,_font,new Vector2(255,-280),1.08f,true);
            Anchor(_humanChips.Root,new Vector2(.5f,0),new Vector2(255,170));
            Anchor(PokerUi.Label(_stage,_font,"ТВОИ ФИШКИ",12,Vector2.zero,new Vector2(240,24),PokerUi.Muted).rectTransform,
                new Vector2(.5f,0),new Vector2(255,132));
            BuildControls();
            _fxLayer=PokerUi.Rect(_stage,"Flying chips",Vector2.zero,Vector2.zero);
            _pause=Modal("Пауза", "Раздача приостановлена", "Продолжить", ()=>SetPaused(false));
            _pause.gameObject.SetActive(false);
            _intro=Modal("Техасский холдем", "2 карты в руке + 5 на столе\nСобери лучшую комбинацию из пяти карт.\n\nУ каждого 2 000 фишек · блайнды 10 / 20", "Начать раздачу",()=>{_intro.gameObject.SetActive(false);Render();});
            _help=PokerHelpOverlay.Create(transform,_font,RefreshControls);
        }

        private void BuildControls()
        {
            _controls=PokerUi.Box(_stage,"Betting controls",new Vector2(0,-397),new Vector2(1530,94),new Color(.025f,.065f,.05f,.97f),12).rectTransform;
            _controls.anchorMin=Vector2.zero;_controls.anchorMax=new Vector2(1,0);
            _controls.sizeDelta=new Vector2(-70,94);_controls.anchoredPosition=new Vector2(0,53);
            _foldButton=PokerUi.Button(_controls,_font,"Фолд",new Vector2(-627,0),new Vector2(158,55),new Color(.36f,.16f,.145f),()=>HumanAction(PokerAction.Fold));
            _callButton=PokerUi.Button(_controls,_font,"Чек",new Vector2(-429,0),new Vector2(210,55),new Color(.17f,.38f,.28f),()=>HumanAction(_game.ToCall(0)==0?PokerAction.Check:PokerAction.Call));
            _callText=_callButton.GetComponentInChildren<Text>();
            _raiseButton=PokerUi.Button(_controls,_font,"Рейз",new Vector2(422,0),new Vector2(194,55),new Color(.49f,.36f,.15f),()=>HumanAction(PokerAction.Raise));
            _raiseText=_raiseButton.GetComponentInChildren<Text>();
            _allInButton=PokerUi.Button(_controls,_font,"Олл-ин",new Vector2(635,0),new Vector2(173,55),new Color(.48f,.21f,.2f),()=>HumanAction(PokerAction.AllIn));
            Anchor((RectTransform)_foldButton.transform,new Vector2(0,.5f),new Vector2(138,0));
            Anchor((RectTransform)_callButton.transform,new Vector2(0,.5f),new Vector2(336,0));
            Anchor((RectTransform)_raiseButton.transform,new Vector2(1,.5f),new Vector2(-343,0));
            Anchor((RectTransform)_allInButton.transform,new Vector2(1,.5f),new Vector2(-130,0));
            _raiseArea=PokerUi.Rect(_controls,"Raise amount",Vector2.zero,new Vector2(580,90));
            PokerUi.Label(_raiseArea,_font,"ДО",12,new Vector2(-128,24),new Vector2(55,27),PokerUi.Muted);
            var field=PokerUi.Box(_raiseArea,"Raise input",new Vector2(-28,24),new Vector2(135,28),new Color(.09f,.17f,.13f),6);
            field.raycastTarget=true;_raiseInput=field.gameObject.AddComponent<InputField>();_raiseInput.targetGraphic=field;
            _raiseInput.textComponent=PokerUi.Label(field.transform,_font,"40",18,Vector2.zero,new Vector2(123,26),PokerUi.Gold);
            _raiseInput.contentType=InputField.ContentType.IntegerNumber;
            _raiseInput.onEndEdit.AddListener(text=>{if(int.TryParse(text,out var value))SetRaise(value);else SetRaise(Mathf.RoundToInt(_raiseSlider.value));});
            var sliderRoot=PokerUi.Rect(_raiseArea,"Raise slider",new Vector2(-35,-5),new Vector2(370,18));
            var sliderHit=sliderRoot.gameObject.AddComponent<Image>();sliderHit.color=Color.clear;
            _raiseSlider=sliderRoot.gameObject.AddComponent<Slider>();_raiseSlider.wholeNumbers=true;
            PokerUi.Box(sliderRoot,"Track",Vector2.zero,new Vector2(370,5),new Color(.22f,.3f,.24f),2);
            var fillArea=PokerUi.Rect(sliderRoot,"Fill area",Vector2.zero,new Vector2(370,5));
            var fill=PokerUi.Box(fillArea,"Fill",Vector2.zero,Vector2.zero,PokerUi.Gold,2);PokerUi.Stretch(fill.rectTransform);_raiseSlider.fillRect=fill.rectTransform;
            var handleArea=PokerUi.Rect(sliderRoot,"Handle area",Vector2.zero,new Vector2(370,18));
            var handle=PokerUi.Box(handleArea,"Handle",Vector2.zero,new Vector2(18,18),PokerUi.Paper,20);handle.raycastTarget=true;
            _raiseSlider.handleRect=handle.rectTransform;_raiseSlider.targetGraphic=handle;
            _raiseSlider.onValueChanged.AddListener(value=>{if(!_editingRaise)_raiseInput.text=Mathf.RoundToInt(value).ToString();});
            _raiseQuick.Add(PokerUi.Button(_raiseArea,_font,"−",new Vector2(-253,-5),new Vector2(38,28),PokerUi.Dark,()=>SetRaise(Mathf.RoundToInt(_raiseSlider.value)-_game.BigBlind)));
            _raiseQuick.Add(PokerUi.Button(_raiseArea,_font,"+",new Vector2(184,-5),new Vector2(38,28),PokerUi.Dark,()=>SetRaise(Mathf.RoundToInt(_raiseSlider.value)+_game.BigBlind)));
            _raiseQuick.Add(PokerUi.Button(_raiseArea,_font,"Мин.",new Vector2(-167,-32),new Vector2(89,24),PokerUi.Dark,()=>SetRaise(_game.MinimumRaiseTo)));
            _raiseQuick.Add(PokerUi.Button(_raiseArea,_font,"½ банка",new Vector2(-47,-32),new Vector2(113,24),PokerUi.Dark,()=>SetRaise(_game.CurrentBet+Math.Max(_game.BigBlind,(_game.Pot+_game.ToCall(0))/2))));
            _raiseQuick.Add(PokerUi.Button(_raiseArea,_font,"Банк",new Vector2(79,-32),new Vector2(97,24),PokerUi.Dark,()=>SetRaise(_game.CurrentBet+_game.Pot+_game.ToCall(0))));
            foreach(var b in _raiseQuick)b.GetComponentInChildren<Text>().fontSize=13;
            _nextButton=PokerUi.Button(_controls,_font,"Следующая раздача",Vector2.zero,new Vector2(350,57),new Color(.42f,.32f,.14f),NextHand);
            _nextText=_nextButton.GetComponentInChildren<Text>();_nextButton.gameObject.SetActive(false);
        }

        private RectTransform Modal(string title,string detail,string button,Action action)
        {
            var root=PokerUi.Box(_stage,title,Vector2.zero,new Vector2(1600,900),new Color(0,0,0,.68f),0).rectTransform;
            PokerUi.Stretch(root);
            root.GetComponent<Image>().raycastTarget=true;
            PokerUi.Box(root,"Gold edge",Vector2.zero,new Vector2(660,420),PokerUi.Gold);
            PokerUi.Box(root,"Panel",Vector2.zero,new Vector2(655,415),PokerUi.Dark);
            PokerUi.Label(root,_font,title,36,new Vector2(0,130),new Vector2(620,58),PokerUi.Gold);
            PokerUi.Label(root,_font,detail,23,new Vector2(0,12),new Vector2(590,160),PokerUi.Paper);
            PokerUi.Button(root,_font,button,new Vector2(0,-130),new Vector2(320,57),new Color(.39f,.3f,.14f),action);
            return root;
        }

        private void Resize()
        {
            if(_stage==null)return;
            var size=((RectTransform)transform).rect.size;
            if(size==_lastSize)return;_lastSize=size;
            var scale=Mathf.Max(.01f,Mathf.Min(size.x/1600,size.y/900));
            _stage.localScale=Vector3.one*scale;
            _stage.sizeDelta=size/scale;
            LayoutSeats();
        }

        private static void Anchor(RectTransform rect,Vector2 anchor,Vector2 position)
        { rect.anchorMin=rect.anchorMax=anchor;rect.anchoredPosition=position; }
    }
}
