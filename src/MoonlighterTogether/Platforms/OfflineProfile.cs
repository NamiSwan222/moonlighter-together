using System;
using System.Collections.Generic;
using System.IO;
using DigitalSunGames.Platforms;
using HarmonyLib;
using UnityEngine;

namespace MoonlighterTogether.Platforms
{
    /// "--mt-profile NAME": the game runs without Steam. Saves, options and key bindings live in
    /// BepInEx\mt-profiles\NAME, achievements go nowhere, and the Steam API is never started, so nothing a
    /// test or a second copy does can reach the player's Steam Cloud saves, achievements or play time.
    /// The game already has this split built in (Platform + I...Controller interfaces); we plug in our own.
    public static class OfflineProfile
    {
        public static bool Active;
        public static string Dir;

        public static void Install(string name)
        {
            Dir = Path.Combine(Path.Combine(Path.Combine(Plugin.GameDir, "BepInEx"), "mt-profiles"), name);
            Directory.CreateDirectory(Dir);
            Active = true;
            Plugin.Log.LogInfo("[profile] offline profile: " + Dir);
        }

        public static void InitPlatform()
        {
            var p = Platform.Instance;
            if (p.m_Initialized) return;
            // --mt-steam-net: Steam stays on for lobbies and P2P only; saves and achievements still stay here.
            p.m_Controller = Args.SteamNet ? (IPlatformController)SteamManager.Instance : new DRMFreeManager();
            p.m_CloudSave = new ProfileFiles(Dir);
            p.m_Achievements = new NoAchievements();
            p.m_FileSystem = new StandaloneFileSystemController();
            p.m_StoreController = new NoStore();
            p.m_DLC = new FolderDlc();
            Platform.CurrentPlatform = SupportedPlatforms.STEAM;   // same menus and videos as the Steam build
            p.m_Initialized = true;
            Plugin.Log.LogInfo("[profile] platform layer replaced (no Steam)");
        }
    }

    [HarmonyPatch(typeof(Platform), nameof(Platform.Init))]
    static class PlatformInitPatch
    {
        static bool Prefix()
        {
            if (!OfflineProfile.Active) return true;
            OfflineProfile.InitPlatform();
            return false;
        }
    }

    // Belt and braces: if anything still creates a SteamManager, it must not start Steam or quit the game.
    [HarmonyPatch(typeof(SteamManager), "Awake")]
    static class SteamManagerAwakePatch
    {
        static bool Prefix() => !OfflineProfile.Active || Args.SteamNet;
    }

    [HarmonyPatch(typeof(SteamManager), "OnDestroy")]
    static class SteamManagerDestroyPatch
    {
        static bool Prefix() => !OfflineProfile.Active || Args.SteamNet;
    }

    /// Save files in one folder. Names from the game are plain file names; anything else is refused.
    public class ProfileFiles : IPersistenFilesController
    {
        private readonly string _dir;
        public ProfileFiles(string dir) { _dir = dir; }

        private string PathOf(string file)
        {
            string name = Path.GetFileName(file ?? "");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("bad save file name: " + file);
            return Path.Combine(_dir, name);
        }

        public bool Exists(string file) => File.Exists(PathOf(file));

        public byte[] ReadBytes(string file)
        {
            string p = PathOf(file);
            return File.Exists(p) ? File.ReadAllBytes(p) : null;
        }

        public bool WriteBytes(string file, byte[] data)
        {
            // Write to a temporary file first so a crash never leaves half a save behind.
            string p = PathOf(file), tmp = p + ".tmp";
            File.WriteAllBytes(tmp, data ?? new byte[0]);
            if (File.Exists(p)) File.Delete(p);
            File.Move(tmp, p);
            return true;
        }

        public void ExistsAsync(string file, Action<bool> callback, Action corruptedCallback) => callback?.Invoke(Exists(file));
        public void WriteBytesAsync(string file, byte[] data, Action callback) { WriteBytes(file, data); callback?.Invoke(); }
        public void ReadBytesAsync(string file, Action<byte[]> callback, Action corruptedCallback) => callback?.Invoke(ReadBytes(file));
    }

    /// Achievements and statistics kept in memory only.
    public class NoAchievements : IAchievementController
    {
        private readonly Dictionary<string, float> _stats = new Dictionary<string, float>();
        public static readonly List<string> Unlocked = new List<string>();
        public void Init(AchievementDatabase database) { }
        public void AddExpansion(AchievementDatabase database) { }
        public float GetStatValue(string statID) => _stats.TryGetValue(statID ?? "", out float v) ? v : 0f;
        public void SetStatValue(string statID, float value) { _stats[statID ?? ""] = value; }
        public void AddToStatValue(string statID, float value) { _stats[statID ?? ""] = GetStatValue(statID) + value; }
        public void UnlockAchievement(string achievementID) { if (!Unlocked.Contains(achievementID)) Unlocked.Add(achievementID); }
        public void ResetAchievementsAndStats() { _stats.Clear(); Unlocked.Clear(); }
    }

    public class NoStore : IStoreController
    {
        public void OpenStoreToDLC() { }
    }

    /// Loads the DLC bundles that Steam installed into the game's "dlc" folder (Steam only puts them there
    /// for an owner), without asking Steam.
    public class FolderDlc : BaseDLCController
    {
        public override void LoadFromDefaultPath()
        {
            string folder = Path.Combine(Plugin.GameDir, "dlc");
            if (!Directory.Exists(folder)) return;
            foreach (string f in Directory.GetFiles(folder))
            {
                var bundle = AssetBundle.LoadFromFile(f);
                if (bundle) RegisterDLC(bundle);
            }
        }

        public override void CheckOwnershipForDLC(DLCHandle handle) { handle.isOwned = handle.bundle != null; }
        public override void CheckDownloadProgressForDLC(DLCHandle handle) { handle.isDownloading = false; }
    }
}
