import copy
import json
import unittest
from pathlib import Path
import test_server as base

class MapBuilderTests(unittest.TestCase):
    setUp=base.RoomTests.setUp
    tearDown=base.RoomTests.tearDown
    act=base.RoomTests.act
    view=base.RoomTests.view
    scene=base.RoomTests.scene
    assertDenied=base.RoomTests.assertDenied

    def test_blueprint_adds_separate_large_scene(self):
        old=self.scene()['id']
        self.act(self.host,'scene.add',name='Большой город',preset='city',cells=256)
        s=self.scene()
        self.assertNotEqual(old,s['id'])
        self.assertEqual((s['width'],s['height'],s['background']),(256,256,'grass'))
        self.assertGreater(len(s['tokens']),25)
        self.assertTrue(any(t['artId']=='kk-castle-r' for t in s['tokens']))
        self.assertTrue(all(t['x']>=0 and t['y']>=0 and t['x']+t['size']<=256 and t['y']+t['size']<=256 for t in s['tokens']))
        self.assertTrue(any(t['kind']=='river' for t in s['terrain']))

    def test_all_blueprints_and_empty_canvas(self):
        for preset in ['city','forest','region','empty']:
            self.act(self.host,'scene.add',name=preset,preset=preset,cells=64)
            self.assertEqual(self.scene()['width'],64)
        self.assertFalse(self.scene()['tokens'])
        self.assertFalse(self.scene()['terrain'])

    def test_paint_persist_undo_and_clear(self):
        self.act(self.host,'scene.add',name='Ландшафт',preset='empty',cells=256)
        self.act(self.host,'terrain.add',kind='river',width=4,points=[dict(x=0,y=0),dict(x=130,y=250)])
        self.act(self.host,'terrain.add',kind='road',width=1,points=[dict(x=250,y=0)])
        self.assertEqual(len(self.scene()['terrain']),2)
        self.store.db.commit()
        saved=self.store.load(self.host['code'])['scenes'][-1]
        self.assertEqual(saved['terrain'][-1]['kind'],'road')
        self.act(self.host,'terrain.undo');self.assertEqual(len(self.scene()['terrain']),1)
        self.act(self.host,'terrain.clear');self.assertEqual(self.scene()['terrain'],[])

    def test_invalid_strokes_are_transactional(self):
        for fields in [dict(kind='bad',width=2,points=[dict(x=1,y=1)]),dict(kind='river',width=0,points=[]),dict(kind='river',width=2,points=[dict(x=float('nan'),y=1)]),dict(kind='river',width=2,points=[dict(x=999,y=1)]),dict(kind='river',width=2,points=[dict(x=1,y=1)]*129)]:
            self.assertDenied(lambda:self.act(self.host,'terrain.add',**fields),400)
            self.assertFalse(self.scene()['terrain'])

    def test_players_cannot_edit_landscape(self):
        self.assertDenied(lambda:self.act(self.player,'terrain.add',kind='road',width=1,points=[dict(x=1,y=1)]),403)
        self.assertDenied(lambda:self.act(self.player,'terrain.clear'),403)
        self.assertDenied(lambda:self.act(self.player,'scene.add',name='x',preset='city',cells=64),403)

    def test_large_scene_rejects_expensive_personal_lighting(self):
        self.act(self.host,'scene.add',name='x',preset='empty',cells=256)
        self.assertDenied(lambda:self.act(self.host,'scene.update',lighting=True),400)
        self.assertFalse(self.scene()['lighting'])

    def test_large_scenery_resize_and_small_canvas_bounds(self):
        self.act(self.host,'token.add',name='Замок',kind='object',size=12,artId='kk-castle-r',x=2,y=2,layer='map')
        self.assertEqual(self.scene()['tokens'][-1]['size'],12)
        self.act(self.host,'scene.add',name='tiny',preset='empty',cells=5)
        self.assertDenied(lambda:self.act(self.host,'token.add',name='big',kind='object',size=16),400)
        self.assertEqual(self.scene()['tokens'],[])

    def test_presets_match_unity_data(self):
        here=Path(__file__).resolve().parents[2]
        self.assertEqual(json.loads((here/'Server/world_presets.json').read_text()),json.loads((here/'Assets/FoxTable/Resources/WorldPresets.json').read_text()))
