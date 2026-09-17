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
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;

    using NUnit.Framework;

    /// <summary>
    /// Self-healing repair for a real, previously-shipped bug: Manufacture.CreateShips's
    /// starbase-replacement branch only ever did `star.Starbase = null` before assigning the
    /// newly-built one, never removing the OLD starbase fleet from OwnedFleets/FleetReports -
    /// leaving it to linger there forever as a stray "fleet in orbit" at that star in every UI
    /// fleet listing, even after the underlying bug itself was fixed. A save already carrying
    /// that corruption stays corrupted forever unless something repairs it on load, since fixing
    /// the bug only stops NEW corruption - it doesn't retroactively clean up old saves.
    /// EmpireData.LinkReferences (run on every load, client and server alike, since ServerData
    /// itself loads each empire via the same XML constructor) now does exactly that via
    /// RemoveOrphanedStarbaseFleets.
    /// </summary>
    [TestFixture]
    public class EmpireDataOrphanedStarbaseRepairTest
    {
        private static ShipDesign MakeStarbaseDesign(long key)
        {
            var design = new ShipDesign(key) { Type = ItemType.Starbase };
            design.Blueprint = new Component();
            var hull = new Hull { Modules = new List<HullModule>() };
            design.Blueprint.Properties.Add("Hull", hull);
            design.Icon = new ShipIcon("Dummy0001.png", null); // GetIconBySource expects <hull><4-digit number>.png
            return design;
        }

        [Test]
        public void XmlRoundTrip_RemovesAnOrphanedOldStarbase_ButKeepsTheCurrentOne()
        {
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();

            Star star = new Star { Name = "Homeworld", Owner = empire.Id };

            ShipDesign smallBase = MakeStarbaseDesign(empire.GetNextDesignKey());
            ShipDesign largeBase = MakeStarbaseDesign(empire.GetNextDesignKey());
            empire.Designs.Add(smallBase.Key, smallBase);
            empire.Designs.Add(largeBase.Key, largeBase);

            // The OLD starbase - exactly what a pre-fix CreateShips left behind: still sitting in
            // OwnedFleets, InOrbit at the star, Type == Starbase, but no longer star.Starbase.
            Fleet oldStarbase = new Fleet(new ShipToken(smallBase, 1), star, empire.GetNextFleetKey());
            oldStarbase.Name = "Homeworld Starbase (old)";
            oldStarbase.Type = ItemType.Starbase;
            empire.AddOrUpdateFleet(oldStarbase);

            // The current (replacement) starbase - the one star.Starbase actually points to now.
            Fleet currentStarbase = new Fleet(new ShipToken(largeBase, 1), star, empire.GetNextFleetKey());
            currentStarbase.Name = "Homeworld Starbase";
            currentStarbase.Type = ItemType.Starbase;
            empire.AddOrUpdateFleet(currentStarbase);
            star.Starbase = currentStarbase;

            empire.OwnedStars.Add(star.Name, star);
            empire.StarReports.Add(star.Name, new StarIntel(star, ScanLevel.Owned, 2100));

            // Confirm the test actually sets up the corrupted precondition before round-tripping:
            // both fleets present, only one of them wired as the star's real Starbase.
            Assert.AreEqual(2, empire.OwnedFleets.Count);
            Assert.AreSame(currentStarbase, star.Starbase);

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(empire.ToXml(xmldoc));

            EmpireData reloaded = new EmpireData(root.FirstChild);

            Assert.AreEqual(1, reloaded.OwnedFleets.Count,
                "The orphaned old starbase must be removed on load - only the current one should remain");
            Assert.IsFalse(reloaded.OwnedFleets.ContainsKey(oldStarbase.Key));
            Assert.IsFalse(reloaded.FleetReports.ContainsKey(oldStarbase.Key),
                "The orphan's FleetReports entry must go with it, not linger as a dangling report");

            Fleet reloadedStar = reloaded.OwnedStars[star.Name].Starbase;
            Assert.IsNotNull(reloadedStar);
            Assert.AreEqual(currentStarbase.Key, reloadedStar.Key,
                "The star's own (current) starbase reference must survive untouched");
        }

        /// <summary>Reproduces a real affected save (user-reported): the game's very first,
        /// game-creation-time starbase (StarMapInitialiser.AllocateStarbase, before its own fix)
        /// was correctly wired via star.Starbase and correctly added to OwnedFleets, but its own
        /// Fleet.Type was left at the Fleet(ShipToken, Star, long) constructor's default
        /// (ItemType.Fleet) rather than ever being stamped Starbase - so once superseded by a
        /// real, correctly-Type-stamped replacement (built via Manufacture.CreateShips), the
        /// original RemoveOrphanedStarbaseFleets (checking fleet.Type alone) could never
        /// recognize it as a starbase-shaped orphan at all, and it lingered forever.</summary>
        [Test]
        public void XmlRoundTrip_RemovesAnOrphanedOldStarbase_EvenWhenItsOwnTypeWasNeverSetToStarbase()
        {
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();

            Star star = new Star { Name = "Homeworld", Owner = empire.Id };

            ShipDesign initialDesign = MakeStarbaseDesign(empire.GetNextDesignKey());
            ShipDesign replacementDesign = MakeStarbaseDesign(empire.GetNextDesignKey());
            empire.Designs.Add(initialDesign.Key, initialDesign);
            empire.Designs.Add(replacementDesign.Key, replacementDesign);

            // The game-creation-time starbase - built from a Starbase-type design, but its OWN
            // Type field is deliberately left at the Fleet constructor's plain default, exactly
            // matching StarMapInitialiser.AllocateStarbase's pre-fix behavior.
            Fleet initialStarbase = new Fleet(new ShipToken(initialDesign, 1), star, empire.GetNextFleetKey());
            initialStarbase.Name = "Homeworld Starbase";
            empire.AddOrUpdateFleet(initialStarbase);
            star.Starbase = initialStarbase;

            // Player later builds a real replacement via production (Manufacture.CreateShips),
            // which DOES correctly stamp Type - superseding the original.
            Fleet replacementStarbase = new Fleet(new ShipToken(replacementDesign, 1), star, empire.GetNextFleetKey());
            replacementStarbase.Name = "Homeworld Starbase";
            replacementStarbase.Type = ItemType.Starbase;
            empire.AddOrUpdateFleet(replacementStarbase);
            star.Starbase = replacementStarbase;

            empire.OwnedStars.Add(star.Name, star);
            empire.StarReports.Add(star.Name, new StarIntel(star, ScanLevel.Owned, 2100));

            Assert.AreEqual(2, empire.OwnedFleets.Count);
            Assert.AreEqual(ItemType.Fleet, initialStarbase.Type,
                "Confirms the precondition: the original starbase's own Type was never Starbase");

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(empire.ToXml(xmldoc));

            EmpireData reloaded = new EmpireData(root.FirstChild);

            Assert.AreEqual(1, reloaded.OwnedFleets.Count,
                "The mis-typed original starbase must still be recognized and removed on load");
            Assert.IsFalse(reloaded.OwnedFleets.ContainsKey(initialStarbase.Key));
            Assert.AreEqual(replacementStarbase.Key, reloaded.OwnedStars[star.Name].Starbase.Key);
        }

        [Test]
        public void XmlRoundTrip_LeavesAGenuineSingleStarbase_Untouched()
        {
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();

            Star star = new Star { Name = "Homeworld", Owner = empire.Id };

            ShipDesign design = MakeStarbaseDesign(empire.GetNextDesignKey());
            empire.Designs.Add(design.Key, design);

            Fleet starbase = new Fleet(new ShipToken(design, 1), star, empire.GetNextFleetKey());
            starbase.Name = "Homeworld Starbase";
            starbase.Type = ItemType.Starbase;
            empire.AddOrUpdateFleet(starbase);
            star.Starbase = starbase;

            empire.OwnedStars.Add(star.Name, star);
            empire.StarReports.Add(star.Name, new StarIntel(star, ScanLevel.Owned, 2100));

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(empire.ToXml(xmldoc));

            EmpireData reloaded = new EmpireData(root.FirstChild);

            Assert.AreEqual(1, reloaded.OwnedFleets.Count,
                "A genuine, un-replaced starbase must never be mistaken for an orphan and removed");
            Assert.AreEqual(starbase.Key, reloaded.OwnedStars[star.Name].Starbase.Key);
        }
    }
}
