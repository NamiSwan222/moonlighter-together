using System.Linq;
using BepInEx.Configuration;
using MoonlighterTogether.Net;
using MoonlighterTogether.Sync;
using UnityEngine;

namespace MoonlighterTogether.UI
{
    /// F9: host or join. Always on screen in a session: who is playing, and what you can do right now.
    public class Menu : MonoBehaviour
    {
        public static Menu Instance;
        public static ConfigEntry<string> CfgName;
        public static ConfigEntry<int> CfgPort;
        public static ConfigEntry<string> CfgLastAddress;
        public static ConfigEntry<KeyCode> CfgKey;

        private bool _open;
        private string _address = "", _code = "", _name = "";
        private Rect _win = new Rect(40, 40, 460, 420);
        private GUIStyle _box, _small, _big;

        public static void Create(ConfigFile cfg)
        {
            CfgName = cfg.Bind("Player", "Name", "", "Name other players see (empty = your Steam name).");
            CfgPort = cfg.Bind("Network", "Port", UdpTransport.DefaultPort, "UDP port for hosting on your own network / VPN / forwarded port.");
            CfgLastAddress = cfg.Bind("Network", "LastAddress", "", "Last address joined.");
            CfgKey = cfg.Bind("Controls", "MenuKey", KeyCode.F9, "Key that opens the co-op menu.");
            var go = new GameObject("MoonlighterTogether.Menu");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<Menu>();
            Instance._address = CfgLastAddress.Value;
            Instance._name = CfgName.Value;
        }

        public static string DisplayName()
        {
            if (!string.IsNullOrEmpty(CfgName?.Value)) return CfgName.Value;
            try { if (SteamTransport.Available) return Steamworks.SteamFriends.GetPersonaName(); } catch { }
            return "Will";
        }

        public void SetOpen(bool open) { _open = open; }

        // ---- chat ----
        private static readonly System.Collections.Generic.List<(string text, float until)> Chat = new System.Collections.Generic.List<(string, float)>();
        private bool _typing;
        private string _line = "";
        public static int ChatReceived;

        public static void InitChat()
        {
            Session.On(Msg.Chat, Relay.Others, (sender, r) =>
            {
                string text = r.Str() ?? "";
                if (text.Length > 120) text = text.Substring(0, 120);
                string who = Session.Players.TryGetValue(sender, out var p) ? p.Name : "?";
                AddChat(who + ": " + text);
                ChatReceived++;
            });
        }

        public static void AddChat(string line)
        {
            Chat.Add((line, Time.unscaledTime + 10f));
            if (Chat.Count > 6) Chat.RemoveAt(0);
        }

        public static void Say(string text)
        {
            if (string.IsNullOrEmpty(text) || !Session.Active) return;
            Session.Send(Msg.Chat, new W().Str(text.Length > 120 ? text.Substring(0, 120) : text));
            AddChat(Session.MyName + ": " + text);
        }

        private void Update()
        {
            if (Input.GetKeyDown(CfgKey.Value)) _open = !_open;
            if (!_typing && Session.Active && Input.GetKeyDown(KeyCode.Return) && !_open && GUIManager.Instance != null && GUIManager.Instance.currentInventory == null)
            {
                _typing = true; _line = "";
                HeroMerchant.Instance?.heroMerchantController.DisableMoveInput();   // typing must not walk Will around
            }
        }

        private void Styles()
        {
            if (_box != null) return;
            _box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, padding = new RectOffset(8, 8, 6, 6) };
            _box.normal.textColor = new Color(1f, 0.95f, 0.85f);
            _small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            _big.normal.textColor = new Color(1f, 0.92f, 0.7f);
        }

        private void OnGUI()
        {
            Styles();
            if (Session.State != SessionState.Off) Hud();
            if (_open) _win = GUILayout.Window(81501, _win, Window, "Moonlighter Together " + Plugin.Version + "   (" + CfgKey.Value + " to close)");
        }

        // ------------------------------------------------------------------ menu

        private void Window(int id)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name", GUILayout.Width(80));
            _name = GUILayout.TextField(_name ?? "", 24);
            if (GUILayout.Button("Save", GUILayout.Width(60))) { CfgName.Value = _name; Session.MyName = DisplayName(); }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            if (Session.State == SessionState.Off)
            {
                bool inGame = HeroMerchant.Instance != null;
                GUILayout.Label("HOST (your world, your shop; up to 4 players)", _small);
                GUI.enabled = inGame;
                GUILayout.BeginHorizontal();
                GUI.enabled = inGame && SteamTransport.Available;
                if (GUILayout.Button("Host with Steam (friends)")) { Session.MyName = DisplayName(); Session.HostSteam(); }
                GUI.enabled = inGame;
                if (GUILayout.Button("Host on IP, port " + CfgPort.Value)) { Session.MyName = DisplayName(); Session.Host(CfgPort.Value); }
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                if (!inGame) GUILayout.Label("Load your game first to host.", _small);
                GUILayout.Space(8);
                GUILayout.Label("JOIN (you bring your character from save slot " + (WorldSync.OwnSlotIndex + 1) + ")", _small);
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP[:port]", GUILayout.Width(60));
                _address = GUILayout.TextField(_address ?? "", 64);
                if (GUILayout.Button("Join", GUILayout.Width(60))) { CfgLastAddress.Value = _address; Session.MyName = DisplayName(); Session.Join(_address); }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Steam code", GUILayout.Width(80));
                _code = GUILayout.TextField(_code ?? "", 24);
                GUI.enabled = SteamTransport.Available && ulong.TryParse(_code, out _);
                if (GUILayout.Button("Join", GUILayout.Width(60)) && ulong.TryParse(_code, out ulong lobby)) { Session.MyName = DisplayName(); Session.JoinSteam(lobby); }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Character from slot", GUILayout.Width(120));
                for (int i = 0; i < 5; i++) if (GUILayout.Toggle(WorldSync.OwnSlotIndex == i, (i + 1).ToString(), GUI.skin.button, GUILayout.Width(30))) WorldSync.OwnSlotIndex = i;
                GUILayout.EndHorizontal();
                GUILayout.Label("A Steam invite also works: accept it and the game joins by itself.", _small);
                if (!string.IsNullOrEmpty(Session.LastError)) GUILayout.Label("Last problem: " + Session.LastError, _small);
            }
            else
            {
                GUILayout.Label(Session.IsHost ? "You are the host." : Session.State == SessionState.Reconnecting ? "Connection lost - getting back in..." : "You are a guest in the host's world.", _small);
                if (Session.Transport is SteamTransport st)
                {
                    GUILayout.Label("Steam: " + st.Status + (Session.LobbyCode != null ? "   code: " + Session.LobbyCode : ""), _small);
                    if (Session.IsHost && GUILayout.Button("Invite Steam friends")) st.OpenInviteOverlay();
                    if (Session.LobbyCode != null && GUILayout.Button("Copy code")) GUIUtility.systemCopyBuffer = Session.LobbyCode;
                }
                else if (Session.IsHost) GUILayout.Label("Friends join with your IP address and port " + ((UdpTransport)Session.Transport).LocalPort + " (same network, VPN, or a forwarded UDP port).", _small);
                GUILayout.Space(6);
                foreach (var p in Session.Players.Values.OrderBy(x => x.Id))
                {
                    var r = Presence.Get(p.Id);
                    string where = p.IsLocal ? Presence.MyZone : r?.Zone ?? "?";
                    GUILayout.Label($"{(p.Id == 0 ? "[host] " : "")}{p.Name}{(p.IsLocal ? " (you)" : "")}  -  {Short(where)}{(p.Connected ? "" : "  (reconnecting)")}{(Downed.IsGhost(p.Id) ? "  (down)" : "")}{(p.IsLocal || p.RttMs < 0 ? "" : "  " + p.RttMs + " ms")}", _small);
                }
                GUILayout.Space(8);
                if (GUILayout.Button(Session.IsHost ? "End the session" : "Leave")) Session.Leave("left from the menu");
            }
            GUI.DragWindow();
        }

        private static string Short(string zone)
        {
            if (zone == null) return "?";
            if (zone.StartsWith("dungeon:"))
            {
                var p = zone.Split(':');
                return p.Length >= 4 ? $"{p[1]} floor {int.Parse(p[2]) + 1}, room {p[3]}" : zone;
            }
            return zone;
        }

        // ------------------------------------------------------------------ HUD

        private void ChatBox()
        {
            float y = Screen.height * 0.62f;
            foreach (var (text, until) in Chat)
            {
                if (until < Time.unscaledTime && !_typing) continue;
                GUI.Label(new Rect(14, y, Screen.width * 0.6f, 20), text, _box);
                y += 22;
            }
            if (!_typing) return;
            var e = Event.current;
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)) { Say(_line); StopTyping(); e.Use(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { StopTyping(); e.Use(); return; }
            GUI.SetNextControlName("mt-chat");
            _line = GUI.TextField(new Rect(14, y + 4, Screen.width * 0.5f, 22), _line, 120);
            GUI.FocusControl("mt-chat");
        }

        private void StopTyping()
        {
            _typing = false;
            if (GUIManager.Instance != null && GUIManager.Instance.currentInventory == null) HeroMerchant.Instance?.heroMerchantController.EnableMoveInput();
        }

        private void Hud()
        {
            ChatBox();
            float w = 230, x = 12, y = Screen.height * 0.17f;
            var lines = new System.Text.StringBuilder();
            lines.Append(Session.IsHost ? "Co-op: hosting" : Session.State == SessionState.Reconnecting ? "Co-op: reconnecting..." : Session.State == SessionState.Joining ? "Co-op: joining..." : "Co-op: guest");
            lines.Append("   [" + CfgKey.Value + "]");
            foreach (var p in Session.Players.Values.OrderBy(q => q.Id))
            {
                if (p.IsLocal) continue;
                var r = Presence.Get(p.Id);
                string hp = r != null && r.MaxHp > 0 ? $"{Mathf.CeilToInt(r.Hp)}/{Mathf.CeilToInt(r.MaxHp)}" : "";
                lines.Append($"\n{p.Name}  {hp}{(Downed.IsGhost(p.Id) ? "  DOWN" : "")}{(p.Connected ? "" : "  (lost)")}{(r != null && r.SameZoneAsMe ? "  - here" : "")}");
            }
            GUI.Box(new Rect(x, y, w, 22 + 18 * (Session.Players.Count)), lines.ToString(), _box);

            if (Downed.Me)
                GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 30), "You are down. A teammate can revive you (R next to you), or you come back on the next floor.", _big);
            else
            {
                int g = Downed.GhostInReach();
                if (g >= 0 && Session.Players.TryGetValue(g, out var gp))
                    GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 30), "Press R to revive " + gp.Name, _big);
            }
            if (Session.State == SessionState.Reconnecting)
                GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 30), "Connection to the host lost - getting back in...", _big);
        }
    }
}
