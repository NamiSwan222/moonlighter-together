"""Test helpers for Moonlighter Together: start copies of the game and talk to their test control port.

Every game copy is started with --mt-profile (no Steam, saves in its own folder) and --mt-bridge PORT.
The bridge speaks one JSON object per line (see src/MoonlighterTogether/Testing/Bridge.cs).
"""
import json
import os
import socket
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
GAME = Path(os.environ.get("MT_GAME", ROOT / "testgame"))
RUN = ROOT / "run"
LOGS = RUN / "logs"
SHOTS = RUN / "shots"


class BridgeError(Exception):
    pass


class Game:
    """One running copy of the game."""

    def __init__(self, name, port, x=0, y=40, width=800, height=450, extra=None, profile=None, fresh=False):
        self.name = name
        self.port = port
        self.profile = profile or name
        self.proc = None
        self.sock = None
        self.rfile = None
        self._id = 0
        self.geometry = (x, y, width, height)
        self.extra = list(extra or [])
        self.fresh = fresh
        self.player_log = LOGS / f"{name}-player.log"

    # ---- process -------------------------------------------------------------------------------
    @property
    def profile_dir(self):
        return GAME / "BepInEx" / "mt-profiles" / self.profile

    def start(self, timeout=90):
        LOGS.mkdir(parents=True, exist_ok=True)
        if self.fresh and self.profile_dir.exists():
            for f in self.profile_dir.iterdir():
                if f.is_file():
                    f.unlink()
        x, y, w, h = self.geometry
        args = [str(GAME / "Moonlighter.exe"),
                "-screen-fullscreen", "0", "-screen-width", str(w), "-screen-height", str(h),
                "--mt-profile", self.profile, "--mt-bridge", str(self.port), "--mt-name", self.name,
                "--mt-window", f"{x},{y}", "--mt-skip-intro",
                "-logFile", str(self.player_log)] + self.extra
        self.proc = subprocess.Popen(args, cwd=str(GAME))
        self.connect(timeout)
        return self

    def connect(self, timeout=90):
        end = time.time() + timeout
        last = None
        while time.time() < end:
            if self.proc is not None and self.proc.poll() is not None:
                raise BridgeError(f"{self.name}: game exited with code {self.proc.returncode} before the bridge came up")
            try:
                self.sock = socket.create_connection(("127.0.0.1", self.port), timeout=2)
                self.sock.settimeout(None)
                self.rfile = self.sock.makefile("r", encoding="utf-8", newline="\n")
                self.call("ping")
                return
            except OSError as e:
                last = e
                time.sleep(0.5)
        raise BridgeError(f"{self.name}: no bridge on port {self.port} after {timeout}s ({last})")

    def stop(self):
        try:
            if self.sock:
                try:
                    self.call("quit", timeout=3)
                except Exception:
                    pass
                self.sock.close()
        finally:
            self.sock = None
            if self.proc:
                try:
                    self.proc.wait(timeout=8)
                except subprocess.TimeoutExpired:
                    self.proc.kill()
                self.proc = None

    def alive(self):
        return self.proc is not None and self.proc.poll() is None

    # ---- bridge --------------------------------------------------------------------------------
    def call(self, cmd, timeout=60, **args):
        if "id" in args or "cmd" in args:
            raise ValueError("bridge arguments may not be called 'id' or 'cmd'")
        self._id += 1
        body = {"id": self._id, "cmd": cmd}
        body.update(args)
        self.sock.settimeout(timeout)
        self.sock.sendall((json.dumps(body) + "\n").encode("utf-8"))
        while True:
            line = self.rfile.readline()
            if not line:
                raise BridgeError(f"{self.name}: bridge closed during {cmd}")
            reply = json.loads(line)
            if reply.get("id") != self._id:
                continue
            if not reply.get("ok"):
                raise BridgeError(f"{self.name}: {cmd} failed: {reply.get('error')}")
            return reply.get("result")

    def wait(self, what, predicate, timeout=60, every=0.25, cmd="state", **args):
        """Poll a command until predicate(result) is true; returns the result. Raises on timeout."""
        end = time.time() + timeout
        last = None
        while time.time() < end:
            last = self.call(cmd, **args)
            try:
                if predicate(last):
                    return last
            except (KeyError, TypeError, IndexError):
                pass
            time.sleep(every)
        raise TimeoutError(f"{self.name}: timed out after {timeout}s waiting for {what}; last = {json.dumps(last)[:600]}")

    def shot(self, label):
        SHOTS.mkdir(parents=True, exist_ok=True)
        path = SHOTS / f"{label}-{self.name}.png"
        self.call("screenshot", path=str(path))
        return path


def kill_all():
    """Stop every running test copy (only processes started from the test game folder)."""
    exe = str(GAME / "Moonlighter.exe").lower()
    try:
        out = subprocess.run(["powershell", "-NoProfile", "-Command",
                              "Get-Process Moonlighter -ErrorAction SilentlyContinue | "
                              "Where-Object { $_.Path -and $_.Path.ToLower() -eq '" + exe.replace("'", "''") + "' } | "
                              "ForEach-Object { Stop-Process -Id $_.Id -Force; $_.Id }"],
                             capture_output=True, text=True, timeout=30)
        killed = out.stdout.split()
        # Wait until they are really gone (ports and files free) before a new test starts copies.
        for _ in range(30):
            chk = subprocess.run(["powershell", "-NoProfile", "-Command",
                                  "(Get-Process Moonlighter -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.ToLower() -eq '" + exe.replace("'", "''") + "' }).Count"],
                                 capture_output=True, text=True, timeout=30)
            if chk.stdout.strip() in ("", "0"):
                break
            time.sleep(1)
        return killed
    except Exception as e:  # noqa
        return []


class Checks:
    """Collects named checks for one scenario and prints a table at the end."""

    def __init__(self, title):
        self.title = title
        self.rows = []
        self.t0 = time.time()

    def check(self, name, ok, detail=""):
        self.rows.append((name, bool(ok), str(detail)))
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}: {detail}", flush=True)
        return bool(ok)

    def note(self, text):
        print(f"  .. {text}", flush=True)

    @property
    def passed(self):
        return all(ok for _, ok, _ in self.rows) and len(self.rows) > 0

    def finish(self):
        n = sum(1 for _, ok, _ in self.rows if ok)
        print(f"== {self.title}: {n}/{len(self.rows)} checks passed in {time.time() - self.t0:.0f}s ==", flush=True)
        RUN.mkdir(parents=True, exist_ok=True)
        with open(RUN / "results.jsonl", "a", encoding="utf-8") as f:
            f.write(json.dumps({"scenario": self.title, "time": time.strftime("%Y-%m-%d %H:%M:%S"),
                                "passed": n, "total": len(self.rows),
                                "rows": [{"name": a, "ok": b, "detail": c} for a, b, c in self.rows]}) + "\n")
        return self.passed


if __name__ == "__main__":
    # python tests/mt.py PORT CMD [json-args]   -> one bridge call against an already running copy
    g = Game("cli", int(sys.argv[1]))
    g.connect(5)
    extra = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
    print(json.dumps(g.call(sys.argv[2], **extra), indent=1))
