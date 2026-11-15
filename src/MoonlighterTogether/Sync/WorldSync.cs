using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using FullSerializer;
using HarmonyLib;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// The host's world, each player's own character.
    ///
    /// On joining, the guest gets the host's save slot (shop, town, story, prices, day) and plays in it with
    /// the character from one of its OWN slots (bag, equipment, gold, wishlist, familiar). While the session
    /// lasts, the guest's saves write only that character back into its own slot: the guest's own world is
    /// never overwritten, and whatever it earns at the host's place comes home with it.
    public static class WorldSync
    {
        public const Msg WorldMsg = (Msg)20;
        public const Msg WorldAsk = (Msg)21;

        /// Guest: index of its own slot whose character it brings (and saves back to).
        public static int OwnSlotIndex = 0;
        /// Guest: its own slot, as it is on disk (gameSaveData.gameSlots[OwnSlotIndex] while in session).
        public static GameSlot OwnSlot;
        /// Guest: the slot actually being played (host world + own character).
        public static GameSlot Merged;
        public static int WorldsReceived, WorldsSent;
        public static string LastRefusal;
        private static byte[] _pending;

        public static void Init()
        {
            Session.On(WorldMsg, Relay.None, OnWorld);
            Session.On(WorldAsk, Relay.None, OnAsk);
            // The guest asks once it is in (and again only if it never got one), so a world is sent exactly once.
            Session.Started += back =>
            {
                if (!Session.IsGuest) return;
                if (!back) Session.SendToHost(WorldAsk, new W());
                else if (GuestInHostWorld) Floor.SendReady();   // catch up on trips made while we were away
            };
            Session.Ended += why => Plugin.Instance.StartCoroutine(LeaveHostWorld());
        }

        public static bool GuestInHostWorld => Session.IsGuest && Merged != null && GameManager.Instance != null && GameManager.Instance.currentGameSlot == Merged;

        // ---------------- host ----------------

        private static void OnAsk(int sender, R r) { if (Session.IsHost) SendWorldTo(sender); }

        public static void SendWorldTo(int playerId)
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.currentGameSlot == null || HeroMerchant.Instance == null)
            {
                Plugin.Log.LogInfo($"[world] player {playerId} asked for the world but the host is not in a game yet");
                return;
            }
            // Bring the slot up to date with the live game first (gold, day, shop) without touching the disk.
            var slot = gm.currentGameSlot;
            slot.willGold = HeroMerchant.Instance.GetCurrentGold();
            FsJSONManager.Serialize(slot, out fsData data);
            var bytes = Encoding.UTF8.GetBytes(fsJsonPrinter.CompressedJson(data));
            Session.SendTo(playerId, WorldMsg, new W().Bytes(bytes));
            WorldsSent++;
            Plugin.Log.LogInfo($"[world] sent the world to player {playerId}: {bytes.Length} bytes");
        }

        // ---------------- guest ----------------

        private static void OnWorld(int sender, R r)
        {
            if (Session.IsHost) return;
            var bytes = r.Bytes(8 * 1024 * 1024);
            WorldsReceived++;
            Plugin.Log.LogInfo($"[world] got the host's world: {bytes.Length} bytes");
            if (_entering || GuestInHostWorld) { Plugin.Log.LogInfo("[world] already in (or entering) the host's world: ignored"); return; }
            _pending = bytes;
            Plugin.Instance.StartCoroutine(EnterHostWorld());
        }

        private static bool _entering;

        private static IEnumerator EnterHostWorld()
        {
            _entering = true;
            try
            {
                var e = EnterHostWorldSteps();
                while (e.MoveNext()) yield return e.Current;
            }
            finally { _entering = false; }
        }

        private static IEnumerator EnterHostWorldSteps()
        {
            var gm = GameManager.Instance;
            while (gm == null || gm.gameSaveData == null || gm._loadingGameSlot) { yield return null; gm = GameManager.Instance; }

            var bytes = _pending; _pending = null;
            if (bytes == null) yield break;
            if (!SaveGuard.IsSafe(bytes, out string why)) { LastRefusal = why; Plugin.Log.LogWarning("[world] refused the host's world: " + why); yield break; }

            GameSlot host = null;
            FsJSONManager.Deserialize(Encoding.UTF8.GetString(bytes), ref host, false);
            if (host == null) { LastRefusal = "unreadable"; Plugin.Log.LogWarning("[world] could not read the host's world"); yield break; }
            host.FixGameSlot();

            // In a game already: back to the main menu first (that also re-reads our own save from disk).
            if (HeroMerchant.Instance != null)
            {
                gm.ReturnToMainMenu();
                float until = Time.unscaledTime + 60f;
                while (Time.unscaledTime < until && (HeroMerchant.Instance != null || !gm.NotLoadingScene || gm._loadingGameSlot)) yield return null;
                yield return new WaitForSecondsRealtime(1f);
            }

            OwnSlot = gm.gameSaveData.gameSlots[OwnSlotIndex];
            if (OwnSlot == null || !OwnSlot.IsValid || OwnSlot.playedTutorial < 4)
            {
                // No character there yet: a new one with the game's starting kit.
                gm.ResetSlot(OwnSlotIndex);
                OwnSlot = gm.gameSaveData.gameSlots[OwnSlotIndex];
                OwnSlot.gameType = GameSlot.GameType.Normal;
                OwnSlot.timeLastSave = DateTime.UtcNow;
                OwnSlot.playedTutorial = 4;
                OwnSlot.townboardSaveInfo.UnlockBlacksmith();
                OwnSlot.townboardSaveInfo.UnlockWitch();
                Plugin.Log.LogInfo($"[world] slot {OwnSlotIndex} had no character: starting a new one");
            }
            CopyCharacter(OwnSlot, host);
            Merged = host;
            Plugin.Log.LogInfo($"[world] entering the host's world with the character of slot {OwnSlotIndex} ({host.willGold} gold, {host.willInventory.Count} bag items)");

            var gui = GUIManager.Instance;
            gm.CheckDLCIntegrity();   // as the title screen does; joining may skip it
            gui.AllowPanelChange();
            gui.DisableCurrentPanel();
            gui.currentInventory = null;
            gui.dungeonLoadingPanel.showHUDSOnDisable = false;
            gui.GoToDungeonLoadingPanel(toDungeon: false);
            gui.dungeonLoadingPanel.showHUDSOnDisable = true;
            gm.LoadSlot(Merged);
            float readyUntil = Time.unscaledTime + 120f;
            while (Time.unscaledTime < readyUntil && !(HeroMerchant.Instance != null && gm.IsTownSceneLoaded() && gm.NotLoadingScene && HeroMerchant.Instance.heroMerchantController._canMove)) yield return null;
            Floor.SendReady();
        }

        /// The session is over: save our character into our own slot, then back to the main menu (we must not
        /// keep playing a copy of someone else's world that nothing will save).
        private static IEnumerator LeaveHostWorld()
        {
            if (Merged == null) yield break;
            var gm = GameManager.Instance;
            if (GuestInHostWorld && gm != null)
            {
                try { gm.SerializeGameSlot(); } catch (Exception e) { Plugin.Log.LogWarning("[world] saving the character on leave: " + e.Message); }
                Plugin.Log.LogInfo("[world] session over: character saved, back to the main menu");
                while (!gm.NotLoadingScene) yield return null;
                gm.ReturnToMainMenu();
            }
            Merged = null; OwnSlot = null;
        }

        /// The parts of a save that belong to the player, not to the world.
        public static void CopyCharacter(GameSlot from, GameSlot to)
        {
            to.willInventory = Clone(from.willInventory);
            to.willEquippedItems = Clone(from.willEquippedItems);
            to.equippedWeaponSet = from.equippedWeaponSet;
            to.willGold = from.willGold;
            to.willWishlistedRecipes = from.willWishlistedRecipes;
            to.equippedFamiliar = from.equippedFamiliar;
        }

        private static List<ItemSaveInfo> Clone(List<ItemSaveInfo> list)
        {
            if (list == null) return new List<ItemSaveInfo>();
            FsJSONManager.Serialize(list, out fsData data);
            List<ItemSaveInfo> copy = null;
            FsJSONManager.Deserialize(fsJsonPrinter.CompressedJson(data), ref copy, false);
            return copy ?? new List<ItemSaveInfo>();
        }

        /// Guest saving while in the host's world: only the character goes into our own slot.
        public static void BeforeSave()
        {
            if (!GuestInHostWorld || OwnSlot == null) return;
            var gm = GameManager.Instance;
            // The live bag, equipment and gold first (the game's own save handler for them).
            var inv = HeroMerchant.Instance != null ? HeroMerchant.Instance.heroMerchantInventory : null;
            if (inv != null) inv.GameManager_OnSave(Merged);
            CopyCharacter(Merged, OwnSlot);
            OwnSlot.timeLastSave = DateTime.Now;
            gm.gameSaveData.gameSlots[OwnSlotIndex] = OwnSlot;
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.SerializeGameSlot))]
    static class GuestSavesOwnCharacterOnly
    {
        // The game serialises gameSaveData (all slots), not currentGameSlot: with our own slot in place and
        // only its character refreshed, the host's world never reaches our disk.
        static void Prefix() => WorldSync.BeforeSave();
    }

    /// A received save is only ever the game's own data: FullSerializer may create any type named in a
    /// "$type" field, so every such name must be a plain data type of the game. Size is capped too.
    public static class SaveGuard
    {
        public static bool IsSafe(byte[] bytes, out string why)
        {
            why = null;
            if (bytes.Length > 8 * 1024 * 1024) { why = "too large"; return false; }
            // The game's JSON parser recurses once per nesting level with no limit: refuse deep nesting before
            // parsing, or a crafted save would overflow the stack (which cannot be caught).
            int depth = 0; bool inStr = false, esc = false;
            foreach (byte b in bytes)
            {
                if (inStr) { if (esc) esc = false; else if (b == (byte)'\\') esc = true; else if (b == (byte)'"') inStr = false; continue; }
                if (b == (byte)'"') inStr = true;
                else if (b == (byte)'[' || b == (byte)'{') { if (++depth > 64) { why = "nested too deep"; return false; } }
                else if (b == (byte)']' || b == (byte)'}') depth--;
            }
            fsData root;
            var res = fsJsonParser.Parse(Encoding.UTF8.GetString(bytes), out root);
            if (res.Failed) { why = "not JSON: " + res.FormattedMessages; return false; }
            return Walk(root, 0, ref why);
        }

        private static readonly System.Reflection.Assembly GameAssembly = typeof(GameSlot).Assembly;

        private static bool Walk(fsData d, int depth, ref string why)
        {
            if (depth > 64) { why = "nested too deep"; return false; }
            if (d.IsDictionary)
            {
                foreach (var kv in d.AsDictionary)
                {
                    if (kv.Key == "$type")
                    {
                        if (!kv.Value.IsString || !Allowed(kv.Value.AsString)) { why = "unexpected type " + (kv.Value.IsString ? kv.Value.AsString : "?"); return false; }
                    }
                    else if (!Walk(kv.Value, depth + 1, ref why)) return false;
                }
            }
            else if (d.IsList)
            {
                foreach (var x in d.AsList) if (!Walk(x, depth + 1, ref why)) return false;
            }
            return true;
        }

        private static bool Allowed(string name)
        {
            var t = GameAssembly.GetType(name, false);
            if (t == null) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t) || typeof(Delegate).IsAssignableFrom(t)) return false;
            return true;
        }
    }
}
