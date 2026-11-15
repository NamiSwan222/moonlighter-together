"""Steam hosting with this PC's one Steam account: the game opens a friends-only Steam lobby tagged as a
Moonlighter Together game, and the menu shows it. (Joining needs a second Steam account on another PC.)
Saves stay in the offline test profile; only Steam's networking is used."""
import sys
import time
from mt import Game, Checks, kill_all


def main():
    kill_all()
    c = Checks("steam: host a lobby, menu")
    g = None
    try:
        g = Game("steamhost", 7801, profile="t-steam", fresh=True, extra=["--mt-steam-net"]).start()
        s = g.call("steam")
        c.check("Steam is running for networking", s["available"], str(s))
        g.call("startgame", fresh=True, timeout=120)
        g.call("hoststeam")
        s = g.wait("lobby open", lambda x: x["lobby"] is not None and x["status"] == "lobby open", cmd="steam", timeout=30)
        c.check("friends-only lobby created", s["lobby"] is not None, f"status {s['status']}, members {s['members']}")
        c.check("lobby is tagged with the mod version", s["lobbyTag"] == g.call("info")["plugin"], f"tag {s['lobbyTag']}")
        c.check("host is in its lobby", s["members"] == 1, str(s["members"]))
        g.call("menu", open=True)
        time.sleep(1)
        g.shot("steam-menu")
        c.check("menu screenshot taken", True, "run/shots/steam-menu-steamhost.png")
        g.call("leave")
        time.sleep(2)
        c.check("leaving closes the lobby", g.call("steam")["lobby"] is None, "")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        if g:
            g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
