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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
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
    /// Reproduces a real, reported bug: a freshly-built "Large Freighter" showed as 100% damaged
    /// (Armor 0 of a possible 150) the very turn it was constructed, despite its hull's own
    /// ArmorStrength being 150. Root cause: ShipDesign.Armor's getter, unlike every sibling
    /// summary-derived property (FuelCapacity, CargoCapacity, Weapons, IsBomber, IsStarbase, ...),
    /// never called Update() itself - it only ever worked by accident, piggy-backing on some OTHER
    /// property access (e.g. Cost, read by ShipProductionUnit's own constructor when an order is
    /// queued) having already populated Summary first on the SAME design object. Whenever Armor
    /// was the FIRST summary-derived property ever touched on a particular ShipDesign instance
    /// (e.g. a design object freshly loaded from a save at the start of a turn, before anything
    /// else happened to touch it), Summary was still empty and Armor silently returned 0 - seeding
    /// ShipToken's constructor (Armor = newDesign.Armor * quantity) with zero instead of full
    /// armor for a brand new ship.
    /// </summary>
    [TestFixture]
    public class ManufactureFreshShipArmorTest
    {
        private static ShipDesign MakeFreighterDesign(long key)
        {
            ShipDesign design = new ShipDesign(key) { Name = "Large Freighter" };
            design.Blueprint = new Component();

            Hull hull = new Hull { ArmorStrength = 150, FuelCapacity = 2600, BaseCargo = 1200 };
            hull.Modules = new List<HullModule>
            {
                // Matches the reported design: an empty "Shield or Armor" slot (no component
                // allocated - AllocatedComponent stays null) alongside a slot that IS filled.
                new HullModule { CellNumber = 17, ComponentMaximum = 2, ComponentType = "Shield or Armor" },
                new HullModule { CellNumber = 7, ComponentMaximum = 2, ComponentType = "Scanner Electrical Mechanical" },
            };

            design.Blueprint.Properties.Add("Hull", hull);
            return design;
        }

        [Test]
        public void FreshlyLoadedDesign_ArmorPropertySelfUpdates_EvenAsTheFirstPropertyTouched()
        {
            // A brand-new ShipDesign object with nothing else ever having accessed it - the exact
            // shape of a design object right after a turn's state is freshly loaded from XML,
            // before anything else (e.g. ShipProductionUnit reading .Cost) has touched it.
            ShipDesign freighterDesign = MakeFreighterDesign(1);

            Assert.AreEqual(150, freighterDesign.Armor,
                "Design.Armor must compute correctly even when it's the very first summary-derived property read on this instance.");
        }

        [Test]
        public void BuildingAFreshShip_HasFullArmorNotZero()
        {
            ServerData serverState = new ServerData();

            EmpireData empire = new EmpireData { Id = 1 };
            serverState.AllEmpires.Add(empire.Id, empire);

            Star star = new Star { Name = "Nelson", Owner = empire.Id };
            empire.OwnedStars.Add(star);

            ShipDesign freighterDesign = MakeFreighterDesign(empire.GetNextDesignKey());
            empire.Designs.Add(freighterDesign.Key, freighterDesign);

            Manufacture manufacture = new Manufacture(serverState);

            // Zero-cost stub design (no Cost set on Blueprint) completes in a single Items() pass
            // regardless of star.ResourcesOnHand - same technique ManufactureStarbaseReplacementTest
            // uses, isolating this test to the armor question rather than resource accounting.
            star.ManufacturingQueue.Queue.Add(new ProductionOrder(1, new ShipProductionUnit(freighterDesign), false));
            manufacture.Items(star);

            Fleet builtFleet = empire.OwnedFleets.Values.FirstOrDefault(f => f.Name.StartsWith("Large Freighter"));
            Assert.IsNotNull(builtFleet, "The freighter should have been built this pass.");

            ShipToken token = builtFleet.Composition.Values.First();
            Assert.AreEqual(150, token.Armor, "A freshly-built ship must start at full armor (its design's ArmorStrength), not damaged.");
        }
    }
}
