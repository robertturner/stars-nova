#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars-Nova.
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
    using Nova.Server;

    /// <summary>
    /// Covers a real, reported bug: building a replacement (larger) starbase correctly took over
    /// as the star's OWN Starbase (shown as such in the Inspector's Overview tab), but the old
    /// starbase fleet kept lingering in the empire's OwnedFleets - so it also showed up as an
    /// ordinary fleet in orbit at that star (e.g. the mobile Map's "Viewing" switcher - see
    /// MapSelectionSwitcherViewModel, which only ever excludes whichever fleet star.Starbase
    /// CURRENTLY points to). Root cause: Manufacture.CreateShips's starbase-replacement branch
    /// only ever did `star.Starbase = null` before assigning the new one - detaching the OLD
    /// fleet from the star without ever removing it from the empire's own OwnedFleets/
    /// FleetReports bookkeeping (unlike a real Scrap, which does).
    /// </summary>
    [TestFixture]
    public class ManufactureStarbaseReplacementTest
    {
        private static ShipDesign MakeStarbaseDesign(long key)
        {
            var design = new ShipDesign(key) { Type = ItemType.Starbase };
            design.Blueprint = new Component();
            var hull = new Hull { Modules = new List<HullModule>() };
            design.Blueprint.Properties.Add("Hull", hull);
            return design;
        }

        [Test]
        public void BuildingAReplacementStarbase_RemovesTheOldOneFromOwnedFleets()
        {
            ServerData serverState = new ServerData();

            EmpireData empire = new EmpireData();
            empire.Id = 1;
            serverState.AllEmpires.Add(empire.Id, empire);

            Star star = new Star { Name = "Homeworld", Owner = empire.Id };
            empire.OwnedStars.Add(star);

            ShipDesign smallBase = MakeStarbaseDesign(empire.GetNextDesignKey());
            ShipDesign largeBase = MakeStarbaseDesign(empire.GetNextDesignKey());
            empire.Designs.Add(smallBase.Key, smallBase);
            empire.Designs.Add(largeBase.Key, largeBase);

            var manufacture = new Manufacture(serverState);

            // Build the first (small) starbase - zero-cost stub designs (no Update() ever called
            // on them) complete in a single Items() pass regardless of star.ResourcesOnHand.
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(smallBase), false));
            manufacture.Items(star);

            Fleet firstStarbase = star.Starbase;
            Assert.IsNotNull(firstStarbase, "The first starbase should have been built and assigned to the star");
            Assert.IsTrue(empire.OwnedFleets.ContainsKey(firstStarbase.Key));

            // Now build the replacement (larger) starbase at the same star.
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(largeBase), false));
            manufacture.Items(star);

            Fleet secondStarbase = star.Starbase;
            Assert.IsNotNull(secondStarbase);
            Assert.AreNotEqual(firstStarbase.Key, secondStarbase.Key, "The replacement must be a distinct fleet from the original");

            Assert.IsFalse(empire.OwnedFleets.ContainsKey(firstStarbase.Key),
                "The old starbase must be removed from OwnedFleets, not just detached from star.Starbase - " +
                "otherwise it lingers and shows up as a stray extra fleet in orbit at this star.");
            Assert.IsTrue(empire.OwnedFleets.ContainsKey(secondStarbase.Key));

            // Exactly one fleet should now sit at this star - the new starbase - not two.
            int fleetsAtStar = empire.OwnedFleets.Values.Count(f => f.InOrbit == star);
            Assert.AreEqual(1, fleetsAtStar);
        }
    }
}
