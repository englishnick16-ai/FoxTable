using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable.Editor
{
    public static class WorkspaceChecks
    {
        private static int checks;
        private static void Check(bool value,string label){if(!value)throw new Exception("Workspace check failed: "+label);checks++;}
        private static List<VisualElement> Pieces(VisualElement parent)
        {
            var all=new List<VisualElement>();
            foreach(var child in parent.Children()){if(child.ClassListContains("piece"))all.Add(child);all.AddRange(Pieces(child));}
            return all;
        }
        [UnityEditor.MenuItem("FoxTable/Check map workspace")]
        public static void Run()
        {
            checks=0;
            Check(Resources.Load<Texture2D>("Sanctuary")!=null,"startup map is a 2D texture");
            var font=Resources.Load<Font>("DejaVuSans");Check(font!=null && font.HasCharacter('Ж'),"startup Cyrillic font");
            Check(Resources.Load<StyleSheet>("FoxTable")!=null && Resources.Load<ThemeStyleSheet>("FoxTheme")!=null,"startup styles and theme");
            Check(TokenCatalog.All.Length==32 && TokenCatalog.All.Select(m=>m.id).Distinct().Count()==32,"catalog identities");
            foreach(var entry in TokenCatalog.All)Check(entry.Texture!=null && entry.Texture.width>0,"miniature imported: "+entry.id);
            var scene=LocalGame.Create().Active;
            var auto=MapLayout.ImageCells(scene,1000,500);
            Check(Mathf.Abs(auto.width/auto.height-2)<0.0001f,"automatic placement preserves image aspect");
            Check(auto.x>=0 && auto.y>=0 && auto.xMax<=scene.width+0.001f && auto.yMax<=scene.height+0.001f,"automatic image contained in board");
            scene.mapPixelsPerCell=50;scene.mapOffsetX=-0.25f;scene.mapOffsetY=0.75f;
            var aligned=MapLayout.ImageCells(scene,1000,500);
            Check(aligned==new Rect(-0.25f,0.75f,20,10),"pixel grid calibration and offsets");
            var legacy=JsonUtility.FromJson<SceneState>("{\"id\":\"legacy\",\"width\":30,\"height\":22}");
            Check(MapLayout.ImageCells(legacy,600,300).width==30,"legacy scene without new fields");
            Check(MapLayout.RowsForImage(1000,500,30)==15,"image row suggestion");
            var location=MapLayout.Place(scene,new Vector2(-20,999),3);
            Check(location.x==0 && location.y==scene.height-3,"stamp clamped to board");
            var game=new LocalGame(new System.Random(4));var state=LocalGame.Create();
            var b=Json.Fields(TokenCatalog.Placement(TokenCatalog.Find("knight"),state.Active,new Vector2(4.5f,5.5f)));b["type"]="token.add";
            state=game.ApplyCopy(state,b);var knight=state.Active.tokens.Last();
            Check(knight.artId=="knight" && knight.x==4 && knight.y==5 && knight.hp==38 && knight.ac==18,"catalog spawn is complete");
            state=game.ApplyCopy(state,Json.Fields("type","token.update","tokenId",knight.id,"rotation",90));
            Check(state.Active.tokens.Last().rotation==90,"rotation saved");
            state=game.ApplyCopy(state,Json.Fields("type","scene.update","mapPixelsPerCell",50,"mapOffsetX",-.25f,"gridColor","#ffffff","gridOpacity",.7f));
            var reopened=JsonUtility.FromJson<RoomState>(JsonUtility.ToJson(state));
            Check(reopened.Active.mapPixelsPerCell==50 && reopened.Active.mapOffsetX==-.25f && reopened.Active.tokens.Last().artId=="knight","new data survives serialization");
            state=game.ApplyCopy(state,Json.Fields("type","scene.add","name","Поле","background","blank"));
            Check(state.Active.background=="blank","blank scene is distinct from default background");
            var board=new BattleBoard(null);state=LocalGame.Create();board.SetState(state);
            var pieces=Pieces(board);Check(pieces.Count==4,"initial retained board pieces");
            var first=pieces[0];
            for(int i=0;i<30;i++){board.SetState(state);board.Select(state.Active.tokens[0].id);}
            Check(Pieces(board).Count==4 && Pieces(board).Any(v=>System.Object.ReferenceEquals(v,first)),"state/selection updates retain existing visual elements");
            state=game.ApplyCopy(state,Json.Fields("type","token.delete","tokenId",state.Active.tokens[0].id));
            board.SetState(state);Check(Pieces(board).Count==3,"deleted visual removed without leaking elements");
            var preview=new MapPreview();preview.Set(TokenCatalog.Find("knight").Texture,state.Active);
            Check(preview.childCount==2,"preview initializes before a layout is available");
            var png=new byte[24];png[0]=137;png[1]=80;png[2]=78;png[3]=71;png[12]=73;png[13]=72;png[14]=68;png[15]=82;
            png[18]=4;png[22]=4;Check(MapImage.Fits(png),"PNG size inspected before texture allocation");
            png[16]=1;Check(!MapImage.Fits(png),"oversized compressed image rejected");
            Check(!MapImage.Fits(new byte[30]) && !MapImage.Fits(null),"unknown image rejected");
            Debug.Log("FoxTable workspace checks passed: "+checks);
        }
    }
}
