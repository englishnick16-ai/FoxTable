using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace FoxTable
{
    public sealed class TerrainLayer : VisualElement
    {
        private readonly Dictionary<string,VisualElement> views=new Dictionary<string,VisualElement>();
        public TerrainLayer(){pickingMode=PickingMode.Ignore;style.position=Position.Absolute;style.left=style.right=style.top=style.bottom=0;}
        public void Set(TerrainStroke[] strokes){
            strokes=strokes??Array.Empty<TerrainStroke>();var ids=new HashSet<string>(strokes.Select(s=>s.id));
            foreach(var id in views.Keys.Where(id=>!ids.Contains(id)).ToArray()){views[id].RemoveFromHierarchy();views.Remove(id);}
            foreach(var stroke in strokes)if(!views.ContainsKey(stroke.id)){
                var view=new VisualElement{pickingMode=PickingMode.Ignore};view.style.position=Position.Absolute;view.style.left=view.style.right=view.style.top=view.style.bottom=0;
                view.generateVisualContent+=context=>Paint(context.painter2D,stroke,p=>new Vector2(p.x,p.y)*48,48);
                views[stroke.id]=view;Add(view);
            }
        }
        public static Color Ground(string value){switch(value){case "grass":return new Color(.38f,.49f,.35f);case "sand":return new Color(.74f,.65f,.46f);case "snow":return new Color(.80f,.85f,.84f);case "paper":return new Color(.72f,.65f,.49f);default:return new Color(.045f,.06f,.065f);}}
        private static void Stroke(Painter2D p,TerrainStroke s,Func<MapPoint,Vector2> project,float width,Color color){
            if(s.points==null || s.points.Length==0)return;p.strokeColor=color;p.lineWidth=Mathf.Max(.1f,width);
            p.BeginPath();var start=project(s.points[0]);p.MoveTo(start);
            if(s.points.Length==1)p.LineTo(start+new Vector2(.01f,.01f));else foreach(var point in s.points.Skip(1))p.LineTo(project(point));p.Stroke();
            // Round joints without relying on a platform-specific cap style.
            p.fillColor=color;
            foreach(var point in s.points){var c=project(point);p.BeginPath();for(int i=0;i<12;i++){float a=i*Mathf.PI/6;var q=c+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*width/2;if(i==0)p.MoveTo(q);else p.LineTo(q);}p.ClosePath();p.Fill();}
        }
        public static void Paint(Painter2D p,TerrainStroke s,Func<MapPoint,Vector2> project,float unit){
            float w=s.width*unit;
            if(s.kind=="river" || s.kind=="water"){
                Stroke(p,s,project,w+unit*.45f,new Color(.30f,.38f,.26f));Stroke(p,s,project,w,new Color(.18f,.40f,.49f));Stroke(p,s,project,w*.62f,new Color(.24f,.52f,.59f));
            } else if(s.kind=="road"){
                Stroke(p,s,project,w+unit*.18f,new Color(.32f,.29f,.23f));Stroke(p,s,project,w,new Color(.70f,.62f,.45f));Stroke(p,s,project,w*.55f,new Color(.76f,.69f,.52f));
            } else {Color c=s.kind=="sand"?new Color(.78f,.68f,.47f):s.kind=="snow"?new Color(.88f,.92f,.91f):s.kind=="forest"?new Color(.23f,.37f,.24f):new Color(.45f,.58f,.38f);Stroke(p,s,project,w,c);}
        }
    }
}
