"""Run with Blender --background --python; render licensed KayKit geometry for the mobile board."""
import bpy, math, json, sys
from pathlib import Path
from mathutils import Vector
root=Path(__file__).resolve().parents[1]
source=root.parent/'vendor-kaykit/addons/kaykit_medieval_hexagon_pack/Assets/obj'
items=[]
buildings=[('home_A','Дом с башенкой'),('home_B','Жилой дом'),('castle','Замок'),('church','Храм'),('tavern','Таверна'),('market','Рынок'),('blacksmith','Кузница'),('barracks','Казармы'),('archeryrange','Стрельбище'),('mine','Шахта'),('well','Колодец'),('windmill','Ветряная мельница'),('watermill','Водяная мельница'),('lumbermill','Лесопилка'),('tower_A','Сторожевая башня'),('tower_B','Дозорная башня')]
for color,russian in [('red','красная'),('blue','синяя'),('green','зелёная')]:
 for name,title in buildings:
  p=source/'buildings'/color/f'building_{name}_{color}.obj'
  if p.exists():items.append(dict(id=f'kk-{name.lower().replace("_","-")}-{color[0]}',name=title+' · '+russian,category='Постройки',path=str(p),size=4 if name=='castle' else 3 if name in ['church','market','barracks'] else 2))
nature={'tree_single_A':'Лиственное дерево','tree_single_B':'Ель','trees_A_small':'Роща','trees_A_medium':'Лес','trees_A_large':'Густой лес','trees_B_small':'Сосновая роща','trees_B_medium':'Хвойный лес','trees_B_large':'Густой хвойный лес','hill_single_A':'Холм','hill_single_B':'Высокий холм','hill_single_C':'Каменистый холм','hills_A':'Холмы','hills_A_trees':'Лесистые холмы','hills_B_trees':'Зелёные холмы','hills_C_trees':'Хвойные холмы','mountain_A':'Горная вершина','mountain_A_grass':'Зелёная гора','mountain_A_grass_trees':'Гора с лесом','mountain_B':'Скальная гора','mountain_B_grass_trees':'Лесное нагорье','mountain_C':'Острая вершина','mountain_C_grass_trees':'Горный хребет','rock_single_A':'Валун','rock_single_B':'Скалы','rock_single_C':'Каменная гряда','waterlily_A':'Кувшинки','waterlily_B':'Лотосы','waterplant_A':'Камыш','waterplant_B':'Речные травы','cloud_big':'Большое облако','cloud_small':'Облако'}
for name,title in nature.items():
 p=source/'decoration/nature'/f'{name}.obj'
 if p.exists():items.append(dict(id='kk-'+name.lower().replace('_','-'),name=title,category='Ландшафт',path=str(p),size=4 if 'mountain' in name else 3 if 'hills' in name or 'large' in name else 2))
for folder,category,prefix in [('rivers','Реки и берега','Река'),('coast','Реки и берега','Побережье'),('roads','Дороги','Дорога')]:
 for p in sorted((source/'tiles'/folder).glob('*.obj')):
  items.append(dict(id='kk-'+p.stem.lower().replace('_','-'),name=prefix+' '+p.stem.split('_')[-1],category=category,path=str(p),size=3))
props={'tent':'Походная палатка','barrel':'Деревянная бочка','wheelbarrow':'Тачка','weaponrack':'Стойка с оружием','resource_stone':'Запасы камня','resource_lumber':'Дрова','ladder':'Деревянная лестница','crate_open':'Открытый ящик','sack':'Мешки','flag_red':'Красный флаг','flag_blue':'Синий флаг','target':'Мишень'}
for name,title in props.items():
 p=source/'decoration/props'/f'{name}.obj'
 if p.exists():items.append(dict(id='kk-'+name.replace('_','-'),name=title,category='Обстановка',path=str(p),size=2))
# Keep a reproducible, inspectable manifest and original geometry/CC0 license with the project.
manifest=root/'Art/KayKit/catalog.json'
manifest.write_text(json.dumps([{k:v for k,v in item.items() if k!='path'} | {'source':str(Path(item['path']).relative_to(source))} for item in items],ensure_ascii=False,indent=2))
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=12;scene.cycles.use_denoising=False
scene.render.resolution_x=scene.render.resolution_y=256;scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG';scene.render.image_settings.color_mode='RGBA';scene.render.film_transparent=True
scene.world.color=(.55,.55,.55);scene.view_settings.view_transform='Standard'
light_data=bpy.data.lights.new('Softbox','AREA');light=bpy.data.objects.new('Softbox',light_data);scene.collection.objects.link(light);light.location=(3,-4,8);light_data.energy=650;light_data.size=5
cam_data=bpy.data.cameras.new('Camera');cam=bpy.data.objects.new('Camera',cam_data);scene.collection.objects.link(cam);scene.camera=cam;cam_data.type='ORTHO'
for index,item in enumerate(items):
 out=root/'Assets/FoxTable/Resources/World'/f'{item["id"]}.png'
 if out.exists():continue
 before=set(bpy.data.objects);bpy.ops.wm.obj_import(filepath=item['path']);objects=[o for o in bpy.data.objects if o not in before]
 meshes=[o for o in objects if o.type=='MESH'];points=[o.matrix_world@Vector(p) for o in meshes for p in o.bound_box]
 lo=Vector([min(p[i] for p in points) for i in range(3)]);hi=Vector([max(p[i] for p in points) for i in range(3)]);center=(lo+hi)*.5
 radius=max((hi-lo).length,.1)
 cam.location=center+Vector((1,-1.4,1.9)).normalized()*radius*3;cam.rotation_euler=(center-cam.location).to_track_quat('-Z','Y').to_euler();cam_data.ortho_scale=radius*1.12
 light.location=center+Vector((3,-4,8));scene.render.filepath=str(out);bpy.ops.render.render(write_still=True)
 for o in objects:bpy.data.objects.remove(o,do_unlink=True)
 for mesh in list(bpy.data.meshes):
  if mesh.users==0:bpy.data.meshes.remove(mesh)
 print(f'WORLD_ASSET {index+1}/{len(items)} {item["id"]}',flush=True)
print('WORLD_ASSETS_DONE',len(items),flush=True)
