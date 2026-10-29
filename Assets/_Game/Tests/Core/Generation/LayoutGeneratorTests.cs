using System;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class LayoutGeneratorTests
    {
        private static DifficultySettings Settings(int colors, int mix, int buffer = 5, int seed = 1, int active = 3, bool hard = false) =>
            new DifficultySettings
            {
                colorCount = colors, activeBoxes = active, boxCapacity = 3, bufferSlots = buffer,
                mixWindow = mix, seed = seed, isHard = hard,
            };

        private static void AssertValid(LevelDefinition def, DifficultySettings s, LevelLayout layout)
        {
            int n = def.bolts.Length;
            Assert.AreEqual(n, layout.boltColors.Length);
            Assert.AreEqual(n / s.boxCapacity, layout.boxQueue.Length);
            int used = Math.Min(s.colorCount, n / s.boxCapacity);
            var boltCount = new int[16];
            var boxCount = new int[16];
            foreach (var c in layout.boltColors)
            {
                Assert.GreaterOrEqual(Array.IndexOf(def.allowedColors, c), 0, "color from allowedColors");
                Assert.Less(Array.IndexOf(def.allowedColors, c), used, "color among the first colorCount");
                boltCount[c]++;
            }
            foreach (var c in layout.boxQueue) boxCount[c]++;
            for (int c = 0; c < 16; c++)
                Assert.AreEqual(boxCount[c] * s.boxCapacity, boltCount[c], "bolts of color " + c + " fill its boxes exactly");
        }

        private static bool BotWins(LevelDefinition def, DifficultySettings s, LevelLayout layout, out int peak)
        {
            var session = new FakeGameSession(def, layout, s);
            bool won = new GreedySolver().Solve(session);
            peak = session.PeakBuffer;
            return won;
        }

        [Test]
        public void InvalidInput_Throws()
        {
            var gen = new LayoutGenerator();
            var def = TestLevels.TwoLayers();
            Assert.Throws<ArgumentNullException>(() => gen.Generate(null, Settings(2, 0)));
            Assert.Throws<ArgumentNullException>(() => gen.Generate(def, null));
            Assert.Throws<ArgumentException>(() => gen.Generate(def, Settings(0, 0)));
            Assert.Throws<ArgumentException>(() => gen.Generate(def, Settings(5, 0)), "more than allowedColors");
            Assert.Throws<ArgumentException>(() => gen.Generate(def, Settings(2, 0, buffer: 0)));
            Assert.Throws<ArgumentException>(() => gen.Generate(def, Settings(2, -1)));
            Assert.Throws<ArgumentException>(() => gen.Generate(TestLevels.Build(new[] { 0, 0, 0, 0 }), Settings(2, 0)), "4 bolts");
        }

        [Test]
        public void CyclicBlocking_Throws()
        {
            var def = TestLevels.Build(new[] { 0, 0, 0, 1, 1, 1 },
                new[] { new[] { 1 }, new[] { 1 }, new[] { 1 }, new[] { 0 }, new[] { 0 }, new[] { 0 } });
            Assert.Throws<InvalidOperationException>(() => new LayoutGenerator().Generate(def, Settings(2, 0)));
        }

        [Test]
        public void RemovalOrder_RespectsBlocking()
        {
            var def = SyntheticLevels.Real(10);
            var order = LayoutGenerator.RandomRemovalOrder(def, new Rng(5));
            var removed = new bool[def.bolts.Length];
            foreach (var b in order)
            {
                Assert.IsTrue(BoardRules.IsAccessible(def, removed, b));
                removed[b] = true;
            }
        }

        [Test]
        public void MixZero_NeedsNoBuffer_EvenWithOneHole()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var def = SyntheticLevels.Real(7, seed);
                var s = Settings(8, 0, buffer: 1, seed: seed);
                var gen = new LayoutGenerator();
                var layout = gen.Generate(def, s);
                AssertValid(def, s, layout);
                Assert.AreEqual(0, gen.LastPlannedPeak);
                Assert.IsTrue(BotWins(def, s, layout, out int peak), "seed " + seed);
                Assert.AreEqual(0, peak);
            }
        }

        [Test]
        public void SameSeed_SameLayout_DifferentSeed_DifferentLayout()
        {
            var def = SyntheticLevels.Real(15);
            var a = new LayoutGenerator().Generate(def, Settings(9, 4, seed: 42));
            var b = new LayoutGenerator().Generate(def, Settings(9, 4, seed: 42));
            var c = new LayoutGenerator().Generate(def, Settings(9, 4, seed: 43));
            CollectionAssert.AreEqual(a.boltColors, b.boltColors);
            CollectionAssert.AreEqual(a.boxQueue, b.boxQueue);
            CollectionAssert.AreNotEqual(a.boltColors, c.boltColors);
        }

        [Test]
        public void ColorCount_IsRespected_AndAllColorsUsed()
        {
            var def = SyntheticLevels.Real(20);
            var s = Settings(6, 2);
            var layout = new LayoutGenerator().Generate(def, s);
            AssertValid(def, s, layout);
            for (int c = 0; c < 6; c++) Assert.GreaterOrEqual(Array.IndexOf(layout.boxQueue, c), 0, "color " + c + " used");
        }

        [Test]
        public void MoreColorsThanBoxes_UsesOnlyAsManyAsBoxes()
        {
            var def = SyntheticLevels.Create(1, 2, 6, 5, 3);
            var s = Settings(5, 0);
            var layout = new LayoutGenerator().Generate(def, s);
            AssertValid(def, s, layout);
        }

        [Test]
        public void LargerMixWindow_RaisesBufferPressure()
        {
            long low = 0, high = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var def = SyntheticLevels.Real(19, seed);
                var gen = new LayoutGenerator();
                gen.Generate(def, Settings(9, 0, buffer: 5, seed: seed));
                low += gen.LastPlannedPeak;
                gen.Generate(def, Settings(9, 5, buffer: 5, seed: seed));
                high += gen.LastPlannedPeak;
                Assert.LessOrEqual(gen.LastPlannedPeak, 3, "normal level keeps a spare hole");
            }
            Assert.AreEqual(0, low);
            Assert.Greater(high, 20, "mixing must actually use the buffer");
        }

        [Test]
        public void HardLevel_MayPlanUpToLastButOneHole()
        {
            var def = SyntheticLevels.Real(20);
            var gen = new LayoutGenerator();
            var s = Settings(10, 6, buffer: 4, hard: true, seed: 9);
            var layout = gen.Generate(def, s);
            Assert.LessOrEqual(gen.LastPlannedPeak, 3);
            Assert.IsTrue(BotWins(def, s, layout, out _));
        }

        /// <summary>Synthetic levels of the real campaign sizes, settings from DifficultyCurve, several DAG variants each.</summary>
        [Test]
        public void RealLevelSizes_AreSolvableByBot_AndDeterministic(
            [Range(1, 20)] int level)
        {
            var curve = new DifficultyCurve();
            for (int variant = 0; variant < 5; variant++)
            {
                var def = SyntheticLevels.Real(level, variant);
                var s = curve.Get(def);
                s.seed += variant; // vary layouts too
                var gen = new LayoutGenerator();
                var layout = gen.Generate(def, s);
                AssertValid(def, s, layout);
                Assert.IsTrue(gen.LastPassedSolverCheck, $"level {level} variant {variant}: generator self-check");
                Assert.IsTrue(BotWins(def, s, layout, out int peak), $"level {level} variant {variant}: bot on fake session");
                Assert.Less(peak, s.bufferSlots);

                var again = new LayoutGenerator().Generate(def, s);
                CollectionAssert.AreEqual(layout.boltColors, again.boltColors, "determinism");
                CollectionAssert.AreEqual(layout.boxQueue, again.boxQueue, "determinism");
            }
        }

        /// <summary>Generator self-check (policy on PuzzleSim) must agree with the bot on an independent session implementation.</summary>
        [Test]
        public void Stress_SelfCheckAgreesWithBotOnSession()
        {
            var curve = new DifficultyCurve();
            int fallbacks = 0;
            for (int level = 1; level <= 20; level++)
            {
                for (int variant = 0; variant < 20; variant++)
                {
                    var def = SyntheticLevels.Real(level, 1000 + variant);
                    def.number = level + 20 * (variant % 3); // also endless-mode numbers
                    var s = curve.Get(def);
                    var gen = new LayoutGenerator();
                    var layout = gen.Generate(def, s);
                    if (!gen.LastPassedSolverCheck) fallbacks++;
                    Assert.AreEqual(gen.LastPassedSolverCheck, BotWins(def, s, layout, out _), $"level {def.number} variant {variant}");
                }
            }
            Assert.AreEqual(0, fallbacks, "fallback layouts");
        }

        [Test]
        public void RealLevelSizes_DifficultyReport()
        {
            var curve = new DifficultyCurve();
            var report = new System.Text.StringBuilder();
            for (int level = 1; level <= 20; level++)
            {
                var def = SyntheticLevels.Real(level);
                var s = curve.Get(def);
                var gen = new LayoutGenerator();
                var layout = gen.Generate(def, s);
                BotWins(def, s, layout, out int peak);
                report.AppendLine($"L{level,2} bolts={def.bolts.Length,3} colors={s.colorCount,2} buf={s.bufferSlots} mix={s.mixWindow}" +
                                  $" usedMix={gen.LastMixWindowUsed} attempts={gen.LastAttempts} plannedPeak={gen.LastPlannedPeak} botPeak={peak}" +
                                  (s.isHard ? " HARD" : ""));
            }
            TestContext.WriteLine(report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
