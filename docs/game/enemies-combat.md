# Moonlighter 1.15.8: enemies, combat, bosses (code map for the co-op mod)

Source: ILSpy decompile `moonlighter/decomp/Assembly-CSharp`. All citations are `File.cs:line` relative to that folder. "Inferred" means the fact is not directly visible in C# (it lives in prefabs, Animator controllers or animation-clip events).

---

## 1. Enemy class hierarchy and lifecycle

### Core components (one GameObject per enemy)
| Class | Role | Key members |
|---|---|---|
| `Enemy : MonoBehaviour` (Enemy.cs:7) | Facade / damage entry | `public EnemyStats enemyStats` (28), `public EnemyBehaviour enemyBehaviour` (31), `public PolyNavAgent navAgent` (34), `public List<ItemDrop> itemDrops` (36), `public SpriteRenderer enemySprite` (39), `public DungeonRoom enemyRoom` (41), `public bool initAtStart` (43), `public new string name` (66, the **stats key**, not GameObject.name), `protected bool _isMiniBoss` / `IsMiniBoss` (74, 100), `public List<Enemy> enemyParts` (76), `public EnemyDamageReceivedType LastDamageReceived` (80), `public static event Action<Enemy, float, EnemyDamageReceivedType> OnEnemyRecivesDamage` (112) |
| `EnemyBehaviour : MonoBehaviour` (EnemyBehaviour.cs:8) | AI base; ~80 subclasses | `public Enemy enemy` (28), **`public Transform heroMerchantTransform` (31)**, `public Animator enemyAnimator` (34), `public EnemyState enemyState` (38; enum Idle/PursuingPlayer/Attacking/RunningAway/Stunned, 10-17), `public bool canMove` (43), `protected bool targetPlayer` (51), `private bool playingDamagedEffect` (53), `public List<SpriteRenderer> aditionalSpriteRenderer` (57), `public MoonlighterEffect effectOnAttack` (63), `protected Vector3 playerOffset` (65) |
| `EnemyStats : MonoBehaviour` (EnemyStats.cs:7) | HP and stats | `CurrentHealth` property (62-93), `Invencible` property (97-112), `isAlive` (60), `isChampion` (28), read-only `maxHealth/strength/speed/defense/weight` from `_currentStats` (44-58), events `OnHealthChanged`, `OnHealthDecreased` (`event Action<float>`, 114-116), delegate fields `OnDeath` (`OnDeathState`, 40), `OnInvencibleUpdate` (38) |
| `EnemyStatModificator` (EnemyStatModificator.cs:5) | Serializable stats row (from JSON) | size, tint, maxHealth, speed, strength, defense, weight, shieldPushForce, culture, isModifiedByFloor |
| `EnemyStatManager : ScriptableObject` (EnemyStatManager.cs:6) | Singleton stats table | `static Instance` (14), `ReadFile("enemy_stats")` (39-47), DLC `enemy_stats.json` (49-63), `GetModificator(string key, out normal, out champion)` (65) |
| `EnemyDamageReceiver : MonoBehaviour` (EnemyDamageReceiver.cs:3) | Weak-point hitbox on a child object (tag `"EnemyDamageReceiver"`) | `public EnemyBehaviour gameObjectToAdvise`; `DealDamageToEnemy(float, type)` forwards to `Enemy.DealDamageToEnemy(damage, this, type)` (7-13) |
| `EnemyAttackCollisionDetection` (EnemyAttackCollisionDetection.cs:3) | Attack hitbox on a child object | `OnTriggerEnter2D`/`OnCollisionEnter2D` → `gameObjectToAdvise.GetComponent<EnemyBehaviour>().AttackHit(other.gameObject)` (9-46) |
| `EnemyHealthBar` (EnemyHealthBar.cs:6) | World-space bar, created in `Enemy.Init` (Enemy.cs:139-152) | `Init(Enemy)` subscribes `OnHealthChanged` (38-58) |
| `PolyNavAgent` / `PolyNav2D` / `PolyNav2DBody` | Pathfinding (navmesh polygons). `PolyNav2D.current` is a static (EnemyBehaviour.cs:233). `AStar.cs`/`Heap.cs` exist (inferred: used by PolyNav). | `navAgent.SetDestination(Vector2)`, `Stop()`, `maxSpeed`, `stoppingDistance` |
| `BreakableObject : Enemy` (BreakableObject.cs:3) + `BreakableObjectBehaviour : EnemyBehaviour` (BreakableObjectBehaviour.cs:7) | Pots/boxes are Enemies. No health bar (Enemy.cs:139), not counted for room clear (EnemyStats.cs:268), no kill stats (EnemyStats.cs:240) |
| `InvulnerableEnemy : Enemy` (InvulnerableEnemy.cs:1) | Dies on `enemyRoom.OnAllEnemiesDefeated` (3-12); ignored by room-clear check (DungeonRoom.cs:236-238) |

`Agent`/`AgentSystem`/`Blackboard`/`Mind`/`Body` are the **shop visitor / cinematic NPC** framework (DummyAgent etc.), not used by enemies (inferred from usage; enemy code never references them).

### Instantiation (room spawn)
- `DungeonRoom.SpawnEnemies()` (DungeonRoom.cs:789-825): picks `roomInfo.enemiesSpawnPacks[currentSpawnPack]`; champion index via `UnityEngine.Random.Range` (794, 802); loops `SpawnEnemy(enemySpawn, enemySpawn.index, num == i)`.
- `public Enemy SpawnEnemy(DungeonRoomInfo.EnemySpawn spawnPair, int tile, bool isChampion = false)` (827-854): `CreateEnemyPrefab` (856-880: `EnemiesPrefabRegister.GetEnemyByName(name)` else `PrefabRegister.Instance.GetPrefabByName`, instantiated under a new `"EnemySpawn"` GameObject at `TranslateTileToPosition(tile)` inside `"DungeonLevelEnemies"` container), then `enemyRoom = this`, `SetActive(true)`, `Init(spawnPair.enemy.name)`, optional `TransformToChampion()`, `roomEnemies.Add`.
- `Enemy.Init(string _name)` (Enemy.cs:126-176) → `enemyStats.Init(name)` → `enemyBehaviour.Init()` → health bar; Wanderer-culture adds an extra ItemDrop.
- `EnemyBehaviour.Init()` (EnemyBehaviour.cs:92-120): **caches `heroMerchantTransform = HeroMerchant.Instance.transform` (103)**, sets `navAgent.maxSpeed`, subscribes `enemyStats.OnHealthDecreased += EnemyWasHit`, `enemyStats.OnDeath += EnemyStats_OnDeath`, `enemyRoom.OnRoomEnter += EnemyRoom_OnRoomEnter`.
- `ResurrectEnemy(EnemySpawn)` (882-902) re-spawns from `deadEnemies` (used e.g. by revives).

### Activation / deactivation (no pooling of normal enemies)
- `DungeonRoom.EnterRoom()` (1551-1575): `Invoke("EnableEnemies", enemiesAppearDelay)`, `HeroMerchant.Instance.IsInCombat = true` if enemies, `CloseAllDoors()`, `TriggerOnRoomEnter()`.
- `EnableEnemies()` (934-948): `SetActive(true)` + `StartTargetingPlayerAfterTime(enemyTimeToStartTargetingPlayer = 1f)` (33) → `Invoke("StartTargetingPlayer")` → `targetPlayer = true` (EnemyBehaviour.cs:138-165).
- `ExitRoom()` (1527-1543) → `DisableEnemies()` (950-966): `OnPlayerLeaveRoom()` (sets vencible), `CancelPlayerTargetingAfterTime()`, `SetActive(false)`; room GameObject disabled 0.5 s later (1537, 1545-1549).
- `EnemyBehaviour.EnemyRoom_OnRoomEnter()` (132-136) **resets local position to zero and HP to max on every room re-entry** (enemies heal if you leave).
- Only boss/minion helpers use `DungeonManager.Instance.poolManager` (PoolManager.cs:88-147, `CreateOrFillGameObjectPool(name, prefab, n)`, `GetObject(name)`, `GameObjectPool.ReturnObject(go)`), e.g. MotherGolem elementals (MotherGolemBehaviour.cs:51, 227-234), Desert boss elementals (section 7).

### Stable identification
- No enemy id field exists. Candidates: (a) index in `DungeonRoom.roomEnemies` (`public List<Enemy>`, DungeonRoom.cs:107) — **unstable**, entries are removed on death (906) and added by spawners/bosses; (b) **`enemiesToSpawnSelection[enemy]`** (`Dictionary<Enemy, EnemySpawn>`, set at 841/893) → the `DungeonRoomInfo.EnemySpawn` object, and its position in `roomInfo.enemiesSpawnPacks[currentSpawnPack].spawns` is the spawn-order index `i` in `SpawnEnemies` (818-822). Recommended id = (room id, spawn order i) assigned in a postfix on `SpawnEnemies`/`SpawnEnemy`; runtime-spawned children get owner-assigned ids. Note `EnemySpawn.index` (tile) is overwritten with the death tile on death (928).
- Room generation and spawn pack selection must already be deterministic across machines (dungeon seed: other doc); champion choice uses unseeded `UnityEngine.Random` (794, 802) → owner must send it.

---

## 2. Targeting: how AI finds the player

- **Central cache**: `EnemyBehaviour.heroMerchantTransform` is assigned **exactly once in the whole assembly**, at EnemyBehaviour.cs:103 (`grep "heroMerchantTransform = "` → 1 hit). It is used 291 times across 75 files. Redirecting it per enemy (postfix on `EnemyBehaviour.Init`, or set it every frame/decision tick) retargets movement, facing and most aiming.
- **But the singleton is also read directly**: `HeroMerchant.Instance` appears 446 times in 99 `*Behaviour*/*Boss*/*Projectile*` files. `GameManager.Instance.hero` style accessors: none exist (0 hits). `HeroMerchant.Instance` is a lazy `FindObjectOfType<HeroMerchant>()` static (HeroMerchant.cs:91-101).
- Categories of direct reads:
  1. **Damage target** (always the local hero): `EnemyBehaviour.AttackHit(GameObject)` (EnemyBehaviour.cs:355-365) checks `other.CompareTag("Player")`, then shield/isDead on `HeroMerchant.Instance` and calls `HeroMerchant.Instance.DealDamageToPlayer(...)` regardless of which "Player" collider was hit. `BossBehaviour.DealDamageToPlayer()` (BossBehaviour.cs:24-30). Many projectiles route via owner `AttackHit` (section 6).
  2. **Knockback on hero**: `PushPlayerBackwards()` (372-389), `WaitAndCheckForHoles` (391-398).
  3. **Aim at spawn time**: e.g. `EnemyDashTowardsThePlayer` (296-301), GolemKing rocks (GolemKingBoss.cs:253), Gizmo dash (GizmoBoss.cs:193), Wanderer jump/teleport (WandererBossPhaseOneBehaviour.cs:306-315, 381-395).
  4. **Homing every frame**: `GolemKingPunchBehaviour.Update` (GolemKingPunchBehaviour.cs:36, 41-47), `GizmoBossPortalAttack.Update` (GizmoBossPortalAttack.cs:15-44), `PirateBossProjectile` (PirateBossProjectile.cs:61).
  5. **Events/state**: `HeroMerchant.OnDie` subscribed by ~13 behaviours (grep list: GiantSlimeBehaviour.cs:49, NormalSlimeBehaviour.cs:51, Slime2_0Behaviour.cs:51, GolemWarriorBehaviour.cs:88, FruitTreeBehaviour.cs:113, FireRhombusBehaviour.cs:109, OscillatorBehaviour.cs:145, EvilWillBehaviour.cs:223, DimensionalWizardBehaviour.cs:71, DoubleWeaponSkeletonBehaviour.cs:63, CorruptDimensionalPoliceBehaviour.cs:215 …); comboCounter for damage numbers (Enemy.cs:393).
- Death push uses `heroMerchantTransform` (EnemyBehaviour.cs:638).
- **Most central places to patch (redirect target)**:
  1. `EnemyBehaviour.Init` — EnemyBehaviour.cs:103 (the cache).
  2. `EnemyBehaviour.ChooseAndApplyAction` — 191-202 (distance check → AttackPlayer/MoveEnemy).
  3. `EnemyBehaviour.SetPlayerDestination` — 219-262 (nav destination).
  4. `EnemyBehaviour.LookToPosition` — 316-330 (Animator `DistanceX/DistanceY`).
  5. `EnemyBehaviour.AttackHit(GameObject)` — 355-365 (the hit → hero damage; must dispatch to the hit player's machine).
  6. `EnemyBehaviour.EnemyDashTowardsThePlayer` — 296-301.
  7. `EnemyBehaviour.PushPlayerBackwards(…)` — 372-389.
  8. `BossBehaviour.DealDamageToPlayer` — BossBehaviour.cs:24-30.
  9. `HeroMerchant.DealDamageToPlayer` — HeroMerchant.cs:1014 (choke point for all hero damage).
  10. `DamageOverTimeEffect.DamageHeroMerchant` — DamageOverTimeEffect.cs:97-136 (reads `HeroMerchant.Instance`).
  11. `MoonlighterEffect.InternalInit/GetMerchant` — MoonlighterEffect.cs:105-199 (status on hero).
  12. Per-class overrides with their own `HeroMerchant.Instance` reads: GolemKingBoss.cs:131-138, 253, 387; TeslaBoss.cs:312-332, 475-494; GizmoBoss.cs:193; GolemKingPunchBehaviour.cs:36; GizmoBossPortalAttack.cs:15-44.
  13. `Enemy.DropItemsOnDeath` directional drop uses `HeroMerchant.Instance.transform` (Enemy.cs:269) and quests (261).
- Design implication: patch `heroMerchantTransform` to the "current target" chosen by the owner (nearest alive player, local hero Transform or remote puppet Transform); remote puppets must carry tag `"Player"` (and a layer the enemy hitboxes collide with) so `AttackHit` fires, and a prefix on `EnemyBehaviour.AttackHit` must route the damage to the owner of that player instead of the local `HeroMerchant.Instance`.

---

## 3. Damage pipeline

### 3a. Hero hits enemy
1. Weapon child collider `WeaponCollisionDetection` (WeaponCollisionDetection.cs:3): `OnTriggerEnter2D/OnCollisionEnter2D` → `_weapon.SendMainAttackHit(go)` / `SendSecondaryAttackHit(go)` (42-68); while overlapping re-hits every `Constants "attackTickTime"` (84-95).
2. `Weapon.SendMainAttackHit` → `SendMessage("OnMainAttackHit", other)` (Weapon.cs:268-276) → `public void OnMainAttackHit(GameObject other)` (278-355): fires `static event Action<Weapon, GameObject> OnMainAttackHitEvent` (84, 280-283); for tag `HardEnemy`/`SoftEnemy`: screen shake, `PushEnemyBackwards(other, weaponMaster.mainAttackPushBackwardsForce)` (296; 416-425: impulse if not boss and `weight < 48`), rolls combat/secondary/slime effects with `UnityEngine.Random.Range` (304-315), then `component.DealDamageToEnemy(strength * lastComboHitDamageMultiplier on combo == maxComboCounter, else strength)` (316-323), hit-stop `GameManager.Instance.SetGameSpeedForTime(...)` (325). Tag `EnemyDamageReceiver` → `EnemyDamageReceiver.DealDamageToEnemy` (327-350).
3. Secondary attacks: `Weapon.OnSecondaryAttackHit` (376-414) only pushes/effects; damage comes from weapon-specific classes: `SecondaryAttackWeapon` (SecondaryAttackWeapon.cs:38), `Bow` (Bow.cs:197), `BowArrow` (BowArrow.cs:57, 65), `ExplosiveArrow` (ExplosiveArrow.cs:56-60, 110, type `Trick`), `TrickWeapon` (Moonlighter.Weapons/TrickWeapon.cs:257), `HeroMerchantProjectile` (HeroMerchantProjectile.cs:157-161, uses `intelligence`), `HeroRingAmuletEffect_Projectile` (Moonlighter.DLC.WandererDungeon/HeroRingAmuletEffect_Projectile.cs:35).
4. **`public void DealDamageToEnemy(float attackStrength, EnemyDamageReceiver damageReceiver = null, EnemyDamageReceivedType enemyDamageReceivedType = EnemyDamageReceivedType.Normal)`** (Enemy.cs:328-402): returns if behaviour disabled (330); `totalDamage = CalcHitDamage(attackStrength, enemyStats.defense)` (337; formula at 404-408: `hit - round(hit*def/100)`, clamped ≥0); returns if HP ≤ 0; amulet `IModifyPlayerDamage.GetPlayerExtraDamageValue` adds damage (345-353); `Effect`/`Trick` types temporarily bypass `Invencible` (354-367); `enemyStats.CurrentHealth -= totalDamage` (358); fires `Enemy.OnEnemyRecivesDamage` (359-362); hit sound; floating number via `QuantitytIndicatorManager.Instance.InstantiateFloatingDamageIndicator(...)` (389-401).
   - `EnemyDamageReceivedType { Normal, Effect, Familiar, Trick }` (Enemy.cs:9-15).
5. `EnemyStats.CurrentHealth` setter (EnemyStats.cs:62-93): **ignored while `_invincible` or dead** (70); fires `OnHealthChanged(value)`, `OnHealthDecreased(value)` (before assignment, so `CurrentHealth` still holds the old value inside handlers), clamps, at 0 sets `_isAlive=false`, invokes `OnDeath` delegate then `Die()`.
6. Hit reaction: `EnemyBehaviour.EnemyWasHit(float)` (428-438) → `HealthDecreasedWhenHit` (545-551) → `PlayDamagedEffect()` (441-455): **sets `Invencible = true` (461) and `ForbidEnemyMovement()`**, red/white flash via `Invoke("SetEnemyShaderColorToWhite"/"…Red"/"SetEnemyShaderToDefault")` with `Constants enemyDamagedRedTime/WhiteTime`, coroutine `SetPlayingDamagedEffectToFalse` re-enables (467-479). So **i-frames after each hit = 2*red+white time**. Unchanged health → `SetTrigger("NotDamagedWhenHit")` (553-556). Shader params: material `_FlashAmount`, `_Color` (486-534).
7. Death → section 4.

### 3b. Enemy / projectile / trap hits hero
1. Enemy hitbox `EnemyAttackCollisionDetection` → `EnemyBehaviour.AttackHit(GameObject other)` (EnemyBehaviour.cs:355-365): requires tag `"Player"`, `!CheckIfPlayerIsShieldProtected(gameObject)` (HeroMerchantController.cs:2133-2141: short-sword block facing enemy within `defensiveFieldOfVisionAngle`), hero not dead, enemy alive; applies `effectOnAttack` via `MoonlighterEffect.Create(effectOnAttack, HeroMerchant.Instance.gameObject, enemy.enemyStats)`; then `HeroMerchant.Instance.DealDamageToPlayer(enemy.enemyStats.strength, enemy.name, HeroMerchant.DamageType.Normal, enemy)`.
2. **`public void DealDamageToPlayer(float attackStrength, string damageSource, DamageType type = DamageType.Normal, Enemy enemySource = null)`** (HeroMerchant.cs:1014-1103). `DamageType { Normal, ItemDrain, Self }` (14-19).
   - 0 damage in town (1016-1019); return if `isDead` (1020).
   - `CalcHitDamage(strength, heroMerchantStats.defense.Value)` (1029; same formula 1141-1145) unless `Self`; amulets `IModifyEnemyDamage.GetEnemyExtraDamageValue` (1031-1037).
   - Shield charge: `shield.Value--`, damage 0, i-frames (1045-1057). Absorption (1058-1072). Health: `currentHealth.Value -= num3` (1073-1076).
   - **I-frames**: `heroMerchantStats.SetInvencible(1f)` (1080; HeroMerchantStats.cs:81-97: sets `currentHealth.variableStat = false`, re-enabled by `DOVirtual.DelayedCall(time, SetVencible)`), `heroMerchantController.StartPlayingInvencibilityEffect(1f, 0.2f)` (HeroMerchantController.cs:3053: blink), `SetIntangibleMode()` (3205-3215: layer `"IntangibleHero"`). Invincibility is implemented by `Stat.Value` setter refusing writes when `variableStat == false` (Stat.cs:26, 53-60).
   - Camera rumble, red `GUIManager.Instance.Flash`, vibration; `static event Action<float, Enemy> OnDealedDamage` (245, 1091-1094); `ItemDrain` → `heroMerchantInventory.DropRandomItems(3, false)` (1099-1102).
3. Hero death: `Stat.OnStateUpdated` → `HeroMerchantStats.HealthValueChanged` (HeroMerchantStats.cs:47, 122-128) → `Die()` (138-154): `isDead = true`, death animation `PlayDeathAnimation()` (HeroMerchantController.cs:3435-3449, **calls `GameManager.Instance.PauseGame()`**), `SetInvencible()`. After the animation → `HeroMerchant.OnWillDeathAnimationEnded()` (HeroMerchant.cs:691-715) → `AngelAmuletEffect` revive or `static event Action OnDie` (241, 702-705) → `DungeonManager.HeroMerchant_OnDie` (DungeonManager.cs:178, 204).
4. Knockback: `EnemyBehaviour.PushPlayerBackwards(float)` → `HeroMerchantController.AddForce(Vector2)` (HeroMerchantController.cs:841).

### 3c. Status effects
- Base `MoonlighterEffect : MonoBehaviour` (MoonlighterEffect.cs:6): `EffectDamageTarget { None, Player, Enemy }`, `UniqueAction`, `effectName`; **created as a child GameObject of the target**: `public static MoonlighterEffect Create(MoonlighterEffect prefab, GameObject target[, HeroMerchantStats | EnemyStats stats])` (79-103); `InternalInit` resolves `GetMerchant()`/`GetEnemy()` from the parent (105-199) and calls `ApplyToHeroMerchant` / `ApplyToEnemy` (199-204). Static events `OnEffectCreated/OnEffectReset/OnEffectDestroyed` (54-58). Registry: `MoonlighterEffectRegistry.GetEffects<T>()`, `GetEffect(Type, EffectDamageTarget)` (MoonlighterEffectRegistry.cs).
- `DamageOverTimeEffect : MoonlighterEffect` (DamageOverTimeEffect.cs:4), `EffectType { Venom, Burn, Electrical }` (6-11) = poison/fire/electric. Coroutines with `WaitForSeconds(timeBetweenTicks)`: on hero `HeroMerchant.Instance.DealDamageToPlayer(damagePerTick * _enemyStats.strength, "DamageOverTime")` (118, 128); on enemy `enemy.DealDamageToEnemy(damagePerTick * heroStrength, null, EnemyDamageReceivedType.Effect)` (177).
- `ConfusedEffect : MoonlighterEffect` (ConfusedEffect.cs:4): on enemy `ForbidEnemyMovement`, `navAgent.Stop`, **`enemyAnimator.speed = 0`** (76-96), restored after `confusedTime` (108-123). Also on hero (41-63). `DungeonRoom` confuse-all helper (DungeonRoom.cs:2267-2272).
- `SpeedEffect : MoonlighterEffect` (SpeedEffect.cs:3): slow; on enemy `StopTargetingPlayer()`/`StartTargetingPlayer()` (80-93).
- Others: `GhostEffect`, `BoostEffect`, `AreaEffect` (pooled lava/gas areas), `GasAreaEffect`, `TrickGasAreaEffect`, potion effects (`SpeedPotionEffect`, `StrengthPotionEffect`, `InvulneravilityPotionEffect`), ~25 `*AmuletEffect : AmuletMoonlighterEffect` (anti-poison/fire/electric/confusion, Vampire, InstaKill, Luck, BlackCat…).
- Where applied: hero weapon hit (Weapon.cs:304-315, `Constants.GetFloat(effectName)` probability), `EnemyBehaviour.effectOnAttack` (EnemyBehaviour.cs:359-362), projectiles/traps via owner `AttackHit`.

---

## 4. Enemy death

Chain: `CurrentHealth = 0` (EnemyStats.cs:83-91) → `OnDeath` delegate → `EnemyBehaviour.EnemyStats_OnDeath()` (EnemyBehaviour.cs:122-130: unsub OnRoomEnter, analytics) → `protected virtual void OnDeath()` (596-641: layer `"TransparentFX"`, DOTween flash/grey, `Invoke("SpawnDeathParticles", 0.48)`, **`Destroy(gameObject, 0.6f)`**, animator speed 0 + disabled, death impulse away from `heroMerchantTransform`, `enabled = false`) → then `EnemyStats.Die()` (237-263: platform stats `ENEMIES_KILLED`, `*_ENEMIES_KILLED_NUM`) → `MarkEnemyAsDefeated()` (265-272) → **`internal void DungeonRoom.EnemyDefeated(Enemy enemy)`** (DungeonRoom.cs:904-932): `roomEnemies.Remove`, if `AreAllEnemiesDefeated()` → `isRoomCleared = true`, minimap, `event Action OnAllEnemiesDefeated` (253; handlers `UnLockRoomChests`, `OpenAllDoors`, `ResetHeroMerchantIsInCombat`, 1909-1911); then `event Action<Enemy> OnEnemyDefeated` (247); records the death tile into `deadEnemies`.
- Falling into a hole also kills (EnemyBehaviour.cs:179-188, sets HP 0 with random spin direction).
- **Loot happens in `Enemy.OnDestroy()`** (Enemy.cs:178-193), i.e. 0.6 s after death, only for non-boss: `SpawnEnemyDeadSprite()` (→ `DungeonRoom.SpawnEnemyDeadSprite(Sprite, Vector3)`, DungeonRoom.cs:978) and `DropItemsOnDeath()` (Enemy.cs:242-285):
  - Skipped in Intermediate dungeons (244).
  - Per `ItemDrop` (ItemDrop.cs:5): `List<ItemStack> DropItem(Vector3 position, float radius = 30f, int _minDropRate = 0, Transform parent = null)` (16-40): **`UnityEngine.Random.value`** vs `dropProbability` (+Luck/−BlackCat amulets), count `UnityEngine.Random.Range(min, max+1)`; `SpawnItem` → `ItemDatabase.GetItemByName(name, GameManager.Instance.GetCurrentGamePlusLevel())` → `ItemStack.Create(master)` → `itemStack.Drop(position, radius, parent)` (42-52). The floor item is an **`ItemStack`** GameObject (scatter position chosen inside `Drop`, inferred random). Results registered with `enemyRoom.AddDropItems(items)` (DungeonRoom.cs:1613; fires `OnDropListChanged`, 255).
  - Kill quest: matching `HeroMerchant.Instance.activeQuests` (`quest.target == name`) drops `quest.killQuestTarget` item (261-280).
  - Familiar egg: `SpawnFamiliarEgg()` (287-296) using `FamiliarsManager.GetEggItem(name, out SpawnEggInfo)` and `UnityEngine.Random.value > 0.5f`.
  - Champions add `"championDroppedItemName"` drops (203-240).
- Gold: enemies drop no gold directly; coin items are `ItemStack` + `Coin` (Coin.cs:3) consumed on pickup → `HeroMerchant.Instance.addGold(Quantity+1)` (12-17).
- Hooks for the mod: `Enemy.OnEnemyRecivesDamage` (static), `EnemyStats.OnDeath`, `DungeonRoom.OnEnemyDefeated`, `DungeonRoom.OnAllEnemiesDefeated`, `Enemy.DropItemsOnDeath` (prefix to run loot only on the owner and replicate `ItemStack`s).

---

## 5. Enemies that create enemies or objects

| Spawner | Spawn call | Registration | Notes |
|---|---|---|---|
| Room API | `DungeonRoom.SpawnEnemy(EnemySpawn, int tile, bool champion)` (DungeonRoom.cs:827) | adds to `roomEnemies` itself | used by all "proper" spawners below |
| `SlimeBossBehaviour` (abstract base of `GiantSlimeBehaviour`, `NormalSlimeBehaviour`; SlimeBossBehaviour.cs:5) | pre-spawns split slimes in `Init` with `enemyRoom.SpawnEnemy(...)` (94), then removes them from `roomEnemies` and `SetActive(false)` (76-101) | `OnDeath` re-adds `enemy.enemyRoom.roomEnemies.Add(spawnedSlime)` (210) and plays split | split children exist hidden from the start → can be id'd at spawn |
| `ForestSeederBehaviour` | `enemyRoom.SpawnEnemy(enemySpawn, tile)` (ForestSeederBehaviour.cs:122), `StartTargetingPlayer()`, `parentEnemy = this` (123-127) | via SpawnEnemy | `SeederCuttingBehaviour` children |
| `MotherGolemBehaviour` | pooled `poolManager.GetObject(spawnedEnemyPooledName)` (MotherGolemBehaviour.cs:51, 227-234) | `MotherGolemElementalBehaviour.SetParent` → `roomEnemies.Add(enemy)` (MotherGolemElementalBehaviour.cs:30-35) | pooled, reused objects |
| `GolemKingRockBehaviour` (boss) | Instantiate `RandomEnemyPrefabs.GetRandom()` (GolemKingRockBehaviour.cs:180-195) | `roomEnemies.Add` (191) | |
| `CarnivorousMutaeBossBehaviour` | `"Enemies/Forest/Cutting"` (360-371) | `DungeonManager.Instance.currentDungeonRoom.roomEnemies.Add` (453) | uses local current room |
| `DesertBossBehaviour` | pooled `"Enemies/Desert/DesertBossElemental"` (466-499) | **not registered** | |
| `EnemyWarpSpawner` | `room.SpawnEnemy(...)` (EnemyWarpSpawner.cs:175), names from `spawnNamesPack.GetRandom()` | via SpawnEnemy | used by Gizmo phase 2 |
| `PostBossSpawnEnemyController` | `bossRoom.SpawnEnemy(enemySpawn, 0)` (PostBossSpawnEnemyController.cs:82) | via SpawnEnemy | weighted random `EligibleEnemySet` |
| `EnemyRandomSpawnerController` (Moonlighter.DungeonGeneration) | `_room.SpawnEnemy(...)` (EnemyRandomSpawnerController.cs:103), `_room.DisableEnemies()` (108) | via SpawnEnemy | |
| `DeathGeneratorBehaviour` | Instantiates electric balls/rays (DeathGeneratorBehaviour.cs:67-70); iterates `enemy.enemyRoom.roomEnemies` (276) | objects, not enemies | |
| Healers/buffers (`RepairDroneBehaviour`, `FlyingRepairGolemBehaviour`, `RechargerBehaviour`, `GraafGeneratorBehaviour`) | pick targets from `enemyRoom.roomEnemies` (inferred from DeathGenerator pattern; not individually verified) and apply `EnemyStats.AddModificator(key, mod)` (EnemyStats.cs:283) (inferred) | | |
| Debug | `HUDDebug.SpawnEnemy` (HUDDebug.cs:1072-1092) | | |

Other runtime-spawned enemy-side objects: turret/tree projectiles (section 6), `GolemKingSlimeTrap`, `CarnivorousMutaeBossRoots`, `AreaEffect` (pooled), traps (`SlimeTrapBehaviour`, `ElectricPostBehaviour`, `FireRhombusBehaviour`, `OscillatorBehaviour`, `LavaCatapultBehaviour` are themselves `EnemyBehaviour` subclasses placed by the room). Mimics (`Mimic`, `GoldMimic`, `IronMimic`, `WoodMimic`) are `EnemyBehaviour` subclasses (inferred from file names `Mimic.cs` in subclass list).

---

## 6. Projectiles

- **No common base class**: every enemy projectile is an independent `MonoBehaviour` with a `public GameObject projectileOwner` (e.g. GolemTurretProjectile.cs:13) and damages by calling **`projectileOwner.GetComponent<OwnerBehaviour>().AttackHit(other.gameObject)`** → `EnemyBehaviour.AttackHit` → `HeroMerchant.Instance.DealDamageToPlayer(ownerStrength, …)`. Damage strength therefore comes from the owner enemy's stats; if the owner is destroyed the call NREs (inferred).
- Typical spawn: `Instantiate(prefab)` from a serialized prefab field on the behaviour, aimed once at `heroMerchantTransform.position` (e.g. `GolemTurretBehaviour.ShootProjectile`, GolemTurretBehaviour.cs:323-326) and launched from an animation event (`SetTrigger("Shoot")` at 320 → `ShootProjectile`, inferred event). Movement is mostly DOTween `DOMove*` with `SetUpdate(isIndependentUpdate:false)` (GolemTurretProjectile.cs:35-59, includes `UnityEngine.Random` deviation), some `Rigidbody2D.velocity` or manual `Time.deltaTime`.
- Pooling: only a few use `DungeonManager.Instance.poolManager` (ExplosiveArmedRobotProjectile, FruitTreeProjectile, Carnivorous spores, Desert fire). `ProjectilePool` (ProjectilePool.cs:3: `GetObject()`, `PoolObject(go)`) is a simple child-based pool. 45 `poolManager.*` call sites in total.
- Lobbed projectiles: no `DOJump`/Bezier in projectiles; arcs are made with separate X/Y `DOMove` tweens like `EnemyBehaviour.MakeJump` (EnemyBehaviour.cs:272-294) (e.g. CarnivorousMutaeBossSpawnProjectile, FruitTreeProjectile, PileOfRocksProjectile — inferred from DOMove usage).

| Class | Movement | Hits hero via | Pooled |
|---|---|---|---|
| BladeTreeLeafProjectile | DOMove | `BladeTreeSproutBehaviour.AttackHit` (150) | no |
| BoomerangSkeletonProjectile | velocity, reads HM.Instance | `BoomerangSkeletonBehaviour.AttackHit` (145) | no |
| BrokenGolemTurretProjectile | DOMove | owner AttackHit (65) | no |
| CarnivorousMutaeBossSporeProjectile | DOMove | `projectileOwner.AttackHit` (87) | yes (boss) |
| CarnivorousMutaeBossSpawnProjectile | DOMove arc | spawns Cutting | no |
| ClothTangleFireballProyectile | DOMove | owner AttackHit (54) | no |
| CorruptDimensionalPoliceProjectile | Time.deltaTime | owner AttackHit (93) | no |
| CuttingProjectile / SeederCuttingProjectile | DOMove | owner AttackHit (75 / 50) | no |
| DesertBossFireProyectile / DesertBossElementalFireProyectile | DOMove | owner AttackHit (58 / 52) | yes / no |
| DoubleWeaponSkeletonProjectile | DOMove | own `AttackHit` (75) | no |
| ExplosiveArmedRobotProjectile | DOMove, HM.Instance ×5 | **direct** `HeroMerchant.Instance.DealDamageToPlayer` (145) | yes |
| FirePuppetProyectile / MagicianPuppetProyectile | deltaTime / ? | owner AttackHit (94 / 94) | no |
| FruitTreeProjectile(_Corrupted), FruitTurretProjectile | DOMove | owner AttackHit (151 / 131 / 72) | Fruit: yes |
| GoldMimicProjectile, GolemButlerProjectile, GolemTurretProjectile, SporeBushProjectile, PileOfRocksProjectile, TutorialBrokenGolemTurretProjectile | DOMove | owner AttackHit (77 / 45 / 67 / 80 / 200 / 58) | no |
| PirateBossProjectile | velocity, homing on HM.Instance | `GizmoBoss.AttackHit` (92) | no |
| WandererBossProjectile, WindTreeSproutProjectile | velocity | `WindTreeSproutBehaviour.AttackHit` (96) | no |
| WandererDimensionalEnergyProjectile | DOMove, aims HM.Instance | `WandererBossPhaseTwoBehaviour.AttackHit` (146) | yes (boss pool) |
| Hero side: `HeroMerchantProjectile` (base), `BowArrow : HeroMerchantProjectile`, `ExplosiveArrow : BowArrow`, `TrickWeaponProjectile : HeroMerchantProjectile`, `HeroRingAmuletEffect_Projectile : HeroMerchantProjectile` | velocity | `Enemy/EnemyDamageReceiver.DealDamageToEnemy` (see 3a) | no |
| Familiar: `BladeTreeSproutFamiliarProjectile`, `GolemTurretFamiliarProjectile`, `WindTreeSproutFamiliarProjectile` | DOMove/velocity | enemy damage (inferred) | no |

Shield: some projectiles also collide with tag `"Shield"` (GolemTurretProjectile.cs:65).

---

## 7. Bosses

### 7.0 Common shape
- Every boss is `Enemy` + a `BossBehaviour : EnemyBehaviour` subclass (BossBehaviour.cs:3). `Enemy.IsBoss()` = `enemyBehaviour is BossBehaviour` (Enemy.cs:450-457). Bosses get no `EnemyHealthBar` (Enemy.cs:139) and no automatic `DropItemsOnDeath` in `OnDestroy` (Enemy.cs:180-188).
- `BossBehaviour` fields: `public DungeonBossRoom.OnPlayerAction OnBossEndedStartAnimation; OnBossDeath;` (BossBehaviour.cs:5-7). `Init()` sets `Invencible = true` and `OnHealthChanged += UpdateBossHealtBar` (9-14). Virtuals: `StartWakeAnimation`, `StartMovingEnemy`, `DealDamageToPlayer`, `SetBossHealthBar`, `ShowBossGUI` (97-102), `EndedBossStartAnimation` (104-112: `Invencible=false`, **`roomEnemies.Add(enemy)`**, invoke delegate), `EndedBossDieAnimation` (114-120), `OnDeath` (132-141).
- State machines are **Animator-driven**: C# sets parameters (`"curentMovemment"`, `"Attack"`, `"Death"`…) and exposes many public parameterless methods without C# callers (animation-event targets, inferred). Timing via `Invoke/InvokeRepeating`, DOTween, and `ChooseAndApplyAction` from `EnemyBehaviour.Update` while `targetPlayer`.
- **No HP-percentage phases.** `BossBehaviour.HealthDecreasedWhenHit` (42-53) computes a speed-up but sets `enemyAnimator.speed = isAlive ? 1f : num2` → no-op while alive. "Phases" are separate boss objects (Gizmo 1→2, Wanderer 1→2) or part destruction (Naja).
- No `PlayableDirector`/Timeline in the assembly (0 grep hits). `Moonlighter.CutsceneUtils.SetMeTriggers` (SetMeTriggers.cs:7-57) only fires Animator triggers on a timer, never touches the hero.

### 7.A Boss classes
**GolemKingBoss : BossBehaviour** (Golem dungeon, GolemKingBoss.cs:8)
- Fixed cycle `enum GolemBossMovemment { Punch, Rocks, SlimeAttack }` (10-15); `OnMovemmentEnded()` → `(cur+1)%3`, `SetInteger("curentMovemment")`, `Push()` (115-120).
- `ChooseAndApplyAction` sets `PlayerInRange` (122-129); `MoveEnemy` X-only nav toward `heroMerchantTransform` (174-180); `IsPlayerInRange` reads `HeroMerchant.Instance` (131-138).
- Rocks: pre-instantiated (97-113); `SpawnRocks()` (247-337) heavy `UnityEngine.Random`, aims at `HeroMerchant.Instance` if `RockMovement.aimAtWill` (253); rock may contain an enemy (`enemyInRockProb`, 261, 308-311); `GolemKingSlimeTrap` (318-330).
- `GolemKingRockBehaviour.SpawnEnemy()` → Instantiate, `Init`, `StartTargetingPlayer`, `roomEnemies.Add` (GolemKingRockBehaviour.cs:180-195); rock contact damage (115-124).
- Sticky arm `InvokeRepeating("AimStickyArmAtWill", 0f, 0.1f)` (371-387), DOTween swipe (404-497), `GolemKingSlimeArm.OnCollisionEnter2D` (GolemKingSlimeArm.cs:7-13). Rocket punch homing (`GolemKingPunchBehaviour.Update`, GolemKingPunchBehaviour.cs:36-47, 81-86, 118-124). `GolemKingForcePushAttack.Push()` CircleCastAll + AddForce on tag `"Player"` (GolemKingForcePushAttack.cs:17-28).

**CarnivorousMutaeBossBehaviour : BossBehaviour** (Forest, CarnivorousMutaeBossBehaviour.cs:9)
- Cycle `{ SporeAttack, SpawnAttack, Roots, Smash }` (11-17), skip spawn when `spawnedEnemies.Count >= maxSpawnedEnemies` (124-132). Movement to random circle point around hero, re-`Invoke` after `Random.Range(1,5)` (142-151).
- Spores `InvokeRepeating("ThrowSpores", 0, 0.2f)` pooled (211-224, pools 101-104). Spawn attack lobs projectile, lands → `"Enemies/Forest/Cutting"` registered in `DungeonManager.Instance.currentDungeonRoom.roomEnemies` (244-260, 360-371, 448-459). Roots at hero + random (262-280). Jump `MakeJump` (287-301). Contact damage also hits enemies (480-497). `CarnivorousMutaeBossTentacle` self-timed `Invoke("Attack", Random.Range(1,10))` (CarnivorousMutaeBossTentacle.cs:38-67).

**DesertBossBehaviour : BossBehaviour** (Desert "Naja", DesertBossBehaviour.cs:10)
- `enum DesertBossAttacks { Head, Rombus, Square, Triangle, Circle, All }` (12-20), random pick excluding last (224-254), attacks removed as pieces die (909-912, 938).
- Multi-part: `List<DesertBossBodyPiece> pieces` (30), each its own `Enemy` + `DesertBossBodyPiece : EnemyBehaviour` (DesertBossBodyPiece.cs:7), init'd by boss (198); head invincible until pieces gone (827, 935-943); piece death → `boss.OnPieceDestroyed` (DesertBossBodyPiece.cs:234-242); bar = sum (872-881, 854-870).
- Movement random/chase/charge, uses `Time.realtimeSinceStartup` (510-613, 651-688). Fire beam / spiral pooled (354-433), lava `AreaEffect` (441-460), elementals pooled and unregistered (466-499). `AttackHit` override with source `"Naja"` (950-971).

**TeslaBoss : BossBehaviour** (Tech, TeslaBoss.cs:7)
- `{ ElectrifiedDash, HalfCircleAttack, ElectrifiedWall, ElectricBalls }` (9-15); `OnMovemmentEnded` → `Invoke("NextMovemment", Random.Range(1f,5f))` + `"Teleport"` (156-176, 203-206); empty `ChooseAndApplyAction` (199-201). Teleport to random `fixedMovePositions` (208-243), DOTween dash (245-281). Beam/wave hit test = `Physics2D.OverlapBoxAll` + tag `"Player"` → `AttackHit(HeroMerchant.Instance.gameObject)` (312-332, 475-494). Electric balls with `EnemyAttackCollisionDetection` (138-154).

**GizmoBoss : BossBehaviour** (final boss phase 1, culture `Unknown`, GizmoBoss.cs:8)
- `{ Slash, Dash, Slam, FlyingTorment }` weighted `{0.425, 0.1, 0.425, 0.15}` with `Random.Range` (10-24, 145-164). `MoveEnemy` sets `body.velocity` at hero (258-280); dash `DOMove` to `HeroMerchant.Instance` position (193, 347-370); `PirateBossProjectile` homing (376-385). `PirateDungeonMachine` holds `public GizmoBoss boss` (PirateDungeonMachine.cs:5) — no separate pirate boss.
- `public static bool isFirst = true` (18): intro dialog advanced by local `GUIManager.Instance.input.Action1.WasPressed` (211-217, 479-542). `ShowBossGUI` overridden empty (127-129).
- "Death" is a transition: `OnDeath` sets `"AlmostDie"`, freezes hero, no base call (429-441) → crawl to machine (282-297, 443-452) → `(enemy.enemyRoom as DungeonFinalBossRoom).ReplaceBoss()` (454-465).

**GizmoPhase2Boss : BossBehaviour** (GizmoPhase2Boss.cs:6)
- `{ HugAttack, PortalAttack, CrackAttack }` (8-13); random with `Invoke("StartMovemment", Random.Range(min,max))` (103-111). Hug pulls hero `AddForce` and **every `FindObjectsOfType<Enemy>()`** (171-220). `GizmoBossPortalAttack` follows `HeroMerchant.Instance` every Update (GizmoBossPortalAttack.cs:15-44). Crack → `EnemyWarpSpawner.Enable()` (334-391). `OnDisable` destroys all `roomEnemies` (513-523).

**WandererBossPhaseOneBehaviour / WandererBossPhaseTwoBehaviour : BossBehaviour** (DLC)
- P1: `StartWakeAnimation` → `WandererBossFightDialog.StartDialogFlow()` (local input coroutine, WandererBossFightDialog.cs:16-45). Range-based ground smash/jump/move (166-191), `SelectAttackType()` random thirds Jump/Teleport/Tentacles (203-234), `Invoke("StartAttack", timeBetweenAttacks)` (447-452); teleport random around hero (306-315); `WandererObstacle` pooled puddles (317-338); waves `SlimeWaveController.DoWave()` (408-415); own boss bar in `StartTargetingPlayer` (130-149).
- P2: `Init` sets `Invencible=false`, pool, `WandererArmsController` delegates (127-145); random quarters Teleport/Melt/LaunchArms/Projectile (233-264); melt coroutines use `Time.deltaTime` (340-365); `AttackHit` while smashing adds `DamageType.ItemDrain` (787-798).
- `Wanderer : EnemyBehaviour` (Wanderer.cs:6) is the roaming dungeon Wanderer, removed in boss rooms via `DungeonManager.Instance.RemoveWandererFromDungeon()` (DungeonBossRoom.cs:190-193).

**Mini-bosses** (plain `EnemyBehaviour` subclasses setting `enemy.IsMiniBoss = true` in Init; no boss room): `GolemWarriorBehaviour` (85), `GolemWarriorCorruptedBehaviour` (112), `FruitTreeBehaviour` (110), `FruitTreeBehaviour_Corrupted` (132), `FireRhombusBehaviour` (137), `OscillatorBehaviour` (151), `GiantSlimeBehaviour : SlimeBossBehaviour` (48), `EvilWillBehaviour` (221), `DoubleWeaponSkeletonBehaviour` (61), `DimensionalWizardBehaviour` (64), `CorruptDimensionalPoliceBehaviour` (209). Pattern (GolemWarriorBehaviour.cs:78-104): `StartTargetingPlayer` override writes the shared HUD boss bar (`SetMaxBossHealth/EnableBossHealthBar/SetBossHealth`) and `ShowDungeonWelcomeScroll(culture, 2, isBoss:true, isMiniBoss:true)`, hooks `HeroMerchant.OnDealedDamage` and `OnDie`; DLC ones `PlaylistManager.Instance.StartMinibossFightPlaylist()` (EvilWillBehaviour.cs:232-244). Rooms chosen by `GetMinibossRoom` (DungeonGenerator.cs:801-843) / `AddMinibossRoom` (Moonlighter.DungeonGeneration/DungeonGenerator.cs:1540, 1563).

### 7.B Boss room and intro flow
- Boss is part of the room prefab: `public BossBehaviour Boss` (DungeonBossRoom.cs:14); prefab `string.Concat(culture, "BossRoom")` instantiated in `AddBossRoom` (Moonlighter.DungeonGeneration/DungeonGenerator.cs:454-455, 729-766), which also places `DungeonBossRoomEnterDoor` and sets `dungeonManager.bossRoom`.
- `DungeonBossRoom.Start()` (101-121): if not `IsBossDefeated` (83-93: save bit `culturesDefeated` or `GameManager.Instance.bossDefeatedCulture`), `Boss.enemy.enemyRoom = this`, `isBossBattle = true`; else hides `Boss` and `bossChest`.
- Trigger chain:
  1. `DungeonBossRoomEnterDoor.OnPlayerEnter(GameObject player)` requires `player == HeroMerchant.Instance.gameObject` (DungeonBossRoomEnterDoor.cs:42-48); locks GUI, `DisableMoveInput()`, `ForbidMovement()`, `DisableHeroMerchantArt()`, door `"CloseSpecial"` (57-64).
  2. `OnDoorClosed()` (anim event, inferred) → re-enable hero, stop music, `base.OnPlayerEnter(HeroMerchant.Instance.gameObject)` (72-83).
  3. `DungeonBossRoom.EnterRoom()` (151-223): `PolyNav2D.current.GenerateMap(true)`, `HeroMerchant.OnDealedDamage += OnHeroDamaged` (perfect tracking), activate `DungeonRoomBossAlertArea`, close south door (184-187), `TriggerOnRoomEnter()`.
  4. `DungeonRoomBossAlertArea.OnTriggerEnter2D`: any `CompareTag("Player")` → `bossRoom.StartBossAwakeAnimation()` and self-disable (DungeonRoomBossAlertArea.cs:7-13).
  5. `StartBossAwakeAnimation()` (318-347): `canUsePendant=false`, `CameraManager.Instance.MoveCamera(Boss.transform)`, `PreventPanelChange()`, `IsInCombat=true`, hero `DisableMoveInput()+ForbidMovement()`, subscribe `Boss.OnBossEndedStartAnimation += BossStartAnimationEnded`, `Boss.OnBossDeath += InternalOnBossDeath`, `Boss.StartWakeAnimation()`.
  6. Wake clip → boss `EndedBossStartAnimation()` (GolemKingBoss.cs:146, CarnivorousMutaeBossBehaviour.cs:310, DesertBossBehaviour.cs:809, TeslaBoss.cs:593, GizmoBoss.cs:479/540, GizmoPhase2Boss.cs:497) → BossBehaviour.cs:104-112.
  7. `BossStartAnimationEnded()` → `AllowPlayerAndEnemyMovement()` (357-375: hero `AllowMovement()+EnableMoveInput()`, familiars `ForceDecide()`, `Boss.StartMovingEnemy()`), camera `SetArea`+`SetFollowTarget` (378-382).
- No `PauseGame`/`timeScale` in the intro; freeze = controller flags + GUI locks. Music: `BossBehaviour.StartBossPlaylist()` → `DungeonBossRoom.StartBossPlaylist()` (BossBehaviour.cs:127-130, DungeonBossRoom.cs:349-355).
- `DungeonFinalBossRoom : DungeonBossRoom` (DungeonFinalBossRoom.cs:4): `EnterRoom` teleports hero to room, `HeroMerchant.OnDie += OnWillDeath` (24-40); `ReplaceBoss()` swaps phase rooms, `Boss = bossPhase2`, `StartBossAwakeAnimation()` (47-64; delegates get combined twice); `OnWillDeath` → `LoadTownSceneFromDungeon()` (71-77).
- `DungeonWandererBossRoom : DungeonBossRoom` (Moonlighter.DLC.WandererDungeon/DungeonWandererBossRoom.cs:9): same teleport + `OnDie`, `HUDManager.Instance.HideAll()` (51-66), camera on `cameraObjective` (68-72), `OnWillDeath` → results panel (157-164).

### 7.C Boss health bar UI
- `HUDManager` single bar: fields `boss`, `bossMaxHealth`, `bossCurrentHealth`, `bossHealthSlider` (HUDManager.cs:39-51, 131, 161-163). `SetMaxBossHealth(float)` (470), `SetBossHealth(float)` (476-510; at ≤0 → `OnBossBattleFinished`), `OnBossBattleStart(BossBehaviour)` (482-490), `EnableBossHealthBar` (512), `DisableBossHealthBarAndName` (518), `DisableBossHealthBar` (524).
- `BossBehaviour.ShowBossGUI()` → `GUIManager.Instance.ShowDungeonWelcomeScroll(culture, 2, isBoss:true)` + `OnBossBattleStart(this)` + `SetBossHealthBar()` (97-102; called from animation, inferred). Updates via `OnHealthChanged → UpdateBossHealtBar` (13, 37-40); `OnDestroy` hides bar (55-59). `GUIManager.ShowDungeonWelcomeScroll(ItemMaster.Culture, int, bool isBoss=false, bool isMiniBoss=false, bool isCorrupted=false)` (GUIManager.cs:1022-1030).

### 7.D Boss death
- `BossBehaviour.OnDeath` (132-141): `enemyRoom.EnemyDefeated(enemy)`, unsub bar, destroy child effects, `base.OnDeath()` (destroy in 0.6 s). **Guardians and Gizmo override `OnDeath` without base**, so the object persists for the death animation. Typical (GolemKingBoss.cs:190-222; CarnivorousMutaeBossBehaviour.cs:379-416; DesertBossBehaviour.cs:743-790; TeslaBoss.cs:520-550): `SetGameSpeedForTime(bossKilledEffectTimeScale…)`, flash, `"Death"`, GUI lock, camera on boss, hero frozen, `HeroStatistics` + achievement `DEFEAT_*_BOSS`, music fade.
- Drops: Golem `enemy.DropItemsOnDeath()` in `EndedBossDieAnimation` (187); Forest in 0.125 s DelayedCall (400-409); Desert `DropDesertBossItemsOnDeath()` (792-807); Tesla none in C# (anim event, inferred).
- `DungeonBossRoom.InternalOnBossDeath()` (455-502): `bossChest` active, `IsInCombat=false`, `PlayKeyObtainAnimation()` (232-316: hero frozen ~7 s, key flies to hero, camera to chest, unlock), `GameManager.Instance.bossDefeatedCulture = roomInfo.culture`, **`DestroyMenacingInstances()` (504-520: destroys every `FindObjectsOfType<Enemy>()` except boss/breakables, all `AreaEffect`s)**, `PERFECT_*_BOSS`.
- Chest closed → `OnClosedBossChest()` → `GameManager.Instance.OpenPortalInDungeon(portalDoor.position)` (522-533; GameManager.cs:2712-2722); save flag `culturesDefeated |= 1 << culture` on return to town (GameManager.cs:2483-2497).
- Final boss: `GizmoPhase2Boss.OnDeath` (419-485) destroys all other `EnemyBehaviour`s, `TeleportTo(willExpectatorPosition)`, 10 s tween, `LoadTownSceneFromEmblemPortal()` → town `TownCinematics.PrepareEndGameAnimation()` / `PlayEndGameAnimation(Unknown)` → `EndGameCinematic` (GameManager.cs:2094-2100, 2150-2156; TownCinematics.cs:486-502).
- Wanderer: P1 `OnDeath` → DelayedCalls to `EndedBossDieAnimation` and `base.OnDeath` (WandererBossPhaseOneBehaviour.cs:511-525) → room `ReplaceBossCoroutine` (fade, hero repositioned, `StopMovement`, familiars `ForceStatic`, activate phase 2; DungeonWandererBossRoom.cs:86-118). P2 `OnDeath` (860-877) → `bossDefeatedCulture = Wanderer` → `EndDLCCinematic.Play()` (119-149).

### 7.E Cinematic machinery / hero freezing
- `EndDLCCinematic` (Moonlighter.DLC.WandererDungeon.Cinematics/EndDLCCinematic.cs:13, coroutine chain 135-151; freeze 153-162; unfreeze 179-204). `CinematicUtils` static helpers (`SpawnAgents`, `CreateNewAgent`, `MoveWillWithPathfinding` → `heroMerchantController.MoveAutomaticallyToPoint`, CinematicUtils.cs:119-333). NPCs are `DummyAgent : Agent` (DummyAgent.cs:3-7). `EndGameCinematic` (111, 174-189, `static event OnEndGameCinematicEnded` 109).
- Freeze primitives: `HeroMerchantController.DisableMoveInput()` (423), `EnableMoveInput()` (435), `AllowMovement()` (566), `ForbidMovement()` (575), `StopMovement()` (580), `TeleportTo(Vector3)` (868), `MoveAutomaticallyToPoint(...)` (2626), `DisableHeroMerchantArt()` (3872) / `EnableHeroMerchantArt(bool)` (3859); `GUIManager.PreventPanelChange()` (654) / `AllowPanelChange()` (649), `canOpenPanels` (115), `canOpenPause` (111); `HUDManager.HideAll/ShowAll`, `cinematicMode`; `GameManager.SetGameSpeedForTime(float, float, bool)` (2651); `CameraManager.MoveCamera(Transform, Action, bool)` (314), `SetFollowTarget(Transform=null)` (201), `SetArea(...)` (242).

### 7.F Boss co-op gotchas
- Intro alert area fires on any `"Player"` tag (remote puppet could trigger it); enter door requires the local hero. Each client would run `StartBossAwakeAnimation` independently unless networked.
- Boss code freezes/teleports only `HeroMerchant.Instance` (intro, death, key animation, final/Wanderer `EnterRoom` teleport, Gizmo2 `TeleportTo` 461/489). Remote players need equivalent handling.
- Boss is absent from `roomEnemies` and invincible until `EndedBossStartAnimation`; puppets must reproduce this transition.
- Animation events call boss methods even with the behaviour disabled (Unity behaviour, inferred) → puppet must patch out spawn/attack methods, not just disable the component.
- `IsBossDefeated` reads local save → clients with different progress see different boss rooms.
- Global sweeps (`DestroyMenacingInstances`, Gizmo2 death, Hug attack) use `FindObjectsOfType` → also hit puppet enemies elsewhere.
- Single HUD boss bar; Naja's bar is a sum over parts.
- Local-only side effects: achievements, `HeroStatistics`, `bossDefeatedCulture`, scene loads, `OnDie` → town/results.

---

## 8. Time, randomness, animation, and puppeting

### Timing sources (counted over `*Behaviour*.cs` + `*Boss*.cs`)
- `Time.deltaTime`: 68 uses (Update timers like `_lapse`, EnemyBehaviour.cs:211). `FixedUpdate`: only 2 files. `Invoke`/`InvokeRepeating`: 311 uses (string-named, e.g. `"StartTargetingPlayer"`, damage flash). DOTween (`DO*`, `DOTween.To`, `DOVirtual`): 342 uses, usually `SetUpdate(isIndependentUpdate:false)` (scaled time). `StartCoroutine`: 27.
- Real-time sources: `unscaledDeltaTime`/`realtimeSinceStartup` in CorruptDimensionalPoliceBehaviour, DesertBossBehaviour, GizmoBoss, GolemWarriorBehaviour, GolemWarriorCorruptedBehaviour, NormalSlimeBehaviour, Slime2_0Behaviour.
- **Global time scale**: hits call `GameManager.Instance.SetGameSpeedForTime(...)` (Weapon.cs:325, 349, 385; kill pause EnemyBehaviour.cs:577-580; damaged HeroMerchant.cs:1105-1108); hero death calls `GameManager.Instance.PauseGame()` (HeroMerchantController.cs:3444). These must be made local-visual-only or disabled in co-op, otherwise one player's hit-stop desyncs simulation timing.
- Animator: `AnimatorStateEvents : StateMachineBehaviour` (AnimatorStateEvents.cs:4-35) exposes `UnityEvent onStateEnter/onStateExit/onStateUpdate` wired in assets (inferred: drive some behaviour methods). Animation clip events call public behaviour methods (inferred; many public methods without C# callers).

### Randomness
- 358 uses of `UnityEngine.Random` / `GetRandom()` in behaviour + boss files (attack choice, positions, deviation, repath jitter EnemyBehaviour.cs:247, hole-death spin 184). Also loot (ItemDrop.cs:30-32), weapon effect procs (Weapon.cs:304-312), champion selection (DungeonRoom.cs:794-802), egg drop (Enemy.cs:290). Nothing is seeded per enemy → **only the owner may run AI; puppets must not**.

### Animation driving
- Behaviours drive animation through Animator parameters only: 813 `SetTrigger/SetBool/SetFloat/SetInteger` calls; **0 direct `Animator.Play(`** calls in behaviour/boss files. Common params: `"DistanceX"`, `"DistanceY"` (EnemyBehaviour.cs:322-328), `"Attacking"` (206, 313), `"NotDamagedWhenHit"` (555), boss-specific (`"curentMovemment"`, `"Death"`, `"Teleport"`, `"Shoot"`…).
- Facing is encoded in Animator floats `DistanceX/DistanceY` (not SpriteRenderer.flipX), per `LookToPosition` (316-330).
- Hit flash is shader material params `_FlashAmount` and `_Color` on `enemySprite` + `aditionalSpriteRenderer/MeshRenderer` (486-534). Death look: DOTween to `_FlashAmount=1`, colour grey (607-627).

### Freezing an enemy as a puppet (recommendation; partly inferred)
- Disabling `EnemyBehaviour` (`enabled=false`) stops `Update` → `ChooseAndApplyAction`, hole check, and `MoveEnemy` (173-189). It does **not** stop: pending `Invoke`s (`StartTargetingPlayer`, `ShootAnimation`…), running DOTweens/coroutines, animation events and `AnimatorStateEvents` calling public methods (inferred: Unity sends these to disabled components), `PolyNavAgent` continuing to a set destination, Rigidbody2D physics, `EnemyAttackCollisionDetection` triggers (which call `AttackHit` and damage the local hero), and `EnemyStats` damage (`DealDamageToEnemy` returns early if `!enemyBehaviour.isActiveAndEnabled`, Enemy.cs:330 — so a disabled puppet is unhittable locally; route hits manually).
- Suggested puppet setup: keep `Enemy`/`EnemyStats`/Animator/SpriteRenderer enabled; never call `StartTargetingPlayer` on puppets (prefix `EnemyBehaviour.StartTargetingPlayer` / `ChooseAndApplyAction` / `Update` to skip for non-owned); `navAgent.enabled = false` (or `Stop()`), `Rigidbody2D.isKinematic = true`; Harmony-prefix the per-class attack/spawn methods (or blanket-skip `AttackHit` and projectile spawners) on puppets; set Animator params received from the owner (or `Play(stateHash, 0, normalizedTime)` on desync).
- Minimal mirrored state per enemy: position (2D), Animator layer-0 `fullPathHash` + `normalizedTime` (or the parameter set `DistanceX/DistanceY` + triggers as events), `enabled`/active, `CurrentHealth` + `maxHealth`, `isAlive`/death event, `Invencible`, hit-flash event (call `PlayDamagedEffect()` locally), `isChampion` (tint), `localScale` (stat modificators tween scale, EnemyStats.cs:320, 336), `Animator.speed` (confusion sets 0), status-effect events (create the `MoonlighterEffect` visual locally), and for bosses the current attack/state enum and HUD bar values.

---

## 9. Familiars, companions and hero-spawned objects

- `FamiliarBase` (FamiliarBase.cs, enum `FamiliarBase.Familiars`); targets from `CurrentDungeonRoom.roomEnemies` (226, 297, 413-431 raycast visibility); damage `protected void DealDamageToEnemy(Enemy enemy)` → `enemy.DealDamageToEnemy(totalDamage, null, EnemyDamageReceivedType.Familiar)` (853-867).
- Subclasses: `BabySlimeFamiliarBehaviour` (slime companion), `BladeTreeSproutFamiliar`, `DancerPuppetFamiliar`, `FlyingRepairGolemFamiliar`, `GasSuitFamiliar`, `GolemTurretFamiliar`, `GraafGeneratorFamiliar`, `MotherGolemFamiliar`, `RechargerFamiliar`, `SpikeChainFamiliar`, `WindTreeSproutFamiliar`, `WoodMimicFamiliar` (each `X.cs:3-6`). Related: `MotherGolemFamiliarElemental`, `SpikedChainFamiliarBall`/`SpikeChainedBallFamiliar`, `FamiliarElementalAttackCollisionDetection`, `GasAreaFamiliarEffect`, movement `IFamiliarMovement`, `FlyingFamiliarMovement`, `PolyNavFamiliarMovement`, `FollowPlayer`. `SlimePet` is an `EnemyBehaviour` subclass (town/shop pet, inferred).
- `FamiliarsManager` static API: `SpawnEquippedFamiliar()` (457), `AddFamiliar(Familiars)` (494), `DespawnRequestedFamiliar` (533), `ActivateFamiliars(bool)` (577), `TeleportFamiliarsToWill()` (590), `TeleportFamiliarsTo(Vector3,…)` (603), `GetFamiliarOfType` (670), `GetEggItem(string, out SpawnEggInfo)` (171), hatching helpers (380-432). `HeroMerchant.familiars` list (HeroMerchant.cs:33).
- Hero-spawned: `HeroMerchantProjectile`, `BowArrow`, `ExplosiveArrow`, `TrickWeaponProjectile`, `HeroRingAmuletEffect_Projectile` (damage calls in 3a), weapon effects `Moonlighter.Weapons/*WeaponEffect` (BoneSword, CompositeBow, ExplosiveBow, GhostGloves, MechanicBigSword, MorningStar, ParasiteSpear, SporeSword, StaffSpear, ThiefGloves, TrickWeaponEffect), `TrickGasAreaEffect`, `HealingArea`. No bombs found.

---

## 10. Difficulty and stat scaling

All in `EnemyStats.Init(string name)` (EnemyStats.cs:129-228), in this order:
1. Base row from `EnemyStatManager.GetModificator(name, …)` (132).
2. Floor scaling if `isModifiedByFloor`: level = `DungeonManager.Instance.level`; factor `Constants floorLevelingValue` (night: `floorLevelingValueAtNight`; Wanderer: `floorLevelingValueForWanderer` + `floorLevelingStrengthPowForWanderer`); HP `+= f*level*HP`, strength `+= f*max(0, level^0.6-0.5)*str`; champion `championHealthPercent/championStrengthPercent` (137-165).
3. **Difficulty**: `Constants.GetFloatArray("difficultyMod")[OptionsManager.Get("GAME","Difficulty",-1)]` multiplies maxHealth and strength (166-172).
4. NG+ factors `ngpEnemies*Factor` / `ngpPostDLCEnemies*Factor` for health/strength/defense/speed (174-193).
5. Cross-culture auto-scale `enemyAutoScaleHealth/Strength[dungeonCulture][enemyCulture]` (194-214).
6. `CurrentHealth = maxHealth` (227).
- Runtime modifiers: `AddModificator(string key, EnemyStatModificator)` / `RemoveModificator` (283-337) (buffers, inferred).
- Co-op scaling hook: **postfix on `EnemyStats.Init`** and multiply via reflection on private `_currentStats` (`EnemyStatModificator`, line 23) `.maxHealth` (then reset `_currentHealth`), or pre-multiply the `difficultyMod` value. Note `TransformToChampion()` re-runs `Init` (230-234) and the postfix would apply twice only if it compounds — it rebuilds from `_baseStats`, so a postfix multiply is safe. Boss HP is also set in `EnemyStats.Init` (bosses use the same path), and the HUD bar reads `maxHealth` in `SetBossHealthBar` (BossBehaviour.cs:32-35).
