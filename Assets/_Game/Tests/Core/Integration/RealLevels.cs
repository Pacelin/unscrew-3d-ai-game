using System.IO;

namespace Unscrew.Core.Tests
{
    /// <summary>Loads baked Assets/_Game/Data/Levels/Level_XX.json in both Unity and dotnet test runs.</summary>
    public static class RealLevels
    {
        public const int Count = 20;

        public static LevelDefinition Load(int number)
        {
            string json = File.ReadAllText(Path.Combine(LevelsDir(), $"Level_{number:00}.json"));
#if CORE_DOTNET_TESTS
            var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            return System.Text.Json.JsonSerializer.Deserialize<LevelDefinition>(json, options);
#else
            return UnityEngine.JsonUtility.FromJson<LevelDefinition>(json);
#endif
        }

        private static string LevelsDir()
        {
#if CORE_DOTNET_TESTS
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "_Game")))
                dir = dir.Parent;
            if (dir == null) throw new DirectoryNotFoundException("Project root with Assets/_Game not found");
            return Path.Combine(dir.FullName, "Assets", "_Game", "Data", "Levels");
#else
            return Path.Combine(UnityEngine.Application.dataPath, "_Game", "Data", "Levels");
#endif
        }
    }
}
