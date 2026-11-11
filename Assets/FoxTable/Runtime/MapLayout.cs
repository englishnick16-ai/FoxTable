using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable
{
    public static class MapLayout
    {
        public static Rect ImageCells(SceneState scene, int pixelWidth, int pixelHeight)
        {
            if(pixelWidth<=0 || pixelHeight<=0) return new Rect(0,0,scene.width,scene.height);
            double ppc=scene.mapPixelsPerCell;
            bool automatic=ppc<=0;
            if(automatic)ppc=Math.Max((double)pixelWidth/scene.width,(double)pixelHeight/scene.height);
            float width=(float)(pixelWidth/ppc),height=(float)(pixelHeight/ppc);
            return new Rect(scene.mapOffsetX+(automatic?(scene.width-width)/2:0),
                scene.mapOffsetY+(automatic?(scene.height-height)/2:0),width,height);
        }
        public static int RowsForImage(int width,int height,int columns)
        { return Mathf.Clamp(Mathf.CeilToInt((float)height*columns/width),5,80); }
        public static Vector2 Place(SceneState s,Vector2 point,int size)
        {
            float x=Mathf.Clamp(point.x-size/2f,0,s.width-size),y=Mathf.Clamp(point.y-size/2f,0,s.height-size);
            return s.snap?new Vector2(Mathf.Floor(x+0.5f),Mathf.Floor(y+0.5f)):new Vector2(x,y);
        }
    }
    public sealed class MapPreview : UnityEngine.UIElements.VisualElement
    {
        public Action Changed;
        private readonly System.Collections.Generic.Dictionary<int,Vector2> fingers=new System.Collections.Generic.Dictionary<int,Vector2>();
        private Vector2 start,shift,center,imageAnchor;
        private float startPpc,distance;
        private bool pinch;
        private readonly UnityEngine.UIElements.Image image;
        private readonly UnityEngine.UIElements.VisualElement lines;
        private Texture2D texture;
        private SceneState scene;
        public MapPreview()
        {
            AddToClassList("map-preview");
            image=new UnityEngine.UIElements.Image{pickingMode=UnityEngine.UIElements.PickingMode.Ignore,scaleMode=ScaleMode.StretchToFill};
            image.style.position=UnityEngine.UIElements.Position.Absolute;Add(image);
            lines=new UnityEngine.UIElements.VisualElement{pickingMode=UnityEngine.UIElements.PickingMode.Ignore};
            lines.style.position=UnityEngine.UIElements.Position.Absolute;
            lines.style.left=lines.style.right=lines.style.top=lines.style.bottom=0;
            Add(lines);lines.generateVisualContent+=Draw;
            RegisterCallback<UnityEngine.UIElements.GeometryChangedEvent>(e=>Refresh());
            RegisterCallback<UnityEngine.UIElements.PointerDownEvent>(e=>{
                if(texture==null || scene==null || Unit<=0 || !float.IsFinite(Unit))return;
                fingers[e.pointerId]=this.WorldToLocal(e.position);this.CapturePointer(e.pointerId);BeginGesture();e.StopPropagation();
            });
            RegisterCallback<UnityEngine.UIElements.PointerMoveEvent>(e=>{
                if(!fingers.ContainsKey(e.pointerId))return;
                fingers[e.pointerId]=this.WorldToLocal(e.position);
                if(pinch && fingers.Count>=2){
                    Pair(out var a,out var b);float scale=Mathf.Max(.1f,Vector2.Distance(a,b)/distance);
                    scene.mapPixelsPerCell=Mathf.Clamp(startPpc/scale,.5f,8192);
                    float actual=startPpc/scene.mapPixelsPerCell;
                    var at=((a+b)/2-Origin)/Unit;
                    scene.mapOffsetX=at.x-imageAnchor.x*actual;scene.mapOffsetY=at.y-imageAnchor.y*actual;
                } else {
                    var delta=(fingers[e.pointerId]-start)/Unit;
                    scene.mapOffsetX=shift.x+delta.x;scene.mapOffsetY=shift.y+delta.y;
                }
                scene.mapOffsetX=Mathf.Clamp(scene.mapOffsetX,-80,80);scene.mapOffsetY=Mathf.Clamp(scene.mapOffsetY,-80,80);
                Refresh();Changed?.Invoke();e.StopPropagation();
            });
            RegisterCallback<UnityEngine.UIElements.PointerUpEvent>(e=>{
                if(!fingers.Remove(e.pointerId))return;
                this.ReleasePointer(e.pointerId);if(fingers.Count>0)BeginGesture();e.StopPropagation();
            });
            RegisterCallback<UnityEngine.UIElements.PointerCancelEvent>(e=>{fingers.Clear();pinch=false;});
        }
        private void Pair(out Vector2 a,out Vector2 b){var it=fingers.Values.GetEnumerator();it.MoveNext();a=it.Current;it.MoveNext();b=it.Current;}
        private void Manual()
        {
            if(scene.mapPixelsPerCell>0)return;
            var rect=MapLayout.ImageCells(scene,texture.width,texture.height);
            scene.mapPixelsPerCell=texture.width/rect.width;scene.mapOffsetX=rect.x;scene.mapOffsetY=rect.y;
        }
        private void BeginGesture()
        {
            Manual();startPpc=scene.mapPixelsPerCell;shift=new Vector2(scene.mapOffsetX,scene.mapOffsetY);pinch=fingers.Count>=2;
            if(pinch){Pair(out var a,out var b);distance=Mathf.Max(1,Vector2.Distance(a,b));center=(a+b)/2;imageAnchor=(center-Origin)/Unit-shift;}
            else foreach(var p in fingers.Values){start=p;break;}
        }
        public void ScaleImage(float factor)
        {
            if(texture==null || scene==null || factor<=0 || !float.IsFinite(factor))return;
            Manual();var old=MapLayout.ImageCells(scene,texture.width,texture.height);
            scene.mapPixelsPerCell=Mathf.Clamp(scene.mapPixelsPerCell/factor,.5f,8192);
            var next=MapLayout.ImageCells(scene,texture.width,texture.height);
            scene.mapOffsetX=Mathf.Clamp(old.center.x-next.width/2,-80,80);scene.mapOffsetY=Mathf.Clamp(old.center.y-next.height/2,-80,80);
            Refresh();Changed?.Invoke();
        }
        public void FitImage(){if(scene==null)return;scene.mapPixelsPerCell=0;scene.mapOffsetX=scene.mapOffsetY=0;Refresh();Changed?.Invoke();}
        public void Set(Texture2D value,SceneState settings){texture=value;scene=settings;image.image=value;Refresh();}
        private float Unit=>scene==null?1:Mathf.Min((contentRect.width-16)/scene.width,(contentRect.height-16)/scene.height);
        private Vector2 Origin=>new Vector2((contentRect.width-scene.width*Unit)/2,(contentRect.height-scene.height*Unit)/2);
        private void Refresh()
        {
            if(scene==null || float.IsNaN(contentRect.width) || float.IsNaN(contentRect.height) || contentRect.width<20 || contentRect.height<20)return;
            var rect=MapLayout.ImageCells(scene,texture==null?0:texture.width,texture==null?0:texture.height);
            image.style.left=Origin.x+rect.x*Unit;image.style.top=Origin.y+rect.y*Unit;
            image.style.width=rect.width*Unit;image.style.height=rect.height*Unit;
            lines.MarkDirtyRepaint();
        }
        private void Draw(UnityEngine.UIElements.MeshGenerationContext context)
        {
            if(scene==null || !scene.grid || Unit<=0 || !float.IsFinite(Unit) || !float.IsFinite(Origin.x) || !float.IsFinite(Origin.y))return;
            var p=context.painter2D;p.lineWidth=1;
            Color color=Color.white;if(!string.IsNullOrEmpty(scene.gridColor))ColorUtility.TryParseHtmlString(scene.gridColor,out color);
            color.a=scene.gridOpacity>0?scene.gridOpacity:0.4f;p.strokeColor=color;
            for(int x=0;x<=scene.width;x++){p.BeginPath();p.MoveTo(Origin+new Vector2(x*Unit,0));p.LineTo(Origin+new Vector2(x*Unit,scene.height*Unit));p.Stroke();}
            for(int y=0;y<=scene.height;y++){p.BeginPath();p.MoveTo(Origin+new Vector2(0,y*Unit));p.LineTo(Origin+new Vector2(scene.width*Unit,y*Unit));p.Stroke();}
        }
    }
}
