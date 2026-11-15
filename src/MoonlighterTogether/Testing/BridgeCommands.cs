using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoonlighterTogether.Testing
{
    /// The commands the test scripts can send. Each one is small; scenarios live in the test scripts.
    public static partial class BridgeCommands
    {
        public static void RegisterAll()
        {
            Bridge.Register("ping", a => "pong");
            Bridge.Register("info", a => Info());
            Bridge.Register("quit", a => { Application.Quit(); return true; });
            Bridge.Register("timescale", a => { Time.timeScale = (float)a["value"]; return Time.timeScale; });
            Bridge.Register("screenshot", a => Screenshot((string)a["path"]));
            NetCommands.Register();
            DungeonCommands.Register();
            RegisterGame();
        }

        static partial void RegisterGame();

        private static object Info()
        {
            var scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                scenes.Add(s.name + (s.isLoaded ? "" : "(loading)"));
            }
            return new
            {
                plugin = Plugin.Version,
                protocol = Plugin.Protocol,
                profile = Args.Profile,
                unity = Application.unityVersion,
                game = Application.version,
                scenes,
                frame = Time.frameCount,
                time = Time.unscaledTime,
                timeScale = Time.timeScale,
                fps = 1f / Mathf.Max(0.0001f, Time.smoothDeltaTime),
                achievementsOffline = Platforms.NoAchievements.Unlocked,
            };
        }

        private static IEnumerator Screenshot(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(path, ImageConversion.EncodeToPNG(tex)); }
            finally { UnityEngine.Object.Destroy(tex); }
            yield return new Bridge.Result { Value = path };
        }
    }
}
