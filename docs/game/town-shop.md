# Moonlighter 1.15.8 — Town and Shop map (for the co-op mod)

Source: ILSpy output in `decomp/Assembly-CSharp`. Every claim cites `File.cs:line` (root folder unless a namespace folder is given).
Anything not read directly in code is marked **(inferred)**. All RNG in this area is `UnityEngine.Random` (global, unseeded).

Contents: 0 Quick facts · 1 Town scene/areas · 2 Shop · 3 Customers · 4 Mirroring a customer · 5 Day/night · 6 Town NPCs · 7 Chests · 8 Save data · 9 Tutorial/cutscenes · 10 Single-hero assumptions

---

## 0. Quick facts and top gotchas

| Fact | Where |
|---|---|
| Town is ONE additive scene (`"Scenes/Town"`, loaded by build index `townSceneID`); the shop interior is a prefab `Resources "Shop/Shop_Lvl{n}"` instantiated into it | `GameManager.cs:140,1567`, `ShopManager.cs:127-150` |
| Moving between areas = fade + `HeroMerchantController.TeleportTo` + camera area change + **moving the single PolyNav2D map** | `ShopEntranceDoor.cs:293-355`, `TravelToDungeonZone.cs:27-47`, `TravelToVillageZone.cs:87-104` |
| Customers are `Agent`s ticked by `AgentSystem` on **`Time.realtimeSinceStartup`** in fixed 0.04 s steps (not `Time.timeScale`) | `AgentSystem.cs:21,86-110` |
| The customer state machine advances on **bubble Animator state-exit callbacks** (`BubbleAnimationEndedMessage`) | `VisitorAnimations.cs:85-96,372-438`, `VisitorMind.cs:245-249,881-909` |
| Many panels call `GameManager.Instance.PauseGame()` (`Time.timeScale = 0`): showcase, chest, sales box, quest, results, notebook, inventory… | `ShowcaseInventoryPanel.cs:93`, `QuestPanel.cs:137`, `ShopResultsPanel.cs:44`, `GameManager.cs:2592-2595` |
| Shop-day timer is a DOTween `DOVirtual.DelayedCall(num2, ChangeTimeAndCloseShop, ignoreTimeScale: false)` | `ShopManager.cs:1158` |
| Closing the shop always ends in `TimeManagerV2.Instance.ChangeToAfternoon()` | `ShopManager.cs:1139-1147` |
| Returning from a dungeon **toggles** day/night (`OnTownLoaded` → `SwapDayTime`) | `TimeManagerV2.cs:56-60` |
| Gold from a sale goes straight to `HeroMerchant.Instance.addGold(...)` | `CashPoint.cs:113` |
| Thief is caught when a `"Player"`/`"ActionArea"` collider enters `ThiefCatcher` while the local hero's `Ability1` or `Ability2` `.Ability.isInUse` (the roll) | `ThiefCatcher.cs:12-17` |

---

## 1. The Town scene and its areas

### 1.1 One scene, several areas
- `GameManager.LoadTownScene()` unloads dungeon/main-menu scenes and starts `WaitUntilShopManagerExists(fromMainMenu: true)` — `GameManager.cs:1537-1552`.
- `private IEnumerator WaitUntilShopManagerExists(bool fromMainMenu)` — `GameManager.cs:1561`: loads DLC town bundles (1563-1566), `SceneManager.LoadSceneAsync(townSceneID, LoadSceneMode.Additive)` (1567), waits for `ShopManager.Instance` (1569), fires `GameManager.OnTownLoadStarted` (1578-1581), then `ShopManager.Instance.Init()` (1590), `TimeManagerV2.Instance.Init()` (1591), `DLCSpawner.SpawnDefault()` for each spawner (1592-1600).
- Events: `public static event OnTownLoadStartedDelegate OnTownLoadStarted;` `public event Action OnTownLoadedFromMainMenu;` `public event Action OnTownLoaded;` — `GameManager.cs:244-248`. `OnTownLoadedFromMainMenu` fires for a load from the menu (1630-1633); `OnTownLoaded` fires for the return-from-dungeon branches (1619-1622, 1687-1690, 1712-1715).
- Areas inside the Town scene (all in the same world space, separated by distance):
  - **Village outdoors**: `environment.transform.Find("VillageOutdoors")` → `_townBB2D` — `ShopManager.cs:682`. Camera clamp `CameraManager.SetTownArea()` = `SetArea(-1060f, 242f, -988f, 75f)` — `CameraManager.cs:266-269`.
  - **Dungeon-gates area**: `public Bounds dungeonEntranceBounds;` → `_dungeonEntranceBB2D` — `ShopManager.cs:39,683`. Camera clamp `SetDungeonEntrancesArea()` = `SetArea(1566f, 2435f, -512f, 512f)` — `CameraManager.cs:271-274`.
  - **Will's shop interior** (shop floor + back room with bed and storage chests): the instantiated `_shop` prefab, `_shopBB2D = new BoundingBox2D(_shop.transform)` — `ShopManager.cs:145,684`. Two fixed camera points `shopCameraPoint` / `storageCameraPoint` — `ShopReferences.cs:7-9`. The bedroom is the "storage" side: wake-up teleports the camera to `storageCameraPoint` (`GameManager.cs:1602-1606`) and the bed is spawned at `references.bedSpawnPoint` (`ShopManager.cs:325`). **(inferred: bedroom == storage room.)**
- Gotcha: `CameraManager.SetArea(float minX, float minY, float maxX, float maxY, ...)` uses its parameters as (minX, **maxX**, **minY**, maxY) — `CameraManager.cs:230-235`. So the town area is X −1060…242, Y −988…75.

### 1.2 How the hero moves between areas (no scene loads)
| Transition | Class / method | What it does |
|---|---|---|
| Village → shop | `ShopManager.villageEntranceDoor` (`ActionTrigger`) listener `references.shopEntranceDoor.OpenDoorFromOutside` (`ShopManager.cs:689-690`) → `ShopEntranceDoor.OpenDoorFromOutside()` (`ShopEntranceDoor.cs:144-158`) → `GoToShop(bool tutorial = true)` (`:333-355`) | `TownManager.Instance.ThrowOnGoToShopEvent()`, `TeleportTo(references.shopEntrancePoint.position)`, `CameraManager.StopFollowingTarget()`, `HeroMerchant.ChangeToShopMode()`, `ShopManager.PlacePolynavInShop()`, after 0.5 s `TeleportCameraToShop` → `TeleportCameraToPoint(shopCameraPoint, clamp:false)` + `FadeIn` (`:357-371`) |
| Shop → village | `ShopEntranceDoor.ExitShop()` (`:85-103`, only if `ShopManager.Instance.IsShopClosed`) → `GoToVillage()` (`:293-331`) | `TeleportTo(VillageManager.Instance.willShopOutdoorEntrancePoint)`, `HeroMerchant.ChangeToDungeonMode()`, familiars `OnChangeToTownArea()`, `PlacePolynavInTown()`, `SetTownArea()`, `SetFollowTarget()` |
| Village → dungeon gates | `TravelToDungeonZone.OnTriggerEnter2D` (tag `"Player"`) → `GoToDungeonEntrance(bool iscinematic = false)` — `TravelToDungeonZone.cs:9-47` | `TownManager.ThrowOnGoToDungeonsEntrancesEvent()`, stores `VillageManager.Instance.lastVillageEntrancePoint = reeturnPoint`, `TeleportTo(VillageManager.Instance.dungeonEntrancePoint)`, `SetDungeonEntrancesArea()`, `PlacePolynavInDungeonEntrance()` |
| Dungeon gates → village | `TravelToVillageZone.OnTriggerEnter2D` (tag `"Player"`) → `GoToVillage(bool iscinematic = false)` — `TravelToVillageZone.cs:70-122` | `TeleportTo(lastVillageEntrancePoint ?? villageEntrancePointEast)`, `PlacePolynavInTown()`, `SetTownArea()` |
| Shop floor ↔ storage/bedroom | `DoorToStorageCameraSwap` polls every 0.1 s `Vector3.Distance(transformPlayer.position, transform.position) < 50f` and calls `MoveCameraToStorage()` / `MoveCameraToShop()` by comparing Y — `DoorToStorageCameraSwap.cs:8-26,40-48` | Hero simply walks; only the camera tweens between the two points |
| Anti-escape | `ReturnWillToShop.OnTriggerStay2D`: any collider tagged `"Player"` gets `transform.position = safePoint.transform.position` — `ReturnWillToShop.cs:12-18` | |

`VillageManager` (`public static VillageManager Instance;` `VillageManager.cs:7`) holds the anchor objects: `willShopOutdoorEntrancePoint`, `dungeonEntrancePoint`, `villageEntrancePointEast`, `lastVillageEntrancePoint` (`:29-35`) and the NPC stall objects (`Witch`, `Blacksmith`, `Rival`, `Banker`, `Hawker` + `Blocked*`, `:9-27`).

`TownManager` (`public static TownManager Instance;` `TownManager.cs:6`): `public event Action OnGoToShop;` `public event Action OnGoToDungeonsEntrances;` (`:11-13`) — good hook points for "local player changed area".

### 1.3 Camera per area
- `CameraManager.Instance` (`CameraManager.cs:139`); follow target defaults to the hero: `SetFollowTarget(Transform _target = null)` (`:201-213`), `StopFollowingTarget()` (`:223-228`), `TeleportCameraToPoint(Vector3 position, bool clamp = true)` (`:405-417`), `MoveTo(Vector3 position, float duration, bool kill = false, bool _followingTarget = false, bool clamp = true)` (`:349`).
- In the shop the camera does **not** follow the hero (`ShopEntranceDoor.cs:344`); outdoors it follows and is clamped by `SetTownArea`/`SetDungeonEntrancesArea`.
- `CameraArea.OnTriggerEnter2D` (tag `"Player"`) sets `CameraManager.Instance.SetArea(_area, updatePosition: false)` and `SetFollowTarget(HeroMerchant.Instance.transform)` — `CameraArea.cs:21-28`.

### 1.4 Who knows "where the hero is"
- `ShopManager.isWillInShop` (property over `public bool _isWillInShop = true;`) — `ShopManager.cs:73,95-111`. Setter runs `OnWillInShopChanged`, `CheckHelperInstance()` and `CameraManager.Instance.RefreshDayTime()`. Set by `PlacePolynavInShop/InTown/InDungeonEntrance` (`:895-936`).
- `HeroMerchant.currentMode` (`public HeroMerchantMode currentMode = HeroMerchantMode.ShopMode;` `HeroMerchant.cs:39`; enum `DungeonMode, ShopMode` `:21-25`). Inside the shop = `ShopMode`; village and dungeon gates = `DungeonMode` (`ShopEntranceDoor.cs:297,345`).
- There is no "current area" enum; village vs dungeon-gates is only distinguishable through the last `TownManager` event, the camera area, or position vs `dungeonEntranceBounds` **(inferred)**.
- **PolyNav gotcha**: there is one nav map `_polynav = PolyNav2D.current` (`ShopManager.cs:685`) that is physically moved/resized to the area where Will is: `PlacePolynavInBB` sets `masterCollider` size and calls `GenerateMap(generateMaster: true)` (`:938-942`). Citizens, customers, the helper and cutscene agents all path on it, so NPCs in an area where the *local* hero is not have no nav map.

---

## 2. The shop

### 2.1 `ShopManager` (scene singleton)
`public class ShopManager : MonoBehaviour` — `ShopManager.cs:9`. `public static ShopManager Instance;` (`:21`, set in `Awake` `:545`, nulled in `OnDestroy` `:569`).

Important fields:
| Field | Line |
|---|---|
| `public VisitorGenerator visitorGenerator;` | 23 |
| `private Queue<Agent> visitorsInQueue` / `private Queue<Agent> questVisitorsInQueue` | 25, 27 |
| `public ShopReferences references;` (per-shop-prefab references) | 31 |
| `public ActionTrigger villageEntranceDoor;` | 33 |
| `public ShopStats shopStats;` | 51 |
| `public Transform willStartPoint; public Transform bedChestStartPoint; public Bed bed; public Animator bedAnimator; public Chest bedChest; public Animator cashAnimator;` | 54-69 |
| `private static SpeedEffect shopSpeedEffect;` | 79 |
| `public int shopLevel;` | 81 |
| `public List<ShowcaseSlot> itemPoints;` (flattened list of every shelf slot) | 83 |
| `public bool canOpenShop = true;` | 85 |
| `private Tween changeTimeAndCloseCall;` | 87 |
| `public bool IsShopClosed { get; private set; }` / `IsShopWaitingForResult` / `IsWaitingToCloseShop` / `IsShopInited` | 89-93, 113 |

Events: `public static event OnWillWakeAnimationEndedDelegate OnWillWakeAnimationEnded;` `public static event Action OnShopOpen;` `public static event Action OnShopClose;` `public event Action OnShopInited;` — `ShopManager.cs:115-121`.

`ShopReferences` (on the shop prefab) — `ShopReferences.cs:7-45`: `shopCameraPoint`, `storageCameraPoint`, `shopEntrancePoint`, `CashPoint shopCash`, `QuestPoint shopQuest`, `Transform shopExit` (customer spawn AND leave point), `shopHelperStartPoint`, `Chest shopHelperChest`, `ShopEntranceDoor shopEntranceDoor`, `List<Showcase> shopShowcases`, `List<TargetPoint> queuePoints`, `queueQuestPoints`, `interestPoints`, `List<DecorativeSlot> shopDecorativeSlots`, `List<Chest> shopChests`, `Chest salesBox`, `bedSpawnPoint`, `cashMachineSpawnPoint`, `cashTableOrderer`.

`public void Init()` — `ShopManager.cs:675-697`: creates `shopStats`, `LoadShopFromSave(currentGameSlot)`, builds the three bounding boxes, hooks the village door, `RecoverRescuedItems()`, sets `IsShopInited`, fires `OnShopInited`.

Statics pushed into customer minds by `RefreshItemPositions()` — `ShopManager.cs:524-541`: `CommonVisitorMind._showcasePoints = itemPoints; CommonVisitorMind._interestingPoints = references.interestPoints; CommonVisitorMind._salesBoxPoint = new ChestVisitorSlot{...}`.

### 2.2 Showcases / shelves
- `public class Showcase : MonoBehaviour, IActionable` — `Showcase.cs:7`. `enum Type { Slot1x1 = 1, Slot2x1, Slot1x3, Slot2x2 }` (`:9-15`; the int value is the slot count, used by `ShowcaseInventoryPanel.InitShowcaseGrid` `ShowcaseInventoryPanel.cs:388`). Fields `public bool isFancy; public Type type; public List<ShowcaseSlot> itemPositions; public bool blocked;` (`:17-23`).
- `public void Open()` — `Showcase.cs:58-75`: only if `HeroMerchant.Instance.currentMode == HeroMerchant.HeroMerchantMode.ShopMode`; freezes the hero if the showcase has an Animator, then `GUIManager.Instance.GoToShowcasePanel(this)`. No code caller: it is wired through the `ActionTrigger.onExecuteAction` UnityEvent in the prefab **(inferred)**.
- `public void SetItemInPosition(ItemStack stack, int index)` — `Showcase.cs:85-109`: hides the old stack if `stack == null`, assigns `itemPositions[index].itemStack`, calls `itemPositions[index].SetItem(stack)`.
- `[Serializable] public class ShowcaseSlot` (plain class, not a component) — `ShowcaseSlot.cs:7`. Fields: `public ItemStack itemStack; public Transform itemPosition; public TargetPoint point; public Transform priceLabelsParent; public RectTransform priceLabelsRectParent; public Text textPrice; public bool isCool; public bool canPick = true; public SpriteDepthOrderRender spriteDepthOrder;` (`:9-25`). `public event Action OnItemRetrieved;` (`:29`).
  - `public void SetItem(ItemStack item)` — `:31-65`: parents the stack under `itemPosition`, plays the drop tween (uses `UnityEngine.Random.Range(0.1f, 0.3f)` for the delay), then `SetPrice(itemStack.unitSellingPrice * itemStack.Quantity)`.
  - `public ItemStack RetrieveItem()` — `:67-82`: returns null when `!canPick`; otherwise clears `itemStack`, fires `OnItemRetrieved`, hides the price tag.
  - `public void SetPrice(int price)` — `:84-123`: world-space price tag only (the price shown is unit price × quantity; ≥ 1 000 000 is shown as `"{n}M"`).
- **Where the price lives**: `ItemStack.unitSellingPrice` (`public int unitSellingPrice;` `ItemStack.cs:15`). It is written only by `ShopManager.LoadShopFromSave` (`ShopManager.cs:176`), `ShowcaseSlotGUI.CommitEditingPrize` (`ShowcaseSlotGUI.cs:164`), `ShowcaseSlotGUI.ResetEditingPrize` (`:176`) and tutorial/trailer code.

### 2.3 Placing/removing an item and setting a price (the UI)
- `GUIManager.GoToShowcasePanel(Showcase showcase)` — `GUIManager.cs:659-675`: requires `canOpenPanels && currentInventory == null`; calls `showcaseInventoryPanel.SetShowcase(showcase)`.
- `public class ShowcaseInventoryPanel : SideInventoryPanelContainer` — `ShowcaseInventoryPanel.cs:9`. `public Showcase openendShowCase; public List<ShowcaseSlotGUI> showcaseSlots;` (`:12-14`).
  - `Enable()` — `:88-102`: `dayAfternoonPanel.PauseWheelAnimation()`, `nightFilter.PauseFade()`, **`GameManager.Instance.PauseGame()`**, `InitShowcaseGrid(openendShowCase)`.
  - `Disable()` — `:104-125`: resume wheel/fade + `UnpauseGame()` (unless `_quicksearch`), showcase `"Close"` trigger, re-enable hero movement.
  - `InitShowcaseGrid` — `:384-404`: binds each GUI slot (`slotIndex`, `ownerShowcase`), `SetItemStack(showcase.itemPositions[num].itemStack, forInitialize: true)`, sets `canPick = true`.
  - Price edit: `StartEditingPrice()` sets `openendShowCase.itemPositions[_currentSlotEditing.slotIndex].canPick = false` (`:147-156`); `CommitEditingPrice()` sets it back to true and calls `_currentSlotEditing.CommitEditingPrize()` (`:158-170`).
  - Quick-move bag→shelf: `private int MoveToShowcase(InventorySlotGUI slot)` — `:276-382` (stacks into matching stacks first, then free slots; ends in `showcaseSlots[index].SetItemStack(stack)`).
- `public class ShowcaseSlotGUI : InventorySlotGUI` — `ShowcaseSlotGUI.cs:6`.
  - `public override bool SetItemStack(ItemStack stack, bool forInitialize = false)` — `:66-104`: **the single funnel for shelf contents**: `ownerShowcase.SetItemInPosition(stack, slotIndex)`; when not initializing calls `ResetEditingPrize()`; when the slot becomes empty calls `ownerShowcase.itemPositions[slotIndex].SetPrice(0)`.
  - `public void CommitEditingPrize()` — `:162-167`: `itemStack.unitSellingPrice = ParseLabelNumber(); ownerShowcase.itemPositions[slotIndex].SetPrice(...); ItemPriceManager.Instance.SetLastSettedPrice(itemStack.master, itemStack.unitSellingPrice);`
  - `public void ResetEditingPrize()` — `:169-179`: default price = `ItemPriceManager.Instance.GetLastPrice(item, ItemPriceValoration.Cheap)`, else `GetLastSettedPrice(item)`, else 0.
  - While the panel is open it subscribes `ownerShowcase.itemPositions[slotIndex].OnItemRetrieved += ShowcaseSlot_OnItemRetrieved` (`:144-151`) so a customer/thief taking the item clears the GUI slot.
- Showcase interaction is NOT blocked while the shop is open: `IsInstantActionBlocked() => blocked` — `Showcase.cs:111-114`.

### 2.4 Special shelves and the sales box
- **Display case ("cool"/"fancy")**: `ShowcaseSlot.isCool` (`ShowcaseSlot.cs:21`), `Showcase.isFancy` (`Showcase.cs:17`). Effects:
  - popularity is bumped one step for cool slots: `GetPopularity(ItemMaster item, bool isShowcaseCool = false)` returns `Mathf.Min(3, popularity + 1)` when `isShowcaseCool && value.popularity != 0` — `ItemPriceManager.cs:245-260`; all price thresholds take `isShowcaseCool` (`:177-232`).
  - showcase thieves never target them: `list.RemoveAll((ShowcaseSlot x) => x.isCool)` — `ThiefVisitorMind.cs:790`.
  - `Linear` customers sort non-cool first (`VisitorMind.cs:492-509`).
  - GUI art only: `ShowcaseSlotGUI.Enable()` (`ShowcaseSlotGUI.cs:119-138`), `SetShowcase` (`ShowcaseInventoryPanel.cs:79-86`).
- **Sales box**: `references.salesBox` is a `Chest` (`ShopReferences.cs:37`), active only if `townboardSaveInfo.GetSalesBoxLevel() > 0` (`ShopManager.cs:253-256`). Opened through `Chest.OpenChestGUI` → `GUIManager.Instance.GoToSalesBox(this)` (`Chest.cs:685-688`).
  - Customers with `visitSalesBox` and `UnityEngine.Random.value < visitSalesBoxProb` go to it (`VisitorMind.cs:477-480`) and buy a random stack instantly, no till: price `Mathf.Max(1, (int)(GetCorrectPriceWithPopularity(master) * (1f - Constants.GetFloatArray("salesboxPricePercentPerLevel")[salesBoxLevel])))` × quantity → `HeroMerchant.Instance.addGold(num)` — `VisitorMind.cs:651-690`.
  - On close everything left is auto-sold the same way: `public void SellAllSaleBoxItems()` — `ShopManager.cs:1108-1137`.

### 2.5 Shop upgrades
- Levels live in `TownboardSaveInfo`: `shopLevel`, `salesBoxLevel`, `cashMachineLevel`, `bedLevel`, `bedChestLevel` (+ `*Seen`, `*Available`) — `TownboardSaveInfo.cs:47-75`.
- Bought on the town board (`TownBoardShopPanel.YesButton_OnClickEvent`, `TownBoardShopPanel.cs:148-264`): `HeroMerchant.Instance.addGold(-_selectedCost)`, then per type:
  - Shop: `townboardSaveInfo.UpgradeShop(num)`, `UnlockHawker()` when `num >= 1`, `UnlockBed/BedChest/CashMachine/SalesBox(num)`, `ShopManager.Instance.UpgradeShopTo(num)`, `TownCinematics.PlayShopUpgradeAnimation()` (`:157-195`).
  - SalesBox → `UpgradeSalesBox(num)`, `ShopManager.Instance.UnlockSalesBoxIfAvailable()` at level 1 (`:196-213`). CashMachine → `ShopManager.Instance.UpgradeCashMachineTo(num)` (`:214-228`). Bed → `UpgradeBedTo(num)` (`:229-243`). BedChest → `UpgradeBedChestTo(num)` (`:244-258`).
- Level 5 (DLC shop) is bought from the DLC `Worker` NPC: `townboardSaveInfo.UnlockShop(5); MarkShopUnlockAsSeen(5); UpgradeShop(5); ShopManager.Instance.UpgradeShopTo(5);` — `Moonlighter.DLC.WandererDungeon.Town/Worker.cs:250-262`. The prefab comes from the DLC bundle via `iDLCController.LoadAsset<GameObject>(text)` (`ShopManager.cs:129-144,393-408`).
- `public void UpgradeShopTo(int level)` — `ShopManager.cs:388-522`: instantiates the new prefab, moves sales box / chests / shelf items across (`:416-480`), `CheckHelperInstance(shopReferences)`, destroys the old shop after 0.2 s, rewires `villageEntranceDoor`, re-adds `overcapacity`, `visitorGenerator.Init(references.shopExit)`, rebuilds bed / bed chest / cash machine, and for `level == 1` places the default pots in decoration slots (`:504-520`).
- What a level changes (all from `Constants`, a JSON TextAsset `Resources "constants"` — `Constants.cs:36-44`):
  - max simultaneous customers `maxNumberOfVisitorsPerShop[shopLevel]` → `shopStats["overcapacity"]` (`ShopManager.cs:147-148,493-494`) → `_maxNumberOfVisitors` (`VisitorGenerator.cs:207`).
  - open duration `timePerShopLvl` (see 2.7), thief probabilities `thiefProbPerShop` / `maxThiefProbPerShop` (`VisitorGenerator.cs:182-192`), `maxNumberOfThiefsPerShop` (`Moonlighter.Shop/ThiefVisitorSpawner.cs:13-18`), `maxQuestProposalsPerShopLvl` (`Moonlighter.Shop/QuestVisitorSpawner.cs:25-26`), `minRichProbPerShop` (`VisitorGenerator.cs:216-217`), hagglers only at `shopLevel >= 5` (`Moonlighter.DLC.Shop/HaggleVisitorSpawner.cs:21`), customer prefabs gated by `VisitorGenerationParams.minShopLvl` (`VisitorGenerationParams.cs:10`, `Moonlighter.Shop/NormalVisitorSpawner.cs:20`).
  - number of shelves / chests / decoration slots / queue points = whatever the prefab's `ShopReferences` lists **(inferred from the prefab-driven design)**.
  - cash register level: tip `Constants.GetFloatArray("cashMachinePercentPerLevel")[cashMachineLevel]` (`CashPoint.cs:110-112`); bed level: `bedAbsorptionPerLevel`, `bedShieldsPerLevel` (`ShopManager.cs:829-830`); bed chest: `bedChestSizePerLevel` (`:338`).
- `ShopStats` (decoration buffs) — `ShopStats.cs:4-65`: string-keyed floats, `public float this[string name]`, `public event Action<string, float> OnStatValueUpdate;` (only fired by `SetValue`, not by the indexer). Defaults in `ResetShopStats()` — `ShopManager.cs:699-710`: `fasterWalkers=1, generousCustomers=1, repelThieves=0, luxury=0.2, longerDays=1, patientCustomers=1, overcapacity=0, delay=0, hagglersReducedItems=0`.

### 2.6 The till (`CashPoint`) and queues
- `public class CashPoint : TargetPoint, IActionable` — `CashPoint.cs:12`. `public Agent visitorBuying; public ItemStack lastStack;` (`:14,20`). `public static event Action<ItemMaster, int, ItemPriceValoration> OnItemSold;` (`:33`).
- Queue API on `ShopManager`: `public int WaitInCashQueue(Agent visitor)` (`ShopManager.cs:1196`), `public TargetPoint GetTargetPointByQueuePosition(int i)` (`:1204`), `public void UpdateQueue(int advanceFromIndex = 0)` (`:1213`, sends `AdvanceInQueueMessage(i)` to everyone from that index), `public void LeaveQueue(int index)` (`:1227`). Quest/haggle queue mirrors it: `WaitInQuestQueue`, `GetTargetPointByQuestQueuePosition`, `UpdateQuestQueue`, `LeaveQuestQueue` (`:1235-1267`).
- Flow:
  1. Customer picks the item: `_pickedItem = _showcaseSelected.RetrieveItem()` and parents it to itself — `VisitorMind.cs:1016-1026`; then `ChangeToPaying()` → `_queuePosition = ShopManager.Instance.WaitInCashQueue(m_Owner)` — `:407-413`.
  2. Walks to `GetTargetPointByQueuePosition(_queuePosition)` (`:1281-1283`). On arrival sets `_lapse = maxWaitingTime * shopStats["patientCustomers"]`; if position 0: `shopCash.PlaceItem(_pickedItem)` and `(_lastTargetPoint as CashPoint).visitorBuying = m_Owner` — `:1262-1277`.
  3. Hero uses the till: `CashPoint.IsInstantActionBlocked() => !visitorBuying` (`CashPoint.cs:169-172`); the trigger's UnityEvent calls `public void TryToSellItem()` (`:90-142`; no code caller, prefab-wired **(inferred)**).
  4. `TryToSellItem`: finds the `ItemStack` under sibling `"Item_Cash_Point"`; gold = `(int)(unitSellingPrice * Quantity * (shopStats["generousCustomers"] + cashMachinePercentPerLevel[cashMachineLevel] + indecisiveTip))` (`:109-112`); `HeroMerchant.Instance.addGold(num3)`; `HeroStatistics.Instance.RegisterItemSell(...)`, `RegisterCustomerBuy()`; `ItemPriceManager.Instance.RegisterItemSell(stack)`; if `visitorMind.LastItemValoration == Expensive` → `ShopManager.Instance.OnExpensiveItemSold(master)`; fires `CashPoint.OnItemSold`; destroys the stack; `visitorBuying.SendAgentMessage<BuyTransactionCompleteMessage>()`; `PlayCashRegisterAnimation()`; `ShopManager.Instance.Invoke("UpdateQueue", 0.3f)` (`:113-140`).
  5. Customer receives the message → `LeaveQueue`, `visitorBuying = null`, leaves — `VisitorMind.cs:1316-1326`.
- Impatience: if `_lapse` runs out the customer leaves **with the item** and pays only `unitSellingPrice * Constants.GetFloat("visitorLeavePenalty")` (× `Quantity` in the queue branch, not in the cash-point branch) — `VisitorMind.cs:1302-1313,1331-1341`.
- `ShopManager.OnExpensiveItemSold(ItemMaster master)` — `ShopManager.cs:262-276`: popularity −1 (min `Low`), `visitorGenerator.IncrementThiefProbability(Constants.GetFloat("thiefProbIncrement"))`, `DecrementRichProbability(...)`.
- The till requires no fixed standing spot other than the `ActionTrigger` collider being overlapped by a `"Player"`/`"ActionArea"` collider (`ActionTrigger.cs:95-117`) and `GUIManager.Instance.CurrentInventory == null` (`:322`).

### 2.7 Open / close flow
Door: `public class ShopEntranceDoor : MonoBehaviour, IActionable` — `ShopEntranceDoor.cs:9`, `public bool opened;` (`:11`), `public bool blocked;` (`:17`).
- Instant action = enter/exit: blocked unless `!blocked && IsShopClosed && !IsWaitingToCloseShop && !IsShopWaitingForResult` — `:412-419`.
- Long-press = open/close (`SwapShopOpenOrClose()` `:105-115`, prefab-wired **(inferred)**): `IsLongPressedActionBlocked()` — `:439-450`: allowed only if `canOpenShop`; opening additionally requires `visitorGenerator.generatedVisitors.Count == 0`.
- `canOpenShop` is recomputed on every day-time change: `canOpenShop = obj == DayTime.Morning;` — `ShopManager.cs:562-565`.

**Open** — `ShopEntranceDoor.OpenShop()` (`:117-123`) → `public void OpenShop()` `ShopManager.cs:1149-1176`:
- duration `num2 = ((floatArray.Length >= num) ? floatArray[^1] : floatArray[num])` with `floatArray = Constants.GetFloatArray("timePerShopLvl")`, `× 60f × shopStats["longerDays"]` (`:1153-1157`). As decompiled, this picks the **last** array entry whenever the array is at least `shopLevel` long.
- `changeTimeAndCloseCall = DOVirtual.DelayedCall(num2, ChangeTimeAndCloseShop, ignoreTimeScale: false);` (`:1158`), `nightFilter.SetFade(1f, num2)`, `dayAfternoonPanel.StartWheelAnimation(num2)` (`:1159-1160`).
- `TimeManagerV2.Instance.OnDayTimeChange += OnOpenedShopDayChanged;` (`:1161`), resets the quest queue, `visitorGenerator.EnableSpawn(num)` (`:1168`), `IsShopClosed = false`, fires `ShopManager.OnShopOpen` (`:1171-1174`).

**Close by player** — `ShopEntranceDoor.CloseShop()` (`:125-135`) → `public void CloseShop()` `ShopManager.cs:944-975`:
- returns if `!canOpenShop`; `changeTimeAndCloseCall.Complete()`; `visitorGenerator.DisableSpawn()`; `IsShopWaitingForResult = true; IsShopClosed = true;`; hide wheel, pause night fade; `SellAllSaleBoxItems()`; fire `OnShopClose`; `ItemPriceManager.Instance.RegisterEndOfRun()`; `visitorGenerator.PrintStatistics()`.
- If `!VisitorMind.IsAnyoneBuying() && !ThiefVisitorMind.IsAnyoneBusy()` → `ShowShopResultsAndClose()`; else `IsWaitingToCloseShop = true`, send `CloseShopMessage` to every generated visitor and wait on `VisitorGenerator.OnVisitorLeft` until `visitorGenerator.VisitorsInShop <= 0` (`:964-985`).
- `private void ShowShopResultsAndClose()` — `:993-1024`: `canOpenShop = false`, destroys the helper mind object, `CloseShopMessage` to visitors not in the cash queue, `FadeToNightAndCloseShopDefinitely(showShopResultPanel: true)`, frees interest points, `GiveWillBedBenefits(...)`.
- `private void FadeToNightAndCloseShopDefinitely(bool showShopResultPanel = false)` — `:1080-1106`: unsubscribes `OnOpenedShopDayChanged`, `GUIManager.Instance.FadeBoth(in: { ChangeTimeAndCloseShop(); StopWheelAnimation(); Destroy every generatedVisitors[i].gameObject; GameManager.Instance.SaveSlotOnGoToDungeon(serialize: true); }, out: { GoToShopResultsPanel() or re-enable hero movement }, 1f)`.
- `private void ChangeTimeAndCloseShop()` — `:1139-1147`: `changeTimeAndCloseCall.Complete(); if (IsShopOpen()) references.shopEntranceDoor.CloseShop(); TimeManagerV2.Instance.ChangeToAfternoon();`

**Close by timer**: the DelayedCall runs `ChangeTimeAndCloseShop` directly (door `CloseShop()` → the same `ShopManager.CloseShop()`), then `ChangeToAfternoon()` fires `OnOpenedShopDayChanged` (`:1178-1184`), which fades to night without the results panel only if the queue is empty, the shop is closed and nobody is buying/stealing.

Other variants: `public void CloseShopWithoutResultsPanel()` (`:1031-1078`, no code caller found), `public void PreventShopFromCloseAfterTime()` kills the timer (`:1026-1029`, used by `TutorialScene3.cs:158`). `UpdateQueue` also triggers the final fade when the queue empties after close (`:1221-1224`). `ShopManager.OnDestroy` completes the timer if the shop was open (`:574-579`).

Customers on close: each mind sets `_isShopClosed` on `CloseShopMessage` (`VisitorMind.cs:241-244`); browsing customers switch to `Leaving` (`:442-445`), ones already paying finish; thieves leave immediately (`ThiefVisitorMind.cs:170-174`); finally all remaining visitor GameObjects are destroyed in the fade callback (`ShopManager.cs:1089-1092`).

### 2.8 End-of-day summary
- `public class ShopResultsPanel : Panel` — `ShopResultsPanel.cs:6`; tabs `ShopResultsShopTab shopTab; ShopResultsPopularityTab popularityTab;` (`:9-11`); `public bool talkingWithHelper;` (`:23`).
- Opened by `GUIManager.GoToShopResultsPanel(bool isTalkingWithHelper = false)` — `GUIManager.cs:809-822` (requires `canOpenPanels`).
- `Enable()` → `GameManager.Instance.PauseGame()` (`ShopResultsPanel.cs:41-44`); `Disable()` → `ShopManager.Instance.OnClosedResultPanel()` unless `talkingWithHelper`, `UnpauseGame()`, `HeroStatistics.Instance.ResetShopThisRunStatistics()` (`:59-79`). `OnClosedResultPanel()` clears `IsShopWaitingForResult` and re-checks the helper (`ShopManager.cs:987-991`).
- Data source is entirely `HeroStatistics.Instance` "this run" lists: `itemsSoldThisRun`, `itemsSoldQuantityThisRun`, `itemsSoldPriceThisRun`, `salesBoxItemsSoldThisRun`, `totalSalesBoxGoldThisRun`, `robbedItemsThisRun`, `questRewardsThisRun`, `customersThisRun`, `thievesThisRun`, `helperItemsSoldThisRun`, `totalHelperGoldThisRun` (`ShopResultsShopTab.cs:39-156`) and `popularityAscendedToNeutral/ToPopular`, `popularityDescendedToNeutral/ToLow` (`ShopResultsPopularityTab.cs:31-34`). `HeroStatistics` is a `ScriptableObject` singleton (`HeroStatistics.cs:5-7`). Stats are reset at shop load (`ShopManager.cs:557-560`).

---

## 3. Customers ("visitors")

### 3.1 Agent / Mind / Body / message system
- `public class AgentSystem : MonoBehaviour` — `AgentSystem.cs:5`. `private List<Agent> m_Agents`, `m_AgentsToAdd`, `private Dictionary<Agent, List<AgentSystemMessage>> m_Mailbox` (`:7-11`). `public float TickSimulation = 0.04f;` (`:21`). API: `public List<Agent> Agents()`, `Register`, `Unregister`, `GetMessages(Agent)`, `public void SendAgentMessage(Agent destination, AgentSystemMessage message)`, `public void BroadcastAgentMessage(AgentSystemMessage message)` (`:27-79`).
  - `Update()` — `:86-131`: accumulates `Time.realtimeSinceStartup` deltas and runs `m_Agents[i].Tick(TickSimulation)` in a `while (m_TickLapse > TickSimulation)` loop (catch-up after a hitch = many ticks in one frame); newly registered agents join at the end of the frame (`:126-130`). Exceptions per agent are caught and logged (`:99-106`).
  - Not a singleton: found with `UnityEngine.Object.FindObjectOfType<AgentSystem>()` (`VisitorGenerator.cs:148`, `ShopManager.cs:1015`, `ShopHelperMind.cs:353`); `TownCinematics` and the tutorial manager have their own references (`TownCinematics.cs:121`, `TutorialScene2.cs:101`).
- `public class Agent : MonoBehaviour` — `Agent.cs:4`: `private AgentSystem m_AgentSystem; private Mind m_Mind; private Body m_Body;` (`:6-10`); `public void SetAgentSystem(AgentSystem system)`, `public void Init()`, `public void Init(AgentSystem agentSystem, Mind mind = null, Body body = null)` (registers and calls `m_Mind.Init(this)`), `public void Tick(float delta)` (drains the mailbox through `m_Mind.ProcessMessage`, then `m_Mind.Tick`, then `m_Body.Tick`), `private void OnDestroy()` → `OnKill()` + `Unregister`, `public void SendAgentMessage<T>() where T : AgentSystemMessage, new()`, `public void SendAgentMessage(AgentSystemMessage message)` (`:27-120`).
- `public class Mind : MonoBehaviour` — `Mind.cs:3`: `protected Agent m_Owner; protected AgentSystem m_AgentSystem; protected Body m_OwnerBody;`, `public virtual void Tick(float delta)`, `public virtual bool ProcessMessage(AgentSystemMessage message)`, `public virtual void OnKill()`.
- `public class Body : MonoBehaviour` — `Body.cs:4`: virtual `IsMoving`, `Speed`, `Direction`, `Objective`, `ReachedObjective`; `public event Action<Vector3> OnObjectiveChange;`; `MoveTo(Vector3 pos)`, `Stop()`, `Jump(float v)`. Customers use `PolyNav2DBody : Body` wrapping a `PolyNavAgent` (`PolyNav2DBody.cs:4-70`); `MoveTo` = `m_PolynavAgent.SetDestination(pos, delegate { IsMoving = false; })` (`:47-54`). `PolyNavAgent.LateUpdate` moves with `Time.deltaTime` (`PolyNavAgent.cs:238-267`).
- `Blackboard` is a plain `Dictionary<string, object>` wrapper (`Blackboard.cs:3-41`); no shop class uses it **(inferred from finding no references in the shop/visitor files read)**.
- `public class AgentSystemMessage` — `AgentSystemMessage.cs:1-23` (auto id from `private static int m_LastId`).

Message classes (all `: AgentSystemMessage`):
| Class | Payload | Sent by → handled by |
|---|---|---|
| `CloseShopMessage` | – | `ShopManager.cs:972,1006,1017`, `ShopEntranceDoor.cs:93` → every mind |
| `AdvanceInQueueMessage` | `public int nextPosition;` | `ShopManager.cs:1219,1257` → `VisitorMind`, `QuestVisitorMind`, `HaggleVisitorMind` |
| `BuyTransactionCompleteMessage` | – | `CashPoint.cs:138` → `VisitorMind.cs:257-260` |
| `BubbleAnimationEndedMessage` | `public BubbleAnimation animation;` | `VisitorAnimations.cs:372-438` → `VisitorMind.cs:245-249` |
| `YouAreBeingRobbedMessage` | – | `ThiefVisitorMind.cs:521` → `VisitorMind.cs:261-265` |
| `SeenSomethingHorribleMessage` | `public Vector3 position;` | `ThiefVisitorMind.cs:700-703` → `VisitorMind.cs:266-270` |
| `ThiefStartRobbing` / `ThiefWasCaught` | `public Thief thief;` | `ThiefVisitorMind.cs:231-234,457-460,593-596,621-624` → `ShopHelperMind.cs:73-88` |
| `ThiefCaughtByWillMessage` / `ThiefCaughtByHelperMessage` | – | `ThiefCatcher.cs:9` / `ShopHelperMind.cs:302` → `ThiefVisitorMind.cs:151-160` |
| `StopFighting` | – | `ThiefVisitorMind.cs:686` → `ShopHelperMind.cs:79-82` |
| `QuestTransactionComplete` | `public bool accepted;` | `QuestPanel.cs:203,355,361`, `Moonlighter.DLC.WandererDungeon/HagglePanel.cs:420`, `HagglerVisitorPanel.cs:98` → `QuestVisitorMind.cs:118-122`, `HaggleVisitorMind.cs:115-118` |
| `HasReturnedForQuestMessage` | `public ActiveQuest activeQuest;` | `Moonlighter.Shop/QuestVisitorSpawner.cs:121` → `QuestVisitorMind.cs:123-129` |
| `Moonlighter.DLC.Shop.Agents.WillAttendedCustomerMessage` | – | `IndecisiveVisitor.SendWillAttendedCustomerMessage()` (`IndecisiveVisitor.cs:24-27`) → `VisitorMind.cs:271-274` |
| `BirdEnteredShopMessage` | `public BirdPosition GetBirdPosition; public BirdClose GetBirdClose;` (delegates) | `BirdVisitorMind.cs:128-132` → `VisitorMind.cs:275-287`, `ShopHelperMind.cs:93-101` |
| `BirdExitedShopMessage`, `BirdCloseMessage`, `BirdFarMessage` | – / `position` | `BirdVisitorMind.cs:103-116,143` |
| `MoveToMessage`, `ShowTextDialogMessage`, `HideTextDialogMessage` | cutscene agents (`DummyMind.cs:56-73`) | |

### 3.2 Customer classes
| Agent class | Mind | Notes |
|---|---|---|
| `Visitor : Agent, IVisitor` (`Visitor.cs:5`) | `VisitorMind : CommonVisitorMind` (`VisitorMind.cs:9`) | normal / rich / "wants hero items" / "wants merchant items" are the **same class** with different serialized fields |
| `Moonlighter.DLC.Shop.Agents.IndecisiveVisitor` (`IndecisiveVisitor.cs:7`) | `IndecisiveVisitorMind : VisitorMind, IActionable` (`IndecisiveVisitorMind.cs:7`) | DLC; must be "attended" by the hero |
| `Thief : Agent, IVisitor` (`Thief.cs:5`) | `ThiefVisitorMind : CommonVisitorMind` (`ThiefVisitorMind.cs:6`) | `enum ThiefType { Showcase, Visitor, Register }` (`:8-13`) |
| `QuestVisitor : Agent, IVisitor` (`QuestVisitor.cs:5`, `public bool isMale = true;`) | `QuestVisitorMind : CommonVisitorMind` (`QuestVisitorMind.cs:4`) | `enum QuestVisitorType { Merchant, Hero }` (`:27-31`) |
| `Moonlighter.DLC.WandererDungeon.Shop.HaggleVisitor` (`HaggleVisitor.cs:7`) | `HaggleVisitorMind : CommonVisitorMind` (`HaggleVisitorMind.cs:6`) | DLC interdimensional barter customer; shop level ≥ 5 |
| (no Agent) | `BirdVisitorMind : MonoBehaviour` (`BirdVisitorMind.cs:6`) | DLC bird; DOTween-driven, not ticked by `AgentSystem` |
| `ShopHelper : Agent` (`ShopHelper.cs:5`) | `ShopHelperMind : CommonVisitorMind` (`ShopHelperMind.cs:5`) | shop assistant (see 6.8) |

`interface IVisitor { void SetVisitorGenerator(VisitorGenerator visitorGenerator); }` — `IVisitor.cs:1-4`. All visitor agents call `_visitorGenerator.RemoveVisitor(this)` from `OnKill` (i.e. `OnDestroy`); only `Visitor` and `IndecisiveVisitor` null-check the generator (`Visitor.cs:14-20` vs `Thief.cs:14-17`, `QuestVisitor.cs:16-19`, `HaggleVisitor.cs:18-21`).

`CommonVisitorMind : Mind` — `CommonVisitorMind.cs:4-99`: statics `public static ChestVisitorSlot _salesBoxPoint; public static List<ShowcaseSlot> _showcasePoints; public static List<TargetPoint> _interestingPoints;` (`:6-10`); `public bool hasHighBudget;` (`:16`, "rich"); `protected virtual ItemPriceValoration GetPriceValoration(ShowcaseSlot showcase)` (`:36-69`); `MoveToTargetPoint(TargetPoint point)` sets `point.InUse = true` (`:91-98`); `FreeLastTargetPoint()` (`:82-89`). `TargetPoint` is just a `bool InUse` marker (`TargetPoint.cs:3-19`).

`VisitorMind` serialized configuration (what makes a "type") — `VisitorMind.cs:157-190`: `minInterestingPoints = 3`, `maxInterestingPoints = 5`, `public bool visitShowcase;`, `public ItemCategory interestedIn;` (`AllItems, MerchantItems, HeroItems` `:78-83`), `private VisitShowcaseTypes visitShowcaseType;` (`Linear, Random`), `visitSalesBox`, `visitSalesBoxProb`, `visitAlsoInterestingPoints`, `visitInterestingPointsProb`, `isIndecisive`, `indecisiveValorations`, `indicisiveWaitTime`, `public float maxWaitingTime;`, `public Sprite spriteVisitorType;` (bubble icon shown for 2 s on spawn `:229-232`). `ItemCategory.HeroItems` means `slot.itemStack.master is EquipmentItemMaster` (`:1378-1383`).

States — `VisitorMind.cs:25-70`: `State { Visiting, IndecisiveShowcase, Showcase, SalesBox, Paying, Shocked, Robbed, Leaving }`, `ShowcaseState { Approaching, Validating, HighBudgetValidation, Pick, Exit }`, `PayingState { Approaching, WaitInQueue, WaitInCashPoint, Exit }`, `IndecisiveShowcaseState { Approaching, Waiting, Validating, Pick, Exit }`. All `protected`/private. `public ItemStack PickedItem()` (`:1350`), `public ItemPriceValoration LastItemValoration` (`:194-205`), `public ItemPriceInfo.Popularity PopularityWhenPicked` (`:206-216`), `public static bool IsAnyoneBuying()` — uses `Resources.FindObjectsOfTypeAll<VisitorMind>()` (`:1394-1405`).

### 3.3 Spawn logic and RNG
- `public class VisitorGenerator : MonoBehaviour` — `VisitorGenerator.cs:12`. `public AgentSystem agentSystem; public List<GameObject> generatedGameObjects; public List<Agent> generatedVisitors; public List<VisitorSpawner> visitorSpawners;` (`:14-30`). `public int VisitorsInShop`, `MaxVisitors`, `OccupationPercentage` (`:78-82`). `public static event Action<Agent> OnVisitorLeft;` (`:84`).
- DLC spawners are appended at `Start` from `iDLCController.LoadAssets<GameObject>("Shop/VisitorSpawners")` (`:105-129`).
- `public void EnableSpawn(int shopLevel)` — `:144-210`: finds the `AgentSystem`, splits spawners into `WeightedVisitorSpawner` / `TimedVisitorSpawner` / `ConditionalVisitorSpawner`, calls `CalculateSpawns(_shopLevel)` on each, computes `_thiefsProb = Mathf.Clamp(num3 + shopStats["repelThieves"] * num3, 0f, max)` and assigns `AssignedWeight` (thief spawners share `_thiefsProb`, the rest share `1 - _thiefsProb`), `_maxNumberOfVisitors = (int)shopStats["overcapacity"]`, starts `SpawnCoroutine()`. `public void DisableSpawn()` just clears `_canSpawn` (`:252-255`).
- `private IEnumerator SpawnCoroutine()` — `:257-324`. Each iteration: conditional spawners first (`CheckCondition()` then `TrySpawn(_time)`); if `_visitorsInShop < _maxNumberOfVisitors`: timed spawners, then one weighted roll `UnityEngine.Random.value * _maxAssignedWeight`, then a shuffled fallback over DEFAULT spawners. Then `yield return new WaitForSeconds(UnityEngine.Random.Range(minCooldown, maxCooldown))` with `minCooldownBetweenVisitors` / `maxCooldownBetweenVisitors`; `_time` = seconds since open (`Time.time`).
  - As decompiled the fallback loop indexes `_weigthtedVisitorSpawners[l]` while iterating `_defaultWeigthtedVisitorSpawners.Count` (`:310-317`).
- `public Agent SpawnVisitor(GameObject prefab, bool affectedByStats = false)` — `:336-374`: `Instantiate(prefab, _shopSpawnPoint.transform.position, Quaternion.identity)`, parent = generator, add to both lists, `SetAgentSystem`, `SetVisitorGenerator(this)`, optional `maxSpeed *= shopStats["fasterWalkers"]` and `maxWaitingTime *= shopStats["patientCustomers"]`, `component.Init()`, `_visitorsInShop++`. **This is the one place every shop agent (except the bird) is created.**
- `public void RemoveVisitor(Agent visitor)` — `:401-416`: removes from the lists, decrements counters, fires `OnVisitorLeft`.
- Spawner classes (`Moonlighter.Shop`): `abstract VisitorSpawner : MonoBehaviour, IVisitorSpawner` (`VisitorSpawner.cs:6`, `public List<VisitorGenerationParams> visitorGeneration`, `GetUniqueValidVisitor()` rejects a prefab whose `Animator.runtimeAnimatorController.name` is already in the shop `:14-17`), `WeightedVisitorSpawner` (`public string id; public float weight; AssignedWeight`), `TimedVisitorSpawner` (`ResetSpawns/ScheduleSpawn/HasToSpawn/MoveToNextSpawn`), `ConditionalVisitorSpawner` (`CheckCondition()`).
  - `NormalVisitorSpawner` (id `"DEFAULT"` **(inferred from `VisitorGenerator.cs:181`)**): weighted list of prefabs with `minShopLvl <= shopLevel`; spawns with `affectedByStats: true` (`NormalVisitorSpawner.cs:14-40`).
  - `ThiefVisitorSpawner` (id `"THIEF"`): cap `maxNumberOfThiefsPerShop`; note its private `_thiefsInShop` is only ever incremented (`ThiefVisitorSpawner.cs:36-46`). Calls `HeroStatistics.Instance.RegisterThief()`.
  - `QuestVisitorSpawner : TimedVisitorSpawner` — `QuestVisitorSpawner.cs:23-141`: number of offers = Bernoulli trials using `Random.value > num4`, times `Random.Range(minQuestTime, maxQuestTime) * timePerShopLvl[shopLevel] * 60`; returning quest-givers are spawned first when `activeQuest.completeDay == MoonlighterCalendar.TotalDays`; the visitor look is set by assigning `Animator.runtimeAnimatorController` on a generic `questVisitorPrefab`.
  - `Moonlighter.DLC.Shop.HaggleVisitorSpawner : TimedVisitorSpawner` — up to 3 per day, only `shopLevel >= 5`, same "generic prefab + animator controller" pattern (`HaggleVisitorSpawner.cs:19-50`).
  - `Moonlighter.DLC.WandererDungeon.Shop.BirdVisitorSpawner : ConditionalVisitorSpawner` — once per day if `minShopLevel <= shopLevel && Random.value < probabilityPercentage`, when `OccupationPercentage >= occupationPercentage`; `Object.Instantiate(birdPrefab)` directly, not via `SpawnVisitor` (`BirdVisitorSpawner.cs:22-51`).
- Rich probability: `DecrementRichProbability` lowers the weight of prefabs whose `VisitorMind.hasHighBudget` is true, floored at `minRichProbPerShop[level] + shopStats["luxury"]` (`VisitorGenerator.cs:212-227`).

### 3.4 How a customer picks an item
`VisitingStep` — `VisitorMind.cs:440-564`:
1. Waits `_lapse`; if `visitShowcase && visitAlsoInterestingPoints` flips a coin `UnityEngine.Random.value >= 0.5f` between shelf and wandering.
2. Candidates = `CommonVisitorMind._showcasePoints.FindAll(IsShowcaseInteresting)`. `IsShowcaseInteresting` — `:1355-1392`: slot not in use and has an item; `unitSellingPrice > 0`; `GetCorrectPriceWithPopularity != 0`; category filter; not already in `_showcasesVisited` (unless it was rejected as too expensive and the price changed since).
3. `Linear`: sorts the free list by cool-last then total price, but then takes `list[0]` of the unsorted candidate list (`:490-511`, as decompiled). `Random`: `list.GetRandom()`.
4. Otherwise visits `_interestingPointsToVisit` random free `TargetPoint`s (`UnityEngine.Random.Range(2f, 5f)` wait each), then leaves (`:542-562`).

At the shelf (`ShowcaseApproachingStep` `:766-810`): plays `BubbleAnimation.Thinking` with the popularity arrow, 1 s later evaluates.

### 3.5 Reaction faces and the price notebook
- `enum ItemPriceValoration { TooCheap, Cheap, Ignore, Expensive, TooExpensive, OverTooExpensive }` — `ItemPriceValoration.cs:1-9`.
- `enum BubbleAnimation { Idle, Thinking, StopThinking, TalkTooCheap, TalkCheap, TalkExpensive, TalkTooExpensive, TalkAngry, Warning, Waiting, StopWaiting, IndecisiveWaiting, StopIndecisiveWaiting, Disable }` — `BubbleAnimation.cs:1-17`.
- Thresholds — `CommonVisitorMind.GetPriceValoration` (`CommonVisitorMind.cs:36-69`) on `unitSellingPrice`: `< GetMinCorrectPrice` → TooCheap; `<= GetMaxCorrectPrice` → Cheap (the "perfect" face); `<= GetMaxOverpricedPrice` → Expensive; `<= GetTooExpensiveLimitPrice` → TooExpensive; else OverTooExpensive. Formulas in `ItemPriceManager.cs:147-232` (constants `cheapPriceLimit`, `expensivePriceLimit`, `overpricedExpensivePriceIncrement`, `tooExpensivePriceLimit`, `highPopularityPerfectPriceIncrement`, `highBudgetExpensivePriceIncrement`, `lowPopularityPerfectPriceDecrement`). Base price = `item.goldValue`, or `recipe.GetTotalCost()` if a recipe exists (`:147-160`).
- Outcome — `ShowcaseValidatingStep` `VisitorMind.cs:812-910`: TooCheap/Cheap/Expensive → buys (after the bubble state exits); TooExpensive → rejects, `_overpricedShowcasesVisited++` (leaves after 3); OverTooExpensive → `TalkAngry`. Rich customers (`hasHighBudget`) take `ShowcaseHighBudgetValidationStep` (`:912-993`): they show only `StopThinking` + their type sprite and **never write to the notebook**.
- Changing the price while the customer is at `Pick` makes them angry and walk away (`:995-1015`).
- **Notebook write**: only when popularity is `None` or `Neutral` (except OverTooExpensive, always): `ItemPriceManager.Instance.RegisterItemPricePerOpinion(_showcaseSelected.itemStack, valoration)` — `VisitorMind.cs:834-869`. `public bool RegisterItemPricePerOpinion(ItemStack item, ItemPriceValoration opinion)` — `ItemPriceManager.cs:100-136`: keeps max price for TooCheap/Cheap/Expensive and min for TooExpensive in `ItemPriceInfo.lastPricePerOpinion[(int)opinion]`, promotes `Popularity.None` → `Neutral`, fires `public event Action<ItemMaster, int, ItemPriceValoration> OnValorationMade;` (`:35,131-134`), returns true if the stored value changed. When true the mind calls `_visitorAnimations.PrepareItemValorationAnimation(...)`, and on the bubble's exit `ShopPricesUpdater.Instance.UpdatePriceInNotebook(...)` plays the HUD card (`VisitorAnimations.cs:485-497`, `ShopPricesUpdater.cs:126`).
- `public class ItemPriceManager : ScriptableObject, ISerializationCallbackReceiver` — `ItemPriceManager.cs:5`; `public static ItemPriceManager Instance` (`:16-33`, lazily created; subscribes `GameManager.Instance.OnSaveToDungeon` and `CultureManager.Instance.OnItemDiscovered`). State: `private Dictionary<string, ItemPriceInfo> _itemPrizeInfos` keyed by `ItemMaster.name` (`:9`), `public int runsToEndCycle;` (`:14`).
- `[Serializable] public class ItemPriceInfo` — `ItemPriceInfo.cs:4-39`: `name, timesSold, soldThisRun, int[] lastPricePerOpinion (6 entries), lastSetPrice, cyclesToReset, cyclesToUpgradePopularity, Popularity popularity`; `enum Popularity { None, Low, Neutral, Popular }`.
- Popularity dynamics: sales counted by `RegisterItemSell` (`ItemPriceManager.cs:138-145`); selling at `Expensive` drops popularity one step (`ShopManager.cs:262-276`); `public void RegisterEndOfRun()` (`ItemPriceManager.cs:314-376`) is the cycle tick, called at shop close (`ShopManager.cs:962`), sleeping (`Bed.cs:64`) and return from dungeon (`GameManager.cs:2093`).

### 3.6 Thieves
- Spawn: bubble icon `spriteVisitorType` for 2 s; `_caughtTrigger = base.transform.GetChild(1).GetComponent<Collider2D>()` disabled until the theft (`ThiefVisitorMind.cs:128-147`).
- Loop: `Visiting` (interest points) → `SearchingVictim` (looks left/right 4 × 1 s) → by `thiefType` (`:302-370`):
  - `Showcase` (`stealShowcase`): `SearchForShowcase()` — random interesting non-cool slot, avoiding the item stolen last time (`private static string _lastItemMasterNameStolen`) (`:787-801`); walks to the slot; `StealingShowcaseStealStep` (`:584-612`): `_pickedItem = _showcaseToSteal.RetrieveItem()`, `ShowCaughtHUD()`, `AdviseVisitors()`, `_caughtTrigger.enabled = true`.
  - `Visitor`: `SearchForVisitor()` = first generated visitor with `PickedItem() != null` (`:773-785`); chases to `robRadius`; takes the item and sends `YouAreBeingRobbedMessage` (`:516-530`).
  - `Register`: aborts while the hero is within 50 units of the till — `Vector2.Distance(HeroMerchant.Instance.transform.position, references.shopCash.transform.position) < 50f` (`:562-568`); otherwise steals `(int)(HeroMerchant.Instance.GetCurrentGold() * UnityEngine.Random.Range(min, max))` (`thiefRegisterCashStealGoldPercentageMin/Max`) via `addGold(-_pickedGold)` (`:614-641`).
- Then `Leaving` → walks to `shopExit`; on arrival `HeroStatistics.Instance.RegisterThiefRobbery(...)` and `Destroy(gameObject, 1f)` (`:420-444`). The catch trigger turns off within 60 units of the exit (`:445-449`).
- **Catching**: `public class ThiefCatcher : MonoBehaviour` on the thief's child — `OnTriggerEnter2D`: `(collision.CompareTag("Player") || collision.CompareTag("ActionArea")) && (HeroMerchant.Instance.heroMerchantInventory.GetEquippedItemByType(HeroMerchantInventory.EquipmentSlot.Ability1).Ability.isInUse || ...Ability2).Ability.isInUse)` → `CatchThief()` → `Thief.SendAgentMessage<ThiefCaughtByWillMessage>()` (`ThiefCatcher.cs:6-17`). `Ability.isInUse` is toggled by `AbilityUseStart/End` (`Ability.cs:110-118`); i.e. roll into the thief. The helper catches by proximity ≤ 10 (`ShopHelperMind.cs:298-303`).
- `StartFight()` — `ThiefVisitorMind.cs:708-748`: if caught by Will: `ForbidMovement()`, `StopMovement()`, **`heroMerchantController.TeleportTo(base.transform.position)`**, `GUIManager.Instance.PreventPanelChange()`, 2 s later `FreeWill()`; showcase thief → `Invoke("GiveItemToWill", UnityEngine.Random.Range(0f, 2f))` which does `HeroMerchant.Instance.heroMerchantInventory.TryAddItem(_pickedItem)` or drops it (`:750-771`); register thief → gold returned after a random 0–2 s.
- `public static bool IsAnyoneBusy()` uses `Resources.FindObjectsOfTypeAll<ThiefVisitorMind>()` (`:803-811`).

### 3.7 Quest customers ("hero/adventurer" and "merchant" givers)
- `QuestVisitorMind.Start()` — `QuestVisitorMind.cs:67-99`: picks `QuestManager.GetQuests(pred).GetRandom()` filtered by cultures defeated, `maxDungeonLevelVisited`, `giverType == visitorType`, not already active, not in `public static List<Quest> currentVisitorsPendingQuests` (`:63`). Adds `shopStats["delay"]` to `selectedQuest.daysToComplete` **on the shared `Quest` object** (`:81-84`).
- Queues at `shopQuest` via `WaitInQuestQueue` (`:201-233`); at position 0 sets `(_lastTargetPoint as QuestPoint).questVisitor` (`:209-213`).
- Hero interacts with `QuestPoint` (`QuestPoint.cs:13-30`) → `GUIManager.Instance.GoToQuestPanel(questVisitor)`; the panel pauses the game (`QuestPanel.cs:127-137`) and answers with `QuestTransactionComplete(accepted)`.
- Accept: `HeroMerchant.Instance.activeQuests.Add(new ActiveQuest(selectedQuest, m_Owner as QuestVisitor))` if below `maxActiveQuests` (`QuestVisitorMind.cs:274-277`).
- Return day: spawner sends `HasReturnedForQuestMessage`; `QuestPanel.Enable(QuestVisitor)` calls `HeroMerchant.Instance.CompleteQuest(...)` and `PlayFulfilledAnimation()` → `addGold(reward)` (`QuestPanel.cs:101-116,221-246`); `QuestVisitorMind` also has an `addGold` branch guarded by `questFulfilled` (`QuestVisitorMind.cs:250-272`; no code sets `questFulfilled` in the files read **(inferred: prefab/unused)**).
- Data: `Quest` (`Quest.cs:6-30`: `culture, floor, target, quantity, daysToComplete, reward, killQuestTarget, killQuestTargetKey, giverType, description`), loaded from CSV `Resources "generatedQuests"` (`QuestManager.cs:43-55`). `ActiveQuest` (`ActiveQuest.cs:6-39`: `quest, completed, failed, completeDay, giverVisitorPrefabName, giverVisitorIsMale`).
- `HeroMerchant.CheckQuestsCompletion()` fails overdue quests on every day-time change and on save (`HeroMerchant.cs:382-385,398-400,414-431`); `CompleteQuest` removes the items via `ShopManager.Instance.RemoveItems` (`:433-461`).

### 3.8 DLC customers
- **Indecisive** (`IndecisiveVisitorMind`): at the shelf shows `IndecisiveWaiting` for `indicisiveWaitTime` and enables `_collider2D` only in that state (`IndecisiveVisitorMind.cs:11-24`); the hero's action sends `WillAttendedCustomerMessage`; then it behaves as a normal buyer and pays an extra `Constants.GetFloat("indecisiveTipPercentage")` (`CashPoint.cs:101-104`, `VisitorMind.cs:1103-1211`).
- **Haggler** (`HaggleVisitorMind`): builds an offer in `Start` through `HaggleManager.GetNewHaggleOffer(new HaggleManager.HaggleParams{...})` (RNG) (`HaggleVisitorMind.cs:76-101`), uses the quest queue/point (`QuestPoint.haggleVisitor`, `:198-202`), UI `HagglerVisitorPanel` / `HagglePanel` (both pause the game; result via `QuestTransactionComplete`). Trade moves items with `ShopManager.Instance.RemoveItems` + `heroMerchantInventory.TryAddItem` (`HagglePanel.cs:664-665`).
- **Bird** (`BirdVisitorMind`): on spawn `OpenDoorUntilCondition(() => _leaving, 1f)`, **blocks the till and quest point** (`shopCash/shopQuest` `ActionTrigger.BlockInteraction()`), `visitorGenerator.DisableSpawn()`, sends `BirdEnteredShopMessage` to every non-thief agent (customers freeze in `Shocked`) (`BirdVisitorMind.cs:63-73,122-135`). Moves with `transform.DOMove` between random interest points (`:148-202`). Caught exactly like a thief (`:295-301`) → hero teleported onto it and frozen 2 s, then `EnableSpawn(shopLevel)` and `ExitShop()` (`:239-266`). `FixedUpdate` checks `Vector2.Distance(HeroMerchant.Instance.transform.position, transform.position)` for the catch hint (`:87-94`).

---

## 4. Minimal mirrored state for a customer on a guest

### 4.1 What a customer consists of
Read from `RequireComponent`/`GetComponent` usage (prefab layout itself **inferred**):
- root: `Visitor`/`Thief`/`QuestVisitor`/`HaggleVisitor`/`IndecisiveVisitor` (Agent), the Mind, `PolyNav2DBody`, `PolyNavAgent` (`Visitor.cs:3-4`, `PolyNav2DBody.cs:4`), `Animator` (body; controller name identifies the look — `VisitorSpawner.cs:16`), `VisitorAnimations`, `SoundsVisitors` (`VisitorAnimations.cs:70,97-100`), a collider tagged `"ShopVisitor"` that opens the door (`ShopEntranceDoorVisistorChecker.cs:6-12`).
- child `GetChild(0).GetChild(0)`: `SpriteDepthOrderRender` + `SpriteRenderer` (`VisitorAnimations.cs:69,101-104`).
- runtime child: the bubble (`bubbleAnimatorPrefab`) with an `Animator` + `SMAnimatorHandler` (`VisitorAnimations.cs:75-81`).
- thieves: `GetChild(1)` = catch trigger collider with `ThiefCatcher`, plus `CaughtHUD` object (`ThiefVisitorMind.cs:60,136`).

### 4.2 State to send (host → guest)
| Data | Source on host | Apply on guest |
|---|---|---|
| network id | assign in a `VisitorGenerator.SpawnVisitor` postfix (`VisitorGenerator.cs:336`) | key for the puppet |
| prefab identity | `prefab.name` passed to `SpawnVisitor`; the prefabs live in `VisitorSpawner.visitorGeneration[i].agent`, `QuestVisitorSpawner.questVisitorPrefab`, `HaggleVisitorSpawner.haggleVisitorPrefab`, `BirdVisitorSpawner.birdPrefab` | instantiate the same prefab from the guest's own spawner lists |
| look override | `Animator.runtimeAnimatorController.name` (set after spawn for quest/haggle visitors — `QuestVisitorSpawner.cs:118,133`, `HaggleVisitorSpawner.cs:44`) + `QuestVisitor.isMale` | assign the controller found by name in `questVisitorGeneration` / `haggleVisitorGeneration` |
| position | `transform.position` | set directly |
| moving + facing | body Animator bools `"Moving"`, `"FaceUp"`, `"FaceDown"`, `"FaceRight"`, `"FaceLeft"` (`VisitorAnimations.cs:131,166-169`) | set the bools, or drive `VisitorAnimations` via `LookTo(point, overrideFacing: true)` / `OverrideFacing*()` (`:278-353`) |
| bubble | every call of `public void PlayBubbleAnimation(BubbleAnimation animation, ItemPriceInfo.Popularity popularity = ItemPriceInfo.Popularity.None)` (`:182`) and `PlayBubbleAnimationWithSprite(Sprite sprite)` (`:172`) | replay the same call (enum + popularity, or the mind's `spriteVisitorType`) |
| carried item | `VisitorMind.PickedItem()` (`VisitorMind.cs:1350`): item name, plusLevel, quantity; carried at `localPosition = GetFacing() * 12f` (`:371-374`) | create/destroy a display `ItemStack` child |
| item on the till | `CashPoint.PlaceItem(ItemStack stack)` / `lastStack` (`CashPoint.cs:51-76`) | parent display stack under `"Item_Cash_Point"` |
| thief flags | `CaughtHUD` active, fight particles (`ThiefVisitorMind.cs:653-667,710`) | toggle visuals |
| despawn | `VisitorGenerator.OnVisitorLeft` (`VisitorGenerator.cs:84,411-414`) or object destroy | destroy puppet |
| shop-wide | shelf slot contents + `unitSellingPrice` (2.2), `visitorBuying != null` for the till prompt, `questVisitor`/`haggleVisitor` on `QuestPoint`, door `opened`, `IsShopClosed`, day wheel time | |

### 4.3 Making a puppet (what to disable)
- Do **not** call `Agent.Init()`: an agent that never registers is never ticked (`Agent.cs:37-65`, `AgentSystem.cs:97-101`).
- Disable the Mind **before its `Start` runs** (i.e. right after `Instantiate`): the `Start` methods have side effects on the guest —
  `VisitorMind.Start` calls `HeroStatistics.Instance.RegisterCustomerEnter()` (`VisitorMind.cs:233-236`); `QuestVisitorMind.Start` rolls a random quest and edits `currentVisitorsPendingQuests` (`QuestVisitorMind.cs:79-85`); `HaggleVisitorMind.Start` rolls a haggle offer (`HaggleVisitorMind.cs:79-92`); `ThiefVisitorMind.Start` is only visual set-up (`ThiefVisitorMind.cs:128-147`). `CommonVisitorMind.Awake` only caches `VisitorAnimations` (`CommonVisitorMind.cs:22-25`).
- Disable `PolyNavAgent`: with no path its `LateUpdate` still calls `Restrict()` every frame (`PolyNavAgent.cs:244-248`), which would clamp the puppet into the guest's local nav map **(clamp behaviour inferred from the method name and the `restrict` flag `:30`)**.
- `VisitorAnimations.LateUpdate` reads `m_VisitorBody.Speed` (= `PolyNavAgent.velocity.magnitude`, `PolyNav2DBody.cs:12-22`, `PolyNavAgent.cs:141`) and `Direction` every frame and overwrites the Animator bools (`VisitorAnimations.cs:124-170`). Either disable `VisitorAnimations` and set the bools yourself (then also create nothing: the bubble child is created in its `Start` `:73-107`, which you still want), or keep it enabled and feed it: write `PolyNavAgent.velocity` (public field `PolyNavAgent.cs:38`) for `"Moving"` and use `LookTo(..., overrideFacing: true)` for facing.
- Bubble exit callbacks on a puppet are harmless: they guard on `m_VisitorAgent.AgentSystem()` being non-null (`VisitorAnimations.cs:372-378`). But `CheckPlayUpdatePriceInNotebookAnimation` only fires if `PrepareItemValorationAnimation` was called (`:485-497`), so notebook cards on guests must be triggered explicitly.
- `Thief`, `QuestVisitor`, `HaggleVisitor` dereference `_visitorGenerator` without a null check in `OnKill` → NullReferenceException in `OnDestroy` for a puppet unless `SetVisitorGenerator` was called (`Thief.cs:14-17`). If you do pass the generator, `RemoveVisitor` is a no-op for agents not in `generatedVisitors` (`VisitorGenerator.cs:403`).
- Disable the `ThiefCatcher` trigger / `IndecisiveVisitorMind._collider2D` logic locally and send a request instead (the local checks read `HeroMerchant.Instance`, see 10).
- Do not put puppets in `VisitorGenerator.generatedVisitors` unless you want local code (door long-press check `ShopEntranceDoor.cs:445`, `AdviseVisitors`) to see them.
- Host side: the sim depends on bubble Animator state exits and on `PolyNavAgent.LateUpdate`, so the host must keep the shop objects active and animating even if its own hero is elsewhere **(inferred consequence of 3.1/3.5)**.

---

## 5. Day / night

- `public class TimeManagerV2 : MonoBehaviour` — `TimeManagerV2.cs:4`. `public static TimeManagerV2 Instance` (lazy `FindObjectOfType`, `:10-20`). `public DayTime CurrrentDayTime => currentDayTime;` (three r's, `:22`). `public event Action<DayTime> OnDayTimeChange;` (`:24`).
- `enum DayTime { Morning, Afternoon }` — `DayTime.cs:1-5`. "Afternoon" is the night state.
- `Init()` loads `currentGameSlot.dayTime` and fires the event (`:37-44`). `SwapDayTime()` (`:63-74`), `ChangeToMorning()` = `MoonlighterCalendar.AddDay(); currentGameSlot.daysPassed = MoonlighterCalendar.TotalDays;` + event (`:77-86`), `ChangeToAfternoon()` (`:89-96`). Saved in both save events (`:46-54`).
- There is no continuous clock. The only timed thing is the shop-open DOTween timer plus the visual night fade and day wheel (`ShopManager.cs:1158-1160`).
- **Day counter**: `MoonlighterCalendar` (`ScriptableObject` singleton, `MoonlighterCalendar.cs:3,35-53`): `TotalDays`, `Day`, `Month`, `Year`, `DayOfTheWeek`, `IsSunday`, `DaysPerWeek => 7`, 12 × 28-day months (`:27-33,55-73`); `Init(int daysPassed)` (`:80-96`) from `gameSlot.daysPassed` (`GameManager.cs:2436`); `public static void AddDay()` (`:98-113`).

What changes the time of day:
| Action | Code |
|---|---|
| Shop closes (player or timer) → Afternoon | `ShopManager.cs:1146` |
| Sleeping in the bed → swap | `Bed.cs:60` |
| Returning from a dungeon (any `OnTownLoaded`) → swap | `TimeManagerV2.cs:28,56-60` |
| Dungeon results "replay" after death → swap | `DungeonResultsPanel.cs:786` |
| End-game return forces Morning | `GameManager.cs:2157-2160` |
| Tutorial / DLC cinematics / debug HUD | `TutorialScene1.cs:323`, `TutorialScene2.cs:79`, `TutorialScene3.cs:157`, `DLCIntroManager.cs:434,533`, `EndDLCCinematic.cs:473`, `HUDDebug.cs:337,341` |

A load from the main menu does **not** swap (it fires `OnTownLoadedFromMainMenu`, `GameManager.cs:1630-1633`).

When the shop can open: `canOpenShop` is true only in `DayTime.Morning` (`ShopManager.cs:562-565`), and is forced false by the tutorial (`TutorialScene2.cs:155,318,378`) and by accepting the helper's sell contract (`ShopHelperEmployer.cs:192`).

Subscribers of `OnDayTimeChange`: `ShopManager` (×2), `CameraManager`, `MorningNightFilter`, `NightTownInstanceHandler` (toggles day-only objects and the banker/hawker/rival stalls, `NightTownInstanceHandler.cs:42-67`), `CitizenGenerator` (regenerates all citizens, `CitizenGenerator.cs:130-131`), `HeroMerchant` (quest expiry), `ShopkeeperInteractive` subclasses, `ShopHelperEmployer`, `StorytellerInteractive`, `FamiliarsYard`, `Roomba`, `TreeBehaviour`, `WandererDLCController`.

### Bed
`public class Bed : MonoBehaviour, IActionable` — `Bed.cs:7`. `private static Bed _instance;` (`:9`), `public bool blocked;` (`:11`), `public static TweenCallback ReturnToGameAction = ReturnToGame;` (`:15`, a replaceable static delegate).
- Long-press allowed when `!blocked && IsShopClosed && !IsWaitingToCloseShop && !IsShopWaitingForResult` (`:93-100`).
- `public void SleepInBed()` — `:43-54`: requires `ShopManager.Instance.IsShopClosed && HeroMerchant.Instance.heroMerchantController.canMove`; locks panels/pause, `GUIManager.Instance.FadeOut(ChangeDayTimeEffectFromBed)`, `DisableMoveInput()`.
- `private void ChangeDayTimeEffectFromBed()` — `:56-70`: `SaveLastPrizePerItem()`, `SaveCultureInfo()`, `TimeManagerV2.Instance.SwapDayTime()`, `SaveSlotOnGoToDungeon(serialize: true)`, `ShopManager.Instance.GameSlot_OnSaveToDungeon(currentGameSlot)`, `DOVirtual.DelayedCall(2f, ReturnToGameAction, ignoreTimeScale: false)`, `ItemPriceManager.Instance.RegisterEndOfRun()`, destroys the `ShopHelper`.
- `public static void ReturnToGame()` — `:72-86`: hides the hero art, fades in, `ShopManager.Instance.PlayWillBedAnimation()`, sets the day wheel.
- `ShopManager.PlayWillBedAnimation()` — `ShopManager.cs:805-815`: bed Animator `"WakeUp"`, hero `SetFlyingLayer()`, `ForbidMovement()`, `DisableMoveInput()`, `PreventPanelChange()`, `stopActionableFromAnimatic = true`, `GiveWillBedBenefits(bedLevel)`. On the bed state `"Bed_WakeUp"` exit (`:327`) → `OnAnimationFinished()` (`:846-871`): `TeleportTo(willStartPoint.position)`, re-enable art/movement, `CheckHelperInstance()`, `ApplySpeedFactor()`, fire `ShopManager.OnWillWakeAnimationEnded`.
- The same wake-up sequence plays when the game is loaded from the main menu (`GameManager.cs:1646-1652`).

---

## 6. Town NPCs and services

Base class for stall NPCs: `public class ShopkeeperInteractive : MonoBehaviour` — `ShopkeeperInteractive.cs:9`. `public ActionTrigger stand;` (`:53`); static shared dialogs `protected static CitizenDialog _dialog; protected static OptionsDialog _optionsDialog; protected static ShopkeeperInteractive _usingDialog; ... _usingOptionsDialog;` (`:80-86`). `OnWillInteract()` starts `ShowDialogs()` (`:130-190`), which calls `HeroMerchant.Instance.heroMerchantController.StopMovement()` and `GUIManager.Instance.canOpenPanels = false`, shows greeting tokens, then `OpenPanel()` or the two-option dialog (`OnOption1Selected` / `OnOption2Selected`). Virtuals: `Init`, `OpenPanel`, `RefreshHasToSeeSomething`, `TimeManagerV2_OnDayTimeChange`, `HasToPlayUnlockText`, `MarkUnlockTextAsPlayed`, `GetDialogParams` (`:88-128`).

Unlock state for all of them is in `TownboardSaveInfo` (`blacksmith/witch/rival/banker/hawker` × `Unlocked`, `Available`, `Seen`, `PlayedUnlockedText`, `TownboardSaveInfo.cs:7-45`); stall objects are toggled by `VillageManager.UpdateCharacters()` (`VillageManager.cs:55-84`).

| NPC | Classes | State it changes |
|---|---|---|
| **Blacksmith** (craft / upgrade gear) | `BlackSmithInteractive : ShopkeeperInteractive, IActionable` (`BlackSmithInteractive.cs:6`) → `GUIManager.Instance.GoToBlacksmithPanel()` (`:34-37`); `BlacksmithPanel : InventoryPanel` (`BlacksmithPanel.cs:11`) | `HeroMerchant.Instance.addGold(-selectedRecipe.GetTotalCost())`, ingredients via `ShopManager.Instance.RemoveItems(requiredItem, quantity)`, result via `heroMerchantInventory.TryAddItem` or dropped (`BlacksmithPanel.cs:447-474`); `townboardSaveInfo.blacksmithPlayedUnlockedText` (`BlackSmithInteractive.cs:48-51`); recipe "seen" flags (`RecipeManager`) |
| **Witch** (potions, enchant) | `WitchInteractive` (`WitchInteractive.cs:6`): option 1 `GoToWitchPotionsPanel()`, option 2 `GoToWitchEnchantmentPanel()` (`:67-77`); `WitchPanel`, `WitchPotionsPanel`, `WitchEnchantmentsPanel`; DLC `WandererWitchPanel`, `WitchPortal` | potions: `addGold`, `ShopManager.Instance.RemoveItems`, `TryAddItem` (`WitchPotionsPanel.cs:425-449`); enchant: `equipment.EnchantWith(recipe)` then `addGold(-recipe.labourCost)` and `RemoveItems` (`WitchEnchantmentsPanel.cs:396-403`) |
| **Banker** (investments) | `BankerInteractive : MonoBehaviour, IActionable` (`BankerInteractive.cs:10`, not a `ShopkeeperInteractive`) → `GUIManager.Instance.GoToBankerPanel(hasToInvest)` (`:198`); `BankerPanel : InventoryPanel` (`BankerPanel.cs:11`) | `GameSlot.bankerSaveInfo` (`BankerSaveInfo.cs:4-16`: `dayAccepted`, `moneyInvested`, `gathered`); invest: `dayAccepted = MoonlighterCalendar.TotalDays; moneyInvested = _moneyToInvest; gathered = false; addGold(-_moneyToInvest)` (`BankerPanel.cs:540-546`); gather: `addGold(moneyForToday); gathered = true; dayAccepted = -1` (`:520-526`). Daily interest is deterministic Perlin noise of (seed, day) (`:430-473`); week logic uses `MoonlighterCalendar.IsSunday/DayOfTheWeek` (`BankerInteractive.cs:135-142`). Unlocked after the Forest boss: `townboardSaveInfo.UnlockBanker()` (`GameManager.cs:2493-2496`). Day-only (`NightTownInstanceHandler.cs:58-61`) |
| **Retailer stall** ("Rival" in code) | `RivalInteractive` (`RivalInteractive.cs:5`) → `GoToRivalPanel()`; `RivalPanel : InventoryPanel`, `RivalStandItems` | buys materials: `addGold(-1 * currentPrice)`, `TryAddItem` or drop (`RivalPanel.cs:403-426`). Blocked at night (`RivalInteractive.cs:12-22,56-59`) |
| **Decorator stall** ("Hawker" in code) | `HawkerInteractive` (`HawkerInteractive.cs:6`) → `GoToHawkerPanel()`; `HawkerPanel : InventoryPanel`, `DecorationBuyPanel` | buys a decoration item: `addGold(-num)`, `TryAddItem` (`HawkerPanel.cs:344-346`); owned decorations = `GameSlot.decorativeItemsDiscovered` (added in `DecorativeItem.Consume()`, `DecorativeItem.cs:12-17`). Blocked at night (`HawkerInteractive.cs:17-27,61-64`). Becomes available with shop level ≥ 1 (`TownBoardShopPanel.cs:170-173`) |
| **Shop decorations and buffs** | `DecorativeSlot : MonoBehaviour, IActionable` in the shop (`DecorativeSlot.cs:6`), `DecorativePanel : Panel`, `DecorativeItemMaster : AutoConsumableItemMaster` with `ShopStatMod shopStats` and `enum DecorationPlace { Wall, Table }` (`DecorativeItemMaster.cs:5-43`) | `public void SetDecorativeItem(DecorativeItemMaster dm)` subtracts the old item's mods from and adds the new item's mods to `ShopManager.Instance.shopStats[...]` for `fasterWalkers, generousCustomers, repelThieves, luxury, longerDays, patientCustomers, overcapacity, delay, hagglersReducedItems` (`DecorativeSlot.cs:55-100`). Only usable while the shop is closed (`IsInstantActionBlocked() => !ShopManager.Instance.IsShopClosed`, `:112-115`). Saved as `shopSaveInfo.shopDecorativeItems` (names by slot index) |
| **Town board** | `TownBoardInteractive : ShopkeeperInteractive, IActionable` (`TownBoardInteractive.cs:5`): option 1 `GoToTownboardStaffPanel()`, option 2 `GoToTownboardShopPanel()` (`:43-53`); `TownBoardPanel`, `TownBoardStaffPanel`, `TownBoardShopPanel` | Staff: `addGold(-_selectedCost)`, `townboardSaveInfo.UpgradeBlacksmith/Witch/Rival/Banker/Hawker()` (sets `*Unlocked`), `VillageManager.Instance.OnUnlockCharacter(type)` → `TownCinematics.PlayUnlockCharacterAnimation(...)` (`TownBoardStaffPanel.cs:136-164`, `VillageManager.cs:86-116`). Shop: see 2.5 |
| **Customer quests** | `QuestVisitor*`, `QuestPanel`, `QuestManager`, `ActiveQuest` (3.7) | `HeroMerchant.activeQuests` / `completedQuests` (`HeroMerchant.cs:41-43`) → `GameSlot.willActiveQuests` / `completedQuests` |
| **Shop assistant** | see 6.8 | |
| **Townspeople** | `CitizenGenerator`, `Citizen : MonoBehaviour, IActionable` (`Citizen.cs:11`), `SpecialCitizen`, `PostmanInteractable` | random walkers on the nav map (`UnityEngine.Random`, `Citizen.cs:126-211`); talking only freezes the hero (`:317,488-507`). Regenerated on every day-time change |
| **Storytelling / Zenon** | `StorytellerInteractive`, `ZenonStoryteller`, `WillStoryteller` | `GameSlot.storiesSaveInfo` (`StorySaveInfo`: `key, isRead, hasBeenSelected`) (`StorytellerInteractive.cs:310-343`) |
| **Dungeon gates** | `WorldDungeonDoor : MonoBehaviour, IActionable` (`WorldDungeonDoor.cs:7`), `public static Action<ItemMaster.Culture, bool> OnDungeonPortalActivate;` (`:9`); DLC `WandererWorldDungeonDoor`; `EmblemPortal` | scene change to the dungeon (out of scope here) |

### 6.8 Shop assistant
- `ShopHelper : Agent` + `ShopHelperMind` + `ShopHelperEmployer : MonoBehaviour, IActionable` (`ShopHelperEmployer.cs:9`). Instance is managed by `ShopManager.CheckHelperInstance(...)` — `ShopManager.cs:278-317`: lives under `references.shopHelperStartPoint`, active when `CurrrentDayTime == DayTime.Morning` or there are sold items to collect; instantiates `shopHelperPrefab` if missing.
- On shop open: `ShopHelperEmployer.ShopManager_OnShopOpen()` → `_helperMind.ChangeToGuardMode()` (Animator trigger `"Topknot"`), blocks its trigger, empties the helper chest (`ShopHelperEmployer.cs:52-59`). `ShopHelperMind.GuardModeChanged()` (`ShopHelperMind.cs:350-355`) registers the agent (no code caller; animation event **(inferred)**). As a guard it patrols interest points and pursues thieves announced by `ThiefStartRobbing` (`:146-308`).
- Shop closed, talking (`OnWillInteract` `ShopHelperEmployer.cs:82-120`): if `shopSaveInfo.helperSoldItems` and the saved helper chest has items → pays `Σ GetCorrectPriceWithPopularity × quantity × (1 - shopHelperSellPercentage)` via `addGold`, clears the chest, opens the results panel with `isTalkingWithHelper: true`. Otherwise offers the **sell contract** (`SellDialog` `:180-202`): on accept `ShopManager.Instance.canOpenShop = false`, `_helperMind.ChangeToSellMode()` (shows `references.shopHelperChest`, `ShopHelperMind.cs:357-366`).
- `helperSoldItems` is set when saving with items in the helper chest (`ShopManager.cs:638-649`).
- `GuardDialog()` (hire for `shopHelperGuardCost`) exists but has no caller in the decompiled code (`ShopHelperEmployer.cs:248`).

### 6.9 Wanderer DLC town additions
- `Moonlighter.DLC.WandererDungeon.WandererDLCController` (`public static WandererDLCController Instance { get; private set; }` `WandererDLCController.cs:54`, `public WandererDLCSaveInfo saveInfo;` `:19`) implements `Moonlighter.DLC.IDLCController` (`IDLCController.cs:7-28`: `LoadTownBundles`, `LoadAsset<T>`, `LoadAssets<T>`, …). Its save is JSON inside `GameSlot.dlc[i].data` (`:116,391,400`).
- `Moonlighter.DLC.WandererDungeon.Town.DLCSpawner` spawns DLC town prefabs; `GameManager` waits for `DLCSpawner.IsEverythingSpawned()` (`GameManager.cs:1592-1600`).
- `DLCTownNPCUnlocker` (`DLCTownNPCUnlocker.cs:8-119`): on town load, shows the **Worker** when `shopLevel == 4 && maxLevelReachedInWandererDungeon >= 1` (`:97-105`); shows the **HagglerUnlocker** when `maxLevelReachedInWandererDungeon >= 3 && !hagglerUnlocked` (`:86-95`); spawns the haggler stall if `saveInfo.hagglerUnlocked` (`:64-67,112-118`).
- `Worker : MonoBehaviour, IActionable` sells shop level 5 (2.5). `HagglerUnlocker : MonoBehaviour, IActionable` charges coins to unlock; `WandererHagglerSpawner.UnlockHaggler()` sets `saveInfo.hagglerUnlocked = true` (`WandererHagglerSpawner.cs:32-38`). `WandererHaggler : MonoBehaviour, IActionable` is the town barter NPC (`WandererHagglerPanel`), state in `saveInfo.hagglerInfo` (`WandererDLCHagglerSaveInfo`: `expireDay`, `alreadyHaggledItems`).
- Other DLC town NPCs: `WandererInDisguise`, `WitchPortal`, `WandererDungeonLiftAccess`/`WandererDungeonLiftUnlock` (lift to dungeon floors), `WandererNPCAppearance`. Cinematics: `Moonlighter.DLC.WandererDungeon.Cinematics.DLCIntroManager`, `EndDLCCinematic`.
- In the shop: DLC visitor spawners (3.3), shop prefab level 5, `shopStats["hagglersReducedItems"]`.

---

## 7. Storage chests

- `public class Chest : MonoBehaviour, IActionable` — `Chest.cs:9`. `enum chestType { WOOD, IRON, GOLD, BLUE, PURPLE, PIRATE, PEDESTAL, SHOP, SLIME, SHOP_5 }` (`:23-35`). `public int capacity = 28;` (`:47`). Contents: `private List<ItemStack> _chestContent` exposed as `public List<ItemStack> ChestContent { get; set; }` (`:67,85-95`) — a fixed-size list indexed by slot, null = empty; items are live `ItemStack` GameObjects parented under a hidden `container` (`:494-497`). Lookup index `private Dictionary<ItemMaster, HashSet<int>> _itemIndexByItemName` (`:65`).
- Events: `public event Action<ItemStack, int> OnItemAdded; public event Action<int> OnItemChanged; public event Action<ItemStack, int> OnItemRemoved; public event Action OnChestClosed; public event Action OnChestOpen;` (`:156-164`). Static `public static Chest currentChestOppening;` (`:75`).
- Mutators: `public bool SetItem(ItemStack item, int index)` (`:465-509`, the low-level funnel; fires Added/Removed), `public bool TryAddItem(ItemStack item, int start = 0)` (`:544-574`, merges stacks, fires `OnItemChanged`), `public int RemoveItem(ItemMaster item, int quantity = 1)` (`:430-463`), `public void Empty()` (`:227`), `public void SetCapacity(int capacity)` (`:206`), `public void SetChestFromSave(ChestSaveInfo save)` (`:974`), `internal void LoadFromSave(ChestSaveInfo savedChest)` (`:857`).
- UI: `OpenChest()` (`:600-673`) freezes the hero and after 1.5 s `OpenChestGUI()` (`:675-698`) → `GUIManager.Instance.GoToChestPanel(this)` or `GoToSalesBox(this)`; `ChestInventoryPanel` / `SalesBoxInventoryPanel` (both pause the game); `ChestSlotGUI.SetItemStack` writes through `ownerChest.SetItem(stack, slotIndex)` + `RefreshIndex()` (`ChestSlotGUI.cs:31-47`).
- Usable in the shop when: `IsShopClosed || (!IsShopWaitingForResult && !IsWaitingToCloseShop)` and not already open (`Chest.cs:742-749`) — i.e. also while the shop is open.
- Shop chests:
  - `references.shopChests` (`List<Chest>`) — storage room chests.
  - `ShopManager.bedChest` — spawned from `Resources "Shop/Bed_Chest_Lvl{level}"` with capacity `bedChestSizePerLevel[level]` (`ShopManager.cs:331-349`).
  - `references.salesBox`, `references.shopHelperChest`.
  - Mimic familiar chest (`GameSlot.mimicFamiliarChest`, `ShopManager.cs:1393-1413`).
- Aggregated item API used by crafting/quests: `ShopManager.GetItemCount(ItemMaster)` (`:1454-1457`) and `public int RemoveItems(ItemMaster item, int quantity = -1)` (`:1459-1474`) — order: hero inventory → mimic familiar chest → shop chests → bed chest. `GetChestItemCount`, `TryRemoveChestItem`, `RemoveItemFromChests`, `GetChestItems`, `GetAllItemsOfType` (`:1296-1490`).
- Items rescued from a dungeon (children of scene object `"RescuedItems"`) are pushed into `shopChests` at shop init, overflow dropped on the floor (`ShopManager.cs:548,722-768`).

---

## 8. Save data

Root: `[Serializable] public class GameSlot` — `GameSlot.cs:6`. Runtime instance `GameManager.Instance.currentGameSlot`. Save hooks: `public event Action<GameSlot> OnSaveToDungeon;` / `OnSaveFromDungeon` (`GameManager.cs:254-256`), raised by `SaveSlotOnGoToDungeon(bool serialize = false)` (`:2464-2481`) and `SaveSlotOnReturnFromDungeon` (`:2483-2522`); `SaveAll()` (`:2452-2462`).

| What | Field | Written by |
|---|---|---|
| Shelf contents + prices | `GameSlot.shopSaveInfo.shopShowcases` (`List<ItemSaveInfo>`, one entry per `ShopManager.itemPoints` index, empty slot = `name == ""`); price in `ItemSaveInfo.sellingPrize` | `ShopManager.GameSlot_OnSaveToDungeon` `ShopManager.cs:655-660`; `ItemSaveInfo(ItemStack)` `ItemSaveInfo.cs:42-73` |
| Storage chests | `shopSaveInfo.shopChests` (`List<ChestSaveInfo>`), `bedChest`, `salesBox`, `helperChest`, `helperSoldItems` | `ShopManager.cs:627-654`; `ChestSaveInfo(Chest)` copies every slot incl. empties (`ChestSaveInfo.cs:14-24`) |
| Decorations placed | `shopSaveInfo.shopDecorativeItems` (`List<string>`, null for empty slot) | `ShopManager.cs:661-672` |
| Decorations owned | `GameSlot.decorativeItemsDiscovered` | `DecorativeItem.cs:12-17` |
| `ShopSaveInfo.haveToUpgrade` | declared only; no other reference found | `ShopSaveInfo.cs:21` |
| Prices known (notebook), popularity, sales counters | `GameSlot.itemPriceInfo` (`ItemPriceManagerInfo`: `runsToEndCycle`, `List<ItemPriceInfo> itemPrizeInfos`) | `ItemPriceManager.GameManager_OnSaveToDungeon` `ItemPriceManager.cs:90-98`; loaded in `Init()` `:54-80` |
| Legacy last price | `GameSlot.lastPrizePerItem` (`List<ShowcasePrizeSaveInfo>`) ↔ `GameManager._itemToLastPrice`; `SetLastPrizeFor` has no caller outside `GameManager` | `GameManager.cs:174,2202-2220,2524-2536` |
| Shop level + sub-upgrades, NPC unlocks | `GameSlot.townboardSaveInfo` (`TownboardSaveInfo.cs:5-84`) | mutated in place by the town board panels, `Worker`, tutorial, `SaveSlotOnReturnFromDungeon` (`UnlockShop((int)bossDefeatedCulture)`, `GameManager.cs:2491-2492`) |
| Quests | `GameSlot.willActiveQuests` (`List<ActiveQuest>`), `completedQuests` (`Dictionary<int, ActiveQuest>` keyed by `completeDay`) | `HeroMerchant.OnSave` `HeroMerchant.cs:398-410` |
| Day number / time | `GameSlot.daysPassed`, `GameSlot.dayTime` | `TimeManagerV2.cs:46-54,80` |
| Gold | `GameSlot.willGold` (runtime: `heroMerchantStats.gold`, `HeroMerchant.cs:958-975`) | hero save |
| Banker | `GameSlot.bankerSaveInfo` | `BankerPanel.cs:520-546` |
| Stories read | `GameSlot.storiesSaveInfo` | `StorytellerInteractive.cs:310-343` |
| Progress gates | `GameSlot.culturesDefeated` (bitmask by `ItemMaster.Culture`), `maxDungeonLevelVisited[8]`, `playedTutorial`, `wasDlcCompleted` | `GameManager.cs:2491`, `GameSlot.cs:54-56,20,80` |
| DLC | `GameSlot.dlc` (`List<DLCSaveInfo>`: `id, nameKey, data` JSON of `WandererDLCSaveInfo`) | `WandererDLCController.cs:391,400` |
| Items discovered | `GameSlot.itemsDiscovered`, `recipesSeen`, `enchantmentsSeen` | `GameManager.SaveCultureInfo` `GameManager.cs:2538-2564` |

`ItemSaveInfo` fields: `prefabName, plusLevel, name, quantity, sellingPrize, curseName, enchantmentLevel, enchantmentType, enchantmentEffectName` (`ItemSaveInfo.cs:14-30`). Rebuild recipe used everywhere: `ItemDatabase.GetItemByName(name, plusLevel)` → `ItemStack.Create(master, quantity)` → `Equipment.EnchantWith(...)` × `enchantmentLevel` (`ShopManager.cs:165-176`). Note the loader ignores `curseName` for shelf items (`:162-178`).

Saves happen automatically at: shop close fade (`ShopManager.cs:1093`), sleeping (`Bed.cs:58-62`), going to a dungeon at floor 0 (`GameManager.cs:1752-1755`), rescued-item recovery (`ShopManager.cs:763-767`).

---

## 9. Tutorial and scripted town sequences

### 9.1 Gate
- `GameSlot.playedTutorial` (int, 0–4; `TutorialManager.SCENE_COUNT = 4`, `TutorialManager.cs:11`). On slot load: `if (gameSlot.playedTutorial != 4) { LoadTutorial(gameSlot.playedTutorial); yield break; }` else `LoadTownScene()` — `GameManager.cs:2439-2448`.
- `LoadTutorial(int scenes)` instantiates `Resources "@TutorialManager"` and calls `Start(scenes)` (`:2027-2032`); each finished scene sets `currentGameSlot.playedTutorial = scene + 1` (`:2040-2043`). Scenes are prefabs `"Tutorial/Scenes/Scene" + scene` deriving from `abstract class TutorialScene` (`StartScene/InterruptScene/Dispose/EndScene`, `TutorialScene.cs:4-21`). Static hooks `public static Action<int> OnTutorialSceneFinished; public static Action OnTutorialFinished;` (`TutorialManager.cs:7-9`).
- `TownBoardInteractive` hides its "!" while a `TutorialManager` exists (`TownBoardInteractive.cs:11`).

### 9.2 Scenes that touch the town/shop
- `TutorialScene0`: intro video/animatics; `TutorialScene1`: tutorial dungeon; ends with `TimeManagerV2.Instance.ChangeToAfternoon()` (`TutorialScene1.cs:323`).
- **`TutorialScene2` — first morning, grandpa (Zenon, "green old man" in code), first shop opening** (`TutorialScene2.cs`):
  - `StartScene()` (`:76-82`): `ChangeToMorning()`, `GameManager.Instance.LoadTownSceneFromTutorial(PrepareScene, Scene7)` (a separate loader that does **not** fire `OnTownLoaded`, `GameManager.cs:1976-2018`).
  - `PrepareScene()` (`:84-136`): replaces the bed with a scripted animation object, spawns the old man as a `DummyAgent` on the `TutorialManager`'s own `AgentSystem` (`:100-102`), hides hero art and `TutorialUtils.PreventWillMovement()`, puts two "Crystalized Energy" stacks with preset prices on `shopShowcases[0]` slots 1 and 2 (`:107-116`), sets `showcase.blocked = true`, **clears `VisitorGenerator.visitorSpawners`** (`:119`), `BlockShopInteractions()`.
  - `public static void BlockShopInteractions()` (`:147-156`): `shopEntranceDoor.blocked = true`, `bedChest.isLocked = true`, every `shopChests[i].isLocked = true`, `canOpenShop = false`.
  - `Scene9_4` (`:364-373`): `canOpenShop = true; ShopManager.OnShopOpen += Scene9_5;`. `Scene9_5` (`:375-384`): `canOpenShop = false`, stops the day wheel, `TutorialUtils.SpawnVisitorAndWaitToSell(firstVisitorPrefab, Scene9_6, ItemPriceValoration.Cheap)` (waits on `CashPoint.OnItemSold`, `TutorialUtils.cs:98-104`). `Scene9_7` (`:398-405`): `SpawnVisitorAndWaitToOpinion(secondVisitorPrefab, Scene9_8, ItemPriceValoration.TooExpensive)` (waits on `ItemPriceManager.Instance.OnValorationMade`, `TutorialUtils.cs:90-96`). Then sends `CloseShopMessage` to `generatedVisitors[0]` (`:415`).
- **`TutorialScene3`** — first night: `PrepareTown()` deactivates `villageEntranceDoor`, `ChangeToAfternoon()`, `ShopManager.Instance.PreventShopFromCloseAfterTime()`, blocks the town board (`TutorialScene3.cs:155-165`); `EndScene()` (`:471-488`) sets `HeroMerchant.Instance.canUseEmblem = true`, `GameManager.Instance.bossDefeatedCulture = ItemMaster.Culture.Merchant`, `townboardSaveInfo.UnlockBlacksmith()` / `UnlockWitch()`, `SerializeGameSlot()`.
- Helpers: `TutorialUtils` (static; `StartConversation`, `MoveAgentTo`, `PreventWillMovement`, `AllowWillMovement`, highlight helpers — `TutorialUtils.cs:48-175`), `TutorialChecker` components, `TutorialScene1.BlockMerchantUtilities()` / `TutorialScene3.UnblockMerchantUtilities()` (`TutorialScene1.cs:78-95`, `TutorialScene3.cs:90-104`).

### 9.3 Post-tutorial town cutscenes (`TownCinematics`)
- Zenon talk after each boss: `GameManager.bossDefeatedCulture` (default `Developer` = "nothing pending", `GameManager.cs:123`) selects `"CITIZENS/" + { "ZENON_TALK_POSTTUT", "ZENON_TALK_GOLEM", "ZENON_TALK_FOREST", "ZENON_TALK_DESERT", "ZENON_TALK_TECH" }[(int)bossDefeatedCulture]` (`TownCinematics.cs:281-292`); entry `PlayBossDoorUnlockAnimation(bool isWillDead = false)` (`:180-226`), then `TeleportGranpaToSpot()` (`:263-`) which freezes the hero and walks him with `MoveAutomaticallyToPoint`. Afterwards `culturesDefeated |= ...` and `bossDefeatedCulture = Developer` (`:427-432`).
- Shop upgrade build: `PlayShopUpgradeAnimation()` (`:505-521`) blocks **every** `ActionTrigger` in the scene, freezes the hero, moves the camera; ends in `ReturnFromShopUpgrade()` (`:621-632`); `public static Action OnShopUpgraded;` (`:12,676-679`); the next shop entry uses `OpenDoorFromOutsideAndShowTutorial()` (`:674`, `ShopEntranceDoor.cs:160-212`) which shows tip panels `"Tutorial/Lvl{shopLevel}_{n}"`.
- NPC stall build: `PlayUnlockCharacterAnimation(...)` (`:692-727`) → `OnWorkerReturnedUnlock()` (`:802-818`).
- DLC: `DLCIntroManager`, `EndDLCCinematic` (gate `WandererDLCSaveInfo.hasPlayedDLCIntro`).

### 9.4 How dialogues/cutscenes freeze the hero
There is no global "cutscene" flag; each sequence toggles these independently:
- `HeroMerchantController.ForbidMovement()` / `AllowMovement()` (`canMove`, also drives `_canAttack`) — `HeroMerchantController.cs:297-312,566-578`; `StopMovement()` (`:580-585`).
- `DisableMoveInput()` / `EnableMoveInput()` (`_isMoveInputEnabled`) — `:423-440`.
- `public bool stopActionableFromAnimatic;` (`:247`, set during the bed wake-up `ShopManager.cs:813,852`).
- `TeleportTo(Vector3 newPosition)` (`:868-877`), `MoveAutomaticallyToPoint(...)`, `DisableHeroMerchantArt()` / `EnableHeroMerchantArt(bool showArmor = true)` (`:3859-3872`).
- GUI: `GUIManager.Instance.canOpenPanels` (`PreventPanelChange()` / `AllowPanelChange()`, `GUIManager.cs:115,649-657`), `canOpenPause` (`:111`), `FadeOut(Action callback = null, float duration = 0.5f)`, `FadeIn(Action callback = null, float duration = 0.5f, bool activeHUD = true)`, `FadeBoth(Action inCallback = null, Action outCallback = null, float duration = 0.5f, bool activeHUDOnFadeIn = true)` (`:554-610`), `public static event Action<Panel> OnPanelChange;` (`:181`).
- Interaction lock: `ActionTrigger.BlockInteraction()` / `UnblockInteraction()` / `DeactivateWithCollider()` / `ActivateWithCollider()` (`ActionTrigger.cs:237-273`); `executeAction()` also self-blocks for 1 s after each use (`:320-331`).
- Panels pause time: `GameManager.PauseGame(float delay = 0f)` / `UnpauseGame()` set `Time.timeScale` and raise `public static event Action<bool> OnGamePaused;` (`GameManager.cs:260,2592-2617`).

---

## 10. Static / singleton code that assumes one hero

Interaction plumbing (applies to everything):
- `ActionTrigger` reacts to **any** collider tagged `"Player"` or `"ActionArea"` (`ActionTrigger.cs:95-117`), keeps a single `_isInside` bool, and `executeAction()` requires `GUIManager.Instance.CurrentInventory == null` (`:320-331`). A remote-player puppet carrying those tags will light up prompts, enter `TravelTo*Zone`s (`TravelToDungeonZone.cs:11`, `TravelToVillageZone.cs:72`), trigger `CameraArea` (`CameraArea.cs:23`) and be teleported by `ReturnWillToShop` (`ReturnWillToShop.cs:14-17`).
- The hero's `ActionArea` (`ActionArea.cs:4-134`) picks the nearest trigger; `HeroMerchantController.InteractWithActionableObject()` calls `firstActionTrigger.executeAction()` (`HeroMerchantController.cs:2927-2945`).

Shop:
| Assumption | Where |
|---|---|
| All sale/penalty/sales-box/quest/thief gold goes to `HeroMerchant.Instance.addGold` | `CashPoint.cs:113`, `VisitorMind.cs:684,1311,1339`, `ShopManager.cs:1131`, `QuestVisitorMind.cs:267`, `QuestPanel.cs:244`, `ThiefVisitorMind.cs:630,741` |
| Register thief checks distance of `HeroMerchant.Instance` to the till (< 50) and steals a % of `HeroMerchant.Instance.GetCurrentGold()` | `ThiefVisitorMind.cs:562-568,635-641` |
| Thief/bird catch tests the **local** hero's abilities regardless of which collider entered | `ThiefCatcher.cs:14`, `BirdVisitorMind.cs:297` |
| Catch teleports and freezes `HeroMerchant.Instance`, and returns the item to its inventory | `ThiefVisitorMind.cs:715-723,750-771`, `BirdVisitorMind.cs:250-254` |
| Bird hint uses distance to `HeroMerchant.Instance` | `BirdVisitorMind.cs:87` |
| `ShopManager.isWillInShop` single bool; drives helper visibility, door bell (`ShopEntranceDoor.cs:373-379`), slime pet, shop speed effect | `ShopManager.cs:95-111,895-929` |
| Shop speed effect parented to `HeroMerchant.Instance.transform` (`private static SpeedEffect shopSpeedEffect`) | `ShopManager.cs:79,782-794` |
| One PolyNav2D map moved to the hero's area | `ShopManager.cs:895-942` |
| Showcase/chest/bed/quest panels are local modal UIs that **pause the game** and assume a single editor; shelf price edit uses `ShowcaseSlot.canPick = false` as its only lock | `ShowcaseInventoryPanel.cs:93,154,166` |
| Only one `GUIManager.currentInventory`; `Chest.currentChestOppening` static | `GUIManager.cs:19`, `Chest.cs:75` |
| `CashPoint.visitorBuying`, `QuestPoint.questVisitor/haggleVisitor` single slots; `QuestPoint.OnWillInteract` forbids local hero movement | `CashPoint.cs:14`, `QuestPoint.cs:7-9,19-30` |
| `Showcase.Open` requires local `HeroMerchant.Instance.currentMode == ShopMode` and freezes the local hero | `Showcase.cs:58-75` |
| `DoorToStorageCameraSwap` caches `HeroMerchant.Instance.transform` and moves the shared camera | `DoorToStorageCameraSwap.cs:5-26` |
| Bed: `SleepInBed` checks/locks the local hero; wake-up animation teleports `HeroMerchant.Instance` to `willStartPoint`; `private static Bed _instance` | `Bed.cs:9,43-54`, `ShopManager.cs:805-871` |
| Leaving/entering the shop teleports `HeroMerchant.Instance`, switches its mode, moves the nav map and camera | `ShopEntranceDoor.cs:293-355` |
| `VisitorMind.IsAnyoneBuying()` / `ThiefVisitorMind.IsAnyoneBusy()` scan **all** loaded minds with `Resources.FindObjectsOfTypeAll` (would include puppets' disabled minds; their state fields default to `Visiting`, so they read as "not buying") | `VisitorMind.cs:1394-1405`, `ThiefVisitorMind.cs:803-835` |
| Statics shared by all minds: `CommonVisitorMind._showcasePoints/_interestingPoints/_salesBoxPoint`, `QuestVisitorMind.currentVisitorsPendingQuests`, `ThiefVisitorMind._lastItemMasterNameStolen`, `AgentSystemMessage.m_LastId` | `CommonVisitorMind.cs:6-10`, `QuestVisitorMind.cs:63`, `ThiefVisitorMind.cs:98`, `AgentSystemMessage.cs:3` |
| Quest state lives on the hero (`HeroMerchant.Instance.activeQuests`); quest spawns read it | `QuestVisitorSpawner.cs:77,85-87,127`, `QuestVisitorMind.cs:79,274-276` |
| Item counts/removal for crafting and quests combine the local hero's bag with shared shop chests | `ShopManager.cs:1454-1474` |
| `ShopHelperEmployer.Update` looks at `HeroMerchant.Instance` within 20 units | `ShopHelperEmployer.cs:298-316` |
| Town NPC dialogs: static shared `_dialog` / `_optionsDialog`; freeze `HeroMerchant.Instance` and `GUIManager.Instance.canOpenPanels` | `ShopkeeperInteractive.cs:80-86,139-142,301-306` |
| Town cutscenes freeze `HeroMerchant.Instance`, steal the single camera and block every `ActionTrigger` | `TownCinematics.cs:505-521,692-727` |
| Day/night, calendar, price knowledge, statistics and shop stats are process-wide singletons (`TimeManagerV2.Instance`, `MoonlighterCalendar.Instance`, `ItemPriceManager.Instance`, `HeroStatistics.Instance`, `ShopManager.Instance.shopStats`) | `TimeManagerV2.cs:10-20`, `MoonlighterCalendar.cs:35-53`, `ItemPriceManager.cs:16-33`, `HeroStatistics.cs:5-7` |

### Suggested hook points (summary)
- Spawn/despawn: `VisitorGenerator.SpawnVisitor` (postfix), `VisitorGenerator.RemoveVisitor` / `VisitorGenerator.OnVisitorLeft`, `BirdVisitorSpawner.TrySpawn`.
- Visuals: `VisitorAnimations.PlayBubbleAnimation`, `PlayBubbleAnimationWithSprite`, `LookTo`, `OverrideFacing*`; `CashPoint.PlaceItem`; `ThiefVisitorMind.ShowCaughtHUD` / `StartFight` (private).
- Shelf state: `Showcase.SetItemInPosition`, `ShowcaseSlot.SetItem`, `ShowcaseSlot.RetrieveItem`, `ShowcaseSlot.SetPrice`, `ShowcaseSlotGUI.CommitEditingPrize`.
- Sales: `CashPoint.TryToSellItem`, `CashPoint.OnItemSold`, `ItemPriceManager.OnValorationMade`, `ItemPriceManager.RegisterItemPricePerOpinion`, `HeroMerchant.addGold`.
- Shop state: `ShopManager.OpenShop` / `CloseShop` / `OnShopOpen` / `OnShopClose` / `OnShopInited`, `ShopEntranceDoor.OpenShop` / `CloseShop` / `GoToShop` / `GoToVillage`, `ShopManager.UpgradeShopTo`.
- Catching: `ThiefCatcher.CatchThief()` (public, sends the message), `IndecisiveVisitor.SendWillAttendedCustomerMessage()` (public).
- Time: `TimeManagerV2.OnDayTimeChange`, `ChangeToMorning` / `ChangeToAfternoon` / `SwapDayTime`, `Bed.SleepInBed`, `Bed.ReturnToGameAction`.
- Area: `TownManager.OnGoToShop` / `OnGoToDungeonsEntrances`, `TravelToVillageZone.GoToVillage`, `ShopManager.PlacePolynavIn*`.
- Chests: `Chest.SetItem`, `Chest.TryAddItem`, `Chest.RemoveItem`, events `OnItemAdded/OnItemChanged/OnItemRemoved`.

---

## Appendix A. Day wheel and night filter (visual clock while the shop is open)

- `DayAfternoonPanel` (`GUIManager.Instance.dayAfternoonPanel`, `GUIManager.cs:32`):
  - `SetAngle(DayTime time)` — Morning = 180°, Afternoon = 0° (`DayAfternoonPanel.cs:65-78`).
  - `public void StartWheelAnimation(float time)` — `DayAfternoonPanel.cs:88-112`: `transformDayTimeWheel.DORotate(new Vector3(0f, 0f, 180f), time).SetRelative(true).SetUpdate(isIndependentUpdate: false)`; hides on complete. Hides itself while the door's long-press trigger is charging (`:114-128`).
  - `PauseWheelAnimation()` / `ResumeWheelAnimation()` / `StopWheelAnimation()` (`:127-150`), `PlayWheelBounceAnimation()` (`:80-86`).
- `MorningNightFilter` (`CameraManager.Instance.nightFilter`, `CameraManager.cs:103,183`):
  - `UpdateTime()` — `MorningNightFilter.cs:58-71`: picks `shopNightFilter` vs `townNightFilter` from `ShopManager.Instance.isWillInShop` (local hero!), fade 1 when the town is loaded and `currentDayTime == Afternoon`, else 0.
  - `public Tween SetFade(float v, float time)` (`:84-90`, `SetUpdate(isIndependentUpdate: false)` so it stops with `timeScale = 0`), `PauseFade()`, `ResumeFade()`, `StopFade(bool complete = false)` (`:92-114`).
- Sync note: shop-time remaining on the host = remaining time of `ShopManager.changeTimeAndCloseCall` (private `Tween`, `ShopManager.cs:87`; DOTween `Elapsed()`/`Duration()`), the wheel tween and the night fade are three independent tweens started together in `OpenShop()` (`ShopManager.cs:1158-1160`). All three are scaled time, so every host panel that calls `PauseGame()` pauses the whole shop day (while `AgentSystem` keeps ticking customers on real time — `AgentSystem.cs:90`; customers still move only via `PolyNavAgent` `Time.deltaTime`, so they freeze in place but their wait timers `_lapse` keep running) **(the "timers keep running" part follows from AgentSystem using real time; inferred)**.

## Appendix B. Tutorial coverage note
Tutorial scenes 0–3 are covered in section 9; `TutorialScene0` (intro video) and `TutorialScene1` (tutorial dungeon, `TutorialScene1.cs:97-405`) do not touch the shop except `ChangeToAfternoon()` (`TutorialScene1.cs:323`) and `BlockMerchantUtilities()` (`:78-95`). For co-op the simplest gate is to require `GameSlot.playedTutorial == 4` before allowing a session (`GameManager.cs:2439`) **(recommendation)**.
