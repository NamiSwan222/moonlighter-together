"""One copy: from the main menu straight into a fresh game in town."""
import json
import time
from mt import Game, kill_all

kill_all()
g = Game("solo", 7801, profile="t-solo", fresh=True).start()
try:
    time.sleep(8)
    print(json.dumps(g.call("state")))
    print(json.dumps(g.call("startgame", fresh=True, timeout=120)))
    time.sleep(4)
    print(json.dumps(g.call("state")))
    print(g.shot("boot-town"))
finally:
    g.stop()
