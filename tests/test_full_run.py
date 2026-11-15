"""A full dungeon run for two players: every floor of a dungeon down to its guardian, fought together,
then home. Usage: py test_full_run.py [Golem|Forest|Desert|Tech]"""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair
import dungeon
from dungeon import wait_floor, path, walk, clear_room


def find(rooms, pred):
    return [r["key"] for r in rooms["rooms"] if pred(r)]


def main(culture="Golem"):
    kill_all()
    c = Checks(f"full run: {culture} dungeon to the boss and home")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        host.call("godmode", on=True); guest.call("godmode", on=True)
        host.call("enterdungeon", culture=culture)
        for level in range(0, 3):
            hs = wait_floor(host, level, timeout=150); gs = wait_floor(guest, level, timeout=150)
            fh = host.call("floorsig"); fg = guest.call("floorsig")
            c.check(f"floor {level + 1}: both there, same floor", fh["sig"] == fg["sig"], f"{fh['sig']} / {fg['sig']}")
            host.call("godmode", on=True); guest.call("godmode", on=True)
            rooms = host.call("rooms")
            if level < 2:
                exits = find(rooms, lambda r: r["exit"])
                if not exits:
                    c.check(f"floor {level + 1}: exit found", False, "no room with a floor exit")
                    return c.finish()
                goal = exits[0]
                # Split up on the way: the guest takes the first room on the route alone, then both meet.
                route = path(rooms, rooms["current"], goal, only_open=False)
                c.note(f"floor {level + 1}: route {route}")
                walk(host, goal)
                walk(guest, goal)
                clear_room(host, damage=40)
                time.sleep(1)
                # The guest takes the exit: the host must come along.
                guest.call("exit")
            else:
                boss = rooms["bossRoom"]
                route = path(rooms, rooms["current"], boss, only_open=False)
                if route is None:
                    import json
                    c.note("boss room " + str(boss) + "; doors: " + json.dumps([(r["key"], [(d["to"], d["kind"]) for d in r["doors"]]) for r in rooms["rooms"]]))
                before = route[-2]
                walk(host, before); walk(guest, before)
                clear_room(host, damage=40)
                time.sleep(1)
                host.call("door", to=boss, force=True)
                host.wait("host in the boss room", lambda s: host.call("rooms")["current"] == boss and not s["loading"], timeout=40)
                time.sleep(1.5)
                guest.call("door", to=boss, force=True)
                guest.wait("guest in the boss room", lambda s: guest.call("rooms")["current"] == boss and not s["loading"], timeout=40)
                time.sleep(3)
                c.note("host walks in: " + str(host.call("bossalert")))
                time.sleep(1)
                c.note("guest walks in: " + str(guest.call("bossalert")))
                # The boss's entrance takes a few seconds and it cannot be hurt during it.
                # The boss has woken when it is active; some bosses (the Desert serpent) stay invulnerable until
                # parts of them are destroyed, so wait only for "active" and then fight the whole room.
                host.wait("boss awake", lambda b: b and b["active"], cmd="boss", timeout=60)
                time.sleep(8)
                time.sleep(3)
                bh = host.call("boss"); bg = guest.call("boss")
                c.check("boss fight: host owns the boss, guest sees a mirrored boss", bh and bg and not bh["mirrored"] and bg["mirrored"], f"host {bh}, guest {bg}")
                guest.shot("boss"); host.shot("boss")
                # Both hit it: the guest's hits go to the host.
                end = time.time() + 240
                hits = 0
                while time.time() < end:
                    bh = host.call("boss")
                    if not bh["alive"]:
                        break
                    try:
                        hits += guest.call("hitall", damage=60) + host.call("hitall", damage=60)
                    except Exception as e:  # noqa - the boss may be mid-phase (untargetable)
                        pass
                    time.sleep(0.4)
                bh = host.call("boss")
                c.check("boss defeated", not bh["alive"], f"after {hits} hits, hp {bh['hp']}")
                bg = guest.wait("boss dead on the guest", lambda b: not b["alive"], cmd="boss", timeout=30)
                c.check("boss dies on the guest's copy too", not bg["alive"], str(bg))
                host.shot("boss-dead"); guest.shot("boss-dead")
        # Home through the boss room's portal path the game offers: the host goes home, the guest comes along.
        time.sleep(8)
        host.call("gohome")
        hs = host.wait("host home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=120)
        gs = guest.wait("guest home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=120)
        c.check("both back in the village", True, f"host {hs['zone']}, guest {gs['zone']}")
        info = host.call("slotinfo", current=True)
        c.check("dungeon counted as beaten in the host's world", info.get("defeated", 0) & (1 << {"Golem": 1, "Forest": 2, "Desert": 3, "Tech": 4}[culture]) != 0, str(info))
        c.check("no locked door had to be forced (outside arrival rooms)", not dungeon.FORCED, "; ".join(dungeon.FORCED))
        if dungeon.FORCED_KILLS:
            c.note("enemies the bot could not hurt with hits and finished directly (needs a human check): " + "; ".join(dungeon.FORCED_KILLS))
        if dungeon.ARRIVAL_FORCED:
            c.note("arrival-room doors forced (same in the plain game with the bot): " + "; ".join(dungeon.ARRIVAL_FORCED))
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
    sys.exit(0 if main(sys.argv[1] if len(sys.argv) > 1 else "Golem") else 1)
