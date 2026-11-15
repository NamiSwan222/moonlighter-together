using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterTogether
{
    /// Entry point. Reads the command line, installs the offline profile when asked, applies the Harmony
    /// patches and pumps every subsystem once per frame from one place (so the order is always the same).
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "moonlighter.together";
        public const string Name = "Moonlighter Together";
        public const string Version = "0.1.0";
        /// Bumped whenever the wire format changes; peers with another number are refused with a clear message.
        public const int Protocol = 1;

        public static Plugin Instance;
        public static ManualLogSource Log;
        public static Harmony Harmony;
        public static string GameDir;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            GameDir = Path.GetDirectoryName(Application.dataPath);
            Args.Parse(Environment.GetCommandLineArgs());

            // Keep both copies running when their window is not the focused one (two players on one PC, tests).
            Application.runInBackground = true;

            Harmony = new Harmony(Guid);
            if (Args.Profile != null) Platforms.OfflineProfile.Install(Args.Profile);
            // Patch class by class: one patch that does not fit (another game version, another mod) is logged
            // and skipped instead of taking every other patch down with it.
            int ok = 0, bad = 0;
            foreach (var t in typeof(Plugin).Assembly.GetTypes())
            {
                if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { Harmony.CreateClassProcessor(t).Patch(); ok++; }
                catch (Exception e) { bad++; Log.LogError($"[patch] {t.Name} not applied: {e.InnerException?.Message ?? e.Message}"); }
            }
            Log.LogInfo($"[patch] {ok} patch classes applied, {bad} failed");
            Log.LogInfo($"{Name} {Version} (protocol {Protocol}) loaded; profile={(Args.Profile ?? "(Steam)")} bridge={Args.BridgePort}");

            Sync.WorldSync.Init();
            Sync.Presence.Init();
            Sync.Floor.Init();
            Sync.Enemies.Init();
            Sync.Downed.Init();
            Sync.Shop.Init();
            if (Args.Slot >= 0) Sync.WorldSync.OwnSlotIndex = Args.Slot;
            if (Args.BridgePort > 0) Testing.Bridge.Start(Args.BridgePort);
            UI.Menu.Create(Config);
            UI.Menu.InitChat();
            Net.Session.MyName = UI.Menu.DisplayName();
            if (!string.IsNullOrEmpty(Args.PlayerName)) Net.Session.MyName = Args.PlayerName;
            if (Args.HostPort >= 0) Net.Session.Host(Args.HostPort);
            else if (Args.JoinAddress != null) Net.Session.Join(Args.JoinAddress);
        }

        private bool _steamInit;

        private void Update()
        {
            if (!_steamInit && Net.SteamTransport.Available) { _steamInit = true; try { Net.SteamInvites.Init(); Net.Session.MyName = string.IsNullOrEmpty(Args.PlayerName) ? UI.Menu.DisplayName() : Args.PlayerName; } catch (Exception e) { Log.LogWarning("[steam] " + e.Message); } }
            try { Testing.Bridge.Pump(); }
            catch (Exception e) { Log.LogError("[bridge] " + e); }
            try { Net.Session.Tick(); }
            catch (Exception e) { Log.LogError("[session] " + e); }
            try { Sync.Presence.Tick(); }
            catch (Exception e) { Log.LogError("[presence] " + e); }
            try { Sync.Shop.Tick(); }
            catch (Exception e) { Log.LogError("[shop] " + e); }
            try { Sync.Downed.Tick(); }
            catch (Exception e) { Log.LogError("[downed] " + e); }
            try { Sync.Enemies.Tick(); }
            catch (Exception e) { Log.LogError("[enemies] " + e); }
        }

        private void OnApplicationQuit()
        {
            try { Net.Session.Leave("game closed"); } catch { }
        }
    }

    /// Command-line switches (all optional):
    ///   --mt-profile NAME   play without Steam: saves live in BepInEx\mt-profiles\NAME, nothing reaches Steam
    ///   --mt-bridge PORT    open the local test control port (127.0.0.1 only)
    ///   --mt-host [PORT]    host a session at start
    ///   --mt-join ADDR      join a session at start (ADDR = ip[:port])
    ///   --mt-name NAME      player name shown to others
    public static class Args
    {
        public static string Profile;
        public static int BridgePort;
        public static int HostPort = -1;
        public static string JoinAddress;
        public static string PlayerName;
        public static bool SkipIntro;
        public static bool SteamNet;
        public static int Slot = -1;

        public static void Parse(string[] argv)
        {
            for (int i = 0; i < argv.Length; i++)
            {
                string a = argv[i];
                string next = i + 1 < argv.Length ? argv[i + 1] : null;
                switch (a)
                {
                    case "--mt-profile": Profile = Clean(next); i++; break;
                    case "--mt-bridge": int.TryParse(next, out BridgePort); i++; break;
                    case "--mt-host":
                        if (next != null && int.TryParse(next, out int p)) { HostPort = p; i++; } else HostPort = 0;
                        break;
                    case "--mt-join": JoinAddress = next; i++; break;
                    case "--mt-name": PlayerName = next; i++; break;
                    case "--mt-skip-intro": SkipIntro = true; break;
                    case "--mt-steam-net": SteamNet = true; break;
                    case "--mt-slot": int.TryParse(next, out Slot); i++; break;
                }
            }
        }

        /// A profile name becomes a folder name: letters, digits, dash and underscore only.
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!(char.IsLetterOrDigit(chars[i]) || chars[i] == '-' || chars[i] == '_')) chars[i] = '_';
            return new string(chars);
        }
    }
}
