#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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
    using System;
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Ship-design rows of behavior-specs-10/ship-design-and-components.md: the combined
    /// armor/shield secondary stats (§7), the retroactive slot-removal check (§3), the graded
    /// tech-shortfall check (§11 / research-tech-tree.md §4), and the backslash-letter template
    /// convention of dynamic-string-table.md §5.2.
    /// </summary>
    [TestFixture]
    public class DesignRowsT3Test
    {
        private static ShipDesign DesignWith(string componentName, int count)
        {
            Component part = new AllComponents().Fetch(componentName);
            Assert.IsNotNull(part, componentName + " missing from components.xml");

            Component blueprint = new Component { Mass = 10, Name = "Test Hull" };
            Hull hull = new Hull { Modules = new List<HullModule>(), FuelCapacity = 100, ArmorStrength = 20 };
            hull.Modules.Add(new HullModule { ComponentType = "General Purpose", ComponentMaximum = 8, AllocatedComponent = part, ComponentCount = count });
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(1) { Blueprint = blueprint, Name = "Test", Type = ItemType.Ship };
            design.Update();
            return design;
        }

        // ------------------------------------------------------------------ §7 secondary stats

        [Test]
        public void LangstonShell_Jams5PercentPerUnit_Compounding_AndAdds65Armor()
        {
            ShipDesign one = DesignWith("Langston Shell", 1);
            Assert.AreEqual(5, one.Jammer, 1e-9, "A 95% per-unit multiplier: 5% jamming");
            Assert.AreEqual(125, one.Shield);
            Assert.AreEqual(20 + 65, one.Armor, "Croby Sharmor / Langston Shell +65 armor per unit");

            ShipDesign two = DesignWith("Langston Shell", 2);
            Assert.AreEqual(100 - (95.0 * 95.0 / 100.0), two.Jammer, 1e-9, "Units compound: 1 - 0.95^2");
            Assert.AreEqual(20 + 130, two.Armor);
        }

        [Test]
        public void MegaPolyShell_Jams20PercentPerUnit_AndAdds100Shield()
        {
            ShipDesign design = DesignWith("Mega Poly Shell", 1);
            Assert.AreEqual(20, design.Jammer, 1e-9, "An 80% per-unit multiplier: 20% jamming");
            Assert.AreEqual(100, design.Shield, "Mega Poly Shell +100 shield-flavoured points per unit");
            Assert.AreEqual(20 + 400, design.Armor);
        }

        [Test]
        public void TheDesignJammingFigure_IsCappedAt95()
        {
            ShipDesign design = DesignWith("Jammer 50", 6); // 1 - 0.5^6 = 98.4%
            Assert.AreEqual(ShipDesign.MaximumJammer, design.Jammer, 1e-9);
        }

        // ------------------------------------------------------------------ §3 slot removal

        [Test]
        public void SlotRemoval_OnADesignWithShipsBuilt_NeedsConfirmation_OtherwiseNot()
        {
            ShipDesign design = DesignWith("Mega Poly Shell", 1);
            ShipDesign otherDesign = DesignWith("Langston Shell", 1);
            otherDesign.Key = 2;

            Fleet built = new Fleet(1);
            ShipToken token = new ShipToken(design, 3);
            built.Composition.Add(token.Key, token);

            Fleet unrelated = new Fleet(2);
            ShipToken otherToken = new ShipToken(otherDesign, 1);
            unrelated.Composition.Add(otherToken.Key, otherToken);

            List<Fleet> fleets = new List<Fleet> { built, unrelated };

            List<Fleet> affected = DesignAvailability.FleetsAffectedBySlotRemoval(fleets, design, 0);
            Assert.AreEqual(1, affected.Count);
            Assert.AreSame(built, affected[0]);
            Assert.IsTrue(DesignAvailability.SlotRemovalNeedsConfirmation(fleets, design, 0));

            Assert.IsFalse(DesignAvailability.SlotRemovalNeedsConfirmation(new List<Fleet> { unrelated }, design, 0), "No ships of the design: removal proceeds silently");

            design.Hull.Modules[0].AllocatedComponent = null;
            design.Hull.Modules[0].ComponentCount = 0;
            Assert.IsFalse(DesignAvailability.SlotRemovalNeedsConfirmation(fleets, design, 0), "An empty slot affects no ship");
        }

        // ------------------------------------------------------------------ graded shortfall

        private static TechLevel Levels(int energy, int weapons, int propulsion, int construction, int electronics, int biotechnology)
        {
            return new TechLevel(biotechnology, electronics, energy, propulsion, weapons, construction);
        }

        [Test]
        public void TechShortfall_EveryFieldMet_IsAvailable()
        {
            TechShortfall result = DesignAvailability.GradeTechShortfall(Levels(3, 0, 2, 0, 0, 0), Levels(3, 1, 5, 0, 0, 0), TechLevel.ResearchField.Weapons);
            Assert.AreEqual(TechShortfallGrade.Available, result.Grade);
            Assert.AreEqual(0, result.Code);
        }

        [Test]
        public void TechShortfall_OneLevelShortInTheResearchedField_IsOneLevelAway()
        {
            TechShortfall result = DesignAvailability.GradeTechShortfall(Levels(4, 0, 0, 0, 0, 0), Levels(3, 9, 9, 9, 9, 9), TechLevel.ResearchField.Energy);
            Assert.AreEqual(TechShortfallGrade.OneLevelAway, result.Grade);
            Assert.AreEqual(1, result.Gap);
            Assert.AreEqual(1, result.Code);
        }

        [Test]
        public void TechShortfall_SeveralLevelsShortInTheResearchedField_IsTheGapPlusOne()
        {
            TechShortfall result = DesignAvailability.GradeTechShortfall(Levels(6, 0, 0, 0, 0, 0), Levels(3, 0, 0, 0, 0, 0), TechLevel.ResearchField.Energy);
            Assert.AreEqual(TechShortfallGrade.Further, result.Grade);
            Assert.AreEqual(3, result.Gap);
            Assert.AreEqual(4, result.Code);
        }

        [Test]
        public void TechShortfall_AShortFieldThatIsNotBeingResearched_OrTwoShortFields_IsFarAway()
        {
            TechShortfall other = DesignAvailability.GradeTechShortfall(Levels(4, 0, 0, 0, 0, 0), Levels(3, 0, 0, 0, 0, 0), TechLevel.ResearchField.Weapons);
            Assert.AreEqual(TechShortfallGrade.FarAway, other.Grade);

            TechShortfall two = DesignAvailability.GradeTechShortfall(Levels(4, 4, 0, 0, 0, 0), Levels(3, 3, 0, 0, 0, 0), TechLevel.ResearchField.Energy);
            Assert.AreEqual(TechShortfallGrade.FarAway, two.Grade);

            TechShortfall none = DesignAvailability.GradeTechShortfall(Levels(4, 0, 0, 0, 0, 0), Levels(3, 0, 0, 0, 0, 0), null);
            Assert.AreEqual(TechShortfallGrade.FarAway, none.Grade);
            Assert.AreNotEqual(0, none.Code);
            Assert.AreNotEqual(1, none.Code);
        }

        [Test]
        public void TechShortfall_TheResearchedFieldIsTheFirstTopicSetToOne()
        {
            TechLevel topics = new TechLevel(0);
            Assert.IsNull(DesignAvailability.ResearchingField(topics));
            topics[TechLevel.ResearchField.Propulsion] = 1;
            Assert.AreEqual(TechLevel.ResearchField.Propulsion, DesignAvailability.ResearchingField(topics));
        }

        // ------------------------------------------------------------------ backslash templates

        [Test]
        public void BackslashTemplate_ALowercaseLetterMarkerIsReplacedThroughTheResolver()
        {
            Func<char, string> names = letter => letter == 'h' ? "game.hst" : letter == 'x' ? "game.x1" : null;
            Assert.AreEqual("Cannot open game.hst or game.x1.", BackslashTemplate.Expand("Cannot open \\h or \\x.", names));
        }

        [Test]
        public void BackslashTemplate_UnknownLetters_UppercaseLetters_AndOtherBackslashesAreKept()
        {
            Func<char, string> names = letter => letter == 'h' ? "game.hst" : null;
            // "\\h" (two backslashes): the first is not followed by a letter and is copied, the
            // second starts a marker.
            Assert.AreEqual("\\q \\H \\1 \\game.hst trailing\\", BackslashTemplate.Expand("\\q \\H \\1 \\\\h trailing\\", names));
            Assert.AreEqual("a\\", BackslashTemplate.Expand("a\\", names));
            Assert.AreEqual("%d of %s", BackslashTemplate.Expand("%d of %s", names), "printf specifiers are the other convention");
        }

        [Test]
        public void BackslashTemplate_MarkerLettersListsTheMarkersInOrder()
        {
            Assert.AreEqual("hxu", BackslashTemplate.MarkerLetters("\\h then \\x and \\u, not \\H or \\9"));
            Assert.AreEqual(string.Empty, BackslashTemplate.MarkerLetters(null));
        }
    }
}
