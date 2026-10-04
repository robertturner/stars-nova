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
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The ship designer's slot-category rule (behavior-specs-10/ship-design-and-components.md
    /// §4 and §15e): a component fits a slot when its one category is in the slot's allowed
    /// mask, read here from the family names components.xml spells each slot with. The exact
    /// membership of the "eight ship-mountable families" of a general-purpose slot is a reported
    /// ambiguity (SlotCompatibility.GeneralPurposeFamilies); these tests pin only what the spec
    /// gives: never an engine, a bomb, a mining robot or an orbital part, and the scanner /
    /// shield / armor / weapon / electrical / mechanical families it does name as slot masks.
    /// </summary>
    [TestFixture]
    public class SlotCompatibilityTest
    {
        private static Component Part(ItemType type, string name = "part")
        {
            return new Component { Name = name, Type = type };
        }

        private static HullModule Slot(string type)
        {
            return new HullModule { ComponentType = type, ComponentMaximum = 1 };
        }

        [Test]
        public void ArmorScannerElectMech_AdmitsElectricalAndMechanicalParts_ByTheirAbbreviations()
        {
            // BUG FOUND: the designer matched a slot's words against the full type name, so
            // "Elect" and "Mech" admitted nothing.
            string slot = "Armor Scanner Elect Mech";
            Assert.IsTrue(SlotCompatibility.SlotAdmits(slot, ItemType.Electrical));
            Assert.IsTrue(SlotCompatibility.SlotAdmits(slot, ItemType.Mechanical));
            Assert.IsTrue(SlotCompatibility.SlotAdmits(slot, ItemType.Armor));
            Assert.IsTrue(SlotCompatibility.SlotAdmits(slot, ItemType.Scanner));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Shield));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Engine));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.BeamWeapons));
        }

        [Test]
        public void GeneralPurpose_IsNotEverythingButEngines()
        {
            // BUG FOUND: a general-purpose slot admitted every non-engine part, bombs, mining
            // robots and orbital (starbase-only) parts included.
            string slot = SlotCompatibility.GeneralPurposeSlot;
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Engine));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Bomb));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.MiningRobot));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Orbital));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Gate));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.Hull));
            Assert.IsFalse(SlotCompatibility.SlotAdmits(slot, ItemType.PlanetaryInstallations));

            foreach (ItemType family in new[] { ItemType.Scanner, ItemType.Shield, ItemType.Armor, ItemType.BeamWeapons, ItemType.Torpedoes, ItemType.Electrical, ItemType.Mechanical })
            {
                Assert.IsTrue(SlotCompatibility.SlotAdmits(slot, family), family.ToString());
            }

            Assert.AreEqual(8, SlotCompatibility.GeneralPurposeFamilies.Count, "the union of the eight ship-mountable families");
        }

        [Test]
        public void TheRecurringMasks_ReadBackFromTheirNames()
        {
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Weapon", ItemType.BeamWeapons), "a weapons slot is beam plus torpedo");
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Weapon", ItemType.Torpedoes));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Weapon", ItemType.Shield));

            Assert.IsTrue(SlotCompatibility.SlotAdmits("Weapon or Shield", ItemType.Shield));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Weapon or Shield", ItemType.Torpedoes));

            Assert.IsTrue(SlotCompatibility.SlotAdmits("Shield or Armor", ItemType.Armor));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Shield or Armor", ItemType.Shield));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Shield or Armor", ItemType.Electrical));

            Assert.IsTrue(SlotCompatibility.SlotAdmits("Scanner Electrical Mechanical", ItemType.Electrical));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Scanner Electrical Mechanical", ItemType.Mechanical));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Scanner Electrical Mechanical", ItemType.Scanner));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Scanner Electrical Mechanical", ItemType.Armor));

            Assert.IsTrue(SlotCompatibility.SlotAdmits("Mine Layer Electrical Mechanical", ItemType.MineLayer));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Mine Layer Electrical Mechanical", ItemType.Mechanical));

            Assert.IsTrue(SlotCompatibility.SlotAdmits("Orbital or Electrical", ItemType.Orbital));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Orbital or Electrical", ItemType.Electrical));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Orbital or Electrical", ItemType.Mechanical));

            // Single-category slots.
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Engine", ItemType.Engine));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Engine", ItemType.Scanner));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Bomb", ItemType.Bomb));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Bomb", ItemType.Electrical));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Mining Robot", ItemType.MiningRobot));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Mining Robot", ItemType.MineLayer));
            Assert.IsTrue(SlotCompatibility.SlotAdmits("Mine Layer", ItemType.MineLayer));
            Assert.IsFalse(SlotCompatibility.SlotAdmits("Mine Layer", ItemType.MiningRobot));
        }

        [Test]
        public void BuiltInSlots_AndUnknownNames_AdmitNothing()
        {
            Assert.AreEqual(0, SlotCompatibility.AllowedTypes("Base Cargo").Count);
            Assert.AreEqual(0, SlotCompatibility.AllowedTypes("Space Dock").Count);
            Assert.AreEqual(0, SlotCompatibility.AllowedTypes(null).Count);
            Assert.AreEqual(0, SlotCompatibility.AllowedTypes(string.Empty).Count);
        }

        [Test]
        public void Accepts_AppliesHullAffinity_TransportOnly_AndNeverAHull()
        {
            HullModule slot = Slot("Scanner Electrical Mechanical");
            List<HullModule> hull = new List<HullModule> { slot, Slot("Weapon") };

            Component hullPart = Part(ItemType.Hull, "A Hull");
            hullPart.Properties.Add("Hull", new Hull());
            Assert.IsFalse(SlotCompatibility.Accepts(hullPart, Slot(SlotCompatibility.GeneralPurposeSlot), "Scout", hull), "a hull never goes in a slot");

            Component bound = Part(ItemType.Mechanical, "Bound Part");
            bound.Properties.Add("Hull Affinity", new HullAffinity("Nubian"));
            Assert.IsTrue(SlotCompatibility.Accepts(bound, slot, "Nubian", hull));
            Assert.IsFalse(SlotCompatibility.Accepts(bound, slot, "Scout", hull));

            Component transportOnly = Part(ItemType.Mechanical, "Cargo Pod");
            transportOnly.Properties.Add("Transport Ships Only", new SimpleProperty());
            Assert.IsTrue(SlotCompatibility.Accepts(transportOnly, slot, "Scout", hull), "no weapon on the hull yet");

            Component weapon = Part(ItemType.BeamWeapons, "Laser");
            weapon.Properties.Add("Weapon", new Weapon());
            hull[1].AllocatedComponent = weapon;
            Assert.IsFalse(SlotCompatibility.Accepts(transportOnly, slot, "Scout", hull), "an allocated weapon bars a transport-only part");
            Assert.IsTrue(SlotCompatibility.Accepts(Part(ItemType.Scanner), slot, "Scout", hull));
            Assert.IsFalse(SlotCompatibility.Accepts(null, slot, "Scout", hull));
        }
    }
}
