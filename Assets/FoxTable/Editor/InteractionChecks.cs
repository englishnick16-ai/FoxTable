using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable.Editor
{
    // Deterministic UI-event scenarios, not a substitute for Android lifecycle/device tests.
    public static class InteractionChecks
    {
        private static int checks;
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Check(bool value,string label){if(!value)throw new Exception("Interaction check failed: "+label);checks++;}
        private static void Layout(VisualElement view,float width,float height)
        {
            // A synchronous pre-export check has no frame-driven panel layout. Supply a fixed test viewport.
            typeof(VisualElement).GetProperty("layout").SetValue(view,new Rect(0,0,width,height));
        }
        private static void Pointer<T>(VisualElement view,string method,int id,Vector2 position) where T:PointerEventBase<T>,new()
        {
            using(var e=PointerEventBase<T>.GetPooled()){
                typeof(PointerEventBase<T>).GetProperty("position").SetValue(e,new Vector3(position.x,position.y,0));
                typeof(PointerEventBase<T>).GetProperty("pointerId").SetValue(e,id);
                typeof(PointerEventBase<T>).GetProperty("button").SetValue(e,0);
                typeof(BattleBoard).GetMethod(method,Private).Invoke(view,new object[]{e});
            }
        }
        private static Vector2 Point(BattleBoard board,float x,float y)=>(Vector2)typeof(BattleBoard).GetMethod("ToLocal",Private).Invoke(board,new object[]{x,y});
        private static void DisposeBoard(BattleBoard board)
        {
            var texture=board.Q<Image>("board-fog")?.image as Texture2D;
            if(texture!=null)UnityEngine.Object.DestroyImmediate(texture);
        }
        [UnityEditor.MenuItem("FoxTable/Check touch and image regressions")]
        public static void Run()
        {
            checks=0;
            var game=new LocalGame(new System.Random(4));var state=LocalGame.Create();var t=state.Active.tokens[0];
            state=game.ApplyCopy(state,Json.Fields("type","token.move","tokenId",t.id,"x",state.Active.width-1,"y",state.Active.height-1));
            state=game.ApplyCopy(state,Json.Fields("type","token.update","tokenId",t.id,"size",4));
            t=state.Active.tokens[0];Check(t.size==4 && t.x==state.Active.width-4 && t.y==state.Active.height-4,"resize at bottom-right edge succeeds and stays inside board");
            state=game.ApplyCopy(state,Json.Fields("type","token.update","tokenId",t.id,"size",1));
            Check(state.Active.tokens[0].x==state.Active.width-4,"shrinking keeps position");
            state=LocalGame.Create();t=state.Active.tokens[0];
            var commands=new List<KeyValuePair<string,object[]>>();
            var board=new BattleBoard(null,(kind,args)=>commands.Add(new KeyValuePair<string,object[]>(kind,args)));
            Layout(board,480,500);board.SetState(state);
            var world=board.Q("board-world");
            var before=world.transform.position;board.Tool="pan";
            Pointer<PointerDownEvent>(board,"Down",1,new Vector2(100,100));
            Pointer<PointerMoveEvent>(board,"Move",1,new Vector2(160,125));
            Pointer<PointerUpEvent>(board,"Up",1,Vector2.zero);
            Check(Vector2.Distance((Vector2)(world.transform.position-before),new Vector2(60,25))<.01f,"one finger pans map; release does not jump to zero");
            Check(commands.Count==0,"panning does not send a game command");
            before=world.transform.position;
            Pointer<PointerDownEvent>(board,"Down",1,new Vector2(100,100));
            Pointer<PointerDownEvent>(board,"Down",2,new Vector2(200,100));
            float scale=world.transform.scale.x;
            Pointer<PointerMoveEvent>(board,"Move",2,new Vector2(250,100));
            Check(Mathf.Abs(world.transform.scale.x/scale-1.5f)<.01f,"two-finger pinch changes camera scale");
            Pointer<PointerUpEvent>(board,"Up",2,Vector2.zero);before=world.transform.position;
            Pointer<PointerMoveEvent>(board,"Move",1,new Vector2(135,120));
            Check(Vector2.Distance((Vector2)(world.transform.position-before),new Vector2(35,20))<.01f,"remaining finger continues to pan after pinch");
            Pointer<PointerUpEvent>(board,"Up",1,Vector2.zero);
            board.Fit();board.Tool="select";var originalPosition=new Vector2(t.x,t.y);var origin=Point(board,t.x+.5f,t.y+.5f);var end=Point(board,t.x+2.5f,t.y+1.5f);
            Pointer<PointerDownEvent>(board,"Down",1,origin);Pointer<PointerMoveEvent>(board,"Move",1,end);Pointer<PointerUpEvent>(board,"Up",1,Vector2.zero);
            Check(commands.Count==1 && commands[0].Key=="token.move","drag sends exactly one move command");
            var move=Json.Fields(commands[0].Value);Check(Mathf.Abs(Convert.ToSingle(move["x"])-(t.x+2))<.01f && Mathf.Abs(Convert.ToSingle(move["y"])-(t.y+1))<.01f,"drag uses start position and retains snapped endpoint");
            Check(new Vector2(t.x,t.y)==originalPosition,"local drag preview does not mutate authoritative snapshot");
            int count=commands.Count;
            board.Tool="pan";Pointer<PointerDownEvent>(board,"Down",1,origin);
            var pieces=world.Query<VisualElement>(className:"piece").ToList();var widths=pieces.Select(p=>p.style.width).ToArray();var positions=pieces.Select(p=>p.transform.position).ToArray();
            var timer=System.Diagnostics.Stopwatch.StartNew();
            for(int i=0;i<500;i++)Pointer<PointerMoveEvent>(board,"Move",1,origin+new Vector2(i*.25f,i*.1f));
            timer.Stop();Pointer<PointerUpEvent>(board,"Up",1,Vector2.zero);
            Check(pieces.Select((p,i)=>p.style.width==widths[i] && p.transform.position==positions[i]).All(v=>v),"500 pan samples preserve per-piece layout and transforms");
            Check(commands.Count==count,"camera gestures do not enqueue state copies or saves");
            Debug.Log("FoxTable synthetic 500 pan events: "+timer.ElapsedMilliseconds+" ms in Editor (not Android FPS)");
            DisposeBoard(board);
            // A dense fog is a single small texture, not thousands of tessellated paths per pointer event.
            state=LocalGame.Create();state.Active.width=state.Active.height=80;state.Active.fog=true;
            board=new BattleBoard(null);Layout(board,480,500);board.SetState(state);
            var fog=(Texture2D)board.Q<Image>("board-fog").image;
            Check(fog.width==80 && fog.height==80,"largest board fog occupies only 6400 pixels");
            var oldFog=fog;board.Zoom(1.2f);board.Zoom(.8f);Check(ReferenceEquals(oldFog,board.Q<Image>("board-fog").image),"camera zoom does not rebuild fog texture");
            DisposeBoard(board);
            var source=new Texture2D(64,32,TextureFormat.RGBA32,false);source.SetPixel(0,0,Color.red);source.Apply();
            byte[] png=source.EncodeToPNG(),jpeg=source.EncodeToJPG();
            for(int i=0;i<5;i++)foreach(var bytes in new[]{png,jpeg}){
                var decoded=MapImage.Decode(bytes);Check(decoded.width==64 && decoded.height==32 && !decoded.isReadable && decoded.mipmapCount==1,"PNG/JPEG decode releases CPU copy and has no mip chain");UnityEngine.Object.DestroyImmediate(decoded);
            }
            UnityEngine.Object.DestroyImmediate(source);
            var draft=LocalGame.Create().Active;draft.mapPixelsPerCell=50;draft.mapOffsetX=1;draft.mapOffsetY=2;
            var texture=TokenCatalog.Find("knight").Texture;var preview=new MapPreview();preview.Set(texture,draft);
            var rect=MapLayout.ImageCells(draft,texture.width,texture.height);preview.ScaleImage(2);
            var larger=MapLayout.ImageCells(draft,texture.width,texture.height);
            Check(Mathf.Abs(larger.width-rect.width*2)<.001f && Vector2.Distance(rect.center,larger.center)<.001f,"image resize preserves its center relative to grid");
            preview.FitImage();Check(draft.mapPixelsPerCell==0 && draft.mapOffsetX==0 && draft.mapOffsetY==0,"image fit resets calibration without changing figures");
            Debug.Log("FoxTable interaction checks passed: "+checks);
        }
    }
}
