# Moonlighter 1.15.8: Hero, Inventory, Items (decompiled code map)

Source: `moonlighter/decomp/Assembly-CSharp` (ILSpy). All citations are `File.cs:line` in that folder. "inferred" means not read directly in code.
Constants come from the `constants` TextAsset (`Constants.ReadFile`, Constants.cs:41-45); values quoted below were grepped from `testgame/Moonlighter_Data/resources.assets` (bagSize 20, pocketSize 5, kBaseHealth 100, kBaseSpeed 185, kBaseStrength 5, kBaseDefense 10, kMaxDefense 50, kInitGold 200, kMaxShield 4, attackTickTime 0.95, tileSize 24, willFallDamage 0.05, HPPotionI..VIII = 40/75/200/500/750/1500/2500/4500, ngpPotionsHealFactor 8.5, maxWeaponEnchantmentLevel 3, maxEnchantmentLevel 1, itemMoveToWillForce 48).

---

## 1. HeroMerchant (singleton)

`public class HeroMerchant : MonoBehaviour` (HeroMerchant.cs:12)

Singleton:
- `private static HeroMerchant _instance;` (HeroMerchant.cs:47)
- `public static HeroMerchant Instance` falls back to `FindObjectOfType<HeroMerchant>()` (HeroMerchant.cs:91-101).
- `Awake()` destroys any second HeroMerchant GameObject: `else if (this != _instance) Destroy(base.gameObject)` (HeroMerchant.cs:330-340). **A cloned hero prefab will self-destroy** unless the component is removed/disabled before Awake, or `_instance` is managed.
- `Init()` forces `_instance = this` then calls Stats/Inventory/Controller `Init()` and subscribes many GameManager events (HeroMerchant.cs:342-380). `OnDestroy()` sets `_instance = null` and destroys familiars (HeroMerchant.cs:463-489).

Creation: prefab loaded from bundle `hero_merchant`, asset `"Assets/ResourcesForBundles/HeroMerchant.prefab"`, instantiated, then `HeroMerchant.Instance.Init()` later (GameManager.cs:2342-2347, 2434). Test path uses `Resources.LoadAsync<GameObject>("HeroMerchant")` (GameManager.cs:1073-1079).

Key fields (all instance):
| field | type | line |
|---|---|---|
| `heroMerchantInventory` | `HeroMerchantInventory` public [HideInInspector] | 28 |
| `heroMerchantStats` | `HeroMerchantStats` public | 31 |
| `familiars` | `List<FamiliarBase>` public | 33 |
| `heroMerchantController` | `HeroMerchantController` public | 35 |
| `depthOrderer` | `SpriteDepthOrderRender` public | 37 |
| `currentMode` | `HeroMerchantMode { DungeonMode, ShopMode }` | 21-25, 39 |
| `canUseEmblem/canUseMirror/canUsePendant` | bool | 49-53 |
| `_currentDungeonLevel` | private int | 59 |
| `_lastDungeonCulture` | private `ItemMaster.Culture` | 65 |
| `_lastObjectToDealDamage` | private string | 67 |
| `isInCombat` | private bool (prop `IsInCombat`) | 69, 111-147 |
| `spriteRendererWillShieldLoss`, `spriteRendererWillShieldLossWave` | SpriteRenderer (shield-break FX) | 77-79 |
| `_flying`, `_isGodModeEnabled` | private bool | 87, 57 |

Events/delegates:
- `public static event Action<ItemMaster.Culture,int> OnFloorChange;` (239)
- `public static event Action OnDie;` (241) raised in `OnWillDeathAnimationEnded` (702-705)
- `public static event Action<bool> OnCombatStanceChange;` (243)
- `public static event Action<float, Enemy> OnDealedDamage;` (245) (damage the player received, raised in DealDamageToPlayer 1091-1094)
- `public event Action EnteredInCombat; ExitInCombat;` (247-249)
- `HeroMerchantStats.OnBecameInvincible` static `Action<bool>` (HeroMerchantStats.cs:33)
- `HeroMerchantController.OnSpecialHoleFall` static, `OnChangedWeapon` instance (HeroMerchantController.cs:411-413)

Important methods:
- `public void DealDamageToPlayer(float attackStrength, string damageSource, DamageType type = DamageType.Normal, Enemy enemySource = null)` (1014-1103). Town => 0 damage; returns if dead; damage = `CalcHitDamage(str, defense)` = `str - RoundToInt(str*def/100)` (1141-1145); amulet `IModifyEnemyDamage` add-ons; shield stat consumes one hit; absorption first; then `currentHealth.Value -= dmg`, `SetInvencible(1f)`, `StartPlayingInvencibilityEffect(1f,0.2f)`, `SetIntangibleMode()`, camera rumble, red flash. `DamageType.ItemDrain` drops 3 random bag items (1099-1102).
- `public void addGold(int amount)` (958-970); `GetCurrentGold()` (972).
- `AddSlimeCoins(int)`, `DepleteSlimeCoins()` (982-1001).
- `UseMerchantPendant()` (717-736), `UseMerchantEmblem()` (745-758), `UseMerchantMirror(ItemStack)` (760-766).
- `ChangeToShopMode()` / `ChangeToDungeonMode()` (768-808): animator triggers `ChangeToShopMode`/`ChangeToDungeonMode`, bool `IsShopMode`, shows/hides armour, spawns familiars.
- `LoadDungeonScene(culture)`, `LoadNextLevelDungeonScene(culture)` (583-633), `LoadTownScene()` (640-645).
- `OnWillDeathAnimationEnded()` (691-715) (animation event, no code callers).
- `SetGodMode()/SetFlying()` (1163-1183); `IsMerchantRolling()` (1198-1205).
- Debug keys in `Update()` only when `GameManager.IsDebugEnabled` (491-552).

### Child structure (as code reveals)
From `HeroMerchantController.Init()` (HeroMerchantController.cs:902-921):
- `transform.Find("HeroMerchantArt")` -> `heroMerchantArt` (body SpriteRenderer is `heroMerchantArt.GetComponentInChildren<SpriteRenderer>()`, used at 2523, 3090, 3140, 3983; Mirror.cs:18; HealingArea.cs:276).
- `transform.Find("Weapon")` -> `weapon` GameObject.
- `transform.Find("Head")`, `"Body"`, `"Boots"` -> `helmetArmor`, `chestArmor`, `bootsArmor`.
- `transform.Find("AbilityGhostEffect")` -> `GhostEffect abilityTrail` (roll after-images, GhostEffect.cs:39-53 copies `renderTarget.sprite`).
- Root components: `Animator` (characterAnimator, 905), `Rigidbody2D` (919), `BoxCollider2D` (920), `SMAnimatorHandler` (1030).
- Inventory parents (serialized on HeroMerchantInventory): `bagInventoryParent, headParent, bodyParent, bootsParent, weaponParent, abilityParent, potionParent, amuletParent` (HeroMerchantInventory.cs:27-41). Equipped ItemStack GameObjects are reparented under these (HeroMerchantInventory.cs:574, 1275-1291). So the **weapon art lives inside the ItemStack** under the hero.
- Equipment art: `Equipment.InitializeAnimators()` instantiates prefab `equipmentMaster.inGameArtPrefabName` (from `ItemDatabase.GetPrefab` or `PrefabRegister`) under the ItemStack's child `"Logic"` (Equipment.cs:65-108). Optional extra piece `inGameExtraPieceArtPrefabName` (colour-swapped via material `_ColorOrigin1..3/_ColorDesired1..3`, Equipment.cs:122-130).
- Weapon sprites: `currentEquippedWeapon.transform.GetChild(1).GetChild(0).GetChild(0/1).GetComponentInChildren<SpriteRenderer>()` = main / secondary weapon sprite (HealingArea.cs:327-337).
- Also: `MerchantHeroMerchantTrail`, `foresHeroMerchantTrail`, `abilityEffect` ParticleSystems (HeroMerchantController.cs:276-284), `trickEnabledAnimator` (87), `bubble` Animator (49), `left/right/front/backActionArea` (193-199), `potionGameObjectEffect` (85).

### Tags / layers
- Hero root tag `"Player"` (inferred from all enemy checks e.g. EnemyBehaviour.cs:357, PickOnWalk.cs:44, HealingArea.cs:405, TallGrass.cs:31). 157 occurrences of `"Player"` across ~120 files.
- Weapon hit colliders use tags `ShortSword, Shield, Spear, Bow, Gloves, BigSword` (PickOnWalk.cs:44, GasAreaEffect.cs:142). Enemy projectiles test `"Shield"` for blocks.
- Layers set at runtime: `"Default"` (3192, 3225), `"IntangibleHero"` (3209; roll/i-frames; IgnoreLayerCollision with `"SunkenObstacle"` 3208), `"Flying"` (3197, god mode), `"EnemyProjectile"` (3237). Holes: layer `"SunkenObstacle"`, pickups layer `"Item"` (1027-1028).
- Enemy tags: `HardEnemy`, `SoftEnemy`, `EnemyDamageReceiver` (Weapon.cs:284, 327).

### Stats (HeroMerchantStats.cs)
All are `Stat` (Stat.cs:6) instances, which wrap **CodeStage `ObscuredFloat`** `_value/_minValue/_maxValue` and `ObscuredBool variableStat` (Stat.cs:10-16). Fields: `gold, slimeCoins, fullHealth, currentHealth, speed, intelligence, strength, defense, absorption, shield` (HeroMerchantStats.cs:9-27); `public ObscuredBool isDead;` (29).
- Max health is `currentHealth.maxValue` (equipment adds to it, EquipmentStats.cs:98-99). `fullHealth` not used for that.
- `Stat.Value` setter clamps and fires `OnStateUpdated(float)`; if `variableStat == false` the write is ignored (this IS invulnerability) (Stat.cs:18-62). Events: `OnStateUpdated`, `OnMaxValueStateUpdated`, `OnMinValueStateUpdated` (Stat.cs:100-104).
- `SetInvencible(float time=0)` sets `currentHealth.variableStat=false`, DOTween delayed `SetVencible` (HeroMerchantStats.cs:81-97).
- `HealthValueChanged` -> `Die()` when <= kMinHealth (122-128). `Die()` (138-154) stops movement, armour `Death` trigger, `PlayDeathAnimation()`, `SetInvencible()`.
- `Init()` loads `gold.Value = currentGameSlot.willGold` (49).
- Obscured types need `(float)` casts (`implicit` operators); writing via `Stat.Value = x` is fine from Harmony code.

---

## 2. HeroMerchantController

`public class HeroMerchantController : MonoBehaviour` (HeroMerchantController.cs:10). No explicit enum state machine: state = **Animator state tags on layer 0** + bool flags.

Enums: `Direction { Left, Right, Back, Front }` (12-18; Back=up, Front=down), `EightDirection` (20-30).

Key fields: `inputDevice` (`HeroMerchantActions`, 32, from `InputActionsManager.Instance.actions` 926 - global input, a puppet must not read it), `characterRigidbody` (38 private), `characterCollider` (41), `heroMerchantArt` (43), `isTrapped` (47), `currentPlayerMoveSpeed` (55), `move` (57), `_canMove` (75), `speedFactor` (95), `heroDirection` (99), `_comboCounter` / `maxComboCounter=3` (104-106), `lastComboHitDamageMultiplier = 2f` (109), `inputDirection` (113), `weaponAnimator` (120 public), `_bareHandsWeapon` (122), `_currentEquippedWeapon` (126), `_currentEquipedAbility` (128), `characterAnimator` (133), armour animators `helmetArmorAnimator`, `chestArmorAnimator`, `bootsArmorAnimator` + `...ExtraAnimator` and `equipedHelmetArmor/ChestArmor/BootsArmor` (`Equipment`) (157-191), `_activatedTrigger` (201), `isFalling` (243 private), `isIntangible`, `isHolesIntangible` (271-273), `isInGrass` (290), `trickWeaponActivated` (89), `isMissed` (225).

State tags checked (layer 0): `Idle, Walk, IdleShop, WalkShop, WalkGrass, Ability` (=roll), `SwordAndShieldMainAttack, SwordAndShieldSecondaryAttack, BigSwordMainAttack, BigSwordSecondaryAttack, SpearMainAttack, SpearSecondaryAttack, GlovesMainAttack, GlovesSecondaryAttack, BowMainAttack, BowSecondaryAttack, TrickAbility` (1316, 1347, 3720-3817).
State names registered on `SMAnimatorHandler` (via `SMStateAdvisor` StateMachineBehaviour, SMStateAdvisor.cs:3-24, `SMAnimatorHandler.RegisterOnEnter/Exit` SMAnimatorHandler.cs:45-61) in `Start()` (1030-1228): `{ShortSword|BigSword|Spear}Main{First|Second|Third}{Down|Right|Left|Up}`, `GlovesMain{First|Second|Third}Punch{Dir}`, `BowAttackAnimation{Dir}` -> `MainAttackUseStart/End`; `Idle{Dir}`, `Walk{Dir}`; `BlockPunch{Dir}`, `StopBlocking{Dir}`; `PrepareBigSwordSpin, BigSwordSpinPrepared, BigSwordSpinAttackDown, EndBigSwordSpin`; `PrepareCharge{Dir}, ChargeReady{Dir}, ChargingCycle{Dir}`, `SpearFinishSecondaryAttack{Dir}`; `GlovesSecondaryAttack{Charge|ChargeReady|Punch|End}{Dir}`; `BowSecondaryAttack{Charge|ChargeReady|Shoot|End}{Dir}`; `Ability{Dir}`; `MissAttack{Dir}`; `EnableTrickAbility, DisableTrickAbility`; `UsePendant`. Also `BlockingUp/Down/Left/Right`, `Blocking{Dir}Move`, (`BlockPunchkDown` typo) (2146-2161).
`SMAnimatorHandler.OnStateEnter/OnStateExit` are `event Action<string>` (SMAnimatorHandler.cs:19-21) - a good hook for sending discrete animation events.

### Movement
- `Update()` (1232-1646): computes `move = ClampMagnitude((velX,velY),0.75) * currentPlayerMoveSpeed * speedFactor * Time.deltaTime` and **`transform.Translate(move, Space.World)`** (1304-1309). Not Rigidbody velocity. Rigidbody2D is used only for impulse knockback `AddForce(Vector2) -> characterRigidbody.AddForce(force, ForceMode2D.Impulse)` (841-844) and `TeleportTo` resets.
- `velX/velY` set from `inputDevice.LeftStick` in `UpdateMoveAnimations()` (2195-2434), which also does BoxCast wall/hole/door collision (`collisionDetectionLayers`), corner sliding, hole push timer -> `PlayFallAnimation()`, door push -> `DungeonDoor.OnPlayerEnter(gameObject)` (2387-2395), and sets bool `Walk` + direction.
- `SetDirection(Direction)` sets bools `UpDirection/DownDirection/LeftDirection/RightDirection` on body + armour (2663-2724). Input bools `UpInput/DownInput/LeftInput/RightInput` set every frame (1260-1303).
- `PlayerMoveSpeedUpdated` sets float `PlayerTotalMoveSpeed` (415-421).
- `public void TeleportTo(Vector3 newPosition)` (868-877): zero rb velocity, Sleep/WakeUp, zero vel, set `transform.position`. `TeleportToKeepingMomentum` (879-883). `MoveAutomaticallyToPoint(Vector2,float,bool,bool)` DOTween (2626-2656).
- Gates: `canMove` prop (297-315; in ShopMode forces `_canAttack=false`), `DisableMoveInput()/EnableMoveInput()` (423-440), `AllowMovement/ForbidMovement/StopMovement` (566-585).

### Rolling (Ability)
- LeftTrigger -> `UseAbility()` (1441-1448, 1663-1704): trigger `Ability`, weapon `ResetWeapon`, `currentEquipedAbility.SendUseAbility()`.
- Every ability ItemStack gets `Dash` (Ability.cs:36). `Dash.UseAbility()` adds SpeedEffect x1.5, sets vel by 8-dir input (Dash.cs:22-89); `Dash.AbilityUseStart()` -> `SetIntangibleMode()` (Dash.cs:91-116) = layer `IntangibleHero` (HeroMerchantController.cs:3205-3215). Note roll i-frames are via layer, not `variableStat`.
- `AbilityUseStart()/AbilityUseEnd()` on controller (3250-3266) are animation events -> `Ability.SendAbilityUseStart/End` (`isInUse`, events `Ability.OnAbilityUsedStarted/Ended` static, Ability.cs:22-26, 82-108). `AbilityUseStarted/Ended` (1969-2036) on state enter/exit: animator speed 1.25, then `SetDefaultMode()`.
- Ability cooldown via `Invoke("MakeAbilityAvailable", coolDownTime)` (Ability.cs:58-70).

### Attacks
- Main (Action1, 1339-1376): `SetTrigger("<Type>MainAttack")` for `ShortSword/BigSword/Gloves/Spear/Bow` (no weapon -> `GlovesMainAttack`), gated by normalizedTime windows (constants `*AttackToAttackNormalizedTimeWindow`, 0.55; spear 0.65).
- On state enter `MainAttackUseStart()` (1727-1786): lunge `Move*WithAttackIfInputDirection(moveTimeWhileAttacking=0.35)` for non-bow, `currentEquippedWeapon.UseMainAttack()`, weapon animator trigger `MainAttackUp/Down/Left/Right`, `weaponAnimator.SetInteger("ComboCounter", comboCounter)` then `IncreaseComboCounter()` and body `ComboCounter`. Combo 1..3; third hit damage x2 (Weapon.cs:316-323).
- `MainAttackUseEnd()` (1788-1816) resets triggers.
- Secondary (Action3 held, 1377-1440): `currentEquippedWeapon.UseSecondaryAttack()` and bool `<Type>SecondaryAttack` = pressed (or confused). Weapon trigger `mainAttackToSecondaryAttack` in window.
  - ShortSword: shield block (`BlockPunch*`/`Blocking*`). `CheckIfPlayerIsShieldProtected(GameObject enemy)` (2133-2141) = within 60 deg of block facing; used by enemies before damaging (EnemyBehaviour.cs:357). Shield hits counted in `ShortSword.AddShieldHit`, break -> `ApplyMissedAttack` + push back (ShortSword.cs:58-80).
  - BigSword/Spear/Gloves/Bow: charge. `SecondaryAttackUseStart` (1818-1872) sets weapon bools `SecondaryAttack{Up|Down|Left|Right}`; `SecondaryAttackChargeReady` (1874-1913) sets `secondaryAttackReady` (prop writes weapon bool `SecondaryAttackReady`, 361-375; also lowercase `secondaryAttackReady` if present), fires `Weapon.OnSecondaryAttackFullyCharged`; `SecondaryAttackChargeRelease` (1915-1929) makes hero invulnerable+intangible (non-bow) until `OnChargingCycleExit` (1939-1943); `SecondaryAttackUseEnd` (1945-1967).
- Trick weapons (DLC): Action3 -> triggers `TrickAbility` on body+weapon, bool `IsTrickAbilityEnabled` (1382-1395); `TrickAbilityStart/End` (2038-2059) toggles effect via `UseSecondaryAttack`.
- Bow: animation events `StoreWillDirectionAndPositionToShootBow`, `CreateAndShootMainAttackArrow`, `CreateAndShootSecondaryAttackArrow`, `ShootBow` (976-1022); knockback `BowKnockback{Dir}` (783-833). These have **no code callers** -> called from animation clip events (inferred).
- Other animation-event methods (no code callers): `MoveRightABit` etc. (731-781), `PlaySecondaryAttackReadyEffect` (1931), `ApplyRollBack` (2099), `ApplyShieldSpeedFactor` (540), `StartVibrationFrom*`, `PlayShieldShown`, `PlayStepSound`, `PlaySwimmingSound` (3346), `HeroMerchant.OnWillDeathAnimationEnded`. A puppet Animator running the same controller **will fire these events** on the puppet's own components -> they must exist (or be stubbed) on the puppet and must not act on the real hero (most use `HeroMerchant.Instance`!).
- Weapon set toggle LB/RB -> `heroMerchantInventory.ToggleWeaponSet()` (1520-1560).
- Miss: `ApplyMissedAttack()` trigger `MissHit` (2080-2090).

### Falling / holes
- `CheckForHoles()` every 0.2 s: 4 OverlapBox on `SunkenObstacle` (2451-2473) -> `PlayFallAnimation(Vector3 point=default)` (2499-2624): triggers `BeginFall`, sets `fallMaterial`, DOTween fade/scale/rotate of `heroMerchantArt`, then `EndFall`, teleports to `lastHolesFreePosition` (or room/last door), damage `max(1, maxHP*willFallDamage)` via `DealDamageToPlayer(n,"Fall")`. Special rooms raise `OnSpecialHoleFall`.

### Swimming / pools / grass
- No swim state in code. Healing pool = `HealingArea` trigger (tag Player) heals over time via DOVirtual chain and calls `DestroyEffects()` (cures status) (HealingArea.cs:359-418). Swim visuals presumably via animator (sound event `PlaySwimmingSound`, inferred).
- `HealingArea` reflection copies sprites from hero body/armour/weapon renderers each frame (HealingArea.cs:132-240) - proof that sprite-copy mirroring works locally.
- Tall grass: `TallGrass` calls `OnEnterGrass/OnExitGrass` (TallGrass.cs:29-68; HeroMerchantController.cs:3962-4012): triggers `ChangeToGrassMode`, bool `IsInGrass`, body sorting layer `"UpperDefault"`.

### Being hit
- `OnHealthChanged` -> trigger `Damaged` on decrease (2970-2979).
- `PlayDamagedEffect(Color)` (2992-3037) shader flash (`_FlashAmount`, `_Color` material props on body+armour, 3088-3165); called from `WillHealthBar.PlayAnimation` (WillHealthBar.cs:139-142).
- I-frames: `SetInvencible(1f)` + `StartPlayingInvencibilityEffect(1f,0.2f)` blinks by toggling `heroMerchantArt.SetActive` every `willInvincibleBlinkDuration` (3053-3183) + `SetIntangibleMode()`.
- Knockback: enemies call `HeroMerchant.Instance.gameObject.GetComponent<HeroMerchantController>().AddForce(...)` (EnemyBehaviour.cs:372-389).

### Death
`HeroMerchantStats.Die()` -> `PlayDeathAnimation()` (3435-3449): `GameManager.Instance.PauseGame()` (Time.timeScale=0, GameManager.cs:2592), trigger `Death`. Animation event -> `HeroMerchant.OnWillDeathAnimationEnded` (see 9).

### Animator parameter list (body = root Animator; armour animators mirror the same names via `SetArmorAnimator*`, 3451-3617)
Triggers: `ShortSwordMainAttack, BigSwordMainAttack, SpearMainAttack, GlovesMainAttack, BowMainAttack, Ability, TrickAbility, UsePendant, MissHit, Damaged, Death, BeginFall, EndFall, ChangeToShopMode, ChangeToDungeonMode, ChangeToGrassMode`.
Bools: `Walk, UpDirection, DownDirection, LeftDirection, RightDirection, UpInput, DownInput, LeftInput, RightInput, IsShopMode, IsInGrass, IsTrickAbilityEnabled, ShortSwordSecondaryAttack, BigSwordSecondaryAttack, SpearSecondaryAttack, GlovesSecondaryAttack, BowSecondaryAttack`.
Int: `ComboCounter`. Float: `PlayerTotalMoveSpeed`.
Weapon animator (`Equipment.Animator` of the weapon ItemStack art): triggers `MainAttackUp/Down/Left/Right, ResetWeapon, ResetWeaponAnimationToDefault, TrickAbility, mainAttackToSecondaryAttack`; bools `SecondaryAttackUp/Down/Left/Right, SecondaryAttackReady, secondaryAttackReady, Walk, IsTrickAbilityEnabled`; int `ComboCounter`.
Armour animators sync on show via `Equipment.CopyAnimatorParameters(from)` = `CrossFade(from.GetCurrentAnimatorStateInfo(0).fullPathHash, 0, 0, normalizedTime)` + copy all params (Equipment.cs:407-428) - the game itself syncs animators by state hash + normalizedTime.
Animator speeds are changed by hit-stop: `GameManager.SetGameSpeedForTime` sets body/armour/weapon `speed` (GameManager.cs:2651-2663).

---

## 3. Minimal replicated state (recommendation, partly inferred)

Per tick (15-20 Hz):
- `transform.position` (x,y), `heroDirection` (byte), Walk bool, `currentPlayerMoveSpeed*speedFactor` (or interpolate).
- Body layer-0 `fullPathHash` + `normalizedTime` (+ `characterAnimator.speed`). Drive puppet with `Animator.Play(hash, 0, t)` / `CrossFade` exactly like `Equipment.CopyAnimatorParameters` (Equipment.cs:409). Also send the bools (direction/Walk/IsShopMode/IsInGrass) so transitions look right between snapshots.
- Weapon animator state hash + normalizedTime; armour animators can be slaved from body hash (they share state names, Equipment.cs:409 uses body hash on armour animator).
- Visibility flags: `heroMerchantArt.activeSelf` (blink/hidden), armour shown (`_showArmor`, HeroMerchantInventory.cs:49), falling (scale/rotation of heroMerchantArt), sortingLayer (grass), flash amount/colour.
- Equipment identity on change only: item `name` + `plusLevel` + `enchantmentLevel`/`enchantmentType`/`enchantmentEffectName` for Head/Body/Boots/current weapon (+ trick on/off).

Sprite-name approach: workable but second best. Animated sprites live in bundles; `ItemDatabase.LoadFromBundle<Sprite>(name)` only searches the items bundle + DLC (ItemDatabase.cs:290-315), not the hero animation sprites. Receiving side would need its own name->Sprite map built from the local hero/equipment renderers or `Resources.FindObjectsOfTypeAll<Sprite>()` (inferred). Armour/weapon sprites only exist once their prefab is instantiated. Simpler: build the puppet with real equipment art (create ItemStacks with `ItemStack.Create(ItemDatabase.GetItemByName(name, plus))` and call `Equipment.InitializeAnimators()`) and drive Animators by hash.
Renderers that matter: body (`heroMerchantArt` child SpriteRenderer), each `Equipment.equipmentRenderer` + `equipmentExtraRenderer` (Equipment.cs:23,35), weapon main/secondary renderers (HealingArea.cs:327-337), shield-loss FX renderers (HeroMerchant.cs:77-79), `abilityTrail` GhostEffect, trails/particles. Body material uses props `_FlashAmount`, `_Color`; extra pieces use colour-swap props.

---

## 4. Weapons

Components added at `ItemStack.Init` (ItemStack.cs:253-289): `Weapon` (or `Moonlighter.Weapons.TrickWeapon : Weapon`), plus `Bow` for `BowWeaponMaster`, `SecondaryAttackWeapon` for Gloves/BigSword/Spear, `ShortSword` for ShortSword. `Equipment` + `EquipmentStats` for all equipment.

Master data: `WeaponEquipmentMaster : EquipmentItemMaster` (WeaponEquipmentMaster.cs:5-43): `weaponType { None, ShortSword, BigSword, Spear, Gloves, Bow }`, push forces, `secondaryAttackDamageMultiplier = 2f`, effect prefab names, `combatEffect`. `BowWeaponMaster.arrowPrefabName` (BowWeaponMaster.cs:8), `ShortSwordWeaponMaster.shieldHits`, `TrickWeaponMaster.trickEffect/arrowPrefabName/...`.

Hitbox: colliders in the weapon art prefab with `WeaponCollisionDetection` (`weaponAttackType MAINATTACK|SECONDARYATTACK`), enabled/disabled by weapon animation clips (inferred; no code enables them). `OnTriggerEnter2D/OnCollisionEnter2D` -> `_weapon.SendMainAttackHit(go)` / `SendSecondaryAttackHit(go)` (SendMessage `OnMainAttackHit`/`OnSecondaryAttackHit` to all components on the ItemStack) and re-hits every `attackTickTime` while staying (WeaponCollisionDetection.cs:42-95).

Damage formula:
- Main hit: `Weapon.OnMainAttackHit(GameObject other)` (Weapon.cs:278-355): tag HardEnemy/SoftEnemy -> push, hit FX, `combatEffect` chance `Constants.GetFloat(effectName)` -> `MoonlighterEffect.Create(_combatEffect, enemy, HeroMerchant.Instance.heroMerchantStats)`; damage `enemy.DealDamageToEnemy(strength * (comboCounter==3 && !bow ? 2 : 1))`. EnemyDamageReceiver tag -> `component2.DealDamageToEnemy(...)`. Also sets global hit-stop timeScale 0 for 0.05 s (325).
- Secondary: `SecondaryAttackWeapon.DealSecondaryAttackDamage` = `strength * secondaryAttackDamageMultiplier` (1 if EnergyFlux amulet) (SecondaryAttackWeapon.cs:28-40). Bow same (Bow.cs:190-198).
- `strength` = `HeroMerchant.Instance.heroMerchantStats.strength.Value` = base + sum of equipment `StatsModificator` (+enchant stats via `EquipmentStats.AddModificator`, EquipmentStats.cs:52-113; Equipment.EnchantWith Equipment.cs:353-405). **Enchant level is not a separate multiplier**; it's already in strength. Elemental enchant swaps `combatEffect`.
- Enemy side: `public void DealDamageToEnemy(float attackStrength, EnemyDamageReceiver damageReceiver = null, EnemyDamageReceivedType enemyDamageReceivedType = Normal)` (Enemy.cs:328-402): `total = attack - RoundToInt(attack*enemyDefense/100)`, plus `IModifyPlayerDamage` amulets of the LOCAL registry (345-353), raises `Enemy.OnEnemyRecivesDamage` (static, 112). Floating number uses `HeroMerchant.Instance...comboCounter` (393).
- Remote-hit application (recommended): remote sends `{enemyId, rawStrength (already multiplied by combo/secondary), attackType, combatEffectName/proc}`; owner calls `enemy.DealDamageToEnemy(raw)`; push via `Weapon.PushEnemyBackwards` logic (Weapon.cs:416-425, uses `HeroMerchant.Instance.transform` -> needs puppet position instead). Owner-local amulet modifiers would wrongly apply -> compute modifiers on the attacker side and pass `EnemyDamageReceivedType` to skip (inferred).

Projectiles: `Bow.ShootMainAttackArrow()` -> `CreateAndLaunchProjectile(GameObject prefab, float, bool)` instantiates arrow at `originalHeroPosition + offset` (Bow.cs:46-160), `BowArrow.InitializeProjectile(HeroMerchant.Instance, dir, ajust)`, `HeroMerchantProjectile.StartMove` (DOTween-driven rb velocity 500, homing for secondary; HeroMerchantProjectile.cs:55-81). `BowArrow.DealDamage` uses `projectileOwner.heroMerchantStats.strength` (BowArrow.cs:48-66) - projectileOwner is a HeroMerchant, so a puppet needs a HeroMerchant-like stats object or patch. Trick weapons: `TrickWeapon.CreateAndLaunchProjectile` (TrickWeapon.cs:187-220), `TrickWeaponProjectile` damage `strength*damageModifier/100` type Trick (TrickWeaponProjectile.cs:131-135); trick effects (`Moonlighter.Weapons.*WeaponEffect : TrickWeaponEffect`) subscribe static `Weapon.OnMainAttackUsed`/`OnMainAttackHitEvent` (e.g. MechanicBigSwordWeaponEffect.cs:53-54).
Static weapon events (Weapon.cs:74-90): `OnWeaponUsed, OnMainAttackUsed, OnMainAttackEnded, OnSecondaryAttackUsed, OnSecondaryAttackEnded, OnMainAttackHitEvent(Weapon,GameObject), OnSecondaryAttackHitEvent, OnSecondaryAttackFullyCharged, OnSecondaryAttackReleased` - good send hooks.

Replay on puppet: set puppet body trigger/state + weapon trigger `MainAttack{Dir}` / `ComboCounter`; to avoid double damage, disable `WeaponCollisionDetection` on puppet weapon (or make it report to network only on owner side). For arrows, spawn the arrow prefab locally with colliders disabled, or let owner simulate.

---

## 5. HeroMerchantInventory

`public class HeroMerchantInventory : MonoBehaviour` on the hero (HeroMerchantInventory.cs:5).
- `enum EquipmentSlot { Head, Body, Boots, Weapon1, Weapon2, Ability1, Ability2, Potion, Amulet1, Amulet2, Count }` (7-20). Weapon set 0 = Weapon1+Ability1, set 1 = Weapon2+Ability2 (`_currentWeaponSet` 43, `ToggleWeaponSet/EquipWeaponSet` 1044-1102, event `OnEquippedWeaponSetChange` 79). "Ability" = roll item. Amulet1/2 = rings (DLC).
- `public List<ItemStack> equippedItems;` 10 entries (23, 95-98). `public InventorySlot[] slots;` size `bagSize`=20 (25, 88-94); grid `width=5, height=4` (45-47). Pockets = first `pocketSize`=5 slots are `isSafe` (SetDefaultSafeCondition 1402-1410).
- `InventorySlot` (InventorySlot.cs): `ItemStack item; bool isSafe; isBlocked/isAffected` counters (7-39).
- Static events: `OnItemAddedToInventory(ItemStack,int)`, `OnItemChanged(int)`, `OnItemRemovedFromInventory(ItemStack,int)`, `OnItemEquipped(ItemStack,EquipmentSlot)`, `OnItemUnequipped`, `OnEquippedWeaponSetChange(int)` (69-79). Static => a second inventory instance would fire the same events consumed by UI/amulets.
- API: `bool SetItem(ItemStack stack, int index)` (252-292; note bounds check bug `index < 0 && index >= len`), `ItemStack GetItem(int)` (303), `bool TryAddItem(ItemStack item, int start = 0)` (708-747; AutoConsumable consumed instantly), `bool CanPlaceItem(ItemStack)` (679), `CanPlaceItemAt(ItemStack,int,bool)` (634-677), `int RemoveItem(ItemMaster master, int quantity = 1, int start = 0)` (749-792), `bool TryRemoveItem(ItemMaster master, int quantity = 1, int start = 0)` (808-855), `ItemStack GetEquippedItemByType(EquipmentSlot)` (411), `bool SetEquippedItemByType(ItemStack item, EquipmentSlot type)` (509-579), `bool Equip(int itemIndex)` (581), `Unequip(EquipmentSlot)` (862), `GetBagItemCount(...)` (871-896), `DropRandomItems(int,bool)` (1293), `DropAllNotSafeItems()` (1322), `RemoveAllNotSafeItems()` (1363), `ShowArmor/HideArmor` (986-1042), `RecalculateAffectedStacks()` (1436-1458).
- `Equipment.OnEquipped` writes itself into `HeroMerchant.Instance.heroMerchantController` and adds stats to `HeroMerchant.Instance.heroMerchantStats` (Equipment.cs:132-224, 343-351); `Weapon.OnEquipped` sets controller weapon (Weapon.cs:131-139). **Equipping on a puppet modifies the real hero** - puppets must not run Equip.

Items:
- `ItemMaster` [Serializable] (ItemMaster.cs:6-91): `name` (identity key), `nameKey, worldSpriteName, goldValue, tier, culture, maxStack, isDestroyedOnRunEnded, plusLevel`. Subclasses: `EquipmentItemMaster` (`equipmentSlot`, `stats`, art prefab names; EquipmentItemMaster.cs:5-38), `WeaponEquipmentMaster`, `BowWeaponMaster`, `ShortSwordWeaponMaster`, `BigSwordWeaponMaster`, `SpearWeaponMaster`, `GlovesWeaponMaster`, `TrickWeaponMaster`, `AbilityItemMaster`, `ConsumableItemMaster` (`consumableEffectName`), `AutoConsumableItemMaster`, `AmuletEquipmentMaster` (`amuletEffectName`), `DecorativeItemMaster`, `FamiliarItemMaster`.
- Database: `ItemDatabase : ScriptableObject` (ItemDatabase.cs:8), JSON `Resources/items` + `ngpitems` (62-67), 3 collections by plusLevel (27). Lookup: `public static ItemMaster GetItemByName(string name, int plusLevel)` (173-183), `GetItemByName(string)` = plus 0 (168), `GetItems(Predicate, plusLevel)`, `HasItem`. Prefab/sprite: `GetPrefab(string)`, `GetSprite(ItemMaster)`, `LoadFromBundle<T>` (271-315). Current plus level: `GameManager.Instance.GetCurrentGamePlusLevel()` (GameManager.cs:2823-2834). ItemMaster instances are shared references; compare by reference (`stack.master == master`).
- `ItemStack : MonoBehaviour` (ItemStack.cs:7): `master`, `Quantity` (75-86), `effect` (curse, `Effect`) (33), `pickOnWalk` (30), `MaxStack` (fixedStack of curse or `master.maxStack`, 127-141), components getters (88-106). `public static ItemStack Create(ItemMaster master, int quantity = 1)` instantiates `PrefabRegister.Instance.GetPrefabByName("ItemStack")` (171-184). `CreateFrom(stack, qty)`, `Clone(...)`, `GenerateEffect(GameObject effectPrefab)` (197-210), `Drop(...)` (463-547), `Hide/Show`.
- Curses: `Effect` + `InventorySlotAction : EffectAction` (Effect.cs; InventorySlotAction.cs) with `Direction` mask, `evil`, `CanAffectSlotFrom(index,dir)` on 5-wide grid (InventorySlotAction.cs:378-399), `GetIndexByDirection` (413-427). Curse prefabs by name: `ItemEffectManager.Instance.GetEffectByName(string)` (ItemEffectManager.cs:72-80). Many subclasses (BreakItemAction, BlockSlotsAction, TransmuteSlotAction, DamageAction, ExplodeInventoryAction, SendItemToShopAction, UknownAction, ...). `InventorySlotAction.OnRelease` calls `HeroMerchant.Instance.heroMerchantInventory.RefreshSlotAt` (InventorySlotAction.cs:295-300).
- Enchantment: `Equipment.enchantmentLevel` + `EnchantWith(EnchantmentRecipe)`; recipe via `RecipeManager.Instance.GetEnchantment(name, 0, plus)` or `GetEnchantmentRecipeToItemAndEffect(name, effectName, plus)` (HeroMerchantInventory.cs:1190-1197).

Serialisation:
- `ItemSaveInfo` [Serializable] (ItemSaveInfo.cs:5-87): `prefabName, plusLevel, name, quantity, sellingPrize, curseName, enchantmentLevel, enchantmentType (0 none,1 standard,2 elemental), enchantmentEffectName`; ctor `ItemSaveInfo(ItemStack)` (42-73). Good wire format for one item.
- Save: `GameSlot.willInventory : List<ItemSaveInfo>` (20 entries), `willEquippedItems` (10, indexed by EquipmentSlot), `equippedWeaponSet`, `willGold` (GameSlot.cs:40-48), written in `HeroMerchantInventory.GameManager_OnSave` on `OnSaveToDungeon/OnSaveFromDungeon` (HeroMerchantInventory.cs:191-207). Restore: `SetInitialItems`/`SetInitialEquippedItems` (1167-1237) - note these do NOT restore `curseName` (Chest.LoadFromSave does, Chest.cs:876-879).
- Whole save: `GameManager.GameSaveData_V2 { GameSlot[] gameSlots(5), ... }` serialized with FullSerializer `FsJSONManager.Serialize` + `fsJsonPrinter.CompressedJson`, UTF8, `Platform.CloudSave.WriteBytes` (GameManager.cs:33-41, 2569-2590). Use `FsJSONManager.Serialize<T>/Deserialize<T>` (FsJSONManager.cs:26-63) to send `List<ItemSaveInfo>`.

Home storage: `Chest : MonoBehaviour` (Chest.cs:9), `capacity` (51), `ChestContent` (100), `SetItem/TryAddItem/RemoveItem/CanPlaceItemAt` (430-598), save `ChestSaveInfo { List<ItemSaveInfo> items }` (ChestSaveInfo.cs), `SetChestFromSave` (974-1000) / `LoadFromSave` (857-896). Shop storage in `GameSlot.shopSaveInfo.shopChests`, `bedChest`, `salesBox`, `helperChest` (ShopSaveInfo.cs:7-19); loaded in ShopManager (ShopManager.cs:183-240).

---

## 6. Items on the floor

- A dropped item is just an `ItemStack` GameObject shown in the world, parented to the current `DungeonRoom` transform or the `"ItemCanvas"`-tagged object (ItemStack.cs:463-477). Rooms track them: `DungeonRoom.AddDropItems(List<ItemStack>)` (DungeonRoom.cs:1613-1619), `OnDropListChanged`.
- Spawn: `ItemStack.Create(ItemDatabase.GetItemByName(name, plus), qty).Drop(Vector2 position, float radius = 30f, Transform parent = null)` (ItemStack.cs:171, 463) - pattern used by `ItemDrop.SpawnItem` (ItemDrop.cs:42-52) and `HeroMerchant.DropRandomItem` (HeroMerchant.cs:387-396). Drop uses `Random` direction and DOTween hop; `OnEndDrop` -> `CheckIfInSunkenObstacle` (fall) or `pickOnWalk.CanPick = true` (707-744).
- Magnet: hero `Update` OverlapCircle radius 30 on layer `Item`, moves items toward hero if `CanPlaceItem` (HeroMerchantController.cs:1246-1259).
- Pickup: `PickOnWalk.OnTriggerEnter2D` with tag Player or weapon tags -> `TryToAdd()` (PickOnWalk.cs:42-128): consumables go to Potion slot first; else `HeroMerchant.Instance.heroMerchantInventory.TryAddItem(_stack)`; full bag -> `CanPick=false` for 1 s and `HUDManager.Instance.ShowFullBagInfo()`. Static event `PickOnWalk.OnPickItem(GameObject)` (34). Always targets `HeroMerchant.Instance` - a puppet touching an item would give it to the local player (puppets must not be tag Player, or patch).
- Gold pickup: gold coins are AutoConsumable items with `Coin` component: `Consume()` -> `addGold(Quantity+1)` (Coin.cs:12-17); slime drops `SlimeDrop` -> `AddSlimeCoins` (SlimeDrop.cs:15-20). AutoConsumables are consumed inside `TryAddItem` (HeroMerchantInventory.cs:714-718).
- Leaving a dungeon: `GameManager.DeleteAllDroppedItems()` destroys children of ItemCanvas (GameManager.cs:2671-2684).

## 7. Gold

`HeroMerchantStats.gold : Stat` (ObscuredFloat) (HeroMerchantStats.cs:9, 37). Add/spend: `HeroMerchant.addGold(int amount)` (negative to spend, clamps 0) -> `GUIManager.Instance.HUDSPanel.UpdateGoldQuantity(amount, gold.Value, isInit:false)` (HeroMerchant.cs:958-970; HUDManager.cs:715). Read: `GetCurrentGold()` (972). Event: `gold.OnStateUpdated` (achievements, HeroMerchantStats.cs:51-75). Saved to `GameSlot.willGold` (HeroMerchantInventory.cs:206). Shop sales also go through addGold (inferred).

## 8. Potions / consumables

- Potion slot `EquipmentSlot.Potion`; RT -> `ConsumeHotkeyItem()` (HeroMerchantController.cs:1516-1519, 3276-3297) -> `Consumable.SendConsumeEvent()` = SendMessage `CanConsume(ConsumableChecker)` then `Consume` (Consumable.cs:20-47; Consumable decrements Quantity / destroys). The effect component is added by type name `Type.GetType(consumableEffectName)` (Consumable.cs:15).
- `HPPotionI..VIII.Consume()`: heal `Constants.GetFloat("HPPotionN")` x NG+ factor (`ngpPotionsHealFactor` / `ngpPostDLCPotionsHealFactor`), + MedicEmergency amulet bonus; first `HeroMerchant.Instance.DestroyEffects()` (cures all player MoonlighterEffects except amulet/trick, HeroMerchant.cs:680-689) (HPPotionI.cs:18-45). Only usable in dungeon and when not full HP (12-16). `HyperPotion` heals % of max (HyperPotion.cs:18-26). `SpeedPotion`, `StrengthPotion`, `InvulneravilityPotion` add `*PotionEffect` MonoBehaviours under the hero. Guidance/Reveal potions also call DestroyEffects.
- Status effects: `MoonlighterEffect` (MoonlighterEffect.cs) attached as child of target; registry `MoonlighterEffectRegistry` global static (MoonlighterEffectRegistry.cs). `DamageOverTimeEffect {Venom,Burn,Electrical}` (DamageOverTimeEffect.cs:6-11), `ConfusedEffect`, `SpeedEffect`. Cures: potions, healing pool, pendant (DestroyEffects). Anti-* amulets block types (DamageOverTimeEffect.cs:69-92). "Slime jelly" cure: no specific class found; slime grab is enemy behaviour (`NormalSlimeBehaviour`, releases on `OnDie`) (inferred).

## 9. Death and leaving

Death flow:
1. `currentHealth` hits 0 -> `HeroMerchantStats.Die()` (HeroMerchantStats.cs:138) -> `PlayDeathAnimation()` pauses game (Time.timeScale = 0) (HeroMerchantController.cs:3435-3449).
2. Animation event `HeroMerchant.OnWillDeathAnimationEnded()` (HeroMerchant.cs:691-715): if `AngelAmuletEffect` -> `UseAmuletEffect()` revive (AngelAmuletEffect.cs:83, RecoverAllHealth 178); else pixel effect, `DestroyEffects`, raise `HeroMerchant.OnDie`.
3. `DungeonManager.HeroMerchant_OnDie` -> `GUIManager.Instance.GoToResultsPanel(fromEmblem:false, overridePanel:true)` (DungeonManager.cs:204-213; GUIManager.cs:1032-1048).
4. Results panel class `DungeonResultsPanel` (`Enable(bool fromEmblem)` DungeonResultsPanel.cs:127-146; `_willDied = isDead`). `EndResult()` (728-802): if died, every non-safe (non-pocket) bag item that is equipment (and not run-only) is moved to `"RecuperableItems"` (later rival/recovery), then `RemoveAllNotSafeItems()` destroys the rest of bag rows 2-4; pockets (first 5) and equipped items are kept. Run-only items (`isDestroyedOnRunEnded`) destroyed everywhere. Then replay (`OnReplayTriggered`, new day half) or `GameManager.LoadTownSceneFromDungeon()`.
5. Town load: `LoadTownSceneFromDungeonCoroutine` (GameManager.cs:2062-2172) finds the culture's `WorldDungeonDoor`, teleports hero below its `pointToSpawnPlayer` (GameManager.cs:1654-1685), plays spit animation; `WorldDungeonDoor.OnFinishedSpitAnimation` -> `RecoverAllHealth()` + `ChangeToDungeonMode()` (WorldDungeonDoor.cs:153-163). **Game is saved**: `SaveSlotOnReturnFromDungeon(serialize:true)` (GameManager.cs:2169).
Pendant: `UseMerchantPendant()` costs `GetPendantCost()` gold (HeroMerchant.cs:267-300, 717-736), trigger `UsePendant`, pause; on state exit `OnUsedPendant` -> `GoToResultsPanel(false)` (742) -> results with `_willDied=false`: nothing lost. Emblem: opens portal (`GameManager.OpenPortalInDungeon`, GameManager.cs:2712), results via `EmblemPortal` (EmblemPortal.cs:181-205).
Statistics `HeroStatistics.Instance.totalWillDeaths` (HeroStatistics.cs:313).

## 10. Amulets / rings, familiars, NG+

- Rings: `AmuletEquipmentMaster` -> `Amulet` component (Amulet.cs:3-89); on equip `MoonlighterEffect.Create(amuletEffectGO.GetComponent<MoonlighterEffect>(), HeroMerchant.Instance.gameObject)` (80). Effects are `AmuletMoonlighterEffect` subclasses (AmuletMoonlighterEffect.cs): AddFamiliar, Angel (revive, IModifyEnemyDamage), AntiConfusion/Electric/Fire/Poison, Armor (fall dmg), AttackEmergency, BlackCat, CarnivorousMutae, ChargedAttack (IModifyPlayerDamage), DefenseEmergency, ElementalDamage, EnergyFlux, GolemKing, InstaKill, Luck, MedicEmergency, Naja, Puddle, SpeedEmergency, Vampire, Wanderer; DLC: EmergencyDamage, Familiar, HeroRing, LifeDrain, SpikedRoll (Moonlighter.DLC.WandererDungeon/). Hooks: queried via `MoonlighterEffectRegistry.GetEffect<T>()` throughout, or interfaces `IModifyPlayerDamage` (outgoing, Enemy.cs:345), `IModifyEnemyDamage` (incoming, HeroMerchant.cs:1031), `IModifyFamiliarDamage`. Registry is global, not per-player.
- Familiars: `FamiliarBase` (enum `Familiars` 15 values, FamiliarBase.cs:9-26); subclasses BabySlime, BladeTreeSprout, DancerPuppet, FlyingRepairGolem, GasSuit, GolemTurret, GraafGenerator, MotherGolem, Recharger, SpikeChain, WindTreeSprout, WoodMimic. `FamiliarsManager` static: `SpawnEquippedFamiliar()` (FamiliarsManager.cs:457-492; adds to `HeroMerchant.Instance.familiars`), `AddFamiliar`, `ActivateFamiliars`, `TeleportFamiliarsToWill` (590). Familiar damage = `max(1, hero strength * attackModifier)` (FamiliarBase.cs:101-102), dealt with type `Familiar` (867). Equipped familiar in `GameSlot.equippedFamiliar` string.
- NG+: `GameSlot.gameType == NewGamePlus`, plus level 1 or 2 (`wasDlcCompleted`) (GameManager.cs:2823-2834). Item stats scaled by `ngp*Factor` constants when the DB is built (ItemDatabase.cs:101-156) -> items differ by `plusLevel`, so always send plusLevel. Enemies scaled (EnemyStats.cs:177-191), potions/pool/pendant cost scaled. Slot conversion `StartGamePanel.UpdateSlotToNewGamePlus` (StartGamePanel.cs:1248-1308): non-equipment bag items sold to gold, Broom Spear + Training Sword given, familiars reset.

## 11. Single-hero assumptions (puppet risks)

- `HeroMerchant.Instance` used ~1700 times in 313 files (grep). `FindObjectOfType<HeroMerchant>` fallback (HeroMerchant.cs:97); `Awake` destroys duplicates (330-340); `OnDestroy` nulls `_instance` (465) -> destroying a puppet HeroMerchant breaks the real one.
- Enemies target `HeroMerchant.Instance.transform` (EnemyBehaviour.cs:103) and damage only `HeroMerchant.Instance` on any `"Player"`-tagged contact (EnemyBehaviour.cs:357-363). A puppet tagged Player would make the local player take the hit. Untag puppets (or patch `AttackHit`).
- `PickOnWalk`, `HealingArea`, `TallGrass` (calls `GetComponent<HeroMerchant>()` on collider, TallGrass.cs:34), `Mirror`, `DungeonDoor`, triggers all key off tag `Player`.
- `Equipment.OnEquipped/OnUnequipped`, `Weapon.OnEquipped`, `Ability.OnEquipped`, `Amulet`, `EquipmentStats` write into `HeroMerchant.Instance` (Equipment.cs:143-264, Weapon.cs:131-145, Ability.cs:120-128).
- `MoonlighterEffect.InternalInit` treats any effect under a HeroMerchant as "Player" target and is unique per type globally (MoonlighterEffect.cs:105-128).
- Static events on `HeroMerchantInventory`, `Weapon`, `Ability`, `PickOnWalk`, `HeroMerchant` are shared.
- Camera: `CameraManager.heroTransform` = `HeroMerchant.Instance.transform`; `SetFollowTarget(Transform=null)` defaults to hero; `followTarget` field (CameraManager.cs:69, 151-219).
- Input: one global `InputActionsManager.Instance.actions` read directly in controller `Update` (HeroMerchantController.cs:926). A puppet must have `HeroMerchantController.Update` disabled (or component removed).
- `PausePanel.Enable` sets speed 0 on all Animators under `HeroMerchant.Instance` (PausePanel.cs:96-100); hit-stop and death `PauseGame()` change global `Time.timeScale` (GameManager.cs:2592, 2651) - affects puppets and both machines' sim if replicated.
- `SMAnimatorHandler`: `SMStateAdvisor.target` set in OnEnable for the animator's behaviours (SMAnimatorHandler.cs:36-43); a cloned animator with SMAnimatorHandler would call the puppet's controller callbacks.
- Layers: hero layer toggles Default/IntangibleHero/Flying; enemy projectiles collide with these layers (inferred); puppets should use a non-colliding layer.
