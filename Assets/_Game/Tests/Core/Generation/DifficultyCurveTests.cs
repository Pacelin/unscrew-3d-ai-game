using System;
using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class DifficultyCurveTests
    {
        private readonly DifficultyCurve _curve = new DifficultyCurve();

        private static LevelDefinition Sized(int number, int bolts = 90, int colors = 9, int parts = 40)
        {
            var owners = new int[bolts];
            for (int b = 0; b < bolts; b++) owners[b] = b % parts;
            var palette = new int[colors];
            for (int i = 0; i < colors; i++) palette[i] = i;
            var def = TestLevels.Build(owners, null, palette);
            def.number = number;
            return def;
        }

        [Test]
        public void Tutorial_IsEasiest()
        {
            var s = _curve.Get(Sized(1, 9, 2, 4));
            Assert.AreEqual(0, s.mixWindow);
            Assert.IsFalse(s.isHard);
            Assert.AreEqual(2, s.colorCount);
            Assert.AreEqual(3, s.boxCapacity);
            Assert.GreaterOrEqual(s.bufferSlots, DifficultyCurve.BufferSlots);
        }

        [Test]
        public void EveryFifthLevel_IsHard()
        {
            for (int n = 1; n <= 20; n++)
                Assert.AreEqual(n % 5 == 0, _curve.Get(Sized(n)).isHard, "level " + n);
        }

        [Test]
        public void NormalLevels_AreMonotonic_AndHardLevelsAreSpikes()
        {
            DifficultySettings prev = null;
            for (int n = 2; n <= 20; n++)
            {
                var s = _curve.Get(Sized(n));
                if (s.isHard) continue;
                if (prev != null)
                {
                    Assert.GreaterOrEqual(s.mixWindow, prev.mixWindow, "mix at level " + n);
                    Assert.LessOrEqual(s.bufferSlots, prev.bufferSlots, "buffer at level " + n);
                }
                prev = s;
            }

            for (int n = 5; n <= 20; n += 5)
            {
                var hard = _curve.Get(Sized(n));
                var before = _curve.Get(Sized(n - 1));
                Assert.Less(hard.bufferSlots, before.bufferSlots, "hard buffer " + n);
                Assert.Greater(hard.mixWindow, before.mixWindow, "hard mix " + n);
                if (n < 20)
                {
                    var after = _curve.Get(Sized(n + 1));
                    Assert.GreaterOrEqual(hard.mixWindow, after.mixWindow);
                    Assert.Less(hard.bufferSlots, after.bufferSlots);
                }
            }
        }

        [Test]
        public void LastLevel_IsHarderThanSecond()
        {
            var a = _curve.Get(Sized(2));
            var b = _curve.Get(Sized(20));
            Assert.Greater(b.mixWindow, a.mixWindow);
            Assert.Less(b.bufferSlots, a.bufferSlots);
        }

        [Test]
        public void ColorCount_UsesAllowedColors_ButNotMoreThanBoxes()
        {
            Assert.AreEqual(9, _curve.Get(Sized(8, 90, 9)).colorCount);
            Assert.AreEqual(2, _curve.Get(Sized(3, 6, 5, 2)).colorCount, "only 2 boxes");
        }

        [Test]
        public void MixWindow_IsClampedBySize()
        {
            var small = _curve.Get(Sized(20, 9, 3, 3)); // 3 boxes
            Assert.LessOrEqual(small.mixWindow, 1);
        }

        [Test]
        public void Seed_IsDeterministic_AndDependsOnNumberAndSize()
        {
            Assert.AreEqual(_curve.Get(Sized(7)).seed, _curve.Get(Sized(7)).seed);
            Assert.AreNotEqual(_curve.Get(Sized(7)).seed, _curve.Get(Sized(8)).seed);
            Assert.AreNotEqual(_curve.Get(Sized(7, 90)).seed, _curve.Get(Sized(7, 93)).seed);
        }

        [TestCase(21, 11)]
        [TestCase(25, 15)]
        [TestCase(30, 20)]
        [TestCase(31, 11)]
        [TestCase(45, 15)]
        [TestCase(0, 1)]
        public void Endless_LoopsLevels11To20(int number, int effective)
        {
            Assert.AreEqual(effective, DifficultyCurve.EffectiveLevel(number));
        }

        [Test]
        public void Endless_RepeatsDifficultyWithNewSeed()
        {
            var a = _curve.Get(Sized(15));
            var b = _curve.Get(Sized(25));
            Assert.AreEqual(a.mixWindow, b.mixWindow);
            Assert.AreEqual(a.bufferSlots, b.bufferSlots);
            Assert.IsTrue(b.isHard);
            Assert.AreNotEqual(a.seed, b.seed);
        }

        [Test]
        public void InvalidDefinition_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => _curve.Get(null));
            var def = Sized(3);
            def.allowedColors = new int[0];
            Assert.Throws<ArgumentException>(() => _curve.Get(def));
        }
    }
}
