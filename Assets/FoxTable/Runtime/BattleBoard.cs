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
        private readonly RoomClient client;
        private readonly Image map;
        private readonly VisualElement markings, pieces;
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
        public string Tool { get; set; } = "select";
        private const float CellSize = 48;
        private float Unit => CellSize * zoom;

        public BattleBoard(RoomClient client)
        {
            this.client = client;
            name = "battle-board";
            style.flexGrow = 1;
            style.overflow = Overflow.Hidden;
            style.backgroundColor = new Color(0.045f, 0.06f, 0.065f);
            map = new Image { pickingMode = PickingMode.Ignore, scaleMode = ScaleMode.StretchToFill };
            map.style.position = Position.Absolute;
            Add(map);
            markings = new VisualElement { pickingMode = PickingMode.Ignore };
            Stretch(markings);
            markings.generateVisualContent += Paint;
            Add(markings);
            pieces = new VisualElement { pickingMode = PickingMode.Ignore };
            Stretch(pieces);
            Add(pieces);
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCancelEvent>(e => ResetGesture());
            RegisterCallback<WheelEvent>(e => {
                var local = this.WorldToLocal(e.mousePosition);
                ZoomAt(local, zoom * Mathf.Exp(-e.delta.y * 0.06f));
                e.StopPropagation();
            });
            RegisterCallback<GeometryChangedEvent>(e => { if (scene != null) Refresh(); });
            schedule.Execute(() => {
                if (state?.effect != null && DateTimeOffset.UtcNow.ToUnixTimeSeconds()-state.effect.timestamp < 3)
                    markings.MarkDirtyRepaint();
            }).Every(60);
        }

        private static void Stretch(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.right = element.style.top = element.style.bottom = 0;
        }

        public void SetState(RoomState value)
        {
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
                ResetGesture();
                Fit();
            }
            Refresh();
        }

        public void SetMap(Texture2D texture) { map.image = texture; }
        public void Select(string id) { selectedId = id; Refresh(); }

        public void Fit()
        {
            if (scene == null) return;
            zoom = Mathf.Clamp(Mathf.Min((contentRect.width-30) / (scene.width * CellSize),
                (contentRect.height-30) / (scene.height * CellSize)), 0.12f, 3f);
            offset = new Vector2((contentRect.width-scene.width*Unit)/2, (contentRect.height-scene.height*Unit)/2);
            Refresh();
        }

        public void Zoom(float multiplier) { ZoomAt(contentRect.center, zoom * multiplier); }
        private void ZoomAt(Vector2 location, float value)
        {
            var world = ToCell(location);
            zoom = Mathf.Clamp(value, 0.12f, 3f);
            offset = location - world * Unit;
            Refresh();
        }

        private Vector2 ToCell(Vector2 local) => (local-offset)/Unit;
        private Vector2 ToLocal(float x, float y) => offset+new Vector2(x,y)*Unit;

        private TokenState Hit(Vector2 p)
        {
            if (scene == null) return null;
            foreach(var t in scene.tokens.OrderBy(t=>t.layer=="map"?0:t.layer=="gm"?2:1).Reverse())
            {
                if (new Rect(t.x,t.y,t.size,t.size).Contains(p)) return t;
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
                var points = new List<Vector2>(pointers.Values);
                pinching = true;
                pinchDistance = Mathf.Max(1, Vector2.Distance(points[0],points[1]));
                pinchZoom=zoom;
                pinchCenter=(points[0]+points[1])/2;
                pinchWorld=ToCell(pinchCenter);
                dragId=null;
                measuring=false;
                return;
            }
            moved=false;
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
                    selectedId=t.id;
                    if (!client.IsPreview && (state.IsGm || (t.ownerId==state.selfId && t.layer=="tokens" && !t.locked)))
                    { dragId=t.id; dragPosition=new Vector2(t.x,t.y); }
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
                var points=new List<Vector2>(pointers.Values);
                zoom=Mathf.Clamp(pinchZoom*Vector2.Distance(points[0],points[1])/pinchDistance,0.12f,3f);
                offset=(points[0]+points[1])/2-pinchWorld*Unit;
                Refresh();
                return;
            }
            if (pinching) return;
            moved |= Vector2.Distance(local,start)>7;
            if (Tool=="pan" || (Tool=="select" && dragId==null)) offset=panStart+local-start;
            else if (dragId!=null && moved)
            {
                var t=Array.Find(scene.tokens,v=>v.id==dragId);
                if (t==null) return;
                dragPosition=new Vector2(t.x,t.y)+ (local-start)/Unit;
                dragPosition.x=Mathf.Clamp(dragPosition.x,0,scene.width-t.size);
                dragPosition.y=Mathf.Clamp(dragPosition.y,0,scene.height-t.size);
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
            var local=this.WorldToLocal(e.position);
            pointers.Remove(e.pointerId);
            this.ReleasePointer(e.pointerId);
            if (pinching)
            {
                if (pointers.Count==0) ResetGesture();
                return;
            }
            if (Tool=="select" && !moved)
            {
                var picked=Hit(ToCell(local));
                if(picked!=null) Selected?.Invoke(picked.id);
            }
            if (dragId!=null && moved)
                client.Act("token.move", "tokenId",dragId,"x",dragPosition.x,"y",dragPosition.y);
            if (measuring && moved && Tool!="measure" && state.IsGm)
            {
                var end=ToCell(local);
                bool barrier=Tool=="wall" || Tool=="door" || Tool=="window";
                var from=anchor;
                if(barrier && scene.snap) {from=new Vector2(Mathf.Round(from.x),Mathf.Round(from.y));end=new Vector2(Mathf.Round(end.x),Mathf.Round(end.y));}
                client.Act(barrier?"barrier.add":"drawing.add",barrier?"kind":"shape",Tool,
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
                if(closest!=null)client.Act("door.toggle","barrierId",closest.id);
                else Hint?.Invoke("Нажмите на видимую дверь рядом с героем");
            }
            if (Tool=="ping")
            {
                var cell=ToCell(local);
                client.Act("ping","x",Mathf.Clamp(cell.x,0,scene.width),"y",Mathf.Clamp(cell.y,0,scene.height));
            }
            ResetGesture();
            Refresh();
        }

        private void ResetGesture()
        {
            foreach (var id in pointers.Keys) if (this.HasPointerCapture(id)) this.ReleasePointer(id);
            pointers.Clear();
            pinching=false; dragId=null; measuring=false; moved=false;
        }

        private void Fog(Vector2 cell)
        {
            if (!state.IsGm || cell.x<0 || cell.y<0 || cell.x>=scene.width || cell.y>=scene.height) return;
            lastFog=Time.unscaledTime;
            client.Act("fog.paint","x",Mathf.FloorToInt(cell.x),"y",Mathf.FloorToInt(cell.y),"radius",1,"reveal",Tool=="reveal");
        }

        private void Refresh()
        {
            if (scene==null) return;
            map.style.left=offset.x; map.style.top=offset.y;
            map.style.width=scene.width*Unit; map.style.height=scene.height*Unit;
            pieces.Clear();
            foreach (var t in scene.tokens.OrderBy(t=>t.layer=="map"?0:t.layer=="gm"?2:1))
            {
                var piece=new VisualElement {pickingMode=PickingMode.Ignore};
                piece.AddToClassList("piece");
                Vector2 pos=t.id==dragId && moved ? dragPosition : new Vector2(t.x,t.y);
                piece.style.left=offset.x+pos.x*Unit+3;
                piece.style.top=offset.y+pos.y*Unit+3;
                piece.style.width=t.size*Unit-6; piece.style.height=t.size*Unit-6;
                if (ColorUtility.TryParseHtmlString(t.color,out Color color)) piece.style.backgroundColor=color;
                var letter=new Label(t.name.Length>0?t.name.Substring(0,1).ToUpperInvariant():"?") {pickingMode=PickingMode.Ignore};
                letter.style.fontSize=Mathf.Clamp(Unit*t.size*0.43f,10,36);
                piece.Add(letter);
                piece.style.opacity=t.hidden || t.layer=="gm" ? 0.4f : 1;
                if(t.layer=="map") {piece.style.borderTopLeftRadius=4;piece.style.borderTopRightRadius=4;piece.style.borderBottomLeftRadius=4;piece.style.borderBottomRightRadius=4;}
                if (t.id==selectedId || t.id==scene.turnId) piece.AddToClassList("selected-piece");
                var hp=new VisualElement {pickingMode=PickingMode.Ignore};
                hp.AddToClassList("hp-bar");
                hp.style.width=Length.Percent(t.maxHp>0 ? 100f*t.hp/t.maxHp:0);
                piece.Add(hp);
                pieces.Add(piece);
                if (Unit>25)
                {
                    var label=new Label(t.name+(t.conditions.Length>0?" •":"")) {pickingMode=PickingMode.Ignore};
                    label.AddToClassList("piece-name");
                    label.style.left=offset.x+pos.x*Unit-20; label.style.top=offset.y+(pos.y+t.size)*Unit;
                    label.style.width=t.size*Unit+40;
                    pieces.Add(label);
                }
            }
            markings.MarkDirtyRepaint();
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
                Color grid=new Color(0.7f,0.78f,0.74f,0.19f);
                for(int x=0;x<=scene.width;x++) Line(p,ToLocal(x,0),ToLocal(x,scene.height),grid);
                for(int y=0;y<=scene.height;y++) Line(p,ToLocal(0,y),ToLocal(scene.width,y),grid);
            }
            foreach(var t in scene.tokens)
                if(t.auraFeet>0)Shape(p,"circle",ToLocal(t.x+t.size/2f,t.y+t.size/2f),
                    ToLocal(t.x+t.size/2f+(float)t.auraFeet/scene.cellFeet,t.y+t.size/2f));
            foreach(var d in scene.drawings) Shape(p,d.shape,ToLocal(d.x,d.y),ToLocal(d.ex,d.ey));
            if(measuring) Shape(p,Tool,ToLocal(anchor.x,anchor.y),ToLocal(dragPosition.x,dragPosition.y));
            if(scene.fog)
            {
                Color fog=new Color(0.025f,0.035f,0.045f,state.IsGm ? 0.62f:1);
                for(int y=0;y<scene.height;y++)
                for(int x=0;x<scene.width;x++)
                    if(!revealed.Contains(y*100+x))
                    {
                        Vector2 pos=ToLocal(x,y);
                        if(pos.x+Unit<0 || pos.y+Unit<0 || pos.x>contentRect.width || pos.y>contentRect.height)continue;
                        Rect(p,pos.x,pos.y,Unit+0.5f,Unit+0.5f,fog);
                    }
            }
            if(scene.lighting && !state.IsGm)
            {
                for(int y=0;y<scene.height;y++)
                for(int x=0;x<scene.width;x++)
                {
                    if(currentCells.Contains(y*100+x))continue;
                    Vector2 pos=ToLocal(x,y);
                    if(pos.x+Unit<0 || pos.y+Unit<0 || pos.x>contentRect.width || pos.y>contentRect.height)continue;
                    bool explored=scene.explorer && exploredCells.Contains(y*100+x);
                    Rect(p,pos.x,pos.y,Unit+0.5f,Unit+0.5f,new Color(0.025f,0.035f,0.045f,explored?0.78f:1));
                }
            }
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
