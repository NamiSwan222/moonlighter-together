"""One copy, no session: enter the Golem dungeon from the test command (does plain generation work?)."""
import json
import sys
import time
from mt import Game, kill_all
from dungeon import wait_floor

kill_all()
g = Game("solo", 7801, profile="t-solo", fresh=True, extra=["--mt-no-isolate"] if "noiso" in sys.argv else []).start()
try:
    g.call("startgame", fresh=True, timeout=120)
    if "host" in sys.argv:
        g.call("host", port=7810)
    g.call("enterdungeon", culture="Golem")
    s = wait_floor(g, 0, timeout=90)
    print(json.dumps(s["dungeon"]))
    print(json.dumps(g.call("floorsig")))
    g.shot("solo-dungeon")
finally:
    g.stop()
