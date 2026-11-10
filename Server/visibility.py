"""Cell-based visibility and segment barriers, independent of HTTP/Unity."""
import math

EPS = 1e-8


def center(token):
    return token['x'] + token['size']/2, token['y'] + token['size']/2


def intersects(a, b, c, d):
    def cross(p, q, r):
        return (q[0]-p[0])*(r[1]-p[1]) - (q[1]-p[1])*(r[0]-p[0])
    def between(p, q, r):
        return (min(p[0], q[0])-EPS <= r[0] <= max(p[0], q[0])+EPS and
                min(p[1], q[1])-EPS <= r[1] <= max(p[1], q[1])+EPS)
    o1, o2, o3, o4 = cross(a,b,c), cross(a,b,d), cross(c,d,a), cross(c,d,b)
    if ((o1 > EPS and o2 < -EPS) or (o1 < -EPS and o2 > EPS)) and \
       ((o3 > EPS and o4 < -EPS) or (o3 < -EPS and o4 > EPS)):
        return True
    return ((abs(o1) <= EPS and between(a,b,c)) or (abs(o2) <= EPS and between(a,b,d)) or
            (abs(o3) <= EPS and between(c,d,a)) or (abs(o4) <= EPS and between(c,d,b)))


def clear_path(scene, a, b, movement=False):
    if math.dist(a, b) < EPS:
        return True
    for wall in scene.get('barriers', []):
        if wall['kind'] in ('door', 'window') and wall['open']:
            continue
        if wall['kind'] == 'window' and not movement:
            continue
        c, d = (wall['x'], wall['y']), (wall['ex'], wall['ey'])
        if intersects(a, b, c, d):
            return False
    return True


def vision_cells(scene, actor):
    """Visible cell centers. Euclidean vision; union of eligible observer tokens."""
    if not scene.get('lighting', False):
        return None
    observers = [t for t in scene['tokens'] if t['layer'] == 'tokens' and not t['hidden'] and
                 (t['ownerId'] == actor['id'] or
                  (scene['sharedVision'] and t['kind'] == 'hero' and t['ownerId']))]
    lights = [(center(t), t['lightRadius']/scene['cellFeet']) for t in scene['tokens']
              if t['lightRadius'] > 0 and not t['hidden'] and t['layer'] != 'gm']
    result = set()
    for observer in observers:
        origin = center(observer)
        radius = observer['visionRange']/scene['cellFeet']
        night = observer['nightVision']/scene['cellFeet']
        if 'Ослеплён' in observer['conditions']:
            radius = night = 0
        # The observer's own occupied cells remain visible for orientation.
        for yy in range(int(observer['y']), math.ceil(observer['y']+observer['size'])):
            for xx in range(int(observer['x']), math.ceil(observer['x']+observer['size'])):
                result.add((xx, yy))
        for y in range(max(0, math.floor(origin[1]-radius)), min(scene['height'], math.ceil(origin[1]+radius))):
            for x in range(max(0, math.floor(origin[0]-radius)), min(scene['width'], math.ceil(origin[0]+radius))):
                point = x+0.5, y+0.5
                distance = math.dist(origin, point)
                if distance > radius+EPS or not clear_path(scene, origin, point):
                    continue
                lit = scene['daylight'] or (night > 0 and distance <= night+EPS)
                if not lit:
                    lit = any(math.dist(source, point) <= reach+EPS and clear_path(scene, source, point)
                              for source, reach in lights)
                if lit:
                    result.add((x, y))
    if scene['fog']:
        result.intersection_update((p['x'], p['y']) for p in scene['revealed'])
    return result


def token_in_cells(token, cells):
    return any((x, y) in cells for y in range(int(token['y']), math.ceil(token['y']+token['size']))
               for x in range(int(token['x']), math.ceil(token['x']+token['size'])))


def cells_json(cells):
    return [dict(x=x, y=y) for x, y in sorted(cells)]


def upgrade(state):
    """Additive v1 -> v2 migration. Existing IDs and fields are preserved."""
    state['formatVersion'] = 2
    for scene in state['scenes']:
        defaults = dict(lighting=False, daylight=True, explorer=True, sharedVision=False,
                        restrictMovement=True, enforceSpeed=False, barriers=[], exploredBy={})
        for key, value in defaults.items():
            scene.setdefault(key, value)
        for token in scene['tokens']:
            for key, value in dict(layer='tokens', locked=False, visionRange=60, nightVision=0,
                    lightRadius=0, auraFeet=0, speed=30, movedFeet=0, tempHp=0,
                    attackBonus=3, damage='1d6+1', attackRange=5, macros=[]).items():
                token.setdefault(key, value)
    return state
