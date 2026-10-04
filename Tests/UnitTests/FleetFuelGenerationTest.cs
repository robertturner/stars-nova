#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License version 2 as
// published by the Free Software Foundation.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    // Regression test for a real gap: docs/behavior-specs-7/fleet-movement-scanning-cargo.md
    // confirms "every conventional (non-scoop) engine generat[es] exactly 1 mg at warp 1... the
    // mechanical basis for treating warp 1 as an always-available, self-sustaining crawl speed" -
    // but Fleet.FuelGeneration previously only ever generated fuel for ramscoop engines, so a
    // fleet with only ordinary engines that ran low on fuel had no way to ever generate more.
    [TestFixture]
    public class FleetFuelGenerationTest
    {
        private static Fleet MakeSingleEngineFleet(bool ramScoop)
        {
            ShipDesign design = new ShipDesign(1);
            design.Blueprint = new Component();
            design.Blueprint.Mass = 200;
            Hull hull = new Hull();
            hull.FuelCapacity = 1000;
            hull.Modules = new List<HullModule>();

            HullModule engineModule = new HullModule();
            Component engineComponent = new Component();
            Engine engine = new Engine();
            engine.RamScoop = ramScoop;
            // Warp-1 consumption at Mass 200 works out to exactly 1 mg/ly:
            // (Mass + cargo) * (FuelConsumption[0]/100) * (1*1) / 200 = 200 * 1.0 * 1 / 200 = 1.
            engine.FuelConsumption[0] = 100;
            engineComponent.Properties.Add("Engine", engine);
            engineModule.AllocatedComponent = engineComponent;
            engineModule.ComponentCount = 1;
            hull.Modules.Add(engineModule);
            design.Blueprint.Properties.Add("Hull", hull);
            design.Update();

            ShipToken token = new ShipToken(design, 1);

            Fleet fleet = new Fleet(1);
            fleet.Owner = 1;
            fleet.Position = new NovaPoint(0, 0);
            fleet.Composition.Add(token.Key, token);
            fleet.FuelAvailable = 100;

            Waypoint waypoint = new Waypoint();
            waypoint.Position = new NovaPoint(1000000, 0); // far enough that fuel, not distance, binds
            waypoint.WarpFactor = 1;
            waypoint.Task = new NoTask();
            waypoint.Destination = "Space at (1000000, 0)";
            fleet.Waypoints.Add(waypoint);

            return fleet;
        }

        [Test]
        public void OrdinaryEngine_StillGeneratesFuelAtWarp1_SoAFleetCanNeverBePermanentlyStranded()
        {
            Fleet fleet = MakeSingleEngineFleet(ramScoop: false);
            Race race = new Race();
            double availableTime = 1000; // comfortably more than the fuel-bound travel time below

            fleet.Move(ref availableTime, race);

            // 100mg at 1mg/ly buys exactly 100 ly this turn, consuming the tank down to 0 - but
            // that same 100 ly at warp 1 must also generate 1mg/ly * 100ly = 100mg back, leaving
            // the tank exactly where it started rather than empty.
            Assert.AreEqual(100, fleet.FuelAvailable,
                "An ordinary (non-ramscoop) engine must still generate 1mg/ly at warp 1");
        }

        [Test]
        public void OrdinaryEngine_GeneratesNoFuel_AtWarpFactorsAboveOne()
        {
            Fleet fleet = MakeSingleEngineFleet(ramScoop: false);
            fleet.Waypoints[0].WarpFactor = 2;
            // Give warp 2 its own real (nonzero) consumption rate too - otherwise this design's
            // untouched-default FuelConsumption[1] (warp 2's own table slot) makes warp 2 free,
            // and the tank would read back unchanged (100) regardless of whether generation is
            // correctly scoped to warp 1 or not, making the assertion below vacuous either way.
            Engine engine = (Engine)fleet.Composition.Values.Single().Design.Summary.Properties["Engine"];
            engine.FuelConsumption[1] = 100;
            Race race = new Race();
            double availableTime = 1000;

            fleet.Move(ref availableTime, race);

            Assert.AreEqual(0, fleet.FuelAvailable,
                "The warp-1 self-sustaining crawl rule is specific to warp 1 - it must not generate fuel at warp 2+, so a fully-drained tank must stay drained");
        }

        [Test]
        public void RamscoopEngine_StepTableBehaviorAtWarp1_IsUnchangedByThisFix()
        {
            Fleet fleet = MakeSingleEngineFleet(ramScoop: true);
            // MakeSingleEngineFleet only sets FuelConsumption[0]; the rest of the table stays 0,
            // so FreeWarpSpeed (the highest index still reading 0) comes out to 10 - meaning at
            // warp 1, belowFreeWarp = 10-1 = 9, landing in the pre-existing step table's "else"
            // branch (perEngineFactor 10), not the new non-ramscoop warp-1 branch this fix added.
            // 10 * 100 ly generated = 1000mg, clamped to the fleet's 1000mg tank capacity.
            Race race = new Race();
            double availableTime = 1000;

            fleet.Move(ref availableTime, race);

            Assert.AreEqual(1000, fleet.FuelAvailable,
                "The pre-existing ramscoop step-table math must be untouched by this fix");
        }
    }
}
