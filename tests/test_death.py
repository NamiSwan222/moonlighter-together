"""Death and revive: the guest falls while the host stands -> ghost (not sent home); the host revives it;
then both fall -> the game's own death for both, results screen, everyone home."""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair
from dungeon import wait_floor


def main():
    kill_all()
    c = Checks("death: ghost, revive, everyone down")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        host.call("enterdungeon", culture="Golem")
        wait_floor(host, 0); wait_floor(guest, 0)
        host.call("godmode", on=True)
        guest.call("godmode", on=False)
        gold_before = guest.call("state")["hero"]["gold"]

        guest.call("hurtme", damage=9999)
        d = guest.wait("guest is a ghost", lambda s: s["me"], cmd="downed", timeout=30)
        gs = guest.call("state")
        c.check("guest falls and becomes a ghost", d["me"] and d["TimesDowned"] == 1, str(d))
        c.check("ghost stays in the dungeon", gs["zone"] == "dungeon" and not gs["hero"]["dead"], f"zone {gs['zone']} dead {gs['hero']['dead']}")
        c.check("ghost can move", gs["hero"]["canMove"], "")
        time.sleep(1)
        hd = host.call("downed"); hp = host.call("presence")
        c.check("host sees the guest as down", 1 in hd["ghosts"] and any(r["id"] == 1 and r["ghost"] for r in hp["remotes"]), str(hd["ghosts"]))
        guest.shot("ghost"); host.shot("ghost")

        # Ghost cannot be hurt.
        hp_ghost = guest.call("state")["hero"]["hp"]
        guest.call("hurtme", damage=50)
        c.check("a ghost takes no damage", guest.call("state")["hero"]["hp"] == hp_ghost, f"{hp_ghost}")

        # Revive: the host walks up and presses revive.
        g = guest.call("state")["hero"]
        host.call("teleport", x=g["x"] + 15, y=g["y"])
        time.sleep(1.0)
        c.check("host is in reach of the ghost", host.call("downed")["inReach"] == 1, str(host.call("downed")))
        host.call("revive")
        d = guest.wait("guest revived", lambda s: not s["me"], cmd="downed", timeout=15)
        gs = guest.call("state")["hero"]
        c.check("guest is back on its feet", not d["me"] and d["TimesRevived"] == 1, str(d))
        c.check("with half its health", gs["hp"] >= 0.5 * gs["maxHp"] - 0.5, f"{gs['hp']}/{gs['maxHp']}")
        time.sleep(1)
        guest.call("hurtme", damage=5)
        c.check("revived guest can be hurt again", guest.call("state")["hero"]["hp"] < gs["hp"], "")

        # Everyone down. (Will is invulnerable for a second after any hit.)
        time.sleep(1.5)
        guest.call("hurtme", damage=9999)
        guest.wait("guest ghost again", lambda s: s["me"], cmd="downed", timeout=30)
        host.call("godmode", on=False)
        host.call("hurtme", damage=9999)
        hs = host.wait("host results screen", lambda s: s["panel"] == "DungeonResultsPanel", timeout=40)
        gs = guest.wait("guest results screen", lambda s: s["panel"] == "DungeonResultsPanel", timeout=40)
        c.check("when everyone is down the run ends for both", True, f"host panel {hs['panel']}, guest panel {gs['panel']}")
        c.check("the real death happened on both", hs["hero"]["dead"] and gs["hero"]["dead"], f"host dead {hs['hero']['dead']}, guest dead {gs['hero']['dead']}")
        host.call("results"); guest.call("results")
        hs = host.wait("host home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=90)
        gs = guest.wait("guest home", lambda s: s["zone"] in ("town", "shop") and not s["loading"], timeout=90)
        c.check("both back in the village", True, f"host {hs['zone']}, guest {gs['zone']}")
        c.check("nobody is left a ghost", not host.call("downed")["me"] and not guest.call("downed")["me"], "")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
