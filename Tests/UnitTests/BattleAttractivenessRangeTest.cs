namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Drawing;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    // Regression test for a real over-implementation: behavior-specs-7/combat-resolution.md's
    // §4 traces the beam APN/attractiveness formula against the decompiled targeting function
    // using four independent techniques (structural trace, full line-by-line read, mechanical
    // constant search, and a caller-chain search for a range-based pre-filter) and concludes no
    // range term feeds into it at all - the "1 - 0.1*(Range/MaxRange)" shape it once inferred
    // belongs only to the separate beam range-dissipation formula (applied to a shot's actual
    // damage), not to targeting priority. BattleEngine.GetAttractiveness previously multiplied
    // its beam-branch APN by such a range-derived factor, making a target artificially look
    // MORE attractive the closer it was - a mechanic the real game never has.
    [TestFixture]
    public class BattleAttractivenessRangeTest
    {
        private ServerData serverState;
        private BattleEngine battleEngine;

        [SetUp]
        public void Init()
        {
            serverState = new ServerData();
            battleEngine = new BattleEngine(serverState, new BattleReport());
        }

        private static ShipDesign BuildBeamWolfDesign(int range)
        {
            Component blueprint = new Component
            {
                Cost = new Resources(10, 10, 10, 10),
                Mass = 100,
            };

            Hull hull = new Hull
            {
                FuelCapacity = 100,
                ArmorStrength = 100,
                Modules = new List<HullModule>(),
            };

            Component weaponComponent = new Component();
            weaponComponent.Properties.Add("Weapon", new Weapon { Power = 10, Range = range, Accuracy = 75, Group = WeaponType.standardBeam });

            HullModule weaponModule = new HullModule
            {
                AllocatedComponent = weaponComponent,
                ComponentCount = 1,
            };
            hull.Modules.Add(weaponModule);

            blueprint.Properties.Add("Hull", hull);
            blueprint.Properties.Add("Battle Movement", new DoubleProperty(1.0));

            ShipDesign design = new ShipDesign(1) { Name = "BeamWolf", Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static ShipDesign BuildTargetDesign()
        {
            Component blueprint = new Component
            {
                Cost = new Resources(50, 0, 0, 50),
                Mass = 100,
            };

            Hull hull = new Hull
            {
                FuelCapacity = 100,
                ArmorStrength = 100,
                Modules = new List<HullModule>(),
            };

            blueprint.Properties.Add("Hull", hull);
            blueprint.Properties.Add("Battle Movement", new DoubleProperty(1.0));

            ShipDesign design = new ShipDesign(2) { Name = "Target", Blueprint = blueprint };
            design.Update();
            return design;
        }

        private static Stack MakeStack(ShipDesign design, int owner, Point position, int armor, int shields)
        {
            Fleet fleet = new Fleet("fleet-" + owner + "-" + design.Name, (ushort)owner, 1, position);
            ShipToken token = new ShipToken(design, 1) { Armor = armor, Shields = shields };
            fleet.Composition.Add(token.Key, token);
            return new Stack(fleet, 0, token);
        }

        [Test]
        public void GetAttractiveness_BeamWeapon_IsIndependentOfTargetDistance()
        {
            ShipDesign wolfDesign = BuildBeamWolfDesign(range: 2);
            ShipDesign targetDesign = BuildTargetDesign();

            Stack wolf = MakeStack(wolfDesign, 1, new Point(0, 0), armor: 0, shields: 0);
            Stack nearTarget = MakeStack(targetDesign, 2, new Point(1, 0), armor: 100, shields: 50);
            Stack farTarget = MakeStack(targetDesign, 2, new Point(9, 9), armor: 100, shields: 50);

            double nearAttractiveness = battleEngine.GetAttractiveness(wolf, nearTarget);
            double farAttractiveness = battleEngine.GetAttractiveness(wolf, farTarget);

            Assert.AreEqual(nearAttractiveness, farAttractiveness, 0.0001,
                "A beam weapon's attractiveness score must depend only on cost/armor/shields/deflectors, not on the target's distance - the real game's targeting formula has no range term.");
        }
    }
}
