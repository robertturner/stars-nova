namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.NewGame;
    using Nova.Server.TurnSteps;

    using static Nova.Common.TechLevel;

    /// <summary>
    /// behavior-specs-10/race-traits.md coverage rows 2 (HE), 9 (IS), 20 (JOAT), 22 (IFE),
    /// 27 (MA), 28 (GR), 32 (NAS), 33 (LSP) and 42 (lesser-trait bit numbering). Rows 2, 28 and
    /// 33 are also covered numerically by PopulationGrowthCoverageTest / ResearchCoverageTest; the
    /// cases here pin the trait-specific numbers of section 2 / 3 directly.
    /// </summary>
    [TestFixture]
    public class RaceTraitsCoverageTest
    {
        // ---- Row 42: lesser-trait bit order, and the trait-name table it indexes ----

        [Test]
        public void TraitKeys_ArePrimaryTraitIndexThenLesserTraitBitOrder()
        {
            // Section 2a / ship-design section 5: PRT 0 HE, 1 SS, 2 WM, 3 CA, 4 IS, 5 SD, 6 PP,
            // 7 IT, 8 AR, 9 JOAT. Section 3: lesser bits 0 IFE, 1 TT, 2 ARM, 3 ISB, 4 GR, 5 UR, 6 MA,
            // 7 NRSE (Nova's key "NRS"), 8 CE, 9 OBRM, 10 NAS, 11 LSP, 12 BET, 13 RS.
            string[] expected =
            {
                "HE", "SS", "WM", "CA", "IS", "SD", "PP", "IT", "AR", "JOAT",
                "IFE", "TT", "ARM", "ISB", "GR", "UR", "MA", "NRS", "CE", "OBRM", "NAS", "LSP", "BET", "RS",
            };
            CollectionAssert.AreEqual(expected, AllTraits.TraitKeys);
            Assert.AreEqual(10, AllTraits.NumberOfPrimaryRacialTraits);
            Assert.AreEqual(14, AllTraits.NumberOfSecondaryRacialTraits);
        }

        [Test]
        public void TraitStringNamesTheTraitAtTheSameIndex()
        {
            // RaceRestriction.ToString pairs the two tables by index.
            for (int i = AllTraits.NumberOfPrimaryRacialTraits; i < AllTraits.TraitKeys.Length; i++)
            {
                TraitEntry entry = SecondaryTraits.Traits.First(t => t.Code == AllTraits.TraitKeys[i]);
                Assert.AreEqual(entry.Name, AllTraits.TraitString[i], AllTraits.TraitKeys[i]);
            }
        }

        [Test]
        public void ARobotBarredToOnlyBasicRemoteMining_SaysSo()
        {
            Component robot = new AllComponents().Fetch("Robo-Miner");
            Assume.That(robot.Restrictions.Availability("OBRM"), Is.EqualTo(RaceAvailability.not_available));
            string text = robot.Restrictions.ToString();
            StringAssert.Contains("Only Basic Remote Mining", text);
            StringAssert.DoesNotContain("Cheap Engines", text);
        }

        // ---- Row 2: Hyper Expansion ----

        [Test]
        public void HyperExpansion_DoublesTheGrowthRate_AndHalvesMaximumPopulation()
        {
            Race he = new Race();
            he.Traits.SetPrimary("HE");
            he.GrowthRate = 10;
            Race plain = new Race();
            plain.Traits.SetPrimary("IS");
            plain.GrowthRate = 20;

            Star star = new Star { Gravity = 50, Temperature = 50, Radiation = 50, Colonists = 10000 };
            Assert.AreEqual(plain.MaxPopulation / 2, he.MaxPopulation);

            // 100 units x 20% x 100% = 20 units, below a quarter of either capacity.
            Assert.AreEqual(2000, star.GrowthWithCarry(he, out int heCarry));
            Assert.AreEqual(2000, star.GrowthWithCarry(plain, out int plainCarry));
            Assert.AreEqual(0, heCarry + plainCarry);
        }

        // ---- Row 20: Jack of All Trades ----

        [Test]
        public void JackOfAllTrades_StartsAtTechThreeInEveryField_WithAFifthMoreMaximumPopulation()
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("JOAT");
            Gameinitializer.ProcessPrimaryTraits(empire);

            foreach (ResearchField field in System.Enum.GetValues(typeof(ResearchField)))
            {
                Assert.AreEqual(3, empire.ResearchLevels[field], field.ToString());
            }

            Assert.AreEqual(1200000, empire.Race.MaxPopulation);
        }

        // ---- Row 22: Improved Fuel Efficiency ----

        [Test]
        public void ImprovedFuelEfficiency_AddsOneStartingPropulsionLevel()
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("HE");
            empire.Race.Traits.Add("IFE");
            Gameinitializer.ProcessPrimaryTraits(empire);
            Gameinitializer.ProcessSecondaryTraits(empire);
            Assert.AreEqual(1, empire.ResearchLevels[ResearchField.Propulsion]);
        }

        private static ShipDesign FuelMizerDesign(int hullMass)
        {
            Component engine = new AllComponents().Fetch("Fuel Mizer");
            Component hullBlueprint = new Component { Name = "Test Hull", Mass = hullMass, Cost = new Resources(1, 1, 1, 1) };
            Hull hull = new Hull { FuelCapacity = 1400, Modules = new List<HullModule>() };
            hull.Modules.Add(new HullModule { CellNumber = 0, ComponentType = "Engine", ComponentMaximum = 1, ComponentCount = 1, AllocatedComponent = engine });
            hullBlueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = hullBlueprint, Name = "Mizer" };
            design.Update();
            return design;
        }

        /// <summary>
        /// fleet-movement-scanning-cargo.md section 2, step 2: IFE reduces the per-warp table value
        /// by 15% of itself, rounded down (Fuel Mizer at warp 8: 235 - 35 = 200), before the
        /// mass x value x distance / 20,000 rule. The spec's worked example: a 330 kT Privateer at
        /// warp 8 burns 330 / 200 x 2.00 = 3.3 mg per light-year.
        /// </summary>
        [Test]
        public void ImprovedFuelEfficiency_TakesTheTruncatedFifteenPercentOffTheTableValue()
        {
            ShipDesign design = FuelMizerDesign(330 - 6);
            Assume.That(design.Mass, Is.EqualTo(330));
            Assume.That(design.Engine.FuelConsumption[7], Is.EqualTo(235), "Fuel Mizer, warp 8 (component-stats.tsv)");

            Race ife = new Race();
            ife.Traits.SetPrimary("HE");
            ife.Traits.Add("IFE");
            Race plain = new Race();
            plain.Traits.SetPrimary("HE");

            double perYearLy = 8 * 8;
            Assert.AreEqual(330 * 200 / 100.0 / 200.0 * perYearLy, design.FuelConsumption(8, ife, 0), 1e-9);
            Assert.AreEqual(330 * 235 / 100.0 / 200.0 * perYearLy, design.FuelConsumption(8, plain, 0), 1e-9);
            Assert.AreEqual(3.3, design.FuelConsumption(8, ife, 0) / perYearLy, 1e-9, "the worked example's mg per light-year");
        }

        // ---- Row 27: Mineral Alchemy ----

        [Test]
        public void MineralAlchemy_IsExactlyFourTimesAsEfficient()
        {
            Race ma = new Race();
            ma.Traits.SetPrimary("HE");
            ma.Traits.Add("MA");
            Race plain = new Race();
            plain.Traits.SetPrimary("HE");

            Assert.AreEqual(100, new AlchemyProductionUnit(plain).ResourcesPerUnit);
            Assert.AreEqual(25, new AlchemyProductionUnit(ma).ResourcesPerUnit);
        }

        // ---- Row 9: Inner Strength (and the War Monger counterpart) ----

        private static Resources WeaponCost(string primaryTrait)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primaryTrait);
            Component hullBlueprint = new Component { Name = "Test Hull", Mass = 10, Cost = new Resources(0, 0, 0, 0) };
            Hull hull = new Hull { FuelCapacity = 100, Modules = new List<HullModule>() };
            Component laser = new Component { Name = "Test Beam", Mass = 1, Cost = new Resources(8, 4, 12, 100), Type = ItemType.BeamWeapons };
            hull.Modules.Add(new HullModule { CellNumber = 0, ComponentType = "Weapon", ComponentMaximum = 1, ComponentCount = 1, AllocatedComponent = laser });
            hullBlueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = hullBlueprint, Name = "Gun" };
            design.Update(race, null);
            return design.Cost;
        }

        [Test]
        public void InnerStrength_WeaponsCostAQuarterMore_WarMongerAQuarterLess()
        {
            Resources plain = WeaponCost("HE");
            Resources inner = WeaponCost("IS");
            Resources monger = WeaponCost("WM");

            Assert.AreEqual(100, plain.Energy);
            Assert.AreEqual(125, inner.Energy);
            Assert.AreEqual(10, inner.Ironium);
            Assert.AreEqual(5, inner.Boranium);
            Assert.AreEqual(15, inner.Germanium);
            Assert.AreEqual(75, monger.Energy);
            Assert.AreEqual(6, monger.Ironium);
            Assert.AreEqual(3, monger.Boranium);
            Assert.AreEqual(9, monger.Germanium);
        }

        private static (Star star, EmpireData attacker, EmpireData defender, Fleet fleet) Invasion(string attackerTrait, string defenderTrait, int troopKilotons)
        {
            EmpireData attacker = new EmpireData { Id = 1 };
            attacker.Race = new Race();
            attacker.Race.Traits.SetPrimary(attackerTrait);
            EmpireData defender = new EmpireData { Id = 2 };
            defender.Race = new Race();
            defender.Race.Traits.SetPrimary(defenderTrait);
            attacker.EmpireReports.Add(defender.Id, new EmpireIntel(defender) { Relation = PlayerRelation.Enemy });

            Star star = new Star { Name = "Target", Owner = defender.Id, Colonists = 10000 };
            defender.OwnedStars.Add(star);
            Fleet fleet = new Fleet(1) { Owner = attacker.Id, InOrbit = star };
            fleet.Cargo.ColonistsInKilotons = troopKilotons;
            return (star, attacker, defender, fleet);
        }

        /// <summary>fleet-movement-scanning-cargo.md, colonist unload outcomes: invasion strength
        /// 110% (War Monger 165%) against the population, doubled for an Inner Strength
        /// defender. 100 kT = 10,000 troops = 11,000 strength against 10,000 defenders.</summary>
        [TestCase("HE", "HE", 100, true)]
        [TestCase("HE", "IS", 100, false)]
        [TestCase("HE", "IS", 190, true)]
        [TestCase("HE", "HE", 90, false)]
        [TestCase("WM", "HE", 70, true)]
        [TestCase("HE", "HE", 70, false)]
        [TestCase("WM", "IS", 120, false)]
        [TestCase("WM", "IS", 130, true)]
        public void InvasionStrengths(string attackerTrait, string defenderTrait, int troopKilotons, bool captured)
        {
            (Star star, EmpireData attacker, EmpireData defender, Fleet fleet) = Invasion(attackerTrait, defenderTrait, troopKilotons);
            using (GameRandom.Use(new System.Random(1)))
            {
                new InvadeTask().Invade(fleet, star, attacker, defender, troopKilotons);
            }

            Assert.AreEqual(captured ? attacker.Id : defender.Id, star.Owner);
        }

        // ---- Row 32: No Advanced Scanners doubles a planetary scanner too ----

        [Test]
        public void NoAdvancedScanners_DoublesTheRangeOfANewPlanetaryScanner()
        {
            Assert.AreEqual(90, PlanetaryScanRangeAfterElectronicsOne(false), "Viewer 90 reached at Electronics 1");
            Assert.AreEqual(180, PlanetaryScanRangeAfterElectronicsOne(true));
        }

        private static int PlanetaryScanRangeAfterElectronicsOne(bool nas)
        {
            ServerData server = new ServerData();
            EmpireData empire = new SimpleEmpireData { Id = 1 };
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary("HE");
            if (nas)
            {
                empire.Race.Traits.Add("NAS");
            }

            foreach (ResearchField field in System.Enum.GetValues(typeof(ResearchField)))
            {
                empire.Race.ResearchCosts[field] = 100;
            }

            empire.AvailableComponents = new RaceComponents();
            empire.ResearchLevels = new TechLevel(0);
            empire.ResearchTopics = new TechLevel();
            empire.ResearchTopics[ResearchField.Electronics] = 1;
            server.AllEmpires.Add(1, empire);

            Star star = new Star { Name = "Home", Owner = 1, ScannerType = "Viewer 50", ScanRange = 50 };
            empire.OwnedStars.Add(star);

            StarUpdateStep step = new StarUpdateStep();
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(step, server);
            typeof(StarUpdateStep).GetMethod("ContributeResearch", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(step, new object[] { star, 50 });

            Assume.That(empire.ResearchLevels[ResearchField.Electronics], Is.EqualTo(1));
            return star.ScanRange;
        }
    }
}
