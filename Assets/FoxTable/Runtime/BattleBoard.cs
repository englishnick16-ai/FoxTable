using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable
{
    public sealed class BattleBoard : VisualElement
    {
        public Action<string> Selected;
        public Action<string> Hint;
        public Action<Vector2> Placed;
        private readonly RoomClient client;
        private readonly Image map;
        private readonly VisualElement world, markings, pieces, overlay;
        private readonly Image fogImage;
        private Texture2D fogTexture;
        private Color32[] fogPixels;
        private TokenState[] hitOrder=Array.Empty<TokenState>();
        private readonly Action<string,object[]> dispatch;
        private sealed class PendingMove {public Vector2 point;public int revision;public float time;}
        private readonly Dictionary<string,PendingMove> pendingMoves=new Dictionary<string,PendingMove>();
        private bool panOnly, labelsVisible;
        private Texture2D mapTexture;
        private readonly Dictionary<string,PieceView> views=new Dictionary<string,PieceView>();
        private sealed class PieceView { public VisualElement root,hp;public Image art;public Label letter,label; }
        private bool needsFit=true;
        private Vector2 dragOrigin;
        private RoomState state;
        private SceneState scene;
        private readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
        private HashSet<int> revealed = new HashSet<int>();
        private readonly HashSet<int> currentCells = new HashSet<int>(), exploredCells = new HashSet<int>();
        private float zoom = 1, pinchDistance, pinchZoom;
        private Vector2 offset = new Vector2(30, 30), start, anchor, panStart, dragPosition;
        private Vector2 pinchCenter, pinchWorld;
        private string dragId, activeScene, selectedId;
        private bool pinching, moved, measuring;
        private float lastFog;
        private string tool="select";
        public string Tool { get=>tool;set{if(tool!=value)ResetGesture();tool=value;} }
        public string SelectedId=>selectedId;
        public string EditingLayer {get;set;}="all";
        private const float CellSize = 48;
        private float Unit => CellSize * zoom;

        public BattleBoard(RoomClient client,Action<string,object[]> dispatch=null)
        {
            this.client = client;
            this.dispatch=dispatch ?? ((type,args)=>client?.Act(type,args));
            name = "battle-board";
            style.flexGrow = 1;
            style.minHeight=100;
            world=new VisualElement {name="board-world",pickingMode=PickingMode.Ignore,usageHints=UsageHints.DynamicTransform | UsageHints.GroupTransform};
            world.style.position=Position.Absolute;world.style.left=0;world.style.top=0;
            world.style.transformOrigin=new TransformOrigin(0,0,0);
            Add(world);
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.045f, 0.06f, 0.065f);
            map = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            map.style.position = Position.Absolute;
            world.Add(map);
            markings = new VisualElement { pickingMode = PickingMode.Ignore };
            Stretch(markings);
            markings.generateVisualContent += Paint;
            world.Add(markings);
            fogImage=new Image {name="board-fog",pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill};
            Stretch(fogImage);world.Add(fogImage);
            pieces = new VisualElement { pickingMode = PickingMode.Ignore };
            Stretch(pieces);
            world.Add(pieces);
            overlay=new VisualElement {pickingMode=PickingMode.Ignore};Stretch(overlay);Add(overlay);
            overlay.generateVisualContent+=PaintOverlay;
            RegisterCallback<DetachFromPanelEvent>(e=>{ResetGesture();if(fogTexture!=null)UnityEngine.Object.Destroy(fogTexture);fogTexture=null;});
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCancelEvent>(e => ResetGesture());
            RegisterCallback<WheelEvent>(e => {
                var local = this.WorldToLocal(e.mousePosition);
                ZoomAt(local, zoom * Mathf.Exp(-e.delta.y * 0.06f));
                e.StopPropagation();
            });
            RegisterCallback<GeometryChangedEvent>(e => { if (scene != null) {if(needsFit)Fit();else Refresh();} });
            schedule.Execute(() => {
                foreach(var id in pendingMoves.Keys.ToArray())if(Time.unscaledTime-pendingMoves[id].time>10){
                    pendingMoves.Remove(id);var token=scene==null?null:Array.Find(scene.tokens,t=>t.id==id);
                    if(token!=null)UpdatePiecePosition(token,new Vector2(token.x,token.y));
                }
                if (state?.effect != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds()-state.effect.timestamp < 4 || state?.ping!=null && DateTimeOffset.UtcNow.ToUnixTimeSeconds()-state.ping.timestamp<6)
                    overlay.MarkDirtyRepaint();
            }).Every(60);
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.right = element.style.top = element.style.bottom = 0;
        }

        public void SetState(RoomState value)
        {
            bool layoutChanged=scene==null || scene.id!=value.Active.id || scene.width!=value.Active.width || scene.height!=value.Active.height || scene.mapId!=value.Active.mapId;
            state = value;
            scene = value.Active;
            revealed.Clear(); currentCells.Clear(); exploredCells.Clear();
            foreach (var cell in scene.currentCells) currentCells.Add(cell.y*100+cell.x);
            foreach (var cell in scene.exploredCells) exploredCells.Add(cell.y*100+cell.x);
            foreach (var cell in scene.revealed) revealed.Add(cell.y * 100 + cell.x);
            if (activeScene != scene.id)
            {
                activeScene = scene.id;
                selectedId = null;
                needsFit=true;
                ResetGesture();
                Fit();
            }
            else if(layoutChanged){ResetGesture();needsFit=true;Fit();}
            if(selectedId!=null && !scene.tokens.Any(t=>t.id==selectedId)){selectedId=null;Selected?.Invoke(null);}
            foreach(var id in pendingMoves.Keys.ToArray())
                if(state.revision>pendingMoves[id].revision || !scene.tokens.Any(t=>t.id==id))pendingMoves.Remove(id);
            world.style.width=scene.width*CellSize;world.style.height=scene.height*CellSize;
            SyncPieces();UpdateMapGeometry();UpdateFog();
            UpdateSelection();foreach(var view in views.Values)view.label.style.display=Unit>22?DisplayStyle.Flex:DisplayStyle.None;
            markings.MarkDirtyRepaint();overlay.MarkDirtyRepaint();
            Refresh();
        }

        public void SetMap(Texture2D texture) { mapTexture=texture;map.image = texture;UpdateMapGeometry();Refresh(); }
        public void Select(string id) { selectedId = id; UpdateSelection(); }
        public void CancelInteraction(){ResetGesture();Refresh();}
        public void RejectPendingMoves()
        {
            var ids=pendingMoves.Keys.ToArray();pendingMoves.Clear();
            if(scene==null)return;
            foreach(var id in ids){var t=Array.Find(scene.tokens,v=>v.id==id);if(t!=null)UpdatePiecePosition(t,new Vector2(t.x,t.y));}
        }

        public void Fit()
        {
            if (scene == null) return;
            if(float.IsNaN(contentRect.width) || float.IsNaN(contentRect.height) || contentRect.width<30 || contentRect.height<30){needsFit=true;return;}
            needsFit=false;
            zoom = Mathf.Clamp(Mathf.Min((contentRect.width-30) / (scene.width * CellSize),
                (contentRect.height-30) / (scene.height * CellSize)), 0.05f, 3f);
            offset = new Vector2((contentRect.width-scene.width*Unit)/2, (contentRect.height-scene.height*Unit)/2);
            Refresh();
        }

        public void Zoom(float multiplier) { ZoomAt(contentRect.center, zoom * multiplier); }
        private void ZoomAt(Vector2 location, float value)
        {
            if(float.IsNaN(location.x) || float.IsNaN(location.y))return;
            var world = ToCell(location);
            zoom = Mathf.Clamp(value, 0.05f, 3f);
            offset = location - world * Unit;
            Refresh();
        }

        private Vector2 ToCell(Vector2 local) => (local-offset)/Unit;
        private Vector2 ToLocal(float x, float y) => offset+new Vector2(x,y)*Unit;
        private static Vector2 World(float x,float y)=>new Vector2(x,y)*CellSize;

        private TokenState Hit(Vector2 p)
        {
            if (scene == null) return null;
            for(int i=hitOrder.Length-1;i>=0;i--)
            {
                var t=hitOrder[i];
                if(EditingLayer!="all" && t.layer!=EditingLayer)continue;
                // A minimum touch target makes a one-cell figure selectable while zoomed out.
                float pad=Mathf.Max(0,(28/Unit-t.size)/2);
                var point=pendingMoves.TryGetValue(t.id,out var pending)?pending.point:new Vector2(t.x,t.y);
                if(new Rect(point.x-pad,point.y-pad,t.size+pad*2,t.size+pad*2).Contains(p))return t;
            }
            return null;
        }

        private void Down(PointerDownEvent e)
        {
            if (scene == null || e.button > 0) return;
            Vector2 local = this.WorldToLocal(e.position);
            pointers[e.pointerId]=local;
            this.CapturePointer(e.pointerId);
            if (pointers.Count>=2)
            {
                RestoreDragPreview();
                GetPair(out var first,out var second);
                pinching = true;
                pinchDistance = Mathf.Max(1, Vector2.Distance(first,second));
                pinchZoom=zoom;
                pinchCenter=(first+second)/2;
                pinchWorld=ToCell(pinchCenter);
                dragId=null;
                measuring=false;
                return;
            }
            moved=false;panOnly=false;
            start=local;
            anchor=ToCell(local);
            panStart=offset;
            dragPosition=anchor;
            dragId=null;
            if (Tool=="select")
            {
                var t=Hit(anchor);
                if (t!=null)
                {
                    Select(t.id);
                    if (!(client?.IsPreview??false) && !t.locked && (state.IsGm || (t.ownerId==state.selfId && t.layer=="tokens")))
                    { dragId=t.id; dragPosition=dragOrigin=pendingMoves.TryGetValue(t.id,out var pending)?pending.point:new Vector2(t.x,t.y); }
                }
                Refresh();
            }
            else if (Tool=="reveal" || Tool=="conceal") Fog(anchor);
            else if (Tool=="measure" || Tool=="circle" || Tool=="cone" || Tool=="line" || Tool=="wall" || Tool=="door" || Tool=="window") measuring=true;
            e.StopPropagation();
        }

        private void Move(PointerMoveEvent e)
        {
            if (!pointers.ContainsKey(e.pointerId) || scene == null) return;
            Vector2 local=this.WorldToLocal(e.position);
            pointers[e.pointerId]=local;
            if (pinching && pointers.Count>=2)
            {
                GetPair(out var first,out var second);
                zoom=Mathf.Clamp(pinchZoom*Vector2.Distance(first,second)/pinchDistance,0.05f,3f);
                offset=(first+second)/2-pinchWorld*Unit;
                Refresh();
                return;
            }
            if (pinching) return;
            moved |= Vector2.Distance(local,start)>7;
            if (panOnly || Tool=="pan" || Tool=="place" || (Tool=="select" && dragId==null)) offset=panStart+local-start;
            else if (dragId!=null && moved)
            {
                var t=Array.Find(scene.tokens,v=>v.id==dragId);
                if (t==null) return;
                dragPosition=dragOrigin+ (local-start)/Unit;
                dragPosition.x=Mathf.Clamp(dragPosition.x,0,scene.width-t.size);
                dragPosition.y=Mathf.Clamp(dragPosition.y,0,scene.height-t.size);
                UpdatePiecePosition(t,dragPosition);
            }
            else if (measuring)
            {
                dragPosition=ToCell(local);
                float distance=Mathf.Max(Mathf.Abs(dragPosition.x-anchor.x),Mathf.Abs(dragPosition.y-anchor.y))*scene.cellFeet;
                Hint?.Invoke($"{distance:0.#} фт · диагональ = 1 клетка");
            }
            else if ((Tool=="reveal" || Tool=="conceal") && Time.unscaledTime-lastFog>0.2f) Fog(ToCell(local));
            Refresh();
            e.StopPropagation();
        }

        private void Up(PointerUpEvent e)
        {
            if (!pointers.ContainsKey(e.pointerId)) return;
            // Touch release can have an unset position. Use the last tracked point.
            var local=pointers[e.pointerId];
            pointers.Remove(e.pointerId);
            this.ReleasePointer(e.pointerId);
            if (pinching)
            {
                if(pointers.Count==0)ResetGesture();
                else {
                    // Continue panning with the remaining finger instead of requiring both to lift.
                    foreach(var point in pointers.Values){start=point;break;}
                    panStart=offset;pinching=false;panOnly=true;moved=true;dragId=null;measuring=false;
                }
                return;
            }
            if (Tool=="select" && !moved)
            {
                var picked=Hit(ToCell(local));
                selectedId=picked?.id;Selected?.Invoke(selectedId);
            }
            if(Tool=="place" && !moved && !(client?.IsPreview??false))Placed?.Invoke(ToCell(local));
            if (dragId!=null && moved) {
                var point=scene.snap?new Vector2(Mathf.Floor(dragPosition.x+.5f),Mathf.Floor(dragPosition.y+.5f)):dragPosition;
                pendingMoves[dragId]=new PendingMove {point=point,revision=state.revision,time=Time.unscaledTime};
                var token=Array.Find(scene.tokens,t=>t.id==dragId);if(token!=null)UpdatePiecePosition(token,point);
                dispatch("token.move",new object[]{"tokenId",dragId,"x",point.x,"y",point.y});
                Selected?.Invoke(dragId);
            }
            if (measuring && moved && Tool!="measure" && state.IsGm)
            {
                var end=ToCell(local);
                bool barrier=Tool=="wall" || Tool=="door" || Tool=="window";
                var from=anchor;
                if(barrier && scene.snap) {from=new Vector2(Mathf.Round(from.x),Mathf.Round(from.y));end=new Vector2(Mathf.Round(end.x),Mathf.Round(end.y));}
                Send(barrier?"barrier.add":"drawing.add",barrier?"kind":"shape",Tool,
                    "x",Mathf.Clamp(from.x,0,scene.width),"y",Mathf.Clamp(from.y,0,scene.height),
                    "ex",Mathf.Clamp(end.x,0,scene.width),"ey",Mathf.Clamp(end.y,0,scene.height));
            }
            if (Tool=="doorUse" && !moved)
            {
                var point=ToCell(local);
                Barrier closest=null;float nearest=Mathf.Max(0.6f,18/Unit);
                foreach(var wall in scene.barriers)
                {
                    if(wall.kind!="door" && wall.kind!="window")continue;
                    var a=new Vector2(wall.x,wall.y);var b=new Vector2(wall.ex,wall.ey);
                    var delta=b-a;
                    float t=delta.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(point-a,delta)/delta.sqrMagnitude):0;
                    float distance=Vector2.Distance(point,a+delta*t);
                    if(distance<nearest){nearest=distance;closest=wall;}
                }
                if(closest!=null)Send("door.toggle","barrierId",closest.id);
                else Hint?.Invoke("Нажмите на видимую дверь рядом с героем");
            }
            if (Tool=="ping")
            {
                var cell=ToCell(local);
                Send("ping","x",Mathf.Clamp(cell.x,0,scene.width),"y",Mathf.Clamp(cell.y,0,scene.height));
            }
            ResetGesture();
            Refresh();
        }

        private void Send(string type,params object[] args)=>dispatch(type,args);
        private void GetPair(out Vector2 first,out Vector2 second)
        {
            var it=pointers.Values.GetEnumerator();it.MoveNext();first=it.Current;it.MoveNext();second=it.Current;
        }

        private void ResetGesture()
        {
            RestoreDragPreview();
            var ids=pointers.Keys.ToArray();pointers.Clear();
            foreach (var id in ids) if (this.HasPointerCapture(id)) this.ReleasePointer(id);
            pointers.Clear();
            pinching=false; panOnly=false;dragId=null; measuring=false; moved=false;
            overlay.MarkDirtyRepaint();
        }
        private void RestoreDragPreview()
        {
            if(dragId==null || scene==null)return;
            var t=Array.Find(scene.tokens,v=>v.id==dragId);if(t==null)return;
            var point=pendingMoves.TryGetValue(t.id,out var pending)?pending.point:new Vector2(t.x,t.y);
            UpdatePiecePosition(t,point);
        }

        private void Fog(Vector2 cell)
        {
            if (!state.IsGm || cell.x<0 || cell.y<0 || cell.x>=scene.width || cell.y>=scene.height) return;
            lastFog=Time.unscaledTime;
            Send("fog.paint","x",Mathf.FloorToInt(cell.x),"y",Mathf.FloorToInt(cell.y),"radius",1,"reveal",Tool=="reveal");
        }

        private void SyncPieces()
        {
            var alive=new HashSet<string>(scene.tokens.Select(t=>t.id));
            foreach(var id in views.Keys.Where(id=>!alive.Contains(id)).ToArray())
            { views[id].root.RemoveFromHierarchy();views[id].label.RemoveFromHierarchy();views.Remove(id); }
            hitOrder=scene.tokens.OrderBy(t=>t.layer=="map"?0:t.layer=="gm"?2:1).ToArray();
            foreach(var t in hitOrder)
            {
                if(!views.TryGetValue(t.id,out var view))
                {
                    view=new PieceView {root=new VisualElement{pickingMode=PickingMode.Ignore,usageHints=UsageHints.DynamicTransform},
                        art=new Image{pickingMode=PickingMode.Ignore,scaleMode=ScaleMode.ScaleToFit},
                        letter=new Label{pickingMode=PickingMode.Ignore},hp=new VisualElement{pickingMode=PickingMode.Ignore},
                        label=new Label{pickingMode=PickingMode.Ignore,usageHints=UsageHints.DynamicTransform}};
                    view.root.AddToClassList("piece");view.art.AddToClassList("miniature-art");
                    view.hp.AddToClassList("hp-bar");view.label.AddToClassList("piece-name");
                    view.root.Add(view.art);view.root.Add(view.letter);view.root.Add(view.hp);
                    pieces.Add(view.root);pieces.Add(view.label);views.Add(t.id,view);
                }
                view.root.BringToFront();view.label.BringToFront();
                view.art.image=TokenCatalog.Texture(TokenCatalog.Art(t));
                view.art.style.display=view.art.image==null?DisplayStyle.None:DisplayStyle.Flex;
                view.art.transform.rotation=Quaternion.Euler(0,0,t.rotation);
                view.letter.style.display=view.art.image==null?DisplayStyle.Flex:DisplayStyle.None;
                view.letter.text=string.IsNullOrEmpty(t.name)?"?":t.name.Substring(0,1).ToUpperInvariant();
                view.root.EnableInClassList("prop-piece",t.kind=="object");
                if(ColorUtility.TryParseHtmlString(t.color,out Color color))
                    view.root.style.backgroundColor=t.kind=="object"?Color.clear:new Color(color.r*0.35f,color.g*0.35f,color.b*0.35f,0.95f);
                view.root.style.opacity=t.hidden || t.layer=="gm"?0.4f:1;
                view.hp.style.display=t.kind=="object"?DisplayStyle.None:DisplayStyle.Flex;
                view.hp.style.width=Length.Percent(t.maxHp>0?100f*t.hp/t.maxHp:0);
                view.label.text=t.name+(t.conditions.Length>0?" •":"")+(t.locked?" · закреплён":"");
                view.root.style.left=0;view.root.style.top=0;
                view.root.style.width=t.size*CellSize-2;view.root.style.height=t.size*CellSize-2;
                view.letter.style.fontSize=Mathf.Clamp(CellSize*t.size*.43f,10,36);
                view.label.style.left=0;view.label.style.top=0;view.label.style.width=t.size*CellSize+40;
                Vector2 point=t.id==dragId && moved?dragPosition:pendingMoves.TryGetValue(t.id,out var pending)?pending.point:new Vector2(t.x,t.y);
                UpdatePiecePosition(t,point);
            }
        }
        private void UpdateMapGeometry()
        {
            if(scene==null)return;
            var bounds=MapLayout.ImageCells(scene,mapTexture==null?0:mapTexture.width,mapTexture==null?0:mapTexture.height);
            map.style.left=bounds.x*CellSize;map.style.top=bounds.y*CellSize;
            map.style.width=bounds.width*CellSize;map.style.height=bounds.height*CellSize;
        }
        private void UpdatePiecePosition(TokenState t,Vector2 point)
        {
            if(!views.TryGetValue(t.id,out var view))return;
            view.root.transform.position=new Vector3(point.x*CellSize+1,point.y*CellSize+1,0);
            view.label.transform.position=new Vector3(point.x*CellSize-20,(point.y+t.size)*CellSize,0);
        }
        private void UpdateSelection()
        {
            if(scene==null)return;
            foreach(var t in scene.tokens)if(views.TryGetValue(t.id,out var view))
                view.root.EnableInClassList("selected-piece",t.id==selectedId || t.id==scene.turnId);
        }
        private void Refresh()
        {
            if(scene==null)return;
            // One GPU transform. No per-token layout changes or grid/fog tessellation while panning.
            world.transform.position=new Vector3(offset.x,offset.y,0);
            world.transform.scale=new Vector3(zoom,zoom,1);
            bool show=Unit>22;
            if(show!=labelsVisible){labelsVisible=show;foreach(var view in views.Values)view.label.style.display=show?DisplayStyle.Flex:DisplayStyle.None;}
            if(measuring || scene.barriers.Length>0 || state.effect!=null || state.ping!=null)overlay.MarkDirtyRepaint();
        }
        private void UpdateFog()
        {
            bool use=scene.fog || scene.lighting && !state.IsGm;
            fogImage.style.display=use?DisplayStyle.Flex:DisplayStyle.None;
            if(!use)return;
            if(fogTexture==null || fogTexture.width!=scene.width || fogTexture.height!=scene.height){
                if(fogTexture!=null)UnityEngine.Object.Destroy(fogTexture);
                fogTexture=new Texture2D(scene.width,scene.height,TextureFormat.RGBA32,false){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                fogPixels=new Color32[scene.width*scene.height];fogImage.image=fogTexture;
            }
            for(int y=0;y<scene.height;y++)for(int x=0;x<scene.width;x++){
                int key=y*100+x;float alpha=scene.fog && !revealed.Contains(key)?(state.IsGm?.62f:1):0;
                if(scene.lighting && !state.IsGm && !currentCells.Contains(key)){
                    float light=scene.explorer && exploredCells.Contains(key)?.78f:1;alpha=1-(1-alpha)*(1-light);
                }
                fogPixels[(scene.height-1-y)*scene.width+x]=new Color32(6,9,11,(byte)Mathf.RoundToInt(alpha*255));
            }
            fogTexture.SetPixels32(fogPixels);fogTexture.Apply(false);
            UpdateSelection();
        }

        private static void Rect(Painter2D p, float x, float y, float w, float h, Color color)
        {
            p.fillColor=color; p.BeginPath(); p.MoveTo(new Vector2(x,y));
            p.LineTo(new Vector2(x+w,y)); p.LineTo(new Vector2(x+w,y+h));
            p.LineTo(new Vector2(x,y+h)); p.ClosePath(); p.Fill();
        }

        private static void Line(Painter2D p, Vector2 a, Vector2 b, Color color, float width=1)
        {
            p.strokeColor=color; p.lineWidth=width; p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
        }

        private void Shape(Painter2D p, string shape, Vector2 a, Vector2 b)
        {
            p.strokeColor=new Color(0.9f,0.68f,0.32f,0.9f);
            p.fillColor=new Color(0.9f,0.68f,0.32f,0.16f);
            p.lineWidth=2;
            if (shape=="line" || shape=="measure" || shape=="wall" || shape=="door" || shape=="window") { Line(p,a,b,p.strokeColor,2); return; }
            p.BeginPath();
            float radius=Vector2.Distance(a,b);
            if (shape=="circle")
            {
                for(int i=0;i<=48;i++)
                {
                    float angle=i*Mathf.PI*2/48;
                    var point=a+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    if(i==0)p.MoveTo(point);else p.LineTo(point);
                }
            }
            else
            {
                float angle=Mathf.Atan2(b.y-a.y,b.x-a.x);
                p.MoveTo(a);
                p.LineTo(a+new Vector2(Mathf.Cos(angle-0.46365f),Mathf.Sin(angle-0.46365f))*radius);
                p.LineTo(a+new Vector2(Mathf.Cos(angle+0.46365f),Mathf.Sin(angle+0.46365f))*radius);
            }
            p.ClosePath(); p.Fill(); p.Stroke();
        }

        private void Paint(MeshGenerationContext context)
        {
            if(scene==null)return;
            var p=context.painter2D;
            if(scene.grid)
            {
                Color grid=Color.white;
                if(!string.IsNullOrEmpty(scene.gridColor))ColorUtility.TryParseHtmlString(scene.gridColor,out grid);
                grid.a=scene.gridOpacity>0?scene.gridOpacity:0.4f;
                for(int x=0;x<=scene.width;x++) Line(p,World(x,0),World(x,scene.height),grid);
                for(int y=0;y<=scene.height;y++) Line(p,World(0,y),World(scene.width,y),grid);
            }
            foreach(var t in scene.tokens)
                if(t.auraFeet>0)Shape(p,"circle",World(t.x+t.size/2f,t.y+t.size/2f),
                    World(t.x+t.size/2f+(float)t.auraFeet/scene.cellFeet,t.y+t.size/2f));
            foreach(var d in scene.drawings) Shape(p,d.shape,World(d.x,d.y),World(d.ex,d.ey));
        }
        private void PaintOverlay(MeshGenerationContext context)
        {
            if(scene==null)return;
            var p=context.painter2D;
            if(measuring)Shape(p,Tool,ToLocal(anchor.x,anchor.y),ToLocal(dragPosition.x,dragPosition.y));
            foreach(var wall in scene.barriers)
            {
                Color color=wall.kind=="wall"?new Color(0.8f,0.65f,0.4f):wall.kind=="window"?new Color(0.35f,0.75f,0.9f):wall.open?new Color(0.4f,0.8f,0.55f):new Color(0.95f,0.55f,0.25f);
                if(wall.secret)color=new Color(0.75f,0.4f,0.85f);
                Line(p,ToLocal(wall.x,wall.y),ToLocal(wall.ex,wall.ey),color,wall.open?2:4);
            }
            if(state.effect!=null && state.effect.sceneId==scene.id && DateTimeOffset.UtcNow.ToUnixTimeSeconds()-state.effect.timestamp<3)
            {
                var target=Array.Find(scene.tokens,t=>t.id==state.effect.tokenId);
                if(target!=null)
                {
                    float pulse=(Mathf.Sin(Time.unscaledTime*14)+1)/2;
                    var point=ToLocal(target.x+target.size/2f,target.y+target.size/2f);
                    p.strokeColor=state.effect.kind=="critical"?new Color(1,0.78f,0.2f):new Color(1,0.25f,0.15f);
                    p.lineWidth=2+3*pulse;p.BeginPath();
                    for(int i=0;i<=36;i++)
                    {
                        float angle=i*Mathf.PI*2/36;
                        var v=point+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*Unit*(target.size/2f+0.12f+pulse*0.15f);
                        if(i==0)p.MoveTo(v);else p.LineTo(v);
                    }
                    p.Stroke();
                }
            }
            if(state.ping!=null && state.ping.sceneId==scene.id && DateTimeOffset.UtcNow.ToUnixTimeSeconds()-state.ping.timestamp<5)
                Shape(p,"circle",ToLocal(state.ping.x,state.ping.y),ToLocal(state.ping.x+0.5f,state.ping.y));
        }
    }
}
