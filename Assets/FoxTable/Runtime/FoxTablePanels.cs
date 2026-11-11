using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoxTable
{
    public sealed partial class FoxTableApp
    {
        private int quickAmount=5;
        private string actionTargetId;

        private void Actions(VisualElement content)
        {
            var s=client.State;
            var t=Array.Find(s.Active.tokens,v=>v.id==selectedId);
            if(t==null){Label(content,"Жетон больше не виден");return;}
            Label(content,t.name,"headline-small");
            actionStats=Label(content,t.kind=="object"?"Объект карты":$"{t.hp}/{t.maxHp} HP · временные {t.tempHp} · КД {t.ac}");
            if(t.kind!="object")Label(content,$"Перемещение {t.movedFeet:0.#}/{t.speed} фт · атака {t.attackBonus:+0;-0;0}, {t.damage}","muted");
            bool editable=!client.IsPreview && (s.IsGm || (t.ownerId==s.selfId && t.layer=="tokens"));
            if(editable && t.kind!="object")
            {
                var amount=Number(content,"Урон / лечение",quickAmount);
                amount.RegisterValueChangedCallback(e=>quickAmount=Mathf.Clamp(e.newValue,1,9999));
                var row=Row(content);
                Button(row,"− Урон",()=>client.Act("token.hp","tokenId",t.id,"delta",-quickAmount));
                Button(row,"+ Лечение",()=>client.Act("token.hp","tokenId",t.id,"delta",quickAmount));
                var targets=s.Active.tokens.Where(v=>v.id!=t.id && v.layer=="tokens").ToList();
                if(targets.Count>0 && t.kind!="object")
                {
                    int targetIndex=Mathf.Max(0,targets.FindIndex(v=>v.id==actionTargetId));
                    var target=new PopupField<string>("Цель",targets.Select(v=>v.name+" · "+v.id.Substring(0,4)).ToList(),targetIndex);
                    content.Add(target);
                    actionTargetId=targets[targetIndex].id;
                    target.RegisterValueChangedCallback(e=>actionTargetId=targets[target.index].id);
                    void Attack(string mode) {client.Act("token.attack","tokenId",t.id,"targetId",targets[target.index].id,"mode",mode);ClosePanel();}
                    Button(content,"Атаковать · d20 → КД → урон",()=>Attack("normal"),"primary");
                    row=Row(content);
                    Button(row,"Преимущество",()=>Attack("advantage"));
                    Button(row,"Помеха",()=>Attack("disadvantage"));
                    Label(content,client.IsLocal?"Дальность и преграды проверяет приложение. Крит удваивает кубики урона. Особые правила решает мастер.":"Дальность и преграды проверяет сервер. Крит удваивает кубики урона. Особые правила решает мастер.","muted");
                }
                foreach(var macro in t.macros)
                    Button(content,macro.name+" · "+macro.formula,()=>{client.Act("token.macro","tokenId",t.id,"macroId",macro.id);OpenPanel("chat");});
            }
            Button(content,"Карточка и состояния",()=>OpenPanel("token"));
            if(s.IsGm)
            {
                var quick=Row(content);
                Button(quick,"Повернуть 45°",()=>{var current=Array.Find(client.State.Active.tokens,v=>v.id==t.id);if(current!=null)client.Act("token.update","tokenId",t.id,"rotation",(current.rotation+45)%360);});
                Button(quick,t.locked?"Открепить":"Закрепить",()=>{client.Act("token.update","tokenId",t.id,"locked",!t.locked);ClosePanel();});
                Button(quick,t.hidden?"Показать":"Скрыть",()=>{client.Act("token.update","tokenId",t.id,"hidden",!t.hidden);ClosePanel();});
                Button(content,"Зрение, свет, слои и действия",()=>OpenPanel("profile"));
                Button(content,"Дублировать жетон",()=>{client.Act("token.duplicate","tokenId",t.id);ClosePanel();});
            }
        }

        private void Profile(VisualElement content)
        {
            var s=client.State;if(!s.IsGm)return;
            var t=Array.Find(s.Active.tokens,v=>v.id==selectedId);
            if(t==null)return;
            Label(content,t.name,"headline-small");
            Label(content,"ЗРЕНИЕ И СВЕТ · ФУТЫ","eyebrow");
            var vision=Number(content,"Дальность зрения",t.visionRange);
            var night=Number(content,"Тёмное зрение",t.nightVision);
            var light=Number(content,"Радиус света",t.lightRadius);
            var aura=Number(content,"Радиус ауры",t.auraFeet);
            Label(content,"Факел: свет 20–40 фт. Тёмное зрение действует в пределах общей дальности зрения. Стены перекрывают оба.","muted");
            Label(content,"СЛОИ И ПЕРЕМЕЩЕНИЕ","eyebrow");
            var layers=new[]{"tokens","map","gm"};
            var layer=new PopupField<string>("Слой",new List<string>{"Жетоны","Объекты карты","Только мастер"},Mathf.Max(0,Array.IndexOf(layers,t.layer)));
            content.Add(layer);
            bool locked=t.locked;
            Check(content,"Закрепить на карте",locked,value=>locked=value);
            var speed=Number(content,"Скорость за ход",t.speed);
            var temporary=Number(content,"Временные HP",t.tempHp);
            Label(content,"БЫСТРАЯ АТАКА","eyebrow");
            var attack=Number(content,"Бонус попадания",t.attackBonus);
            var damage=Field(content,"Урон",t.damage);
            var range=Number(content,"Дальность атаки",t.attackRange);
            Label(content,"КНОПКИ ДЕЙСТВИЙ · ДО 12","eyebrow");
            var names=new List<TextField>();var formulas=new List<TextField>();
            for(int i=0;i<Mathf.Max(3,t.macros.Length);i++)
            {
                names.Add(Field(content,"Действие "+(i+1),i<t.macros.Length?t.macros[i].name:""));
                formulas.Add(Field(content,"Формула",i<t.macros.Length?t.macros[i].formula:""));
            }
            Button(content,"Сохранить настройки",()=> {
                var macros=new List<object>();
                for(int i=0;i<names.Count;i++)
                {
                    if(string.IsNullOrWhiteSpace(names[i].value) && string.IsNullOrWhiteSpace(formulas[i].value))continue;
                    macros.Add(Json.Fields("name",names[i].value,"formula",formulas[i].value));
                }
                client.Act("token.update","tokenId",t.id,"visionRange",vision.value,"nightVision",night.value,
                    "lightRadius",light.value,"auraFeet",aura.value,"layer",layers[layer.index],"locked",locked,
                    "speed",speed.value,"tempHp",temporary.value,"attackBonus",attack.value,"damage",damage.value,
                    "attackRange",range.value,"macros",macros);
                ClosePanel();
            },"primary");
        }

        private void Lighting(VisualElement content)
        {
            var s=client.State;if(!s.IsGm)return;
            var scene=s.Active;
            if(client.IsLocal) Label(content,"Локально открыт стол мастера: он видит всю карту. Динамический обзор игроков и ограничения движения используются в онлайн-комнатах.","muted");
            Check(content,"Динамическая видимость",scene.lighting,v=>client.Act("scene.update","lighting",v));
            Check(content,"Дневное освещение",scene.daylight,v=>client.Act("scene.update","daylight",v));
            Check(content,"Память исследованных мест",scene.explorer,v=>client.Act("scene.update","explorer",v));
            Check(content,"Общий обзор группы",scene.sharedVision,v=>client.Act("scene.update","sharedVision",v));
            Check(content,"Стены запрещают проход",scene.restrictMovement,v=>client.Act("scene.update","restrictMovement",v));
            Check(content,"Учитывать скорость и ход в бою",scene.enforceSpeed,v=>client.Act("scene.update","enforceSpeed",v));
            Label(content,"Зрение и свет задаются в настройках жетона. Назначьте герою владельца. Для ночного подземелья выключите дневной свет.","muted");
            Label(content,"НАРИСОВАТЬ ПРЕГРАДУ","eyebrow");
            var row=Row(content);
            void Draw(string tool) {SetTool(tool);ClosePanel();ShowToast("Проведите по карте. Концы привязываются к узлам сетки.");}
            Button(row,"Стена",()=>Draw("wall"));Button(row,"Дверь",()=>Draw("door"));Button(row,"Окно",()=>Draw("window"));
            Label(content,"У двери должен быть разрыв в стене. Сплошная стена под открытой дверью продолжает перекрывать путь.","muted");
            foreach(var wall in scene.barriers)
            {
                var item=new VisualElement();item.AddToClassList("list-item");content.Add(item);
                Label(item,(wall.kind=="wall"?"Стена":wall.kind=="door"?"Дверь":"Окно")+
                    $" · ({wall.x:0.#},{wall.y:0.#}) → ({wall.ex:0.#},{wall.ey:0.#})");
                if(wall.kind!="wall")
                {
                    row=Row(item);
                    Button(row,wall.open?"Закрыть":"Открыть",()=>client.Act("door.toggle","barrierId",wall.id));
                    if(!wall.open)Check(item,"Заперто",wall.locked,v=>client.Act("barrier.update","barrierId",wall.id,"locked",v));
                    else Label(item,"Чтобы запереть, сначала закройте.","muted");
                    Check(item,"Секретная",wall.secret,v=>client.Act("barrier.update","barrierId",wall.id,"secret",v));
                }
                Button(item,"Удалить отрезок",()=>client.Act("barrier.delete","barrierId",wall.id));
            }
            if(!client.IsLocal)Button(content,"Посмотреть глазами игрока",()=>OpenPanel("vision"),"primary");
            Button(content,"Сбросить память исследования",()=>Confirm("Забыть исследованные места для всех игроков?",()=>{client.Act("vision.reset");ClosePanel();}));
            Button(content,"Удалить все преграды",()=>Confirm("Удалить все стены, двери и окна этой сцены?",()=>{client.Act("barrier.clear");ClosePanel();}),"danger");
        }

        private void Vision(VisualElement content)
        {
            if(client.Session.role!="gm")return;
            Label(content,"Предпросмотр использует серверные права и зрение выбранного игрока. Изменять игру в нём нельзя.","muted");
            foreach(var member in client.State.members.Where(m=>m.role=="player"))
                Button(content,"Глазами: "+member.name,()=>{client.Preview(member.id);ClosePanel();});
            if(!client.State.members.Any(m=>m.role=="player"))Label(content,"Сначала пригласите игрока и назначьте ему жетон.");
            if(client.IsPreview)Button(content,"Вернуться мастером",()=>{client.Preview(null);ClosePanel();},"primary");
        }
    }
}
