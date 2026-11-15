import json
from mt import Game, kill_all
kill_all()
g = Game("solo", 7801, profile="t-solo").start()
try:
    import time; time.sleep(6)
    print(json.dumps(g.call("dlc"), indent=1))
finally:
    g.stop()
