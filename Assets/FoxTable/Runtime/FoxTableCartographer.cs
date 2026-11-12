using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
namespace FoxTable
{
    public sealed partial class FoxTableApp
    {
        private string blueprint="city";
        private void CreateMapPanel(VisualElement content)
        {
            Label(content,"СОБЕРИ СВОЙ МИР","eyebrow");Label(content,"С чего начнём?","headline-small");
            Label(content,"Новая карта станет отдельной сценой. Текущая останется в кампании.","muted");
            var cards=new VisualElement();cards.AddToClassList("blueprint-grid");content.Add(cards);
            var ids=new[]{"empty","city","forest","region"};var names=new[]{"Чистый холст","Город у реки","Лесная застава","Большой регион"};var arts=new[]{"kk-hills-a-trees","kk-castle-r","kk-trees-a-large","kk-mountain-a-grass-trees"};
            var buttons=new List<Button>();
            for(int i=0;i<ids.Length;i++){
                int index=i;var button=new Button(()=>{blueprint=ids[index];foreach(var b in buttons)b.EnableInClassList("chosen",(string)b.userData==blueprint);});
                button.userData=ids[i];button.AddToClassList("blueprint-card");button.EnableInClassList("chosen",ids[i]==blueprint);
                var art=new Image{image=TokenCatalog.Texture(arts[i]),scaleMode=ScaleMode.ScaleToFit,pickingMode=PickingMode.Ignore};art.style.height=90;button.Add(art);Label(button,names[i]);cards.Add(button);buttons.Add(button);
            }
            var name=Field(content,"Название","Новая карта");
            var size=new PopupField<string>("Размер",new List<string>{"64 × 64 · локация","128 × 128 · область","256 × 256 · регион"},0);content.Add(size);
            Label(content,"Потом можно менять фон, рисовать реки и дороги, ставить дома, горы и лес. Для больших сцен персональное освещение отключено.","muted");
            Button(content,"Создать карту",()=>{client.Act("scene.add","name",name.value,"preset",blueprint,"cells",new[]{64,128,256}[size.index]);ClosePanel();SetTool("pan");},"primary");
        }
        private void TerrainPanel(VisualElement content)
        {
            if(!client.State.IsGm)return;
            Label(content,"МЕСТНОСТЬ","eyebrow");Label(content,"Нарисуй ландшафт","headline-small");
            Label(content,"Выбери кисть и проведи пальцем по карте. Два пальца двигают и приближают полотно.","muted");
            var width=new Slider("Ширина кисти · клетки",.5f,24){value=board.BrushWidth,showInputField=true};content.Add(width);
            width.RegisterValueChangedCallback(e=>board.BrushWidth=e.newValue);
            var grid=new VisualElement();grid.AddToClassList("brush-grid");content.Add(grid);
            var names=new[]{"Река","Дорога","Озеро / море","Трава","Песок","Снег","Лес"};
            for(int i=0;i<MapBuilder.Brushes.Length;i++){
                string tool="paint:"+MapBuilder.Brushes[i];var b=Button(grid,names[i],()=>{ClosePanel();SetTool(tool);});b.AddToClassList("brush-card");
            }
            Label(content,"ФОН ПОЛОТНА","eyebrow");var row=Row(content);
            foreach(var item in new[]{new[]{"grass","Трава"},new[]{"sand","Песок"},new[]{"snow","Снег"},new[]{"paper","Пергамент"}})
                Button(row,item[1],()=>{client.Act("scene.update","background",item[0]);ClosePanel();});
            Label(content,"Фон виден под загруженной картинкой. Фигуры и другие штрихи сохраняются.","muted");
            Button(content,"Убрать последний штрих",()=>client.Act("terrain.undo"));
            Button(content,"Очистить местность",()=>Confirm("Убрать все штрихи местности? Фигуры и картинка останутся.",()=>{client.Act("terrain.clear");ClosePanel();}),"danger");
        }
        private void GamePanel(VisualElement content)
        {
            Label(content,"ИГРА ЗА СТОЛОМ","eyebrow");
            Button(content,"Бой · инициатива и раунды",()=>OpenPanel("combat"),"primary");
            Button(content,"Кубики и формулы",()=>OpenPanel("dice"));
            Button(content,"Журнал и сообщения",()=>OpenPanel("chat"));
            Button(content,"Инструменты и туман",()=>OpenPanel("tools"));
        }
    }
}
