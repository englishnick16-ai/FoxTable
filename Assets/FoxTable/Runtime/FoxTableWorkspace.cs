using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable
{
    public sealed partial class FoxTableApp
    {
        private VisualElement selectionBar,placementBar,journalItems;
        private ScrollView journalScroll;
        private string diceFormula="1d20";
        private Label selectionText,toolText,placementText,actionStats;
        private Image selectionArt;
        private Button undoButton;
        private Miniature stamp;
        private readonly List<Button> modeButtons=new List<Button>();
        private string catalogCategory="Герои",catalogSearch="";
        private Texture2D pendingTexture;
        private byte[] pendingImage;
        private string pendingMime,gridSceneId;
        private SceneState gridDraft;
        private Label selectedSize;
        private void ReleaseImport()
        {
            if(pendingTexture!=null)Destroy(pendingTexture);
            pendingTexture=null;pendingImage=null;pendingMime=null;
        }

        private void BuildSelectionBar()
        {
            selectionBar=new VisualElement();screen.Add(selectionBar);selectionBar.AddToClassList("selection-bar");
            var summary=Row(selectionBar);
            selectionArt=new Image{scaleMode=ScaleMode.ScaleToFit};selectionArt.AddToClassList("selection-art");summary.Add(selectionArt);
            selectionText=Label(summary,"","selection-summary");selectionText.style.flexGrow=1;
            Button(summary,"Действия",()=>OpenPanel("actions"));
            selectedSize=null;
            if(client.State.IsGm){
                var edit=Row(selectionBar);
                Button(edit,"Меньше",()=>ResizeSelected(-1));selectedSize=Label(edit,"","muted");
                Button(edit,"Больше",()=>ResizeSelected(1));
                Button(edit,"↻ 45°",()=>{var t=Array.Find(client.State.Active.tokens,v=>v.id==selectedId);if(t!=null)client.Act("token.update","tokenId",t.id,"rotation",(t.rotation+45)%360);});
                Button(edit,"Карточка",()=>OpenPanel("token"));
            }
            selectionBar.style.display=DisplayStyle.None;
            placementBar=Row(screen);placementBar.AddToClassList("placement-bar");
            placementText=Label(placementBar,"");placementText.style.flexGrow=1;
            Button(placementBar,"Готово",()=>{stamp=null;SetTool("select");});placementBar.style.display=DisplayStyle.None;
        }
        private void ResizeSelected(int delta)
        {
            var t=Array.Find(client.State.Active.tokens,v=>v.id==selectedId);
            if(t==null || !client.State.IsGm || client.IsPreview)return;
            int size=Mathf.Clamp(t.size+delta,1,4);
            if(size!=t.size)client.Act("token.update","tokenId",t.id,"size",size);
            else ShowToast("Размер фигуры: от 1 до 4 клеток");
        }
        private void QuickHp(int delta)
        {
            var t=Array.Find(client.State.Active.tokens,v=>v.id==selectedId);
            if(t==null || t.kind=="object")return;
            if(!client.IsPreview && (client.State.IsGm || t.ownerId==client.State.selfId))client.Act("token.hp","tokenId",t.id,"delta",delta);
        }
        private void RefreshSelection()
        {
            if(selectionBar==null || client.State==null)return;
            var t=Array.Find(client.State.Active.tokens,v=>v.id==selectedId);
            selectionBar.style.display=t==null || stamp!=null?DisplayStyle.None:DisplayStyle.Flex;
            if(t!=null)
            {
                selectionText.text=t.name+"\n"+(t.kind=="object"?"Объект карты":$"{t.hp}/{t.maxHp} HP · КД {t.ac}");
                selectionArt.image=TokenCatalog.Texture(TokenCatalog.Art(t));
                if(selectedSize!=null)selectedSize.text=t.size+" × "+t.size;
                bool edit=!client.IsPreview && (client.State.IsGm || t.ownerId==client.State.selfId);
                foreach(var button in selectionBar.Children().OfType<Button>())if(button.text!="Действия")button.SetEnabled(edit && t.kind!="object");
                if(actionStats!=null)actionStats.text=t.kind=="object"?"Объект карты":$"{t.hp}/{t.maxHp} HP · временные {t.tempHp} · КД {t.ac}";
            }
            if(undoButton!=null)undoButton.SetEnabled(client.CanUndo);
        }
        private void SetTool(string value)
        {
            board.Tool=value;
            if(value!="place")stamp=null;
            if(placementBar!=null)placementBar.style.display=value=="place"?DisplayStyle.Flex:DisplayStyle.None;
            foreach(var button in modeButtons)button.EnableInClassList("active-tool",(string)button.userData==value);
            if(toolText!=null)toolText.text=ToolHelp(value);
            RefreshSelection();
        }
        private static string ToolHelp(string value)
        {
            switch(value)
            {
                case "select":return "Тяни фигуру · пустое место двигает карту · два пальца — масштаб";
                case "pan":return "Тяни карту одним пальцем · два пальца — масштаб";
                case "place":return "Коснись карты, чтобы поставить модель · можно ставить несколько";
                case "measure":return "Проведи по карте, чтобы измерить расстояние";
                case "reveal":return "Проведи пальцем: открыть туман кистью 3 × 3";
                case "conceal":return "Проведи пальцем: скрыть область кистью 3 × 3";
                case "doorUse":return "Нажми на дверь, чтобы открыть или закрыть её";
                case "ping":return "Коснись места на карте: метку увидят участники";
                case "wall":case "door":case "window":return "Проведи от начала до конца преграды · два пальца — масштаб";
                default:return "Проведи по карте, чтобы задать размер шаблона";
            }
        }
        private void ModeButton(VisualElement parent,string label,string mode)
        {
            var b=Button(parent,label,()=>SetTool(mode));b.userData=mode;modeButtons.Add(b);
            if(mode=="select")b.AddToClassList("active-tool");
        }
        private void ChooseMiniature(Miniature miniature)
        {
            stamp=miniature;ClosePanel();SetTool("place");
            placementText.text=miniature.name+" · коснись карты";
        }
        private void PlaceMiniature(Vector2 position)
        {
            if(stamp==null || !client.State.IsGm || client.IsPreview)return;
            client.Act("token.add",TokenCatalog.Placement(stamp,client.State.Active,position));
        }
        private void Catalog(VisualElement content)
        {
            if(!client.State.IsGm){Tokens(content);return;}
            Label(content,"Выбери модель → коснись места на карте. Двумя пальцами можно приблизить карту перед расстановкой.","muted");
            var search=Field(content,"Поиск",catalogSearch);
            var tabs=new ScrollView(ScrollViewMode.Horizontal);tabs.AddToClassList("catalog-tabs");content.Add(tabs);var row=Row(tabs);
            var items=new VisualElement();items.AddToClassList("catalog-grid");
            void Render()
            {
                items.Clear();
                foreach(var m in TokenCatalog.All.Where(m=>m.category==catalogCategory && (m.name.IndexOf(catalogSearch,StringComparison.OrdinalIgnoreCase)>=0)))
                {
                    var tile=new Button(()=>ChooseMiniature(m));tile.AddToClassList("catalog-tile");
                    var art=new Image{image=m.Texture,scaleMode=ScaleMode.ScaleToFit};art.AddToClassList("catalog-art");tile.Add(art);
                    Label(tile,m.name,"catalog-name");Label(tile,m.size+" × "+m.size+" кл.","muted");items.Add(tile);
                }
                if(items.childCount==0)Label(items,"Ничего не найдено. Попробуй другое название.","muted");
                foreach(var b in row.Children().OfType<Button>())b.EnableInClassList("active-tool",b.text==catalogCategory);
            }
            foreach(string category in new[]{"Герои","Монстры","Природа","Предметы","Подземелье"})
                Button(row,category,()=>{catalogCategory=category;Render();});
            search.RegisterValueChangedCallback(e=>{catalogSearch=e.newValue;Render();});
            content.Add(items);Render();
            Button(content,"На карте: "+client.State.Active.tokens.Length+" объектов →",()=>OpenPanel("tokens"));
            Label(content,"Дверь и стена из каталога — декорации. Для преграды движению и атаке используй «Инструменты → Стены и двери».","muted");
        }
        private void ToolsPanel(VisualElement content)
        {
            void Use(string tool){ClosePanel();SetTool(tool);}
            Label(content,"КАРТА И ИЗМЕРЕНИЯ","eyebrow");
            var row=Row(content);Button(row,"Двигать карту",()=>Use("pan"));Button(row,"Метка",()=>Use("ping"));Button(row,"Двери",()=>Use("doorUse"));
            Button(content,"Линейка расстояний",()=>Use("measure"));
            if(!client.State.IsGm)return;
            Label(content,"ТУМАН ВОЙНЫ","eyebrow");
            Check(content,"Туман включён",client.State.Active.fog,v=>client.Act("scene.update","fog",v));
            row=Row(content);
            void FogTool(string tool){if(!client.State.Active.fog)client.Act("scene.update","fog",true);Use(tool);}
            Button(row,"Открывать",()=>FogTool("reveal"));Button(row,"Скрывать",()=>FogTool("conceal"));
            Label(content,"ШАБЛОНЫ ЗАКЛИНАНИЙ","eyebrow");row=Row(content);
            Button(row,"Круг",()=>Use("circle"));Button(row,"Конус",()=>Use("cone"));Button(row,"Линия",()=>Use("line"));
            Button(content,"Убрать шаблоны",()=>{client.Act("drawing.clear");ClosePanel();});
            Label(content,"СТЕНЫ И ДВЕРИ","eyebrow");row=Row(content);
            Button(row,"Стена",()=>Use("wall"));Button(row,"Дверь",()=>Use("door"));Button(row,"Окно",()=>Use("window"));
            Button(content,client.IsLocal?"Список стен и дверей →":"Список преград и освещение →",()=>OpenPanel("lighting"));
            Label(content,"ВЫБИРАТЬ НА КАРТЕ","eyebrow");
            var layer=new PopupField<string>("Слой",new List<string>{"Все объекты","Только фигуры","Только декор"},board.EditingLayer=="tokens"?1:board.EditingLayer=="map"?2:0);
            content.Add(layer);layer.RegisterValueChangedCallback(e=>board.EditingLayer=new[]{"all","tokens","map"}[layer.index]);
            Label(content,"Слой выбора помогает выделить стол под фигурой. Закреплённые объекты можно выбрать, но нельзя случайно сдвинуть.","muted");
        }
        private Texture2D CurrentMap()=>!string.IsNullOrEmpty(client.State.Active.mapId)?downloadedMap:
            client.State.Active.background=="blank"?null:Resources.Load<Texture2D>("Sanctuary");
        private void MapWorkspace(VisualElement content)
        {
            var state=client.State;
            Label(content,state.Active.name,"headline-small");
            Label(content,$"{state.Active.width} × {state.Active.height} клеток · {state.Active.cellFeet} фт в клетке","muted");
            if(!state.IsGm)return;
            Button(content,"Загрузить картинку карты",PickMap,"primary");
            Button(content,"Настроить сетку и масштаб",()=>OpenPanel("grid"));
            Label(content,"PNG / JPEG до 32 МБ. Большие картинки автоматически уменьшаются до 2048 пикселей для памяти телефона.","muted");
            Label(content,"СЦЕНЫ КАМПАНИИ","eyebrow");
            foreach(var scene in state.scenes)
            {
                var row=Row(content);row.AddToClassList("list-item");
                var label=Label(row,(scene.id==state.sceneId?"● ":"")+scene.name);label.style.flexGrow=1;
                Button(row,scene.id==state.sceneId?"Текущая":"Открыть",()=>{client.Act("scene.switch","sceneId",scene.id);ClosePanel();});
            }
            var name=Field(content,"Новая сцена","Новая локация");var add=Row(content);
            Button(add,"+ Пустая карта",()=>{client.Act("scene.add","name",name.value,"background","blank");ClosePanel();});
            Button(add,"+ Святилище",()=>{client.Act("scene.add","name",name.value,"background","");ClosePanel();});
            Button(content,"Удалить текущую сцену",()=>Confirm("Удалить сцену и все её фигуры? Последнюю сцену удалить нельзя.",()=>{client.Act("scene.delete");ClosePanel();}),"danger");
        }
        private void PrepareMap(byte[] bytes,string mime)
        {
            if(client.State==null || !client.State.IsGm || client.IsPreview)return;
            if(!MapImage.Fits(bytes)){ShowToast("Нужен PNG / JPEG до 6 МБ, 16 мегапикселей и 8192 пикселей по стороне");return;}
            ReleaseImport();
            var texture=MapImage.Decode(bytes);
            pendingTexture=texture;pendingImage=bytes;pendingMime=bytes[0]==137?"image/png":"image/jpeg";
            gridSceneId=client.State.sceneId;
            gridDraft=JsonUtility.FromJson<SceneState>(JsonUtility.ToJson(client.State.Active));
            gridDraft.mapPixelsPerCell=Mathf.Max((float)texture.width/gridDraft.width,(float)texture.height/80);
            gridDraft.height=Mathf.Max(MapLayout.RowsForImage(texture.width,texture.height,gridDraft.width),
                gridDraft.tokens.Length==0?5:Mathf.CeilToInt(gridDraft.tokens.Max(t=>t.y+t.size)));
            gridDraft.mapOffsetX=gridDraft.mapOffsetY=0;gridDraft.grid=true;gridDraft.gridOpacity=0.4f;
            OpenPanel("importMap");
        }
        private static float Finite(float value,float min,float max)=>float.IsNaN(value) || float.IsInfinity(value)?0:Mathf.Clamp(value,min,max);
        private void GridWorkspace(VisualElement content,bool importing)
        {
            if(!client.State.IsGm)return;
            if(!importing){gridSceneId=client.State.sceneId;gridDraft=JsonUtility.FromJson<SceneState>(JsonUtility.ToJson(client.State.Active));}
            Texture2D texture=importing?pendingTexture:CurrentMap();
            if(gridDraft==null || (importing && pendingImage==null)){Label(content,"Выбери картинку заново");return;}
            Label(content,importing?"1. Проверь картинку и сетку":"Сетка поверх карты","headline-small");
            var preview=new MapPreview();content.Add(preview);preview.Set(texture,gridDraft);
            if(texture!=null){
                Label(content,"Тяни картинку одним пальцем. Два пальца меняют её размер относительно сетки.","muted");
                var zoom=Row(content);Button(zoom,"Картинка −",()=>preview.ScaleImage(.8f));
                Button(zoom,"Вписать",preview.FitImage);Button(zoom,"Картинка +",()=>preview.ScaleImage(1.25f));
            }
            var advanced=new Foldout{text="Точная подгонка и параметры",value=!importing};
            var width=Number(advanced,"Ширина сцены, кл.",gridDraft.width);var height=Number(advanced,"Высота сцены, кл.",gridDraft.height);
            var ppc=new FloatField("Пикселей в клетке"){value=gridDraft.mapPixelsPerCell,isDelayed=true};advanced.Add(ppc);
            var ox=new FloatField("Сдвиг X, клеток"){value=gridDraft.mapOffsetX,isDelayed=true};advanced.Add(ox);
            var oy=new FloatField("Сдвиг Y, клеток"){value=gridDraft.mapOffsetY,isDelayed=true};advanced.Add(oy);
            preview.Changed=()=>{ppc.SetValueWithoutNotify(gridDraft.mapPixelsPerCell);ox.SetValueWithoutNotify(gridDraft.mapOffsetX);oy.SetValueWithoutNotify(gridDraft.mapOffsetY);};
            var feet=Number(advanced,"Футов в клетке",gridDraft.cellFeet);
            void Refresh(){preview.Set(texture,gridDraft);}
            width.RegisterValueChangedCallback(e=>{gridDraft.width=Mathf.Clamp(e.newValue,5,80);Refresh();});
            height.RegisterValueChangedCallback(e=>{gridDraft.height=Mathf.Clamp(e.newValue,5,80);Refresh();});
            ppc.RegisterValueChangedCallback(e=>{gridDraft.mapPixelsPerCell=Finite(e.newValue,0,8192);Refresh();});
            ox.RegisterValueChangedCallback(e=>{gridDraft.mapOffsetX=Finite(e.newValue,-80,80);Refresh();});
            oy.RegisterValueChangedCallback(e=>{gridDraft.mapOffsetY=Finite(e.newValue,-80,80);Refresh();});
            feet.RegisterValueChangedCallback(e=>gridDraft.cellFeet=Mathf.Clamp(e.newValue,1,100));
            if(texture!=null)
            {
                Label(content,"БЫСТРАЯ ПОДГОНКА","eyebrow");
                var columns=Number(content,"Клеток на картинке по ширине",Mathf.Clamp(Mathf.RoundToInt(texture.width/(gridDraft.mapPixelsPerCell>0?gridDraft.mapPixelsPerCell:(float)texture.width/gridDraft.width)),5,80));
                Button(content,"Подогнать по числу клеток",()=>{
                    int cols=Mathf.Clamp(columns.value,5,80);
                    gridDraft.mapPixelsPerCell=Mathf.Max((float)texture.width/cols,(float)texture.height/80);
                    gridDraft.width=Mathf.Max(cols,gridDraft.tokens.Length==0?5:Mathf.CeilToInt(gridDraft.tokens.Max(t=>t.x+t.size)));
                    gridDraft.height=Mathf.Max(MapLayout.RowsForImage(texture.width,texture.height,cols),gridDraft.tokens.Length==0?5:Mathf.CeilToInt(gridDraft.tokens.Max(t=>t.y+t.size)));
                    gridDraft.mapOffsetX=gridDraft.mapOffsetY=0;
                    width.SetValueWithoutNotify(gridDraft.width);height.SetValueWithoutNotify(gridDraft.height);
                    ppc.SetValueWithoutNotify(gridDraft.mapPixelsPerCell);ox.SetValueWithoutNotify(0);oy.SetValueWithoutNotify(0);Refresh();
                });
            }
            content.Add(advanced);
            Check(advanced,"Показывать сетку",gridDraft.grid,v=>{gridDraft.grid=v;Refresh();});
            Check(advanced,"Привязывать фигуры к клеткам",gridDraft.snap,v=>gridDraft.snap=v);
            var opacity=new Slider("Яркость сетки",0.1f,1f){value=gridDraft.gridOpacity>0?gridDraft.gridOpacity:0.4f};advanced.Add(opacity);
            opacity.RegisterValueChangedCallback(e=>{gridDraft.gridOpacity=e.newValue;Refresh();});
            var colors=new[]{"#ffffff","#182225","#e8bd78"};
            var color=new PopupField<string>("Цвет сетки",new List<string>{"Белая","Тёмная","Янтарная"},Mathf.Max(0,Array.IndexOf(colors,gridDraft.gridColor)));advanced.Add(color);
            color.RegisterValueChangedCallback(e=>{gridDraft.gridColor=colors[color.index];Refresh();});
            Label(advanced,"Карта сохраняет пропорции. Если сетка уже нарисована на картинке, укажи размер одной клетки в пикселях и подстрой сдвиг X/Y. Ноль пикселей — автоматическое вписывание.","muted");
            Button(content,importing?"2. Использовать эту карту":"Применить сетку",()=>{
                if(client.State.sceneId!=gridSceneId){ShowToast("Сцена сменилась. Открой настройку карты заново.");return;}
                if(client.State.Active.tokens.Any(t=>t.x+t.size>gridDraft.width || t.y+t.size>gridDraft.height)){ShowToast("Размер сцены слишком мал для фигур. Увеличь ширину или высоту.");return;}
                var settings=Json.Fields("sceneId",gridSceneId,"width",gridDraft.width,"height",gridDraft.height,"cellFeet",gridDraft.cellFeet,
                    "grid",gridDraft.grid,"snap",gridDraft.snap,"mapPixelsPerCell",gridDraft.mapPixelsPerCell,"mapOffsetX",gridDraft.mapOffsetX,
                    "mapOffsetY",gridDraft.mapOffsetY,"gridOpacity",gridDraft.gridOpacity>0?gridDraft.gridOpacity:0.4f,"gridColor",gridDraft.gridColor??"");
                if(importing)client.UploadMap(pendingImage,pendingMime,settings);else client.Act("scene.update",Json.Pairs(settings));
                ClosePanel();
            },"primary");
        }
        private void ObjectCard(VisualElement content,TokenState t)
        {
            var portrait=new Image{image=TokenCatalog.Texture(TokenCatalog.Art(t)),scaleMode=ScaleMode.ScaleToFit};
            portrait.style.height=110;content.Add(portrait);
            var name=Field(content,"Название",t.name);var size=Number(content,"Размер 1–4 клетки",t.size);
            var rotation=Number(content,"Поворот 0–359°",t.rotation);
            bool locked=t.locked,hidden=t.hidden;
            Check(content,"Закрепить на карте",locked,v=>locked=v);Check(content,"Скрыть от игроков",hidden,v=>hidden=v);
            Button(content,"Сохранить объект",()=>{client.Act("token.update","tokenId",t.id,"name",name.value,"size",size.value,"rotation",rotation.value,"locked",locked,"hidden",hidden);ClosePanel();},"primary");
            Button(content,"Дублировать",()=>{client.Act("token.duplicate","tokenId",t.id);ClosePanel();});
            Button(content,"Удалить объект",()=>Confirm("Удалить «"+t.name+"»?",()=>{client.Act("token.delete","tokenId",t.id);ClosePanel();}),"danger");
        }
        private void RefreshJournal()
        {
            if(journalItems==null || client.State==null)return;
            bool follow=journalScroll==null || journalScroll.verticalScroller.highValue-journalScroll.verticalScroller.value<50;
            journalItems.Clear();
            foreach(var m in client.State.messages.Reverse().Take(80).Reverse())
            {
                var item=new VisualElement();item.AddToClassList(m.kind=="roll"?"roll-message":m.kind=="combat"?"combat-message":"chat-message");
                Label(item,m.name,"eyebrow");Label(item,m.text);journalItems.Add(item);
            }
            if(follow && journalScroll!=null && journalItems.childCount>0)
            {var last=journalItems[journalItems.childCount-1];var scroll=journalScroll;journalItems.schedule.Execute(()=>{if(last.panel!=null)scroll.ScrollTo(last);});}
        }
    }
}
