"""Helpers for dungeon scenarios: waiting for floors, walking through doors, clearing rooms."""
import time

FORCED = []   # doors the bot had to force: reported by the scenarios
ARRIVAL_FORCED = []   # same, but in the room a floor starts in (shut in the plain game too when the bot arrives)
ARRIVAL = {}  # copy name -> arrival room of its current floor
FORCED_KILLS = []   # enemies the damage function would not hurt; the bot lowered their health directly
from collections import deque


def in_dungeon(s, level=None):
    return s["zone"] == "dungeon" and not s["loading"] and s["hero"] is not None and s["dungeon"] is not None \
        and s["dungeon"]["room"] is not None and (level is None or s["dungeon"]["level"] == level)


def wait_floor(g, level, timeout=120):
    s = g.wait(f"dungeon floor {level}", lambda s: in_dungeon(s, level) and s["hero"]["canMove"], timeout=timeout)
    ARRIVAL[g.name] = g.call("rooms")["current"]
    return s


def path(rooms, start, goal, only_open=True):
    """Shortest route of room keys from start to goal over doors."""
    graph = {r["key"]: [d["to"] for d in r["doors"] if d["to"] and (d["open"] or not only_open)] for r in rooms["rooms"]}
    prev = {start: None}
    q = deque([start])
    while q:
        k = q.popleft()
        if k == goal:
            break
        for n in graph.get(k, []):
            if n not in prev:
                prev[n] = k
                q.append(n)
    if goal not in prev:
        return None
    out = []
    k = goal
    while k is not None:
        out.append(k)
        k = prev[k]
    return list(reversed(out))


def step(g, to, timeout=20):
    """Walk through the door into an adjacent room and wait until we stand in it."""
    g.call("door", to=to)
    g.wait(f"room {to}", lambda s: g.call("rooms")["current"] == to and s["hero"]["canMove"] and not s["loading"], timeout=timeout)


def clear_room(g, damage=25, timeout=120, every=0.2, quiet=3.5):
    """Hit every enemy in the current room until none is left (owner or not: hits go where they must).
    Some rooms send a second wave after the first dies, so the room must stay empty for `quiet` seconds."""
    end = time.time() + timeout
    empty_since = None
    last_hp, stuck_since = None, time.time()
    ever_dropped = False
    hp_mark = None   # once an enemy takes damage it is awake: never jump next to it (a jump can land on a door)
    detail_done = False
    t_start = time.time()
    while time.time() < end:
        if not detail_done and time.time() - t_start > 10:
            detail_done = True
            try:
                print(f"  .. {g.name} hit detail: {g.call('hitdetail', damage=damage)}", flush=True)
                time.sleep(0.5)
                print(f"  .. {g.name} 0.5 s later: {[(e['id'], e['hp']) for e in g.call('enemies')['here']]}", flush=True)
            except Exception as ex:  # noqa
                print("  .. detail failed " + repr(ex)[:200], flush=True)
        n = g.call("hitall", damage=damage)
        if n:
            here = g.call("enemies")["here"]
            hp = sum(e["hp"] for e in here)
            if last_hp is not None and hp < last_hp - 0.01:
                ever_dropped = True
            # Not going down over 10 s (a mini-boss that regenerates faster than the bot hits): hit harder.
            if hp_mark is None or time.time() - hp_mark[0] > 10:
                if hp_mark is not None and hp >= hp_mark[1] - 1 and damage < 20000:
                    damage = min(damage * 3, 20000)
                    print(f"  .. {g.name}: health not going down, bot damage now {damage}", flush=True)
                hp_mark = (time.time(), hp)
            if last_hp is None or hp < last_hp - 0.01:
                last_hp, stuck_since = hp, time.time()
            elif time.time() - stuck_since > 4 and here and not ever_dropped:
                # Some enemies only wake up when Will comes close: walk up to one. Some have armour that a
                # flat bot hit does not get through: hit harder (the hit still goes through the game's own
                # damage function and the network).
                e = here[0]
                me = g.call("state")["hero"]
                # Two thirds of the way from where Will stands to the enemy: close, but away from the walls.
                g.call("teleport", x=me["x"] + (e["x"] - me["x"]) * 0.66, y=me["y"] + (e["y"] - me["y"]) * 0.66)
                damage = min(damage * 3, 20000)
                stuck_since = time.time()
                if damage >= 20000:
                    for x in here:
                        try:
                            print(f"  .. probe {x['id']} {x['name']}: {g.call('probehit', enemy=x['id'])}", flush=True)
                            print(f"  .. enemies view: {[(e['id'], e['hp'], e.get('amOwnerOf'), e.get('ai'), e.get('state')) for e in g.call('enemies')['here']]}", flush=True)
                            print(f"  .. session: {g.call('session')['players']}, owners: {g.call('enemies')['owners']}", flush=True)
                        except Exception as ex:  # noqa
                            print("  .. probe failed: " + repr(ex)[:300], flush=True)
                        g.call("forcehit", enemy=x["id"], damage=x["hp"] + 1)
                        FORCED_KILLS.append(f"{g.name}: {x['name']} ({x['id']}) hp {x['hp']}")
        if n == 0:
            empty_since = empty_since or time.time()
            if time.time() - empty_since >= quiet:
                return True
        else:
            empty_since = None
        time.sleep(every)
    try:
        rs = g.call("roomstats")
        print(f"  .. room enters {rs['enters']}; enemy resets (top) {sorted(rs['resets'].items(), key=lambda kv: -kv[1])[:5]}", flush=True)
    except Exception:  # noqa
        pass
    left = g.call("enemies")["here"]
    print(f"  .. {g.name}: room not cleared in {timeout}s; left: {[(e['id'], e['name'], round(e['hp'], 1), 'mirrored' if e['mirrored'] else 'own', 'ai on' if e.get('ai') else 'ai off', e.get('state'), 'invincible' if e.get('invincible') else '', 'room', e.get('room'), 'owner of it' if e.get('amOwnerOf') else 'NOT owner', 'def', e.get('defense')) for e in left]}", flush=True)
    return False


def neighbours(rooms, key):
    r = next(x for x in rooms["rooms"] if x["key"] == key)
    return [d["to"] for d in r["doors"] if d["to"] and d["open"]]


def walk(g, goal, clear=True, damage=30):
    """Go room by room to goal. A shut door means a fight is on: clear the room first, as a player must."""
    rooms = g.call("rooms")
    route = path(rooms, rooms["current"], goal, only_open=False)
    if route is None:
        raise RuntimeError(f"{g.name}: no way from {rooms['current']} to {goal}")
    for k in route[1:]:
        cur = g.call("rooms")
        here = next(r for r in cur["rooms"] if r["key"] == cur["current"])
        door = next((d for d in here["doors"] if d["to"] == k), None)
        if door and not door["open"] and clear:
            clear_room(g, damage=damage)
            time.sleep(1.5)
            cur = g.call("rooms")
            here = next(r for r in cur["rooms"] if r["key"] == cur["current"])
            door = next((d for d in here["doors"] if d["to"] == k), None)
            if door and not door["open"]:
                (ARRIVAL_FORCED if cur["current"] == ARRIVAL.get(g.name) else FORCED).append(
                    f"{g.name}: {cur['current']}->{k} ({here['enemiesLeft']} enemies left by the room's count)")
                g.call("door", to=k, force=True)
                g.wait(f"room {k}", lambda s: g.call("rooms")["current"] == k and s["hero"]["canMove"] and not s["loading"], timeout=20)
                continue
        step(g, k)
