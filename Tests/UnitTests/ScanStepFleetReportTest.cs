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

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// Covers a real, reported bug: a fleet's own map marker (drawn from its FleetReports self-
    /// report - see StarMapDocumentViewModel's own comment) rendered nowhere near its actual,
    /// live position/route. Root cause: ScanStep.Scan's self-scan update indexed
    /// empire.FleetReports[scanner.Key] directly, with no guarantee that entry already existed -
    /// unlike AddStars' own ContainsKey-or-Add handling for stars, nothing ensured every owned
    /// fleet had a report before this ran. A fleet reaching this method with no existing report
    /// (this test's scenario) threw KeyNotFoundException, which - however it was actually being
    /// swallowed upstream in the real game - left that fleet's report stuck wherever it last was,
    /// forever, since the same fleet hits this same throw every subsequent turn too.
    /// </summary>
    [TestFixture]
    public class ScanStepFleetReportTest
    {
        [Test]
        public void Process_OwnedFleetWithNoExistingReport_CreatesOneInsteadOfThrowing()
        {
            ServerData serverState = new ServerData();

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverState.AllEmpires.Add(empire.Id, empire);

            ShipDesign design = new ShipDesign(1);
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            design.Blueprint.Properties.Add("Hull", hull);

            Fleet fleet = new Fleet(6) { Owner = empire.Id, Name = "Scout2 #6", Position = new NovaPoint(123, 456) };
            fleet.Composition.Add(new ShipToken(design, 1).Key, new ShipToken(design, 1));
            fleet.Waypoints.Add(new Waypoint { Position = fleet.Position });

            // Added directly to OwnedFleets, deliberately bypassing EmpireData.AddOrUpdateFleet
            // (which would have safely created a matching report itself) - this is exactly the
            // "fleet exists but its own report doesn't yet" state the fix guards against,
            // regardless of how a real game might reach it.
            empire.OwnedFleets.Add(fleet);

            Assert.DoesNotThrow(() => new ScanStep().Process(serverState));

            Assert.That(empire.FleetReports.ContainsKey(fleet.Key), Is.True);
            Assert.That(empire.FleetReports[fleet.Key].Position, Is.EqualTo(fleet.Position));
        }
    }
}
