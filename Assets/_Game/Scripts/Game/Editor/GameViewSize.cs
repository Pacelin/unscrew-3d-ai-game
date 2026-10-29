using System;
using System.Reflection;
using UnityEditor;

namespace Unscrew.Game.Editor
{
    /// <summary>
    /// Selects (adding if needed) a fixed Game view resolution, e.g. 1080x1920 for 9:16 screenshots.
    /// Unity has no public API for this, so it goes through internal GameViewSizes via reflection.
    /// </summary>
    public static class GameViewSize
    {
        [MenuItem("Unscrew/Game View/Portrait 9x16 (1080x1920)")]
        public static void Portrait916() => Set(1080, 1920);

        [MenuItem("Unscrew/Game View/Portrait 9x19.5 (1080x2340)")]
        public static void Portrait9195() => Set(1080, 2340);

        public static string Set(int width, int height)
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            var sizes = singleton.GetProperty("instance").GetValue(null);
            var group = sizesType.GetProperty("currentGroup").GetValue(sizes);
            var groupType = group.GetType();
            int total = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null);
            var getSize = groupType.GetMethod("GetGameViewSize");
            var sizeType = asm.GetType("UnityEditor.GameViewSize");
            int index = -1;
            for (int i = 0; i < total; i++)
            {
                var s = getSize.Invoke(group, new object[] { i });
                if ((int)sizeType.GetProperty("width").GetValue(s) == width &&
                    (int)sizeType.GetProperty("height").GetValue(s) == height &&
                    sizeType.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution")
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                var enumType = asm.GetType("UnityEditor.GameViewSizeType");
                var ctor = sizeType.GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) });
                var size = ctor.Invoke(new[] { Enum.Parse(enumType, "FixedResolution"), width, height, $"Unscrew {width}x{height}" });
                groupType.GetMethod("AddCustomSize").Invoke(group, new[] { size });
                index = (int)groupType.GetMethod("GetTotalCount").Invoke(group, null) - 1;
            }
            var gameViewType = asm.GetType("UnityEditor.GameView");
            var window = EditorWindow.GetWindow(gameViewType, false, null, false);
            var select = gameViewType.GetMethod("SizeSelectionCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            select.Invoke(window, new object[] { index, null });
            return $"Game view: {width}x{height} (index {index})";
        }
    }
}
