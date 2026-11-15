using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MoonlighterTogether.Testing
{
    public static class TestGod
    {
        public static bool On;
    }

    [HarmonyLib.HarmonyPatch(typeof(HeroMerchant), nameof(HeroMerchant.DealDamageToPlayer))]
    static class TestGodPatch
    {
        static bool Prefix() => !TestGod.On;
    }

    public static partial class BridgeCommands
    {
        static partial void RegisterGame()
        {
            Bridge.Register("state", a => State());
            Bridge.Register("startgame", a => StartGame((int?)a["slot"] ?? 0, (bool?)a["fresh"] ?? false, (bool?)a["skipTutorial"] ?? true));
            // Test invulnerability (the game's own god mode wears off after the first hit).
            Bridge.Register("godmode", a => { TestGod.On = (bool?)a["on"] ?? true; return TestGod.On; });
            Bridge.Register("downed", a => Sync.Downed.Snapshot());
            Bridge.Register("shop", a => Sync.Shop.Snapshot());
            // At the till: the host rings the customer up; a guest's press goes to the host.
            Bridge.Register("till", a =>
            {
                var cash = ShopManager.Instance.references.shopCash;
                if (Sync.Shop.GuestHere) { cash.TryToSellItem(); return true; }
                return Sync.Shop.WorkTill();
            });
            Bridge.Register("openshop", a => { ShopManager.Instance.references.shopEntranceDoor.OpenShop(); return true; });
            Bridge.Register("closeshop", a => { ShopManager.Instance.references.shopEntranceDoor.CloseShop(); return true; });
            // Put items on the shelves (as the showcase menu does): one stack per slot, at the given price.
            Bridge.Register("stock", a =>
            {
                var refs = ShopManager.Instance.references;
                string item = (string)a["item"] ?? "Rich Jelly"; int price = (int?)a["price"] ?? 0; int qty = (int?)a["qty"] ?? 1; int count = (int?)a["slots"] ?? 4;
                var master = ItemDatabase.GetItemByName(item) ?? throw new System.Exception("no item " + item);
                if (price <= 0) price = ItemPriceManager.Instance.GetCorrectPriceWithPopularity(master);   // the fair price
                int placed = 0;
                foreach (var sc in refs.shopShowcases)
                    for (int i = 0; i < sc.itemPositions.Count && placed < count; i++)
                        if (sc.itemPositions[i].itemStack == null)
                        {
                            var st = ItemStack.Create(master, qty); st.unitSellingPrice = price;
                            sc.SetItemInPosition(st, i); placed++;
                        }
                return placed;
            });
            // On the end-of-run screen: "return to the village" (B), as a player would press it.
            Bridge.Register("results", a =>
            {
                var p = GUIManager.Instance.currentInventory as DungeonResultsPanel;
                if (p == null) return false;
                p.OnBButtonPressed();
                return true;
            });
            Bridge.Register("revive", a => Sync.Downed.ReviveNearest());
            Bridge.Register("teleport", a => { var h = HeroMerchant.Instance; h.heroMerchantController.TeleportTo(new Vector3((float)a["x"], (float)a["y"], h.transform.position.z)); return true; });
            Bridge.Register("dlc", a =>
            {
                var list = new System.Collections.Generic.List<object>();
                foreach (var cat in new[] { "DUNGEON", "ITEMS", "Default", "TOWN", "SHOP" })
                    foreach (var h in DigitalSunGames.Platforms.Platform.DLC.GetByCategory(cat))
                        list.Add(new { cat, id = h.info?.internalId, platform = h.info?.platform.ToString(), pid = h.info?.platformId, owned = h.isOwned, version = h.isVersionValid, valid = h.IsValid, bundle = h.bundle != null ? h.bundle.name : null });
                return list;
            });
            Bridge.Register("save", a => { GameManager.Instance.SerializeGameSlot(); return true; });
            Bridge.Register("addgold", a => { HeroMerchant.Instance.addGold((int)a["amount"]); return HeroMerchant.Instance.GetCurrentGold(); });
        }

        public static object State()
        {
            var gm = GameManager.Instance;
            var hero = HeroMerchant.Instance;
            // Will exists a little before his stats and the scene are ready: report "loading" until then.
            if (hero != null && (hero.heroMerchantStats == null || hero.heroMerchantStats.currentHealth == null || hero.heroMerchantStats.gold == null)) hero = null;
            var dm = DungeonManager.Instance;
            string zone = "menu";
            if (gm != null && hero != null)
            {
                if (gm.IsDungeonSceneLoaded()) zone = "dungeon";
                else if (gm.IsTownSceneLoaded()) zone = ShopManager.Instance != null && ShopManager.Instance.isWillInShop ? "shop" : "town";
            }
            var gui = GUIManager.Instance;
            return new
            {
                zone,
                loading = gm != null && !gm.NotLoadingScene,
                paused = Time.timeScale == 0f,
                panel = gui != null && gui.currentInventory != null ? gui.currentInventory.GetType().Name : null,
                hero = hero == null ? null : new
                {
                    x = hero.transform.position.x,
                    y = hero.transform.position.y,
                    hp = (float)hero.heroMerchantStats.currentHealth.Value,
                    maxHp = (float)hero.heroMerchantStats.fullHealth.Value,
                    dead = (bool)hero.heroMerchantStats.isDead,
                    gold = hero.GetCurrentGold(),
                    mode = hero.currentMode.ToString(),
                    floor = hero._currentDungeonLevel,
                    canMove = hero.heroMerchantController != null && hero.heroMerchantController._canMove,
                },
                dungeon = dm == null || gm == null || !gm.IsDungeonSceneLoaded() ? null : new
                {
                    culture = dm.culture.ToString(),
                    level = dm.level,
                    room = dm.currentDungeonRoom == null ? null : new { x = dm.currentDungeonRoom.x, y = dm.currentDungeonRoom.y, enemies = dm.currentDungeonRoom.roomEnemies.Count },
                    rooms = dm.dungeonRooms?.Count ?? 0,
                },
                tutorial = gm?.currentGameSlot?.playedTutorial ?? -1,
            };
        }

        /// From the main menu (or the "press any key" / news screens) straight into a save slot.
        /// fresh: wipe the slot first; skipTutorial: mark the tutorial as done (it is single-player content).
        private static IEnumerator StartGame(int slot, bool fresh, bool skipTutorial)
        {
            var gm = GameManager.Instance;
            // Wait for the main menu itself: before it is up, the game may still (re)load the save file and
            // would replace the slot we are about to set up.
            float limit = Time.unscaledTime + 60f;
            while (Time.unscaledTime < limit && (gm == null || gm.gameSaveData == null || gm._loadingGameSlot || GUIManager.Instance == null
                   || !(GUIManager.Instance.currentInventory is MainMenuPanel))) { yield return null; gm = GameManager.Instance; }
            yield return new WaitForSecondsRealtime(0.5f);
            var gui = GUIManager.Instance;
            if (fresh) gm.ResetSlot(slot);
            var gs = gm.gameSaveData.gameSlots[slot];
            if (fresh)
            {
                gs.gameType = GameSlot.GameType.Normal;
                gs.timeLastSave = System.DateTime.UtcNow;
            }
            if (skipTutorial && gs.playedTutorial < 4)
            {
                // What the end of the last tutorial scene does (TutorialScene3.EndScene).
                gs.playedTutorial = 4;
                gs.townboardSaveInfo.UnlockBlacksmith();
                gs.townboardSaveInfo.UnlockWitch();
                gm.bossDefeatedCulture = ItemMaster.Culture.Merchant;
            }
            // What the title screen does when a key is pressed: set up the DLC controllers for this game.
            gm.CheckDLCIntegrity();
            gui.AllowPanelChange();
            gui.DisableCurrentPanel();
            gui.currentInventory = null;
            gui.dungeonLoadingPanel.showHUDSOnDisable = false;
            gui.GoToDungeonLoadingPanel(toDungeon: false);
            gui.dungeonLoadingPanel.showHUDSOnDisable = true;
            gm.LoadSlot(gs);
            float until = Time.unscaledTime + 90f;
            // In town, loaded, and the wake-up-in-bed animation over (it gives control back at its end).
            while (Time.unscaledTime < until && !(HeroMerchant.Instance != null && gm.IsTownSceneLoaded() && gm.NotLoadingScene
                   && HeroMerchant.Instance.heroMerchantController._canMove && !gui.Fading)) yield return null;
            yield return new Bridge.Result { Value = State() };
        }
    }
}
