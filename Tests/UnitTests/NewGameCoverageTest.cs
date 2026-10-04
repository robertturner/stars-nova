namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Server.NewGame;

    /// <summary>
    /// Spec-driven coverage of behavior-specs-10/new-game-setup.md coverage row 42: the planet
    /// environment roll (population-growth.md section 2 correction, new-game-setup.md section 3):
    /// two axes are 1 + U(0..89) + U(0..9) (a trapezoid over 1-99) and the radiation axis is
    /// 1 + U(0..98) (uniform over 1-99); the original values are the rolled ones.
    /// </summary>
    /// <remarks>
    /// The order in which the three axes are drawn is not stated, so the scripted Random answers
    /// by the requested range, not by position in the draw sequence.
    /// </remarks>
    [TestFixture]
    public class NewGameCoverageTest
    {
        /// <summary>Answers Next(min, max) / Next(max) from a function of the range, and records
        /// every range asked for.</summary>
        private class RangeScriptedRandom : Random
        {
            private readonly Func<int, int, int> answer;

            public RangeScriptedRandom(Func<int, int, int> answer)
            {
                this.answer = answer;
            }

            public List<(int Min, int Max)> Ranges { get; } = new List<(int, int)>();

            public override int Next(int minValue, int maxValue)
            {
                Ranges.Add((minValue, maxValue));
                return answer(minValue, maxValue);
            }

            public override int Next(int maxValue)
            {
                return Next(0, maxValue);
            }

            public override int Next()
            {
                return Next(0, int.MaxValue);
            }

            public override double NextDouble()
            {
                throw new AssertionException("the environment roll uses bounded integer draws only");
            }
        }

        [Test]
        public void Row42_TheRollAsksForU0to89PlusU0to9OnTwoAxes_AndU0to98OnTheThird()
        {
            RangeScriptedRandom random = new RangeScriptedRandom((min, max) => min);
            StarMapinitializer.RollEnvironment(new Star(), random);

            // Exclusive upper bounds: U(0..89) is Next(0, 90), U(0..9) Next(0, 10), U(0..98) Next(0, 99).
            CollectionAssert.AreEquivalent(
                new[] { (0, 90), (0, 90), (0, 10), (0, 10), (0, 99) },
                random.Ranges);
        }

        [Test]
        public void Row42_GravityAndTemperature_AreOnePlusBothDraws_RadiationOnePlusItsDraw()
        {
            // U(0..89) answers 37, U(0..9) answers 6, U(0..98) answers 70.
            RangeScriptedRandom random = new RangeScriptedRandom((min, max) => max == 90 ? 37 : max == 10 ? 6 : 70);
            Star star = new Star();

            StarMapinitializer.RollEnvironment(star, random);

            Assert.AreEqual(1 + 37 + 6, star.Gravity);
            Assert.AreEqual(1 + 37 + 6, star.Temperature);
            Assert.AreEqual(1 + 70, star.Radiation, "radiation is the uniform axis");
            Assert.AreEqual(star.Gravity, star.OriginalGravity);
            Assert.AreEqual(star.Temperature, star.OriginalTemperature);
            Assert.AreEqual(star.Radiation, star.OriginalRadiation);
        }

        [Test]
        public void Row42_EveryAxisSpansExactlyOneTo99()
        {
            Star lowest = new Star();
            StarMapinitializer.RollEnvironment(lowest, new RangeScriptedRandom((min, max) => min));
            Assert.AreEqual((1, 1, 1), (lowest.Gravity, lowest.Temperature, lowest.Radiation), "every draw at its minimum");

            Star highest = new Star();
            StarMapinitializer.RollEnvironment(highest, new RangeScriptedRandom((min, max) => max - 1));
            Assert.AreEqual((99, 99, 99), (highest.Gravity, highest.Temperature, highest.Radiation), "1 + 89 + 9 and 1 + 98");
        }

        [Test]
        public void Row42_TheTwoTrapezoidAxesDrawIndependently()
        {
            // Successive U(0..89) draws answer 10 then 80, successive U(0..9) draws 1 then 8: the
            // two trapezoid axes take different values, each 1 + one U(0..89) + one U(0..9).
            Queue<int> big = new Queue<int>(new[] { 10, 80 });
            Queue<int> small = new Queue<int>(new[] { 1, 8 });
            RangeScriptedRandom random = new RangeScriptedRandom((min, max) => max == 90 ? big.Dequeue() : max == 10 ? small.Dequeue() : 0);
            Star star = new Star();

            StarMapinitializer.RollEnvironment(star, random);

            CollectionAssert.AreEquivalent(new[] { 1 + 10 + 1, 1 + 80 + 8 }, new[] { star.Gravity, star.Temperature });
            Assert.AreEqual(1, star.Radiation);
        }

        [Test]
        public void Row42_TheTrapezoidShape_EveryValueCount_ForTheTwoTrapezoidAxes()
        {
            // Enumerate all 90 x 10 equally likely draw pairs: values 1-9 and 91-99 sit on the
            // ramps (k and 100 - k ways), 10-90 on the 10-way plateau.
            int[] ways = new int[101];
            for (int a = 0; a < 90; a++)
            {
                for (int b = 0; b < 10; b++)
                {
                    int left = a;
                    int right = b;
                    Star star = new Star();
                    StarMapinitializer.RollEnvironment(star, new RangeScriptedRandom((min, max) => max == 90 ? left : max == 10 ? right : 0));
                    Assert.AreEqual(star.Gravity, star.Temperature);
                    ways[star.Gravity]++;
                }
            }

            Assert.AreEqual(0, ways[0]);
            Assert.AreEqual(0, ways[100]);
            Assert.AreEqual(1, ways[1]);
            Assert.AreEqual(9, ways[9]);
            Assert.IsTrue(Enumerable.Range(10, 81).All(v => ways[v] == 10), "the flat plateau 10-90");
            Assert.AreEqual(9, ways[91]);
            Assert.AreEqual(1, ways[99]);
        }
    }
}
