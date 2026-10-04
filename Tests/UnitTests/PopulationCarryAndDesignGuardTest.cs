namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;

    /// <summary>
    /// behavior-specs-10/population-growth.md section 3 (coverage rows 14 and 15: the decline
    /// floor and the deterministic fractional-carry byte) and row 30 (Alternate Reality's
    /// protected starbase designs).
    /// </summary>
    [TestFixture]
    public class PopulationCarryAndDesignGuardTest
    {
        private static Race NewRace()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            race.GrowthRate = 15;
            return race;
        }

        private static Star IdealStar(Race race, int colonists)
        {
            Star star = new Star();
            star.ThisRace = race;
            star.Gravity = race.GravityTolerance.OptimumLevel;
            star.Temperature = race.TemperatureTolerance.OptimumLevel;
            star.Radiation = race.RadiationTolerance.OptimumLevel;
            star.Colonists = colonists;
            return star;
        }

        // ---- Fractional carry (rows 14, 15) ----

        [Test]
        public void Growth_WholeUnitsArePaid_AndTheRemainderIsCarried()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 25000); // 2% of capacity: no crowding

            // 250 units x 15% = 37.5 units: 37 units now, 50 colonists carried.
            star.UpdatePopulation(race);

            Assert.AreEqual(28700, star.Colonists);
            Assert.AreEqual(50, star.PopulationCarry);
        }

        [Test]
        public void Growth_TheCarryOverflowsIntoAWholeUnit()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 25000);
            star.PopulationCarry = 60;

            star.UpdatePopulation(race);

            Assert.AreEqual(28800, star.Colonists, "3,750 + 60 carried = 38 whole units");
            Assert.AreEqual(10, star.PopulationCarry);
        }

        [Test]
        public void Decline_TheRemainderIsBorrowed_SoASmallDeclineStillTakesUnits()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 5000);
            star.Radiation = race.RadiationTolerance.MaximumValue + 1; // value -1%
            Assert.AreEqual(-1, race.HabPercent(star));

            // Raw decline 1 x 50 units / 10 = 5 hundredths: no whole unit, 5 borrowed from an
            // empty carry - one unit comes off and the carry is 95.
            star.UpdatePopulation(race);
            Assert.AreEqual(4900, star.Colonists);
            Assert.AreEqual(95, star.PopulationCarry);

            // At 49 units the decline is 4 hundredths a year: 23 years come out of the carry
            // (95 -> 3) without losing a unit, and the 24th borrows the next one.
            for (int year = 0; year < 23; year++)
            {
                star.UpdatePopulation(race);
            }

            Assert.AreEqual(4900, star.Colonists);
            Assert.AreEqual(3, star.PopulationCarry);

            star.UpdatePopulation(race);
            Assert.AreEqual(4800, star.Colonists);
            Assert.AreEqual(99, star.PopulationCarry);
        }

        [Test]
        public void Decline_HasAFloorOfOneHundredth()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 500);
            star.Radiation = race.RadiationTolerance.MaximumValue + 1;

            // 1 x 5 units / 10 = 0.5 hundredths, raised to the floor of 1.
            star.UpdatePopulation(race);

            Assert.AreEqual(400, star.Colonists);
            Assert.AreEqual(99, star.PopulationCarry);
        }

        [Test]
        public void PopulationCarry_SurvivesAnXmlRoundTrip()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 25000);
            star.Name = "Tierra";
            star.PopulationCarry = 42;

            Star copy = new Star(star.ToXml(new XmlDocument()));

            Assert.AreEqual(42, copy.PopulationCarry);
        }

        [Test]
        public void CalculateGrowth_TheWholeUnitPrediction_IsUnchanged()
        {
            Race race = NewRace();
            Star star = IdealStar(race, 25000);
            star.PopulationCarry = 99;

            Assert.AreEqual(3700, star.CalculateGrowth(race), "Compute-only: the carry is not applied");
        }

        // ---- Alternate Reality's protected starbase designs (row 30) ----

        private static ShipDesign StarbaseDesign(EmpireData empire, string name)
        {
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Type = ItemType.Starbase, Name = name };
            design.Blueprint = new Component();
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>() });
            empire.Designs.Add(design.Key, design);
            return design;
        }

        private static EmpireData Empire(string primaryTrait)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary(primaryTrait);
            return empire;
        }

        [Test]
        public void AlternateReality_StarterColony_CanBeNeitherDeletedNorEdited()
        {
            EmpireData empire = Empire("AR");
            ShipDesign starter = StarbaseDesign(empire, DesignCommand.StarterColonyDesignName);

            Assert.IsFalse(new DesignCommand(CommandMode.Delete, starter.Key).IsValid(empire));
            Assert.IsFalse(new DesignCommand(CommandMode.Edit, starter).IsValid(empire));
        }

        [Test]
        public void AlternateReality_AStarbaseDesignInUse_CannotBeDeleted_ButCanBeEdited()
        {
            EmpireData empire = Empire("AR");
            ShipDesign station = StarbaseDesign(empire, "Starbase");
            Star home = new Star { Name = "Home", Owner = 1 };
            Fleet starbase = new Fleet(new ShipToken(station, 1), home, empire.GetNextFleetKey());
            empire.AddOrUpdateFleet(starbase);

            Assert.IsFalse(new DesignCommand(CommandMode.Delete, station.Key).IsValid(empire));
            Assert.IsTrue(new DesignCommand(CommandMode.Edit, station).IsValid(empire));
        }

        [Test]
        public void AlternateReality_AnUnusedStarbaseDesign_CanBeDeleted()
        {
            EmpireData empire = Empire("AR");
            ShipDesign spare = StarbaseDesign(empire, "Spare");

            Assert.IsTrue(new DesignCommand(CommandMode.Delete, spare.Key).IsValid(empire));
        }

        [Test]
        public void OtherRaces_MayDeleteAStarbaseDesignInUse()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign station = StarbaseDesign(empire, "Starbase");
            Fleet starbase = new Fleet(new ShipToken(station, 1), new Star { Name = "Home", Owner = 1 }, empire.GetNextFleetKey());
            empire.AddOrUpdateFleet(starbase);

            Assert.IsTrue(new DesignCommand(CommandMode.Delete, station.Key).IsValid(empire));
        }
    }
}
