using NUnit.Framework;

namespace Unscrew.Core.Tests
{
    public class BoardRulesTests
    {
        [Test]
        public void BlockedBolt_BecomesAccessible_WhenBlockingPartReleased()
        {
            var def = TestLevels.TwoLayers();
            var removed = new bool[def.bolts.Length];

            Assert.IsTrue(BoardRules.IsAccessible(def, removed, 0));
            Assert.IsFalse(BoardRules.IsAccessible(def, removed, 3));

            removed[0] = removed[1] = removed[2] = true;
            Assert.IsTrue(BoardRules.IsPartReleased(def, removed, 0));
            Assert.IsTrue(BoardRules.IsAccessible(def, removed, 3));
            Assert.IsFalse(BoardRules.IsAccessible(def, removed, 0), "removed bolt is not accessible");
        }

        [Test]
        public void Rng_IsDeterministic()
        {
            var a = new Rng(42);
            var b = new Rng(42);
            for (int i = 0; i < 100; i++) Assert.AreEqual(a.Range(0, 1000), b.Range(0, 1000));
        }
    }
}
