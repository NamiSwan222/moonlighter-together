# Moonlighter 1.15.8: dungeon structure (decomp notes for the co-op mod)

Source: `decomp/Assembly-CSharp` (ILSpy). All paths below are relative to that folder. `File.cs:N` = line in the decompiled file.
"(inferred)" marks conclusions drawn from reading, not stated directly by code.

There are **two generators**:

| Class | File | Used for |
|---|---|---|
| `DungeonGenerator` (global namespace) | `DungeonGenerator.cs` | Base game: Golem/Forest/Desert/Tech/Unknown dungeons, emblem (saved) dungeons, tutorial prefab dungeons |
| `Moonlighter.DungeonGeneration.DungeonGenerator` | `Moonlighter.DungeonGeneration/DungeonGenerator.cs` | DLC Wanderer dungeon and DLC "intermediate" dungeons, driven by `DungeonGenerationData` ScriptableObjects |

Both expose a `public static event Action OnGeneratedDungeon` (`DungeonGenerator.cs:219`, `Moonlighter.DungeonGeneration/DungeonGenerator.cs:64`). Hook **both**. `ArmorAmuletEffect.cs:14-15` subscribes to both, which confirms this.

Useful constants (read from `testgame/Moonlighter_Data/resources.assets`, the `constants` JSON that `Constants.ReadFile` loads, `Constants.cs:41`): `roomsInFloor1/2/3` = 10/12/14, `roomTileCountX/Y` = 20/11, `tileSize` = 24, `wandererTimeToAppear` = 360, `wandererFeedbackTimeToAppear` = 15, `merchantPendantNeededMoney` = [200,200,800,3200,12800,12800,0,12800], `merchantEmblemNeededMoney` = [1000,1000,4000,12000,40000,40000], `pendantIncrementPerLevel` = `emblemIncrementPerLevel` = 0.5, `HealingPoolCapacity` = [0,100,250,450,800,1000,1000,1500], `ChestsIDs` = ["WoodChest","IronChest","GoldChest","MagicChest"], `mimicChestProbabilities` = [0.07,0.07,0.07,0], `minChestsFloorN/maxChestsFloorN` = 2-3 / 3-4 / 4-5, `min/maxTimedChestsPerFloor` = 0/1, `CrazyPeteRoomProbability` = 1.0, `championProb` = 0.7.

---

## 1. DungeonGenerator (base game)

### Entry points (static)
- `public static IEnumerator GenerateDungeon(ItemMaster.Culture culture, int level = 0, bool test = false)` (`DungeonGenerator.cs:240`)
  1. Updates `GameSlot.maxDungeonLevelVisited[(int)culture]` (`:250`); sets `spawnSpecialUnlockAtInit` for Golem floor 2/3 the first time (`:246-249`).
  2. `ChestGeneration.RefreshWeightLists()`, `AmuletUtility.Instance.MarkToResetAmuletLists()` (`:256-257`).
  3. Loads asset bundle `dungeon_common` plus the culture bundle (`dungeon_golem`/`dungeon_forest`/`dungeon_desert`/`dungeon_tesla`/`dungeon_unknown`), **yielding** on each (`:258-310`). Loads prefab `DungeonGeneration/<X>DungeonGenerator-<level>` (`:270-302`). Each floor is a separate generator prefab with its own serialized fields (`bossRoom`, door prefabs, `wanderer`, and so on, `:103-163`).
  4. `roomDatabase.LoadFromJSON()` (static `DungeonRoomDatabase roomDatabase`, `:101`, `:311-315`). This reapplies the cached JSON text (`DungeonRoomDatabase.cs:81-88`), so weights mutated by the last floor are reset.
  5. `Instantiate(prefab)` then `component.GenerateDungeon()` (private, `:329`). That calls `GeneratePrefabDungeonCoroutine` if `dungeonPrefab` is set (fixed layouts: Unknown/tutorial). Otherwise it calls `Init()` and then `StartCoroutine(GenerateDungeonCoroutine())` (`:331-337`).
- `public static IEnumerator GenerateSavedDungeon(DungeonSaveInfo info)` (`:340`), which leads to `Init(info)` and then `GenerateSavedDungeonCoroutine` (`:409-413`, `:1041`).
- `public static DungeonGenerator GenerateDungeonPrefab(GameObject prefab)` (`:232`) for tutorial and prefab dungeons.
- Caller: `DungeonLoadingPanel.Enable(bool toDungeon, ItemMaster.Culture culture, int level, bool isTest)` → `WaitUntilGenerationCanStart` (destroys the old `DungeonManager`, waits 1s, `UnloadUnusedAssets`, waits 1s, waits for the scene) → `StartCoroutine(DungeonGenerator.GenerateDungeon(...))` + `OnGeneratedDungeon += Generator_OnGeneratedDungeon` (`DungeonLoadingPanel.cs:91-149`). The saved variant is at `:151-185`.
- Other events: `public static event Func<DungeonGenerator, IEnumerator> PostprocessDungeonEvent` (`:221`, each handler is yielded in turn at `:764-772`), and `public static event GenerateSoundsDelegate GenerateSoundsEvent` (`:223`).
- `public bool IsGeneratingDungeon { get; private set; }` (`:217`). `GameManager.LoadDungeonSceneCoroutine` polls it (`GameManager.cs:1891`).

### `GenerateDungeonCoroutine()` step by step (`DungeonGenerator.cs:438-799`)
1. `yield FreeResources()` (`UnloadUnusedAssets`, GC, `roomGenerator.Clear()`, `:864-872`). Destroys any existing `DungeonManager` (`:441-444`).
2. If `level == 0`: `floorGenerationSaveInfo = new FloorGenerationSaveInfo()` (static, `:99`) then `ResetValues()` (`:445-452`). This **rolls the whole run's special-room floors** with `UnityEngine.Random` (`:37-96`): `crazyPeteRoomFloor`, `corruptedRoomFloor` (0-2), `specialDungeonRoom`, `desertHardEnemiesRoom`, and two of {dungeonToHome, waves, abandonedItems, wandererNest} floors. Floors 2 and 3 reuse the static object.
3. Room lists: `roomDatabase.GetRoomsByCulture(culture, level)` (excludes timed and chest rooms, `DungeonRoomDatabase.cs:137`), `holesOnlyRooms`, `GetTimedChestRoomsByCulture`, `GetChestRoomsByCulture`, `GetCorruptedRooms(culture)` (`:455-460`).
4. Creates GO `"Dungeon"` in scene "Dungeon", `AddComponent<DungeonManager>()`, `loadWanderer = true`, `SetDungeonAsNormal(culture, level)`, `Init()`, sets `wandererPrefab`, `guidancePrefab`, breakable drop probabilities from Constants (`:463-475`).
5. `lastDoorDirectionIndex`: 1 on floor 0, otherwise the complement of the previous floor's exit (`:477-484`). Room count = `roomsInFloor1/2/3` (`:485`).
6. **Cave layout (pure logic, synchronous)**: `cave = new CaveGenerator(); cave.Init()`; 5 `CommonRoomMiner`s; initial `SingleCaveRoom` with one blocked side; `while (cave.rooms.Count < num && areMinersAlive && 100 iterations) cave.IterateMinners()` (`:486-520`). Miners use `Random.Range` (`CommonRoomMiner.cs:28,124,127,146`). They are pure C# and do not touch the scene.
7. End room: on `level == 2`, `BossRoomMiner` + `GenerateBossRoomCaveRoom()` (random pick at `BossRoomMiner.cs:17`). Otherwise `GenerateNextLevelCaveRoom()` (deterministic scan, `:1365`) (`:521-529`).
8. Special cave-room assignment (`:530-560`). Deterministic choices from connection counts, except where noted:
   - `onsenCaveRoom = GenerateCaveOnsen()`: the room at `bossCaveRoom.pathFromInit[Count/2]`. The healing pool sits halfway along the critical path (`:1424-1435`).
   - home chest (most connections), waves/abandoned/wandererNest (fewest connections), each only on its rolled floor.
   - `timedChestCaveRooms`: count = `Random.Range(minTimedChestsPerFloor, maxTimedChestsPerFloor)` (`:1592`).
   - `crazyPeteCaveRoom = GenerateCrazyPeteCaveRoom()`: **always** added, the fewest-connection unused room (`:551`, `:1669`). `crazyPeteRoomFloor` is never read here.
   - `corruptedCaveRoom` on `corruptedRoomFloor` (farthest from the other special rooms, `:1707`).
   - `chestCaveRooms = GeneratChestRooms()`: count `Random.Range(min, max+1)`, takes the most-connected rooms (`:1728-1768`).
9. `yield` bundle loads (`EnemiesPrefabRegister`/`ObstaclePrefabRegister`/`DetailsPrefabRegister`, `:561-569`).
10. **Per cave room loop (`:571-747`), one room per frame**:
    - desired doors = existing connections (boss/end room gets all four, `:578-593`).
    - room info: init → `roomDatabase.initRoomInfo`; onsen → `healingRoomInfo`; crazy Pete → `campRoomInfo`; corrupted → `corruptedRooms.GetRandom()` (then **yields** for more bundle loads, `:611-618`); secret-room hosts → `GetRandomRoom(holesOnlyRooms, …)`; timed/chest rooms → those lists; otherwise `GetRandomRoom(rooms, desired, GetContiguousRooms(x,y))`. End room → `GetMinibossRoom(culture, level)` (floors 0/1 have a fixed miniboss room name per culture; DLC can override through `IDLCControllerOverrideRoom`, `:801-862`).
    - `GetRandomRoom` = weighted pick with `Random.Range(0f, total)`. It **mutates the shared `DungeonRoomInfo.weight *= 0.001f`** (`:1923-1964`).
    - `AddSingleDungeonRoom(...)` (`:1823-1856`): `Instantiate(roomDatabase.dungeonRoomPrefab)`, position `= size * (x,y)`, `InitializeRoom(roomInfo)`, door animators, `DisableAllDoors()`, set `x,y`, `dungeonManager.dungeonRooms.Add`, **`RoomPlaced(level, 0)`** (see §3; this spawns enemies, chests, obstacles and details, all using RNG).
    - `DungeonManager.Instance.currentDungeonRoom = instanceRoom` (temporary, `:652`).
    - `ConnectRoomsByDirection` to already-built neighbours (`:653-668`) → `DungeonRoom.Connect{North,South,East,West}Room` (sets both doors' `destinationRoom`, enables doors, **repositions the room**, sets `playerEnterDestinyRoomPoint`, `DungeonRoom.cs:1251-1377`).
    - init room: `isInit = true`, `AddSpecialInitDoor(firstLevelDoor|lastLevelDoor, lastDoorDirectionIndex)` (`:674-679`).
    - end room: `isEnd = true`, `AddNextLevelDoor(...)`. The exit side is chosen with `list.GetRandom()` and stored in `floorGenerationSaveInfo.lastDoorDirectionIndex` (`:684-688`, `:1290-1315`).
    - secret rooms (waves/home/nest/abandoned): a **second** `DungeonRoom` is instantiated at the **same x,y** (same world position) and linked with `AddSecretRoom(host, secret, secretRoomAdvisorPrefab)`. That sets `host.secretRoom`, `secret.isSecret`, instantiates a `DungeonRoomSecretRope` in the secret room and a `DungeonRoomSecretAdvisor` on a random hole tile of the host (`:689-710`, `:1317-1339`).
    - `BossCaveRoom` → `AddBossRoom(contiguous, bossRoom, …)` (`:712-745`, `:1858-1887`, see §4).
    - `yield return new WaitForEndOfFrame()` (`:746`).
11. After the loop: `SpawnSpecialRoomsAfterGeneration()` (Desert hard-enemies Shrine, random tile, `:874-940`). Then `yield GenerateSounds(...)` and the ambient sound. Then `initialDungeonRoom = currentDungeonRoom = initialInstanceRoom`, PolyNav map for the init room, `PlaceCorruptedRoomDoors`, `UpdateMinimapOnGeneratedDungeon(_roomsGenerated)`, `UpdateChestCount`, `SpawnSpecialUnlockAtInit` if flagged, then **yield each `PostprocessDungeonEvent` handler** (the DLC adds the interdimensional portal room here, §8). Then `yield FreeResources()`, `IsGeneratingDungeon = false`, **`OnGeneratedDungeon()`**, analytics, `Destroy(gameObject)` (`:748-799`).

### RNG used during generation (all `UnityEngine.Random`, global state)
- `FloorGenerationSaveInfo.ResetValues` (`:39-60`), miners (`CommonRoomMiner.cs`, `BossRoomMiner.cs:17`), `GetRandomRoom` (`:1943`, `:1994`), `corruptedRooms.GetRandom()` (`:613`), `AddNextLevelDoor` (`:1309`), timed/normal chest counts (`:1592`, `:1749`), `AddSecretRoom` hole tile (`:1337`), hard-enemies shrine tile (`:925`), `Extensions.GetRandom` = `Random.Range` (`Extensions.cs:308-324`).
- Per room inside `RoomPlaced`/`InitializeRoom`:
  - `LoadGridFromInfo`: **`currentSpawnPack = Random.Range(0, enemiesSpawnPacks.Count)`** (this is the enemy pack) and the `None_Holes`/`Holes_Rocks` tile coin flips (`DungeonRoom.cs:276-280`).
  - `SpawnEnemies`: champion roll and champion index (`:794-802`).
  - `SpawnChests` → `GetChestNameByFloor`: chest tier and mimic roll (`:1803`, `:1814`).
  - `SpawnRoomPrefabs`: skeleton breakable (`:2058-2064`). It uses `FindObjectsOfType<BreakableObjectBehaviour>()`, i.e. **any room's breakables**.
  - `SpawnDetails` (wall/lower/middle details, particles, breakables, creatures, `:536-634`, many `Random` calls), texture fill per pixel (`:717`, `:751`).
  - `DungeonRoomGenerator.AddLightShadow`/hole sprites (`DungeonRoomGenerator.cs:440-643`).
- **Chest contents**: `Chest.Awake` runs on `Instantiate` (the chest is instantiated unparented and active, `DungeonRoom.cs:1488`). It calls `GenerateFixedDrop()` (`Random.value`, `Chest.cs:262-280`) and, if the prefab has `generateRandomContentOnInit`, `ChestGeneration.GenerateChestContent` (weighted lists, `ItemEffectManager.GetRandomEffect` for curses, `Random.Range` stack sizes, shuffle; `Chest.cs:192-195`, `ChestGeneration.cs:100-168`). That room chests use this flag is (inferred) from the prefab. No other code fills dungeon room chests. **Contents are decided at generation time.**
- Also time- and state-dependent inputs: `TimeManagerV2.Instance.CurrrentDayTime` (champion prob, chest tier tables, `DungeonRoom.cs:798`, `:1792`); NG+ level; owned amulets (`AmuletUtility.CheckIfAmuletOwned` reads the hero inventory, `ChestGeneration.cs:125`); `generateUnlockUtility`.

### Is `Random.InitState(seed)` before generation enough? **No (as read).**
- Generation spans many frames: bundle loads, `yield FreeResources`, `WaitForEndOfFrame` after every room (`:746`), corrupted-room bundle loads. Anything else that calls `UnityEngine.Random` in `Update`/`Start`/DOTween callbacks during those frames shifts the sequence. Examples: loading-panel animations, `EnemyBehaviour.DoScreenShake…`, `WandererFeedback`, objects whose `Start` runs (e.g. `BreakableObjectBehaviour.Start` uses `Random` and `GetRandom`, `BreakableObjectBehaviour.cs:44,54`, and those inside the active init room run while later rooms are still being built (inferred)).
- How many frames a bundle load takes varies per machine, but frame count alone does not change the sequence. Interleaved consumers do.
- Hero-dependent inputs (inventory amulets, day time) differ per machine.
- The game itself reseeds the global RNG only on slot reset/load (`GameManager.cs:2243`, `:2421`).
- **Recommendation (inferred):** generate on the host only and serialize the *result* (see below). Alternatively, patch the generator to wrap each room's work in `Random.state` save/restore with a per-room derived seed and also force the day-time and amulet inputs. Do not rely on global-state replay.

### What is decided when
| Thing | When |
|---|---|
| Layout, room templates, connections, exit side, special rooms (onsen, camp, chests, secret, corrupted, boss) | generation |
| Enemy pack (`currentSpawnPack`), champion, enemies **instantiated** (inactive) | generation (`RoomPlaced` → `SpawnEnemies` + `DisableEnemies`, `DungeonRoom.cs:1700-1701`) |
| Chest tier/mimic, chest contents, curses | generation |
| Obstacles, holes, slime/acid/lava/electric/falling floor, details, breakables | generation |
| Per-object `Start()` randomness (breakable skin/flip, skeleton notes index, camp props, abandoned items, crazy Pete note, healing-pool note position, nest item) | **first time that room's GameObject becomes active** (rooms are inactive, `RoomPlaced` ends with `SetActive(isInit)`, `DungeonRoom.cs:1743`) (inferred from Unity `Start` semantics) |
| Timed chest timer, Shrine, WavesShrine, TimedChestSlime binding | on first activation (`Start` reads `DungeonManager.Instance.currentDungeonRoom`) |
| Wanderer spawn | timer after the loading panel closes (§7) |
| Post-boss room enemies/chests (boss already defeated) | `DungeonBossRoom.RoomPlaced`/`EnterRoom` (§4) |

### `DungeonSaveInfo` (`DungeonSaveInfo.cs`): what it captures
Constructor `public DungeonSaveInfo(DungeonManager manager)` (`:151`):
- `culture`, `floor`; `wavesRoomFloor`, `corruptedRoomFloor`, `crazyPeteRoomFloor`, `abandonedItemsRoomFloor`, `wandererNestRoomFloor` from the static `floorGenerationSaveInfo` (`:155-159`).
- `List<RoomSaveInfo> rooms`, one per `manager.dungeonRooms` entry, **skipping `DungeonBossRoom` and the room named `"WND_INTERMEDIATE"`** (`:164`). `RoomSaveInfo` (`:9-121`): `roomInfoName`, `visited` (=`hasBeenVisited`), `culture`, `x`, `y`, `connections` (indices), `chests` (items as `ItemSaveInfo`, `type`, `tile`, plus `isMimic` entries derived from `specialEnemiesSpawns`), `nextLevelDoorIndex`, `lastLevelDoorIndex`, `isSecret`, `wavesUsed` (secret rooms only), `dungeonToHomeChestUsed`, `specialRoom` (never written), **`choosenEnemyPack` (declared at `:36`, never written or read anywhere)**.
- `portalRoomIndex` / `portalPosition` (the room whose `DungeonRoom.portal` is set, `:188-192`; the boss room overrides only the position at `:200-203`).
- `bossContiguousRoomIndex` (computed as an index into `manager.dungeonRooms` but used as an index into `rooms` on load, `:199` vs `DungeonGenerator.cs:1228`; these differ if any skipped room precedes it (inferred bug)), `isBossVisited`, `postBossChests`.

**Not captured** (so replaying it does NOT reproduce an identical floor):
- the enemy pack (`currentSpawnPack` is re-rolled by `LoadGridFromInfo`), champion, enemy alive/dead/health state;
- the `None_Holes`/`Holes_Rocks` tile choices (re-rolled, so obstacle layout can differ), details, backgrounds, breakables and their broken state, the skeleton;
- `isRoomCleared`, door open state, dropped items, healing pool remaining capacity, shrine/camp state, timed-room flag (load forces `roomInfo.isTimedRoom = false` on the shared DB object, `DungeonGenerator.cs:1078`);
- corrupted/secret relations beyond `isSecret` + same x,y.

On load (`GenerateSavedDungeonCoroutine`, `:1041-1288`): rooms are rebuilt through `AddSingleDungeonRoom` (which **re-runs `RoomPlaced`, re-rolling enemies and chests**). Then all freshly rolled chests are destroyed and replaced from the save (`:1147-1185`); mimics are re-spawned except in the portal room; the special doors are re-added; secret rooms are re-linked; the boss room is re-added at `(x, y+1)` of the contiguous room. Finally **all enemies in the portal room are killed** (`:1259-1266`) and the minimap is fully revealed (`:1283`).

Conclusion: to give clients an identical floor, send your own snapshot: per room `roomInfoName`, x, y, connections, `currentSpawnPack`, champion index, resolved `obstaclesGrid`, chests with contents, special door indices, secret links, boss and healing room. Then rebuild with patched `RoomPlaced`/`LoadGridFromInfo`/`SpawnEnemies` that take those values instead of rolling (inferred design).

### Prefab dungeons
`GeneratePrefabDungeonCoroutine(GameObject)` (`:952-1039`) instantiates a ready-made `DungeonManager` prefab (Unknown dungeon, tutorial) and calls `InitializeRoom` + `RoomPlaced(0,0)` on each room. `OnGeneratedDungeon` fires through `DOVirtual.DelayedCall(5f - elapsed)` (`:1024`).

---

## 2. DungeonManager (`DungeonManager.cs`)

Singleton: `private static DungeonManager _instance` / `public static DungeonManager Instance => _instance` (`:31`, `:99`), set in `Awake` (`:114`). A new one is created **per floor** (the old one is destroyed by the generator or the loading panel).

Fields (all `public` instance unless noted):
`GameObject wandererPrefab` (:35), `List<DungeonRoom> dungeonRooms` (:37), `DungeonRoom initialDungeonRoom` (:39), **`DungeonRoom currentDungeonRoom`** (:41), `DungeonRoom healingDungeonRoom` (:43), `int totalDungeonRooms, chestCount, visitedDungeonRooms` (:45-49), `PolyNav2D polyNavInstance` (:51), `DungeonBossRoom bossRoom` (:53), `private DungeonDoor lastDoorUsed` (:55), `DungeonBossRoomEnterDoor dungeonBoosEnterDoor` (:57), `DungeonRoomGenerator roomGenerator` (:59, `Resources.Load("DungeonRoomGenerator")`), `skeletonBreakableSkins`, `private Wanderer wanderer` (:65), `bool isWandererActive` (:68), `bool loadWanderer` (:70), `guidancePrefab`, `private Dictionary<DungeonRoom, DungeonDoor> guide`, breakable `WeightedList`s, `PoolManager poolManager`, `GameObject backgroundEffect`, `ItemMaster.Culture prefabCulture` (:95, `[FormerlySerializedAs("culture")]`), `IntermediateDungeonInfo intermediateInfo` (:97).
- `public ItemMaster.Culture culture => intermediateInfo.dungeonCulture` (:101), `public int level => intermediateInfo.dungeonLevel` (:103).
- `enum DungeonType { Normal, Intermediate }`; `struct IntermediateDungeonInfo { dungeonType, dungeonCulture, intermediateCulture, dungeonLevel, intermediateLevel }` (:11-29). Setters `SetDungeonAsNormal(culture, level)` and `SetDungeonAsIntermediate(...)` (:636-650).

Static events (good hook points):
- `public static event Action<DungeonRoom, DungeonRoom> OnHeroChangingRoom` (:105) (from, to)
- `public static event Action<DungeonRoom> OnEnterRoom` (:107) / `OnExitRoom` (:109), fired from `DungeonRoom.TriggerOnRoomEnter/Exit` (`DungeonRoom.cs:2245-2261`).

Lifecycle: `IEnumerator Start()` waits for `initialDungeonRoom`, moves the camera to `currentDungeonRoom`, calls `HeroMerchant.ChangeToDungeonModeWithReposition()`, resets `currentDungeonRoom = initialDungeonRoom` unless loaded from an emblem portal, and creates the guidance effect (:122-162). `OnEnable`: `DungeonDoor.OnLeavingRoomWithDoor += ChangeRoomTo` (:215-218). `OnDisable` unsubscribes and cancels the wanderer invokes (:220-226). `Init()`: `HeroMerchant.OnDie += HeroMerchant_OnDie` (goes to the results panel, :176-213).

### Room change via door: exact order
1. `HeroMerchantController` movement (`HeroMerchantController.cs:2340-2395`): the box-cast hits a collider tagged `"DungeonLevelDoor"`. After pushing for more than `maxTimePushingDoors = 0.15f` (`:239`) and if `door.isOpened`, it calls `component.OnPlayerEnter(base.gameObject)` (`:2387-2394`). There is also `DungeonDoor.OnCollisionEnter2D` when the hero is in intangible mode (`DungeonDoor.cs:106-112`).
2. `public virtual void DungeonDoor.OnPlayerEnter(GameObject player)` (`DungeonDoor.cs:57-69`): returns if `destinationRoom == null`. Calls `HeroMerchant.Instance.heroMerchantController.StopMovement(); DisableMoveInput();`. **Only if `DungeonManager.Instance.currentDungeonRoom == doorRoom`**: `DungeonDoor.OnLeavingRoomWithDoor(destinationRoom, this)` (static event, delegate `LeavingRoomWithDoor(DungeonRoom toRoom, DungeonDoor door)`, :5, :39), then `TeleportTo(playerEnterDestinyRoomPoint.position)`. The `player` argument is ignored and `HeroMerchant.Instance` is used.
3. `public void DungeonManager.ChangeRoomTo(DungeonRoom to, DungeonDoor door)` (`DungeonManager.cs:228-297`):
   1. Logitech/Razer glitch effects for corrupted rooms;
   2. `lastDoorUsed = door`; `to.DisableRoomDoorsColliders()`;
   3. `from.ExitRoom()` if from ≠ to;
   4. `currentDungeonRoom = to`;
   5. `CameraManager.Instance.followTarget = to.cameraObjective.gameObject`;
   6. PolyNav moved to `to.cameraObjective` and resized to the room floor;
   7. **`to.EnterRoom()`**;
   8. `OnHeroChangingRoom(from, to)`;
   9. `CameraManager.Instance.MoveCamera(to.transform, callback, clamp:false)`. The callback runs `AllowMovement()`, `EnableMoveInput()`, boss-room `CameraFollowPlayer()`, and the guide animation. The `MoveCamera` tween `OnComplete` also calls `CameraManager.FinishedRoomTransitionMovement()`, which runs `currentDungeonRoom.InitializeRoom()` (if needed) + `EnableRoomDoorsColliders()` (`CameraManager.cs:314-347`);
   10. onsen playlist start/stop.
4. Then back in `OnPlayerEnter`, the hero is teleported (step 2).

`DungeonRoom.ExitRoom()` (virtual, `DungeonRoom.cs:1527-1543`): `TriggerOnExitRoom()`, `hasBeenVisited = true`, `CancelInvoke("EnableEnemies")`, **`DisableEnemies()`** (each enemy: `OnPlayerLeaveRoom()`, `CancelPlayerTargetingAfterTime()`, `SetActive(false)`), unsubscribes `HeroMerchantInventory.OnItemAddedToInventory`, `HeroMerchant.Instance.IsInCombat = false`, minimap exit, **`Invoke("DisableRoom", 0.5f)`** (→ `gameObject.SetActive(false)` + `PolyNav2D.current.GenerateMap`, :1545-1549), ambient restore.

`DungeonRoom.EnterRoom()` (virtual, :1551-1599): `CancelInvoke("DisableRoom")`, **`Invoke("EnableEnemies", enemiesAppearDelay)`** (0.5s; each enemy `SetActive(true)` + `StartTargetingPlayerAfterTime(1f)`, :934-949), `CleanDropList` coroutine, `OnItemAddedToInventory += OnHeroPickedItem`, `IsInCombat = true` if enemies remain, **`CloseAllDoors()`**, minimap enter/center, **`gameObject.SetActive(true)`**, ambient, `TriggerOnRoomEnter()` (instance `OnRoomEnter` + static `DungeonManager.OnEnterRoom`), hole shader textures, `OnMustPlayMusic`.

### `SpecialChangeRoomTo(DungeonRoom to, bool teleportToLastDoor = false)` (:299-359)
Used for falling into a hole into a secret room and for climbing the rope back up. It disables input, `from.ExitRoom()`, sets `currentDungeonRoom = to`, plays the pixelated effect, then in the `GUIManager.FadeOut` callback: PolyNav, `to.EnterRoom()`, `OnHeroChangingRoom`, optional `TeleportHeroToLastDoorUsed()`, then `FadeIn`, which re-enables input and panels. It moves the wanderer off-screen and calls `wanderer.OnPlayerLeaveRoom()`. **It does not touch the camera itself**; the secret rope's `OnRoomEnter` teleports the hero (`DungeonRoomSecretRope.cs:30-38`). The camera works because the secret room shares the host room's position (inferred).

Other: `TeleportHeroToLastDoorUsed()` (:361), `OnGeneratedDungeon()` (instance method, wanderer timer; §7), `RemoveWandererFromDungeon()`, `SpawnWanderer()`, `ForceSpawnWanderer()`, `IsThereWayToEnd()`, `ActivateGuide()` (guidance potions), `GetItemCountInDungeon`, `ApplyConfusionEffectToAllEnemiesInCurrentRoom`, `ChangeLastDoorUsed`.

---

## 3. DungeonRoom (`DungeonRoom.cs`)

Key fields: `bool isInit, isEnd` (:16-19), `private bool _isLocked` + `public bool isLocked` (setter closes all doors / fires `OnAllEnemiesDefeated`, :163-181), `public bool hasBeenVisited` (:23), `private bool containsMimics`, `isRoomInitialized`, `_isRoomCleared` (+ property `isRoomCleared`, :219), `public bool canContainMimics = true`, `int x, y` (:55-57), `Vector2 size = (1120, 720)` (:59), `roomFloorWidth = 484`, `roomFloorHeight = 268`, `gridSlicesX = 20`, `gridSlicesY = 11`, `public DungeonRoomInfo roomInfo` (:66), `public int[] obstaclesGrid` (:69), `public List<DungeonDoor> roomDoors` (index 0=N,1=S,2=W,3=E; `GetNorthDoor()`…`GetEastDoor()`, :1191-1209), `List<GameObject> roomDoorsBoundaries`, `List<Collider2D> roomBoundaries`, `List<Chest> roomChests` (:78), `int specialRoomIndex = -1` (index of the special door: init/next-level/boss), `int currentSpawnPack` (:83, **the chosen enemy pack**), `Transform cameraObjective` (:90), `EmblemPortal portal` (:105), **`List<Enemy> roomEnemies`** (:107), `float enemiesAppearDelay = 0.5f`, `DungeonLastLevelDoor lastLevelDoor, firstLevelDoor`, `List<EnemySpawn> specialEnemiesSpawns` (mimics), `List<EnemySpawn> deadEnemies` (:129), `DungeonRoom secretRoom` (:133), `bool isSecret` (:135), `int roomCenterYOffset`, `private DungeonDoor specialDoor` (:155), `Dictionary<int,int> tilesZones`.
- `isDeadEnd` (:183-193): true if `isEnd`, or if it has chests, or if `containsMimics`, or if the room has exactly one used door and is not init/special.
- `areAllEnemiesDefeated` (:231): no non-`InvulnerableEnemy` left in `roomEnemies`. `AreAllEnemiesDefeated()` also requires `!isLocked` (:969).
- Events: `event Action<Enemy> OnEnemyDefeated`, `event Action OnRoomEnter`, `OnRoomExit`, `OnAllEnemiesDefeated`, `event Action<List<ItemStack>> OnDropListChanged` (:247-255); also `Action OnMustPlayMusic`, `Action OnRoomInitialized`.
- `static Sprite[] cacheLowerDetails/cacheLower2Details` + indices (:39-45), cleared by `FreeDetails()`.

### Lifecycle
- `InitializeRoom(DungeonRoomInfo)` (internal, :1872): `LoadGridFromInfo()` (rolls `currentSpawnPack`, builds `obstaclesGrid`), destroys doors not in `availableDoors`, then `InitializeRoom()` (:1905). That is a one-time subscription `OnAllEnemiesDefeated += UnLockRoomChests; += OpenAllDoors; += ResetHeroMerchantIsInCombat`.
- `public virtual void RoomPlaced(int dungeonLevel, int version)` (:1690-1744): `SpawnEnemies()` (instantiates every enemy of `enemiesSpawnPacks[currentSpawnPack]` under a child `"DungeonLevelEnemies"` → `"EnemySpawn"` GO at the tile, sets `enemy.enemyRoom = this`, `Init`, optional `TransformToChampion`, `roomEnemies.Add`; :789-854), **`DisableEnemies()`**, `SpawnChests()` (tile 32 = Chest; chest or mimic enemy, :1388-1448), obstacle/hazard prefabs through `DungeonRoomGenerator` (rocks, holes, slime, acid, lava, electric, falling floor), `SpawnRoomPrefabs()` (fixed prefab spawns + skeleton), `SpawnDetails()`, `SetTilesZones()`, then **`gameObject.SetActive(isInit)`**.
- **Non-current rooms are inactive GameObjects.** Enemies are **instantiated at generation**, inactive, and activated 0.5s after each entry.

### Cleared logic and doors
- Enemy death: `EnemyStats` → `component.enemyRoom.EnemyDefeated(component)` (`EnemyStats.cs:268-270`) → `internal void EnemyDefeated(Enemy)` (:904-932). It removes the enemy from `roomEnemies`. If `AreAllEnemiesDefeated()`: `isRoomCleared = true`, minimap update, **`OnAllEnemiesDefeated()`** (→ `OpenAllDoors`, `UnLockRoomChests`, `IsInCombat=false`, and room-specific subscribers). Then `OnEnemyDefeated(enemy)`, and the dead spawn is stored in `deadEnemies` with its death tile (used by `RechargerBehaviour` resurrection, `ResurrectEnemy`, :882).
- **Who closes doors**: `EnterRoom` → `CloseAllDoors()` (private, :1031-1059). It closes doors only if `isLocked`, or if `isDeadEnd && roomEnemies.Count > 0`. **Normal multi-door rooms without chests do not close their doors** (as read). It calls each `door.CloseDoorAfterTime(0.3f)` (Invoke `CloseDoor` → `isOpened=false`, animator "Close", `doorRoom.OnDoorClosed`, `DungeonDoor.cs:80-95`), switches the boundary layer to the closed layer, and calls `HeroMerchant.Instance.heroMerchantController.PlayWarningAnimationAfterTime(0.5f)`.
- **Who opens**: `OpenAllDoors()` (public, :1061-1089). It is subscribed to `OnAllEnemiesDefeated`, and also called by `Shrine.OnButtonPressed` (`Shrine.cs:27-35`). It opens doors with a `destinationRoom` plus the special door.
- `DungeonDoor.isOpened` (public bool, default true) is the gate the hero controller checks.

### State when leaving and returning
- Enemies stay instantiated, inactive (`DisableEnemies`). On re-entry, `EnemyBehaviour.EnemyRoom_OnRoomEnter()` (subscribed to `enemyRoom.OnRoomEnter`, `EnemyBehaviour.cs:114-117`) **resets `transform.localPosition = 0` (spawn point) and `CurrentHealth = maxHealth`** (`EnemyBehaviour.cs:132-136`). Survivors fully heal and reset. Killed enemies stay dead (they were removed from `roomEnemies`). `OnPlayerLeaveRoom()` → `SetVencible()` (:591).
- `isRoomCleared`, chests, drops (`_droppedItems`), `hasBeenVisited` persist for the floor. The floor itself is discarded on the next floor.
- `FallingFloor` restarts on `OnRoomEnter` (`FallingFloor.cs:23`). `Shrine` re-closes (`Shrine.cs:15-25`). `TimedChestSlime` **destroys its chest when you leave** before clearing (`TimedChestSlime.cs:25`, `:63-71`).

### Traps, breakables, room-specific objects
- Hazards from `obstaclesGrid` tile flags (`DungeonRoomInfo.DungeonTile`: None=1, Holes=2, None_Holes=3, Rocks=4, Holes_Rocks=6, FallingFloors=8, Slime=16, Chest=32, Acid=64, Lava=128, Electric=256; `DungeonRoomInfo.cs:9-22`) → `DungeonRoomGenerator.Generate*Prefab` (`DungeonRoom.cs:1711-1730`).
- Breakables (`BreakableObjectBehaviour`): spawned by `FillRoomWithBreakables` (:637). In `Start`, **`enemy.enemyRoom = DungeonManager.Instance.currentDungeonRoom`** (`BreakableObjectBehaviour.cs:59-65`). Drops use `DungeonManager.GetRandomBreakableDrop` and are parented to `currentDungeonRoom` (:166-228).
- Special rooms and their behaviours: `EnemyWavesShrine` (waves secret room; locks the room, warp spawners, tiered chests; `EnemyWavesShrine.cs`), `DungeonToHomeChest` (sends items to the shop), `AbandonedItemsGenerator`, `WandererNestPedestal` (taking the item → `DungeonManager.ForceSpawnWanderer()`, `WandererNestPedestal.cs:40-50`), `CampGenerator` (Crazy Pete camp), `UnlockUtilityCampGenerator` (sets `myRoom.isLocked`), `Shrine` (hard-enemies room), `TimedChestSlime` (timed chest: `time = roomEnemies.Count * 5` s, `TimedChestSlime.cs:32`), miniboss rooms (fixed room names, §1), `EnemyRandomSpawnerController` (DLC, §8). There are no generic "ambush rooms" beyond the locked or dead-end logic and the waves shrine (as far as read).

---

## 4. Doors and transitions

| Class | Base | Notes |
|---|---|---|
| `DungeonDoor` | MonoBehaviour | `doorRoom`, `destinationRoom`, `playerEnterDestinyRoomPoint`, `isOpened`, `isDisabled`, `isClosedAtStart`, `closedPatriclesPrefab`; `virtual bool CanBeConnected`; `OnPlayerEnter`, `CloseDoor`, `OpenDoor` (virtual), `CloseDoorAfterTime`, `EnableDoor`/`DisableDoor`, `SetAsClosed` (`DungeonDoor.cs`) |
| `NextLevelDoor` | DungeonDoor | floor exit (§ below) |
| `DungeonBossRoomEnterDoor` | DungeonDoor | replaces the contiguous room's north door |
| `DungeonBossRoomExitDoor` | DungeonDoor | the boss room's south door |
| `DungeonLastLevelDoor` | MonoBehaviour (not a door) | decorative entry door in the init room (`firstLevelDoor` on floor 0, `lastLevelDoor` otherwise); closes after 2.5s and on `initialDungeonRoom.OnRoomEnter` |
| `NextLevelPortal` | MonoBehaviour | hole-type exit: `HeroMerchantController.OnSpecialHoleFall` → `LoadNextLevelDungeonScene` (`NextLevelPortal.cs`) |
| `FakeDungeonRoomThatLoadNextLevelDungeon` | DungeonRoom | `EnterRoom` → `LoadNextLevelDungeonScene` (tutorial) |
| `HolePortalToIntermediateLevel` | IActionable | DLC interdimensional portal (§8) |

There are no key-locked doors. Room "locking" is `DungeonRoom.isLocked` only (set by the waves shrine, the unlock camp, the tutorial). Secret rooms have no door: you fall through a hole.

### Floor exit → next floor
1. `NextLevelDoor.OnPlayerEnter` (`NextLevelDoor.cs:50-70`) requires `_room.AreAllEnemiesDefeated() || (!HeroMerchant.Instance.IsInCombat && !_room.isLocked)`. It then disables panels, `doorRoom.TriggerOnExitRoom()`, pauses playlists, animator `"CloseSpecial"`, auto-moves the hero into the door, `DungeonManager.Instance.RemoveWandererFromDungeon()`.
2. Animation event `OnDoorClosed()` (:77-96) → `HeroMerchant.Instance.LoadNextLevelDungeonScene(culture)` (`intermediateCulture` in intermediate dungeons).
3. `HeroMerchant.LoadNextLevelDungeonScene(ItemMaster.Culture culture, bool istest = false)` (`HeroMerchant.cs:618-633`): `_currentDungeonLevel++`; for Golem…Unknown → `GameManager.Instance.LoadDungeonScene(culture, _currentDungeonLevel, replay:true, istest)`; fires `static event Action<ItemMaster.Culture,int> OnFloorChange` (:239). The Wanderer DLC handles its culture through this event (§8).
4. `GameManager.LoadDungeonScene` (`GameManager.cs:1750`) → `LoadDungeonSceneCoroutine` (:1766-1900): fade, `GoToDungeonLoadingPanel(toDungeon:true, culture, level, isTest)`, unload bundles (replay), load the "Dungeon" scene additively if needed, wait for the `DungeonGenerator` and `!IsGeneratingDungeon`, then fire `GameManager.OnDungeonLoaded(false)` (`event Action<bool>`, :252).
5. `DungeonLoadingPanel.Disable()` (`DungeonLoadingPanel.cs:192-336`) calls `initialDungeonRoom.EnterRoom()` and teleports the hero next to the `DungeonLastLevelDoor` (direction by `specialRoomIndex`). In the fade-out callback it re-enables input, shows the welcome scroll, and **calls `DungeonManager.Instance.OnGeneratedDungeon()` (starts the wanderer timer)** (:316).
- Entry from town: `WorldDungeonDoor.LoadDungeon()` → `static Action<ItemMaster.Culture,bool> OnDungeonPortalActivate` (`WorldDungeonDoor.cs:9`, `:200-224`) → `HeroMerchant.LoadDungeonScene(culture)` (sets `_currentDungeonLevel = -1` then `LoadNextLevelDungeonScene`, `HeroMerchant.cs:583-592`; subscribed at `:370`).

### Boss floor
- The boss is on **floor index 2 (third floor) of the same generated floor**: `if (level == 2) BossRoomMiner` (`DungeonGenerator.cs:521-525`). Floors 0 and 1 end in a miniboss room with a `NextLevelDoor`.
- `AddBossRoom(DungeonRoom room, GameObject bossRoom, GameObject generatedDungeon, int x, int y)` (`DungeonGenerator.cs:1858-1887`): instantiates the culture's `DungeonBossRoom` prefab directly **above** `room`. It instantiates `enterRoomPrefab` as a `DungeonBossRoomEnterDoor`, replaces `room`'s north door with it (`ReplaceSpecialDoor`), wires `previousRoom`/`destinationRoom`/`Boss`, and sets `dungeonManager.bossRoom` and `dungeonBoosEnterDoor`. **The boss room is not added to `dungeonRooms` here**; only `_roomsGenerated` gets it, for the minimap.
- `DungeonBossRoomEnterDoor.OnPlayerEnter` (`DungeonBossRoomEnterDoor.cs:42-70`) **returns unless `player == HeroMerchant.Instance.gameObject`**. On the first entry it plays `"CloseSpecial"`, and the animation event `OnDoorClosed()` → `base.OnPlayerEnter` → normal `ChangeRoomTo`.
- `DungeonBossRoom` (`DungeonBossRoom.cs`): fields `BossBehaviour Boss`, `bossRoomEnterDoor`, `DungeonBossRoomExitDoor southDoor`, `Chest bossChest`, `bossKeyPrefab`, `Transform portalDoor`, `OnPlayerAction OnPlayerEnteredRoom/OnPlayerExitRoom`, `bool isBossBattle`; `IsBossDefeated` = `culturesDefeated` bit or `GameManager.bossDefeatedCulture == roomInfo.culture` (:83-93).
  - `EnterRoom` override (:151-223): on the first visit with the boss undefeated, closes the south door, arms `DungeonRoomBossAlertArea`, `RemoveWandererFromDungeon()`, `hasBeenVisited = true`.
  - `DungeonRoomBossAlertArea.OnTriggerEnter2D(Player)` → `StartBossAwakeAnimation()` (camera to boss, input locked, `canUsePendant = false`; `DungeonRoomBossAlertArea.cs:7-14`, `DungeonBossRoom.cs:318-347`).
  - `InternalOnBossDeath()` (:455-502) → `bossChest` active, `PlayKeyObtainAnimation()`, **`GameManager.Instance.bossDefeatedCulture = roomInfo.culture`**, and `DestroyMenacingInstances()`, which destroys **every** `Enemy` in the scene except the boss and breakables (`FindObjectsOfType`, :504-520).
  - When the boss chest closes, `OnClosedBossChest()` → `GameManager.Instance.OpenPortalInDungeon(portalDoor.position)` (an emblem-style portal home, :522-533). The defeat is committed in `GameManager.SaveSlotOnReturnFromDungeon` (`culturesDefeated |= 1 << bossDefeatedCulture`, `GameManager.cs:2489-2497`).
  - If the boss was already defeated: `PostBossSpawnEnemyController` (on the boss room) spawns a random enemy set in `RoomPlaced` and 3 timed chests on the first enter. Clearing the room opens the portal to town (`PostBossSpawnEnemyController.cs:57-207`).
- `DungeonBossRoomExitDoor.OnPlayerEnter` → `OnDoorClosed()` (`DungeonBossRoomExitDoor.cs:37-61`) does its own room change. It calls `destinationRoom.EnterRoom()` **twice**, teleports, sets `currentDungeonRoom` directly, moves the camera, and does **not** call `bossRoom.ExitRoom()` or fire `OnHeroChangingRoom`.
- Final dungeon: `DungeonFinalBossRoom : DungeonBossRoom` (Unknown culture, two phases, `ReplaceBoss()`). Hero death there → `LoadTownSceneFromDungeon` (`DungeonFinalBossRoom.cs`).

---

## 5. Leaving mid-run

### Merchant Pendant (free exit, keeps items)
- `HeroMerchant.CanUsePendant` (`HeroMerchant.cs:149-177`): requires gold ≥ `GetPendantCost()`, in dungeon, `canUsePendant`, and a culture that is not Unknown or Merchant.
- `public static int GetPendantCost()` (:267-300) = `merchantPendantNeededMoney[_lastDungeonCulture] * (1 + 0.5 * _currentDungeonLevel)`. NG+ factors apply; the Wanderer dungeon uses the last array entry with `pendantIncrementPerLevelForWanderer`.
- Trigger: hold the shortcut (`HeroMerchantController.cs:1595-1610`, `quickConsumeUsePendantDelay`) → `HeroMerchant.UseMerchantPendant()` (:717-736): `addGold(-cost)`, pendant animation, `GameManager.PauseGame()` → `OnPendantUsed` → `GUIManager.GoToResultsPanel(fromEmblem:false)` → `DungeonResultsPanel` "return" → `GameManager.LoadTownSceneFromDungeon()` (`DungeonResultsPanel.cs:793-800`). Nothing of the dungeon is saved.

### Merchant Emblem (portal you can come back through)
- `CanUseEmblem` (:179-195): not in a secret room, not during a boss battle, no existing `_dungeonPortal`, gold ≥ `GetEmblemCost()` (`merchantEmblemNeededMoney[culture] * (1 + 0.5*level)`, :302-322), `UnlockedEmblem` (`maxDungeonLevelVisited[1] > 1`), not Wanderer, Unknown, Merchant or intermediate.
- `UseMerchantEmblem()` (:745-758): also requires **`DungeonManager.Instance.currentDungeonRoom.isRoomCleared && !IsInCombat`**. Then `addGold(-cost)`, `GameManager.Instance.OpenPortalInDungeon(hero position)`.
- `GameManager.OpenPortalInDungeon(Vector2)` / private `OpenPortal` (`GameManager.cs:2712-2753`): instantiates `Resources/EmblemPortal`. In the dungeon it **sets `currentDungeonRoom.portal`**, parents the portal to `currentDungeonRoom`, and sets `lastRoomWithPortal`. Single slot `public GameObject _dungeonPortal` (:131), `_townPortal` (:133).
- `EmblemPortal` (`EmblemPortal.cs`) interaction → `AbsorbPlayerAndTeleportPlayerToDungeon()` → animation → `LoadDungeon()` (:171-212). In the dungeon it calls `GoToResultsPanel(!bossBattle)` and **`currentGameSlot.emblemDungeon = new DungeonSaveInfo(DungeonManager.Instance)`** (unless in a boss battle in a normal non-Wanderer dungeon). The results panel then → `LoadTownSceneFromEmblemPortal()`. In town, a portal opens at `"EmblemPortalPosition"` (`DungeonLoadingPanel.cs:322-326`).
- Return: town portal `LoadDungeon()` → `HeroMerchant.LoadDungeonSceneFromEmblemPortal()` (`HeroMerchant.cs:604-616`, restores `_currentDungeonLevel = emblemDungeon.floor`) → `GameManager.LoadDungeonSceneFromEmblemPortal()` (`GameManager.cs:1743`, `isSceneLoadedFromEmblemPortal = true`) → `GoToDungeonLoadingPanel(true, emblemDungeon, …)` → `GenerateSavedDungeon` (§1). `DungeonLoadingPanel.Disable` then teleports the hero to `emblemDungeon.portalPosition`, enters `currentDungeonRoom` (= the `portalRoomIndex` room), sets `emblemDungeon = null`, reopens the portal, and sets `EmblemPortal.spawnWill = true` (the portal destroys itself when you leave that room, `EmblemPortal.cs:54-72`) (`DungeonLoadingPanel.cs:268-284`).
- `GameSlot.emblemDungeon` is a persistent save field (`GameSlot.cs:28`). It is cleared when a normal dungeon load starts (`GameManager.cs:1809-1812`) and on a non-emblem town return (:2083-2087).

---

## 6. Chests, healing pool, camp, notes, secret rooms

### Chests
- `Chest : MonoBehaviour, IActionable` (`Chest.cs`). `enum chestType { WOOD, IRON, GOLD, BLUE, PURPLE, PIRATE, PEDESTAL, SHOP, SLIME, SHOP_5 }` (:27-39). Fields: `type`, `capacity = 28`, `fixedDrop`, `generateRandomContentOnInit`, `dropIncrement`, `hasBeenOpened`, `public static Chest currentChestOppening` (:88), `public static int generatedAmuletsInCurrentDungeon`, property `isLocked` (unlock animation and trigger block), `ChestContent`. Events: `OnItemAdded`, `OnItemChanged`, `OnItemRemoved`, `OnChestClosed`, `OnChestOpen` (:156-164).
- Contents: decided in `Awake` (§1) through `ChestGeneration.GenerateChestContent(culture, chestType, capacity, maxDifferent, maxStack, dropIncrement)` (`ChestGeneration.cs:100`). The weighted lists are built by `RefreshWeightLists()` per generation (Wanderer: `RefreshWandererWeightLists()`).
- **Cursed items**: each generated stack gets `itemStack.GenerateEffect(ItemEffectManager.Instance.GetRandomEffect(chestType))` (`ChestGeneration.cs:130-133`). The effect pools come from `ItemEffectManager.Init` using `*ChestItemEffectProbabilities` per `InventorySlotAction.evil` (`ItemEffectManager.cs:30-70`). On save they are restored by `curseName` (`Chest.cs:876-879`).
- Lock: room chests spawn `isLocked = true` (`DungeonRoom.cs:1407`). `OnAllEnemiesDefeated` → `UnLockRoomChests()` (:1506). Interaction is blocked while `HeroMerchant.Instance.IsInCombat` (`Chest.cs:742-749`).
- Open flow: `OpenChest()` (:600-673; sets `currentChestOppening`, input locked, `Invoke("OpenChestGUI", 1.5f)`, discovers items) → `OpenChestGUI()` → `GUIManager.Instance.GoToChestPanel(this)` (`GUIManager.cs:887`) → `ChestInventoryPanel.SetChest(Chest)` (`ChestInventoryPanel.cs:467`, field `public Chest currentOppenedChest`). Closing the panel → `currentOppenedChest.CloseChest()` (`ChestInventoryPanel.cs:248`, `Chest.cs:700-740`, fires `OnChestClosed`).
- Mimics: `SpawnChests` puts a `"<Culture>Mimic<ChestID>"` enemy into `roomEnemies` and `specialEnemiesSpawns` and sets `containsMimics = true` (`DungeonRoom.cs:1428-1446`). `Mimic.Start` reads `currentDungeonRoom.roomInfo.culture` (`Mimic.cs:21`).
- Variants: `DungeonToHomeChest`, `NextFloorChest`, `ValueChest`. All register themselves in `currentDungeonRoom.roomChests` in `Start`. Boss chest: `DungeonBossRoom.bossChest`.

### Healing pool (onsen)
- Room: `roomDatabase.healingRoomInfo`; `dungeonManager.healingDungeonRoom` (`DungeonGenerator.cs:670-673`). It is placed on the middle of the init→end path (§1).
- `HealingArea` (`HealingArea.cs`): a finite pool `initialHealingHealth = HealingPoolCapacity[culture] * NG+ factor (* Wanderer floor factor)` (:88-107). On `OnTriggerEnter2D` with tag `"Player"` it calls `ApplyHealingEffect` repeatedly and transfers HP from `healingHealth` to **`heroMerchant = HeroMerchant.Instance`** until the hero is full or the pool is empty (:359-418). The pool darkens as it drains. It refills only with a new floor. Usable any number of times until the pool is drained (as read).
- It also positions a `cultureNotes` (`CultureNotes`, lore note `SKELETONS/NOTE_<CULTURE>_<level+1>`) randomly in the room (:115-119, `CultureNotes.cs`).

### Camp (Crazy Pete)
- `roomDatabase.campRoomInfo`, `CampGenerator` (`CampGenerator.cs`): random props in `Start`, fades the music. `CrazyPeteNotes` (`CrazyPeteNotes.cs`) picks the next unread note from `currentGameSlot.achievements.crazyPeteNotesRead` and is read through `I2Conversation`. The Wanderer will not enter rooms containing `CrazyPeteNotes` (`Wanderer.cs:84-87`).

### Other notes
- `SkeletonNotes` (enabled on `OnAllEnemiesDefeated`, random index 1-5). The breakable skeleton spawns `"SkeletonPaper"` (`BreakableObjectBehaviour.cs:150`).

### Secret rooms
- Built at the same x,y as the host (§1). The `DungeonRoomSecretAdvisor` particle hint plays on the host's `OnRoomEnter`.
- Discovery: the hero falls into a hole → `HeroMerchantController.PlayFallAnimation()` → `currentDungeonRoom.OnPlayerFallsOnHole()` (`HeroMerchantController.cs:2509`). If `secretRoom != null`: `DungeonManager.Instance.SpecialChangeRoomTo(secretRoom)`, plus the `SECRET_ROOM_FOUND_NUM` stat on the first visit (`DungeonRoom.cs:2230-2243`). Otherwise the hero takes fall damage and is teleported to `lastDoorUsed`.
- Exit: `DungeonRoomSecretRope.GoUp()` → `SpecialChangeRoomTo(_upsideRoom, teleportToLastDoor:true)` (`DungeonRoomSecretRope.cs:46-51`). The minimap shows `SecretIcon` once `secretRoom.hasBeenVisited`.

---

## 7. The Wanderer ("will hunt you if you linger")

- Class `Wanderer : EnemyBehaviour` (`Wanderer.cs`); prefab `DungeonManager.wandererPrefab`.
- Timer: `DungeonManager.OnGeneratedDungeon()` (instance method, **not** the generator event; `DungeonManager.cs:368-380`). It is called from `DungeonLoadingPanel.Disable` (`DungeonLoadingPanel.cs:316`) and from `WandererDLCController.EndDungeonGeneration` (`WandererDLCController.cs:855`). If `loadWanderer && gold > 50`: `Invoke("SpawnWanderer", wandererTimeToAppear (360 s) * (gold <= pendantCost*0.5 ? 2 : 1))` (×3 in demo mode) and `Invoke("SpawnWandererFeedback", t - 15)`. It is a plain `MonoBehaviour.Invoke`, so it is per floor and **game-time** based.
- `SpawnWanderer()` (:407-444) does nothing if the culture is already defeated, the boss room was visited, the culture is Unknown, or `!loadWanderer`. If the hero is in the init room, the healing room or the end room, it retries in 20 s. Otherwise it instantiates the wanderer **in `currentDungeonRoom`** at `lastDoorUsed.playerEnterDestinyRoomPoint`, `StartTargetingPlayer()`, `isWandererActive = true`, plays the wanderer playlist. The HUD uses `HUDManager.Instance.wandererFeedback` (`WandererFeedback`, `enum WandererStatus { Hide, Seek, Angry }`).
- Behaviour: invincible (`Init`, :31-36). Targets the room's first chest, then a random dropped item, then the hero (`ChooseTarget`, :252-276); destroys chests and items it hits (:278-309). On `enemyRoom.OnRoomExit` → `OnPlayerLeaveRoom()`: it becomes invisible, re-parents to `DungeonManager`, and flies toward **`DungeonManager.Instance.currentDungeonRoom`** at 50 u/s. It re-enters when inside that room (not the boss room, a secret room, the healing room or the camp) (:56-130, :189-197).
- Removal: `RemoveWandererFromDungeon()` (:382-390). It is called by `NextLevelDoor`, the first boss-room entry, and `DungeonResultsPanel.Enable`. `ForceSpawnWanderer()` is used by `WandererNestPedestal`.

---

## 8. Dungeons list, progress, DLC

### `ItemMaster.Culture` (`ItemMaster.cs:17-28`)
`Merchant=0, Golem=1, Forest=2, Desert=3, Tech=4, Unknown=5, Developer=6, Wanderer=7, ICT=8`. `Developer` is used as the "none" sentinel (e.g. `GameManager.bossDefeatedCulture` defaults to Developer, `GameManager.cs:123`).

- Levels: Golem/Forest/Desert/Tech have **3 floors** (level 0,1,2). Floors 0 and 1 end in a miniboss room + `NextLevelDoor`; floor 2 has the boss room (§4). Floor size 10/12/14 rooms.
- Unknown (the final "fifth" dungeon): prefab generators `UknownDungeonGenerator-<level>` (`DungeonGenerator.cs:302`) use fixed `dungeonPrefab` layouts. `maxDungeonLevelVisited[Unknown]` is forced to 2 (:251-254). The final boss is `DungeonFinalBossRoom`. Pendant and emblem are not usable there. The minimap is disabled (`DungeonGenerator.cs:1032-1035`).
- Progress save fields (`GameSlot.cs`): `int culturesDefeated` (bitmask `1 << culture`, default 1 = Merchant, :54, :123; set at `GameManager.cs:2491`; `0x3F` = game beaten), `int[] maxDungeonLevelVisited` (size 8, -1 default, :56, :124-128; updated in `GenerateDungeon` :250), `DungeonSaveInfo emblemDungeon`. `GameManager.bossDefeatedCulture` is the transient "boss killed this run" flag. Town doors unlock from `culturesDefeated` in `WorldDungeonDoor.UpdateByCulturesDefeated()` (`WorldDungeonDoor.cs:68-96`; Wanderer is always unlocked).
- `HeroMerchant` private `_currentDungeonLevel` (:59, property `currentDungeonLevel` :103) and `_lastDungeonCulture` (:65) drive pendant and emblem costs.

### DLC "Between Dimensions" (Wanderer)
- Controller: `Moonlighter.DLC.WandererDungeon.WandererDLCController` (`static Instance`, `WandererDLCSaveInfo saveInfo`). In `Enable()` (`WandererDLCController.cs:180-216`) it subscribes `WorldDungeonDoor.OnDungeonPortalActivate`, `WandererWorldDungeonDoor.OnDungeonPortalActivate` (to a floor), `DungeonGenerator.PostprocessDungeonEvent`, `GenerateSoundsEvent`, `DungeonResultsPanel.OnReplayTriggered`, `HeroMerchant.OnFloorChange`.
- Generation: `StartDungeonGeneration(culture, floor)` (:715-741) → `PrepareDungeonGeneration` (unloads and loads the Dungeon scene, `ChestGeneration.RefreshWandererWeightLists()`) → `LaunchDungeonGenerator(data, floor, roomDatabases, specialRoomDatabases)` (:743-782). It builds a `Moonlighter.DungeonGeneration.DungeonGenerator`, `CreateDungeonParent(culture, floor)`, `yield GenerateDungeonCoroutine(DungeonGenerationData data, int floor)`, and **regenerates if there is neither a boss room nor a NextLevelDoor**. Then `EndDungeonGeneration` (:803-865) does the same as the loading panel (`initialRoom.EnterRoom()`, teleport, `DungeonManager.OnGeneratedDungeon()`).
- New generator differences (`Moonlighter.DungeonGeneration/DungeonGenerator.cs:66-521`):
  - data-driven (`DungeonGenerationData`: `floors`, `hasWanderer`, `hasCrazyPeteRoom`, `hasOnsenRoom`, secret-room flags, `hasMinibossRoom`/`minibossFloorStep`, `hasWandererOnsenRoom`, `hasWandererShopperRoom`, `hasWandererHologramRoom`, `hasWandererMinibossRoom`, chest floors; `DungeonGenerationFloorData`: `min/maxRoomCount`, miners, `minPathLength`, `hasBoss`, holes, lights, details);
  - rooms are tagged through `DungeonRoomRegistry.MarkRoomAs(caveRoom, id)` with string IDs `"INITIAL"`, `"BOSS"`, `"MINIBOSS"`, `"ONSEN"`, `"NEXT_LEVEL"`, `"CHEST"`, `"TIMED_CHEST"`, `"CRAZY_PETE"`, `"WND_HOLOGRAM"`, `"DUNGEON_TO_HOME"`, `"WANDERER_NEST"`, `"CORRUPTED"`, `"ABANDONED_ITEMS"`, `"WAVES"`, `"HARD_ENEMIES"`, `"WND_ONSEN"`, `"WND_SHOPPER"`, `"WND_MINIBOSS"` (:20-54);
  - `DungeonGenerationData.ResetFloorStats()` rolls the special-room floors on floor 0 (`DungeonGenerationData.cs:143-239`); cave via `FixedStepsMiner` + `CommonRoomMiner`s (`GenerateCave`, :1177-1240); `PostProcessCave` (:1242-1317);
  - `RoomPlaced(floor, 1)` (version 1 = floor-data backgrounds), plus `SetFloorData`, `SetHolesImages`, `SetLightData`, `SpawnDetails(floorData.detailData)` per room (:337-342);
  - **the secret rooms are only tagged here; `AddSecretRoom` (:675) is never called in this file** (as read), so the DLC dungeon has no fallable secret rooms except the WND_INTERMEDIATE case below;
  - the end of a floor is a miniboss / wanderer-shopper room or a `"<culture>BossRoom"` (`DungeonWandererBossRoom`, two phases, `bossDefeatedCulture = Wanderer`, `DungeonWandererBossRoom.cs:112-144`);
  - one room per frame (`yield return null`, :460);
  - **not saved by the emblem** (`CanUseEmblem` excludes Wanderer; `GenerateSavedDungeon` only exists in the old generator).
- Floors and lift: `WandererDLCSaveInfo` (`unlockedWeapons`, `finalWeaponsRemaining`, `unlockedLiftToFloors` bitmask, `lastLiftAccessOptionSelected`, `maxLevelReachedInWandererDungeon`, …; `WandererDLCSaveInfo.cs`). `WandererDungeonLiftAccess` offers floors 0,1,3,5,7 (`WandererDungeonLiftAccess.cs:19-86`). The total floor count = `data.floors.Count` (not read; about 10 per the `FLOOR5_10` dialogue keys in `Citizen.cs:443`, inferred). The next floor goes through `HeroMerchant.OnFloorChange` → `StartDungeonGeneration(Wanderer, floor)` (:442-449).
- **Interdimensional portals inside normal dungeons**: `DungeonGenerator_PostprocessDungeonEvent` (:637-664). On levels 0 and 1 of a normal dungeon (any DLC owner), it takes holes-only rooms, shuffles them with `list.Sort((a,b) => Random.Range(0,1000))` (**a non-deterministic comparator**), and adds the special room `"WND_INTERMEDIATE"` as a secret room under the first eligible room (`IntermediateAdvisor` prefab). Its `HolePortalToIntermediateLevel` (IActionable) → `WandererDLCController.Instance.StartIntermediateDungeonGeneration(_culture, level, Wanderer)` (`HolePortalToIntermediateLevel.cs:55-58`) → an intermediate dungeon (`CreateIntermediateDungeonParent`, `DungeonType.Intermediate`, `<Culture>IntermediateDungeonData`; all rooms with enemies are set as dead ends, :306-338). Its `NextLevelDoor` continues to `intermediateCulture`'s next floor (`NextLevelDoor.cs:87-90`).
- Trick weapons: `TrickWeaponMaster` (item master), `Moonlighter.Weapons.TrickWeapon`, `TrickWeaponEffect`; `isDestroyedOnRunEnded` items are removed at the end of the run. They are awarded by `EnemyRandomSpawnerController` (`Moonlighter.DungeonGeneration/EnemyRandomSpawnerController.cs`): it spawns a mini-boss from a per-floor list (static `SpawnsUsedInTheRun`). On clear it spawns 1-2 chests with a random trick weapon (or its blueprint if not yet unlocked) and/or an amulet, and depends on whether the hero took damage (`HeroMerchant.OnDealedDamage`). Picking one despawns the other.
- Other DLC room objects: `WandererShopperSpawner`, `WandererHagglerSpawner`, `WitchPortal`, `WandererNotes`, `WandererHologramRoomSound`, `WandererEmblemPortal : EmblemPortal` (empty).

---

## 9. Minimap

- `MinimapPanel : Panel` at `HUDManager.Instance.minimap` (`HUDManager.cs:14`).
- `LoadDungeonGraphics(Vector2 size, List<DungeonRoom> rooms)` (`MinimapPanel.cs:107-156`) is called by the generators' `UpdateMinimapOnGeneratedDungeon` (cave size or room bounds). It builds `Dictionary<DungeonRoom, MinimapRoom> minimapGraphics` from each room's `x`, `y`, doors (`GetWest/East/North/SouthRoom`), `specialRoomIndex`, `isInit` and culture (corrupted).
- Updates:
  - `OnRoomEntered(room)` (from `EnterRoom`) reveals the room, sets the current colour and stores `lastRoom`;
  - `OnRoomExit`;
  - `Center(room)`;
  - `UpdateRoom(room)` (on clear);
  - `Reveal()` (all rooms; emblem load, map potion).
- `GetRoomIconSprites` (:376-446) reads **`DungeonManager.Instance.currentDungeonRoom == room` (Will icon)**, `room.portal`, `room is DungeonBossRoom`, `healingDungeonRoom`, `secretRoom.hasBeenVisited`, `roomChests[i].type`, `!room.AreAllEnemiesDefeated()`.
- `MinimapDetailPanel` (full map) mirrors `minimapGraphics` and reads `DungeonManager.dungeonRooms`/`chestCount`. Hook `OnMapChanged`.
- For remote players, add your own icon layer per `MinimapRoom` (inferred).

---

## 10. Camera

- `CameraManager` singleton (`static Instance`, `CameraManager.cs:96`, `:139-149`, set in `Awake`). One `Camera _camera`. Fields `public GameObject followTarget` (:69; written by `ChangeRoomTo` but **not read by `FollowTarget`**, which uses private `Transform target` set by `SetFollowTarget`, :107, :201-213, :283-306), `followForwardPlayer`, area clamp `AreaMin/MaxX/Y`.
- Normal rooms: the camera is **static per room**. `MoveCamera(to.transform, cb, clamp:false)` tweens to the room centre in 0.5 s (`:314-335`); `OnComplete` → `FinishedRoomTransitionMovement()`, which reads `DungeonManager.Instance.currentDungeonRoom` (:337-347).
- Boss rooms follow the hero inside an area: `DungeonBossRoom.CameraFollowPlayer()` → `SetArea(room pos, size)` + `SetFollowTarget()` (hero, `DungeonBossRoom.cs:378-382`). `ExitRoom` calls `StopFollowingTarget()`.
- Other moves: `TeleportCameraToPoint`, `MoveTo`, `Rumble`, `PlayPixelatedEffect`.
- Per-player current room: since each client has its own `CameraManager` and its own `DungeonManager.currentDungeonRoom`, keeping `currentDungeonRoom` = **the local hero's room** on every machine gives each player their own camera for free. Rooms are laid out on a 1120×720 grid, so the camera never shows a neighbouring room (inferred).

---

## 11. Global / single-hero / single-room assumptions (break points for co-op)

`DungeonManager.Instance.currentDungeonRoom` has **230 references in 75 files**. The important ones:

1. **Room entry/exit is global**: `ChangeRoomTo`/`SpecialChangeRoomTo` (`DungeonManager.cs:228`, `:299`) always exit the *current* room (enemies disabled, room deactivated after 0.5 s via `Invoke("DisableRoom")`) and enter the new one. A remote puppet's room must be kept active and simulated by your own code. Do not call `EnterRoom`/`ExitRoom` for puppets. Patch `DisableRoom`/`DisableEnemies` so a room occupied by any player stays active (inferred).
2. **`DungeonDoor.OnPlayerEnter`** uses `HeroMerchant.Instance` and requires `currentDungeonRoom == doorRoom` (`DungeonDoor.cs:57-69`). The static `OnLeavingRoomWithDoor` has one subscriber (`ChangeRoomTo`). The boss enter door hard-checks `player == HeroMerchant.Instance.gameObject` (`DungeonBossRoomEnterDoor.cs:45`).
3. **PolyNav is a single instance** (`PolyNav2D.current` / `DungeonManager.polyNavInstance`), repositioned to the current room on every change (`DungeonManager.cs:261-266`, `DungeonRoom.DisableRoom` :1548). Enemy pathfinding only works in the local hero's room. This fits "room owner simulates", but a machine cannot simulate two rooms at once.
4. **Enemies target `HeroMerchant.Instance`**: `EnemyBehaviour.heroMerchantTransform` is cached in `Init` (`EnemyBehaviour.cs:103`); `Wanderer.ChooseTarget` (:273); the death push direction (:625-630).
5. **`Start()` methods bind to `currentDungeonRoom`** instead of their parent room. They run when a room is first activated, so they bind to the wrong room if a puppet's room is activated while the local hero is elsewhere: `BreakableObjectBehaviour.Start` (`:59-65`, sets `enemy.enemyRoom`!), `Shrine.Start`, `FallingFloor.Start`, `TimedChestSlime.Start`, `EnemyWavesShrine.Start`, `CampGenerator.Start`, `AbandonedItemsGenerator.Start` (also checks `hasBeenVisited`), `WandererNestPedestal.Start`, `SkeletonNotes.Start`, `CultureNotes.Start`, `Mimic.Start`, `DungeonToHomeChest/NextFloorChest/ValueChest.Start` (add themselves to *current* `roomChests`), `NextLevelPortal.Start`, `UnlockUtilityCampGenerator.Start`, `EmblemPortal.Start`. Fix: activate rooms only for the local hero, or patch these to `GetComponentInParent<DungeonRoom>()` (inferred).
6. **Drops parent to `currentDungeonRoom`**, not the enemy's room: `Enemy.DropItemsOnDeath` (`Enemy.cs:251-293`), breakables (`BreakableObjectBehaviour.cs:184-227`), `FlyingRepairGolemBehaviour.cs:211`, inventory drops (`HeroMerchantInventory.cs:1310-1337`), `InventoryPanel.cs:360`, `SendItemToShopAction.cs:133`, `MotherGolemBehaviour.cs:230-235` (spawned children get `enemyRoom = currentDungeonRoom`), `PuddleAmuletEffect.cs:100`, `BladeTreeSproutFamiliar.cs:162`, `GoldMimicProjectile.cs:80`, `PileOfRocksProjectile.cs:254` (room bounds), `MoonlighterEffect.cs:157-160`.
7. **`HeroMerchant.Instance.IsInCombat`** is a single global set by `EnterRoom`/`ExitRoom`/`OnAllEnemiesDefeated` (`DungeonRoom.cs:1534`, `:1565`, `:2180`). It gates chests (`Chest.cs:742`), the emblem (`HeroMerchant.cs:751`) and `NextLevelDoor` (`NextLevelDoor.cs:52`). `CloseAllDoors` → `HeroMerchant.Instance...PlayWarningAnimationAfterTime`.
8. **Room-clear fan-out happens on whichever machine kills the last enemy**: `DungeonRoom.EnemyDefeated` → `OnAllEnemiesDefeated` → doors/chests. Remote machines must receive a "room cleared" message and call `EnemyDefeated`/`OpenAllDoors`/`UnLockRoomChests` themselves (inferred).
9. **`DungeonBossRoom.DestroyMenacingInstances()`** destroys every `Enemy` in the scene (`DungeonBossRoom.cs:504-520`). Boss sequences lock the input of `HeroMerchant.Instance`, move the single camera and play the key animation (`:232-316`, `:318-347`). `InternalOnBossDeath` sets the global `GameManager.bossDefeatedCulture`.
10. **Wanderer**: one `DungeonManager.wanderer`, spawns in `currentDungeonRoom`, chases `currentDungeonRoom` (`Wanderer.cs:83-130`). The timer is per machine (`Invoke`). Run it host-only and network it (inferred).
11. **Single portal slot** `GameManager._dungeonPortal` / `lastRoomWithPortal`, parented to `currentDungeonRoom` (`GameManager.cs:2712-2753`). The emblem save is per machine (`GameSlot.emblemDungeon`).
12. **Minimap** Will icon and `lastRoom` are tied to `currentDungeonRoom` (`MinimapPanel.cs:31`, `:380`).
13. **HealingArea** heals only `HeroMerchant.Instance` (`HealingArea.cs:371-383`, trigger tag `"Player"`). Puppets must not carry tag `"Player"` or they will trigger doors, holes, healing, the boss alert area and chests (inferred). The pool is shared per room, so sync the remaining `healingHealth`.
14. **Static generation state**: `DungeonGenerator.floorGenerationSaveInfo`, `DungeonGenerator.roomDatabase` (shared `DungeonRoomInfo` objects whose `weight` and `isTimedRoom` get mutated), `DungeonRoom.cacheLowerDetails`, `Chest.currentChestOppening`, `Chest.generatedAmuletsInCurrentDungeon`, `EnemyRandomSpawnerController.SpawnsUsedInTheRun`, `CommonRoomMiner.maxCommonMiners`.
15. **`DungeonManager.lastDoorUsed`** (single) is used for the wanderer spawn, the fall respawn (`TeleportHeroToLastDoorUsed`) and the rope.
16. **Secret rooms overlap their host room in world space** (same x,y, `DungeonGenerator.cs:706`). If both are active on one machine (a puppet in the secret room, the local hero in the host room) they render on top of each other. Hide the puppet's room (inferred).
17. `NormalSlimeBehaviour.cs:94` and `EnemyWarpSpawner.cs:115` gate logic on `currentDungeonRoom == enemy.enemyRoom`. `Mimic.Start` and `DungeonResultsPanel.cs:832-836` (killer lookup in the current room) do similar.
18. `DungeonBossRoomExitDoor.OnDoorClosed` bypasses `ChangeRoomTo` (no `ExitRoom`, no `OnHeroChangingRoom`, `EnterRoom` twice). Hook it separately for room-tracking messages.

Recommended hooks (Harmony): `DungeonManager.ChangeRoomTo`, `DungeonManager.SpecialChangeRoomTo`, `DungeonBossRoomExitDoor.OnDoorClosed`, `DungeonRoom.EnterRoom/ExitRoom/RoomPlaced/EnemyDefeated/OpenAllDoors`, `DungeonRoom.LoadGridFromInfo` (private), `DungeonRoom.SpawnEnemies/SpawnChests`, `Chest.GenerateChestContent/OpenChest/CloseChest`, `DungeonGenerator.GenerateDungeon` (static coroutine) and the `OnGeneratedDungeon` events of both generators, `DungeonManager.OnGeneratedDungeon/SpawnWanderer`, `NextLevelDoor.OnDoorClosed`, `HeroMerchant.LoadNextLevelDungeonScene`, `HeroMerchant.UseMerchantPendant/UseMerchantEmblem`, `GameManager.OpenPortalInDungeon`, `EmblemPortal.LoadDungeon`, `DungeonBossRoom.StartBossAwakeAnimation/InternalOnBossDeath/OnClosedBossChest`, static events `DungeonManager.OnHeroChangingRoom/OnEnterRoom/OnExitRoom`, `DungeonDoor.OnLeavingRoomWithDoor`, `HeroMerchant.OnFloorChange`, `GameManager.OnDungeonLoaded`.
