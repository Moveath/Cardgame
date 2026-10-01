using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CardGame.Core.Meta;
using UnityEngine;
using UnityEngine.UI;

namespace CardGameClient
{
    /// <summary>Functional local application shell. Pages know only snapshots and async commands.</summary>
    public sealed partial class PlayerHubController : MonoBehaviour
    {
        private static readonly Color HubGold=new Color(.96f,.81f,.37f);
        private static readonly Color HubPaper=new Color(.96f,.95f,1f);
        private static readonly Color HubMuted=new Color(.77f,.75f,.94f);
        private static readonly Color HubPanel=new Color(.09f,.06f,.25f,.8f);
        private Font _font;
        private RectTransform _stage,_content;
        private HubSnapshot _state;
        private Text _pageTitle,_pageSubtitle,_identity,_wallet,_level,_toast;
        private Image _portrait,_levelFill;
        private CanvasGroup _interaction;
        private readonly List<string> _history=new List<string>();
        private Button _backButton;
        private string _page="home";
        private bool _built,_busy;
        private Vector2 _size;
        private Action _play,_avatarPicker;
        private Action<string> _quickGame;
        private Action<string,int> _launch;
        private float _refreshAt;
        private string _roomGame="durak",_roomName="Мой стол",_roomCode="",_roomStake="0";
        private int _roomCapacity=4;
        private bool _roomPrivate;
        private string _roomChat="";
        private int _roomListPage;
        private string _selectedGame="";
        private CanvasScaler _menuScaler;
        private Canvas _menuCanvas;
        private CanvasScaler.ScaleMode _previousScaleMode;
        private CanvasScaler.ScreenMatchMode _previousMatchMode;
        private Vector2 _previousReference;
        private bool _previousPixelPerfect,_ownsCanvasScaling;

        public void Configure(Action play,Action avatarPicker,Action<string,int> launch,Action<string> quickGame=null)
        {_play=play;_avatarPicker=avatarPicker;_launch=launch;_quickGame=quickGame;}

        private async void Start()
        {
            BuildShell();_built=true;Resize();
            PlayerHubRuntime.Changed+=OnSnapshot;
            await Reload();
        }
        private void OnEnable()
        {
            UseMenuCanvasScaling();
            if(_built){if(PlayerHubRuntime.Current!=null)OnSnapshot(PlayerHubRuntime.Current);_refreshAt=Time.unscaledTime+1;}
        }
        private void OnDisable(){RestoreCanvasScaling();}
        private void OnDestroy(){RestoreCanvasScaling();PlayerHubRuntime.Changed-=OnSnapshot;}

        private void UseMenuCanvasScaling()
        {
            if(_ownsCanvasScaling)return;
            _menuScaler=GetComponentInParent<CanvasScaler>();
            if(_menuScaler==null)return;
            _menuCanvas=_menuScaler.GetComponent<Canvas>();
            if(_menuCanvas==null || _menuCanvas.renderMode==RenderMode.WorldSpace)return;
            _previousScaleMode=_menuScaler.uiScaleMode;
            _previousMatchMode=_menuScaler.screenMatchMode;
            _previousReference=_menuScaler.referenceResolution;
            _previousPixelPerfect=_menuCanvas.pixelPerfect;
            _ownsCanvasScaling=true;
            // Match the menu's actual design space. Expand keeps its 1600x900 stage at
            // unit scale, so dynamic glyphs are rasterized at their final pixel size.
            _menuScaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _menuScaler.referenceResolution=new Vector2(1600,900);
            _menuScaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
            _menuCanvas.pixelPerfect=true;
        }

        private void RestoreCanvasScaling()
        {
            if(!_ownsCanvasScaling)return;
            _ownsCanvasScaling=false;
            if(_menuScaler!=null)
            {
                _menuScaler.uiScaleMode=_previousScaleMode;
                _menuScaler.screenMatchMode=_previousMatchMode;
                _menuScaler.referenceResolution=_previousReference;
            }
            if(_menuCanvas!=null)_menuCanvas.pixelPerfect=_previousPixelPerfect;
        }
        private void Update()
        {
            Resize();
            UpdateToast();
            var selected=UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            var typing=selected!=null && selected.GetComponent<InputField>()!=null;
            UpdateCity(typing);
            if(_built && !_busy && !typing && (_page=="bank" || _page=="quests") && Time.unscaledTime>=_refreshAt)
            {_refreshAt=Time.unscaledTime+5;RefreshTimedPage();}
        }
        private async void RefreshTimedPage()
        {
            var result=await PlayerHubRuntime.RefreshAsync();
            if(this==null)return;
            if(!result.Success)Toast(PlayerHubRuntime.ErrorText(result));
        }

        private async Task Reload()
        {
            SetBusy(true);
            var result=await PlayerHubRuntime.LoadAsync();
            if(this==null)return;
            SetBusy(false);
            if(result.Success)
            {
                _state=result.Snapshot;
                Render();
            }
            else
            {
                Title("Профиль недоступен",PlayerHubRuntime.ErrorText(result));
                Button("Повторить",0,20,260,48,async()=>{await RetryLoad();});
            }
        }

        private async Task RetryLoad()
        {
            var result=await PlayerHubRuntime.RefreshAsync();
            if(this==null)return;
            if(result.Success){_state=result.Snapshot;Render();}else Toast(PlayerHubRuntime.ErrorText(result));
        }

        private void OnSnapshot(HubSnapshot snapshot)
        {
            if(this==null){PlayerHubRuntime.Changed-=OnSnapshot;return;}
            _state=snapshot;
            if(_built && isActiveAndEnabled && !_busy)Render();
        }

        public void Navigate(string page)
        {
            if(_busy)return;
            CloseOutdoorVisit();
            if(page=="home"){_history.Clear();_tableTrip=-1;}
            else if(page!=_page)
            {
                _history.Add(_page);
                if(_history.Count>32)_history.RemoveAt(0);
            }
            if(page.StartsWith("game:",StringComparison.Ordinal))_selectedGame=page.Substring(5);
            if(page=="create-room" || page=="quick-game" || page=="room-settings")
                PrepareGameSetup(page=="create-room"?"create":page=="quick-game"?"quick":"edit",
                    string.IsNullOrEmpty(_selectedGame)?_roomGame:_selectedGame);
            _page=page;
            if(_state!=null)Render();
        }

        public void Back()
        {
            if(_busy)return;
            if(OutdoorVisit && _visitPhase==2){CloseOutdoorVisit();return;}
            // На главной внутри здания «Назад» выводит на улицу.
            if(_page=="home"){LeaveInterior();return;}
            if(_history.Count>0)
            {var last=_history.Count-1;_page=_history[last];_history.RemoveAt(last);}
            else _page=ParentPage(_page);
            if(_page.StartsWith("game:",StringComparison.Ordinal))_selectedGame=_page.Substring(5);
            if(_state!=null)Render();
        }

        public void GoHome(){Navigate("home");}

        private void ReturnToRooms()
        {
            if(_busy)return;
            while(_history.Count>0 && (_history[_history.Count-1]=="rooms" || _history[_history.Count-1]=="create-room"))
                _history.RemoveAt(_history.Count-1);
            _page="rooms";
            if(_state!=null)Render();
        }

        private string ParentPage(string page)
        {
            if(page.StartsWith("game:",StringComparison.Ordinal) || IsRulesPage(page))return "games";
            if(page.StartsWith("message:",StringComparison.Ordinal))
            {
                var message=_state.Chats.FirstOrDefault(m=>m.Id==page.Substring(8));
                return message==null?"chats":"chat:"+message.ConversationId;
            }
            if(page.StartsWith("chat:",StringComparison.Ordinal))return "chats";
            if(page.StartsWith("friend:",StringComparison.Ordinal))return "friends";
            if(page.StartsWith("throw:",StringComparison.Ordinal))return "inventory";
            switch(page)
            {
                case "shop-catalog":return "shop";
                case "room":case "create-room":return "rooms";
                case "room-settings":return "room";
                case "quick-game":return "game:"+_roomGame;
                case "rooms":return string.IsNullOrEmpty(_selectedGame)?"games":"game:"+_selectedGame;
                case "quests":case "achievements":case "pass":case "ranks":case "tutorial":return "progress";
                case "inventory":return "profile";
                case "extras":return "settings";
                default:return "home";
            }
        }

        private void OpenAvatarPicker(){_avatarPicker?.Invoke();}
        private void OpenGames(){Navigate("games");}

        private async void Send(string action,string target="",string text="",int amount=0,bool flag=false)
        {await Dispatch(action,target,text,amount,flag);}

        private async Task<HubResult> Dispatch(string action,string target="",string text="",int amount=0,bool flag=false,int value=0,HubGameSettings gameSettings=null)
        {
            if(_busy)return new HubResult{Success=false,Error="Дождись текущего действия"};
            SetBusy(true);
            var result=await PlayerHubRuntime.ExecuteAsync(new HubCommand
            {Action=action,Target=target,Text=text,Amount=amount,Flag=flag,Value=value,GameSettings=gameSettings});
            if(this==null)return result;
            SetBusy(false);
            if(result.Success)
            {
                _state=result.Snapshot;
                if(action=="create_room" || action=="join_room" || action=="invite_accept")
                {
                    var joined=CurrentRoom();
                    if(joined!=null)
                    {
                        _selectedGame=joined.GameId;
                        if(_page=="create-room")_page="rooms";
                        if(_page!="room" && (_history.Count==0 || _history[_history.Count-1]!=_page))_history.Add(_page);
                        _page="room";
                    }
                }
                if(action=="leave_room")
                {
                    _history.RemoveAll(p=>p=="room" || p=="create-room" || p=="room-settings");
                    while(_history.Count>0 && _history[_history.Count-1]=="rooms")_history.RemoveAt(_history.Count-1);
                    _page="rooms";
                }
                Render();Toast(result.Message);
                if(action=="start_room")
                {
                    var room=CurrentRoom();
                    if(room!=null)
                    {HubRoster.Configure(_state,room.Id);PlayerHubRuntime.BeginGame(room.GameId,room.LocalMatchId);_launch?.Invoke(room.GameId,room.Members.Count);}
                }
                else if(action=="play_collectors")
                {HubRoster.Clear();PlayerHubRuntime.BeginGame(_state.Bank.CollectorGame,_state.ActiveMatchId);_launch?.Invoke(_state.Bank.CollectorGame,3);}
            }
            else Toast(PlayerHubRuntime.ErrorText(result));
            return result;
        }

        private void SetBusy(bool busy)
        {_busy=busy;if(_interaction!=null)_interaction.interactable=!busy;}
        private void Toast(string text)
        {
            if(_toast==null)return;
            _toast.text=text??"";
            _toastPill.gameObject.SetActive(!string.IsNullOrEmpty(text));
            _toastUntil=Time.unscaledTime+4f;
        }
        public void ShowMessage(string text){Toast(text);}

        private static void CurrencyIcon(Transform parent,Sprite sprite,float x,float y,float width,float height)
        {
            var icon=MakeRect(parent,"Currency icon",x,y,width,height).gameObject.AddComponent<Image>();
            icon.sprite=sprite;icon.preserveAspect=true;icon.raycastTarget=false;icon.enabled=sprite!=null;
        }

        private void Render()
        {
            if(_state==null || !_built)return;
            // Экраны клуба — доски и золото вывесок (PlayerHubClubStyle.cs), остальные разделы — прежний вид.
            _clubStyle=IsClubPage(_page);
            RememberScrolls();
            for(var i=_content.childCount-1;i>=0;i--)
            {
                var old=_content.GetChild(i).gameObject;old.SetActive(false);
                if(Application.isPlaying)Destroy(old);else DestroyImmediate(old);
            }
            if(_page=="home")BuildHome();
            else if(_page=="games")BuildGames();
            else if(_page.StartsWith("game:",StringComparison.Ordinal))BuildGameEntrance(_page.Substring(5));
            else if(IsRulesPage(_page))BuildGameRules(_page.Substring(6));
            else if(_page=="rooms")BuildRooms();
            else if(_page=="create-room")BuildCreateRoom();
            else if(_page=="quick-game")BuildGameSetup("quick");
            else if(_page=="room-settings")BuildGameSetup("edit");
            else if(_page=="room")BuildRoom();
            else if(!RenderFeaturePage(_page)){_page="home";BuildHome();}
            RefreshShell();
        }

        /// <summary>Плитка-переход в цвете раздела (<see cref="Accent"/>); справа круглая стрелка, за ней — масть игры.</summary>
        private void MenuTile(string title,string detail,float x,float y,float width,float height,Action action,string section,string glyph=null)
        {
            var accent=Accent(section);
            var tile=Button(title,x,y,width,height,action);
            tile.GetComponent<Image>().color=Tint(accent,.5f);
            var heading=tile.GetComponentInChildren<Text>();
            heading.fontSize=28;heading.alignment=TextAnchor.UpperLeft;heading.color=Color.white;
            heading.rectTransform.anchoredPosition=new Vector2(22,-20);
            heading.rectTransform.sizeDelta=new Vector2(width-110,42);
            if(glyph!=null)
            {
                var mark=MakeLabel(tile.transform,glyph,width-250,(height-150)/2,150,150,120);
                mark.alignment=TextAnchor.MiddleCenter;mark.color=new Color(1,1,1,.14f);
            }
            MakeLabel(tile.transform,detail,22,78,width-(glyph==null?110:270),height-84,19).color=new Color(1,1,1,.82f);
            var go=MakeRect(tile.transform,"Go",width-80,(height-56)/2,56,56).gameObject.AddComponent<Image>();
            go.sprite=UiTextures.Circle(128,Color.white);go.color=accent;go.raycastTarget=false;
            var arrow=MakeLabel(go.transform,"›",0,-4,56,60,48);
            arrow.color=IsLight(accent)?HubInk:Color.white;arrow.alignment=TextAnchor.MiddleCenter;arrow.fontStyle=FontStyle.Bold;
        }

        /// <summary>Список игр города (доска «ИГРЫ» в зале Клуба): нажатие ведёт к столу игры.</summary>
        private void BuildGames(){BuildClubGames();}

        /// <summary>
        /// Страница игры — табличка у её стола (PlayerHubClubStyle.cs). Камеры нет у стола — она едет
        /// к нему сама, табличка видна сразу.
        /// </summary>
        private void BuildGameEntrance(string game)
        {
            if(!HubGames.Contains(game))
            {_page="games";BuildGames();return;}
            _selectedGame=game;
            Title(GameTitle(game),GameBlurb(game));
            var table=TablePlace(game);
            if(!AtTable(table))GoToTable(table);
            BuildTablePlaque(game);
        }

        private static string GameTitle(string id)
        {
            switch(id){case "durak":return "Дурак";case "bluff":return "Верю — не верю";case "chests":return "Сундучки";
                case "govno":return "Говно";case "poker":return "Техасский холдем";default:return id;}
        }
        private HubRoom CurrentRoom()=>_state.Rooms.FirstOrDefault(r=>r.Id==_state.CurrentRoomId);

        private void BuildRooms()
        {
            Title("Комнаты"+(string.IsNullOrEmpty(_selectedGame)?"":" · "+GameTitle(_selectedGame)),"Локальные комнаты с ботами · приглашения и коды работают внутри этого профиля");
            Button("Создать комнату",0,0,310,50,()=>Navigate("create-room"));
            var code=Input(_roomCode,345,0,390);code.onValueChanged.AddListener(v=>_roomCode=v);
            Button("Войти по коду",765,0,370,50,()=>Send("join_room",text:_roomCode));
            var rooms=_state.Rooms.Where(r=>(!r.Private || r.Id==_state.CurrentRoomId) &&
                (string.IsNullOrEmpty(_selectedGame) || r.GameId==_selectedGame)).ToList();
            _roomListPage=Mathf.Clamp(_roomListPage,0,Math.Max(0,(rooms.Count-1)/5));
            for(var n=0;n<5;n++)
            {
                var i=_roomListPage*5+n;if(i>=rooms.Count)break;var room=rooms[i];var y=93+n*91;
                if(_clubStyle)Panel("Room row",-12,y-10,1160,84);
                Label(room.Name+"  ·  "+GameTitle(room.GameId),12,y,790,32,24);
                Label(room.Members.Count+" / "+room.MaxPlayers+"   ·   ставка "+room.Stake+"   ·   код "+room.Code+(room.Started?" · играют":""),
                    12,y+33,790,30,18).color=MutedInk;
                Button(room.Id==_state.CurrentRoomId?"В комнату":"Войти",850,y+5,285,51,
                    ()=>{if(room.Id==_state.CurrentRoomId)Navigate("room");else Send("join_room",room.Id);},!room.Started);
            }
            if(rooms.Count==0)Label("Комнат пока нет. Создай свою и добавь ботов.",0,150,1120,70,25);
            if(rooms.Count>5)
            {
                Button("Предыдущая",0,573,200,45,()=>{_roomListPage--;Render();},_roomListPage>0);
                Label((_roomListPage+1)+" / "+((rooms.Count+4)/5),235,575,180,35,21);
                Button("Следующая",450,573,200,45,()=>{_roomListPage++;Render();},(_roomListPage+1)*5<rooms.Count);
            }
        }

        private void BuildCreateRoom(){BuildGameSetup("create");}
        private static int MinPlayers(string game)=>HubGameRules.MinimumPlayers(game);
        private async void CreateRoom()
        {
            if(!int.TryParse(_roomStake,out var stake) || stake<0){Toast("Введи неотрицательную целую ставку.");return;}
            // Creation is one command: room and stake either both succeed or neither does.
            await Dispatch("create_room",_roomGame,_roomName,_roomCapacity,_roomPrivate,stake,GetGameSetupSettings());
        }

        private void BuildRoom()
        {
            var room=CurrentRoom();if(room==null){_page="rooms";BuildRooms();return;}
            Title(room.Name,GameTitle(room.GameId)+"   ·   код "+room.Code+"   ·   "+(room.Private?"закрытая":"открытая")+"   ·   ставка "+room.Stake);
            var mine=room.HostId==_state.Profile.Id;
            for(var i=0;i<room.MaxPlayers;i++)
            {
                var y=i*68;
                // Место за столом — своя доска: видно, сколько мест и какие свободны.
                if(_clubStyle)Panel("Seat",-8,y-4,614,64);
                if(i>=room.Members.Count){Label("Свободное место",12,y+10,510,40,23).color=MutedInk;continue;}
                var member=room.Members[i];var person=_state.People.FirstOrDefault(p=>p.Id==member.PlayerId);
                var name=member.PlayerId==_state.Profile.Id?_state.Profile.DisplayName:person?.Name??"Бот";
                Label(name+(member.PlayerId==room.HostId?" · хозяин":""),12,y,510,34,24);
                Label((member.Ready?"Готов":"Не готов")+(member.IsBot?" · локальный бот":""),12,y+32,460,26,17).color=MutedInk;
                if(mine && member.PlayerId!=_state.Profile.Id)
                    Button("Убрать",470,y+8,125,40,()=>Send("remove_room_member",member.PlayerId),!room.Started);
            }
            Label(GameSetupSummary(room.GameId,room.GameSettings??HubGameSettings.DefaultFor(room.GameId),room.MaxPlayers),
                0,413,602,44,16).color=GoldInk;
            Button("Добавить бота",0,462,290,49,()=>Send("add_room_bot"),mine && room.Members.Count<room.MaxPlayers && !room.Started);
            Button("Пригласить друга",314,462,288,49,()=>Navigate("friends"),!room.Started);
            var self=room.Members.FirstOrDefault(m=>m.PlayerId==_state.Profile.Id);
            Button(self!=null && self.Ready?"Не готов":"Я готов",0,527,290,49,()=>Send("room_ready",flag:self==null || !self.Ready),!room.Started);
            var localHost=mine || _state.People.Any(p=>p.Id==room.HostId && p.IsBot);
            Button("Начать игру",314,527,288,49,()=>Send("start_room"),localHost && !room.Started);
            Button("Покинуть комнату",0,593,290,44,()=>Send("leave_room"),!room.Started);
            Button("Правила комнаты",314,593,288,44,()=>Navigate("room-settings"),mine && !room.Started);
            Label("Чат комнаты",650,0,480,35,26);
            var messages=_state.Chats.Where(m=>m.ConversationId==room.Id).ToList();
            var viewport=Panel("Room chat viewport",650,45,485,399);
            viewport.GetComponent<Image>().raycastTarget=true;
            viewport.gameObject.AddComponent<RectMask2D>();
            var chatContent=MakeRect(viewport,"Messages",0,0,475,399);
            var scroll=viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport=viewport;scroll.content=chatContent;scroll.horizontal=false;
            scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=32;
            var chatY=8f;
            for(var i=0;i<messages.Count;i++)
            {
                var message=messages[i];var author=message.FromId==_state.Profile.Id?_state.Profile.DisplayName:
                    _state.People.FirstOrDefault(p=>p.Id==message.FromId)?.Name??"Комната";
                var text=MakeLabel(chatContent,author+": "+message.Text,14,chatY,447,30,_clubStyle?15:19);
                if(_clubStyle){text.font=ClubFont();text.color=ClubCream;}
                var height=Math.Max(30,text.preferredHeight+6);
                text.rectTransform.sizeDelta=new Vector2(459,height);chatY+=height+10;
            }
            chatContent.sizeDelta=new Vector2(475,Math.Max(399,chatY));
            scroll.verticalNormalizedPosition=0;
            var input=Input(_roomChat,650,465,485);input.onValueChanged.AddListener(v=>_roomChat=v);
            input.characterLimit=400;
            Button("Отправить",650,527,235,49,async()=>
            {var result=await Dispatch("send_chat",room.Id,_roomChat);if(result.Success){_roomChat="";Render();}});
            Button("Скопировать код",906,527,230,49,()=>{GUIUtility.systemCopyBuffer=room.Code;Toast("Код комнаты скопирован");});
            Label("Правила комнаты общие для всех участников.\nИзменить их может хозяин до начала партии.",650,593,485,60,18).color=MutedInk;
        }

        private void Title(string title,string subtitle="")
        {
            _pageTitle.text=title;_pageSubtitle.text=subtitle;
            if(_clubTitle!=null){_clubTitle.text=title;_clubSubtitle.text=subtitle;}
        }
        /// <summary>Подпись страницы; в клубе — шрифтом вывесок, на пятую часть мельче (строка у него выше).</summary>
        private Text Label(string text,float x,float y,float w=1100,float h=34,int size=22)
        {
            var table=IsTablePage(_page);
            var label=MakeLabel(_content,text,x,y,w,h,_clubStyle && !table?Mathf.RoundToInt(size*ClubTextScale):size);
            if(_clubStyle){label.font=table?_font:ClubFont();label.color=ClubCream;}
            return label;
        }
        private Button Button(string text,float x,float y,float w,float h,Action action,bool enabled=true)
        {
            var button=_clubStyle?ClubButton(_content,text,x,y,w,h,action,ClubToneOf(text)):MakeButton(_content,text,x,y,w,h,action);
            button.interactable=enabled;
            // Недоступная доска тускнеет сама, её буквы — вместе с ней.
            if(_clubStyle && !enabled)button.GetComponentInChildren<Text>().color=new Color(1,1,1,.45f);
            return button;
        }
        private RectTransform Panel(string name,float x,float y,float w,float h)
        {
            var rect=MakeRect(_content,name,x,y,w,h);var image=rect.gameObject.AddComponent<Image>();
            if(_clubStyle)SetPlate(image,false,Mathf.Clamp(Mathf.Min(w,h)*.06f,8,16));
            else{Round(image,w,h,Mathf.Min(16,h/2));image.color=HubPanel;}
            image.raycastTarget=false;return rect;
        }
        private InputField Input(string value,float x,float y,float w=420)
        {
            RectTransform rect;Image image;
            if(_clubStyle)
            {
                // Поле ввода клуба — тёмная вставка с тонкой золотой каймой.
                rect=MakeRect(_content,"Input",x,y,w,44);image=rect.gameObject.AddComponent<Image>();
                Round(image,w,44,10);image.color=new Color(.05f,.035f,.03f,.94f);
                var edge=rect.gameObject.AddComponent<Outline>();edge.effectColor=new Color(.86f,.64f,.3f,.85f);edge.effectDistance=new Vector2(1.5f,-1.5f);
            }
            else{rect=Panel("Input",x,y,w,44);image=rect.GetComponent<Image>();}
            image.raycastTarget=true;
            var field=rect.gameObject.AddComponent<InputField>();field.targetGraphic=image;
            field.textComponent=MakeLabel(rect,"",14,4,w-28,36,_clubStyle?17:21);
            if(_clubStyle){field.textComponent.font=ClubFont();field.textComponent.color=ClubCream;field.textComponent.alignment=TextAnchor.MiddleLeft;}
            if(IsTablePage(_page)){field.textComponent.font=_font;field.textComponent.fontSize=21;}
            field.text=value??"";field.characterLimit=180;
            return field;
        }
        private void Progress(int value,int max,float x,float y,float w=500,float h=14)
        {
            Panel("Progress track",x,y,w,h).GetComponent<Image>().color=new Color(0,0,0,.45f);
            var filled=Panel("Progress",x,y,w*Mathf.Clamp01(value/(float)Math.Max(1,max)),h);filled.GetComponent<Image>().color=HubGold;
        }
        private Text MakeLabel(Transform parent,string text,float x,float y,float w,float h,int size)
        {
            var label=MakeRect(parent,"Label",x,y,w,h).gameObject.AddComponent<Text>();
            label.font=_font;label.text=text;label.fontSize=size;label.color=HubPaper;label.raycastTarget=false;label.supportRichText=false;
            label.alignment=TextAnchor.UpperLeft;label.horizontalOverflow=HorizontalWrapMode.Wrap;label.verticalOverflow=VerticalWrapMode.Truncate;
            return label;
        }
        private Button MakeButton(Transform parent,string text,float x,float y,float w,float h,Action action)
        {
            var rect=MakeRect(parent,text,x,y,w,h);
            // Опора в центре: нажатая кнопка проседает к своей середине, а не к углу.
            rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=new Vector2(x+w/2,-y-h/2);
            var image=rect.gameObject.AddComponent<Image>();Round(image,w,h,Mathf.Min(18,h/2));image.color=ToneOf(text);
            var lip=rect.gameObject.AddComponent<Shadow>();lip.effectDistance=new Vector2(0,-4);lip.effectColor=new Color(0,0,0,.35f);
            var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());
            var colors=button.colors;colors.highlightedColor=new Color(.93f,.97f,1f);colors.pressedColor=new Color(.82f,.82f,.82f);
            colors.disabledColor=new Color(.55f,.55f,.55f,.55f);button.colors=colors;
            rect.gameObject.AddComponent<HubPress>();
            var label=MakeLabel(rect,text,8,2,w-16,h-4,21);label.alignment=TextAnchor.MiddleCenter;label.fontStyle=FontStyle.Bold;
            label.color=IsLight(image.color)?HubInk:Color.white;
            return button;
        }

        private static readonly string[] PrimaryWords={"Играть","Начать","Я готов","Получить","Создать","Сыграть","Отправить","Принять","Подать заявку","Открыть"};
        private static readonly string[] DangerWords={"Покинуть","Удалить","Убрать","Отклонить"};

        // Цвет по смыслу надписи, пока у страниц нет своих ролей кнопок: главное действие жёлтое,
        // разрушающее красное, остальное синее.
        private static Color ToneOf(string text)
        {
            if(PrimaryWords.Any(word=>text.StartsWith(word,StringComparison.Ordinal)))return HubYellow;
            if(DangerWords.Any(word=>text.StartsWith(word,StringComparison.Ordinal)))return HubRed;
            return HubBlue;
        }
        private static RectTransform MakeRect(Transform parent,string name,float x,float y,float w,float h)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=new Vector2(x,-y);rect.sizeDelta=new Vector2(w,h);return rect;
        }
        private static void Stretch(RectTransform rect)
        {rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
        private void Resize()
        {
            if(_stage==null)return;var size=((RectTransform)transform).rect.size;if(size==_size)return;_size=size;
            _stage.localScale=Vector3.one*Mathf.Max(.01f,Mathf.Min(size.x/1600,size.y/900));
            PlaceCity();
        }
    }
}
