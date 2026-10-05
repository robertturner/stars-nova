namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;

    /// <summary>
    /// save-turn-file-format.md coverage row 22 (section 3, host step 6): a design record that
    /// replaces an existing design which is in use and has ships in existence fails; the port
    /// rejects the order rather than aborting the whole turn generation. The rule is scoped to
    /// hull (non-starbase) designs, matching the spec's own "0-15 for ships and 16-25 for
    /// starbases" phrasing; in-use starbase designs stay under the Alternate Reality rule in
    /// <see cref="DesignCommand.IsValid"/>.
    /// </summary>
    [TestFixture]
    public class DesignCommandTest
    {
        private static EmpireData Empire(string primaryTrait)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = 1;
            empire.Race = new Race();
            empire.Race.Traits.SetPrimary(primaryTrait);
            return empire;
        }

        private static ShipDesign Design(EmpireData empire, ItemType type, string name)
        {
            ShipDesign design = new ShipDesign(empire.GetNextDesignKey()) { Type = type, Name = name };
            design.Blueprint = new Component();
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>() });
            empire.Designs.Add(design.Key, design);
            return design;
        }

        private static ShipDesign Replacement(ShipDesign existing, string name)
        {
            ShipDesign design = new ShipDesign(existing.Key) { Type = existing.Type, Name = name };
            design.Blueprint = new Component();
            design.Blueprint.Properties.Add("Hull", new Hull { Modules = new List<HullModule>() });
            return design;
        }

        private static void PutShipsInExistence(EmpireData empire, ShipDesign design)
        {
            Star star = new Star { Name = "Home", Owner = empire.Id };
            Fleet fleet = new Fleet(new ShipToken(design, 1), star, empire.GetNextFleetKey());
            empire.AddOrUpdateFleet(fleet);
        }

        [Test]
        public void ReplacingAShipDesignWithShipsInExistence_IsRejected()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign ship = Design(empire, ItemType.Ship, "Warship");
            PutShipsInExistence(empire, ship);

            Assert.IsFalse(new DesignCommand(CommandMode.Edit, Replacement(ship, "Warship II")).IsValid(empire));
        }

        [Test]
        public void ReplacingAnUnusedShipDesign_IsAllowed()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign ship = Design(empire, ItemType.Ship, "Warship");

            Assert.IsTrue(new DesignCommand(CommandMode.Edit, Replacement(ship, "Warship II")).IsValid(empire));
        }

        [Test]
        public void ReplacingAStarbaseDesignWithAStarbaseInExistence_IsStillAllowed()
        {
            // save-turn-file-format.md section 3 uses "ships" for the 0-15 hull slots and
            // "starbases" separately for 16-25; an in-use starbase design is governed by the
            // Alternate Reality rule (IsProtectedAlternateRealityDesign), not this host step 6
            // ship rule.
            EmpireData empire = Empire("JOAT");
            ShipDesign station = Design(empire, ItemType.Starbase, "Starbase");
            PutShipsInExistence(empire, station);

            Assert.IsTrue(new DesignCommand(CommandMode.Edit, Replacement(station, "Starbase II")).IsValid(empire));
        }

        [Test]
        public void DeletingAShipDesignWithShipsInExistence_IsStillAllowed()
        {
            // The rule covers replacing; deleting a design still scraps the ships built to it.
            EmpireData empire = Empire("JOAT");
            ShipDesign ship = Design(empire, ItemType.Ship, "Warship");
            PutShipsInExistence(empire, ship);

            Assert.IsTrue(new DesignCommand(CommandMode.Delete, ship.Key).IsValid(empire));
        }

        [Test]
        public void AddingADesignWhoseKeyIsAlreadyHeld_IsRejected()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign ship = Design(empire, ItemType.Ship, "Warship");

            Assert.IsFalse(new DesignCommand(CommandMode.Add, Replacement(ship, "Warship II")).IsValid(empire));
        }

        [Test]
        public void EditingADesignTheEmpireDoesNotHold_IsRejected()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign absent = new ShipDesign(42) { Type = ItemType.Ship, Name = "Ghost" };

            Assert.IsFalse(new DesignCommand(CommandMode.Edit, absent).IsValid(empire));
        }

        [Test]
        public void IsShipDesignInUse_ReadsTheEmpiresOwnFleetCompositions()
        {
            EmpireData empire = Empire("JOAT");
            ShipDesign ship = Design(empire, ItemType.Ship, "Warship");
            ShipDesign spare = Design(empire, ItemType.Ship, "Spare");

            Assert.IsFalse(DesignCommand.IsShipDesignInUse(empire, ship.Key));
            PutShipsInExistence(empire, ship);

            Assert.IsTrue(DesignCommand.IsShipDesignInUse(empire, ship.Key));
            Assert.IsFalse(DesignCommand.IsShipDesignInUse(empire, spare.Key));
        }
    }
}
