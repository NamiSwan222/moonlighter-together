using System;
using System.IO;
using System.Linq;
using DigitalSunGames.Platforms;
using HarmonyLib;

namespace MoonlighterTogether.Platforms
{
    /// Before the game reads its save for the first time in a run, a copy of it (and of the options) goes
    /// to BepInEx\mt-backups. The last 10 copies are kept. Restoring = put the file back through the game's
    /// own save location (see the readme).
    public static class SaveBackup
    {
        private static bool _done;
        public static string LastBackup;

        public static void Run()
        {
            if (_done) return;
            _done = true;
            try
            {
                var store = Platform.CloudSave;
                if (store == null || !store.Exists("gameslot")) { Plugin.Log.LogInfo("[backup] no save to back up yet"); return; }
                string dir = Path.Combine(Path.Combine(Plugin.GameDir, "BepInEx"), "mt-backups");
                if (OfflineProfile.Active) dir = Path.Combine(OfflineProfile.Dir, "backups");
                Directory.CreateDirectory(dir);
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                var bytes = store.ReadBytes("gameslot");
                if (bytes == null || bytes.Length == 0) return;
                LastBackup = Path.Combine(dir, $"gameslot-{stamp}.json");
                File.WriteAllBytes(LastBackup, bytes);
                if (store.Exists("options.ini")) File.WriteAllBytes(Path.Combine(dir, $"options-{stamp}.ini"), store.ReadBytes("options.ini"));
                foreach (var old in Directory.GetFiles(dir, "gameslot-*.json").OrderByDescending(f => f).Skip(10)) File.Delete(old);
                foreach (var old in Directory.GetFiles(dir, "options-*.ini").OrderByDescending(f => f).Skip(10)) File.Delete(old);
                Plugin.Log.LogInfo($"[backup] save backed up: {LastBackup} ({bytes.Length} bytes)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("[backup] could not back up the save: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(GameManager), nameof(GameManager.PreloadGameSlot))]
    static class BackupBeforeFirstLoad
    {
        static void Prefix() => SaveBackup.Run();
    }
}
