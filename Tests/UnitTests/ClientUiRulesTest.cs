namespace Nova.Tests.UnitTests
{
    using System.Collections.Generic;
    using System.IO;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The UI rules behind the Avalonia panels, kept in Nova.Client so they are testable:
    /// - message filter: behavior-specs-10/client-ui-dialog-catalog.md, Messages (sibling groups,
    ///   all-clear on open, "present this turn" magnifier, Next/Previous skipping, mode switch);
    /// - detail-card status line: behavior-specs-10/research-tech-tree.md section 4 (unavailable /
    ///   available / resources still needed / thousands with "k");
    /// - waypoint selection after a delete: behavior-specs-10/client-interface.md command table,
    ///   ids 103/104.
    /// </summary>
    [TestFixture]
    public class ClientUiRulesTest
    {
        // ---------------- message filter ----------------

        [Test]
        public void MessageFilter_StartsAllClear()
        {
            MessageFilter filter = new MessageFilter();

            Assert.IsFalse(filter.IsFiltered("Minefield"));
            Assert.IsFalse(filter.ShowFiltered, "default mode hides filtered messages");
            Assert.IsEmpty(filter.FilteredGroups);
        }

        [Test]
        public void MessageFilter_BattleReportTypes_AreOneSiblingGroup()
        {
            MessageFilter filter = new MessageFilter();
            filter.Toggle("BattleReport");

            Assert.IsTrue(filter.IsFiltered("BattleReport"));
            Assert.IsTrue(filter.IsFiltered("BattleSummary"), "145-168 run: all battle summaries together");
            Assert.IsTrue(filter.IsFiltered("Battle"));
            Assert.IsFalse(filter.IsFiltered("Bombing"), "an unlisted type is its own group");

            filter.Toggle("Battle");
            Assert.IsFalse(filter.IsFiltered("BattleSummary"), "toggling any sibling flips the whole group back");
        }

        [Test]
        public void MessageFilter_UntypedMessages_ShareTheGeneralGroup()
        {
            Assert.AreEqual(MessageFilter.GeneralGroup, MessageFilter.GroupOf(null));
            Assert.AreEqual(MessageFilter.GeneralGroup, MessageFilter.GroupOf(""));
            Assert.AreEqual("Stargate", MessageFilter.GroupOf("Stargate"));
        }

        [Test]
        public void MessageFilter_Magnifier_OnlyWhenATypePresentThisTurnIsFiltered()
        {
            MessageFilter filter = new MessageFilter();
            filter.Toggle("Comet");

            Assert.IsFalse(filter.IsMagnifierVisible(new[] { "Minefield", "Stargate" }), "filtered type not present this turn");
            Assert.IsTrue(filter.IsMagnifierVisible(new[] { "Minefield", "Comet" }));
        }

        [Test]
        public void MessageFilter_NextAndPrevious_SkipFilteredTypes_OnlyInHideMode()
        {
            var types = new List<string> { "A", "Battle", "B", "BattleSummary", "C" };
            MessageFilter filter = new MessageFilter();
            filter.Toggle("Battle");

            Assert.AreEqual(0, filter.First(types));
            Assert.AreEqual(2, filter.Next(types, 0));
            Assert.AreEqual(4, filter.Next(types, 2));
            Assert.AreEqual(-1, filter.Next(types, 4));
            Assert.AreEqual(2, filter.Previous(types, 4));
            Assert.AreEqual(-1, filter.Previous(types, 0));

            filter.ShowFiltered = true;
            Assert.AreEqual(1, filter.Next(types, 0), "show mode steps through every message");
            Assert.AreEqual(3, filter.Previous(types, 4));
        }

        [Test]
        public void MessageFilter_ModeSwitch_KeepsOrMovesTheSelection()
        {
            var types = new List<string> { "A", "Battle", "B", "Battle", "C" };
            MessageFilter filter = new MessageFilter();
            filter.Toggle("Battle");

            // To show mode: the current message stays only if its type is filtered; else the next
            // filtered one, else the previous filtered one.
            Assert.AreEqual(1, filter.ToggleShowFiltered(types, 0), "next filtered message");
            Assert.IsTrue(filter.ShowFiltered);

            // Back to hide mode from a filtered message: next unfiltered one.
            Assert.AreEqual(2, filter.ToggleShowFiltered(types, 1));
            Assert.IsFalse(filter.ShowFiltered);

            // To show mode from the last message: none follows, so the previous filtered one.
            Assert.AreEqual(3, filter.ToggleShowFiltered(types, 4));

            // Already filtered when switching to show mode: stays.
            filter.ShowFiltered = false;
            Assert.AreEqual(3, filter.ToggleShowFiltered(types, 3));
        }

        [Test]
        public void MessageFilter_PersistsPerPlayer_AndALoadWithNoFileIsAllClear()
        {
            string folder = Path.Combine(Path.GetTempPath(), "nova-msgfilter-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string path = MessageFilter.FilePath(folder, "Humanoid");
                Assert.IsEmpty(MessageFilter.Load(path).FilteredGroups, "no saved filter: all clear");

                MessageFilter filter = new MessageFilter();
                filter.Toggle("BattleSummary");
                filter.Toggle("Minefield");
                filter.ShowFiltered = true;
                filter.Save(path);

                MessageFilter restored = MessageFilter.Load(path);
                Assert.IsTrue(restored.IsFiltered("BattleReport"));
                Assert.IsTrue(restored.IsFiltered("Minefield"));
                Assert.IsFalse(restored.IsFiltered("Stargate"));
                Assert.IsFalse(restored.ShowFiltered, "the hide/show mode itself starts at the default");

                Assert.IsEmpty(MessageFilter.Load(MessageFilter.FilePath(folder, "Other")).FilteredGroups, "per player");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        // ---------------- message filter: the spec's numeric type-to-group map ----------------

        [Test]
        public void MessageFilter_NumericGroupMap_MatchesTheSpecsCompleteMap()
        {
            // behavior-specs-11/client-ui-dialog-catalog.md, Messages, "Complete type-to-group
            // map": group key = the lowest type in the group; every one of the 387 types is in
            // exactly one group.
            Assert.AreEqual(387, MessageFilter.TypeCount);

            // Runs.
            foreach (int type in new[] { 43, 44, 45, 46 })
            {
                Assert.AreEqual(43, MessageFilter.GroupKey(type), $"groups of {type}");
            }

            foreach (int type in new[] { 96, 97, 98, 99, 100 })
            {
                Assert.AreEqual(96, MessageFilter.GroupKey(type), $"groups of {type}");
            }

            foreach (int type in new[] { 106, 107, 108, 109, 110 })
            {
                Assert.AreEqual(106, MessageFilter.GroupKey(type), $"groups of {type}");
            }

            for (int type = 145; type <= 168; type++)
            {
                Assert.AreEqual(145, MessageFilter.GroupKey(type), $"groups of {type}");
            }

            // Pairs.
            Assert.AreEqual(47, MessageFilter.GroupKey(48));
            Assert.AreEqual(53, MessageFilter.GroupKey(54));
            Assert.AreEqual(55, MessageFilter.GroupKey(56));
            Assert.AreEqual(57, MessageFilter.GroupKey(58));
            Assert.AreEqual(66, MessageFilter.GroupKey(67));
            Assert.AreEqual(68, MessageFilter.GroupKey(69));
            Assert.AreEqual(70, MessageFilter.GroupKey(71));
            Assert.AreEqual(72, MessageFilter.GroupKey(73));
            Assert.AreEqual(74, MessageFilter.GroupKey(75));
            Assert.AreEqual(76, MessageFilter.GroupKey(77));
            Assert.AreEqual(121, MessageFilter.GroupKey(122));

            // Types the spec calls out as deliberately not grouped are their own group.
            foreach (int type in new[] { 49, 50, 51, 52, 101, 102, 103, 104, 105 })
            {
                Assert.AreEqual(type, MessageFilter.GroupKey(type), $"{type} is a group of one");
            }

            // Spot checks across the singletons.
            foreach (int type in new[] { 0, 42, 59, 65, 78, 95, 111, 120, 123, 144, 169, 386 })
            {
                Assert.AreEqual(type, MessageFilter.GroupKey(type));
            }
        }

        [Test]
        public void MessageFilter_TogglingOneNumericType_FlipsItsWholeGroup_AndHidesThem()
        {
            MessageFilter filter = new MessageFilter();

            // The battle-summary run: toggling any member filters all of 145-168 and nothing else.
            filter.Toggle(150);

            Assert.IsTrue(filter.IsFiltered(145));
            Assert.IsTrue(filter.IsFiltered(168));
            Assert.IsFalse(filter.IsFiltered(144));
            Assert.IsFalse(filter.IsFiltered(169));

            // In hide mode the whole group is skipped; a lone type is still shown.
            var types = new List<int> { 1, 146, 2, 151, 3 };
            Assert.AreEqual(0, filter.First(types));
            Assert.AreEqual(2, filter.Next(types, 0), "146 and 151 are filtered");
            Assert.AreEqual(4, filter.Next(types, 2));
            Assert.AreEqual(2, filter.Previous(types, 4));

            filter.ShowFiltered = true;
            Assert.AreEqual(1, filter.Next(types, 0), "show mode steps through every message");

            // Toggling the group again restores it.
            filter.Toggle(160);
            Assert.IsFalse(filter.IsFiltered(145));
            Assert.IsEmpty(filter.FilteredTypeGroups);
        }

        // ---------------- message view: the four category-selection controls ----------------

        [Test]
        public void MessageCategories_ThereAreExactlyFour_NamedAllPlanetsFleetsOther()
        {
            Assert.AreEqual(4, MessageCategories.All.Count);
            Assert.AreEqual("All", MessageCategories.Name(MessageCategories.All[0]));
            Assert.AreEqual("Planets", MessageCategories.Name(MessageCategories.All[1]));
            Assert.AreEqual("Fleets", MessageCategories.Name(MessageCategories.All[2]));
            Assert.AreEqual("Other", MessageCategories.Name(MessageCategories.All[3]));
        }

        [Test]
        public void MessageCategories_ScopeIsTheStandInDestinationRule()
        {
            // All includes every destination kind.
            foreach (MessageDestinationKind kind in System.Enum.GetValues(typeof(MessageDestinationKind)))
            {
                Assert.IsTrue(MessageCategories.Includes(MessageCategory.All, kind));
            }

            // Planets: planet and its production queue.
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Planets, MessageDestinationKind.Planet));
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Planets, MessageDestinationKind.ProductionQueue));
            Assert.IsFalse(MessageCategories.Includes(MessageCategory.Planets, MessageDestinationKind.Fleet));

            // Fleets: fleet and battle replay.
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Fleets, MessageDestinationKind.Fleet));
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Fleets, MessageDestinationKind.BattleReplay));
            Assert.IsFalse(MessageCategories.Includes(MessageCategory.Fleets, MessageDestinationKind.Planet));

            // Other is everything that is neither.
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Other, MessageDestinationKind.Research));
            Assert.IsTrue(MessageCategories.Includes(MessageCategory.Other, MessageDestinationKind.None));
            Assert.IsFalse(MessageCategories.Includes(MessageCategory.Other, MessageDestinationKind.Planet));
            Assert.IsFalse(MessageCategories.Includes(MessageCategory.Other, MessageDestinationKind.BattleReplay));
        }

        [Test]
        public void MessageFilter_CategoryScope_IsIndependentOfThePerTypeFilter()
        {
            // A category (view scope) only limits which indices Next/Previous can reach; the
            // per-type filter still decides visibility within it (client-ui-dialog-catalog.md:
            // "The four category controls ... select the message list view, not the filter").
            var types = new List<string> { "A", "B", "C", "D" };
            MessageFilter filter = new MessageFilter();
            filter.Toggle("B");

            // Scope: only indices 0, 2 and 3 are in view.
            bool InScope(int i) => i != 1;

            Assert.AreEqual(0, filter.First(types, InScope));
            Assert.AreEqual(2, filter.Next(types, 0, InScope));

            // Scope everything but filter B: B is skipped inside the scope too.
            Assert.IsTrue(filter.IsVisible("A"));
            Assert.IsFalse(filter.IsVisible("B"));
        }

        // ---------------- detail-card status line ----------------

        private static Race CostRace()
        {
            return new Race { ResearchCosts = new TechLevel(100) };
        }

        [Test]
        public void StatusLine_CostOfEveryMissingLevel_LevelByLevel()
        {
            // Energy 0 -> 2 with 0 total levels: level 1 = 50, then level 2 = 80 + 10 x (1 level
            // now held) = 90.
            int needed = TechStatusLine.ResourcesStillNeeded(CostRace(), new TechLevel(0), new TechLevel(0), new TechLevel(0, 0, 2, 0, 0, 0));

            Assert.AreEqual(140, needed);
            Assert.AreEqual("140 resources needed", TechStatusLine.Format(needed));
        }

        [Test]
        public void StatusLine_BankedResourcesCountPerField_NeverBelowZero()
        {
            Race race = CostRace();
            TechLevel required = new TechLevel(0, 0, 1, 0, 1, 0);

            // Energy level 1 = 50; then Weapons level 1 = 50 + 10 = 60 (Energy already raised).
            Assert.AreEqual(110, TechStatusLine.ResourcesStillNeeded(race, new TechLevel(0), new TechLevel(0), required));

            // 100 banked in Weapons covers its 60 but the surplus does not spill into Energy.
            Assert.AreEqual(50, TechStatusLine.ResourcesStillNeeded(race, new TechLevel(0), new TechLevel(0, 0, 0, 0, 100, 0), required));
        }

        [Test]
        public void StatusLine_NothingNeeded_IsAvailable()
        {
            Race race = CostRace();
            int met = TechStatusLine.ResourcesStillNeeded(race, new TechLevel(3), new TechLevel(0), new TechLevel(0, 0, 3, 0, 0, 0));
            int banked = TechStatusLine.ResourcesStillNeeded(race, new TechLevel(0), new TechLevel(0, 0, 50, 0, 0, 0), new TechLevel(0, 0, 1, 0, 0, 0));

            Assert.AreEqual(0, met);
            Assert.AreEqual(0, banked);
            Assert.AreEqual("Available", TechStatusLine.Format(0));
            Assert.AreEqual(TechStatusLine.Kind.Available, TechStatusLine.KindOf(0));
        }

        [Test]
        public void StatusLine_RequirementAbove26_IsUnavailable()
        {
            int needed = TechStatusLine.ResourcesStillNeeded(CostRace(), new TechLevel(0), new TechLevel(0), new TechLevel(0, 0, 27, 0, 0, 0));

            Assert.AreEqual(TechStatusLine.Unavailable, needed);
            Assert.AreEqual("Unavailable", TechStatusLine.Format(needed));
        }

        [Test]
        public void StatusLine_ForbiddenComponent_IsUnavailable()
        {
            Race race = CostRace();
            race.Traits.SetPrimary("IS");
            Component component = new Component { Name = "Forbidden Thing" };
            component.Restrictions.SetRestriction("IS", RaceAvailability.not_available);

            Assert.AreEqual(TechStatusLine.Unavailable, TechStatusLine.ForComponent(component, race, new TechLevel(0), new TechLevel(0)));

            Component allowed = new Component { Name = "Allowed Thing" };
            Assert.AreEqual(0, TechStatusLine.ForComponent(allowed, race, new TechLevel(0), new TechLevel(0)));
        }

        [Test]
        public void StatusLine_Thresholds_PlainNumberBelow100000_ThousandsWithKFrom100000()
        {
            Assert.AreEqual("1 resources needed", TechStatusLine.Format(1));
            Assert.AreEqual("99999 resources needed", TechStatusLine.Format(99999));
            Assert.AreEqual(TechStatusLine.Kind.Cost, TechStatusLine.KindOf(99999));
            Assert.AreEqual("100k resources needed", TechStatusLine.Format(100000));
            Assert.AreEqual(TechStatusLine.Kind.CostInThousands, TechStatusLine.KindOf(100000));
            Assert.AreEqual("149k resources needed", TechStatusLine.Format(149499), "rounded to the nearest thousand");
            Assert.AreEqual("150k resources needed", TechStatusLine.Format(149500));
        }

        // ---------------- waypoint selection after delete ----------------

        [Test]
        public void WaypointDelete_SelectedWaypoint_SelectionStaysOnThePreviousOne()
        {
            // Route 0 (position), 1, 2, 3; delete 2 -> 0, 1, 2 (old 3).
            Assert.AreEqual(1, WaypointSelection.AfterDelete(2, 2, 3, keepPrevious: true));
        }

        [Test]
        public void WaypointDelete_FollowingVariant_SelectsTheNextOne()
        {
            Assert.AreEqual(2, WaypointSelection.AfterDelete(2, 2, 3, keepPrevious: false), "old waypoint 3 is now index 2");
        }

        [Test]
        public void WaypointDelete_FirstEditableWaypoint_SelectsTheFleetItself()
        {
            // behavior-specs-11: deleting the first leg (waypoint 1) leaves waypoint 0 - the
            // fleet itself - current; it does NOT fall through to the following waypoint.
            Assert.AreEqual(0, WaypointSelection.AfterDelete(1, 1, 3, keepPrevious: true));
        }

        [Test]
        public void WaypointDelete_OnlyWaypoint_LeavesTheFleetItselfCurrent()
        {
            Assert.AreEqual(0, WaypointSelection.AfterDelete(1, 1, 1, keepPrevious: true), "waypoint 0 (the fleet) is current");
        }

        [Test]
        public void WaypointDelete_LastWaypoint_FollowingVariant_FallsBackToPrevious()
        {
            Assert.AreEqual(2, WaypointSelection.AfterDelete(3, 3, 3, keepPrevious: false));
        }

        [Test]
        public void WaypointDelete_AnotherWaypoint_KeepsTheSelectedOne_AtItsShiftedIndex()
        {
            Assert.AreEqual(2, WaypointSelection.AfterDelete(3, 1, 4, keepPrevious: true), "selected moved down one");
            Assert.AreEqual(1, WaypointSelection.AfterDelete(1, 3, 4, keepPrevious: true), "selected before the deleted one: unchanged");
            Assert.AreEqual(-1, WaypointSelection.AfterDelete(-1, 2, 3, keepPrevious: true), "nothing selected stays so");
        }
    }
}
