"""Two copies connect; the guest's network is cut long enough for the host to notice; the guest must
come back by itself as the same player number."""
import sys
import time
from mt import Game, Checks, kill_all

PORT = 7810


def main():
    kill_all()
    c = Checks("session: connect, drop, reconnect")
    host = Game("host", 7801, x=0, profile="t-host").start()
    guest = Game("guest", 7802, x=820, profile="t-guest").start()
    try:
        host.call("host", port=PORT)
        guest.call("join", address=f"127.0.0.1:{PORT}")
        g = guest.wait("joined", lambda s: s["state"] == "Joined", cmd="session", timeout=20)
        c.check("guest joins", g["state"] == "Joined", f"guest is player {g['me']}")
        h = host.wait("host sees guest", lambda s: any(p["id"] == g["me"] and p["up"] for p in s["players"]), cmd="session", timeout=10)
        c.check("host lists the guest", True, str(h["players"]))
        time.sleep(2)
        g = guest.call("session")
        c.check("clock in step", abs(g["now"] - host.call("session")["now"]) < 0.2, f"rtt {g['rtt']} ms")

        first_id = g["me"]
        guest.call("droplink", seconds=12)
        h = host.wait("host notices the drop", lambda s: any(p["id"] == first_id and not p["up"] for p in s["players"]), cmd="session", timeout=20)
        c.check("host sees the guest drop", True, "place kept")
        g = guest.wait("guest back", lambda s: s["state"] == "Joined" and s["reconnects"] >= 1, cmd="session", timeout=40)
        c.check("guest comes back by itself", True, f"reconnects={g['reconnects']}")
        c.check("same player number", g["me"] == first_id, f"{first_id} -> {g['me']}")
        h = host.wait("host sees guest again", lambda s: any(p["id"] == first_id and p["up"] for p in s["players"]), cmd="session", timeout=10)
        c.check("host sees the guest again", len(h["players"]) == 2, str(h["players"]))
        c.check("no bad packets", h["bad"] == 0 and g["bad"] == 0, f"host {h['bad']} guest {g['bad']}")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        guest.stop(); host.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
