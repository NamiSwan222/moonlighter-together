"""Do the arrival room's doors on floor 2 open in the plain game (one copy, no session)? Prints the door states
over time after arriving. Pass 'host' to run the same with a session open (no guest)."""
import json
import sys
import time
from mt import Game, kill_all
from dungeon import wait_floor, walk, clear_room

kill_all()
g = Game("solo", 7801, profile="t-solo", fresh=True).start()
try:
    g.call("startgame", fresh=True, timeout=120)
    if "host" in sys.argv:
        g.call("host", port=7810)
    g.call("godmode", on=True)
    g.call("enterdungeon", culture="Golem")
    wait_floor(g, 0)
    rooms = g.call("rooms")
    goal = [r["key"] for r in rooms["rooms"] if r["exit"]][0]
    walk(g, goal)
    clear_room(g, damage=40)
    g.call("exit")
    wait_floor(g, 1)
    for t in range(8):
        rooms = g.call("rooms")
        here = next(r for r in rooms["rooms"] if r["key"] == rooms["current"])
        print(t * 2, "s:", here["key"], "enemies", here["enemiesLeft"], "doors", [(d["to"], d["open"]) for d in here["doors"]], flush=True)
        if t == 3:
            print("  clearing:", clear_room(g, damage=40), flush=True)
        time.sleep(2)
finally:
    g.stop()
