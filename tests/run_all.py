"""Runs every scenario in turn and prints one line per scenario. Usage: py -u run_all.py [name-filter]"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
SUITE = [
    ("session", ["test_session.py"], {}),
    ("join", ["test_join.py"], {}),
    ("dungeon floor", ["test_dungeon_floor.py"], {}),
    ("combat", ["test_dungeon_combat.py"], {}),
    ("combat, bad internet 120ms 2%", ["test_dungeon_combat.py"], {"NETEM": "60,20,2"}),
    ("death and revive", ["test_death.py"], {}),
    ("death, bad internet 120ms 2%", ["test_death.py"], {"NETEM": "60,20,2"}),
    ("shop day", ["test_shop_day.py"], {}),
    ("reconnect in dungeon", ["test_reconnect_dungeon.py"], {}),
    ("late join", ["test_late_join.py"], {}),
    ("full run Golem", ["test_full_run.py", "Golem"], {}),
    ("full run Forest", ["test_full_run.py", "Forest"], {}),
    ("full run Desert", ["test_full_run.py", "Desert"], {}),
    ("full run Tech", ["test_full_run.py", "Tech"], {}),
    ("full run Golem, bad internet", ["test_full_run.py", "Golem"], {"NETEM": "60,20,2"}),
    ("DLC Between Dimensions", ["test_dlc.py"], {}),
    ("steam host", ["test_steam_host.py"], {}),
]

flt = sys.argv[1].lower() if len(sys.argv) > 1 else ""
summary = []
for name, args, env in SUITE:
    if flt and flt not in name.lower():
        continue
    t0 = time.time()
    e = dict(os.environ); e.update(env)
    p = subprocess.run([sys.executable, "-u"] + args, cwd=HERE, env=e, capture_output=True, text=True, timeout=1800)
    out = p.stdout + p.stderr
    last = [l for l in out.splitlines() if l.startswith("==")]
    fails = [l.strip() for l in out.splitlines() if "[FAIL]" in l]
    line = f"{'PASS' if p.returncode == 0 else 'FAIL'}  {name}: {last[-1] if last else '(no summary)'}  [{time.time() - t0:.0f}s]"
    print(line, flush=True)
    for f in fails:
        print("      " + f[:400], flush=True)
    summary.append(line)
    with open(os.path.join(HERE, "..", "run", "log-" + name.replace(" ", "_").replace(",", "").replace("%", "") + ".txt"), "w", encoding="utf-8") as f:
        f.write(out)
print("\n".join(["", "SUMMARY"] + summary))
