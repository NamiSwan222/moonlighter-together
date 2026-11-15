"""Combat: one shared room (host owns it, guest sees mirrored enemies, enemies go for the guest too, the
guest's hits count), then two rooms fought at the same time by one player each, then a hand-over of a room
whose owner walks out mid-fight."""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair
from dungeon import wait_floor, path, step, clear_room, walk


def rooms_with_enemies(rooms, exclude=()):
    return [r["key"] for r in rooms["rooms"] if r["enemiesLeft"] > 0 and not r["boss"] and r["key"] not in exclude]


def main():
    kill_all()
    c = Checks("dungeon combat: shared room, two rooms at once, owner leaves")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        host.call("enterdungeon", culture="Golem")
        wait_floor(host, 0); wait_floor(guest, 0)
        host.call("godmode", on=True)

        # ---------------- 1. one room, both players ----------------
        rooms = host.call("rooms")
        candidates = [k for k in rooms_with_enemies(rooms, exclude=(rooms["current"],)) if path(rooms, rooms["current"], k, only_open=False)]
        candidates.sort(key=lambda k: len(path(rooms, rooms["current"], k, only_open=False)))
        target = candidates[0]
        walk(host, target)
        time.sleep(0.7)
        walk(guest, target)
        time.sleep(2.5)
        eh = host.call("enemies"); eg = guest.call("enemies")
        c.check("shared room is owned by the host", eh["amOwner"] and not eg["amOwner"] and eg["owner"] == 0, f"{eh['zone']} owner {eg['owner']}")
        mirrored = [e for e in eg["here"] if e["mirrored"]]
        c.check("guest's enemies are mirrored copies", len(eg["here"]) > 0 and len(mirrored) == len(eg["here"]), f"{len(mirrored)}/{len(eg['here'])} mirrored")
        hp = {e["id"]: e for e in eh["here"]}
        dists = [abs(e["x"] - hp[e["id"]]["x"]) + abs(e["y"] - hp[e["id"]]["y"]) for e in eg["here"] if e["id"] in hp]
        c.check("mirrored enemies stand where the host's are", dists and max(dists) < 40, f"largest gap {max(dists) if dists else None:.1f} px over {len(dists)} enemies")
        c.check("enemy states flow", eg["stats"]["StatesApplied"] > 10, f"applied {eg['stats']['StatesApplied']}")

        # Do enemies go after the guest? Put the host far away and watch.
        guest.call("heal")
        hstate = host.call("state")["hero"]; gstate = guest.call("state")["hero"]
        ex = eh["here"][0]
        guest.call("teleport", x=ex["x"] + 10, y=ex["y"])
        targeted = False; hurt = False; start_hp = guest.call("state")["hero"]["hp"]
        end = time.time() + 25
        while time.time() < end and not (targeted and hurt):
            if any(e["target"] == 1 for e in host.call("enemies")["here"]):
                targeted = True
            if guest.call("state")["hero"]["hp"] < start_hp:
                hurt = True
            time.sleep(0.3)
        c.check("enemies choose the guest as target", targeted, "seen on the host's copy")
        c.check("the guest gets hurt by mirrored enemies", hurt, f"guest hp {guest.call('state')['hero']['hp']} (started {start_hp})")
        guest.call("godmode", on=True)

        before_h = host.call("enemies")["stats"]; before_g = guest.call("enemies")["stats"]
        ok = clear_room(guest, damage=30)
        time.sleep(2)
        eh = host.call("enemies"); eg = guest.call("enemies")
        c.check("guest clears the shared room with its hits", ok and len(eh["here"]) == 0 and len(eg["here"]) == 0, f"left host {len(eh['here'])} guest {len(eg['here'])}")
        c.check("guest hits counted on the host", eh["stats"]["HitsApplied"] > before_h["HitsApplied"], f"sent {eg['stats']['HitsSent']} applied {eh['stats']['HitsApplied']}")
        c.check("deaths ran on the guest's copy", eg["stats"]["DeathsApplied"] > before_g["DeathsApplied"], f"deaths applied {eg['stats']['DeathsApplied']}")
        host.shot("shared"); guest.shot("shared")

        # ---------------- 2. two rooms at once ----------------
        rooms = host.call("rooms")
        left = rooms_with_enemies(rooms)
        cur = rooms["current"]
        reach = [k for k in left if k != cur and path(rooms, cur, k, only_open=False)]
        reach.sort(key=lambda k: len(path(rooms, cur, k, only_open=False)))
        if len(reach) >= 2:
            a, b = reach[0], reach[1]
            walk(host, a); walk(guest, b)
            time.sleep(1.5)
            eh = host.call("enemies"); eg = guest.call("enemies")
            c.check("each player owns the room they are fighting in", eh["amOwner"] and eg["amOwner"] and eh["zone"] != eg["zone"], f"host {eh['zone']} guest {eg['zone']}")
            ds_h = eh["stats"]["DeathsSent"]; ds_g = eg["stats"]["DeathsSent"]; sr_h = eh["stats"]["SilentRemovals"]; sr_g = eg["stats"]["SilentRemovals"]
            n_a = len(eh["here"]); n_b = len(eg["here"])
            ok1 = clear_room(host, damage=30); ok2 = clear_room(guest, damage=30)
            time.sleep(1.5)
            eh = host.call("enemies"); eg = guest.call("enemies")
            c.check("both rooms cleared", ok1 and ok2, f"room {a}: {n_a} enemies, room {b}: {n_b} enemies")
            c.check("each side's kills reach the other", eh["stats"]["SilentRemovals"] - sr_h >= n_b and eg["stats"]["SilentRemovals"] - sr_g >= n_a,
                    f"host removed {eh['stats']['SilentRemovals'] - sr_h}/{n_b}, guest removed {eg['stats']['SilentRemovals'] - sr_g}/{n_a}")
            # The host walks into the guest's cleared room: nothing there any more.
            walk(host, b)
            time.sleep(1)
            c.check("a room cleared by the guest is empty for the host", len(host.call("enemies")["here"]) == 0, host.call("rooms")["current"])
        else:
            c.check("two rooms with enemies reachable", False, str(reach))

        # ---------------- 3. owner walks out mid-fight ----------------
        rooms = host.call("rooms")
        cur = rooms["current"]
        reach = [k for k in rooms_with_enemies(rooms, exclude=(cur,)) if path(rooms, cur, k, only_open=False)]
        reach.sort(key=lambda k: len(path(rooms, cur, k, only_open=False)))
        if reach:
            t = reach[0]
            c.note(f"hand-over room {t}"); walk(host, t); c.note("host in"); time.sleep(0.6); walk(guest, t); c.note("guest in"); time.sleep(1.5)
            # Hurt one enemy on the host side, then the host leaves through the door it came in by.
            c.note("asking host for enemies")
            eh = host.call("enemies", timeout=10)
            c.note(f"host has {len(eh['here'])} enemies here")
            victim = eh["here"][0]["id"]
            host.call("hit", enemy=victim, damage=5)
            time.sleep(0.6)
            hp_before = next(e["hp"] for e in host.call("enemies")["here"] if e["id"] == victim)
            back = path(host.call("rooms"), t, cur, only_open=False)[1]
            c.note(f"host leaves to {back}")
            host.call("door", to=back, force=True)
            host.wait(f"room {back}", lambda s: host.call("rooms")["current"] == back and not s["loading"], timeout=20)
            time.sleep(1.5)
            eg = guest.call("enemies")
            c.check("guest takes the room over", eg["amOwner"] and eg["owner"] == 1 and eg["stats"]["Takeovers"] >= 1, f"owner {eg['owner']} takeovers {eg['stats']['Takeovers']}")
            mine = [e for e in eg["here"] if e["id"] == victim]
            c.check("the enemy keeps its wounds after the hand-over", mine and abs(mine[0]["hp"] - hp_before) < 0.5, f"host had {hp_before}, guest has {mine[0]['hp'] if mine else None}")
            c.check("enemies run their own AI on the new owner", all(not e["mirrored"] for e in eg["here"]), f"{sum(e['mirrored'] for e in eg['here'])} still mirrored")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        import os
        if os.environ.get("KEEP"):
            return c.finish()
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
