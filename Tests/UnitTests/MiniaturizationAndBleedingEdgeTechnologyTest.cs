namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.Waypoints;

    // Miniaturization and Bleeding Edge Technology, exactly as behavior-specs-10/race-traits.md
    // section 7 reads the shared item-cost routine FUN_1050_7d44 from the raw bytes:
    // - every item is discounted, hulls and starbase chassis included (only Terraforming and the
    //   planetary scanners/defences are not);
    // - the margin is the smallest (level - requirement) over the fields with a non-zero
    //   requirement, and an item with NO requirement uses the race's LOWEST tech level;
    // - 4% per level capped at 75% (5% / 80% with BET), margin capped at 19;
    // - each non-zero cost field loses MulDiv(cost, pct, 100) - rounded to nearest, halves away
    //   from zero - and never drops below 1;
    // - the WM/IS/IT/CE trait adjustments act on the DISCOUNTED figure;
    // - BET doubling comes last, only when the margin is 0 or less and the item has a
    //   requirement, and never for Scrap Fleet recovery.
    // Both hook into ShipDesign.Update's (Race, TechLevel) overload.
    [TestFixture]
    public class MiniaturizationAndBleedingEdgeTechnologyTest
    {
        private static ShipDesign BuildDesign(Race race, TechLevel currentTechLevels, Component part, Resources hullCost = null, TechLevel hullRequiredTech = null)
        {
            Component blueprint = new Component { Mass = 100 };
            if (hullCost != null)
            {
                blueprint.Cost = hullCost;
            }

            if (hullRequiredTech != null)
            {
                blueprint.RequiredTech = hullRequiredTech;
            }

            Hull hull = new Hull { Modules = new List<HullModule>() };
            if (part != null)
            {
                hull.Modules.Add(new HullModule { AllocatedComponent = part, ComponentCount = 1 });
            }

            blueprint.Properties.Add("Hull", hull);

            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint };
            design.Update(race, currentTechLevels);
            return design;
        }

        private static ShipDesign BuildDesignWithOneComponent(Race race, TechLevel currentTechLevels, TechLevel requiredTech, Resources componentCost)
        {
            return BuildDesign(race, currentTechLevels, new Component { Cost = componentCost, RequiredTech = requiredTech });
        }

        private static Race BetRace()
        {
            Race race = new Race();
            race.Traits.Add("BET");
            return race;
        }

        [Test]
        public void Miniaturization_DiscountsComponentCost_ByFourPercentPerLevelOfSurplus()
        {
            Race race = new Race();
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0); // requires Energy 5
            TechLevel currentTechLevels = new TechLevel(0, 0, 10, 0, 0, 0); // Energy 10 -> surplus 5

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(800, design.Summary.Cost.Energy, "5 levels surplus * 4% = 20% off 1000 -> 800");
        }

        [Test]
        public void Miniaturization_TakesTheMinimumSurplusAcrossAllRequiredFields()
        {
            Race race = new Race();
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 5, 0); // requires Energy 5 AND Weapons 5
            // Energy surplus 5, Weapons surplus 1 - the smaller (1) must govern the discount.
            TechLevel currentTechLevels = new TechLevel(0, 0, 10, 0, 6, 0);

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(960, design.Summary.Cost.Energy, "Minimum surplus (1) * 4% = 4% off 1000 -> 960");
        }

        [Test]
        public void Miniaturization_CapsAtSeventyFivePercentOff()
        {
            Race race = new Race();
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(0, 0, 35, 0, 0, 0); // surplus 30, way past the 19-level clamp

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(250, design.Summary.Cost.Energy, "Discount caps at 75% off, not 30*4%=120% off");
        }

        [Test]
        public void Miniaturization_IgnoresFieldsTheComponentDoesNotRequire()
        {
            Race race = new Race();
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0); // only requires Energy
            // Weapons is far behind, but the component doesn't require Weapons at all, so it must
            // not drag the surplus down to a negative/zero value the way TakesTheMinimum... would
            // if Weapons were actually a requirement.
            TechLevel currentTechLevels = new TechLevel(0, 0, 10, 0, 0, 0);

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(800, design.Summary.Cost.Energy, "Only Energy's surplus (5 -> 20% off) matters; Weapons being at 0 must not affect this component");
        }

        [Test]
        public void NoTechRequirement_UsesTheRacesLowestTechLevelAsItsMargin()
        {
            // Spec-10 corrects the earlier "untouched" reading: an item with no requirement at all
            // is discounted by the race's LOWEST level (here Biotechnology 3 -> 12%).
            Race race = new Race();
            TechLevel noRequirement = new TechLevel(0, 0, 0, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(3, 9, 9, 9, 9, 9);

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, noRequirement, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(880, design.Summary.Cost.Energy);
        }

        [Test]
        public void NoTechRequirement_IsNeverDoubledByBleedingEdge_EvenAtLevelZero()
        {
            TechLevel noRequirement = new TechLevel(0, 0, 0, 0, 0, 0);

            ShipDesign atZero = BuildDesignWithOneComponent(BetRace(), new TechLevel(0, 0, 0, 0, 0, 0), noRequirement, new Resources(0, 0, 0, 1000));
            ShipDesign atFour = BuildDesignWithOneComponent(BetRace(), new TechLevel(4, 4, 4, 4, 4, 4), noRequirement, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(1000, atZero.Summary.Cost.Energy, "Margin 0: no discount, and no doubling without a requirement");
            Assert.AreEqual(800, atFour.Summary.Cost.Energy, "BET rate: lowest level 4 x 5% = 20% off");
        }

        [Test]
        public void Miniaturization_RoundsEachFieldToNearest_NotUp()
        {
            // The spec's own example: 20 resources 6 levels ahead loses MulDiv(20, 24, 100) = 5
            // (4.8 rounded), leaving 15.
            Race race = new Race();
            ShipDesign design = BuildDesignWithOneComponent(race, new TechLevel(0, 0, 11, 0, 0, 0), new TechLevel(0, 0, 5, 0, 0, 0), new Resources(0, 0, 0, 20));

            Assert.AreEqual(15, design.Summary.Cost.Energy);
        }

        [Test]
        public void Miniaturization_RoundsHalvesAwayFromZero()
        {
            // BET, 5 levels ahead: 25% of 10 is 2.5, which MulDiv rounds to 3.
            ShipDesign design = BuildDesignWithOneComponent(BetRace(), new TechLevel(0, 0, 10, 0, 0, 0), new TechLevel(0, 0, 5, 0, 0, 0), new Resources(10, 0, 0, 10));

            Assert.AreEqual(7, design.Summary.Cost.Energy);
            Assert.AreEqual(7, design.Summary.Cost.Ironium);
        }

        [Test]
        public void Miniaturization_LeavesAtLeastOneOfEveryNonZeroField_AndZeroFieldsAtZero()
        {
            // 75% off: 1 Ironium would lose MulDiv(1, 75, 100) = 1 -> 0, held at 1; 3 resources lose
            // 2 (2.25 rounded) -> 1; the zero Boranium/Germanium stay 0.
            Race race = new Race();
            ShipDesign design = BuildDesignWithOneComponent(race, new TechLevel(0, 0, 30, 0, 0, 0), new TechLevel(0, 0, 5, 0, 0, 0), new Resources(1, 0, 0, 3));

            Assert.AreEqual(1, design.Summary.Cost.Ironium);
            Assert.AreEqual(0, design.Summary.Cost.Boranium);
            Assert.AreEqual(0, design.Summary.Cost.Germanium);
            Assert.AreEqual(1, design.Summary.Cost.Energy);
        }

        [Test]
        public void HullCost_IsMiniaturizedLikeAnyPart()
        {
            Race race = new Race();
            ShipDesign design = BuildDesign(race, new TechLevel(0, 0, 0, 0, 0, 10), null, new Resources(40, 0, 10, 1000), new TechLevel(0, 0, 0, 0, 0, 5));

            Assert.AreEqual(800, design.Summary.Cost.Energy, "Construction 10 vs 5: 20% off the hull too");
            Assert.AreEqual(32, design.Summary.Cost.Ironium);
            Assert.AreEqual(8, design.Summary.Cost.Germanium);
        }

        [Test]
        public void HullCost_IsDoubledByBleedingEdge_WhileAtItsRequirement()
        {
            ShipDesign design = BuildDesign(BetRace(), new TechLevel(0, 0, 0, 0, 0, 5), null, new Resources(0, 0, 0, 1000), new TechLevel(0, 0, 0, 0, 0, 5));

            Assert.AreEqual(2000, design.Summary.Cost.Energy);
        }

        [Test]
        public void TraitAdjustments_ActOnTheDiscountedFigure()
        {
            // War Monger beam, 10 resources, 6 levels ahead (24%): miniaturize first, 10 - 2 = 8,
            // then -25% -> 6. (The old order, -25% first then 24%, gave 7.)
            Race race = new Race();
            race.Traits.SetPrimary("WM");
            Component beam = new Component { Type = ItemType.BeamWeapons, Cost = new Resources(0, 0, 0, 10), RequiredTech = new TechLevel(0, 0, 0, 0, 5, 0) };

            ShipDesign design = BuildDesign(race, new TechLevel(0, 0, 0, 0, 11, 0), beam);

            Assert.AreEqual(6, design.Summary.Cost.Energy);
        }

        [Test]
        public void BleedingEdgeDoubling_ComesLast_AfterTheTraitAdjustments()
        {
            // Cheap Engines + BET, an engine at its requirement: 15 halved to 8 (the value less half
            // of itself, rounded down), THEN doubled to 16 - not doubled to 30 and halved to 15.
            Race race = BetRace();
            race.Traits.Add("CE");
            Component engine = new Component { Cost = new Resources(0, 0, 0, 15), RequiredTech = new TechLevel(0, 0, 0, 3, 0, 0) };
            engine.Properties.Add("Engine", new Engine());

            ShipDesign design = BuildDesign(race, new TechLevel(0, 0, 0, 3, 0, 0), engine);

            Assert.AreEqual(16, design.Summary.Cost.Energy);
        }

        [Test]
        public void BleedingEdgeTechnology_DoublesCost_WhileNotYetAheadOnTheRequiredField()
        {
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(0, 0, 5, 0, 0, 0); // exactly at requirement, surplus 0

            ShipDesign design = BuildDesignWithOneComponent(BetRace(), currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(2000, design.Summary.Cost.Energy, "BET pays double until strictly ahead of every requirement");
        }

        [Test]
        public void BleedingEdgeTechnology_StillDoubles_IfBehindOnAnyOneOfSeveralRequiredFields()
        {
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 5, 0); // requires Energy 5 AND Weapons 5
            // Well ahead on Energy (surplus 5), but only exactly AT the Weapons requirement
            // (surplus 0) - the minimum across both fields is what must govern doubling.
            TechLevel currentTechLevels = new TechLevel(0, 0, 10, 0, 5, 0);

            ShipDesign design = BuildDesignWithOneComponent(BetRace(), currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(2000, design.Summary.Cost.Energy, "Being ahead on Energy doesn't help if still behind on Weapons");
        }

        [Test]
        public void BleedingEdgeTechnology_AppliesASteeperFivePercentDiscount_OnceAheadOnEveryRequiredField()
        {
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(0, 0, 6, 0, 0, 0); // surplus 1

            ShipDesign design = BuildDesignWithOneComponent(BetRace(), currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(950, design.Summary.Cost.Energy, "BET's own discount rate is 5%/level, not the normal 4%/level, once ahead");
        }

        [Test]
        public void BleedingEdgeTechnology_CapsAtEightyPercentOff()
        {
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(0, 0, 35, 0, 0, 0); // surplus 30

            ShipDesign design = BuildDesignWithOneComponent(BetRace(), currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(200, design.Summary.Cost.Energy, "BET's discount caps at 80% off, not 30*5%=150% off");
        }

        [Test]
        public void NonBETRace_NeverDoubles_EvenWhileBehindOnPrerequisites()
        {
            Race race = new Race();
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);
            TechLevel currentTechLevels = new TechLevel(0, 0, 5, 0, 0, 0); // surplus 0, same as the BET-doubling case above

            ShipDesign design = BuildDesignWithOneComponent(race, currentTechLevels, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(1000, design.Summary.Cost.Energy, "A non-BET race just pays the plain, undiscounted cost when at (or behind) the requirement - never doubled");
        }

        [Test]
        public void PassingNullCurrentTechLevels_SkipsBothMechanicsEntirely()
        {
            // The overload contract: null currentTechLevels (e.g. for an enemy design scanned from
            // another empire, whose live tech levels aren't tracked here) must skip Miniaturization/
            // BET cleanly rather than throwing, leaving every other race-based modifier intact.
            TechLevel requiredTech = new TechLevel(0, 0, 5, 0, 0, 0);

            ShipDesign design = BuildDesignWithOneComponent(BetRace(), null, requiredTech, new Resources(0, 0, 0, 1000));

            Assert.AreEqual(1000, design.Summary.Cost.Energy);
        }

        [Test]
        public void CostWithoutBleedingEdgeDoubling_IsTheMiniaturizedCostLessTheDoubling()
        {
            // A doubled part (at its requirement) next to a miniaturized hull: the scrap figure keeps
            // the hull's discount but drops the part's doubling.
            Component part = new Component { Cost = new Resources(0, 0, 0, 1000), RequiredTech = new TechLevel(0, 0, 5, 0, 0, 0) };
            ShipDesign design = BuildDesign(BetRace(), new TechLevel(0, 0, 5, 0, 0, 9), part, new Resources(0, 0, 0, 100), new TechLevel(0, 0, 0, 0, 0, 5));

            Assert.AreEqual(80 + 2000, design.Cost.Energy, "Hull 4 levels ahead at 5% = 20% off; part doubled");
            Assert.AreEqual(80 + 1000, design.CostWithoutBleedingEdgeDoubling.Energy);
        }

        [Test]
        public void ScrapFleetCost_DoesNotIncludeBleedingEdgeDoubling()
        {
            ShipDesign design = BuildDesignWithOneComponent(BetRace(), new TechLevel(0, 0, 5, 0, 0, 0), new TechLevel(0, 0, 5, 0, 0, 0), new Resources(0, 0, 0, 1000));
            Fleet fleet = new Fleet(design, 3, new Star(), 1);

            Assert.AreEqual(2000, design.Cost.Energy, "Production pays double");
            Assert.AreEqual(3000, ScrapTask.FleetCost(fleet).Energy, "Scrap is valued at the undoubled cost");
        }
    }
}
