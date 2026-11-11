using System;
using UnityEngine;

namespace FoxTable
{
    public static class MapLayout
    {
        public static Rect ImageCells(SceneState scene, int pixelWidth, int pixelHeight)
        {
            if(pixelWidth<=0 || pixelHeight<=0) return new Rect(0,0,scene.width,scene.height);
            float ppc=scene.mapPixelsPerCell;
            bool automatic=ppc<=0;
            if(automatic)ppc=Mathf.Max((float)pixelWidth/scene.width,(float)pixelHeight/scene.height);
            float width=pixelWidth/ppc,height=pixelHeight/ppc;
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
        }
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
            if(scene==null || !scene.grid || Unit<=0)return;
            var p=context.painter2D;p.lineWidth=1;
            Color color=Color.white;if(!string.IsNullOrEmpty(scene.gridColor))ColorUtility.TryParseHtmlString(scene.gridColor,out color);
            color.a=scene.gridOpacity>0?scene.gridOpacity:0.4f;p.strokeColor=color;
            for(int x=0;x<=scene.width;x++){p.BeginPath();p.MoveTo(Origin+new Vector2(x*Unit,0));p.LineTo(Origin+new Vector2(x*Unit,scene.height*Unit));p.Stroke();}
            for(int y=0;y<=scene.height;y++){p.BeginPath();p.MoveTo(Origin+new Vector2(0,y*Unit));p.LineTo(Origin+new Vector2(scene.width*Unit,y*Unit));p.Stroke();}
        }
    }
}
