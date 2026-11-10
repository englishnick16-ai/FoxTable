import json
import math
import unittest
from unittest.mock import patch

from server import ApiError, Store
from visibility import intersects, clear_path
import test_server as base_tests


class AdvancedTests(unittest.TestCase):
    setUp = base_tests.RoomTests.setUp
    tearDown = base_tests.RoomTests.tearDown
    act = base_tests.RoomTests.act
    view = base_tests.RoomTests.view
    scene = base_tests.RoomTests.scene
    assertDenied = base_tests.RoomTests.assertDenied

    def prepare(self):
        hero, enemy = self.scene()['tokens'][0], self.scene()['tokens'][2]
        self.act(self.host, 'token.update', tokenId=hero['id'], ownerId=self.player['memberId'])
        self.act(self.host, 'token.move', tokenId=hero['id'], x=2, y=2)
        self.act(self.host, 'token.move', tokenId=enemy['id'], x=8, y=2)
        self.act(self.host, 'scene.update', lighting=True, daylight=True)
        return hero, enemy

    def add_wall(self, kind='wall', **fields):
        self.act(self.host, 'barrier.add', kind=kind, x=5, y=0, ex=5, ey=22, **fields)
        return self.scene()['barriers'][-1]

    def seen(self, token):
        return any(t['id'] == token['id'] for t in self.scene(self.player)['tokens'])

    def test_wall_occludes_token_and_deleting_restores_view(self):
        hero, enemy = self.prepare()
        self.assertTrue(self.seen(enemy))
        wall = self.add_wall()
        self.assertFalse(self.seen(enemy))
        self.act(self.host, 'barrier.delete', barrierId=wall['id'])
        self.assertTrue(self.seen(enemy))

    def test_closed_door_sight_and_movement_then_open(self):
        hero, enemy = self.prepare()
        door = self.add_wall('door')
        self.assertFalse(self.seen(enemy))
        self.assertDenied(lambda: self.act(self.player,'token.move',tokenId=hero['id'],x=8,y=2),400)
        self.act(self.host,'door.toggle',barrierId=door['id'])
        self.assertTrue(self.seen(enemy))
        self.act(self.player,'token.move',tokenId=hero['id'],x=8,y=2)

    def test_window_transmits_sight_but_blocks_movement(self):
        hero, enemy = self.prepare()
        window = self.add_wall('window')
        self.assertTrue(self.seen(enemy))
        self.assertDenied(lambda: self.act(self.player,'token.move',tokenId=hero['id'],x=8,y=2),400)
        self.act(self.host,'door.toggle',barrierId=window['id'])
        self.act(self.player,'token.move',tokenId=hero['id'],x=8,y=2)

    def test_darkness_torch_and_night_vision(self):
        hero, enemy = self.prepare()
        self.act(self.host,'scene.update',daylight=False)
        self.assertFalse(self.seen(enemy))
        self.act(self.host,'token.update',tokenId=hero['id'],lightRadius=40)
        self.assertTrue(self.seen(enemy))
        self.act(self.host,'token.update',tokenId=hero['id'],lightRadius=0,nightVision=40)
        self.assertTrue(self.seen(enemy))
        self.add_wall()
        self.assertFalse(self.seen(enemy))

    def test_light_cannot_pass_through_wall(self):
        hero, enemy = self.prepare()
        self.act(self.host,'scene.update',daylight=False)
        # A lamp on the far side cannot illuminate the observer's side through a wall.
        self.act(self.host,'token.update',tokenId=enemy['id'],lightRadius=60)
        self.add_wall()
        current = {(p['x'],p['y']) for p in self.scene(self.player)['currentCells']}
        self.assertNotIn((3,2),current)

    def test_blinded_observer_only_sees_own_cells(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],conditions=['Ослеплён'])
        self.assertFalse(self.seen(enemy))
        self.assertTrue(self.seen(hero))
        self.assertEqual(len(self.scene(self.player)['currentCells']),1)

    def test_vision_is_individual_until_shared(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],visionRange=5)
        other = self.scene()['tokens'][1]
        second = self.store.join(self.code,{'name':'Второй','invite':self.state['invite']})
        self.act(self.host,'token.update',tokenId=other['id'],ownerId=second['memberId'])
        self.act(self.host,'token.move',tokenId=other['id'],x=7,y=2)
        self.assertFalse(self.seen(enemy))
        self.act(self.host,'scene.update',sharedVision=True)
        self.assertTrue(self.seen(enemy))

    def test_explorer_remembers_map_not_monsters(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],visionRange=10)
        self.act(self.player,'token.move',tokenId=hero['id'],x=7,y=2)
        self.assertTrue(self.seen(enemy))
        self.act(self.player,'token.move',tokenId=hero['id'],x=2,y=2)
        scene = self.scene(self.player)
        self.assertFalse(self.seen(enemy))
        self.assertIn({'x':8,'y':2},scene['exploredCells'])
        self.assertNotIn({'x':8,'y':2},scene['currentCells'])
        self.assertNotIn('exploredBy',scene)

    def test_explorer_memory_persists_after_database_reopen(self):
        hero, enemy = self.prepare()
        first = self.scene(self.player)['exploredCells']
        self.store.db.close()
        self.store=Store(self.path)
        self.assertEqual(self.scene(self.player)['exploredCells'],first)

    def test_manual_fog_limits_dynamic_visibility(self):
        hero, enemy = self.prepare()
        self.act(self.host,'scene.update',fog=True)
        self.assertFalse(self.seen(enemy))
        self.act(self.host,'fog.paint',x=8,y=2,radius=0,reveal=True)
        self.assertTrue(self.seen(enemy))

    def test_preview_matches_player_and_does_not_change_history(self):
        self.prepare()
        raw_before=self.store.load(self.code)
        preview=self.store.preview(self.code,self.host['secret'],{'memberId':self.player['memberId']})
        self.assertEqual(preview['role'],'player')
        self.assertNotIn('gmNotes',preview)
        self.assertNotIn('invite',preview)
        self.assertEqual(self.store.load(self.code),raw_before)
        player=self.view(self.player)
        self.assertEqual(preview,player)
        self.assertDenied(lambda: self.store.preview(self.code,self.player['secret'],{'memberId':self.host['memberId']}))

    def test_gm_layer_is_not_sent_even_to_owner(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=enemy['id'],layer='gm',ownerId=self.player['memberId'])
        self.assertFalse(self.seen(enemy))
        self.assertDenied(lambda:self.act(self.player,'token.move',tokenId=enemy['id'],x=3,y=2))

    def test_locked_and_map_layer_cannot_be_moved_by_player(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],locked=True)
        self.assertDenied(lambda:self.act(self.player,'token.move',tokenId=hero['id'],x=3,y=2))
        self.act(self.host,'token.update',tokenId=hero['id'],locked=False,layer='map')
        self.assertDenied(lambda:self.act(self.player,'token.move',tokenId=hero['id'],x=3,y=2))

    def test_nearby_player_can_open_unlocked_door(self):
        hero, enemy = self.prepare()
        door=self.add_wall('door')
        self.assertDenied(lambda:self.act(self.player,'door.toggle',barrierId=door['id']))
        self.act(self.host,'token.move',tokenId=hero['id'],x=4,y=10)
        self.act(self.player,'door.toggle',barrierId=door['id'])
        self.assertTrue(self.scene()['barriers'][0]['open'])

    def test_secret_and_locked_doors_reject_player(self):
        hero, enemy = self.prepare()
        door=self.add_wall('door')
        self.act(self.host,'token.move',tokenId=hero['id'],x=4,y=10)
        self.act(self.host,'barrier.update',barrierId=door['id'],secret=True)
        self.assertEqual(self.scene(self.player)['barriers'],[])
        self.assertDenied(lambda:self.act(self.player,'door.toggle',barrierId=door['id']))
        self.act(self.host,'barrier.update',barrierId=door['id'],secret=False,locked=True)
        self.assertDenied(lambda:self.act(self.player,'door.toggle',barrierId=door['id']))

    def test_speed_budget_turn_and_reset(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],speed=10,initiative=50)
        self.act(self.host,'scene.update',enforceSpeed=True)
        self.act(self.host,'combat.start')
        self.act(self.player,'token.move',tokenId=hero['id'],x=4,y=2)
        self.assertDenied(lambda:self.act(self.player,'token.move',tokenId=hero['id'],x=5,y=2),400)
        self.act(self.host,'combat.next')
        self.assertDenied(lambda:self.act(self.player,'token.move',tokenId=hero['id'],x=3,y=2))
        for _ in range(3):self.act(self.host,'combat.next')
        self.act(self.player,'token.move',tokenId=hero['id'],x=5,y=2)

    def test_temp_hp_absorbs_damage_then_healing_caps(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],tempHp=7)
        self.act(self.player,'token.hp',tokenId=hero['id'],delta=-10)
        updated=self.scene()['tokens'][0]
        self.assertEqual(updated['tempHp'],0)
        self.assertEqual(updated['hp'],hero['hp']-3)
        self.act(self.player,'token.hp',tokenId=hero['id'],delta=999)
        self.assertEqual(self.scene()['tokens'][0]['hp'],hero['maxHp'])

    def test_quick_attack_critical_and_concentration(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.move',tokenId=enemy['id'],x=3,y=2)
        self.act(self.host,'token.update',tokenId=enemy['id'],conditions=['Концентрация'],maxHp=100,hp=100)
        self.act(self.host,'token.update',tokenId=hero['id'],damage='1d6+3')
        with patch('server.secrets.randbelow',side_effect=[19,5,5]):
            state=self.act(self.player,'token.attack',tokenId=hero['id'],targetId=enemy['id'])
        self.assertEqual(next(t for t in self.scene()['tokens'] if t['id']==enemy['id'])['hp'],85)
        self.assertIn('КРИТ',state['messages'][-1]['text'])
        self.assertIn('концентрации',state['messages'][-1]['text'])
        self.assertEqual(state['effect']['kind'],'critical')

    def test_natural_one_misses_even_with_huge_attack_bonus(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.move',tokenId=enemy['id'],x=3,y=2)
        self.act(self.host,'token.update',tokenId=hero['id'],attackBonus=50)
        with patch('server.secrets.randbelow',return_value=0):
            self.act(self.player,'token.attack',tokenId=hero['id'],targetId=enemy['id'])
        self.assertEqual(next(t for t in self.scene()['tokens'] if t['id']==enemy['id'])['hp'],enemy['hp'])

    def test_attack_validates_range_visibility_and_barriers(self):
        hero, enemy = self.prepare()
        self.assertDenied(lambda:self.act(self.player,'token.attack',tokenId=hero['id'],targetId=enemy['id']),400)
        self.act(self.host,'token.update',tokenId=hero['id'],attackRange=60)
        self.add_wall('window')
        self.assertDenied(lambda:self.act(self.player,'token.attack',tokenId=hero['id'],targetId=enemy['id']),400)
        self.act(self.host,'token.update',tokenId=enemy['id'],hidden=True)
        self.assertDenied(lambda:self.act(self.player,'token.attack',tokenId=hero['id'],targetId=enemy['id']))

    def test_token_actions_use_saved_formula(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],macros=[dict(name='Мудрость',formula='1d20+4')])
        macro=self.scene()['tokens'][0]['macros'][0]
        with patch('server.secrets.randbelow',return_value=9):
            state=self.act(self.player,'token.macro',tokenId=hero['id'],macroId=macro['id'])
        self.assertIn('= 14',state['messages'][-1]['text'])
        self.assertDenied(lambda:self.act(self.player,'token.update',tokenId=hero['id'],macros=[]))

    def test_hidden_combat_log_does_not_leak_enemy_name(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=enemy['id'],hidden=True)
        self.act(self.host,'token.hp',tokenId=enemy['id'],delta=-1)
        self.assertEqual(self.view(self.player)['messages'],[])

    def test_duplicate_preserves_profile_with_new_id(self):
        hero, enemy = self.prepare()
        self.act(self.host,'token.update',tokenId=hero['id'],lightRadius=20,auraFeet=10)
        self.act(self.host,'token.duplicate',tokenId=hero['id'])
        duplicate=self.scene()['tokens'][-1]
        self.assertNotEqual(duplicate['id'],hero['id'])
        self.assertEqual((duplicate['lightRadius'],duplicate['auraFeet']),(20,10))
        self.assertDenied(lambda:self.act(self.player,'token.duplicate',tokenId=hero['id']))

    def test_legacy_v1_migration_preserves_original_fields(self):
        old=self.store.load(self.code)
        old['formatVersion']=1
        for s in old['scenes']:
            for key in ('lighting','daylight','barriers','explorer','sharedVision','restrictMovement','enforceSpeed','exploredBy'):
                s.pop(key,None)
            for t in s['tokens']:
                for key in ('layer','locked','visionRange','nightVision','lightRadius','auraFeet','speed','movedFeet','tempHp','attackBonus','damage','attackRange','macros'):
                    t.pop(key,None)
        self.store.db.execute('UPDATE rooms SET state=? WHERE code=?',(json.dumps(old),self.code));self.store.db.commit()
        new=self.view()
        self.assertEqual(new['formatVersion'],2)
        self.assertEqual(new['scenes'][0]['tokens'][0]['id'],old['scenes'][0]['tokens'][0]['id'])
        self.assertFalse(new['scenes'][0]['lighting'])
        self.assertEqual(new['scenes'][0]['tokens'][0]['speed'],30)

    def test_segment_intersections_include_corners_and_collinear_walls(self):
        self.assertTrue(intersects((0,0),(2,2),(2,2),(3,2)))
        self.assertTrue(intersects((0,0),(3,0),(1,0),(2,0)))
        self.assertFalse(intersects((0,0),(1,0),(2,0),(3,0)))
        self.assertTrue(intersects((0,0),(2,2),(0,2),(2,0)))


if __name__ == '__main__':
    unittest.main()
