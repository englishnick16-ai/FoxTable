import math
import unittest
import test_server as base


class WorkspaceTests(unittest.TestCase):
    setUp = base.RoomTests.setUp
    tearDown = base.RoomTests.tearDown
    act = base.RoomTests.act
    view = base.RoomTests.view
    scene = base.RoomTests.scene
    assertDenied = base.RoomTests.assertDenied

    def test_catalog_placement_carries_art_position_and_stats(self):
        self.act(self.host,'token.add',name='Рыцарь',kind='hero',artId='knight',x=4,y=5,size=2,maxHp=38,hp=38,ac=18,layer='tokens')
        t=self.scene()['tokens'][-1]
        self.assertEqual((t['artId'],t['x'],t['y'],t['size'],t['hp'],t['ac']),('knight',4,5,2,38,18))

    def test_scenery_stays_out_of_initiative(self):
        self.act(self.host,'token.add',name='Стол',kind='object',artId='table',layer='map',size=2,x=5,y=5)
        prop=self.scene()['tokens'][-1]['id']
        self.act(self.host,'combat.start')
        for _ in range(10):
            self.assertNotEqual(self.scene()['turnId'],prop)
            self.act(self.host,'combat.next')

    def test_grid_and_model_survive_store_reload(self):
        self.act(self.host,'scene.update',mapPixelsPerCell=70.5,mapOffsetX=-.25,mapOffsetY=.75,gridOpacity=.7,gridColor='#ffffff')
        self.act(self.host,'token.update',tokenId=self.scene()['tokens'][0]['id'],artId='mage',rotation=135)
        self.store.db.commit()
        state=self.store.load(self.host['code']);s=state['scenes'][0]
        self.assertEqual((s['mapPixelsPerCell'],s['mapOffsetX'],s['mapOffsetY']),(70.5,-.25,.75))
        self.assertEqual((s['tokens'][0]['artId'],s['tokens'][0]['rotation']),('mage',135))

    def test_invalid_grid_is_transactional(self):
        before=self.scene()['name']
        self.assertDenied(lambda:self.act(self.host,'scene.update',name='Changed',mapPixelsPerCell=float('nan')),400)
        self.assertEqual(self.scene()['name'],before)

    def test_player_cannot_change_grid_or_art(self):
        hero=self.scene()['tokens'][0]['id']
        self.act(self.host,'token.update',tokenId=hero,ownerId=self.player['memberId'])
        self.assertDenied(lambda:self.act(self.player,'scene.update',mapPixelsPerCell=50),403)
        self.assertDenied(lambda:self.act(self.player,'token.update',tokenId=hero,artId='dragon'),403)

    def test_bad_art_and_out_of_bounds_placement_do_not_spawn(self):
        count=len(self.scene()['tokens'])
        self.assertDenied(lambda:self.act(self.host,'token.add',name='x',kind='enemy',artId='../bad'),400)
        self.assertDenied(lambda:self.act(self.host,'token.add',name='x',kind='enemy',size=3,x=29,y=20),400)
        self.assertEqual(len(self.scene()['tokens']),count)

    def test_rotation_limits_and_independent_duplicate(self):
        hero=self.scene()['tokens'][0]['id']
        self.act(self.host,'token.update',tokenId=hero,artId='ranger',rotation=359)
        self.act(self.host,'token.duplicate',tokenId=hero)
        copy=self.scene()['tokens'][-1]
        self.assertEqual((copy['artId'],copy['rotation']),('ranger',359))
        self.assertDenied(lambda:self.act(self.host,'token.update',tokenId=copy['id'],rotation=360),400)

    def test_new_blank_scene_is_explicit(self):
        self.act(self.host,'scene.add',name='Поле',background='blank')
        self.assertEqual(self.scene()['background'],'blank')

    def test_hidden_scenery_does_not_leak_to_player(self):
        self.act(self.host,'token.add',name='Секрет',kind='object',artId='chest',layer='gm',x=3,y=3)
        self.assertFalse(any(t['name']=='Секрет' for t in self.scene(self.player)['tokens']))
