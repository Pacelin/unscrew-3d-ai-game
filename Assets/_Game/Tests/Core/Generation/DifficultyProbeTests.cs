using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    /// <summary>
    /// Difficulty measured as the win rate of a casual player (CasualPolicy, DifficultyProbe) on the generated layouts.
    /// Prints the per-level table and checks the coarse shape of the campaign curve.
    /// </summary>
    public class DifficultyProbeTests
    {
        private static DifficultySample[] MeasureCampaign(Func<int, LevelDefinition> load, string title, int runs)
        {
            var samples = new DifficultySample[DifficultyCurve.CampaignLength + 1];
            var table = new StringBuilder();
            table.AppendLine(title);
            table.AppendLine(DifficultyProbe.FormatHeader());
            for (int n = 1; n <= DifficultyCurve.CampaignLength; n++)
            {
                samples[n] = DifficultyProbe.Measure(load(n), n, runs);
                table.AppendLine(DifficultyProbe.Format(samples[n]) +
                                 $"  target {DifficultyCurve.TargetCasualWinRate(n) * 100:0}");
            }
            TestContext.WriteLine(table.ToString());
            return samples;
        }

        private static double Rate(DifficultySample[] s, int n) => s[n].CasualWinRate;

        private static double Mean(DifficultySample[] s, int from, int to)
        {
            double sum = 0;
            for (int n = from; n <= to; n++) sum += Rate(s, n);
            return sum / (to - from + 1);
        }

        private static void AssertCurveShape(DifficultySample[] s, double hardGap)
        {
            for (int n = 1; n <= DifficultyCurve.CampaignLength; n++)
                Assert.IsTrue(s[n].botWins, $"level {n}: the greedy bot must win every generated layout");

            Assert.GreaterOrEqual(Rate(s, 1), 0.95, "tutorial");
            for (int n = 2; n <= 4; n++) Assert.GreaterOrEqual(Rate(s, n), 0.85, "early level " + n);

            // Monotone-ish trend over the normal levels of each block of five.
            double b1 = Mean(s, 6, 9), b2 = Mean(s, 11, 14), b3 = Mean(s, 16, 19);
            Assert.Greater(Mean(s, 2, 4), b1, "levels 2-4 easier than 6-9");
            Assert.Greater(b1, b2, "levels 6-9 easier than 11-14");
            Assert.Greater(b2, b3, "levels 11-14 easier than 16-19");
            Assert.Less(b3, 0.75, "late levels must be noticeably harder");
            Assert.Greater(b3, 0.30, "late levels must stay winnable for a casual player");

            for (int h = 5; h <= DifficultyCurve.CampaignLength; h += DifficultyCurve.HardEvery)
            {
                Assert.Less(Rate(s, h), Rate(s, h - 1) - hardGap, $"hard level {h} vs {h - 1}");
                if (h < DifficultyCurve.CampaignLength)
                    Assert.Less(Rate(s, h), Rate(s, h + 1) - hardGap, $"hard level {h} vs {h + 1}");
            }
            Assert.Less(Rate(s, 20), Rate(s, 5), "the last hard level is harder than the first one");
        }

        [Test]
        public void BakedCampaign_DifficultyTable_HasTheTargetShape()
        {
            if (!BakedLevels.Available) Assert.Ignore("baked levels not found");
            // Bake 5 (visibility lifts) re-shaped some blocking graphs; a hard level must still dip below both
            // neighbours, by the same margin the synthetic campaign is held to.
            AssertCurveShape(MeasureCampaign(BakedLevels.Load, "Baked levels", DifficultyProbe.DefaultRuns), 0.05);
        }

        [Test]
        public void SyntheticCampaign_DifficultyTable_HasTheTargetShape()
        {
            // Independent geometry (random DAGs of real sizes): the shape must not depend on the baked models.
            AssertCurveShape(MeasureCampaign(n => SyntheticLevels.Real(n), "Synthetic levels", 120), 0.05);
        }

        [Test]
        public void Generator_HitsTargetOnAverage_AcrossSeeds()
        {
            // The generator's own estimate is noisy; across seeds the probe's mean must track the target.
            var curve = new DifficultyCurve();
            foreach (int n in new[] { 4, 10, 17 })
            {
                var def = SyntheticLevels.Real(n);
                double sum = 0;
                const int seeds = 3;
                for (int k = 0; k < seeds; k++)
                {
                    var s = curve.Get(def);
                    s.seed += 7777 * k;
                    sum += DifficultyProbe.Measure(def, s, n, 120).CasualWinRate;
                }
                Assert.AreEqual(DifficultyCurve.TargetCasualWinRate(n), sum / seeds, 0.15, "level " + n);
            }
        }

        [Test]
        public void CasualPolicy_IsDeterministic_AndWinsAnOpenLevel()
        {
            var def = SyntheticLevels.Real(8);
            var s = new DifficultyCurve().Get(def);
            var layout = new LayoutGenerator().Generate(def, s);
            var sim = new PuzzleSim(def, layout.boltColors, layout.boxQueue, s.activeBoxes, s.boxCapacity, s.bufferSlots);
            double a = new CasualPolicy().WinRate(sim, new Rng(3), 30);
            double b = new CasualPolicy().WinRate(sim, new Rng(3), 30);
            Assert.AreEqual(a, b);

            // No blocking, a single color: every tap goes into a box.
            var open = TestLevels.Build(new[] { 0, 0, 0, 1, 1, 1 }, null, new[] { 0 });
            var simple = new PuzzleSim(open, new[] { 0, 0, 0, 0, 0, 0 }, new[] { 0, 0 }, 1, 3, 1);
            Assert.AreEqual(1.0, new CasualPolicy().WinRate(simple, new Rng(1), 10));
        }

        [Test]
        public void CasualPolicy_NeverPicksAnIllegalMove_AndLosesWhenStuck()
        {
            // One hole, open box needs color 0, but the only accessible bolts are color 1 → second color-1 tap is impossible.
            var def = TestLevels.Build(new[] { 0, 0, 0, 1, 1, 1 },
                new[] { new[] { 1 }, new[] { 1 }, new[] { 1 }, null, null, null }, new[] { 0, 1 });
            var sim = new PuzzleSim(def, new[] { 0, 0, 0, 1, 1, 1 }, new[] { 0, 1 }, 1, 3, 1);
            Assert.IsFalse(new CasualPolicy(100).Play(sim, new Rng(1)));
            Assert.IsTrue(sim.IsLost);
            Assert.AreEqual(1, sim.BufferUsed);
            Assert.Throws<ArgumentOutOfRangeException>(() => new CasualPolicy(101));
        }

        [Test]
        public void Targeting_IsDeterministic_AndCanBeDisabled()
        {
            var def = SyntheticLevels.Real(12);
            var s = new DifficultyCurve().Get(def);
            var g1 = new LayoutGenerator();
            var a = g1.Generate(def, s);
            var b = new LayoutGenerator().Generate(def, s);
            CollectionAssert.AreEqual(a.boltColors, b.boltColors);
            Assert.AreEqual(DifficultyCurve.TargetCasualWinRate(12), g1.LastTarget, 1e-9);
            Assert.GreaterOrEqual(g1.LastCandidates, 1);
            Assert.AreEqual(g1.LastCandidates, g1.LastSearchEstimates.Count);
            Assert.GreaterOrEqual(g1.LastCasualEstimate, 0);

            var plain = new LayoutGenerator { TargetDifficulty = false };
            plain.Generate(def, s);
            Assert.AreEqual(-1, plain.LastCasualEstimate);
            Assert.AreEqual(1, plain.LastCandidates);
            Assert.AreEqual(s.mixWindow, plain.LastMixWindowUsed);
        }

        [Test]
        public void TargetOverride_SteersDifficulty()
        {
            var def = SyntheticLevels.Real(13);
            var s = new DifficultyCurve().Get(def);
            var easy = new LayoutGenerator { TargetOverride = 0.95 };
            var hard = new LayoutGenerator { TargetOverride = 0.30 };
            var le = easy.Generate(def, s);
            var lh = hard.Generate(def, s);
            Assert.Greater(easy.LastCasualEstimate, hard.LastCasualEstimate + 0.3);
            Assert.IsTrue(new GreedyPolicy().Play(new PuzzleSim(def, le.boltColors, le.boxQueue, s.activeBoxes, 3, s.bufferSlots)));
            Assert.IsTrue(new GreedyPolicy().Play(new PuzzleSim(def, lh.boltColors, lh.boxQueue, s.activeBoxes, 3, s.bufferSlots)));
        }

        [Test]
        public void DiveOrder_RespectsBlocking()
        {
            var def = SyntheticLevels.Real(19);
            foreach (int dive in new[] { 0, 50, 100 })
            {
                var order = LayoutGenerator.RandomRemovalOrder(def, new Rng(dive + 1), dive);
                var removed = new bool[def.bolts.Length];
                var seen = new HashSet<int>();
                foreach (var b in order)
                {
                    Assert.IsTrue(BoardRules.IsAccessible(def, removed, b), "dive " + dive);
                    Assert.IsTrue(seen.Add(b));
                    removed[b] = true;
                }
                Assert.AreEqual(def.bolts.Length, seen.Count);
            }
        }

        [TestCase(1, 1.0)]
        [TestCase(3, 0.95)]
        [TestCase(10, 0.56)] // 0.90 - 0.028*5 - 0.20
        [TestCase(21, 0.772)] // endless 21 -> level 11: 0.90 - 0.028*6 + 0.04 (relief)
        public void TargetCurve_Values(int number, double expected)
        {
            Assert.AreEqual(expected, DifficultyCurve.TargetCasualWinRate(number), 0.005);
        }

        [Test]
        public void TargetCurve_DeclinesWithHardDipsAndRelief()
        {
            for (int n = 6; n < 20; n++)
            {
                if (DifficultyCurve.IsHardLevel(n) || DifficultyCurve.IsHardLevel(n + 1)) continue;
                Assert.GreaterOrEqual(DifficultyCurve.TargetCasualWinRate(n), DifficultyCurve.TargetCasualWinRate(n + 1), "trend at " + n);
            }
            for (int h = 5; h <= 20; h += 5)
            {
                double hard = DifficultyCurve.TargetCasualWinRate(h);
                Assert.LessOrEqual(hard, DifficultyCurve.TargetCasualWinRate(h - 1) - 0.15, "hard " + h);
                if (h < 20) Assert.LessOrEqual(hard, DifficultyCurve.TargetCasualWinRate(h + 1) - 0.15, "relief " + h);
            }
            Assert.GreaterOrEqual(DifficultyCurve.TargetCasualWinRate(19), 0.35);
            Assert.LessOrEqual(DifficultyCurve.TargetCasualWinRate(19), 0.55);
        }
    }
}
