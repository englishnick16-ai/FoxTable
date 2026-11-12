using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace FoxTable.Editor
{
    public static class BuilderChecks
    {
        private static int checks;
        private static void Check(bool value,string label){if(!value)throw new Exception("Builder check failed: "+label);checks++;}
        public static void Run(){
            checks=0;var game=new LocalGame(new System.Random(42));var state=LocalGame.Create();string old=state.sceneId;
            state=game.ApplyCopy(state,Json.Fields("type","scene.add","name","Город","preset","city","cells",256));
            Check(state.scenes.Length==2 && state.sceneId!=old,"blueprint creates a separate scene");
            Check(state.Active.width==256 && state.Active.tokens.Length>25 && state.Active.terrain.Any(t=>t.kind=="river"),"large city has models and river");
            Check(state.Active.tokens.All(t=>t.x>=0 && t.y>=0 && t.x+t.size<=256 && t.y+t.size<=256 && TokenCatalog.Find(t.artId)!=null),"all city models exist and fit board");
            foreach(var name in new[]{"forest","region","empty"}){
                state=game.ApplyCopy(state,Json.Fields("type","scene.add","name",name,"preset",name,"cells",128));Check(state.Active.width==128,"preset "+name);
            }
            state=game.ApplyCopy(state,Json.Fields("type","scene.update","width",256,"height",256));
            state=game.ApplyCopy(state,Json.Fields("type","fog.paint","x",200,"y",0,"radius",0,"reveal",true));
            state=game.ApplyCopy(state,Json.Fields("type","fog.paint","x",0,"y",2,"radius",0,"reveal",true));
            Check(state.Active.revealed.Length==2 && state.Active.revealed.Any(p=>p.x==200 && p.y==0),"large-map fog cells do not collide with former stride100");
            var points=new object[]{Json.Fields("x",0,"y",0),Json.Fields("x",250,"y",255)};
            state=game.ApplyCopy(state,Json.Fields("type","terrain.add","kind","river","width",4,"points",points));
            Check(state.Active.terrain.Length==1 && state.Active.terrain[0].points[1].y==255,"stroke reaches far side of large map");
            var copy=JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));Check(copy.Active.terrain[0].kind=="river","landscape survives serialization");
            bool rejected=false;try{game.ApplyCopy(state,Json.Fields("type","terrain.add","kind","road","width",2,"points",new object[]{Json.Fields("x",float.NaN,"y",1)}));}catch(ArgumentException){rejected=true;}
            Check(rejected && state.Active.terrain.Length==1,"bad stroke rejected transactionally");
            state=game.ApplyCopy(state,Json.Fields("type","terrain.undo"));Check(state.Active.terrain.Length==0,"last landscape stroke removed");
            var board=new BattleBoard(null);typeof(VisualElement).GetProperty("layout").SetValue(board,new Rect(0,0,400,600));board.SetState(state);
            var world=board.Q("board-world");Check(world.transform.scale.x*256*48<=400 && world.transform.scale.y*256*48<=600,"256-cell map fits phone viewport");
            var layer=new TerrainLayer();var stroke=new TerrainStroke{id="fixed",kind="river",width=2,points=new[]{new MapPoint{x=1,y=1}}};
            layer.Set(new[]{stroke});var retained=layer[0];layer.Set(new[]{stroke});Check(ReferenceEquals(retained,layer[0]),"terrain keeps existing geometry on snapshots");layer.Set(Array.Empty<TerrainStroke>());Check(layer.childCount==0,"removed terrain view cleaned");
            // Real Windows Editor filesystem round-trip, separate from any user's campaign.
            string path=Path.Combine(Path.GetTempPath(),"foxtable-builder-"+Guid.NewGuid().ToString("N"));
            try{var store=new LocalCampaignStore(path);store.Write(copy);var loaded=store.Load(out _);Check(loaded.Active.terrain.Length==1 && loaded.Active.width==256,"large world and terrain survive save/reopen");}finally{if(Directory.Exists(path))Directory.Delete(path,true);}
            Debug.Log("FoxTable builder checks passed: "+checks);
        }
    }
}
