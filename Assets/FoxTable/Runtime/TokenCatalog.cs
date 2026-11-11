using System;
using System.Collections.Generic;
using UnityEngine;

namespace FoxTable
{
    public sealed class Miniature
    {
        public string id,name,category,kind,color;
        public int size,hp,ac;
        public Texture2D Texture=>TokenCatalog.Texture(id);
        public Miniature(string id,string name,string category,string kind,int size=1,int hp=12,int ac=12,string color="#b9605c")
        {this.id=id;this.name=name;this.category=category;this.kind=kind;this.size=size;this.hp=hp;this.ac=ac;this.color=color;}
    }
    public static class TokenCatalog
    {
        private static readonly Dictionary<string,Texture2D> textures=new Dictionary<string,Texture2D>();
        public static readonly Miniature[] All={
            new Miniature("knight","Рыцарь","Герои","hero",hp:38,ac:18,color:"#79c8ba"),
            new Miniature("mage","Маг","Герои","hero",hp:24,ac:12,color:"#b39cdd"),
            new Miniature("ranger","Следопыт","Герои","hero",hp:30,ac:15,color:"#99bd75"),
            new Miniature("rogue","Плут","Герои","hero",hp:28,ac:14,color:"#8babbc"),
            new Miniature("cleric","Жрец","Герои","hero",hp:32,ac:16,color:"#e1c983"),
            new Miniature("barbarian","Варвар","Герои","hero",hp:45,ac:14,color:"#c69174"),
            new Miniature("goblin","Гоблин","Монстры","enemy",hp:7,ac:15),
            new Miniature("orc","Орк","Монстры","enemy",hp:15,ac:13),
            new Miniature("skeleton","Скелет","Монстры","enemy",hp:13,ac:13),
            new Miniature("zombie","Зомби","Монстры","enemy",hp:22,ac:8),
            new Miniature("wolf","Волк","Монстры","enemy",hp:11,ac:13),
            new Miniature("spider","Паук","Монстры","enemy",2,26,14),
            new Miniature("ogre","Огр","Монстры","enemy",2,59,11),
            new Miniature("dragon","Дракон","Монстры","enemy",3,150,18),
            new Miniature("tree","Дерево","Природа","object",2),
            new Miniature("pine","Ель","Природа","object",2),
            new Miniature("bush","Куст","Природа","object"),
            new Miniature("rock","Камни","Природа","object",2),
            new Miniature("campfire","Костёр","Природа","object"),
            new Miniature("tent","Палатка","Природа","object",2),
            new Miniature("chest","Сундук","Предметы","object"),
            new Miniature("barrel","Бочка","Предметы","object"),
            new Miniature("crate","Ящик","Предметы","object"),
            new Miniature("table","Стол","Предметы","object",2),
            new Miniature("chair","Стул","Предметы","object"),
            new Miniature("bed","Кровать","Предметы","object",2),
            new Miniature("altar","Алтарь","Подземелье","object",2),
            new Miniature("pillar","Колонна","Подземелье","object"),
            new Miniature("stairs","Лестница","Подземелье","object",2),
            new Miniature("door","Дверь","Подземелье","object"),
            new Miniature("wall","Каменная стена","Подземелье","object",2),
            new Miniature("bones","Кости","Подземелье","object")
        };
        public static Miniature Find(string id)=>Array.Find(All,v=>v.id==id);
        public static Texture2D Texture(string id)
        {
            if(string.IsNullOrEmpty(id) || Find(id)==null)return null;
            if(!textures.TryGetValue(id,out var texture))textures[id]=texture=Resources.Load<Texture2D>("Miniatures/"+id);
            return texture;
        }
        public static string Art(TokenState token)
        {
            if(!string.IsNullOrEmpty(token.artId))return token.artId;
            if(token.name.IndexOf("Маг",StringComparison.OrdinalIgnoreCase)>=0)return "mage";
            if(token.name.IndexOf("Гоблин",StringComparison.OrdinalIgnoreCase)>=0)return "goblin";
            return token.kind=="hero"?"knight":token.kind=="enemy"?"orc":null;
        }
        public static object[] Placement(Miniature item,SceneState scene,Vector2 point)
        {
            var position=MapLayout.Place(scene,point,item.size);
            return new object[]{"name",item.name,"kind",item.kind,"artId",item.id,"size",item.size,"x",position.x,"y",position.y,
                "maxHp",item.hp,"hp",item.hp,"ac",item.ac,"color",item.color,"layer",item.kind=="object"?"map":"tokens"};
        }
    }
}
