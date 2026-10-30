using System;
using System.IO;

namespace Unscrew.Core.Tests
{
    /// <summary>Result of measuring one prepared level with DifficultyProbe.</summary>
    public sealed class DifficultySample
    {
        public int level;
        public int bolts;
        public DifficultySettings settings;
        public int plannedPeak;
        public int usedMix;
        public int runs;
        public int casualWins;
        public double casualAvgPeak;
        /// <summary>LayoutGenerator.LastCasualEstimate (-1 when the generator did not estimate).</summary>
        public double generatorEstimate;
        public bool botWins;
        public int botPeak;

        public double CasualWinRate => runs == 0 ? 0 : (double)casualWins / runs;
    }

    /// <summary>
    /// Measures how hard a generated layout is for a human-like player: N playthroughs of CasualPolicy
    /// (p = 0.65 to take a visible box move, otherwise a uniformly random legal bolt) on PuzzleSim.
    /// Metric: casual win rate. The greedy bot (GreedyPolicy) is recorded too and must always win.
    /// Uses its own RNG stream, independent of the generator's internal estimate.
    /// </summary>
    public static class DifficultyProbe
    {
        public const int DefaultRuns = 200;

        public static DifficultySample Measure(LevelDefinition def, int displayNumber, int runs = DefaultRuns)
        {
            int source = def.number;
            def.number = displayNumber;
            var settings = new DifficultyCurve().Get(def);
            def.number = source;
            return Measure(def, settings, displayNumber, runs);
        }

        public static DifficultySample Measure(LevelDefinition def, DifficultySettings settings, int level, int runs)
        {
            var gen = new LayoutGenerator();
            var layout = gen.Generate(def, settings);
            var sample = new DifficultySample
            {
                level = level,
                bolts = def.bolts.Length,
                settings = settings,
                plannedPeak = gen.LastPlannedPeak,
                usedMix = gen.LastMixWindowUsed,
                generatorEstimate = gen.LastCasualEstimate,
                runs = runs,
            };

            var sim = new PuzzleSim(def, layout.boltColors, layout.boxQueue, settings.activeBoxes, settings.boxCapacity,
                                    settings.bufferSlots);
            sample.botWins = new GreedyPolicy().Play(sim);
            sample.botPeak = sim.PeakBuffer;

            double rate = new CasualPolicy().WinRate(sim, new Rng(settings.seed ^ 0x5bd1e995), runs, out double meanPeak);
            sample.casualWins = (int)Math.Round(rate * runs);
            sample.casualAvgPeak = meanPeak;
            return sample;
        }

        public static string FormatHeader() =>
            "lvl bolts col box buf mix used plan | casual% avgPeak est% | bot botPeak";

        public static string Format(DifficultySample s) =>
            $"L{s.level,2} {s.bolts,4} {s.settings.colorCount,3} {s.settings.activeBoxes,3} {s.settings.bufferSlots,3}" +
            $" {s.settings.mixWindow,3} {s.usedMix,4} {s.plannedPeak,4} | {s.CasualWinRate * 100,6:0.0} {s.casualAvgPeak,7:0.00}" +
            $" {(s.generatorEstimate < 0 ? "   -" : (s.generatorEstimate * 100).ToString("0").PadLeft(4))}" +
            $" | {(s.botWins ? "win" : "LOSE")} {s.botPeak,5}{(s.settings.isHard ? "  HARD" : "")}";
    }

    /// <summary>Loads baked Assets/_Game/Data/Levels/Level_XX.json (own copy: Integration is not part of the Generation project).</summary>
    public static class BakedLevels
    {
        public const int Count = 20;

        public static bool Available => File.Exists(PathOf(1));

        public static LevelDefinition Load(int number)
        {
            string json = File.ReadAllText(PathOf(number));
#if CORE_DOTNET_TESTS
            var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            return System.Text.Json.JsonSerializer.Deserialize<LevelDefinition>(json, options);
#else
            return UnityEngine.JsonUtility.FromJson<LevelDefinition>(json);
#endif
        }

        private static string PathOf(int number) => Path.Combine(LevelsDir(), $"Level_{number:00}.json");

        private static string LevelsDir()
        {
#if CORE_DOTNET_TESTS
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
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
