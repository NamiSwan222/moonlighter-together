# Moonlighter co-op: prior-art research

Researched 2026-11-14/15. Read-only; no binaries downloaded. "Unverified" means I could not open the primary source.
Note: web search/fetch quota ran out partway through Part B, so several Part B entries are verified only from their GitHub README/metadata (via `gh`), not from Nexus/Thunderstore pages.

## searched:

| Venue / query | Result |
|---|---|
| Nexus Mods GraphQL API, `gameDomainName=moonlighter`, all 46 mods | Only one co-op mod: #57 "Moonlighter Coop - Online multiplayer (2-5 players)". No other title or summary mentions players, coop, multiplayer, online, remote play or split. Full list is below in Part A. |
| Nexus #57 page, Posts and Bugs tabs (browser) | 0 comments, 0 bug reports. Page shows v0.11.0 (09 Nov 2026), "Some suspicious files" virus-scan flag, "AI Assisted" tag. Uploaded by `EldeAllado1`, credited to `elde_al_lado`. |
| Nexus #26 (BepInEx pack) Posts tab | 16 comments. BepInEx quirks are in Part C. |
| Thunderstore `c/moonlighter` | 4 packages: r2modman, BepInExPack 5.4.2305, FeluciaPS/MoonlighterAPMod 0.1.6, FeluciaPS/MoonlighterItemSpriteFix. No co-op. |
| ModDB `games/moonlighter/mods` | Page is empty ("no media added"). It only shows generic roguelike recommendations. |
| Steam discussions for 606150: "coop" (27), "multiplayer" (22), "multiplayer mod" (1), "remote play" (5), "bepinex" (1), "mods" (50) | Only feature requests from 2017-2022, plus the developer's statements that the game is "single player only" and "designed with single player in mind" (Digital Sun, 20 Jun 2018). No co-op mod and no Remote Play Together or Parsec thread. Gurrenm3's 2021 thread "Play Moonlighter with MODS!" links the modding Discord. |
| Steam guides for 606150 | Tag counts: Co-op 2, Multiplayer 1, Modding or Configuration 7. The tag filter did not render through fetch, so I could not read those guides (unverified). The top 10 guides are all price lists or achievement guides. |
| GitHub repo search: moonlighter + coop / multiplayer / online / mod / bepinex / harmony / netcode / LiteNetLib / steamworks / archipelago / plugin / multijugador / cooperativo / 夜勤人 | Co-op: only Eldeallado/moonlighter-coop. New non-co-op finds: yottya224-maker/moonlighter-qol-bugfixes, jellli/moonlighter-dont-drop-everything, DinhQuocCuong28664/MoonlighterVN, GLITCHKISS/Moonlighter-Recomp, Crow-on-Crowbar/CrowNest-Adapters (a Moonlighter adapter, no README), p1xel8ted/Game-Mods. |
| GitHub code search: `HeroMerchant` with BepInPlugin / HarmonyPatch / LiteNetLib / SteamNetworking / multiplayer / coop / Photon / Mirror | No networking code that references Moonlighter classes, other than Eldeallado's repo. |
| Web (EN): "Moonlighter co-op mod multiplayer BepInEx", "Eldeallado/elde_al_lado/moonlighter-coop", Remote Play/Parsec/Nucleus | Only SEO pages (xmodhub, playco-opgame, gamebrief) that claim no mod exists. Nothing mentions the Eldeallado mod. |
| Web (ZH) 夜勤人 联机 mod / 联机补丁 3DM/游侠/小黑盒 | Nothing relevant. |
| Bilibili search 夜勤人 联机 mod | Only a walkthrough video and Moonlighter 2 trainer and bug videos. No co-op. |
| Web (JA) ムーンライター マルチプレイ MOD 協力 | Nothing relevant. |
| Web (KO) 문라이터 멀티 모드 코옵 | Only the official FAQ statement that the game is single-player. GitHub has many Korean "문라이터 모작" (clone/remake) student projects, which are not mods. |
| Web (RU) Moonlighter кооператив мод | Nothing relevant. Generic Parsec/Sunshine advice only. |
| Web (ES, PT) | Nothing relevant. |
| Reddit r/Moonlighter | Blocked: reddit.com cannot be fetched, searched or opened by any tool here. Not searched (unverified). |
| YouTube search (browser) | Results did not render (empty DOM). Unverified. |
| Tieba | Not reached; the quota ran out. Unverified. |
| archive.org advanced search "moonlighter AND (coop OR multiplayer OR mod)" | 1 irrelevant hit. |
| Modding Discord invite `discord.gg/8H3qwHEUvz` | Invite page says "Invite Invalid" (expired). The server name was not obtainable; gurrenm3's posts call it the "Moonlighter Modding Discord". |
| moonlighterthegame.com FAQ | DNS failure (domain gone). |
| PCGamingWiki Moonlighter | HTTP 403. |

## Part A: Moonlighter 1 co-op attempts

**Conclusion: only one co-op or multiplayer attempt exists, Eldeallado's.** Nothing else turned up on Nexus (all 46 mods checked), Thunderstore, ModDB, Steam, GitHub repo and code search, or the CN/JA/KO/RU/ES/PT web. Reddit, YouTube and Tieba could not be checked.

### Moonlighter Coop (the only one)
- Links: https://github.com/Eldeallado/moonlighter-coop and https://www.nexusmods.com/moonlighter/mods/57
- Author: `elde_al_lado` (GitHub `Eldeallado`, Nexus uploader `EldeAllado1`).
- License: MIT.
- Activity: the repo was created 2026-11-08 and last pushed 2026-11-12. It has 22 releases, from v0.3.0 to v0.13.5 (2026-11-12). Nexus still shows v0.11.0.
- Repo state: 0 stars, 0 forks, 0 issues, main branch only, wiki enabled but unused.
- What it claims to sync (README, in Spanish):
  - Hosting: host-authoritative, 2-5 players, over a Steam invite, a 7-letter "PU" code, or direct IP.
  - Dungeons: identical dungeon from the same seed; Isaac-style rooms where one player crossing a door moves everyone; shared loot, backpack and gold; synced breakables, projectiles, bosses and enemies, with enemies targeting the nearest player.
  - Downed players and run end: downed players become ghosts and are revived at a fountain or on the next floor; a wipe triggers a vote.
  - Town and shop: shared town and shop, with entering or leaving the shop moving everyone; shelves, customers and thieves are host-simulated.
  - Other: synced day/night, mod-list mismatch warning, reconnect, scaling difficulty.
  - Since v0.12: a cross-play Android build.
- Its own notes (NOTES.md):
  - Dungeon generation: `DungeonGenerator.GenerateDungeon` uses the global `UnityEngine.Random`. It shares the dungeon through `DungeonSaveInfo(DungeonManager)` and `DungeonGenerator.GenerateSavedDungeon(info)`, serialized with JsonUtility.
  - Steam: Steamworks.NET lives in Assembly-CSharp-firstpass. A `steam_appid.txt` file is needed for multi-instance testing.
  - Saves: saves go to Steam Cloud through `SteamCloudSaveController`.
  - Anti-cheat: CodeStage ObscuredTypes is present but harmless.
- Known limitations it lists:
  - Guest copies of customers and thieves lag behind the host's.
  - Spawned children such as splitting slimes are synced by position only and can drift.
  - Boss fights are "little tested".
  - The Between Dimensions DLC is untested in co-op.
  - Stack and inventory mods must match on every machine, or items are lost.
- Real user reports: none found anywhere. Nexus Posts and Bugs are both 0, there are no GitHub issues, and Steam has nothing. Reddit and YouTube were not checkable.
- Red flags: the Nexus virus scan flags "Some suspicious files", probably because `Instalar.bat` installs BepInEx. The page is tagged "AI Assisted". The whole project was built in 5 days.

### Other Moonlighter mods (all 46 on Nexus; none multiplayer)
- Nexus mods:
  - #57 Coop (above).
  - #55 More Inventory (DeveloperBlue).
  - #54 Moonlighter Skip.
  - #53 Arabic localisation.
  - #51 Reshade.
  - #49 Luck Ring Def fix.
  - #47 Auto Price, #48 Notebook Prices, #45 Item Magnet and #44 Auto Attack (SirawitPra).
  - #46 AreYouOkey.
  - #43 Quick Price.
  - #42 and #40 Thai localisation.
  - #39 Mouse Control and other Tweaks.
  - #38 PS4 Buttons.
  - #36 Harder Modes.
  - #34 Enable The Other Debug Mode.
  - #33 Stack 999.
  - #30 Debug Mode.
  - #26 BepInEx Pack, #27 StackIt, #28 TrackIt and #29 Ultra-Wide Fix (p1xel8ted).
  - #18, #9-17 and #19-24: Aidanamite's mods.
  - #25 Free Pendant.
  - #8 Take No Damage.
  - #2, #4, #5 and #6 (gurrenm3).
- Thunderstore: MoonlighterAPMod is an Archipelago multiworld randomizer. "Multiworld" here is item exchange between separate solo games, not shared play.
- Remote Play Together and Parsec: Steam does not list Moonlighter for Remote Play Together. The game has no local co-op, so streaming would give a spectator, not a second player. I found no documented workaround.

## Part B: co-op mods for other single-player Unity games

| Mod | Link | Author | License | Last activity | Transport | Authority | Notes |
|---|---|---|---|---|---|---|---|
| HKMP | github.com/Extremelyd1/HKMP | Extremelyd1 | LGPL-2.1 | 2025-10-26 (SSMP sibling: 2026-10-31) | Custom UDP, IP + port-forward or Hamachi; separate server in the client | Server relays. Entities are owned by the scene "host" (the first player in the scene); I read this from the code layout and prior knowledge, it is not fully verified | Bugs go to Discord (discord.gg/KbgxvDyzHP) and GitHub issues. Has an addon API. |
| SULFUR Together | github.com/ryuka-dev/SULFUR-Together and Thunderstore ryuka_labs | ryuka-dev | MIT | 2026-11-11 (left beta at 1.5.0) | LiteNetLib UDP; Steam P2P as a loopback relay underneath | Host-authoritative | Bugs go to the Nexus bug tab and GitHub. Ships architecture docs (`Docs/NetworkingArchitecture.md`, `BossAuthority.md`). |
| Thronefall Multiplayer | github.com/Bemno/Thronefall_Multiplayer (original: MunWolf/thronefall_multiplayer by BadWolf) | Bemno | none stated | 2026-11-12 | Steam lobbies | Host | Detailed changelog with progression and desync fixes. Has host migration. |
| ErenshorCoop | github.com/MizukiBelhi/ErenshorCoop | MizukiBelhi | none stated | 2026-11-10 | Steam lobby | Host syncs enemies, NPCs and sims | Known bugs: summons must be re-cast after joining; about 100 KB/s upload per player. |
| Children of Morta 4P | github.com/williamanjo/Children_of_Morta4P | williamanjo | none stated | 2026-10-18 (v0.9.12, experimental) | The game's own Steam online co-op, extended from 2 to 4 players | Host | The game already has native 2P online; this is a "raise the player cap" mod. |
| Graveyard Keeper 2 co-op | github.com/Fredwuz/graveyard-keeper-2-coop (fork; GK1 co-op not found) | Fredwuz | MIT | 2026-11-01 | Steam, or direct UDP 8889 | Host world; zombies and conveyors are host-run | AI-written code (its README says so). |
| COTLMP (Cult of the Lamb) | github.com/GeoB99/COTLMP | GeoB99 | MIT | 2026-11-11 | not read | not read | WIP. |
| Gungeon online | github.com/kai-bat/GungeonMultiplayer, and Thunderstore GoTeamAlex/Gungeon_Online | kai-bat / GoTeamAlex | none | 2019-02 (kai-bat) | unverified | unverified | The Thunderstore page could not be read (quota). |
| Darkwood co-op | nexusmods darkwood/mods/36 and github.com/felipescoutfx/darkwood-coop-launcher | felipescoutfx (launcher) | none | 2026-07-26 | unverified | unverified | Launcher installs BepInEx and the mod and downloads a starting save. |
| Nine Sols | github.com/asd9176506911298/NineSols-Multiplayer and linwys/multiplayer | various | MIT | 2026-09 | unverified | unverified | |
| Multipeglin | github.com/sigseg1v/peglin-multiplayer | sigseg1v | MIT | 2026-10-12 | unverified | unverified | |
| AtomicropsOnline (romaskun) | not found on GitHub | romaskun | ? | ? | ? | ? | Unverified: the quota ran out before I could check Thunderstore. |

Death's Door, Tunic and Dead Cells co-op mods: not found. Dead Cells is not Unity in any case.

### Transferable lessons (for a host-authoritative top-down dungeon crawler)
1. **Host owns all simulation; clients render proxies.** SULFUR Together syncs enemy state by mirroring it, gives the host authority over bosses (with a dedicated doc on it), and runs "arena lockdown" on the host. Eldeallado's mod does the same and admits that child-spawned enemies drift.
2. **Make each spawn an authoritative event.** Do not reproduce spawns by replaying the RNG. Moonlighter's generator uses the global `UnityEngine.Random`, so any other call to it breaks determinism. It is safer to send the generated result (`DungeonSaveInfo` and `GenerateSavedDungeon`) than to send a seed.
3. **Ownership per room or scene (HKMP model).** When players can be in different rooms, give each room an owner that simulates it. Until host-in-every-room is solved, freeze or simplify rooms with no owner. Eldeallado sidestepped this with "everyone moves together".
4. **Write a transport layer once.** SULFUR runs LiteNetLib over direct IP and also as a loopback relay over Steam P2P. Its verified pitfalls were:
   - Steam's first unreliable send never opens a session, so the first send must be reliable.
   - The per-peer bridge entry must be cleared on `SessionFailed`, or retries from that peer are ignored forever.
   - The inbound-session handler must be subscribed permanently.
   - There is no Steam Datagram Relay fallback for a third-party AppID, so symmetric NATs can still fail.
5. **Progression must have one writer.** Thronefall found that per-player copies of progress drift. The fix was for the host to send its copy after every save, swapping copies only in level select. It also keeps saves per player group, makes a one-time `.mp-backup` of the original save, and never writes the guest's own save while online.
6. **Joiner plays a copy of the host's world.** Graveyard Keeper 2 and Children of Morta copy the host save to the guest, so the guest's own saves stay untouched. Moonlighter's Steam Cloud saves make this even more important.
7. **Clean up the lifecycle edges:** end screens closing for everyone, restarts, host leaving (host migration), and late join. Thronefall's 1.3.x changelog is mostly bugs of this kind.
8. **Ask for logs from both sides.** Pin the mod version to the game build and refuse version mismatches. Require host and client logs plus "who hosted, who triggered it, what each saw" in every bug report (SULFUR's template). Budget bandwidth per player (Erenshor needs about 100 KB/s each).

## Part C: Moonlighter modding resources

### BepInEx and tooling
- Thunderstore BepInExPack 5.4.2305, which wraps BepInEx 5.4.23.5 x64. It is generic, with no Moonlighter-specific doorstop config.
- Nexus #26 "Moonlighter BepInEx Pack" (p1xel8ted) is 5.4.23.2 (2024-08-10). Linux, Deck and Proton need `WINEDLLOVERRIDES="winhttp=n,b" %command%`.
- Eldeallado verified that 5.4.23.6 x64 works on 1.15.8.
- **Crash or "doesn't load" history:** around March 2024 (the DLC and hotfix era), the game briefly shipped as **IL2CPP**. No Mono mods worked then and a different BepInEx was needed. In April 2024 the devs switched back to **Mono** ("hotfix for allowing mods"). p1xel8ted then removed the IL2CPP packs and advised: "Use the mono versions... Remove the old BepInEx entirely."
- Other reports from that thread:
  - BepInEx must not be extracted into a subfolder.
  - One user saw vertical-line artifacts with BepInEx installed.
  - One user lost controller input.
  - One user asked whether achievements still unlock with mods (unconfirmed).
- Modding Discord: "Moonlighter Modding Discord" (gurrenm3, 2021), `discord.gg/8H3qwHEUvz`. The invite is now **invalid or expired**, and I found no current invite. There is no wiki; the docs that exist are gurrenm3's Nexus forum tutorial and the AssetsLib wiki (github.com/Aidanamite/AssetsLib/wiki).
- GLITCHKISS/Moonlighter-Recomp is a native C++ reimplementation targeting 1.15.8 and Unity 2019.2.7f2, with a GameCube fork. Its README says 7.4% is ported. It has `oracle/MoonlighterOracle`, a BepInEx recorder plugin with RngProbe, RoomOracle, EnemyOracle and PolyNavOracle. That makes it a good reference for dungeon-gen and RNG determinism. No license is stated.

### Game classes and methods patched by existing mods
Sources: Aidanamite (16 Moonlighter repos, no license, last pushed 2024-01-30), FeluciaPS/MoonlighterAPMod (no license, 2026-10-21), gurrenm3 (GPL-3.0), p1xel8ted/Game-Mods (GPL-3.0), and yottya224-maker (MIT).

- **Player:**
  - `HeroMerchant` is a singleton (`HeroMerchant.Instance`). It has `heroMerchantInventory`, `heroMerchantStats`, `CompleteQuest`, `DealDamageToPlayer`, `OnWillDeathAnimationEnded` and `Update`.
  - `HeroMerchantController` has `Init`, `UseAbility`, `SecondaryAttackUseStart`, `AddSpeedEffectModifier(float,float)`, `SetArmorAnimatorInteger` and the field `itemAttrackRadius`.
  - `HeroMerchantStats` has `Die`, `RecoverAllHealth` and `CurrentHealth`.
  - `HeroStatistics` has `RegisterItemSell`, `RegisterHelperSell` and `RegisterSalesBoxSell`.
- **Enemies:**
  - `Enemy` has `DealDamageToEnemy`, `CalcHitDamage` and `DropItemsOnDeath`.
  - `EnemyStats.CurrentHealth` (setter).
  - Spawn and room events: `DungeonRoom.SpawnEnemy(DungeonRoomInfo.EnemySpawn,int,bool)` and `DungeonRoom.EnemyDefeated`.
  - Other combat classes: `AreaEffect.Spawn` / `ApplyDamageToPlayer`, `WeaponCollisionDetection.Start`, and familiars (`FamiliarsManager.SpawnEquippedFamiliar`, `FamiliarBase`, `BabySlimeFamiliarBehaviour`).
  - Other enemy classes: `Wanderer.Init` and `WandererDLCController`.
- **Dungeon:**
  - `DungeonGenerator` has `IsGeneratingDungeon`, `GenerateDungeon` and `GenerateSavedDungeon`.
  - `DungeonManager.Instance` has `.culture` and `.level`.
  - `DungeonRoom` has `EnterRoom` and `OpenAllDoors`.
  - `DungeonBossRoom` has `IsBossDefeated` and `InternalOnBossDeath`.
  - Entry and doors: `WorldDungeonDoor` (`LoadDungeon`, `UpdateByCulturesDefeated`) and `TravelToDungeonZone.GoToDungeonEntrance`.
  - Loot tables: `ChestGeneration.RefreshWeightLists` and `ItemDrop.DropItem`.
  - Loading panels: `DungeonLoadingPanel` and `DungeonResultsPanel` (`EndResult`, `SetBagResult`).
- **Items and inventory:**
  - `ItemDatabase` has `ReadDefaultFile`, `GetItems`, `GetSprite`, `LoadDLCItems` and `GetPrefab`.
  - Item classes: `ItemMaster`, `ItemStack` (`Init`, `Drop`, `OnAddedToHeroInventory`, `SendAddedToHeroInventoryEvent`), `Weapon`, `TrickWeapon.Init`, `Amulet`, `HPPotionI` and `Chest` (`CloseChest`, `SetChestFromSave`).
  - `HeroMerchantInventory` has `TryAddItem(ItemStack,int)`, `SetItem`, `SetEquippedItemByType` and `Init`.
  - Inventory UI: `InventoryPanel` and `InventorySlotGUI`.
  - Recipes and wishlist: `RecipeManager` and `WishlistManager`.
- **Shop:**
  - `ShopManager.Instance` has `RemoveItems`, `LoadShopFromSave`, `GameSlot_OnSaveToDungeon` and `GetChestItemCount`.
  - Visitors: `CommonVisitorMind.IsShowcaseInteresting` and `QuestVisitorMind`.
  - Shop UI: `ShowcaseInventoryPanel.MoveToShowcase`, `NotebookPanel.DetailItem`, `BlacksmithPanel`, `WitchPotionsPanel`, `HawkerPanel`, `DecorativePanel`, `TownBoardShopPanel` and `TownBoardStaffPanel`.
- **Game flow and saves:**
  - `GameManager.Instance` has `LoadSlot`, `SerializeGameSlot`, `LoadGameFromBytes`, `currentGameSlot`, `IsTownSceneLoaded`, `LoadTownSceneFromDungeon`, `LoadDungeonSceneFromEmblemPortal`, `DestroyAllDestroyableItems`, `PauseGame` and `UnpauseGame`.
  - `GameSlot` (constructor) and `GameSlotGUI`.
  - `GUIManager.Instance` has `FadeOut`, `FadeBoth` and `GoToDungeonLoadingPanel`.
  - Menus: `MainMenuPanel`, `StartGamePanel` and `PausePanel`.
  - Data and localization: `Constants` (`GetInt`, `GetFloat`, `ReadDefaultFile`) and `I2.Loc.LocalizationManager.GetTranslation`.
  - Cutscenes: `TutorialScene2`, `TutorialScene3` and `TownCinematics`.
  - Scenes: Managers (persistent), plus MainMenu, Town and Dungeon, which are loaded additively.
