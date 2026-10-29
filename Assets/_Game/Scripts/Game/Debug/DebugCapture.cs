using System.Collections;
using System.IO;
using UnityEngine;

namespace Unscrew.Game
{
    /// <summary>
    /// Play Mode screenshot of the full Game view including Screen Space Overlay UI
    /// (camera renders miss overlay canvases). Used for Docs/Screens via execute_code.
    /// </summary>
    public static class DebugCapture
    {
        public static string LastResult { get; private set; } = "";

        /// <summary><paramref name="path"/> is relative to the project root, e.g. "Docs/Screens/x.png".</summary>
        public static string Capture(string path)
        {
            var boot = GameBootstrap.Instance;
            if (!Application.isPlaying || boot == null) return "DebugCapture: Play Mode only";
            LastResult = "pending";
            boot.StartCoroutine(CaptureRoutine(path));
            return "DebugCapture: scheduled " + path;
        }

        private static IEnumerator CaptureRoutine(string path)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            LastResult = $"{path} {tex.width}x{tex.height}";
            Object.Destroy(tex);
        }
    }
}
