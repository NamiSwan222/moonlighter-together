using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MoonlighterTogether.Net;
using UnityEngine;

namespace MoonlighterTogether.Sync
{
    /// The shop and the day are the host's.
    ///
    /// The host's copy runs the shop day as the game does (customers, haggling, thieves, the till, the clock).
    /// A guest's copy spawns no customers of its own; it draws the host's customers from the sprites they
    /// show (like other players' Wills), mirrors the host's shelves, and follows the host's shop door and
    /// day/night. A guest who works the shop door or sleeps in the bed asks the host to do it.
    public static class Shop
    {
        public const Msg StateMsg = (Msg)40;
        public const Msg AskMsg = (Msg)41;
        public const Msg CustomersMsg = (Msg)42;
        public const Msg ShelvesMsg = (Msg)43;

        public enum Ask : byte { OpenShop = 1, CloseShop = 2, Sleep = 3, Till = 4 }

        public static bool ApplyingRemote;
        public static int StatesSent, StatesApplied, CustomerFramesSent, CustomerFramesApplied, ShelfUpdatesSent, ShelfUpdatesApplied, AsksSent;
        private static float _nextCustomers, _nextShelves, _nextState;
        private static string _lastShelves = "", _lastState = "";
        private static readonly W Out = new W();
        private static readonly Dictionary<int, CustomerPuppet> Customers = new Dictionary<int, CustomerPuppet>();

        public static bool GuestHere => WorldSync.GuestInHostWorld;

        public static void Init()
        {
            Session.On(StateMsg, Relay.None, OnState);
            Session.On(AskMsg, Relay.None, OnAsk);
            Session.On(CustomersMsg, Relay.None, OnCustomers, Channel.State);
            Session.On(ShelvesMsg, Relay.None, OnShelves);
            Session.PlayerJoined += (p, back) => { _lastState = ""; _lastShelves = ""; };
            Session.Ended += why => ClearCustomers();
        }

        // ================================================================== host

        public static void Tick()
        {
            if (!Session.Active) return;
            var gm = GameManager.Instance;
            bool inTown = gm != null && HeroMerchant.Instance != null && gm.IsTownSceneLoaded() && gm.NotLoadingScene && ShopManager.Instance != null;
            if (!inTown) { if (!Session.IsHost) ClearCustomers(); return; }
            if (!Session.IsHost) { foreach (var c in Customers.Values) if (c != null) c.Smooth(); return; }
            float now = Time.unscaledTime;
            bool anyoneInTown = Presence.Remotes.Values.Any(r => r.Zone == "town");
            if (now >= _nextState) { _nextState = now + 0.5f; SendState(false); }
            if (!anyoneInTown) return;
            if (now >= _nextCustomers) { _nextCustomers = now + 1f / 15f; SendCustomers(); }
            if (now >= _nextShelves) { _nextShelves = now + 0.5f; SendShelves(false); }
        }

        private static void SendState(bool force)
        {
            var sm = ShopManager.Instance; var tm = TimeManagerV2.Instance; var slot = GameManager.Instance.currentGameSlot;
            if (sm == null || tm == null || slot == null) return;
            string key = $"{(sm.IsShopClosed ? 0 : 1)}|{(int)tm.CurrrentDayTime}|{slot.daysPassed}";
            if (!force && key == _lastState) return;
            _lastState = key;
            Session.Send(StateMsg, Out.Reset().Bool(!sm.IsShopClosed).U8((byte)tm.CurrrentDayTime).I32(slot.daysPassed));
            StatesSent++;
        }

        private static readonly List<SpriteRenderer> Srs = new List<SpriteRenderer>();

        private static void SendCustomers()
        {
            var gen = ShopManager.Instance.visitorGenerator;
            var list = gen != null ? gen.generatedGameObjects.Where(g => g != null && g.activeInHierarchy).ToList() : new List<GameObject>();
            Out.Reset().I32(list.Count);
            foreach (var go in list)
            {
                go.GetComponentsInChildren(false, Srs);
                var vis = Srs.Where(s => s.enabled && s.sprite != null && s.color.a > 0.01f).Take(16).ToList();
                Out.I32(go.GetInstanceID()).V2(go.transform.position).U8((byte)vis.Count);
                foreach (var sr in vis)
                {
                    var c = (Color32)sr.color;
                    Out.Str(sr.sprite.name).V2((Vector2)(sr.transform.position - go.transform.position))
                       .U8((byte)((sr.flipX ? 1 : 0) | (sr.flipY ? 2 : 0) | (sr.transform.lossyScale.x < 0 ? 4 : 0)))
                       .I32(sr.sortingLayerID).I16((short)Mathf.Clamp(sr.sortingOrder, short.MinValue, short.MaxValue))
                       .U8(c.r).U8(c.g).U8(c.b).U8(c.a);
                }
            }
            Session.Send(CustomersMsg, Out, Channel.State);
            CustomerFramesSent++;
        }

        /// Every shelf slot: item, quantity, price. Sent when anything changed (an item sold, a price set).
        private static void SendShelves(bool force)
        {
            var refs = ShopManager.Instance.references;
            if (refs == null) return;
            var w = new W();
            var sb = new System.Text.StringBuilder();
            int n = 0;
            var slots = new List<ShowcaseSlot>();
            foreach (var sc in refs.shopShowcases) if (sc != null) slots.AddRange(sc.itemPositions);
            w.I32(slots.Count);
            foreach (var s in slots)
            {
                var it = s.itemStack;
                string name = it != null && it.master != null ? it.master.name : null;
                int q = it != null ? it.Quantity : 0, price = it != null ? it.unitSellingPrice : 0;
                w.Str(name).I32(q).I32(price);
                sb.Append(name).Append(':').Append(q).Append(':').Append(price).Append(';');
                n++;
            }
            string key = sb.ToString();
            if (!force && key == _lastShelves) return;
            _lastShelves = key;
            Session.Send(ShelvesMsg, w);
            ShelfUpdatesSent++;
        }

        private static void OnAsk(int sender, R r)
        {
            if (!Session.IsHost) return;
            var ask = (Ask)r.U8();
            var sm = ShopManager.Instance;
            if (sm == null) return;
            Plugin.Log.LogInfo($"[shop] player {sender} asks: {ask}");
            switch (ask)
            {
                case Ask.OpenShop: if (sm.IsShopClosed && sm.canOpenShop) sm.references.shopEntranceDoor.OpenShop(); break;
                case Ask.CloseShop: if (!sm.IsShopClosed) sm.references.shopEntranceDoor.CloseShop(); break;
                case Ask.Sleep: if (sm.IsShopClosed) TimeManagerV2.Instance.SwapDayTime(); break;
                case Ask.Till: WorkTill(); break;
            }
        }

        // ================================================================== guest

        private static void OnState(int sender, R r)
        {
            if (Session.IsHost) return;
            bool open = r.Bool(); var dt = (DayTime)r.U8(); int days = r.I32();
            if (!GuestHere) return;
            var sm = ShopManager.Instance; var tm = TimeManagerV2.Instance; var slot = GameManager.Instance.currentGameSlot;
            if (sm == null || tm == null) return;
            ApplyingRemote = true;
            try
            {
                if (open && sm.IsShopClosed)
                {
                    if (!sm.canOpenShop && tm.CurrrentDayTime != DayTime.Morning) tm.ChangeToMorning();
                    sm.canOpenShop = true;
                    sm.references.shopEntranceDoor.OpenShop();
                    sm.PreventShopFromCloseAfterTime();   // the host's clock ends the day, not ours
                }
                else if (!open && !sm.IsShopClosed)
                {
                    sm.canOpenShop = true;
                    sm.references.shopEntranceDoor.CloseShop();
                }
                if (tm.CurrrentDayTime != dt)
                {
                    if (dt == DayTime.Morning) tm.ChangeToMorning(); else tm.ChangeToAfternoon();
                }
                if (slot != null) slot.daysPassed = days;
            }
            finally { ApplyingRemote = false; }
            StatesApplied++;
        }

        private static readonly HashSet<int> Seen = new HashSet<int>();

        private static void OnCustomers(int sender, R r)
        {
            if (Session.IsHost) return;
            int n = r.Count(256);
            Seen.Clear();
            bool show = GuestHere && Presence.MyZone == "town";
            for (int i = 0; i < n; i++)
            {
                int id = r.I32(); Vector2 pos = r.V2(); int parts = r.U8();
                var list = new List<Presence.Part>(parts);
                for (int k = 0; k < parts; k++)
                {
                    var p = new Presence.Part { Sprite = r.Str(), Offset = r.V2() };
                    int flags = r.U8();
                    p.FlipX = (flags & 1) != 0; p.FlipY = (flags & 2) != 0; if ((flags & 4) != 0) p.FlipX = !p.FlipX;
                    p.Layer = r.I32(); p.Order = r.I16();
                    p.Color = new Color32(r.U8(), r.U8(), r.U8(), r.U8());
                    list.Add(p);
                }
                if (!show) continue;
                Seen.Add(id);
                if (!Customers.TryGetValue(id, out var cp) || cp == null)
                {
                    cp = new GameObject("MT Customer " + id).AddComponent<CustomerPuppet>();
                    cp.Snap(pos);
                    Customers[id] = cp;
                }
                cp.Apply(pos, list);
            }
            foreach (var id in Customers.Keys.Where(k => !Seen.Contains(k)).ToList())
            {
                if (Customers[id] != null) Object.Destroy(Customers[id].gameObject);
                Customers.Remove(id);
            }
            CustomerFramesApplied++;
        }

        private static void OnShelves(int sender, R r)
        {
            if (Session.IsHost || !GuestHere || ShopManager.Instance == null) return;
            var refs = ShopManager.Instance.references;
            var slots = new List<(Showcase sc, int idx)>();
            foreach (var sc in refs.shopShowcases) if (sc != null) for (int i = 0; i < sc.itemPositions.Count; i++) slots.Add((sc, i));
            int n = r.Count(1024);
            for (int i = 0; i < n; i++)
            {
                string name = r.Str(); int q = r.I32(); int price = r.I32();
                if (i >= slots.Count) continue;
                var (sc, idx) = slots[i];
                var cur = sc.itemPositions[idx].itemStack;
                string curName = cur != null && cur.master != null ? cur.master.name : null;
                if (curName == name && (cur == null || (cur.Quantity == q && cur.unitSellingPrice == price))) continue;
                if (cur != null) { sc.SetItemInPosition(null, idx); Object.Destroy(cur.gameObject); }
                if (name == null) continue;
                var master = ItemDatabase.GetItemByName(name);
                if (master == null) continue;
                var stack = ItemStack.Create(master, Mathf.Max(1, q));
                stack.unitSellingPrice = price;
                sc.SetItemInPosition(stack, idx);
            }
            ShelfUpdatesApplied++;
        }

        /// Ring up the customer waiting at the till (what pressing the button at the till does). The money
        /// goes to the shop's owner, the host.
        public static bool WorkTill()
        {
            var cash = ShopManager.Instance?.references?.shopCash;
            if (cash == null || cash.visitorBuying == null) return false;
            ApplyingRemote = true;
            try { cash.TryToSellItem(); } finally { ApplyingRemote = false; }
            TillSales++;
            return true;
        }

        public static int TillSales;

        public static void SendAsk(Ask a) { Session.SendToHost(AskMsg, new W().U8((byte)a)); AsksSent++; Plugin.Log.LogInfo("[shop] asking the host: " + a); }

        private static void ClearCustomers()
        {
            foreach (var c in Customers.Values) if (c != null) Object.Destroy(c.gameObject);
            Customers.Clear();
        }

        public static object Snapshot()
        {
            var sm = ShopManager.Instance; var tm = TimeManagerV2.Instance;
            return new
            {
                open = sm != null && !sm.IsShopClosed,
                dayTime = tm != null ? tm.CurrrentDayTime.ToString() : null,
                days = GameManager.Instance?.currentGameSlot?.daysPassed ?? -1,
                localCustomers = sm != null && sm.visitorGenerator != null ? sm.visitorGenerator.generatedGameObjects.Count(g => g != null) : 0,
                drawnCustomers = Customers.Count(kv => kv.Value != null && kv.Value.Drawn > 0),
                shelves = sm?.references?.shopShowcases.SelectMany(s => s.itemPositions).Select(p => p.itemStack != null && p.itemStack.master != null ? $"{p.itemStack.master.name}x{p.itemStack.Quantity}@{p.itemStack.unitSellingPrice}" : "-").ToArray(),
                gold = HeroMerchant.Instance != null ? HeroMerchant.Instance.GetCurrentGold() : 0,
                TillSales, waitingAtTill = ShopManager.Instance?.references?.shopCash?.visitorBuying != null, StatesSent, StatesApplied, CustomerFramesSent, CustomerFramesApplied, ShelfUpdatesSent, ShelfUpdatesApplied, AsksSent,
            };
        }
    }

    /// A customer as the host's copy shows it.
    public sealed class CustomerPuppet : MonoBehaviour
    {
        private readonly List<SpriteRenderer> _parts = new List<SpriteRenderer>();
        private Vector2 _target;
        public int Drawn;

        public void Snap(Vector2 p) { _target = p; transform.position = new Vector3(p.x, p.y, 0); }

        public void Apply(Vector2 pos, List<Presence.Part> parts)
        {
            _target = pos;
            while (_parts.Count < parts.Count)
            {
                var go = new GameObject("part"); go.transform.SetParent(transform, false);
                _parts.Add(go.AddComponent<SpriteRenderer>());
            }
            Drawn = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                var sr = _parts[i];
                if (i >= parts.Count) { sr.enabled = false; continue; }
                var p = parts[i];
                var s = SpriteCache.Find(p.Sprite);
                sr.enabled = s != null;
                if (s == null) continue;
                Drawn++;
                sr.sprite = s; sr.transform.localPosition = p.Offset; sr.flipX = p.FlipX; sr.flipY = p.FlipY;
                sr.sortingLayerID = p.Layer; sr.sortingOrder = p.Order; sr.color = p.Color;
            }
        }

        public void Smooth()
        {
            var p = Vector2.Lerp(transform.position, _target, 1f - Mathf.Exp(-20f * Time.unscaledDeltaTime));
            transform.position = new Vector3(p.x, p.y, 0);
        }
    }

    // ---------------------------------------------------------------------- patches (guest)

    [HarmonyPatch(typeof(VisitorGenerator), nameof(VisitorGenerator.SpawnVisitor))]
    static class GuestSpawnsNoCustomers
    {
        static bool Prefix(ref Agent __result) { if (!Shop.GuestHere) return true; __result = null; return false; }
    }

    [HarmonyPatch(typeof(VisitorGenerator), nameof(VisitorGenerator.EnableSpawn))]
    static class GuestNoSpawnTimer
    {
        static bool Prefix() => !Shop.GuestHere;
    }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.OpenShop))]
    static class GuestAsksToOpen
    {
        static bool Prefix() { if (!Shop.GuestHere || Shop.ApplyingRemote) return true; Shop.SendAsk(Shop.Ask.OpenShop); return false; }
    }

    [HarmonyPatch(typeof(ShopEntranceDoor), nameof(ShopEntranceDoor.CloseShop))]
    static class GuestAsksToClose
    {
        static bool Prefix() { if (!Shop.GuestHere || Shop.ApplyingRemote) return true; Shop.SendAsk(Shop.Ask.CloseShop); return false; }
    }

    // The day is the host's: the guest's own clock changes (sleeping, coming back from a dungeon) are dropped;
    // sleeping asks the host, who changes the day for everyone.
    [HarmonyPatch(typeof(TimeManagerV2), nameof(TimeManagerV2.SwapDayTime))]
    static class GuestDaySwap { static bool Prefix() => !Shop.GuestHere || Shop.ApplyingRemote; }

    [HarmonyPatch(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToMorning))]
    static class GuestMorning { static bool Prefix() => !Shop.GuestHere || Shop.ApplyingRemote; }

    [HarmonyPatch(typeof(TimeManagerV2), nameof(TimeManagerV2.ChangeToAfternoon))]
    static class GuestAfternoon { static bool Prefix() => !Shop.GuestHere || Shop.ApplyingRemote; }

    [HarmonyPatch(typeof(CashPoint), nameof(CashPoint.TryToSellItem))]
    static class GuestTillAsks
    {
        static bool Prefix() { if (!Shop.GuestHere || Shop.ApplyingRemote) return true; Shop.SendAsk(Shop.Ask.Till); return false; }
    }

    [HarmonyPatch(typeof(Bed), nameof(Bed.SleepInBed))]
    static class GuestSleepAsks
    {
        static bool Prefix() { if (!Shop.GuestHere) return true; Shop.SendAsk(Shop.Ask.Sleep); return false; }
    }

    /// The shelves are the host's in this version: a guest cannot put its own items on them (they would be
    /// overwritten by the host's shelves). The showcase simply does not open for a guest.
    [HarmonyPatch(typeof(GUIManager), nameof(GUIManager.GoToShowcasePanel))]
    static class GuestNoShowcase
    {
        static bool Prefix() => !Shop.GuestHere;
    }
}
