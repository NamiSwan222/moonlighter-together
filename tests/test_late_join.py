"""Joining while the host is already in a dungeon: the guest lands in the host's world, is sent to the host's
floor (same floor, same seed), and what the host already killed there is gone for the guest too."""
import sys
import time
from mt import Game, Checks, kill_all
from dungeon import wait_floor, path, walk, clear_room

PORT = 7810


def main():
    kill_all()
    c = Checks("late join into a dungeon")
    host = guest = None
    try:
        host = Game("host", 7801, x=0, profile="t-host", fresh=True).start()
        host.call("startgame", fresh=True, timeout=120)
        host.call("godmode", on=True)
        host.call("host", port=PORT)
        host.call("enterdungeon", culture="Golem")
        wait_floor(host, 0)
        rooms = host.call("rooms")
        cur = rooms["current"]
        t = sorted([r["key"] for r in rooms["rooms"] if r["enemiesLeft"] > 0 and not r["boss"] and r["key"] != cur and path(rooms, cur, r["key"], only_open=False)],
                   key=lambda k: len(path(rooms, cur, k, only_open=False)))[0]
        walk(host, t)
        c.check("host clears a room before anyone joins", clear_room(host, damage=40), t)

        guest = Game("guest", 7802, x=820, profile="t-guest", fresh=True).start()
        guest.call("editslot", slot=0, fresh=True, gold=555)
        guest.call("join", address=f"127.0.0.1:{PORT}")
        gs = wait_floor(guest, 0, timeout=180)
        c.check("guest ends up on the host's floor", gs["zone"] == "dungeon", f"room {gs['dungeon']['room']}")
        fh = host.call("floorsig"); fg = guest.call("floorsig")
        c.check("same floor", fh["sig"] == fg["sig"] and fh["seed"] == fg["seed"], f"{fh['sig']} / {fg['sig']}")
        c.check("with its own character", gs["hero"]["gold"] == 555, f"gold {gs['hero']['gold']}")
        time.sleep(2)
        guest.call("godmode", on=True)
        walk(guest, t)
        time.sleep(1.5)
        c.check("the room the host cleared is empty for the guest", len(guest.call("enemies")["here"]) == 0, "")
        c.check("they see each other there", any(r["id"] == 0 and r["visible"] for r in guest.call("presence")["remotes"]), "")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
