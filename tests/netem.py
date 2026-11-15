"""A bad-internet relay for tests: the guest connects to this relay, which forwards UDP to the host with
added delay, jitter and packet loss in both directions. Every byte of the session crosses it.

    py netem.py LISTEN_PORT HOST_PORT DELAY_MS JITTER_MS LOSS_PERCENT
Runs until killed. Also importable: start_relay(...) returns a stop function.
"""
import heapq
import random
import socket
import sys
import threading
import time


def start_relay(listen_port, host_port, delay_ms=60, jitter_ms=20, loss=2.0, host="127.0.0.1"):
    """One-way delay is delay_ms +- jitter_ms (so the round trip is about twice that)."""
    front = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    front.bind(("127.0.0.1", listen_port))
    back = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    back.bind(("127.0.0.1", 0))
    for s in (front, back):
        s.settimeout(0.005)
    client = {"addr": None}
    queue = []   # (due, seq, sock, data, addr)
    lock = threading.Lock()
    stop = threading.Event()
    stats = {"forwarded": 0, "dropped": 0}
    seq = [0]

    def schedule(sock, data, addr):
        if random.random() * 100 < loss:
            stats["dropped"] += 1
            return
        due = time.time() + max(0.0, (delay_ms + random.uniform(-jitter_ms, jitter_ms)) / 1000.0)
        with lock:
            seq[0] += 1
            heapq.heappush(queue, (due, seq[0], sock, data, addr))

    def rx():
        while not stop.is_set():
            for sock in (front, back):
                try:
                    data, addr = sock.recvfrom(65535)
                except (socket.timeout, BlockingIOError):
                    continue
                except OSError:
                    continue
                if sock is front:
                    client["addr"] = addr
                    schedule(back, data, (host, host_port))
                elif client["addr"]:
                    schedule(front, data, client["addr"])

    def tx():
        while not stop.is_set():
            now = time.time()
            out = []
            with lock:
                while queue and queue[0][0] <= now:
                    out.append(heapq.heappop(queue))
            for _, _, sock, data, addr in out:
                try:
                    sock.sendto(data, addr)
                    stats["forwarded"] += 1
                except OSError:
                    pass
            time.sleep(0.001)

    threads = [threading.Thread(target=rx, daemon=True), threading.Thread(target=tx, daemon=True)]
    for t in threads:
        t.start()

    def close():
        stop.set()
        for t in threads:
            t.join(1)
        front.close(); back.close()
        return stats
    close.stats = stats
    return close


if __name__ == "__main__":
    a = sys.argv[1:]
    stopper = start_relay(int(a[0]), int(a[1]), float(a[2]), float(a[3]), float(a[4]))
    try:
        while True:
            time.sleep(5)
            print(stopper.stats, flush=True)
    except KeyboardInterrupt:
        print(stopper())
