using HarmonyLib;

namespace MoonlighterTogether.Testing
{
    /// "--mt-skip-intro": the studio logo videos end at once (tests start many copies; nothing to watch).
    [HarmonyPatch(typeof(VideoSequence), nameof(VideoSequence.Play))]
    static class SkipIntroVideos
    {
        static bool Prefix(VideoSequence __instance)
        {
            if (!Args.SkipIntro) return true;
            __instance.isPlaying = true;
            __instance._index = int.MaxValue - 1;   // the next step is past the last video: "sequence finished"
            __instance.PlayNextVideo();
            return false;
        }
    }

    /// Same switch: test copies never fetch the publisher's news (no network call, no popup over the game).
    [HarmonyPatch(typeof(DigitalSun.Moonlighter.InGameNewsDataDownloader), "CanRefreshNews")]
    static class NoNewsFetch
    {
        static bool Prefix(ref bool __result)
        {
            if (!Args.SkipIntro) return true;
            __result = false;
            return false;
        }
    }
}
