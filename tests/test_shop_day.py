"""A shop day for two players: the host stocks the shelves and opens the shop; the guest sees the same
shelves, the host's customers walking in, items selling, and the day ending for both."""
import sys
import time
from mt import Checks, kill_all
from test_join import setup_pair


def main():
    kill_all()
    c = Checks("shop day: shelves, customers, sales, day end")
    host = guest = None
    try:
        host, guest, _ = setup_pair(c)
        sh = host.call("shop"); sg = guest.call("shop")
        c.check("same day and time on both", sh["dayTime"] == sg["dayTime"] and sh["days"] == sg["days"], f"host {sh['dayTime']} day {sh['days']}, guest {sg['dayTime']} day {sg['days']}")
        placed = host.call("stock", item="Rich Jelly", qty=1, slots=4)
        c.note(f"host put {placed} stacks on the shelves")
        time.sleep(2)
        sh = host.call("shop"); sg = guest.call("shop")
        c.check("guest sees the host's shelves", sh["shelves"] == sg["shelves"] and placed > 0, f"host {sh['shelves']} | guest {sg['shelves']}")
        gold0 = sh["gold"]

        host.call("openshop")
        sg = guest.wait("shop open on the guest", lambda s: s["open"], cmd="shop", timeout=15)
        c.check("shop opens for the guest too", sg["open"], "")
        host.call("timescale", value=4.0)
        seen = 0; local = 0; sold = False
        end = time.time() + 240
        while time.time() < end:
            sg = guest.call("shop"); sh = host.call("shop")
            seen = max(seen, sg["drawnCustomers"]); local = max(local, sg["localCustomers"])
            if sh["gold"] > gold0:
                sold = True
            # Somebody works the till: the guest presses it (its press is sent to the host's copy).
            if sh["waitingAtTill"]:
                guest.call("till")
            if not sh["open"]:
                break
            if seen and sold and time.time() > end - 200:
                pass
            time.sleep(0.5)
        guest.shot("shopday"); host.shot("shopday")
        c.check("guest sees the host's customers", seen > 0, f"up to {seen} customers drawn on the guest")
        c.check("guest's copy spawns no customers of its own", local == 0, f"guest local customers {local}")
        c.check("items sell during the day, rung up by the guest at the till", sold and host.call("shop")["TillSales"] > 0, f"host gold {gold0} -> {host.call('shop')['gold']}, till sales {host.call('shop')['TillSales']}, guest asks {guest.call('shop')['AsksSent']}")
        time.sleep(2)
        sh = host.call("shop"); sg = guest.call("shop")
        c.check("guest's shelves follow the sales", sh["shelves"] == sg["shelves"], f"host {sh['shelves']} | guest {sg['shelves']}")
        host.call("timescale", value=1.0)
        sh = host.wait("day over on the host", lambda s: not s["open"] and s["dayTime"] == "Afternoon", cmd="shop", timeout=240)
        sg = guest.wait("day over on the guest", lambda s: not s["open"] and s["dayTime"] == "Afternoon", cmd="shop", timeout=30)
        c.check("the day ends for both", True, f"host {sh['dayTime']} open={sh['open']}, guest {sg['dayTime']} open={sg['open']}")
        c.check("guest saw shop updates", sg["StatesApplied"] > 0 and sg["ShelfUpdatesApplied"] > 0 and sg["CustomerFramesApplied"] > 10,
                f"states {sg['StatesApplied']}, shelf updates {sg['ShelfUpdatesApplied']}, customer frames {sg['CustomerFramesApplied']}")
    except Exception as e:  # noqa
        c.check("scenario ran to the end", False, repr(e))
    finally:
        for g in (guest, host):
            if g:
                g.stop()
    return c.finish()


if __name__ == "__main__":
    sys.exit(0 if main() else 1)
