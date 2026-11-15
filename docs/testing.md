# Testing

All tests start real copies of the game (from `testgame/`, a copy of the Steam install with BepInEx) and drive
them through the local test port. Each copy uses `--mt-profile NAME`: no Steam, saves in
`testgame\BepInEx\mt-profiles\NAME`, achievements kept in memory. Nothing reaches the player's Steam account,
except `test_steam_host.py`, which uses `--mt-steam-net` (Steam networking on; saves and achievements still offline).

Run from `tests\` with `py -u <file>`, or everything with `py -u run_all.py` (about 70 minutes). Results are appended to `run\results.jsonl`; screenshots go to `run\shots`.
Set `NETEM=delay,jitter,loss` (one-way milliseconds and percent, e.g. `NETEM=60,20,2`) to send all of the guest's
traffic through `netem.py`, a relay that adds delay, jitter and packet loss in both directions.

| Test | What it plays |
|---|---|
| `test_session.py` | connect; cut the guest's network for 12 s; guest gets back in as the same player |
| `test_join.py` | guest joins from its menu into the host's world with its own character; both Wills drawn; guest saves only its character |
| `test_dungeon_floor.py` | both enter a dungeon; identical floor; two players in two rooms at once, each owning theirs |
| `test_dungeon_combat.py` | shared room (mirrored enemies, enemies target and hurt the guest, guest hits counted, deaths on both); two rooms cleared at once; room owner leaves mid-fight and the guest takes over with enemy health kept |
| `test_death.py` | guest falls -> ghost; ghost can't be hurt; host revives it; both fall -> real death, results, home |
| `test_full_run.py CULTURE` | whole dungeon (3 floors) with floor exits taken by the guest, boss fought by both, home, dungeon credited |
| `test_shop_day.py` | host stocks and opens the shop; guest sees shelves and customers, works the till through the host; day ends for both |
| `test_reconnect_dungeon.py` | guest's network drops for 15 s in a dungeon while the host clears a room; guest comes back on the same floor and the room is empty for it |
| `test_late_join.py` | guest joins while the host is already in a dungeon and is sent to that floor |
| `test_dlc.py` | Between Dimensions: both through its portal, three identical DLC floors, a room fought together, floor exits together, home |
| `test_steam_host.py` | with this PC's Steam account: a friends-only lobby tagged with the mod version; menu screenshot |

What the bot does differently from a person: it moves room to room through the game's own door code (it does not
walk across the room), it hurts enemies through the game's own damage function (the path a sword hit takes, with
the enemy's short invulnerability after each hit skipped), and it presses the till, results screen and doors
through the same functions the buttons call.
