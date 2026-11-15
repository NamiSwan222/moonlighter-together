"""Host starts a game; the guest joins from its main menu and must land in the host's world with its OWN
character (gold from its own slot), see the host's Will, and its own save must keep its own world."""
import sys
import time
from mt import Game, Checks, kill_all

PORT = 7810


def setup_pair(c, guest_gold=1234, host_days=None):
    """Start host (in town) and guest (in the host's world). Returns (host, guest)."""
    host = Game("host", 7801, x=0, profile="t-host", fresh=True).start()
    guest = Game("guest", 7802, x=820, profile="t-guest", fresh=True).start()
    time.sleep(3)
    hs = host.call("startgame", fresh=True, timeout=120)
    c.check("host in town", hs["zone"] in ("town", "shop"), hs["zone"])
    if host_days is not None:
        host.call("editslot", slot=0, days=host_days)
    guest.call("editslot", slot=0, fresh=True, gold=guest_gold)
    host.call("host", port=PORT)
    import os
    netem = os.environ.get("NETEM")
    if netem:
        import netem as nm
        d, j, l = (float(x) for x in netem.split(","))
        setup_pair.relay = nm.start_relay(PORT + 1, PORT, d, j, l)
        c.note(f"guest connects through a bad-internet relay: one-way {d}+-{j} ms, {l}% loss each way")
        guest.call("join", address=f"127.0.0.1:{PORT + 1}")
    else:
        guest.call("join", address=f"127.0.0.1:{PORT}")
    gs = guest.wait("guest in the host's world", lambda s: s["zone"] in ("town", "shop") and s["hero"] is not None and not s["loading"]
                    and s["hero"]["canMove"], timeout=120)
    return host, guest, gs


def main():
    kill_all()
    c = Checks("join: host world, own character, see each other")
    host = guest = None
    try:
        host, guest, gs = setup_pair(c, guest_gold=1234, host_days=7)
        p = guest.call("presence")
        c.check("guest got the host's world", p["worldsReceived"] == 1 and p["inHostWorld"], str({k: p[k] for k in ("worldsReceived", "inHostWorld", "worldRefused")}))
        c.check("guest keeps its own gold", gs["hero"]["gold"] == 1234, f"guest gold {gs['hero']['gold']}, host gold {host.call('state')['hero']['gold']}")
        cur = guest.call("slotinfo", current=True)
        c.check("guest plays the host's day", cur["days"] == 7, f"days in played slot {cur['days']}")

        # Put both Wills side by side and look.
        h = host.call("state")["hero"]
        guest.call("teleport", x=h["x"] + 40, y=h["y"])
        time.sleep(1.5)
        pg = guest.call("presence"); ph = host.call("presence")
        rg = [r for r in pg["remotes"] if r["id"] == 0]
        rh = [r for r in ph["remotes"] if r["id"] == 1]
        c.check("guest sees the host's Will", rg and rg[0]["visible"] and rg[0]["drawn"] > 0, str(rg))
        c.check("host sees the guest's Will", rh and rh[0]["visible"] and rh[0]["drawn"] > 0, str(rh))
        if rg:
            c.check("host's Will drawn where he stands", abs(rg[0]["px"] - h["x"]) < 3 and abs(rg[0]["py"] - h["y"]) < 3, f"drawn at {rg[0]['px']:.1f},{rg[0]['py']:.1f}, real {h['x']:.1f},{h['y']:.1f}")
        c.check("every sprite found", pg["spriteMisses"] == 0 and ph["spriteMisses"] == 0, f"guest misses {pg['spriteMisses']}, host misses {ph['spriteMisses']}")
        guest.shot("join"); host.shot("join")

        # Saving on the guest writes only its character into its own slot.
        guest.call("addgold", amount=66)
        guest.call("save")
        own = guest.call("slotinfo", slot=0)
        c.check("guest's own slot keeps its own world", own["days"] != 7, f"own slot days {own['days']}")
        c.check("guest's own slot gets its new gold", own["gold"] == 1300, f"own slot gold {own['gold']}")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
