namespace Nova.Tests.UnitTests
{
    using NUnit.Framework;

    using Nova.Common;

    // Regression tests for two related population-capacity bugs behavior-specs-7/
    // population-growth.md's own decompile trace confirms: (1) a planet's population capacity
    // is the race's nominal max population SCALED by habitability (a 50%-habitability world
    // supports only half as many colonists as a 100%-habitability one), not the same flat
    // maximum regardless of habitability; (2) the game's own +10% population-capacity bonus is
    // gated on Inner Strength (bit 9 of the generic per-race trait-bitmask accessor), a finding
    // independently cross-confirmed by ship-design-and-components.md's own bit-9 trace via the
    // same race's documented 5-bomb build exclusion - not Only Basic Remote Mining, which this
    // codebase's MaxPopulation previously (incorrectly) gated the bonus on. race-traits.md's own,
    // less rigorous audit pass had attributed this same +10% to OBRM instead, matching the
    // pre-existing (buggy) code rather than independently re-deriving it; population-growth.md's
    // direct bit-level trace, cross-checked twice against an unrelated spec section, is treated
    // as the more authoritative source here.
    [TestFixture]
    public class PopulationCapacityTest
    {
        [Test]
        public void Capacity_IsScaledByHabitability_NotJustRaceMaxPopulation()
        {
            Star star = new Star();
            Race race = new Race();
            race.Traits.SetPrimary("SS"); // avoid the JoAT/HE/IS max-population complications

            // Min=0/Max=100 on every axis, star value 25 -> clicksFromCenter=25, edge-to-center=50,
            // normalized distance 0.5 on all three axes -> HabValue = sqrt(0.75)/sqrt(3) = 0.5 exactly.
            race.GravityTolerance.MinimumValue = 0;
            race.GravityTolerance.MaximumValue = 100;
            race.RadiationTolerance.MinimumValue = 0;
            race.RadiationTolerance.MaximumValue = 100;
            race.TemperatureTolerance.MinimumValue = 0;
            race.TemperatureTolerance.MaximumValue = 100;
            star.Gravity = 25;
            star.Radiation = 25;
            star.Temperature = 25;

            Assume.That(race.HabValue(star), Is.EqualTo(0.5), "Sanity check - this star/race combination should be exactly 50% habitable.");

            star.Colonists = 250000;

            // A 50%-habitable world's capacity is 500,000 (half of the 1,000,000 nominal), so
            // 250,000 colonists is 50% of ITS capacity - not 25%, which is what the pre-fix flat
            // race.MaxPopulation would have given regardless of this star's habitability.
            Assert.AreEqual(50, star.Capacity(race));
        }

        [Test]
        public void Capacity_AtSameColonistCount_DiffersByHabitability()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS");

            Star fullyHabitableStar = new Star { Gravity = 50, Radiation = 50, Temperature = 50, Colonists = 250000 };
            Star halfHabitableStar = new Star { Gravity = 25, Radiation = 25, Temperature = 25, Colonists = 250000 };

            race.GravityTolerance.MinimumValue = 0;
            race.GravityTolerance.MaximumValue = 100;
            race.RadiationTolerance.MinimumValue = 0;
            race.RadiationTolerance.MaximumValue = 100;
            race.TemperatureTolerance.MinimumValue = 0;
            race.TemperatureTolerance.MaximumValue = 100;

            // Same colonist count, but the less-habitable star has a smaller capacity to divide
            // by, so it must show as a HIGHER percentage-of-capacity, not an identical one.
            Assert.Greater(halfHabitableStar.Capacity(race), fullyHabitableStar.Capacity(race));
        }

        // behavior-specs-9/population-growth.md section 3 withdraws spec-8's "Inner Strength" reading of
        // the +10%: the accessor reads the lesser-trait word and bit 9 is Only Basic Remote Mining;
        // Inner Strength is a primary trait with no population bonus.
        [Test]
        public void MaxPopulation_OnlyBasicRemoteMining_Grants10PercentBonus()
        {
            Race race = new Race();
            race.Traits.SetPrimary("SS"); // avoid JOAT's own +20%, the default PRT if unset
            race.Traits.Add("OBRM");

            Assert.AreEqual(1100000, race.MaxPopulation);
        }

        [Test]
        public void MaxPopulation_InnerStrength_HasNoPopulationBonus()
        {
            Race race = new Race();
            race.Traits.SetPrimary("IS");

            Assert.AreEqual(1000000, race.MaxPopulation);
        }
    }
}
