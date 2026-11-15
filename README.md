# Moonlighter Together

Online co-op for **Moonlighter** (Steam, Windows, game version 1.15.8), for 2 to 4 players. Unofficial; not made
or endorsed by Digital Sun or 11 bit studios. Needs your own copy of the game; contains none of its files.

## What it does

- **Free roaming in dungeons.** Everyone is on the same floor, but each player walks through any door they like.
  Two players can fight in two different rooms at the same time; nobody is pulled along. The room you are in is run
  by your own game when you are the first one there; whoever comes in after you sees the same enemies.
- **The same floor for everyone.** Every copy builds the exact same floor (rooms, enemies, champions, chests and
  their contents) from a seed the host picks, so nothing has to be patched up afterwards.
- **Enemies go for any player.** Each enemy chases the nearest living player. What hits you is what you see.
- **Bosses together.** All four dungeon guardians are fought by the whole group; the kill counts for everyone.
- **Your own character.** You join the host's world (shop, village, story, day) with the character from one of
  your own save slots: your bag, equipment and gold. Loot is personal: every player gets their own drops, so there
  is no shared bag that can disagree. When you save in someone else's world, only your character goes back into
  your own slot; your own world is never overwritten.
- **Down, not out.** Falling while a teammate still stands makes you a ghost: you float along, can't hurt or be
  hurt, and enemies ignore you. A teammate revives you with **R** next to you (half health), or you come back on
  the next floor. Only when everyone is down does the run end, for everyone, the game's normal way.
- **The shop day together.** The host runs the shop; guests see the same shelves and customers, and can work the
  till, open and close the shop door, and sleep in the bed: those go through the host.
- **Reconnects.** If your connection drops, the game keeps your place for 5 minutes and you get back in by
  yourself, on the same floor, with what happened meanwhile already applied.
- **Menus never freeze the others.** Opening your bag, a chest or the pause menu only stops you.
- **Steam or IP.** Host through Steam (friends invite from the overlay, or a lobby code; works between homes with
  no ports to open) or on an IP address (same network, a VPN such as Radmin or Tailscale, or a forwarded UDP port).
- **Save backup.** Before the game first reads your save, a copy goes to `BepInEx\mt-backups` (last 10 kept).

## Install

1. Close the game. Open the game folder (Steam: right-click Moonlighter > Manage > Browse local files).
2. Unzip `MoonlighterTogether-<version>.zip` into that folder, so `winhttp.dll` and the `BepInEx` folder sit next
   to `Moonlighter.exe`. (The zip contains BepInEx 5.4.23.6 and the mod. There is no installer and no script.)
3. Start Moonlighter from Steam as usual. Every player needs the same mod version.

To uninstall: delete `winhttp.dll`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt` and the `BepInEx`
folder from the game folder (keep `BepInEx\mt-backups` somewhere if you want the save copies).

## Play

- Load your game, press **F9**.
- Host: **Host with Steam**, then **Invite Steam friends** (or give them the code). Or **Host on IP**.
- Join: accept the Steam invite (the game joins by itself), or paste the code / IP in the F9 menu. Pick which of
  your save slots your character comes from.
- **R**: revive a downed teammate next to you.

## Limits in this version (honest list)

- Only the host can put items on the shelves (a guest's showcase doesn't open). Shop income goes to the host.
- Joining while the host is inside a dungeon puts you in the village; you join the group on its next trip.
  (Coming back after a dropped connection does put you straight back on your floor.)
- Tested with two copies on one PC, including a simulated bad connection. A test between two homes / two Steam
  accounts has not been done yet.
- Between Dimensions (DLC) dungeon: tested together over three floors; its bosses are not tested yet.
- The Tech dungeon is tested to its second floor: a corrupted Oscillator mini-boss there could not be finished by
  the test bot (it regains health faster than the bot's hits remove it); the cause is not found yet.
- Game version 1.15.8 only (Steam build 16760112).

## For developers

- `src/MoonlighterTogether` - the BepInEx plugin (C#, net472). Build: `dotnet build -c Release`
  (`-p:GamePath=...` for a game folder with BepInEx in it; by default `testgame/`, a copy of the game).
- `tests/` - scripted tests that start two (or more) copies of the game and drive them through a local test port
  (`--mt-bridge`), with saves kept away from Steam (`--mt-profile`). See `docs/testing.md`.
- `docs/game/` - notes on the game's code; `docs/research.md` - other co-op work for Moonlighter.

## Credits and licences

- Moonlighter Together: MIT licence (see `LICENSE`).
- BepInEx (LGPL-2.1) and HarmonyX (MIT) are included unchanged; LiteNetLib (MIT) is used for UDP.
- Thanks to elde_al_lado ("Moonlighter Coop", MIT) for showing that co-op in this game is possible; one idea from
  it (moving the hero stand-in while an enemy thinks, so enemies aim at other players) is used here, credited in
  the code. No code was copied.
