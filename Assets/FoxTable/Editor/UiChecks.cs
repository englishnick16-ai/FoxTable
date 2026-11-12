using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace FoxTable.Editor
{
    // Render the actual C#/USS UI through Unity's offscreen runtime panel, never an HTML imitation.
    public static class UiChecks
    {
        private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Call(object owner,string method,params object[] args)=>owner.GetType().GetMethod(method,Private).Invoke(owner,args);
        private static object Field(object owner,string name)=>owner.GetType().GetField(name,Private).GetValue(owner);
        private static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,Private).SetValue(owner,value);
        private static void Check(bool ok,string label){if(!ok)throw new Exception("UI check failed: "+label);}
        private static void Tick(VisualElement root){
            Check(root.panel!=null,"runtime panel attached");
            root.panel.GetType().GetMethod("ValidateLayout").Invoke(root.panel,null);
            var runtime=typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            runtime.GetMethod("UpdatePanels",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,null);
            root.panel.GetType().GetMethod("Repaint").Invoke(root.panel,new object[]{new Event{type=EventType.Repaint}});
        }
        private static void Capture(FoxTableApp app,VisualElement root,PanelSettings settings,int width,int height,string name){
            var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();settings.targetTexture=target;settings.scaleMode=PanelScaleMode.ConstantPixelSize;settings.scale=1;
            root.style.width=width;root.style.height=height;root.EnableInClassList("landscape",width>height);
            Tick(root);Tick(root);
            var board=root.Q<BattleBoard>();
            if(name.EndsWith("table")){
                Check(board.layout.height>=(width>height?140:300),name+" canvas stays usable");
                foreach(var b in root.Query<Button>().ToList().Where(b=>b.parent.ClassListContains("bottom-nav") || b.parent.ClassListContains("tools-main"))){
                    Check(b.layout.height>=43 && b.layout.width>=43,name+" touch target "+b.text);
                    Check(b.resolvedStyle.fontSize>=14,name+" readable navigation "+b.text);
                    Check(b.worldBound.x>=-1 && b.worldBound.xMax<=width+1,name+" navigation stays on screen "+b.text);
                }
            }
            if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null){Debug.LogWarning("FOXTABLE_UI_RENDER_UNAVAILABLE: null graphics device; layout checks ran");settings.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);return;}
            var runtime=typeof(VisualElement).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            runtime.GetMethod("RenderOffscreenPanels",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Invoke(null,null);
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
            byte[] bytes=image.EncodeToPNG();Directory.CreateDirectory("Builds/UI-QA");File.WriteAllBytes("Builds/UI-QA/"+name+".png",bytes);
            Debug.Log("FOXTABLE_UI_PNG|"+name+"|"+Convert.ToBase64String(bytes));
            UnityEngine.Object.DestroyImmediate(image);settings.targetTexture=null;UnityEngine.Object.DestroyImmediate(target);
        }
        public static void Run(){
            var go=new GameObject("FoxTable-UI-QA"){hideFlags=HideFlags.HideAndDontSave};FoxTableApp app=null;PanelSettings settings=null;
            string temp=Path.Combine(Path.GetTempPath(),"foxtable-ui-"+Guid.NewGuid().ToString("N"));
            try{
                app=go.AddComponent<FoxTableApp>();if(Field(app,"root")==null)Call(app,"Awake");
                settings=(PanelSettings)Field(app,"settings");var root=(VisualElement)Field(app,"root");
                var state=LocalGame.Create();state.scenes=new[]{MapBuilder.Create("Город у реки","city",64)};state.sceneId=state.scenes[0].id;
                var store=new LocalCampaignStore(temp);store.Write(state);var client=(RoomClient)Field(app,"client");Set(client,"localStore",store);client.OpenLocal();
                Capture(app,root,settings,400,860,"portrait-table");Capture(app,root,settings,860,400,"landscape-table");
                Call(app,"OpenPanel","catalog");Capture(app,root,settings,400,860,"portrait-catalog");
                Call(app,"PrepareMap",File.ReadAllBytes("Assets/FoxTable/Resources/Sanctuary.png"),"image/png");Capture(app,root,settings,400,860,"portrait-image-grid");
                Debug.Log("FoxTable UI layout checks passed: portrait, landscape, catalog, image grid");
            } finally {
                if(app!=null){var pending=Field(app,"pendingTexture") as Texture2D;if(pending!=null){Set(app,"pendingTexture",null);UnityEngine.Object.DestroyImmediate(pending);}Set(app,"settings",null);}
                UnityEngine.Object.DestroyImmediate(go);if(settings!=null)UnityEngine.Object.DestroyImmediate(settings);
                if(Directory.Exists(temp))Directory.Delete(temp,true);
            }
        }
    }
}
