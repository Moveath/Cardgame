using System;
using System.Collections.Generic;
using System.Linq;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    public sealed partial class PlayerHubController
    {
        private readonly Dictionary<string,HubGameSettings> _gameSetupDrafts=new Dictionary<string,HubGameSettings>();
        private readonly Dictionary<string,int> _gameSetupPlayers=new Dictionary<string,int>();
        private string _setupMode="create";
        private HubGameSettings _editingGameSettings;

        private void PrepareGameSetup(string mode,string game)
        {
            _setupMode=mode;
            if(mode=="edit")
            {
                var room=CurrentRoom();
                if(room==null)return;
                _roomGame=room.GameId;_roomName=room.Name;_roomCapacity=room.MaxPlayers;
                _roomPrivate=room.Private;_roomStake=room.Stake.ToString();
                _editingGameSettings=(room.GameSettings??HubGameSettings.DefaultFor(_roomGame)).Copy();
                return;
            }
            _roomGame=string.IsNullOrEmpty(game)?"durak":game;
            EnsureGameSetupDraft(_roomGame);
            _roomCapacity=_gameSetupPlayers[_roomGame];
        }

        private void EnsureGameSetupDraft(string game)
        {
            if(!_gameSetupDrafts.ContainsKey(game))_gameSetupDrafts[game]=HubGameSettings.DefaultFor(game);
            if(!_gameSetupPlayers.ContainsKey(game))
            {
                switch(game)
                {
                    case "durak":_gameSetupPlayers[game]=DurakMatchSettings.Default.PlayerCount;break;
                    case "bluff":_gameSetupPlayers[game]=BluffMatchSettings.Default.PlayerCount;break;
                    case "chests":_gameSetupPlayers[game]=ChestsMatchSettings.Default.PlayerCount;break;
                    case "govno":_gameSetupPlayers[game]=GovnoMatchSettings.Default.PlayerCount;break;
                    default:_gameSetupPlayers[game]=PokerMatchSettings.Default.PlayerCount;break;
                }
            }
        }

        private HubGameSettings ActiveGameSetup()
        {
            if(_setupMode=="edit" && _editingGameSettings!=null)return _editingGameSettings;
            EnsureGameSetupDraft(_roomGame);
            return _gameSetupDrafts[_roomGame];
        }

        public HubGameSettings GetGameSetupSettings()=>ActiveGameSetup().Copy();
        public int GetGameSetupPlayerCount()=>_roomCapacity;

        private void BuildGameSetup(string mode)
        {
            _setupMode=mode;
            var editing=mode=="edit";var quick=mode=="quick";
            var room=editing?CurrentRoom():null;
            if(editing && (room==null || room.HostId!=_state.Profile.Id || room.Started))
            {_page=room==null?"rooms":"room";if(room==null)BuildRooms();else BuildRoom();return;}
            var settings=ActiveGameSetup();
            Title((quick?"Быстрая игра":editing?"Правила комнаты":"Создать комнату")+" · "+GameTitle(_roomGame),
                "Общие настройки игры: выбранные правила будут действовать за столом");
            Panel("Room parameters",0,0,435,548);
            Panel("Game parameters",455,0,705,548);
            Label(quick?"Партия с ботами":"Комната",22,19,391,38,28).color=GoldInk;
            Label("Правила игры",479,19,657,38,28).color=GoldInk;
            if(quick)
            {
                Label(GameTitle(_roomGame),22,87,391,76,27);
                Label("Ты + "+(_roomCapacity-1)+" ботов",22,181,391,37,24);
                Label("Настрой игру справа и сразу садись за стол. Эти же правила доступны при создании комнаты.",
                    22,242,391,132,21).color=MutedInk;
                Label("Выбор сохраняется отдельно для каждой игры.",22,433,391,77,19).color=MutedInk;
            }
            else
            {
                Label("Название",22,80,391,30,21);
                var name=Input(_roomName,22,118,391);name.characterLimit=32;
                name.onValueChanged.AddListener(v=>_roomName=v);
                Label("Доступ",22,190,391,30,21);
                Button(_roomPrivate?"Закрытая · по коду":"Открытая комната",22,229,391,49,
                    ()=>{_roomPrivate=!_roomPrivate;Render();});
                Label("Ставка в тун-тун коинах",22,311,391,31,21);
                var stake=Input(_roomStake,22,352,391);stake.contentType=InputField.ContentType.IntegerNumber;
                stake.onValueChanged.AddListener(v=>_roomStake=v);
                Label("0 — без ставки. При победе возврат ×2.\nФишки покера — отдельный счётчик.",22,428,391,78,18).color=MutedInk;
            }
            BuildGameRuleControls(settings,editing?room.Members.Count:0);
            Label(GameSetupSummary(_roomGame,settings,_roomCapacity),0,561,1160,42,19).color=MutedInk;
            if(quick)
                Button("Начать игру",0,611,410,45,()=>{if(_quickGame!=null)_quickGame(_roomGame);else _play?.Invoke();});
            else if(editing)
                Button("Сохранить настройки",0,611,410,45,SaveRoomSettings);
            else Button("Создать",0,611,410,45,CreateRoom);
            if(!quick && !editing)Button("К списку",435,611,300,45,ReturnToRooms);
            else Button(editing?"Отмена":"К выбору игры",435,611,300,45,Back);
        }

        // Колонка правил: левый край и ширина (на странице — справа от параметров комнаты, в табличке у стола —
        // во всю её ширину).
        private float _rulesLeft=479,_rulesWidth=657;

        /// <summary>
        /// Правила выбранной игры в колонке от <paramref name="left"/> шириной <paramref name="width"/> с высоты
        /// <paramref name="top"/>. Компактно — в табличке у стола: ряды теснее, без длинных пояснений.
        /// Возвращает, где колонка кончилась.
        /// </summary>
        private float BuildGameRuleControls(HubGameSettings settings,int occupiedSeats,float left=479,float width=657,float top=80,bool compact=false)
        {
            _rulesLeft=left;_rulesWidth=width;
            var y=top;
            Label("Игроков за столом",left,y,width,31,21);y+=39;
            var minimum=HubGameRules.MinimumPlayers(_roomGame);
            var decks=HubGameRules.DeckSizes(_roomGame);
            var capacity=decks.Select(deck=>
            {var candidate=settings.Copy();candidate.DeckSize=deck;return HubGameRules.MaximumPlayers(_roomGame,candidate);})
                .DefaultIfEmpty(HubGameRules.MaximumPlayers(_roomGame,settings)).Max();
            var choice=(width-(capacity-minimum)*12f)/(capacity-minimum+1);
            for(var n=minimum;n<=capacity;n++)
            {
                var count=n;
                SetupChoice(n.ToString(),_roomCapacity==n,left+(n-minimum)*(choice+12),y,choice,
                    ()=>SelectGameSetupPlayerCount(count),n>=occupiedSeats);
            }
            y+=compact?56:65;
            if(_roomGame=="durak" || _roomGame=="bluff" || _roomGame=="chests")
            {
                Label("Колода",left,y,width,31,21);y+=38;
                var deckWidth=(width-(decks.Length-1)*12f)/decks.Length;
                for(var i=0;i<decks.Length;i++)
                {
                    var deck=decks[i];var candidate=settings.Copy();candidate.DeckSize=deck;
                    SetupChoice(deck.ToString(),settings.DeckSize==deck,left+i*(deckWidth+12),y,deckWidth,
                        ()=>SelectGameSetupDeck(deck),HubGameRules.MaximumPlayers(_roomGame,candidate)>=occupiedSeats);
                }
                y+=compact?56:69;
            }
            var step=compact?54:70;
            switch(_roomGame)
            {
                case "durak":
                    SetupToggle("Перевод","Без перевода","Переводной",settings.AllowTransfer,y,v=>settings.AllowTransfer=v);y+=step;
                    SetupToggle("Подкидывают","Все","Соседи",settings.NeighboursOnly,y,v=>settings.NeighboursOnly=v);y+=step;
                    SetupToggle("Шулерство","Без","Включено",settings.AllowCheating,y,v=>settings.AllowCheating=v);y+=step;
                    break;
                case "bluff":
                    SetupToggle("Перестрелка","Без","Включена",settings.Shootout,y,v=>settings.Shootout=v);y+=step;
                    if(!compact)
                    {
                        Label("Каре сбрасывается автоматически.\nТузами заявляться нельзя. Проигрывает тот, у кого в конце больше тузов.",
                            left,y+17,width,105,20).color=MutedInk;
                        y+=122;
                    }
                    break;
                case "chests":
                    if(!compact)
                    {
                        Label("Спроси ранг, который есть у тебя, затем число карт и их масти. Угадал всё — забираешь карты и спрашиваешь снова.\nЧетыре карты одного ранга — сундучок.",
                            left,y+19,width,166,21).color=MutedInk;
                        y+=185;
                    }
                    break;
                case "govno":
                    SetupToggle("Карты в руке","На местах","Хаос",settings.Chaos,y,v=>settings.Chaos=v);y+=step;
                    var pick=new GovnoMatchSettings{Chaos=settings.Chaos};
                    Label(pick.PickSecondsText+" с на передачу карты · по 4 карты · 4 жизни",left,y+10,width,35,20).color=GoldInk;
                    y+=compact?46:69;
                    if(!compact)
                    {
                        Label("Собрал каре — нажми на центр. После чужого крика — хлопни по центру. Последний теряет жизнь.\nРанний хлопок блокирует руку на 1 секунду.",
                            left,y,width,139,21).color=MutedInk;
                        y+=139;
                    }
                    break;
                case "poker":
                    var poker=PokerMatchSettings.Default;
                    Label(poker.StartingStack.ToString("N0")+" фишек каждому",left,y+11,width,40,28).color=GoldInk;y+=compact?50:67;
                    Label("Блайнды "+poker.SmallBlind+" / "+poker.BigBlind+" · 52 карты · безлимитный холдем",left,y,width,39,21);
                    y+=compact?42:79;
                    if(!compact)
                    {
                        Label("Две карты на руки и пять общих на столе. Собери лучшую пятёрку.\nФолд, чек, колл, ставка, рейз и олл-ин.\nФишки сохраняются между раздачами.",
                            left,y,width,152,21).color=MutedInk;
                        y+=152;
                    }
                    break;
            }
            if(occupiedSeats>0)
                Label("Занято мест: "+occupiedSeats+". Для меньшей комнаты сначала убери игроков.",left,top+430,width,30,16).color=MutedInk;
            return y;
        }

        /// <summary>Вариант правила; выбранный — золотой доской (в клубе) или жёлтым с галочкой.</summary>
        private void SetupChoice(string text,bool selected,float x,float y,float width,Action action,bool enabled=true)
        {
            var button=Button((selected && !_clubStyle?"✓ ":"")+text,x,y,width,45,action,enabled);
            if(!selected)return;
            if(_clubStyle)ClubPaint(button,ClubTone.Primary);else MarkSelected(button);
        }

        private void SetupToggle(string title,string optionA,string optionB,bool value,float y,Action<bool> change)
        {
            var labelWidth=_rulesWidth*.355f;
            var choice=(_rulesWidth-labelWidth-24)/2;
            Label(title,_rulesLeft,y+6,labelWidth,32,21);
            SetupChoice(optionA,!value,_rulesLeft+labelWidth+12,y,choice,()=>{change(false);Render();});
            SetupChoice(optionB,value,_rulesLeft+labelWidth+24+choice,y,choice,()=>{change(true);Render();});
        }

        private void SelectGameSetupDeck(int deck)
        {
            var settings=ActiveGameSetup();settings.DeckSize=deck;
            _roomCapacity=Math.Min(_roomCapacity,HubGameRules.MaximumPlayers(_roomGame,settings));
            if(_setupMode!="edit")_gameSetupPlayers[_roomGame]=_roomCapacity;
            Render();
        }

        private void SelectGameSetupPlayerCount(int count)
        {
            var settings=ActiveGameSetup();
            if(count>HubGameRules.MaximumPlayers(_roomGame,settings))
            {
                foreach(var deck in HubGameRules.DeckSizes(_roomGame))
                {
                    var candidate=settings.Copy();candidate.DeckSize=deck;
                    if(HubGameRules.MaximumPlayers(_roomGame,candidate)>=count){settings.DeckSize=deck;break;}
                }
            }
            _roomCapacity=Mathf.Clamp(count,HubGameRules.MinimumPlayers(_roomGame),HubGameRules.MaximumPlayers(_roomGame,settings));
            if(_setupMode!="edit")_gameSetupPlayers[_roomGame]=_roomCapacity;
            Render();
        }

        private async void SaveRoomSettings()
        {
            if(!int.TryParse(_roomStake,out var stake) || stake<0){Toast("Введи неотрицательную целую ставку.");return;}
            var settings=GetGameSetupSettings();
            var result=await Dispatch("update_room_settings",_roomGame,_roomName,_roomCapacity,_roomPrivate,stake,settings);
            if(this==null || !result.Success)return;
            _gameSetupDrafts[_roomGame]=settings.Copy();_gameSetupPlayers[_roomGame]=_roomCapacity;
            Back();
        }

        /// <summary>Число со словом в нужной форме: 1 игрок, 3 игрока, 5 игроков; 24 карты, 36 карт.</summary>
        private static string Numbered(int value,string one,string few,string many)
        {
            var tens=value%100;var last=value%10;
            var word=tens>=11 && tens<=14?many:last==1?one:last>=2 && last<=4?few:many;
            return value+" "+word;
        }

        /// <summary>Коротко для строки настроек в табличке у стола: места, колода и главное правило.</summary>
        private static string TableSummary(string game,HubGameSettings settings,int players)
        {
            settings=settings??HubGameSettings.DefaultFor(game);
            var seats=Numbered(players,"игрок","игрока","игроков");
            var deck=Numbered(settings.DeckSize,"карта","карты","карт");
            switch(game)
            {
                case "durak":return seats+" · "+deck+" · "+(settings.AllowTransfer?"переводной":"подкидной")+(settings.AllowCheating?" · шулерство":"");
                case "bluff":return seats+" · "+deck+" · "+(settings.Shootout?"с перестрелкой":"без перестрелки");
                case "chests":return seats+" · "+deck;
                case "govno":return seats+" · "+(settings.Chaos?"хаос":"карты на местах");
                case "poker":return seats+" · "+PokerMatchSettings.Default.StartingStack.ToString("N0")+" фишек";
                default:return seats;
            }
        }

        public static string GameSetupSummary(string game,HubGameSettings settings,int players)
        {
            settings=settings??HubGameSettings.DefaultFor(game);
            var prefix=Numbered(players,"игрок","игрока","игроков");
            var deck=Numbered(settings.DeckSize,"карта","карты","карт");
            switch(game)
            {
                case "durak":return prefix+" · "+deck+" · "+(settings.AllowTransfer?"переводной":"без перевода")+
                    " · подкидывают "+(settings.NeighboursOnly?"соседи":"все")+(settings.AllowCheating?" · шулерство":"");
                case "bluff":return prefix+" · "+deck+" · "+(settings.Shootout?"перестрелка включена":"без перестрелки");
                case "chests":return prefix+" · "+deck+" · сундучки из четырёх карт одного ранга";
                case "govno":
                    var govno=new GovnoMatchSettings{Chaos=settings.Chaos};
                    return prefix+" · "+(players*4)+" карт · "+(settings.Chaos?"хаос":"на местах")+" · "+govno.PickSecondsText+" с на передачу";
                case "poker":
                    var poker=PokerMatchSettings.Default;
                    return prefix+" · 52 карты · "+poker.StartingStack.ToString("N0")+" фишек · блайнды "+poker.SmallBlind+" / "+poker.BigBlind;
                default:return prefix;
            }
        }
    }
}
