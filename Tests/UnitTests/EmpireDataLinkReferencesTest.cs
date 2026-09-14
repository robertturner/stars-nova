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
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    using NUnit.Framework;

    /// <summary>
    /// Regression test for a gap found while adding starbase info to the Inspector panel:
    /// EmpireData.LinkReferences() resolved a Star's own Starbase reference (via OwnedStars) back
    /// to the real Fleet after an XML round trip, but never did the same for the matching
    /// StarReports entry - which after any normal save/load left StarIntel.Starbase.Composition
    /// permanently empty (StarIntel's XML constructor only recovers a placeholder Fleet(long)
    /// stub), even though StarIntel.Starbase itself was non-null. That meant the star map's
    /// Stargate/Mass-Driver indicator dots (StarMapDocumentViewModel) could show mere starbase
    /// presence but never a design's actual capabilities, for any game that had ever been saved
    /// and reloaded - i.e. every real game.
    /// </summary>
    [TestFixture]
    public class EmpireDataLinkReferencesTest
    {
        [Test]
        public void XmlRoundTrip_ResolvesStarReportsStarbaseToTheRealFleet_WithItsRealComposition()
        {
            EmpireData empire = new EmpireData();
            empire.Id = 1;
            empire.Race = new Race();

            Star star = new Star();
            star.Name = "Homeworld";
            star.Owner = empire.Id;

            ShipDesign design = new ShipDesign(empire.GetNextDesignKey());
            design.Blueprint = new Component();
            Hull hull = new Hull();
            hull.Modules = new List<HullModule>();
            HullModule gateModule = new HullModule();
            Component gateComponent = new Component();
            gateComponent.Name = "Stargate 100/250";
            gateComponent.Properties.Add("Gate", new Gate { SafeHullMass = 100, SafeRange = 250 });
            gateModule.AllocatedComponent = gateComponent;
            hull.Modules.Add(gateModule);
            design.Blueprint.Properties.Add("Hull", hull);
            design.Name = "Starbase";
            design.Icon = new ShipIcon("Dummy0001.png", null); // GetIconBySource expects <hull><4-digit number>.png
            design.Update();
            empire.Designs[design.Key] = design;

            ShipToken starbaseToken = new ShipToken(design, 1);
            Fleet starbaseFleet = new Fleet(starbaseToken, star, empire.GetNextFleetKey());
            starbaseFleet.Name = "Homeworld Starbase";
            star.Starbase = starbaseFleet;
            empire.AddOrUpdateFleet(starbaseFleet);

            empire.OwnedStars.Add(star.Name, star);
            empire.StarReports.Add(star.Name, new StarIntel(star, ScanLevel.Owned, 2100));

            // Confirm this test actually exercises the bug's precondition before round-tripping:
            // the live, in-memory Starbase reference is the real Fleet, shared by both places.
            Assert.AreSame(starbaseFleet, empire.StarReports[star.Name].Starbase);

            XmlDocument xmldoc = new XmlDocument();
            XmlElement root = xmldoc.CreateElement("Root");
            xmldoc.AppendChild(root);
            root.AppendChild(empire.ToXml(xmldoc));

            EmpireData reloaded = new EmpireData(root.FirstChild);

            Fleet reloadedReportStarbase = reloaded.StarReports[star.Name].Starbase;
            Assert.IsNotNull(reloadedReportStarbase);
            Assert.AreEqual(1, reloadedReportStarbase.Composition.Count,
                "The report's Starbase should resolve to the real Fleet (with real Composition), not the empty XML placeholder stub");

            ShipDesign reloadedDesign = new List<ShipToken>(reloadedReportStarbase.Composition.Values)[0].Design;
            reloadedDesign.Update();
            Assert.IsTrue(reloadedDesign.Summary.Properties.ContainsKey("Gate"),
                "The reloaded design should still show its Stargate capability");
        }
    }
}
