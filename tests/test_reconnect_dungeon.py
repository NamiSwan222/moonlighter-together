"""A dropped connection in a dungeon: the guest's network goes away for 15 s while the host clears a room;
the guest finds its way back by itself, as the same player, on the same floor, and the room the host
cleared meanwhile is empty for it too."""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair
from dungeon import wait_floor, path, walk, clear_room


def main():
    kill_all()
    c = Checks("reconnect in a dungeon")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        host.call("godmode", on=True); guest.call("godmode", on=True)
        host.call("enterdungeon", culture="Golem")
        wait_floor(host, 0); wait_floor(guest, 0)
        rooms = host.call("rooms")
        cur = rooms["current"]
        targets = [r["key"] for r in rooms["rooms"] if r["enemiesLeft"] > 0 and not r["boss"] and r["key"] != cur and path(rooms, cur, r["key"], only_open=False)]
        targets.sort(key=lambda k: len(path(rooms, cur, k, only_open=False)))
        t = targets[0]
        n = next(r["enemiesLeft"] for r in rooms["rooms"] if r["key"] == t)

        guest.call("droplink", seconds=15)
        host.wait("host sees the drop", lambda s: any(p["id"] == 1 and not p["up"] for p in s["players"]), cmd="session", timeout=20)
        c.check("host notices the guest is gone", True, "")
        walk(host, t)
        ok = clear_room(host, damage=40)
        c.check("host clears a room while the guest is away", ok, f"room {t}, {n} enemies")
        hp = host.call("presence")
        c.check("guest's Will waits greyed out on the host", any(r["id"] == 1 for r in hp["remotes"]), "")

        gs = guest.wait("guest back", lambda s: s["state"] == "Joined" and s["reconnects"] >= 1, cmd="session", timeout=60)
        c.check("guest comes back by itself", True, f"as player {gs['me']}")
        c.check("same player number", gs["me"] == 1, str(gs["me"]))
        st = guest.call("state")
        fh = host.call("floorsig", detail=True); fg = guest.call("floorsig", detail=True)
        diff = [(a, b) for a, b in zip(fh["rooms"] or [], fg["rooms"] or []) if a != b][:2]
        c.check("guest is still on the same floor", st["zone"] == "dungeon" and fh["seed"] == fg["seed"] and fh["sig"] == fg["sig"],
                f"zone {st['zone']}, seeds {fh['seed']}/{fg['seed']}, sigs {fh['sig']}/{fg['sig']} {diff}")
        time.sleep(2)
        walk(guest, t)
        time.sleep(1.5)
        eg = guest.call("enemies")
        c.check("the room cleared meanwhile is empty for the guest", len(eg["here"]) == 0, f"{len(eg['here'])} enemies left on the guest")
        pg = guest.call("presence")
        c.check("guest sees the host again", any(r["id"] == 0 and r["visible"] for r in pg["remotes"]), str([(r["id"], r["zone"], r["visible"]) for r in pg["remotes"]]))
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
