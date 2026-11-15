# Moonlighter 1.15.8: boot, scenes, saves, Steam, input, GUI, time

Source: ILSpy decomp of `Assembly-CSharp.dll` at `moonlighter/decomp/Assembly-CSharp`. All `File.cs:line` refer to that folder. "inferred" marks anything not read directly in code (e.g. scene contents set in the Unity editor).

---

## 1. Startup sequence, scenes, main menu, new game, tutorial

### Scenes
Scene paths are hard-coded in `GameManager`:
- `private string townScenePath = "Scenes/Town";` `GameManager.cs:140`
- `private string dungeonScenePath = "Scenes/Dungeon";` `GameManager.cs:142`
- `private string mainMenuScenePath = "Scenes/MainMenu";` `GameManager.cs:144`
- Build indices resolved in `Awake` with `SceneUtility.GetBuildIndexByScenePath` into private `int mainMenuSceneID, townSceneID, dungeonSceneID` `GameManager.cs:792-794`.
- A scene named `"Managers"` is loaded non-additively by `SplashScreenLogic.Start` `SplashScreenLogic.cs:22`, `IntroLoadScreen.Start` `IntroLoadScreen.cs:22` and `ReviewScreenBehaviour.LoadFirstGameScene` `ReviewScreenBehaviour.cs:22`. So build index 0 is a splash scene (inferred: the one containing `SplashScreenLogic`), and `Managers` is the persistent scene holding `GameManager`, `GUIManager`, `InControlManager`, etc. (inferred from `GameManager.Start` expecting `GUIManager.Instance` and `HUDManager.Instance` to already exist, `GameManager.cs:1010-1013`).
- `TestManagers` is used only by editor/test tools (`DungeonGenerationTester.cs:37`, `DungeonRoomEditorGUI.cs:2053`).
- Town, Dungeon and MainMenu are always loaded with `LoadSceneMode.Additive` on top of Managers (`GameManager.cs:1030, 1501, 1567, 1872, 1913, 1950, 1991`). The shop interior is part of the Town scene (no separate shop scene; `ShopManager` lives in Town, `GameManager.cs:1569`).

### Boot flow (Steam build)
1. Splash scene: `SplashScreenLogic.Start()` -> `SceneManager.LoadSceneAsync("Managers")` `SplashScreenLogic.cs:20-23`. (`IntroLoadScreen` is an alternate boot path that plays the intro itself; which one is in the shipped build is inferred to be `SplashScreenLogic`.)
2. `GameManager.Awake()` `GameManager.cs:788-872`: sets `Application.targetFrameRate = 240`, resolves scene IDs, inits Logitech/Razer SDKs, singleton check, then
   - `Platform.Init();` `:829`
   - `Platform.Achievements.Init(new MoonlighterAchievements());` `:831`, `AddExpansion(new MoonlighterAchievementsExpansion1())` `:833`
   - `Platform.DLC.Init();` `:835`
   - `ItemDatabase.Init()`, `DOTween.Init()` with `DOTween.defaultTimeScaleIndependent = true` `:842`, `EventDispatcher.ResetEvents()`, `_canSave = false` `:845`, `Constants.ReadDefaultFile()` (Resources `constants`), item DB, recipes, items asset bundle from `StreamingAssets/AssetBundles/items` `:858-861`, quests, enemy stats.
3. `GameManager.Start()` coroutine `private IEnumerator Start()` `GameManager.cs:997-1087`:
   - spins while `isTest` `:999`
   - `OptionsManager.ReadDefaultFile()` (reads `options.ini`) `:1003`
   - subscribes `InputManager.OnDeviceDetached` `:1004`
   - waits `while (!Platform.IsControllerInitialized)` `:1005` (blocks forever if your platform controller never reports Initialized)
   - `HUDManager.Instance.HideAll()`, `GUIManager.Instance.GoToDungeonLoadingPanel()` `:1010-1013`
   - `yield return StartCoroutine(FamiliarsManager.LoadBundle())` `:1014`
   - waits `SplashScreen.isFinished` (Unity splash) `:1015`
   - **Intro video**: `Resources.LoadAsync<GameObject>("Canvas/Canvas - Intro")`, instantiates, gets `VideoSequence`, `Prepare/SetCamera/Play`, waits while `_startSequenceMovie.isPlaying` `:1019-1028`
   - `SceneManager.LoadSceneAsync(mainMenuSceneID, LoadSceneMode.Additive)` `:1030`
   - `PreloadGameSlot()` (reads the save) `:1031`; on `_loadingGameSlotFailed` shows `GUI/INFO_CORRUPTED_SAVE` InfoPopUp and calls `LoadDefaultGame()` `:1032-1047`
   - waits MainMenu load, `GUIManager.Instance.GetComponent<PanelInstancer>().Load()`, waits `PanelInstancer.IsAnyoneLoading` false `:1048-1063`
   - disables the loading panel `:1064-1066`
   - `timeLastSave = (long)Time.time` `:1086`
   - Test branch (`isTest == true`, inspector field `public bool isTest;` `:108`): `HardResetSave(); ResetSlot(0);` instantiates `Resources "HeroMerchant"`, `HeroMerchant.Instance.Init()`, optional `testScene` additive load `:1068-1085`.

### Intro video skipping
- `GameManager.Update()` calls `CheckVideoSequenceSkip()` while `_startSequenceMovie` is set `GameManager.cs:1407-1410`.
- `public void CheckVideoSequenceSkip()` `:1089-1109`: if `GUIManager.Instance.input.Action1.WasReleased` -> `_startSequenceMovie.SkipCurrent()` (one video per press) then `DisableFor(1f)` disables the GameManager component for 1 s `:1111-1118`.
- `VideoSequence` `VideoSequence.cs`: `public bool isPlaying;` `:69`, `public event Action OnSequenceFinished;` `:75`, `public void Play()` `:152`, `public void SkipCurrent()` `:233`. When the last prefab ends: `isPlaying = false` `:216`.
- **Programmatic skip (clean)**: Harmony prefix on `VideoSequence.Play()` that sets `__instance.isPlaying = false`, invokes nothing else and returns false. `GameManager.Start` then proceeds immediately (`while (... _startSequenceMovie.isPlaying)` `:1025`), and `Update` will destroy it via `CheckVideoSequenceSkip` `:1106-1107`. `MainMenuPanel.Start` also waits on any `VideoSequence.isPlaying` `MainMenuPanel.cs:356-365`, so the same flag covers it. Alternative: postfix `Play()` and loop `SkipCurrent()`.

### Main menu
`MainMenuPanel : Panel` `MainMenuPanel.cs:15`.
- `public IEnumerator Start()` `:347-374`: waits splash; if not `GameManager.Instance.isMainmenuLoadedFromGame` it waits for the video, then `ShowGamepadGUI()` (a "gamepad recommended" card shown ~4.5 s, dismissable by any key `:405-447, 500-503`). Sets `GUIManager.Instance.currentInventory = this` `:373`.
- **"Press any key" door**: `private void Update()` -> `if (!_open && Input.anyKeyDown) OnAnyKeyPressed();` `:376-383` (raw `UnityEngine.Input`, not InControl). `public override bool OnAnyKeyPressed()` `:498-515` only opens the door when `InGameNewsViewer.importantNewsAlreadyShown` is true; then `PlayOpenAnimation()` plays sequence `_seq` `:554-571`. Also reachable through GUIManager's dispatch (`panel.OnAnyKeyPressed()` `GUIManager.cs:262`) when a GUIActions button fires.
- Gate: `public static bool importantNewsAlreadyShown;` `DigitalSun.Moonlighter/InGameNewsViewer.cs:91`. It is set true in `Start` when there is no cached news `:111-123` or after `ShowImportantNews()` `:441`. News are fetched over the network by `InGameNewsDataDownloader` (`InGameNewsManager.GetNews(GameId.Moonlighter)` `InGameNewsDataDownloader.cs:29`); option `GAME/InGameNews` (`InGameNewsViewer.cs:502`). For tests: set `InGameNewsViewer.importantNewsAlreadyShown = true` and/or set `OptionsManager.Set("GAME","InGameNews",0)`.
- Fields: `private bool _open;` `:81` (true once the door animation completed, `:265-268`), `private Sequence _seq;` `:91`, `public List<GameObject> mainMenuButtons;` `:32` (index 0 Continue, 1 New/Load game, 2 Options, 3 Exit (PC), 4 demo, 5 BuyDLC container; `PrepareMenuButtons` `:306-331`), `public StartGamePanel newGamePanel;` `:37`.
- `private bool HasLoadedGame` = `currentGameSlot.playedTutorial > 0 && seed != 0` `:105-120`.
- Button handlers (wired in Unity inspector, inferred): `public void StartGame()` (Continue) `:623-650`: requires `_seq.IsPlaying() || _open`, fades, `GUIManager.Instance.GoToDungeonLoadingPanel()`, then after **5 s** `GameManager.Instance.LoadSlot(GameManager.Instance.currentGameSlot)` `:640-647`. `public void NewGame()` `:664-674` opens `newGamePanel` (slot select). `OpenOptionsPanel()` `:676`, `ExitGame()` `:686`, `BuyDLC()` `:696`.
- Return key: `HandleReturnKeyPressed()` submits the selected Button `:385-403`.
- Static events: `OnOpeningMainMenuDoorsEvent`, `OnClosingMainMenuDoorsEvent`, `OnEnteringMainMenuScreenEvent`, `OnLeavingMainMenuScreenEvent` `:134-140`.
- `CheckContinueAndSelectFirstButton()` calls `GameManager.Instance.CheckDLCIntegrity()` and `ShowInfoMessagesIfNeeded` (DLC/binding popups) `:273-286`.

### Slot selection / new game / difficulty
`StartGamePanel : Panel` `StartGamePanel.cs:12`.
- `private enum State { Selecting, Copying, Erasing, SelectingNewGameType, SelectingDifficulty, SelectingBeatedGame }` `:185-193`.
- `Enable()` builds 5 `GameSlotGUI` from `GameManager.Instance.gameSaveData.gameSlots` (`InitSlots` `:1021-1077`).
- `private void GameSlot_OnClick(int)` `:1002-1019`: valid slot -> `StartGame(slot)`; empty -> new-game-type chooser -> `NewGame_OnClickEvent` -> difficulty chooser `:950-960`.
- Difficulty: `NormalDifficulty_OnClickEvent/Hard.../VeryHard...` `:978-994` do `OptionsManager.Set("GAME", "Difficulty", 0|1|2)` (difficulty is a **global option in options.ini, not in the slot**) then `StartNewNormalGame(_targetGameSlotGUI.Index)`. Read by `EnemyStats.cs:167`.
- `private void StartNewNormalGame(int index)` `:1457-1463`: `GameManager.Instance.ResetSlot(index)`; `gameType = GameSlot.GameType.Normal`; `StartNewGame(slot)`.
- `private void StartNewGame(GameSlot)` `:1088-1099`: `FadeBoth(GoToDungeonLoadingPanel, () => GameManager.Instance.LoadSlot(gameSlot), 0.8f)`.
- `private void StartGame(GameSlot)` `:1101-1119`: refuses when `gameSlot.hasInvalidDLC`; otherwise same fade + `LoadSlot`.
- NG+: `StartNewGamePlusGame` `:1465-1471`, `public static void UpdateSlotToNewGamePlus(GameSlot)` sets `playedTutorial = 3` `:1248-1251`.
- Erase/copy write the save immediately (`SerializeGameSlot` `:786-813`).
- Raw keys: Return submits, Escape = back `:549-573`.

**Programmatic new game (skip all menus)**: once `GameManager.Start` finished (MainMenu loaded, `PanelInstancer.IsAnyoneLoading == false`):
```
var gm = GameManager.Instance;
gm.ResetSlot(i);                                  // GameManager.cs:2223
var s = gm.gameSaveData.gameSlots[i];
s.gameType = GameSlot.GameType.Normal;
s.timeLastSave = DateTime.UtcNow;                 // so IsValid is true (GameSlot.cs:91-101)
s.playedTutorial = 4;                             // skip tutorial (see below)
s.townboardSaveInfo.UnlockBlacksmith(); s.townboardSaveInfo.UnlockWitch(); // what TutorialScene3.EndScene does
OptionsManager.Set("GAME","Difficulty",0);
GUIManager.Instance.GoToDungeonLoadingPanel();
gm.LoadSlot(s);                                   // GameManager.cs:2331
```
This is exactly what the debug UI does: `HUDDebug.DoGameSlotWindow` "Reset Slot"/"Skip Tutorial" `HUDDebug.cs:513-529`, `MoonlighterConsoleDebugPanel.OnFakeContinueButton` `MoonlighterConsoleDebugPanel.cs:806-811`. Note `LoadTownScene` unloads MainMenu itself `GameManager.cs:1546-1549`.

### Tutorial
- Flag: `public int playedTutorial;` `GameSlot.cs:20` (0 on new slot `:105`). Value = index of next tutorial scene; `4` means done (`TutorialManager.SCENE_COUNT = 4` `TutorialManager.cs:11`).
- Decision in `LoadSlotCoroutine`: `if (gameSlot.playedTutorial != 4) { LoadTutorial(gameSlot.playedTutorial); yield break; }` else `LoadTownScene()` `GameManager.cs:2439-2448`.
- `public void LoadTutorial(int scenes)` `GameManager.cs:2027-2032` instantiates `Resources "@TutorialManager"` and calls `TutorialManager.Start(int scene)` which loads `Resources "Tutorial/Scenes/Scene" + scene` `TutorialManager.cs:46-58`. Progress: `TutorialManager_OnTutorialSceneFinished(int scene)` -> `currentGameSlot.playedTutorial = scene + 1` `GameManager.cs:2040-2043`.
- Statics: `TutorialManager.OnTutorialSceneFinished (Action<int>)`, `OnTutorialFinished (Action)` `TutorialManager.cs:7-9`.
- Scenes: `TutorialScene0` = intro storyboard (skippable by holding `GUIActions.Action2` for `timeToPress=1f` `TutorialScene0.cs:285-330`); `TutorialScene1` = tutorial dungeon via `GameManager.LoadDungeonSceneFromTutorial(dungeonPrefab, Scene1, Scene1_1)` `TutorialScene1.cs:97-102`, sets `public static bool isWeaponActive` false until weapons are given `:44, :99, :199`; `TutorialScene2` = first shop day in town via `LoadTownSceneFromTutorial` `TutorialScene2.cs:80`, its `EndScene` sets `CanSave = true` and saves `:657-666`; `TutorialScene3` = dungeon entrances / golem dungeon 1, `EndScene` sets `bossDefeatedCulture = Merchant`, unlocks blacksmith+witch, `SerializeGameSlot()` `TutorialScene3.cs:471-488`.
- "Am I in the tutorial" at runtime: `UnityEngine.Object.FindObjectOfType<TutorialManager>() != null` (used by `PausePanel.Enable` `PausePanel.cs:111`), or `GameManager.Instance.currentGameSlot.playedTutorial != 4`. `TutorialScene1.isWeaponActive == false` blocks weapon swapping `HeroMerchantController.cs:1522`.
- Skip: set `playedTutorial = 4` before `LoadSlot` (plus Blacksmith/Witch unlock as above). Demo mode does this too `GameManager.cs:1216`.

---

## 2. GameManager: save data, load, save triggers, scene state, pause, game over

`public class GameManager : MonoBehaviour` `GameManager.cs:23`. Singleton `public static GameManager Instance` (lazy `FindObjectOfType`) `:180-190`, `private static GameManager _instance;` `:49`.

### Save data types
- `public class GameSaveData_V1 { public GameSlot gameSlot; public Dictionary<string,float> globalAchievementStats; }` `:25-31` (legacy, auto-upgraded by `UpgradeSlotToV2` `:1322-1338`).
- `public class GameSaveData_V2` `:33-41`:
  - `public GameSlot[] gameSlots = new GameSlot[5];`
  - `public Dictionary<string, float> globalAchievementStats`
  - `public InGameNewsSaveInfo inGameNewsSaveInfo` (`List<int> importantNewsShown, importantNewsOpen` `DigitalSun.Moonlighter/InGameNewsSaveInfo.cs:9-11`)
- Instance fields: `public GameSaveData_V2 gameSaveData;` `:45`, `public GameSlot currentGameSlot;` `:106`, `private string file = "gameslot";` `:103`.
- Serializer: FullSerializer via `FsJSONManager.Serialize/Deserialize` `FsJSONManager.cs:26-63`, compressed JSON, UTF-8 bytes `GameManager.cs:2586-2589`. All 5 slots live in **one file**.

### `GameSlot` (`[Serializable] public class GameSlot` `GameSlot.cs:5-157`) top-level members and who writes them
| member | type | written by (save hook / site) |
|---|---|---|
| `gameType` | `GameSlot.GameType {Empty, Normal, NewGamePlus, Custom}` `:8-16` | `StartGamePanel` `:1461, :1468` |
| `seed` | int | `GameManager.ResetSlotInfo` `GameManager.cs:2242` |
| `playedTutorial` | int (0..4) | `GameManager.cs:2042` |
| `dayTime` | `DayTime {Morning, Afternoon}` `DayTime.cs` | `TimeManagerV2.GameManager_OnSaveToDungeon/FromDungeon` `TimeManagerV2.cs:46-54` |
| `daysPassed` | int | `TimeManagerV2.ChangeToMorning` `TimeManagerV2.cs:80` |
| `achievements` | `AchievementsSaveInfo` (stats dict, kill counters, `willDeaths`, `totalPlayTime`, `weaponsDiscovered`, `crazyPeteNotesRead`, ngpStart_* `AchievementsSaveInfo.cs:7-83`) | `SaveSlotOnReturnFromDungeon` `GameManager.cs:2498-2511`, `SerializeGameSlot` `:2578` |
| `emblemDungeon` | `DungeonSaveInfo` (floor layout for emblem return) | `EmblemPortal.cs:210`, cleared `GameManager.cs:1811, 2086` |
| `itemsDiscovered` | `List<ItemDiscoverSaveInfo>` | `SaveCultureInfo` `:2544-2561` |
| `recipesSeen`, `enchantmentsSeen` | `List<RecipeDiscoverSaveInfo>` | `SaveCultureInfo` `:2562-2565` |
| `lastPrizePerItem` | `List<ShowcasePrizeSaveInfo>` | `SaveLastPrizePerItem` `:2524-2536` |
| `itemPriceInfo` | `ItemPriceManagerInfo` | `ItemPriceManager.GameManager_OnSaveToDungeon` `ItemPriceManager.cs:90-98` |
| `willInventory`, `willEquippedItems` | `List<ItemSaveInfo>` | `HeroMerchantInventory.GameManager_OnSave` `HeroMerchantInventory.cs:191-206` |
| `willWishlistedRecipes` | string | `HeroMerchant.OnSave` `HeroMerchant.cs:411` |
| `equippedWeaponSet` | int | `HeroMerchantInventory.cs:193` |
| `willGold` | int | `HeroMerchantInventory.cs:206` |
| `shopSaveInfo` | `ShopSaveInfo` (chests, helper/bed chest, showcases, decor, salesBox) | `ShopManager.GameSlot_OnSaveToDungeon` `ShopManager.cs:622-670` |
| `townboardSaveInfo` | `TownboardSaveInfo` (NPC unlocks, building levels) | many (`UnlockShop`, `UnlockBanker` `GameManager.cs:2492-2496`, `HeroMerchant.cs:623`) |
| `culturesDefeated` | int bitmask `1 << Culture` | `GameManager.cs:2491` |
| `maxDungeonLevelVisited` | `int[8]` | `DungeonGenerator.cs:246-253` |
| `willActiveQuests`, `completedQuests` | `List<ActiveQuest>`, `Dictionary<int,ActiveQuest>` | `HeroMerchant.OnSave` `HeroMerchant.cs:398-412` |
| `decorativeItemsDiscovered`, `familiarsDiscovered`, `equippedFamiliar`, `familiarEggs`, `mimicFamiliarChest` | lists/string/`ChestSaveInfo` | `FamiliarsManager.cs`, `WoodMimicFamiliar.GameManager_OnSave` `WoodMimicFamiliar.cs:471`, `DecorativePanel.cs` |
| `bankerSaveInfo` | `BankerSaveInfo {dayAccepted, moneyInvested, gathered}` | `BankerPanel.cs`, `BankerInteractive.cs` |
| `storiesSaveInfo` | `List<StorySaveInfo>` | storyteller |
| `dlc` | `List<DLCSaveInfo {id, nameKey, data}>` (Wanderer DLC state JSON in `data`) | `WandererDLCController.GameManager_OnSaveTo/FromDungeon` `Moonlighter.DLC.WandererDungeon/WandererDLCController.cs:386-402` |
| `timeLastSave` | `DateTime` | `SerializeGameSlot` `GameManager.cs:2583` |
| `wasDlcCompleted` | bool | DLC end |
| `[NonSerialized] hasInvalidDLC, invalidDLC, notFoundDLCs` | runtime only | `CheckDLCIntegrity` `GameManager.cs:1120-1151` |

`IsValid` = `seed != 0 && timeLastSave != DateTime.MinValue && gameType != Empty` `GameSlot.cs:91-101`.

### Load path
- `public void PreloadGameSlot()` `GameManager.cs:1186-1210`: new `GameSaveData_V2`, new `currentGameSlot`; if not `demoModeEnabled`: `Platform.CloudSave.Exists(file)` ? `LoadGameFromBytes(Platform.CloudSave.ReadBytes(file))` : `LoadDefaultGame()`; then `Platform.DLC.CheckOwnership(); CheckDownloadProgress()`.
- `public void LoadGameFromBytes(byte[] bytes)` `:1236-1304`: UTF-8 -> try V1 -> else `FsJSONManager.Deserialize(..., ref gameSaveData, false)` (retries with en-US culture) -> `currentGameSlot = GetMostRecentSlot()` or `HardResetSave()` -> `FixGameSlot()` each -> `BaseAchievementController.LoadStats(...)` -> `OptionsPanel.Load()` (bindings) -> clears `_loadingGameSlot`.
- `public GameSlot GetMostRecentSlot()` `:1306-1320`: max `timeLastSave` among valid slots.
- `public void HardResetSave()` `:2229-2238` (keeps news info), `public void ResetSlot(int index)` `:2223-2227` -> `ResetSlotInfo` (seed, gold `kInitGold`, `initialItems`, `initialEquippedItems`, `initialShopChestContent` inspector lists) `:2240-2329`.
- `public void LoadSlot(GameSlot gameSlot)` `:2331` -> `private IEnumerator LoadSlotCoroutine(GameSlot)` `:2336-2450`: adds DLC entries in category `"ITEMS"`; `currentGameSlot = gameSlot`; loads bundle `hero_merchant` and instantiates `Assets/ResourcesForBundles/HeroMerchant.prefab` `:2342-2347` (**the player object is created here**, persists in Managers scene); DLC controllers `OnSlotLoaded` + `Enable()`; copies kill counters into `HeroStatistics`; `Random.InitState(gameSlot.seed)` `:2421`; `ItemPriceManager.Init`, `CultureManager.Init`, `RecipeManager.Init`, `HeroMerchant.Instance.Init()`, `MoonlighterCalendar.Instance.Init(daysPassed)`, `FamiliarsManager.LoadResources()`; then tutorial or `LoadTownScene()` (+ `DepleteSlimeCoins`, `canUseMirrorShortcut = true`).

### Save path
- `[ContextMenu("Serialize")] public void SerializeGameSlot()` `:2568-2590`: pulls achievement stats, `currentGameSlot.timeLastSave = DateTime.Now`, serialize, `Platform.CloudSave.WriteBytes(file, bytes)`. **Does not check `CanSave`.** This is the single write point for the slot file.
- `public bool CanSave => _canSave && _notLoadingScene` (setter writes `_canSave`) `:216-230`. `_canSave` false at boot `:845` and on `ReturnToMainMenu` `:1474`; true after town load `:1611` and tutorial scene 2 end.
- `public void SaveSlotOnGoToDungeon(bool serialize = false)` `:2464-2481`: if `CanSave`: play time, fires `OnSaveToDungeon(currentGameSlot)`, `HUDManager.ShowSavingAnimation()`, optional serialize.
- `public void SaveSlotOnReturnFromDungeon(bool serialize = false)` `:2483-2522`: boss culture bits/unlocks, stats, fires `OnSaveFromDungeon`, optional serialize.
- `public void SaveAll()` `:2452-2462` (CanSave-guarded: prizes, culture info, both events, serialize).
- Events (instance unless noted): `event Action<GameSlot> OnSaveToDungeon` `:256`, `event Action<GameSlot> OnSaveFromDungeon` `:254`; subscribers: `HeroMerchant.cs:377-378`, `HeroMerchantInventory.cs:104-105`, `ItemPriceManager.cs:28`, `ShopManager.cs:546`, `TimeManagerV2.cs:29-30`, `WoodMimicFamiliar.cs:38-39`, `WandererDLCController.cs:188-189`.

**When the game saves (serialize to disk):**
| trigger | site |
|---|---|
| Entering a dungeon at floor 0 (door) | `LoadDungeonScene` `GameManager.cs:1752-1755` |
| Entering dungeon through emblem portal | `LoadDungeonSceneFromEmblemPortal` `:1745` |
| Returning to town (any reason: death, pendant, boss) | end of `LoadTownSceneFromDungeonCoroutine` `:2169` |
| Sleeping in bed | `Bed.ChangeDayTimeEffectFromBed` `Bed.cs:56-62` |
| Shop closes for the night | `ShopManager.FadeToNightAndCloseShopDefinitely` `ShopManager.cs:1093` |
| Items moved into shop chests | `ShopManager.cs:765-766` |
| Tutorial scene 2/3 end | `TutorialScene2.cs:663-665`, `TutorialScene3.cs:487` |
| Slot erase/copy/NG+ | `StartGamePanel.cs:791, 804, 813` |
| Important news seen | `InGameNewsViewer.cs:438, 516` |
| Wanderer DLC lift / dungeon | `WandererDungeonLiftAccess.cs:115`, `WandererDLCController.cs:719` |
| Credits from DLC | `CreditsPanel.cs:215` |
Note: dungeon floors > 0 do **not** save (`level == 0` check `:1752`). Quitting does not save (`OnApplicationQuit` only shuts down LED SDKs `:1397-1402`).

### Scene state helpers
- `public bool NotLoadingScene { get; set; }` backed by `private bool _notLoadingScene = true;` `:119, :204-214`. Set false at the start of every scene transition and true at the end (`:1539, :1610, :1853, :1948, :1968, :1990, :2016, :2047`; `DungeonLoadingPanel.Disable` sets it true `DungeonLoadingPanel.cs:~333`).
- `public bool IsTownSceneLoaded()` `:2192`, `public bool IsDungeonSceneLoaded()` `:2197-2200` (`SceneManager.GetSceneByBuildIndex(id).isLoaded`), `public bool IsWillInDungeon()` = dungeon loaded and town not `:2174-2181`, `IsWillInWandererDLCDungeon()` `:2183`.
- `public static IEnumerator WaitUntilDungeonSceneIsLoaded(Action callback)` `:2810` uses `private static Dictionary<int,bool> loadingOperations` `:154`.
- Other flags: `public bool isTownLoadedFromMainmenu = true;` `:121`, `public bool isMainmenuLoadedFromGame;` `:125`, `public ItemMaster.Culture bossDefeatedCulture = Developer;` `:123` (Developer = "none"), `public bool isSceneLoadedFromEmblemPortal;` `:172`, `public List<Moonlighter.DLC.IDLCController> dlcControllers` `:160`.
- Events: `public static event OnTownLoadStartedDelegate OnTownLoadStarted` `:244`, `public event Action OnTownLoadedFromMainMenu` `:246`, `public event Action OnTownLoaded` `:248`, `public event Action OnExitToMainMenu` `:250`, `public event Action<bool> OnDungeonLoaded` (arg = isTutorial) `:252`, `public static event Func<ItemMaster.Culture, IEnumerator> PrepareCinematicOnReturnFromDungeonEvent` `:258`, `public static event Action<bool> OnGamePaused` `:260`.
- Also `public void ReturnToMainMenu()` `:1461-1526` (unloads Town/Dungeon, **destroys HeroMerchant**, loads MainMenu additive, `PreloadGameSlot()` again re-reading the save from disk).

### Pause / timescale
- `public void PauseGame(float delay = 0f)` `:2592-2605`: `Time.timeScale = 0f; _isGamePaused = true;` fires `OnGamePaused(true)`, then (delayed) audio snapshot `Mixer_Pause`.
- `public void UnpauseGame()` `:2607-2617`: `Time.timeScale = 1f`, `_isGamePaused = false`, `OnGamePaused(false)`, `Mixer_Normal`.
- `public bool isGamePaused => _isGamePaused || Time.timeScale == 0f` `:232-242`.
- `SetGameSpeed(float)`, `IncreaseGameSpeed`, `DecreaseGameSpeed`, `SetGameSpeedForTime(float, float, bool)` (hit-stop) `:2619-2665`. Debug keys in `Update`: Alpha0 (+Shift/Ctrl) `:1436-1450` (not gated by debug flag!), Tab toggles `debug` `:1411-1422`.
- **Who pauses** (co-op gotcha: every one of these sets global `Time.timeScale = 0`): every inventory-like panel on Enable: `HeroInventoryPanel.cs:187`, `ChestInventoryPanel.cs:179`, `ShowcaseInventoryPanel.cs:93`, `SalesBoxInventoryPanel.cs:45`, `NotebookPanel.cs:322`, `WishlistPanel.cs:97`, `QuestPanel.cs:137`, `ShopResultsPanel.cs:44`, `CalendarPanel.cs:44`, `PausePanel.cs:101`, DLC panels; plus death `HeroMerchantController.PlayDeathAnimation` `HeroMerchantController.cs:3435-3449`, pendant `HeroMerchant.UseMerchantPendant` `HeroMerchant.cs:734`, `EmblemPortal.cs:153`. `HeroMerchantController.Update` skips all gameplay input while `isGamePaused` `HeroMerchantController.cs:1234`. `DungeonLoadingPanel.Disable` unpauses `DungeonLoadingPanel.cs:194-198`.

### Game over (death)
1. `HeroMerchantStats.HealthValueChanged` -> `Die()` `HeroMerchantStats.cs:123-153` sets `isDead`, calls `heroMerchantController.PlayDeathAnimation()` -> `PauseGame()`, `PreventPanelChange()`, animator trigger `"Death"` `HeroMerchantController.cs:3435-3449`.
2. Animation event -> `HeroMerchant.OnWillDeathAnimationEnded()` `HeroMerchant.cs:691-715` (Angel amulet revives instead) -> `HeroMerchant.OnDie` static event.
3. `DungeonManager.HeroMerchant_OnDie()` `DungeonManager.cs:203-212` -> `GUIManager.GoToResultsPanel(fromEmblem:false, overridePanel:true)`.
4. `DungeonResultsPanel.EndResult()` `DungeonResultsPanel.cs:728-801`: removes non-safe items; Replay (A) -> `SwapDayTime` + `OnReplayTriggered` -> `GameManager.LoadDungeonScene(culture, 0, replay:true)` `:124`; otherwise `GameManager.LoadTownSceneFromDungeon()` `:799` (which saves).
Pendant: `HeroMerchant.UseMerchantPendant()` `HeroMerchant.cs:717-743` -> results panel -> same return.

---

## 3. Save backends (brief; you replaced `Platform.Init`)
- `DigitalSunGames.Platforms.Platform` `DigitalSunGames.Platforms/Platform.cs`: private static singleton; static getters `Controller (IPlatformController)`, `CloudSave (IPersistenFilesController)`, `Achievements (IAchievementController)`, `FileSystem (IStreamingFilesController)`, `DLC (IDLCController)`, `StoreController (IStoreController)`, `public static SupportedPlatforms CurrentPlatform` `:23-47`; `IsControllerInitialized => m_Initialized && m_Controller.Initialized` `:49-59`. `Init()` is hard-wired to Steam (no platform switch in this build): `SteamManager`, `SteamCloudSaveController`, `CustomSteamAchievementController`, `StandaloneFileSystemController`, `SteamStoreController`, `SteamDLCController`, `CurrentPlatform = STEAM` `:61-81`. Private fields `m_Controller, m_CloudSave, m_Achievements, m_FileSystem, m_DLC, m_StoreController, m_Initialized` `:9-21`.
- `IPersistenFilesController` `IPersistenFilesController.cs:5-18`: `bool Exists(string)`, `bool WriteBytes(string, byte[])`, `byte[] ReadBytes(string)`, plus async variants (async ones never called by game code; grep).
- `SteamCloudSaveController : MonoBehaviour` uses `SteamRemoteStorage.FileExists/FileWrite/FileRead` `SteamCloudSaveController.cs:89-109`. There is **no local fallback**: with Steam off these calls fail/throw.
- `DRMFreeCloudSaveController` (unused but present) = `Application.persistentDataPath/<file>` via `File.*` `DRMFreeCloudSaveController.cs:9-31`; a ready-made local implementation to drop into `m_CloudSave`.
- File names written via `Platform.CloudSave`: `"gameslot"` (all slots) `GameManager.cs:103`, `"options.ini"` (`OptionsManager.cs:35, 141`; seeded from `StreamingAssets/options.ini` on first run `:40-46`), `"bindings"` and `"guiBindings"` (InControl base64 binding blobs; `OptionsPanel.cs:198, 232`, `OptionsControlTabContent.cs:281-282`). PlayerPrefs: `Language` (`Localization.cs`), `MusicMute`/`SoundMute` (`AudioManager.cs:188-221`) -> registry, shared by both instances.
- Redirect: replacing `m_CloudSave` covers everything; the funnel methods are also `GameManager.SerializeGameSlot()`, `PreloadGameSlot()`, static `GameManager.SaveOptions(string, byte[])/LoadOptions(string)/SaveBindings/LoadBindings/SaveGUIBindings/LoadGUIBindings` `GameManager.cs:742-786`, and `OptionsManager.ReadFile(string)` `OptionsManager.cs:38`.

## 4. Steamworks use (brief)
All direct Steamworks calls (grep `Steam*.`):
- `SteamManager.Awake` `SteamManager.cs:43-83`: `RestartAppIfNecessary(AppId_t.Invalid)`, `SteamAPI.Init()`; **on failure `Application.Quit()`** `:73-78`; also throws if a second instance Awakes (`s_EverInialized`) `:46-49`. `Start` loops `SteamAPI.RunCallbacks()` `:114-125`. `Initialized` property `:36`. `OnDestroy` -> `SteamAPI.Shutdown()`.
- `SteamCloudSaveController` (`SteamUtils.GetAppID`, `SteamRemoteStorage.*`) `:31-163`.
- `CustomSteamAchievementController`: `SteamUserStats.SetStat/StoreStats/SetAchievement/RequestCurrentStats/GetAchievement...` `:100-258`.
- `SteamDLCController`: `SteamApps.BIsDlcInstalled(appID)` `:44`, `GetDlcDownloadProgress` `:20`, `SteamApps.GetAppInstallDir` `:60`.
- `SteamStoreController.OpenStoreToDLC`: `SteamAPI.IsSteamRunning()`, `SteamFriends.ActivateGameOverlayToStore(new AppId_t(1121580u), ...)` `SteamStoreController.cs:8-10`.
- Overlay web pages from news: `InGameNewsPopup.cs:260-262`, `InGameNewsViewer.cs:517-519` (guarded by `SteamManager.Instance.Initialized`). Debug: `HUDDebug.cs:1122-1147` (`GetPersonaName`, `GetNumberOfCurrentPlayers`). No rich presence, no `BIsSubscribedApp`.
- DLC detection: `Platform.DLC.Init()` -> `BaseDLCController.Init()` -> `LoadFromDefaultPath()` `BaseDLCController.cs:70-76`; Steam version loads every file in `<install dir>/dlc` with `AssetBundle.LoadFromFile` and `RegisterDLC` `SteamDLCController.cs:57-75`; info read from bundle asset `*/info.asset` (`DLCInfo {internalId, displayNameKey, platform, platformId, minGameVersion, category}`) `BaseDLCController.cs:96-129`, `DLCInfo.cs`. `DLCHandle.IsValid = isVersionValid && isOwned && !isDownloading` `DLCHandle.cs:19-29`. Ownership = `BIsDlcInstalled(uint.Parse(info.platformId))` `SteamDLCController.cs:44`. Wanderer id string `"WANDERER_DLC"` (`MainMenuPanel.cs:178`, `WandererDLCController.cs:24`). Content: `WandererDLCController` loads nested bundles from TextAssets `Assets/_DLC/WandererDungeon/ResourcesForBundles/wanderer_dlc_town.bytes` / `wanderer_dlc_dungeon.bytes` via `AssetBundle.LoadFromMemory` `WandererDLCController.cs:124-176`; registered as `IDLCController` from GameObject tagged `dlc_controller` in `GameManager.CheckDLCIntegrity` `GameManager.cs:1154-1183`. The `DRMFreeDLCController` alternative loads from `Resources "dlc"` TextAssets and owns them if `platform == DRM_FREE` `DRMFreeDLCController.cs:7-27`; for a non-Steam build you would need a controller that sets `isOwned = true` for the Steam-platform DLC info (inferred).

## 5. Achievements
- Interface `IAchievementController` `IAchievementController.cs:5-18`: `Init`, `AddExpansion`, `GetStatValue`, `SetStatValue(string,float)`, `AddToStatValue(string,float)`, `UnlockAchievement(string)`, `ResetAchievementsAndStats`.
- `BaseAchievementController : MonoBehaviour` keeps `localStats`/`globalStats` dicts and unlocks via `UpdateAchievementProgress` -> `UnlockAchievement(id)` when progress >= 100 `BaseAchievementController.cs:228-235`; `UnlockAchievement` is empty in base `:224-226`.
- Steam impl `CustomSteamAchievementController : BaseAchievementController` overrides `SetStatValue`, `AddToStatValue`, `UnlockAchievement` (calls `SteamUserStats.SetAchievement` + `StoreStats` coroutine) `CustomSteamAchievementController.cs:97-160`; its `Start` loop checks every 10 s for `ALL_ACHIEVEMENTS` `:36-95`.
- Call sites: ~120 `Platform.Achievements.UnlockAchievement/AddToStatValue/SetStatValue` calls across 44 files (e.g. `GameManager.cs:2719`, `HeroMerchant.cs:708`). Databases: `MoonlighterAchievements` (57) `MoonlighterAchievements.cs:7`, `MoonlighterAchievementsExpansion1`, `MoonlighterAchievmentsWandererDLC` (added by `WandererDLCController.cs:186`).
- Block during tests: install a `BaseAchievementController` subclass (no-op `UnlockAchievement`) as `m_Achievements`, or Harmony prefix returning false on `CustomSteamAchievementController.UnlockAchievement/SetStatValue/AddToStatValue`. Keep it a `BaseAchievementController` because `LoadGameFromBytes`/`SerializeGameSlot` cast to it to persist stats `GameManager.cs:1292-1299, 2572-2580`.

---

## 6. Focus / background / single instance
- No `Application.runInBackground` anywhere in game code (grep). Default for standalone players is the Player Settings "Run In Background" flag (inferred: check `Moonlighter_Data/globalgamemanagers`; set `Application.runInBackground = true` from the plugin to be safe).
- `OnApplicationFocus` handlers: `GameManager.OnApplicationFocus` only sets `Cursor.lockState = Confined` `GameManager.cs:2802-2808` (also `InitiatePrototypeTechnicalProperties` confines cursor `:1383-1387`; annoying with two windows). `GUIManager.OnApplicationFocus/OnApplicationPause` reselect `firstSelectedGameObject` `GUIManager.cs:1212-1234`. `UtilityInventorySlot.OnApplicationFocus` `UtilityInventorySlot.cs:560`. None pauses the game or mutes audio.
- InControl: `InControlManager.suspendInBackground` (inspector, default false) `InControl/InControlManager.cs:18, 51`. If true, `InputManager.UpdateInternal` stops updating while unfocused `InControl/InputManager.cs:229` and focus loss clears state `:296-307`. Either way `KeyBindingSource` reads `UnityEngine.Input.GetKey` (`InControl/KeyInfo.cs:151-158`), which Unity only feeds to the focused window, so **keyboard input reaches only the focused instance**; XInput/native gamepads reach both (inferred: unfocused instances still read gamepads -> two copies react to one pad).
- Single instance: no mutex/process check. `SteamAPI.RestartAppIfNecessary` `SteamManager.cs:61` relaunches through Steam when not started from Steam (needs `steam_appid.txt` or your replacement). Steam itself allows only one running copy of an app per account (inferred: second instance's `SteamAPI.Init` may still succeed; you already replaced the layer).

## 7. Input

### Architecture
InControl 1.7.4 (`InControl/VersionInfo.cs:31-39`). Two `PlayerActionSet`s, both default-bound to keyboard **and** gamepad (they read `InputManager.ActiveDevice` unless `Device`/`IncludeDevices` is set `InControl/PlayerActionSet.cs:309-311, 367-386`):
1. Gameplay: `HeroMerchantActions : PlayerActionSet` `HeroMerchantActions.cs:5`, held in `InputActionsManager` (ScriptableObject singleton) `public HeroMerchantActions actions;` `InputActionsManager.cs:9`, `public static InputActionsManager Instance` `:11-30`. `HeroMerchantController.Init()` caches `inputDevice = InputActionsManager.Instance.actions;` `HeroMerchantController.cs:926`; field `public HeroMerchantActions inputDevice;` `:32`.
2. Menus: `GUIActions : PlayerActionSet` `GUIActions.cs:5`, created in `GUIManager.Awake`: `input = new GUIActions();` `GUIManager.cs:194`, field `public GUIActions input;` `:125`. Also used by gameplay for interact (`Action1`) and pause (`ButtonStart`).

Members (both sets, `PlayerAction` fields): `LeftStickUp/Down/Left/Right, RightStick*, DPadUp/Down/Left/Right, Action1..Action4, ButtonLeftStick, ButtonRightStick, LeftBumper, RightBumper, LeftTrigger, RightTrigger, ButtonSelect, ButtonStart`; `PlayerTwoAxisAction LeftStick, RightStick, DPad`; `HeroMerchantActions` also `Submit` `HeroMerchantActions.cs:7-61`.

### Action map (default bindings: `InputActionsManager.SetDefaultBindings` `InputActionsManager.cs:55-123`, `GUIActions.SetDefaultBindings` `GUIActions.cs:95-172`)
| game action | property read | where | keyboard / pad |
|---|---|---|---|
| move | `inputDevice.LeftStick.X/.Y` | `HeroMerchantController.Update` `:1260`, `UpdateMoveAnimations` `:2201, 2210-2214` | WASD (+DPad, LStick) |
| main attack | `inputDevice.Action1.WasPressed` | `:1339` | J / A |
| secondary attack (hold) / trick | `inputDevice.Action3.IsPressed / WasPressed` | `:1384-1436` | K / X |
| roll (ability) | `inputDevice.LeftTrigger.WasPressed` | `:1441` -> `UseAbility()` `:1663` | Space / LT |
| use potion | `inputDevice.RightTrigger.WasPressed` -> `ConsumeHotkeyItem()` | `:1516-1519`, `:3276` | E / RT |
| swap weapon | `inputDevice.LeftBumper/RightBumper.WasPressed` | `:1520` | Z, C / LB, RB |
| open inventory | `inputDevice.Action4.WasPressed` -> `GUIManager.GoToHeroInventoryPanel()` | `:1449-1472` | I / Y |
| pendant (hold) / mirror | `inputDevice.Action2.IsPressed/WasReleased` | `:1566-1645` (note: this block sits outside the `if (!isGamePaused)` block, so it runs while paused) | L / B |
| interact (tap / long press) | `GUIManager.Instance.input.Action1.IsPressed/WasReleased` | `:1473-1515` -> `InteractWithInstantActionableObject` `:2947` / `InteractWithLongPressActionableObject` `:2956` | J / A |
| pause | `GUIManager.Instance.input.ButtonStart.WasPressed` -> `GoToPausePanel()` | `:1561-1564` | Esc / Start (gameplay set also binds P) |
| minimap | `InputActionsManager.Instance.actions.ButtonSelect.WasPressed` | `GUIManager.cs:220` | M / Back |
| menu nav | `input.LeftStick/DPad/RightStick.HasChanged` -> `Panel.OnLeftStickMove(Vector2)` etc | `GUIManager.OnInControlInputProccessed` `GUIManager.cs:218-406` | WASD/arrows |
| menu submit | `input.Action1.WasPressed || WasRepeated` -> `panel.OnAButtonPressed(bool)` | `GUIManager.cs:245-246, 305-308` | J / A |
| menu cancel | `input.Action2.WasPressed` -> `panel.OnBButtonPressed()` | `:243, 287` | L, Backspace / B |
| menu X / Y / bumpers / triggers / start | `Action3`, `Action4`, `LeftBumper`, `RightBumper`, `LeftTrigger/RightTrigger.HasChanged`, `ButtonStart` | `:244-259` | K / I, Esc / Z,C / Q,E / Esc |
| uGUI buttons | `InControlInputModule.SubmitAction = input.Action1`, `CancelAction = input.Action2`, `MoveAction = InputActionsManager.Instance.actions.LeftStick` | `GUIManager.cs:195-201`; module `InControl/InControlInputModule.cs:62-66, 309-344` | |
| dialogue advance | `GUIManager.Instance.input.Action1.WasPressed` (Action2 closes) | see §8 | |

Raw `UnityEngine.Input` bypasses: main-menu door `Input.anyKeyDown` `MainMenuPanel.cs:378, 504`; Return/Escape in `MainMenuPanel.cs:387`, `StartGamePanel.cs:557, 569`; debug keys `GameManager.cs:1411, 1436-1446`, `HeroMerchant.cs:497-551` (gated by `IsDebugEnabled`), `HeroMerchantController.cs:1322-1336` (gated). The mouse also drives uGUI through `InControlInputModule` (`allowMouseInput`).

### InControl internals that matter
- Update order: `InControlManager.Update()` -> `InputManager.UpdateInternal()` (unless `useFixedUpdate`) `InControl/InControlManager.cs:98-104`; inside: device managers update, `devices[i].Update`, `Commit`, `UpdateActiveDevice`, then `UpdatePlayerActionSets` -> each `PlayerAction.Update(tick, dt, device)` `InControl/InputManager.cs:221-251, 503-510`. Execution order of `InControlManager` vs game scripts is set in editor (inferred: earlier, so game `Update`s see the fresh state).
- `PlayerAction.UpdateBindings` takes the max |value| over all bindings, `UpdateWithValue`, then `Commit()` `InControl/PlayerAction.cs:463-515`.
- State props on `OneAxisInputControl` (base of `PlayerAction`): `IsPressed`, `WasPressed` (`thisState && !lastState`), `WasReleased`, `WasRepeated`, `Value`, `HasChanged` are non-virtual property getters reading private `thisState/lastState` structs `InControl/OneAxisInputControl.cs:142-188`. `public bool UpdateWithState(bool, ulong, float)` / `public bool UpdateWithValue(float, ulong, float)` are public `:286-315`; `public void Commit()` `:359`.
- `PlayerTwoAxisAction` (`LeftStick`) is driven from its four component actions each tick (`twoAxisActions[k].Update` `PlayerActionSet.cs:339-343`).
- `InputManager.AttachDevice(InputDevice)` / `DetachDevice` public `InControl/InputManager.cs:446-480`; `InputDevice.Passive` public field `InControl/InputDevice.cs:29`; protected `UpdateWithState/UpdateWithValue(InputControlType, ...)` and public `UpdateLeftStickWithValue(Vector2, ulong, float)` `:493-519`. Shipped sample: `VirtualDeviceExample/VirtualDevice.cs` (subclass, `AddControl`, override `Update(ulong, float)`).
- `GameManager` reacts to `InputManager.OnDeviceDetached` with a "controller disconnected" popup `GameManager.cs:940-952`; avoid detaching your virtual device mid-game.

### Recommended injection points (per instance)
**A. Virtual InControl device (cleanest, covers gameplay + menus + uGUI):** subclass `InControl.InputDevice` like `VirtualDevice`, `AddControl` for `LeftStickLeft/Right/Up/Down, Action1..4, LeftTrigger, RightTrigger, LeftBumper, RightBumper, Start, Back, DPad*`, override `Update(ulong tick, float dt)` and call `UpdateLeftStickWithValue(v, tick, dt)` / `UpdateWithState(InputControlType.Action1, pressed, tick, dt)` from a per-instance script queue. `InputManager.AttachDevice(dev)` after `InputManager.IsSetup` (or in `InputManager.OnSetup`). Because both action sets bind `InputControlType` device bindings and default to `InputManager.ActiveDevice`, the device becomes active as soon as it reports input (`UpdateActiveDevice` picks latest input, `InputManager.cs:428-444`). To make it deterministic set `InputActionsManager.Instance.actions.Device = dev; GUIManager.Instance.input.Device = dev;` (`PlayerActionSet.Device { get; set; }` `PlayerActionSet.cs:197`) so a real pad/keyboard on the other window cannot steal it. Keyboard bindings still read `Input.GetKey` regardless of `Device` (`KeyBindingSource.GetState` ignores the device `InControl/KeyBindingSource.cs:41-44`), so for a headless test instance also block keys (see C) or simply keep that window unfocused.
   Gaps not covered by A: main-menu door uses `Input.anyKeyDown` (call `MainMenuPanel.OnAnyKeyPressed()` is not enough because it also checks `Input.anyKeyDown || base.OnAnyKeyPressed()` and `base` returns true if `receiveInput` -> it works when called directly with `receiveInput == true`; or call `PlayOpenAnimation` via reflection), Return/Escape raw handlers (harmless).
**B. Harmony on the action set (simplest, gameplay only):** postfix the getters `InControl.OneAxisInputControl.get_IsPressed / get_WasPressed / get_WasReleased / get_Value` filtered by `__instance == InputActionsManager.Instance.actions.Action1` etc. Works but these getters are hot (called from every panel) and do not feed `PlayerTwoAxisAction LeftStick` (its X/Y come from `TwoAxisInputControl` state, `PlayerTwoAxisAction.cs`), so you would also need `TwoAxisInputControl.get_X/get_Y` postfixes. More fragile than A.
**C. Feed the PlayerActions directly (not recommended):** `PlayerAction.UpdateBindings` (private) reads all bindings, calls `UpdateWithValue` and then `Commit()` in one go (`PlayerAction.cs:463-497`), so a postfix on `PlayerAction.Update` is too late (already committed) and a prefix cannot add a value. You would have to transpile `UpdateBindings`. Use A instead. To block real keyboard/pad for a test instance, prefix `KeyBindingSource.GetState`/`GetValue` (`KeyBindingSource.cs:33-44`) to return false/0.
**D. Bypass input entirely for puppets/remote players:** the gameplay script reads only `inputDevice` and `GUIManager.Instance.input`; for an AI/test driver you can also call `HeroMerchantController` public methods: `TeleportTo(Vector3)` `:868`, `MoveAutomaticallyToPoint(Vector3, float)` (used by debug key Y `:1324`), `UseAbility()` `:1663`, `ConsumeHotkeyItem()` `:3276`, `EnableMoveInput/DisableMoveInput` `:423-437`, `AllowMovement/ForbidMovement/StopMovement` `:566-580`. Movement gate flags: `public bool canMove` (`_canMove`) `:297-314`, `private bool _isMoveInputEnabled` `:79`, `attackInputEnabled` `:317-330`.

## 8. GUIManager, panels, dialogue, loading screens

`public class GUIManager : MonoBehaviour` `GUIManager.cs:14`; `public static GUIManager Instance;` (set in `Awake`) `:16, 183-192`.
- State: `public Panel currentInventory;` `:19` (the one "active" panel; also MainMenuPanel/StartGamePanel register themselves here), `public Panel currentPopUp;` `:21`, `CurrentPanel` (popup ?? inventory) `:159-173`, `CurrentInventory` (as `InventoryPanel`) `:143-157`, `public bool canOpenPanels;` `:115`, `public bool canOpenPause = true;` `:111`, `canOpenChests` `:113`, `panelChangedDuringInput`, `inInput`, `preventInputPropagation` `:105-109`, `public Image fade;` `:26`, `dungeonLoadingPanel`, `dayAfternoonPanel`, `pausePanel` `:30-86`.
- Event: `public static event Action<Panel> OnPanelChange;` `:181` (null on close), instance `OnFadeOut/OnFadeIn/OnFadeBoth` `:175-179`.
- Panels are lazily found with `GetComponentInChildren<T>()` on first `GoTo*`; the canvases are instantiated from Resources by `PanelInstancer` (`canvasPrefabs` list, plus DLC canvases via `IDLCController.LoadAsset`) `PanelInstancer.cs:59-149`; `public static bool IsAnyoneLoading` `:48`.
- Opening: `GoToHeroInventoryPanel()` `:714` (requires `canOpenPanels && !Fading && currentInventory == null`), `GoToTownboardShopPanel/StaffPanel`, `GoToBlacksmithPanel`, `GoToWitchPotionsPanel/EnchantmentPanel`, `GoToDiagramPanel`, `GoToShopResultsPanel(bool)`, `GoToRivalPanel`, `GoToHawkerPanel`, `GoToBankerPanel(bool)`, `GoToPausePanel()` `:874` (`canOpenPanels || canOpenPause`), `GoToChestPanel(Chest)` `:887` (ignores canOpenPanels), `GoToSalesBox(Chest)`, `GoToShowcasePanel(Showcase)`, `GoToDecorativePanel`, `GoToFamiliarsPanel`, `GoToMinimapDetailPanel(bool)`, `GoToResultsPanel(bool fromEmblem, bool overridePanel=false)` `:1032`, `GoToQuestPanel`, DLC haggle/wanderer panels `:1068-1196`. Common path: `MakeCurrent(Panel)` -> `DisableCurrentPanel()` then `currentInventory = panel; EnablePanel(panel)` -> `panel.Enable()` + `OnPanelChange` `:1236-1257`.
- Popups: `ShowOptionsPopUp()` `:408`, `ShowImageConfirmationPopUp(...)` `:425-489`, `ShowCreditsPopUp()` `:491`, `ShowImportantNewsPopup` `:501`, `HidePopUp()` `:532-543`.
- Closing: `public void DisableCurrentPanel()` `:1203-1210` (only if `currentInventory.IsActive()`; note it does **not** null `currentInventory`; the next `OnInControlInputProccessed` clears it when the panel is inactive `:235-239`), `DisableCurrentPanelAfterTime(float)` `:1198`.
- `public void AllowPanelChange()` / `PreventPanelChange()` just set `canOpenPanels` `:649-657`.
- Fades: `FadeOut(Action cb=null, float d=0.5f)` `:554`, `FadeIn(Action cb=null, float d=0.5f, bool activeHUD=true)` `:582`, `FadeBoth(Action in, Action out, float d=0.5f, bool activeHUDOnFadeIn=true)` `:610`; `Fading` property `:131-141`. DOTween, time-scale independent (global default `GameManager.cs:842`).
- Input dispatch: `Update()` -> `OnInControlInputProccessed()` maps `GUIActions` edges to the virtual handlers of `Panel` `GUIManager.cs:204-406`.
- `Panel : MonoBehaviour` `Panel.cs:4`: `public bool receiveInput; public bool blockInput;` `:12-14`, `protected bool _isActive`, `virtual Init/Enable/Disable` (content child named "Content" toggled after 0.25 s) `:20-59`, handlers `OnAButtonPressed(bool repeated)`, `OnAButtonReleased()`, `OnBButtonPressed`, `OnXButtonPressed`, `OnYButtonPressed`, bumpers, `OnLeftStickMove(Vector2)`, `OnRightStickMove`, `OnDPadChange`, `OnAnyKeyPressed`, triggers, `OnStartButtonPressed/Released` (base returns `IsActive() && receiveInput && !blockInput`) `:96-256`, `LostFocus/RegainFocus` `:258-272`. Tests can call these directly on `GUIManager.Instance.CurrentPanel`.
- `PausePanel` `PausePanel.cs`: `Enable()` pauses, sets `GUIManager.Instance.input.Enabled = false` during open anim `:93-119`; `ResumeGame()` `:203`, `GoToMenu()` (InfoPopUp confirm -> `ReturnToMainMenu`) `:185`, `ExitGame()` `:160`.
- `InfoPopUp : Panel` (modal confirm): `public static bool Active;` `InfoPopUp.cs:13`, `static Show(string, Action)`, `Show(string, Action<bool>)`, `Hide()` `:47-110`; reads `GUIManager.Instance.input.Action1/Action2.WasPressed` in its own `Update` `:270-285`. Auto-dismiss in tests: call `InfoPopUp.Hide()` or invoke `OnAButtonPressed(false)` on the instance.

### Dialogue
- Speech bubble: `CitizenDialog` (prefab `Resources "Citizen/CitizenDialog"`), `public static CitizenDialog conversationOpened;` `CitizenDialog.cs:13` (while set, Will's interact code is skipped `HeroMerchantController.cs:1474`), `ShowDialog(string, Sprite, Material, params KeyValuePair<string,string>[])` `:107`, `IsAnimatingText()` `:223`, `CompleteTextAnimation()` `:228`, `HideDialog(Action)` `:235`, `bool Active` `:73`.
- NPC conversations `I2Conversation : MonoBehaviour, IActionable` `I2Conversation.cs:8`: tokens split on `"(n)"` from I2 keys `I2KeyPrefix_N`; `LateUpdate` while `_active`: `GUIManager.Instance.input.Action1.WasPressed` -> `OnWillInteract()` (complete typing, else next token, else close), `Action2.WasPressed` -> close `:94-127`. Public: `OnWillInteract()` `:139`, `SpawnOneShotDialog(string)` `:191`, `HideDialog(bool)` `:300`.
- Scripted conversations: `ConversationUtils.StartConversation(string text, ActionTrigger, ...)` coroutine; advances on `GUIActions.Action1` or `Action2` `ConversationUtils.cs:14-65`. Tutorial/cutscene: `TutorialUtils.StartConversation(...)` + `DialogEndedChecker` (advances on `GUIActions.Action1.WasPressed` after 0.1 s) `TutorialUtils.cs:48-78`, `DialogEndedChecker.cs:14-31`. So pressing (edge) Action1 on the GUI set advances all dialogue; a virtual device pulsing Action1 one frame on / one frame off is the generic "skip dialogue".
- Loading screen: `DungeonLoadingPanel : Panel` `DungeonLoadingPanel.cs:9`; `public bool showHUDSOnDisable = true; public bool isEnabled;` `:17-19`; `DummyEnable()` (boot/town) `:39`, `Enable(bool toDungeon, GameObject prefab)` `:61`, `Enable(bool toDungeon, ItemMaster.Culture culture, int level=0, bool isTest=false)` `:91` (starts `DungeonGenerator.GenerateDungeon` and subscribes `DungeonGenerator.OnGeneratedDungeon` -> `DisableCurrentPanel`), `Enable(bool, DungeonSaveInfo, bool)` `:151`, `Disable()` `:192-336` (unpauses, fades, re-enables movement in dungeon, sets `canOpenPause/canOpenPanels`, `NotLoadingScene = true`). Opened via `GUIManager.GoToDungeonLoadingPanel(...)` overloads `GUIManager.cs:925-979` (all require `currentInventory == null`).

## 9. Scene travel API

### HeroMerchant (player) entry points `HeroMerchant.cs`
- `public void LoadDungeonScene(ItemMaster.Culture culture, bool istest = false)` `:583-592`: valid if `Golem..Unknown` (enum `Merchant=0, Golem, Forest, Desert, Tech, Unknown, Developer, Wanderer, ICT` `ItemMaster.cs:17-28`); `_currentDungeonLevel = -1`, `LoadNextLevelDungeonScene(culture, istest)`, trail. Wired to `WorldDungeonDoor.OnDungeonPortalActivate` (static `Action<Culture,bool>`) in `Init` `:370`, fired by the town dungeon doors `WorldDungeonDoor.cs:220-223`.
- `public void LoadNextLevelDungeonScene(ItemMaster.Culture culture, bool istest = false)` `:618-633`: `_currentDungeonLevel++`, unlocks rival on Golem floor 1, `GameManager.Instance.LoadDungeonScene(culture, _currentDungeonLevel, replay: true, istest)`, fires static `OnFloorChange(culture, level)`. Called by `NextLevelDoor.cs:89-93`, `NextLevelPortal.cs:31`.
- `LoadDungeonSceneFromIntermediateDungeon(Culture, bool)` `:594`, `LoadDungeonSceneFromEmblemPortal()` `:604-616`, `LoadDungeonSceneWithLastCulture()` `:635`, `public void LoadTownScene()` `:640-645` -> `GameManager.Instance.LoadTownSceneFromDungeon()`, `SwapScene()` `:659`, `SwapTestScene()` `:647` (Shift+T debug).

### GameManager
- `public void LoadDungeonScene(ItemMaster.Culture culture, int level, bool replay, bool isTest = false, bool fromEmblem = false)` `GameManager.cs:1750-1764`: if `level == 0` save (`SaveSlotOnGoToDungeon(serialize:true)`), reset pendant charge, start coroutine.
- `public IEnumerator LoadDungeonSceneCoroutine(...)` `:1766-1900`, step by step: LED colours; `loadingOperations[dungeonSceneID] = true`; clear `emblemDungeon` unless emblem; `ItemPriceManager.RegisterEndOfRun()`; `FadeOut` (wait); `UnpauseGame()`; `GoToDungeonLoadingPanel(toDungeon:true, culture, level, isTest)` (this **starts dungeon generation** once the Dungeon scene exists); `FadeIn` (wait); if `replay` unload enemy/obstacle/detail bundles of other cultures; unload Town (twice-guarded) + DLC town bundles; `DeleteAllDroppedItems()`; `_notLoadingScene = false`; minimap; DLC `LoadDungeonBundles()`; `LoadSceneAsync(dungeonSceneID, Additive)` if not loaded (a floor change keeps the Dungeon scene loaded and regenerates in place via `DungeonLoadingPanel.WaitUntilGenerationCanStart`, which destroys the old `DungeonManager` `DungeonLoadingPanel.cs:126-149`); `ForbidMovement/DisableMoveInput/ShowHeroMerchantArmor`; wait for `DungeonGenerator`, `PanelInstancer`, `generator.IsGeneratingDungeon == false`; fire `OnDungeonLoaded(false)`. Movement re-enabled in `DungeonLoadingPanel.Disable` out-callback.
- `public void LoadTownSceneFromDungeon(bool fromEmblem = false)` `:2045-2060` -> `FadeBoth(() => StartCoroutine(LoadTownSceneFromDungeonCoroutine(fromEmblem)), null, 0.5f, false)`; coroutine `:2062-2172`: `GoToDungeonLoadingPanel()`, wait 1 s realtime, delete drops/destroyables, unload dungeon asset bundles (`dungeon_common/golem/forest/desert/tesla/unknown`), unload Dungeon scene, `WaitUntilShopManagerExists(false)`, boss cinematics, death "spit" animation, `SaveSlotOnReturnFromDungeon(serialize:true)`, `DisableCurrentPanel()`.
- `LoadTownSceneFromEmblemPortal()` `:1738`, `LoadDungeonSceneFromEmblemPortal()` `:1743-1748`.
- `public void LoadTownScene()` (from main menu) `:1537-1552`: `_notLoadingScene = false`, `PreventPanelChange`, stop music, unload Dungeon/MainMenu, `WaitUntilShopManagerExists(fromMainMenu:true)`, `FreeResources`.
- `private IEnumerator WaitUntilShopManagerExists(bool fromMainMenu)` `:1561-1736`: DLC `LoadTownBundles`; `LoadSceneAsync(townSceneID, Additive)`; wait `ShopManager.Instance`, scene loaded; fire static `OnTownLoadStarted`; wait `SeasonManager.Instance.Finished`, `PanelInstancer`; `ShopManager.Instance.Init()`; `TimeManagerV2.Instance.Init()`; `DLCSpawner.SpawnDefault()`; camera; `_notLoadingScene = true; _canSave = true;` unpause; then branch: from main menu -> `OnTownLoadedFromMainMenu`, HUD init, emblem portal, `DisableCurrentPanel`, `FadeIn` + `ShopManager.PlayWillBedAnimation()` (player wakes in bed; control returns in `ShopManager.OnAnimationFinished` `ShopManager.cs:846-871`); from death/Unknown boss -> teleports to the culture's `WorldDungeonDoor`, fires `OnTownLoaded`; otherwise (pendant/return) -> emblem portal in town, `OnTownLoaded`. `TimeManagerV2.OnTownLoaded` subscribes `OnTownLoaded` and swaps day/night on every dungeon return `TimeManagerV2.cs:56-60`.
- Tutorial variants: `LoadDungeonSceneFromTutorial(GameObject, Action, Action)` `:1931-1969`, `LoadDungeonSceneFromTutorialWithPortalAnimation(GameObject, Action)` `:1902`, `LoadTownSceneFromTutorial(Action, Action)` `:1971-2019`.
- `ReturnToMainMenu()` `:1461` (see §2).
- `MoveGameObjectToTown(GameObject)` / `MoveGameObjectToDungeon(GameObject)` (internal) `:1369-1377`: useful for parenting puppets to the right scene so they get unloaded.
- Persistent objects across scene changes: `HeroMerchant` (instantiated into Managers scene in `LoadSlotCoroutine`), GUI/HUD, `GameManager`. Puppets you spawn should live in Managers (`DontDestroyOnLoad` not needed since Managers is never unloaded; inferred).

## 10. Time / day-night / calendar
- `public class TimeManagerV2 : MonoBehaviour` `TimeManagerV2.cs:4`, singleton `Instance` (lazy `FindObjectOfType`) `:10-20`; lives in the Town scene (inferred: `GameManager` calls `TimeManagerV2.Instance.Init()` only after the Town scene loads `GameManager.cs:1591`, and `HeroMerchant` guards `if ((bool)TimeManagerV2.Instance)` `HeroMerchant.cs:485`).
- `private DayTime currentDayTime;` `:8`; `public DayTime CurrrentDayTime` (sic, triple r) `:22`; `public event Action<DayTime> OnDayTimeChange;` `:24`.
- `Init()` loads `currentGameSlot.dayTime` and fires the event `:37-44`. No real-time clock: `Update()` is empty `:33-35`. Day/night is a 2-state toggle.
- `public void SwapDayTime()` `:63-74`; `public void ChangeToMorning()` `:77-86` (`MoonlighterCalendar.AddDay()`, `currentGameSlot.daysPassed = MoonlighterCalendar.TotalDays`); `public void ChangeToAfternoon()` `:89-96`.
- Callers: on every town load from dungeon (`OnTownLoaded` -> `SwapDayTime`) `:56-60`; bed sleep `Bed.cs:60`; death+replay `DungeonResultsPanel.cs:786`; `ShopManager.OpenShop` path `ShopManager.cs:1146` (opening shop -> afternoon), boss end `GameManager.cs:2157-2160`, tutorial/DLC cinematics. Subscribers to `OnDayTimeChange`: `CameraManager.cs:184`, `CitizenGenerator.cs:130`, `MorningNightFilter.cs:49`, `NightTownInstanceHandler.cs:19`, `ShopManager.cs:549`, `HeroMerchant.cs:379`, `FamiliarsYard.cs:43`, `TreeBehaviour.cs:23`, `Roomba.cs:21`, etc.
- Semantics (inferred from flow): Morning = shop can open (daytime); "Afternoon" is used as night/after-shop; going to the dungeon from morning returns at Afternoon, from afternoon returns next Morning (day++).
- Calendar: `public class MoonlighterCalendar : ScriptableObject` `MoonlighterCalendar.cs:3`, `Instance` `:35-53`, statics `Day, Month, Year, TotalDays, DayOfTheWeek, Week, IsSunday` `:55-71`; 12 months x 28 days, 336/year `:27-33`; `Init(int daysPassed)` `:80-96` (called from `LoadSlotCoroutine` `GameManager.cs:2436`), `static AddDay()` `:98-113`. `CalendarPanel` pauses the game on open `CalendarPanel.cs:44`.

---

## Gotchas for co-op / two-instance harness (summary)
- Every menu pauses globally via `Time.timeScale = 0` (§2); host-authoritative sim needs a patch on `GameManager.PauseGame` (or per-panel) when a remote player is connected.
- Single `HeroMerchant.Instance` singleton is assumed everywhere (`FindObjectOfType` fallback `HeroMerchant.cs:91-101`; duplicate `Awake` destroys itself `:330-340`): puppets must not carry the `HeroMerchant` component.
- `WorldDungeonDoor.OnDungeonPortalActivate` is a static delegate; any puppet walking into a door trigger would start a load (inferred).
- Save file is one blob for 5 slots; two instances pointing at the same backend overwrite each other.
- Keyboard reaches only the focused window; gamepads reach both (InControl native/XInput). Pin each instance's action sets to its own (virtual) device via `PlayerActionSet.Device`.
- `Cursor.lockState = Confined` on focus `GameManager.cs:2802-2808`.
- `GameManager.Update` Alpha0 changes timescale with no debug gate `GameManager.cs:1436-1450`.
- Boot blocks on `Platform.IsControllerInitialized`, `SplashScreen.isFinished`, intro video, and the main-menu door waits for `InGameNewsViewer.importantNewsAlreadyShown` (network).
