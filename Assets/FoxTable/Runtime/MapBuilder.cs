using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace FoxTable
{
    [Serializable] public sealed class MapPoint {public float x,y;}
    [Serializable] public sealed class TerrainStroke {public string id,kind;public float width;public MapPoint[] points=Array.Empty<MapPoint>();}
    [Serializable] public sealed class MapProp {public string art;public float x,y;public int size;}
    [Serializable] public sealed class MapBlueprint {public string id,name,background;public MapProp[] props;public TerrainStroke[] terrain;}
    [Serializable] public sealed class MapBlueprints {public MapBlueprint[] items;}
    public static class MapBuilder
    {
        public const int MaxCells=256,CellStride=1024,MaxObjects=1000,MaxStrokes=256;
        public static readonly string[] Brushes={"river","road","water","grass","sand","snow","forest"};
        public static bool Background(string value)=>new[]{"","blank","grass","sand","snow","paper"}.Contains(value);
        public static SceneState Create(string name,string preset,int cells)
        {
            if(cells<5 || cells>MaxCells)throw new ArgumentException("Размер карты: 5–256 клеток");
            var resource=Resources.Load<TextAsset>("WorldPresets");
            var blueprint=resource==null?null:Array.Find(JsonUtility.FromJson<MapBlueprints>(resource.text).items,p=>p.id==preset);
            if(blueprint==null)throw new ArgumentException("Неизвестный макет карты");
            var scene=LocalGame.NewScene(name);scene.width=scene.height=cells;scene.background=blueprint.background;scene.grid=false;scene.snap=false;
            float scale=cells/64f;
            scene.terrain=blueprint.terrain.Select(t=>new TerrainStroke{id=Guid.NewGuid().ToString("N"),kind=t.kind,width=Mathf.Clamp(t.width*scale,.25f,32),points=t.points.Select(p=>new MapPoint{x=p.x*cells,y=p.y*cells}).ToArray()}).ToArray();
            scene.tokens=blueprint.props.Select(p=>{
                var item=TokenCatalog.Find(p.art);int size=Mathf.Clamp(Mathf.RoundToInt(p.size*scale),1,16);
                return new TokenState{id=Guid.NewGuid().ToString("N").Substring(0,16),name=item?.name??"Объект",kind="object",artId=p.art,color="#98b690",size=size,
                    x=Mathf.Clamp(p.x*cells,0,cells-size),y=Mathf.Clamp(p.y*cells,0,cells-size),hp=12,maxHp=12,ac=12,layer="map",ownerId="",damage="1d6",speed=30};
            }).ToArray();return scene;
        }
        public static void AddStroke(SceneState scene,Dictionary<string,object> fields)
        {
            var previous=scene.terrain??Array.Empty<TerrainStroke>();
            if(previous.Length>=MaxStrokes)throw new ArgumentException("До 256 штрихов на сцену");
            if(!fields.ContainsKey("kind") || !fields.ContainsKey("width") || !fields.TryGetValue("points",out var raw) || !(raw is IEnumerable sequence))throw new ArgumentException("Неполный штрих");
            string kind=Convert.ToString(fields["kind"]);if(!Brushes.Contains(kind))throw new ArgumentException("Неизвестная кисть");
            float width=Convert.ToSingle(fields["width"],CultureInfo.InvariantCulture);
            if(!float.IsFinite(width) || width<.25f || width>32)throw new ArgumentException("Ширина кисти: 0.25–32 клетки");
            var points=new List<MapPoint>();
            foreach(var value in sequence){
                if(!(value is Dictionary<string,object> p) || !p.ContainsKey("x") || !p.ContainsKey("y"))throw new ArgumentException("Недопустимая точка");
                float x=Convert.ToSingle(p["x"],CultureInfo.InvariantCulture),y=Convert.ToSingle(p["y"],CultureInfo.InvariantCulture);
                if(!float.IsFinite(x) || !float.IsFinite(y) || x<0 || y<0 || x>scene.width || y>scene.height || points.Count>=128)throw new ArgumentException("Недопустимые точки кисти");
                points.Add(new MapPoint{x=x,y=y});
            }
            if(points.Count<1)throw new ArgumentException("Пустой штрих");
            scene.terrain=previous.Concat(new[]{new TerrainStroke{id=Guid.NewGuid().ToString("N"),kind=kind,width=width,points=points.ToArray()}}).ToArray();
        }
    }
}
