namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Panel rules kept in Nova.Client (the Avalonia view models are not referenced by Tests):
    /// - message-click routing (client-ui-dialog-catalog.md "Message-click routing": 62/63 open the
    ///   named planet's production queue), and the production notices carrying that planet;
    /// - battle-plan delete / rename keeping the fleets' assignments consistent
    ///   (client-interface.md "Battle Plans and Relations dialogs"), and the BattlePlansCommand;
    /// - rename surfaces (client-ui-dialog-catalog.md "Rename surfaces");
    /// - the plain-text report exports (client-ui-dialog-catalog.md "Reports");
    /// - the Technology Browser model, the victory summary, the minefield display options.
    /// </summary>
    [TestFixture]
    public class ClientPanelRulesTest
    {
        // ---------------- message routing ----------------

        [Test]
        public void QueueNotices_SelectOnTheFirstPress_ThenOpenTheQueueOnTheSecond()
        {
            foreach (string type in new[] { ProductionNoticeTypes.QueueEmpty, ProductionNoticeTypes.OrdersCompleted })
            {
                MessageGoto state = new MessageGoto();
                state.Select(new Message(1, "text", type, "Tierra"));

                Assert.AreEqual(MessageRouting.GotoCaption, state.Caption, type);
                MessageDestination first = state.Press();
                Assert.AreEqual(MessageDestinationKind.Planet, first.Kind, type);
                Assert.AreEqual("Tierra", first.PlanetName);
                Assert.AreEqual(MessageRouting.ViewCaption, state.Caption, "the caption becomes View");

                MessageDestination second = state.Press();
                Assert.AreEqual(MessageDestinationKind.ProductionQueue, second.Kind, type);
                Assert.AreEqual(MessageRouting.GotoCaption, state.Caption, "the flag is cleared again");
            }
        }

        [Test]
        public void MovingToAnotherMessage_ClearsTheSecondPressFlag()
        {
            MessageGoto state = new MessageGoto();
            state.Select(new Message(1, "a", ProductionNoticeTypes.QueueEmpty, "Tierra"));
            state.Press();
            Assert.IsTrue(state.ViewArmed);

            state.Select(new Message(1, "b", ProductionNoticeTypes.OrdersCompleted, "Mars"));
            Assert.IsFalse(state.ViewArmed, "a new message resets the button to Goto");
            Assert.AreEqual(MessageRouting.GotoCaption, state.Caption);
        }

        [Test]
        public void OtherProductionNotices_OnlySelectThePlanet()
        {
            MessageDestination destination = MessageRouting.Destination(new Message(1, "built", ProductionNoticeTypes.Production, "Tierra"));
            Assert.AreEqual(MessageDestinationKind.Planet, destination.Kind);
            Assert.AreEqual("Tierra", destination.PlanetName);
            Assert.IsNull(destination.SecondPressKind, "only the queue-empty notices have a second press");
        }

        [Test]
        public void BattleAndTechMessages_Route_AndOthersGoNowhere()
        {
            Assert.AreEqual(MessageDestinationKind.BattleReplay, MessageRouting.Destination(new Message(1, "b", "BattleReport", new BattleReport())).Kind);
            Assert.AreEqual(MessageDestinationKind.Research, MessageRouting.Destination(new Message(1, "t", "TechAdvance", null)).Kind);
            Assert.AreEqual(MessageDestinationKind.None, MessageRouting.Destination(new Message(1, "x", "Minefield", null)).Kind);
            Assert.AreEqual(MessageDestinationKind.None, MessageRouting.Destination(null).Kind);
        }

        [Test]
        public void Manufacture_PostsQueueNotices_WithTheirOwnTypes_AndThePlanetAsSubject()
        {
            Race race = new Race();
            race.Traits.SetPrimary("JOAT");
            ServerData server = new ServerData();
            EmpireData empire = new SimpleEmpireData { Id = 1, Race = race, AvailableComponents = new RaceComponents() };
            server.AllEmpires.Add(empire.Id, empire);
            Star star = new Star { Name = "Tierra", Owner = 1, ThisRace = race, Colonists = 100000 };

            new Manufacture(server).Items(star);

            Message notice = server.AllMessages.Single();
            Assert.AreEqual(ProductionNoticeTypes.QueueEmpty, notice.Type);
            Assert.AreEqual("Tierra", notice.Event);

            // The subject survives the turn file.
            XmlDocument xmldoc = new XmlDocument();
            Message reloaded = new Message(notice.ToXml(xmldoc));
            Assert.AreEqual(ProductionNoticeTypes.QueueEmpty, reloaded.Type);
            Assert.AreEqual("Tierra", reloaded.Event);
        }

        // ---------------- battle plans ----------------

        private static Dictionary<string, BattlePlan> ThreePlans()
        {
            return new Dictionary<string, BattlePlan>
            {
                { "Default", new BattlePlan { Name = "Default" } },
                { "Kill Starbase", new BattlePlan { Name = "Kill Starbase" } },
                { "Run", new BattlePlan { Name = "Run" } },
            };
        }

        [Test]
        public void DeletingAnAssignedPlan_NeedsConfirmation_AndMovesItsFleetsToThePlanAbove()
        {
            Dictionary<string, BattlePlan> plans = ThreePlans();
            Fleet onIt = new Fleet(1) { BattlePlan = "Kill Starbase" };
            Fleet onLater = new Fleet(2) { BattlePlan = "Run" };
            List<Fleet> fleets = new List<Fleet> { onIt, onLater };

            Assert.IsTrue(BattlePlanRules.DeleteNeedsConfirmation("Kill Starbase", fleets));
            Assert.IsFalse(BattlePlanRules.DeleteNeedsConfirmation("Default2", fleets));

            List<Fleet> moved = BattlePlanRules.Delete(plans, "Kill Starbase", fleets);

            CollectionAssert.AreEqual(new[] { onIt }, moved);
            Assert.AreEqual("Default", onIt.BattlePlan, "the plan just above the deleted one");
            Assert.AreEqual("Run", onLater.BattlePlan, "a fleet on a later plan keeps its own plan");
            CollectionAssert.AreEqual(new[] { "Default", "Run" }, plans.Keys.ToArray());
        }

        [Test]
        public void DeletingTheLastPlan_MovesItsFleetsToThePlanAbove_NotTheFirst()
        {
            Dictionary<string, BattlePlan> plans = ThreePlans();
            Fleet onLast = new Fleet(1) { BattlePlan = "Run" };
            Fleet onFirst = new Fleet(2) { BattlePlan = "Default" };
            List<Fleet> fleets = new List<Fleet> { onLast, onFirst };

            List<Fleet> moved = BattlePlanRules.Delete(plans, "Run", fleets);

            CollectionAssert.AreEqual(new[] { onLast }, moved);
            Assert.AreEqual("Kill Starbase", onLast.BattlePlan, "the plan just above the deleted last plan");
            Assert.AreEqual("Default", onFirst.BattlePlan);
            CollectionAssert.AreEqual(new[] { "Default", "Kill Starbase" }, plans.Keys.ToArray());
        }

        [Test]
        public void TheFirstPlan_CannotBeDeleted()
        {
            Dictionary<string, BattlePlan> plans = ThreePlans();
            Assert.IsFalse(BattlePlanRules.CanDelete(plans, "Default"));
            Assert.IsEmpty(BattlePlanRules.Delete(plans, "Default", new List<Fleet>()));
            Assert.AreEqual(3, plans.Count);
        }

        [Test]
        public void Renaming_KeepsTheOrder_FollowsTheFleets_AndRefusesDuplicates()
        {
            Dictionary<string, BattlePlan> plans = ThreePlans();
            Fleet fleet = new Fleet(1) { BattlePlan = "Default" };
            List<Fleet> fleets = new List<Fleet> { fleet };

            Assert.IsNull(BattlePlanRules.Rename(plans, "Default", "Home Guard", fleets));
            CollectionAssert.AreEqual(new[] { "Home Guard", "Kill Starbase", "Run" }, plans.Keys.ToArray());
            Assert.AreEqual("Home Guard", plans["Home Guard"].Name);
            Assert.AreEqual("Home Guard", fleet.BattlePlan);

            Assert.IsNotNull(BattlePlanRules.Rename(plans, "Run", "Kill Starbase", fleets), "a duplicate name is refused");
            Assert.IsNotNull(BattlePlanRules.Rename(plans, "Run", "   ", fleets), "a blank name is refused");
            Assert.IsTrue(plans.ContainsKey("Run"));
        }

        [Test]
        public void BattlePlansCommand_CarriesPlansAndAssignments_ToTheServer()
        {
            EmpireData client = new EmpireData { Id = 1, Race = new Race() };
            client.BattlePlans.Add("Run", new BattlePlan { Name = "Run", Tactic = "Disengage" });
            Fleet fleet = new Fleet(7) { BattlePlan = "Run" };
            client.OwnedFleets.Add(fleet);

            BattlePlansCommand command = new BattlePlansCommand(client.BattlePlans.Values, new Dictionary<long, string> { { fleet.Key, "Run" } });
            XmlDocument xmldoc = new XmlDocument();
            BattlePlansCommand read = new BattlePlansCommand(command.ToXml(xmldoc));

            EmpireData server = new EmpireData { Id = 1, Race = new Race() };
            Fleet serverFleet = new Fleet(7);
            server.OwnedFleets.Add(serverFleet);
            Fleet unlisted = new Fleet(8) { BattlePlan = "Gone" };
            server.OwnedFleets.Add(unlisted);

            Assert.IsTrue(read.IsValid(server));
            read.ApplyToState(server);

            CollectionAssert.AreEqual(new[] { "Default", "Run" }, server.BattlePlans.Keys.ToArray());
            Assert.AreEqual("Disengage", server.BattlePlans["Run"].Tactic);
            Assert.AreEqual("Run", serverFleet.BattlePlan);
            Assert.AreEqual("Default", unlisted.BattlePlan, "a fleet naming a missing plan falls back to the first plan");
        }

        [Test]
        public void BattlePlansCommand_RefusesDuplicateNames_AndOverTheLimit()
        {
            EmpireData empire = new EmpireData { Id = 1, Race = new Race() };
            BattlePlansCommand duplicate = new BattlePlansCommand(
                new[] { new BattlePlan { Name = "A" }, new BattlePlan { Name = "A" } }, null);
            Assert.IsFalse(duplicate.IsValid(empire));

            BattlePlansCommand tooMany = new BattlePlansCommand(
                Enumerable.Range(0, Global.MaxBattlePlans + 1).Select(i => new BattlePlan { Name = "P" + i }), null);
            Assert.IsFalse(tooMany.IsValid(empire));
        }

        [Test]
        public void BattlePlanOrders_KeepOnlyTheNewestPlansCommand()
        {
            ClientData clientState = new ClientData { EmpireState = new EmpireData { Id = 1, Race = new Race() } };
            clientState.Commands.Push(new ResearchCommand());
            BattlePlanOrders.Queue(clientState);
            clientState.Commands.Push(new ResearchCommand());
            BattlePlansCommand newest = BattlePlanOrders.Queue(clientState);

            Assert.AreEqual(1, clientState.Commands.OfType<BattlePlansCommand>().Count());
            Assert.AreSame(newest, clientState.Commands.Peek());
            Assert.AreEqual(3, clientState.Commands.Count);
        }

        // ---------------- rename surfaces ----------------

        [Test]
        public void RenameSurfaces_KeepEveryCharacter_EnforceTheLimits_AndHandleEmpty()
        {
            // No character is removed or rewritten: spaces, punctuation and ampersands survive.
            Assert.AreEqual("A & B\tC", RenameRules.LimitForEdit("A & B\tC", RenameSurface.Fleet));

            // The spec's length limits: fleet 31, template / zip order 12, battle plan 31.
            Assert.AreEqual(31, RenameRules.LimitForEdit(new string('x', 40), RenameSurface.Fleet).Length);
            Assert.AreEqual(31, RenameRules.LimitForEdit(new string('x', 40), RenameSurface.BattlePlan).Length);
            Assert.AreEqual(12, RenameRules.LimitForEdit(new string('x', 40), RenameSurface.ProductionTemplate).Length);
            Assert.AreEqual(12, RenameRules.LimitForEdit(new string('x', 40), RenameSurface.ZipOrder).Length);

            // The fleet surface also cuts from the end until the text is <= 160 px wide.
            Assert.AreEqual("ABC", RenameRules.LimitFleetForEdit("ABCDE", text => text.Length * 50));

            // Accept rules: fleet empty clears the custom name; template empty becomes "custom
            // number N"; a battle-plan name is copied unchanged.
            Assert.AreEqual(string.Empty, RenameRules.AcceptFleetName(string.Empty));
            Assert.IsTrue(RenameRules.FleetNameClearsCustom(string.Empty));
            Assert.AreEqual("custom number 2", RenameRules.AcceptTemplateName(string.Empty, 2));
            Assert.AreEqual("Keep & Me", RenameRules.AcceptBattlePlanName("Keep & Me"));

            // The production-template manager's slot 0 is locked; the zip-order manager is not.
            Assert.IsFalse(RenameRules.CanRename(RenameSurface.ProductionTemplate, 0));
            Assert.IsTrue(RenameRules.CanRename(RenameSurface.ProductionTemplate, 1));
            Assert.IsTrue(RenameRules.CanRename(RenameSurface.ZipOrder, 0));

            // A slot's button caption doubles the ampersand.
            Assert.AreEqual("A && B", RenameRules.ButtonCaption("A & B"));
        }

        // ---------------- exports ----------------

        private static EmpireData ExportEmpire()
        {
            EmpireData empire = new EmpireData { Id = 1, Race = new Race { Name = "Humanoid" } };
            Star star = new Star { Name = "Tierra", Owner = 1, Colonists = 25000 };
            star.ResourcesOnHand = new Resources(10, 20, 30, 0);
            star.MineralConcentration = new Resources(40, 50, 60, 0);
            empire.OwnedStars.Add(star);
            Fleet fleet = new Fleet("Scout #1", 1, 1, new NovaPoint(100, 200));
            empire.OwnedFleets.Add(fleet);
            return empire;
        }

        [Test]
        public void PlanetExport_IsTabSeparated_WithAHeaderRow()
        {
            string[] lines = ReportExport.Planets(ExportEmpire()).Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries);

            Assert.AreEqual(2, lines.Length);
            CollectionAssert.AreEqual(ReportExport.PlanetHeader, lines[0].Split('\t'));
            string[] cells = lines[1].Split('\t');
            Assert.AreEqual(ReportExport.PlanetHeader.Length, cells.Length);
            Assert.AreEqual("Tierra", cells[0]);
            Assert.AreEqual("Humanoid", cells[1]);
            Assert.AreEqual("25000", cells[2]);
            CollectionAssert.AreEqual(new[] { "10", "20", "30", "40", "50", "60" }, cells.Skip(5).ToArray());
        }

        [Test]
        public void FleetExport_IsRowNumberPositionName()
        {
            string[] lines = ReportExport.Fleets(ExportEmpire()).Split(new[] { System.Environment.NewLine }, System.StringSplitOptions.RemoveEmptyEntries);

            CollectionAssert.AreEqual(new[] { "1", "100", "200", "Scout #1" }, lines[1].Split('\t'));
            Assert.AreEqual("Humanoid.fleets.txt", ReportExport.FileName("Humanoid", ReportExport.Kind.Fleets));
        }

        // ---------------- technology browser ----------------

        private static Component MakeComponent(string name, ItemType type)
        {
            return new Component { Name = name, Type = type };
        }

        [Test]
        public void TechBrowser_HasAllPlusSixteenCategories_AndPagesWithWrap()
        {
            List<Component> components = new List<Component>
            {
                MakeComponent("Armor A", ItemType.Armor),
                MakeComponent("Armor B", ItemType.Armor),
                MakeComponent("Engine A", ItemType.Engine),
                MakeComponent("Torpedo A", ItemType.Torpedoes),
            };
            TechBrowser browser = new TechBrowser(components, _ => true);

            Assert.AreEqual(17, browser.Categories.Count);
            Assert.AreEqual("All", browser.Categories[0]);
            Assert.AreEqual("Armor", browser.Categories[1]);
            Assert.AreEqual("Torpedoes", browser.Categories[16]);

            // Opens on All with Armor item 0.
            Assert.AreEqual("All", browser.Category);
            Assert.AreEqual("Armor A", browser.Current.Name);

            // Within a specific category paging wraps.
            browser.SetCategory(1);
            Assert.AreEqual("Armor A", browser.Current.Name);
            browser.Previous();
            Assert.AreEqual("Armor B", browser.Current.Name, "wraps to the last item");
            browser.Next();
            Assert.AreEqual("Armor A", browser.Current.Name);

            // Under All, paging runs on into the next category and wraps from Torpedoes to Armor.
            browser.SetCategory(0);
            Assert.AreEqual("Armor A", browser.Current.Name);
            browser.Previous();
            Assert.AreEqual("Torpedo A", browser.Current.Name, "wraps from the first back to the last");
            browser.Next();
            Assert.AreEqual("Armor A", browser.Current.Name);
        }

        [Test]
        public void TechBrowser_ShowsUnavailableParts_ButHidesUnreceivedGiftParts()
        {
            List<Component> components = new List<Component>
            {
                MakeComponent("Armor A", ItemType.Armor),
                MakeComponent("Multi Cargo Pod", ItemType.Mechanical),
            };
            bool received = false;
            TechBrowser browser = new TechBrowser(components, component => component.Name == "Armor A", _ => received);

            // Checkbox clear: every existing part is shown except an unreceived gift part.
            browser.SetCategory(6);
            Assert.AreEqual(0, browser.Entries.Count, "the unreceived gift part is skipped");

            // A received gift part is shown with the checkbox clear; toggling the checkbox forces
            // the gift predicate to be re-read.
            received = true;
            browser.ShowOnlyAvailable = true;
            Assert.AreEqual(0, browser.Entries.Count, "the received gift part is still not buildable");
            browser.ShowOnlyAvailable = false;
            Assert.AreEqual(1, browser.Entries.Count, "a received gift part is shown");

            // Showing a hidden entry clears the filter.
            browser.ShowOnlyAvailable = true;
            Assert.IsTrue(browser.Show("Multi Cargo Pod"));
            Assert.IsFalse(browser.ShowOnlyAvailable);
            Assert.AreEqual("Multi Cargo Pod", browser.Current.Name);
        }

        [Test]
        public void TechBrowser_LevelsLine_UsesTheBrowsersLabels()
        {
            TechLevel levels = new TechLevel(1, 2, 3, 4, 5, 6);
            string line = TechBrowser.LevelsLine(levels);

            StringAssert.StartsWith("Ener: " + levels[TechLevel.ResearchField.Energy], line);
            foreach (string label in new[] { "Weap:", "Prop:", "Const:", "Elect:", "Bio:" })
            {
                StringAssert.Contains(label, line);
            }

            Assert.AreEqual("None", TechBrowser.RequirementLine(new TechLevel()));
        }

        // ---------------- victory summary ----------------

        [Test]
        public void VictorySummary_ListsTheSevenConditions_AndClampsNOfThem()
        {
            GameSettings settings = GameSettings.Data;
            int savedTargets = settings.TargetsToMeet;
            try
            {
                settings.TargetsToMeet = 8;

                List<VictorySummaryRow> rows = VictorySummary.Rows(settings);

                Assert.AreEqual(7, rows.Count, "seven toggleable conditions; 'in N fields' rides on the tech-level row");
                int enabled = rows.Count(row => row.Enabled);
                Assert.AreEqual(enabled, VictorySummary.EnabledCount(settings));
                Assert.AreEqual(System.Math.Max(1, enabled), VictorySummary.ConditionsToMeet(settings), "min(N, enabled count)");
                StringAssert.Contains(settings.NumberOfFields.NumericValue + " fields", rows[1].Threshold);
                StringAssert.Contains((Global.StartingYear + settings.MinimumGameTime).ToString(), VictorySummary.YearGateLine(settings));
            }
            finally
            {
                settings.TargetsToMeet = savedTargets;
            }
        }

        // ---------------- minefield display ----------------

        [Test]
        public void MinefieldInspector_HasNoDisplaySelector_OnlyTheFieldRows()
        {
            Minefield field = new Minefield { NumberOfMines = 400, FieldType = MinefieldType.Heavy, Position = new NovaPoint(1, 2) };

            // behavior-specs-11: the minefield pane's only control is the detonate checkbox; the
            // old Field/Transit display selector (and its config key) is gone.
            Dictionary<string, string> rows = MinefieldDisplay.Rows(field, "You").ToDictionary(r => r.Key, r => r.Value);
            Assert.AreEqual("20 ly", rows["Radius"]);
            Assert.AreEqual("Heavy", rows["Type"]);
            Assert.AreEqual("No", rows["Detonating"]);
            Assert.IsFalse(rows.ContainsKey("Safe speed"), "no Transit rows: the display selector is retracted");
            Assert.IsFalse(rows.ContainsKey("Hit chance"));
            Assert.IsNull(typeof(MinefieldDisplay).GetProperty("OptionLabels", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static), "the display options are gone");
        }

        // ---------------- research next field ----------------

        [Test]
        public void NextFieldChoices_AreSame_TheSixFieldsInTheOriginalOrder_AndLowest()
        {
            List<KeyValuePair<string, int>> choices = ResearchNextField.Choices();

            Assert.AreEqual(8, choices.Count);
            Assert.AreEqual(Research.NextFieldSame, choices[0].Value);
            CollectionAssert.AreEqual(Research.OriginalFieldOrder.Select(field => (int)field).ToArray(), choices.Skip(1).Take(6).Select(choice => choice.Value).ToArray());
            Assert.AreEqual(Research.NextFieldLowest, choices[7].Value);
            Assert.AreEqual(7, ResearchNextField.IndexOf(Research.NextFieldLowest));
            Assert.AreEqual(0, ResearchNextField.IndexOf(99), "an unknown setting shows as Same");
        }

        // ---------------- production captions / templates ----------------

        [Test]
        public void TerraformCaptions_FollowTheItemType()
        {
            Race race = new Race();
            Assert.AreEqual("Terraform Environment", ProductionCaptions.Of(new ProductionOrder(1, new TerraformProductionUnit(race), false)));
            Assert.AreEqual("Max Terraform", ProductionCaptions.Of(new ProductionOrder(1, new TerraformProductionUnit(race, false), true)));
            Assert.AreEqual("Min Terraform", ProductionCaptions.Of(new ProductionOrder(1, new TerraformProductionUnit(race, true), true)));
            Assert.AreEqual("Factory", ProductionCaptions.Of(new ProductionOrder(1, new FactoryProductionUnit(race), false)));
        }

        [Test]
        public void TemplateOrders_ApplyLocally_AndKeepOneOrderPerSlot()
        {
            ClientData clientState = new ClientData { EmpireState = new EmpireData { Id = 1, Race = new Race() } };
            ProductionTemplate first = new ProductionTemplate { Name = "A" };
            ProductionTemplate second = new ProductionTemplate { Name = "B" };

            TemplateOrders.SetSlot(clientState, 1, first);
            TemplateOrders.SetSlot(clientState, 2, first);
            TemplateOrders.SetSlot(clientState, 1, second);
            TemplateOrders.SetDefault(clientState, 2);
            TemplateOrders.SetDefault(clientState, 1);

            Assert.AreEqual("B", clientState.EmpireState.ProductionTemplates[1].Name);
            Assert.AreEqual(1, clientState.EmpireState.ProductionTemplates.DefaultSlot);
            List<ProductionTemplateCommand> orders = clientState.Commands.OfType<ProductionTemplateCommand>().ToList();
            Assert.AreEqual(3, orders.Count, "slot 1 (newest), slot 2, one default choice");
            Assert.AreEqual(1, orders.Count(order => order.Slot == 1));
            Assert.AreEqual(1, orders.Count(order => order.DefaultSlot.HasValue));
        }

        // ---------------- window placement ----------------

        [Test]
        public void WindowPlacement_RoundTrips_AndRejectsJunk()
        {
            WindowPlacement placement = WindowPlacement.Parse(new WindowPlacement(10, -20, 1200, 800, true).Format());

            Assert.AreEqual(10, placement.X);
            Assert.AreEqual(-20, placement.Y);
            Assert.AreEqual(1200, placement.Width);
            Assert.AreEqual(800, placement.Height);
            Assert.IsTrue(placement.Maximized);
            Assert.IsNull(WindowPlacement.Parse("1,2,3"));
            Assert.IsNull(WindowPlacement.Parse("0,0,50,50,0"), "a collapsed window is not restored");
            Assert.IsNull(WindowPlacement.Parse(null));
        }
    }
}
