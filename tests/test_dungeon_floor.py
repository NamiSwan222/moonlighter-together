"""Host enters the Golem dungeon; the guest comes along; both floors must be identical; then the two players
go to two different rooms at the same time and each one runs their own room."""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair
from dungeon import wait_floor, neighbours, step


def main():
    kill_all()
    c = Checks("dungeon: same floor, separate rooms")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        host.call("enterdungeon", culture="Golem")
        hs = wait_floor(host, 0); gs = wait_floor(guest, 0)
        c.check("both reach floor 1", True, f"host room {hs['dungeon']['room']}, guest room {gs['dungeon']['room']}")
        fh = host.call("floorsig", detail=True); fg = guest.call("floorsig", detail=True)
        same = fh["sig"] == fg["sig"]
        if not same:
            diff = [(a, b) for a, b in zip(fh["rooms"] or [], fg["rooms"] or []) if a != b][:3]
            c.note(f"first differences: {diff}")
        c.check("identical floor (rooms, enemies, chests)", same, f"host {fh['sig']} seed {fh['seed']}, guest {fg['sig']} seed {fg['seed']}")
        eh = host.call("enemies"); eg = guest.call("enemies")
        c.check("same enemies registered", eh["generated"] == eg["generated"] and eh["generated"] > 0, f"host {eh['generated']}, guest {eg['generated']}")

        # Two different rooms at once.
        rooms = host.call("rooms")
        start = rooms["current"]
        nb = neighbours(rooms, start)
        c.check("start room has two open doors", len(nb) >= 2, f"{start} -> {nb}")
        if len(nb) >= 2:
            step(host, nb[0]); step(guest, nb[1])
            time.sleep(2)
            eh = host.call("enemies"); eg = guest.call("enemies")
            c.check("host and guest in different rooms", eh["zone"] != eg["zone"], f"host {eh['zone']} | guest {eg['zone']}")
            c.check("each owns their own room", eh["amOwner"] and eg["amOwner"] and eh["owner"] == 0 and eg["owner"] == 1, f"host owner={eh['owner']} guest owner={eg['owner']}")
            c.check("nobody was pulled along", host.call("rooms")["current"] == nb[0] and guest.call("rooms")["current"] == nb[1], "")
            host.shot("rooms"); guest.shot("rooms")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
