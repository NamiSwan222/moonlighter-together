"""Between Dimensions (DLC): both players go into the Wanderer dungeon through its portal, get the same floor,
fight a room together, and go down floors together."""
import sys
import time
from mt import Checks, kill_all
import dungeon
from dungeon import wait_floor, path, walk, clear_room
from test_join import setup_pair


def main(floors=3):
    kill_all()
    c = Checks("DLC: Between Dimensions dungeon together")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        dh = host.call("dlcstate"); dg = guest.call("dlcstate")
        c.check("DLC active on both copies", dh["active"] and dg["active"], f"host {dh}, guest {dg}")
        host.call("godmode", on=True); guest.call("godmode", on=True)
        host.call("enterwanderer")
        for level in range(floors):
            hs = wait_floor(host, level, timeout=180); gs = wait_floor(guest, level, timeout=180)
            fh = host.call("floorsig"); fg = guest.call("floorsig")
            c.check(f"DLC floor {level + 1}: both there, same floor", hs["dungeon"]["culture"] == "Wanderer" and fh["sig"] == fg["sig"],
                    f"{hs['dungeon']['culture']} {fh['sig']} / {fg['sig']}")
            host.call("godmode", on=True); guest.call("godmode", on=True)
            rooms = host.call("rooms")
            cur = rooms["current"]
            fights = sorted([r["key"] for r in rooms["rooms"] if r["enemiesLeft"] > 0 and not r["boss"] and r["key"] != cur and path(rooms, cur, r["key"], only_open=False)],
                            key=lambda k: len(path(rooms, cur, k, only_open=False)))
            if fights and level == 0:
                t = fights[0]
                walk(host, t); time.sleep(0.6); walk(guest, t); time.sleep(1.5)
                eg = guest.call("enemies")
                c.check("DLC room: guest sees mirrored enemies", eg["here"] and all(e["mirrored"] for e in eg["here"]), f"{len(eg['here'])} enemies")
                ok = clear_room(guest, damage=40)
                time.sleep(1.5)
                c.check("DLC room cleared by the guest's hits", ok and not host.call("enemies")["here"], "")
            exits = [r["key"] for r in host.call("rooms")["rooms"] if r["exit"]]
            if level == floors - 1:
                break
            if not exits:
                c.check(f"DLC floor {level + 1}: exit found", False, "no floor exit room")
                break
            walk(host, exits[0]); walk(guest, exits[0])
            clear_room(host, damage=40)
            time.sleep(1)
            guest.call("exit")
        c.check("no locked door had to be forced (outside arrival rooms)", not dungeon.FORCED, "; ".join(dungeon.FORCED))
        if dungeon.FORCED_KILLS:
            c.note("finished directly by the bot: " + "; ".join(dungeon.FORCED_KILLS))
        host.call("gohome")
        hs = host.wait("host home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=120)
        gs = guest.wait("guest home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=120)
        c.check("both back in the village", True, f"{hs['zone']} / {gs['zone']}")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
