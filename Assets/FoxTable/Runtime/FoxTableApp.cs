using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable
{
    public sealed partial class FoxTableApp : MonoBehaviour
    {
        private RoomClient client;
        private UIDocument document;
        private PanelSettings settings;
        private VisualElement root, screen, modal, sheet;
        private Label status, title, info, toast;
        private BattleBoard board;
        private string panel = "", selectedId, loadedMap;
        private int lastRevision = -1;
        private Texture2D downloadedMap;
        private Coroutine mapRequest;
        private IVisualElementScheduledItem toastTimer;
        private Rect safeArea;
        private string chatDraft = "";
        private bool tableGm;
        private bool pickingMap;
        private float nextPickerPoll;
        private string importCache;
        private static readonly string[] Conditions = {"Ослеплён", "Очарован", "Глухота", "Испуган", "Схвачен",
            "Недееспособен", "Невидим", "Парализован", "Окаменел", "Отравлен", "Сбит с ног", "Опутан",
            "Оглушён", "Без сознания", "Концентрация", "Мёртв"};

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<FoxTableApp>() != null) return;
            var go = new GameObject("FoxTable");
            DontDestroyOnLoad(go);
            go.AddComponent<FoxTableApp>();
        }

        private void Awake()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            client = gameObject.AddComponent<RoomClient>();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("FoxTheme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(400, 860);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = settings;
            root = document.rootVisualElement;
            root.styleSheets.Add(Resources.Load<StyleSheet>("FoxTable"));
            root.AddToClassList("root");
            root.style.unityFontDefinition = FontDefinition.FromFont(Resources.Load<Font>("DejaVuSans"));
            screen = new VisualElement(); screen.style.flexGrow = 1; root.Add(screen);
            modal = new VisualElement(); modal.AddToClassList("modal"); modal.style.display = DisplayStyle.None; root.Add(modal);
            toast = new Label(); toast.AddToClassList("toast"); toast.style.display = DisplayStyle.None; root.Add(toast);
            client.Changed += StateChanged;
            client.Notice += ShowToast;
            client.ActionRejected+=CommandRejected;
            client.ConnectionChanged += message => { if (status != null) status.text = message; };
            Lobby();

        }

        private void Update()
        {
            PollMapPicker();
            if (Screen.safeArea == safeArea) return;
            safeArea = Screen.safeArea;
            root.EnableInClassList("landscape",Screen.width>Screen.height);
            // Percentage insets track UI Toolkit panel scaling and device rotation.
            root.style.paddingTop = Length.Percent((Screen.height-safeArea.yMax)/Screen.height*100);
            root.style.paddingBottom = Length.Percent(safeArea.yMin/Screen.height*100);
            root.style.paddingLeft = Length.Percent(safeArea.xMin/Screen.width*100);
            root.style.paddingRight = Length.Percent((Screen.width-safeArea.xMax)/Screen.width*100);
        }
        private void OnApplicationPause(bool paused){if(paused)board?.CancelInteraction();}
        private void OnApplicationFocus(bool focused){if(!focused)board?.CancelInteraction();}
        private void CommandRejected(){board?.RejectPendingMoves();}

        private static Label Label(VisualElement parent, string text, string cls = null)
        {
            var label = new Label(text);
            if (cls != null) label.AddToClassList(cls);
            parent.Add(label); return label;
        }
        private static Button Button(VisualElement parent, string text, Action action, string cls = null)
        {
            var button = new Button(action) {text = text};
            if (cls != null) button.AddToClassList(cls);
            parent.Add(button); return button;
        }
        private static TextField Field(VisualElement parent, string label, string value = "", bool multiline = false)
        {
            var field = new TextField(label) {value = value, multiline = multiline};
            parent.Add(field); return field;
        }
        private static IntegerField Number(VisualElement parent, string label, int value)
        {
            var field = new IntegerField(label) {value = value,isDelayed=true}; parent.Add(field); return field;
        }
        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement(); row.AddToClassList("row"); parent.Add(row); return row;
        }
        private static void Check(VisualElement parent, string label, bool value, Action<bool> change)
        {
            var field = new Toggle(label) {value = value};
            field.RegisterValueChangedCallback(e => change(e.newValue)); parent.Add(field);
        }

        private void Lobby()
        {
            if(mapRequest!=null) {StopCoroutine(mapRequest);mapRequest=null;}
            if(downloadedMap!=null) {Destroy(downloadedMap);downloadedMap=null;}
            screen.Clear();
            board = null; status = null; lastRevision = -1; loadedMap = null;
            var scroll = new ScrollView(); scroll.AddToClassList("lobby"); screen.Add(scroll);
            Label(scroll, "FOX / TABLE", "brand");
            Label(scroll, "Ваша история.\nВаше поле боя.", "headline");
            Label(scroll, "КАРТЫ И БОИ D&D", "eyebrow");
            var local = new VisualElement(); local.AddToClassList("card"); scroll.Add(local);
            Label(local, "СТОЛ НА ТВОЁМ ТЕЛЕФОНЕ", "eyebrow");
            Button(local, "Играть локально", client.OpenLocal, "primary");
            Label(local, "Без сервера, интернета и регистрации. Кампания сохраняется автоматически; эта кнопка продолжит сохранённую игру.", "muted");
            var online = new Foldout {text="Онлайн с друзьями",value=false};online.AddToClassList("online-foldout");scroll.Add(online);
            var card = new VisualElement(); card.AddToClassList("card"); online.Add(card);
            if(client.HasOnlineSession)Button(card,"Продолжить онлайн-комнату",client.Resume,"primary");
            Label(card, "ОНЛАЙН С ДРУЗЬЯМИ", "eyebrow");
            var server = Field(card, "Сервер", PlayerPrefs.GetString("server", "https://"));
            var name = Field(card, "Твоё имя", PlayerPrefs.GetString("name", "Мастер"));
            var campaign = Field(card, "Кампания", "Забытое святилище");
            Button(card, "Создать комнату мастера", () => {
                PlayerPrefs.SetString("server", server.value); PlayerPrefs.SetString("name", name.value);
                client.Create(server.value, name.value, campaign.value);
            }, "primary");
            Label(card, "ПРИСОЕДИНИТЬСЯ К ИГРЕ", "eyebrow");
            var code = Field(card, "Код комнаты");
            var invite = Field(card, "Ключ приглашения");
            Button(card, "Войти игроком", () => {
                PlayerPrefs.SetString("server", server.value); PlayerPrefs.SetString("name", name.value);
                client.Join(server.value, code.value, name.value, invite.value);
            });
            Label(card, "ВОССТАНОВИТЬ СВОЙ ДОСТУП", "eyebrow");
            var recovery = Field(card, "Личный ключ");
            recovery.isPasswordField = true;
            Button(card, "Восстановить доступ", () => client.Restore(server.value, code.value, recovery.value));
            Label(scroll, "v0.5.0 · Unity / Android\nЛокально — один телефон. Онлайн — общая комната на сервере FoxTable.", "muted");
        }

        private void Table()
        {
            screen.Clear();modeButtons.Clear();stamp=null;
            tableGm=client.State.IsGm;loadedMap=null;lastRevision=-1;
            var header=Row(screen);header.AddToClassList("header");
            var branding=new VisualElement();branding.style.flexGrow=1;branding.style.minWidth=0;header.Add(branding);
            Label(branding,"FOX / TABLE","brand-small");title=Label(branding,"","table-title");
            status=Label(header,client.IsLocal?"На телефоне":"В сети","online");
            Button(header,"Меню",()=>OpenPanel("room"));
            var bar=Row(screen);bar.AddToClassList("scene-bar");
            info=Label(bar,"","muted");info.style.flexGrow=1;
            Button(bar,"−",()=>board.Zoom(0.8f));Button(bar,"Вписать",()=>board.Fit());Button(bar,"+",()=>board.Zoom(1.25f));
            board=new BattleBoard(client);
            board.Selected+=id=>{selectedId=id;RefreshSelection();if(id!=null)board.EnsureSelectionVisible(Screen.width>Screen.height?70:112);};
            board.Placed+=PlaceMiniature;
            board.Hint+=value=>{if(toolText!=null)toolText.text=value;};
            screen.Add(board);
            toolText=Label(screen,ToolHelp("select"),"tool-hint");
            BuildSelectionBar();
            var tools=Row(screen);tools.AddToClassList("tools-main");
            ModeButton(tools,"Объекты","select");ModeButton(tools,"Обзор","pan");
            Button(tools,"Сетка",()=>OpenPanel("grid"));
            if(client.IsLocal)undoButton=Button(tools,"Отменить",client.UndoLocal);else undoButton=null;
            var bottom=Row(screen);bottom.AddToClassList("bottom-nav");
            if(client.State.IsGm)Button(bottom,"Местность",()=>OpenPanel("terrain"));
            Button(bottom,client.State.IsGm?"Модели":"Фигуры",()=>OpenPanel("catalog"));
            if(client.State.IsGm)Button(bottom,"Карты",()=>OpenPanel("maps"));
            Button(bottom,"Игра",()=>OpenPanel("game"));
        }

        private void StateChanged(RoomState state)
        {
            if(board==null || tableGm!=state.IsGm) {ClosePanel();Table();}
            title.text=state.title;
            info.text=state.Active.name+" · "+(state.Active.width>80 || state.Active.height>80?$"{state.Active.width} × {state.Active.height}":state.Active.cellFeet+" фт / кл.");
            if(lastRevision!=state.revision)
            {
                lastRevision=state.revision;
                board.SetState(state);
                if(panel=="chat")RefreshJournal();
                if(panel=="combat" || panel=="lighting")OpenPanel(panel);
                RefreshSelection();
            }
            string key=state.Active.id+":"+state.Active.mapId+":"+state.Active.background;
            if(loadedMap!=key)
            {
                loadedMap=key;
                if(mapRequest!=null) StopCoroutine(mapRequest);
                if(downloadedMap!=null) {Destroy(downloadedMap);downloadedMap=null;}
                board.SetMap(!string.IsNullOrEmpty(state.Active.background) || !string.IsNullOrEmpty(state.Active.mapId)?null:Resources.Load<Texture2D>("Sanctuary"));
                if(!string.IsNullOrEmpty(state.Active.mapId))
                    mapRequest=StartCoroutine(client.DownloadMap(state.Active.mapId, texture=> {
                        if(loadedMap==key && board!=null) {downloadedMap=texture;board.SetMap(texture);}
                        else if(texture!=null)Destroy(texture);
                    }));
            }
        }

        private void ClosePanel() { panel="";actionStats=null;journalItems=null;modal.style.display=DisplayStyle.None;modal.Clear();ReleaseImport(); }
        private void OpenPanel(string kind)
        {
            if(client.State==null)return;
            panel=kind;actionStats=null;journalItems=null;modal.Clear();modal.style.display=DisplayStyle.Flex;
            var dismiss = new VisualElement(); dismiss.AddToClassList("scrim");
            dismiss.RegisterCallback<ClickEvent>(e=>ClosePanel()); modal.Add(dismiss);
            sheet = new VisualElement(); sheet.AddToClassList("sheet"); modal.Add(sheet);
            var row=Row(sheet); row.AddToClassList("sheet-header");
            var heading=Label(row, PanelTitle(kind), "sheet-title"); heading.style.flexGrow=1;
            Button(row,"×",ClosePanel);
            var content=new ScrollView(); content.AddToClassList("sheet-content"); sheet.Add(content);
            switch(kind)
            {
                case "tokens": Tokens(content); break;
                case "token": Token(content); break;
                case "combat": Combat(content); break;
                case "dice": Dice(content); break;
                case "chat": Chat(content); break;
                case "maps": MapWorkspace(content); break;
                case "createMap":CreateMapPanel(content);break;
                case "terrain":TerrainPanel(content);break;
                case "game":GamePanel(content);break;
                case "catalog":Catalog(content);break;
                case "tools":ToolsPanel(content);break;
                case "grid":GridWorkspace(content,false);break;
                case "importMap":GridWorkspace(content,true);break;
                case "room": Room(content); break;
                case "actions": Actions(content); break;
                case "profile": Profile(content); break;
                case "lighting": Lighting(content); break;
                case "vision": Vision(content); break;
            }
        }

        private string PanelTitle(string kind)
        {
            switch(kind)
            {
                case "tokens":return "Объекты на карте";
                case "catalog":return "Каталог моделей";
                case "tools":return "Инструменты карты";
                case "grid":return "Настроить сетку";
                case "importMap":return "Добавить карту";
                case "token":return "Карточка жетона";
                case "combat":return "Порядок инициативы";
                case "dice":return "Бросок кубиков";
                case "chat":return "Журнал приключения";
                case "maps":return "Атлас кампании";
                case "createMap":return "Новая карта";
                case "terrain":return "Мастерская местности";
                case "game":return "Игровой стол";
                case "actions":return "Быстрые действия";
                case "profile":return client.IsLocal?"Слои, ауры и атака":"Зрение, свет и бой";
                case "lighting":return client.IsLocal?"Стены и двери":"Стены, двери и освещение";
                case "vision":return "Глазами игрока";
                default:return "Комната и участники";
            }
        }

        private void Tokens(VisualElement content)
        {
            var s=client.State;
            foreach(var t in s.Active.tokens)
            {
                var row=Row(content); row.AddToClassList("list-item");
                var labels=new VisualElement();labels.style.flexGrow=1;row.Add(labels);
                Label(labels,t.name+(t.hidden || t.layer=="gm"?" · скрыт":"")+(t.layer=="map"?" · карта":""));
                Label(labels,t.kind=="object"?$"Объект карты · {t.size} × {t.size} кл.":$"{t.hp}/{t.maxHp} HP · КД {t.ac} · инициатива {t.initiative}","muted");
                Button(row,"Открыть",()=>{selectedId=t.id;board.Select(t.id);OpenPanel("token");});
            }
            if(s.IsGm)Button(content,"Добавить модель из каталога",()=>OpenPanel("catalog"),"primary");
        }

        private void Token(VisualElement content)
        {
            var s=client.State;
            var t=Array.Find(s.Active.tokens,v=>v.id==selectedId);
            if(t==null){Label(content,"Жетон удалён или скрыт мастером");return;}
            if(t.kind=="object" && s.IsGm){ObjectCard(content,t);return;}
            bool editable=!client.IsPreview && (s.IsGm || (t.ownerId==s.selfId && t.layer=="tokens"));
            Button(content,"Быстрые действия",()=>OpenPanel("actions"));
            if(s.IsGm)Button(content,client.IsLocal?"Слои, ауры и атака":"Зрение, свет, слои и атака",()=>OpenPanel("profile"),"primary");
            var name=Field(content,"Имя",t.name);name.SetEnabled(s.IsGm);
            var row=Row(content);
            var hp=Number(row,"HP",t.hp);hp.SetEnabled(editable);
            var max=Number(row,"Макс.",t.maxHp);max.SetEnabled(s.IsGm);
            row=Row(content);
            var ac=Number(row,"КД",t.ac);ac.SetEnabled(s.IsGm);
            var initiative=Number(row,"Инициатива",t.initiative);initiative.SetEnabled(s.IsGm);
            var conditions=new List<string>(t.conditions);
            Label(content,"СОСТОЯНИЯ","eyebrow");
            foreach(var condition in Conditions)
            {
                var toggle=new Toggle(condition){value=conditions.Contains(condition)};
                toggle.SetEnabled(editable);
                toggle.RegisterValueChangedCallback(e=>{if(e.newValue)conditions.Add(condition);else conditions.Remove(condition);});
                content.Add(toggle);
            }
            IntegerField size=null;TextField color=null;PopupField<string> owner=null;
            List<Member> members=null;
            bool hidden=t.hidden;
            if(s.IsGm)
            {
                size=Number(content,"Размер в клетках (1–4)",t.size);
                color=Field(content,"Цвет #RRGGBB",t.color);
                Check(content,"Скрыт от игроков",hidden,v=>hidden=v);
                if(!client.IsLocal)
                {
                members=new List<Member>{new Member{id="",name="Только мастер"}};members.AddRange(s.members);
                int index=Mathf.Max(0,members.FindIndex(m=>m.id==t.ownerId));
                owner=new PopupField<string>("Владелец",members.Select(m=>m.name+" · "+(m.id==""?"—":m.id.Substring(0,4))).ToList(),index);
                content.Add(owner);
                }
            }
            if(editable) Button(content,"Сохранить жетон",()=> {
                var fields=new List<object>{"tokenId",t.id,"hp",hp.value,"conditions",conditions.ToArray()};
                if(s.IsGm) fields.AddRange(new object[]{"name",name.value,"maxHp",max.value,"ac",ac.value,
                    "initiative",initiative.value,"size",size.value,"color",color.value,"ownerId",client.IsLocal?t.ownerId:members[owner.index].id,"hidden",hidden});
                client.Act("token.update",fields.ToArray());ClosePanel();
            },"primary");
            if(s.IsGm) Button(content,"Удалить жетон",()=>Confirm("Удалить «"+t.name+"»?",()=> {
                client.Act("token.delete","tokenId",t.id);ClosePanel();
            }),"danger");
        }

        private void Combat(VisualElement content)
        {
            var s=client.State;var scene=s.Active;
            Label(content,scene.activeCombat?$"Раунд {scene.round}":"Бой ещё не начался","headline-small");
            foreach(var t in scene.tokens.Where(t=>t.kind!="object" && t.layer!="map").OrderByDescending(t=>t.initiative).ThenBy(t=>t.id,StringComparer.Ordinal))
            {
                var row=Row(content);row.AddToClassList("list-item");
                if(t.id==scene.turnId)row.AddToClassList("turn");
                Label(row,t.initiative.ToString(),"initiative");
                var label=Label(row,t.name+"\n"+t.hp+" / "+t.maxHp+" HP");label.style.flexGrow=1;
                Button(row,"Править",()=>{selectedId=t.id;OpenPanel("token");});
            }
            if(s.IsGm)
            {
                if(!scene.activeCombat)Button(content,"Начать бой",()=>client.Act("combat.start"),"primary");
                else
                {
                    Button(content,"Следующий ход →",()=>client.Act("combat.next"),"primary");
                    Button(content,"Завершить бой",()=>client.Act("combat.stop"));
                }
            }
        }

        private void Dice(VisualElement content)
        {
            Label(content,client.IsLocal?"Кубики бросаются на телефоне. Результаты сохраняются в журнале.":"Кубики бросает сервер. Результат увидят все участники.","muted");
            var expression=Field(content,"Формула",diceFormula);
            expression.RegisterValueChangedCallback(e=>diceFormula=e.newValue);
            var row=Row(content);
            foreach(int sides in new[]{4,6,8,10,12,20,100})
            {int value=sides;Button(row,"d"+value,()=>expression.value="1d"+value);}
            Button(content,"Бросить",()=>{client.Act("roll","text",expression.value);OpenPanel("chat");},"primary");
            row=Row(content);
            Button(row,"Преимущество",()=>{client.Act("roll","text",expression.value,"mode","advantage");OpenPanel("chat");});
            Button(row,"Помеха",()=>{client.Act("roll","text",expression.value,"mode","disadvantage");OpenPanel("chat");});
        }

        private void Chat(VisualElement content)
        {
            journalScroll=content as ScrollView;
            journalItems=new VisualElement();content.Add(journalItems);RefreshJournal();
            var field=Field(content,"Сообщение",chatDraft);
            field.RegisterValueChangedCallback(e=>chatDraft=e.newValue);
            Button(content,"Отправить",()=>{if(!string.IsNullOrWhiteSpace(field.value)){client.Act("chat","text",field.value);field.value="";chatDraft="";}},"primary");
        }

        private void Room(VisualElement content)
        {
            var s=client.State;
            if(client.IsLocal)
            {
                Label(content,"ЛОКАЛЬНАЯ КАМПАНИЯ","headline-small");
                Label(content,"Вы управляете всем столом как мастер. Карты, бой и журнал хранятся на этом телефоне. Для общей игры с разных устройств откройте онлайн-комнату.","muted");
                var localTitle=Field(content,"Кампания",s.title);
                Button(content,"Сохранить название",()=>client.Act("room","title",localTitle.value));
                var localNotes=Field(content,"Заметки мастера",s.gmNotes,true);
                localNotes.style.minHeight=140;
                Button(content,"Сохранить заметки",()=>client.Act("notes","text",localNotes.value));
                Label(content,"Игра сохраняется автоматически. После выхода нажмите «Играть локально», чтобы продолжить. Удаление приложения удалит локальные данные.","muted");
                Button(content,"Сохранить и выйти в меню",()=> {
                    if(client.Leave()){ClosePanel();Lobby();}
                },"primary");
                return;
            }
            Label(content,"КОМНАТА "+client.Session.code,"headline-small");
            Button(content,"Выйти в меню, сохранить доступ",()=>{if(client.Leave(false)){ClosePanel();Lobby();}},"primary");
            Button(content,"Скопировать личный ключ восстановления",()=> {
                GUIUtility.systemCopyBuffer=client.Session.secret;
                ShowToast("Личный ключ скопирован. Сохраните его отдельно; не отправляйте игрокам.");
            });
            foreach(var member in s.members)Label(content,(member.online?"● ":"○ ")+member.name+(member.role=="gm"?" · мастер":""));
            if(s.IsGm)
            {
                Button(content,"Скопировать приглашение",()=> {
                    GUIUtility.systemCopyBuffer="FoxTable\nСервер: "+client.Session.server+"\nКомната: "+client.Session.code+"\nКлюч: "+s.invite;
                    ShowToast("Приглашение скопировано");
                },"primary");
                Check(content,"Закрыть вход новым игрокам",s.locked,v=>client.Act("room","locked",v));
                Button(content,"Сменить ключ приглашения",()=>{client.Act("room","rotateInvite",true);ClosePanel();});
                var notes=Field(content,"Приватные заметки мастера",s.gmNotes,true);
                notes.style.minHeight=140;
                Button(content,"Сохранить заметки",()=>client.Act("notes","text",notes.value));
            }
            Label(content,"Закрытие приложения сохраняет доступ. Кнопка ниже удаляет доступ с этого устройства.","muted");
            Button(content,"Забыть комнату и выйти",()=>Confirm("Удалить доступ с устройства? Без личного ключа восстановить роль мастера нельзя. Приглашение даёт только роль игрока.",()=> {
                if(client.Leave()){ClosePanel();Lobby();}
            }),"danger");
        }

        private void Confirm(string prompt, Action accept)
        {
            sheet.Clear();Label(sheet,prompt,"sheet-title");
            Button(sheet,"Подтвердить",accept,"danger");Button(sheet,"Отмена",ClosePanel);
        }

        private void PickMap()
        {
            if(pickingMap)return;
#if UNITY_EDITOR
            string path=UnityEditor.EditorUtility.OpenFilePanel("Карта боя","","png,jpg,jpeg");
            if(!string.IsNullOrEmpty(path))
            {try{PrepareMap(File.ReadAllBytes(path),path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)?"image/png":"image/jpeg");}
            catch(IOException){ShowToast("Не удалось открыть картинку");}}
#elif UNITY_ANDROID
            try
            {
                using(var unity=new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using(var activity=unity.GetStatic<AndroidJavaObject>("currentActivity"))
                using(var picker=new AndroidJavaClass("com.foxtable.FoxGallery")) {
                    importCache=picker.CallStatic<string>("cacheDirectory",activity);
                    pickingMap=true;
                    picker.CallStatic("open",activity);
                }
            }
            catch(Exception){pickingMap=false;ShowToast("Не удалось открыть выбор изображения");}
#else
            ShowToast("Выбор файла доступен в Android и Unity Editor");
#endif
        }

        private void PollMapPicker()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if(!pickingMap || Time.unscaledTime<nextPickerPoll)return;
            nextPickerPoll=Time.unscaledTime+0.2f;
            try {
                using(var picker=new AndroidJavaClass("com.foxtable.FoxGallery")) {
                    string json=picker.CallStatic<string>("takeResult");
                    if(!string.IsNullOrEmpty(json)){pickingMap=false;OnAndroidImage(json);}
                }
            } catch(Exception){pickingMap=false;ShowToast("Ошибка возврата из выбора картинки");}
#endif
        }
        [Serializable] private sealed class GalleryImage {public string fileName,error;public bool cancelled,resized;}
        // Polling consumes a tiny result only after Unity resumes. Pixels stay in the private cache.
        public void OnAndroidImage(string json)
        {
            string path=null;
            try
            {
                var image=JsonUtility.FromJson<GalleryImage>(json);
                if(image==null || image.cancelled)return;
                if(!string.IsNullOrEmpty(image.error)){ShowToast(image.error);return;}
                if(string.IsNullOrEmpty(importCache) || Path.GetFileName(image.fileName)!=image.fileName ||
                    !Guid.TryParseExact(Path.GetFileNameWithoutExtension(image.fileName),"N",out _))throw new ArgumentException();
                path=Path.Combine(importCache,image.fileName);
                if(new FileInfo(path).Length>MapImage.MaxBytes)throw new ArgumentException();
                var bytes=File.ReadAllBytes(path);
                ReleaseImport();
                StartCoroutine(PrepareMapSoon(bytes,image.resized));
            }
            catch(Exception){ShowToast("Не удалось прочитать изображение");}
            finally {if(path!=null)try{File.Delete(path);}catch(IOException){ShowToast("Не удалось очистить временный файл карты");}}
        }

        private System.Collections.IEnumerator PrepareMapSoon(byte[] bytes,bool resized)
        {
            yield return null;
            try {PrepareMap(bytes,"");if(resized)ShowToast("Большая картинка уменьшена для памяти телефона");}
            catch(ArgumentException e){ShowToast(e.Message);}
        }

        private void ShowToast(string message)
        {
            toast.text=message;toast.style.display=DisplayStyle.Flex;
            toastTimer?.Pause();toastTimer=toast.schedule.Execute(()=>toast.style.display=DisplayStyle.None).StartingIn(4000);
        }

        private void OnDestroy()
        {
            if(client!=null){client.Changed-=StateChanged;client.Notice-=ShowToast;client.ActionRejected-=CommandRejected;}
            if(downloadedMap!=null)Destroy(downloadedMap);
            if(settings!=null)Destroy(settings);
            if(pendingTexture!=null)Destroy(pendingTexture);
        }
    }
}
