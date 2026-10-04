namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using NUnit.Framework;

    using Nova.Client.Map;
    using Nova.Client.Shell;

    /// <summary>
    /// The application-shell rules behind the Avalonia main window (Nova.Client.Shell):
    /// behavior-specs-10/client-interface.md (9-slot Recent Files, toolbar, Window Layout presets,
    /// autosave interval, sound toggles, progress dialog, hotkey relay, Print Map) and
    /// client-ui-dialog-catalog.md (shutdown confirmation, race-name builder, scanner-display
    /// control, component-category browser, progress lifecycle).
    /// Only the spec's own numbers and rules are pinned; the stand-ins named SPEC GAP in the
    /// classes are not.
    /// </summary>
    [TestFixture]
    public class ClientShellTest
    {
        // ---------------- recent files ----------------

        [Test]
        public void RecentFiles_KeepsNineSlots()
        {
            RecentFiles list = new RecentFiles();
            for (int i = 0; i < 12; i++)
            {
                list.Add($"C:\\games\\g{i}\\race.intel");
            }

            Assert.AreEqual(9, RecentFiles.Capacity);
            Assert.AreEqual(9, list.Entries.Count);
        }

        [Test]
        public void RecentFiles_RoundTripsThroughThePreferenceString()
        {
            RecentFiles list = new RecentFiles();
            list.Add("C:\\a\\one.intel");
            list.Add("C:\\b\\two.intel");

            RecentFiles back = RecentFiles.Parse(list.Format());

            CollectionAssert.AreEqual(list.Entries, back.Entries);
            Assert.IsEmpty(RecentFiles.Parse(null).Entries);
            Assert.IsEmpty(RecentFiles.Parse("").Entries);
        }

        [Test]
        public void RecentFiles_ParseNeverExceedsNineOrRepeats()
        {
            string stored = string.Join("|", Enumerable.Range(0, 15).Select(i => $"p{i}.intel")) + "|p0.intel";
            RecentFiles list = RecentFiles.Parse(stored);

            Assert.AreEqual(9, list.Entries.Count);
            Assert.AreEqual(list.Entries.Count, list.Entries.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Test]
        public void RecentFiles_MissingFileOrNoExtensionIsAnError()
        {
            Assert.AreEqual(RecentFileProblem.NoExtension, RecentFiles.Check("C:\\games\\race", _ => true));
            Assert.AreEqual(RecentFileProblem.Missing, RecentFiles.Check("C:\\games\\race.intel", _ => false));
            Assert.AreEqual(RecentFileProblem.None, RecentFiles.Check("C:\\games\\race.intel", _ => true));
        }

        [Test]
        public void RecentFiles_CaptionCarriesTheSlotDigitAsAccessKey()
        {
            Assert.AreEqual("_1 C:\\x.intel", RecentFiles.MenuCaption(0, "C:\\x.intel"));
            Assert.AreEqual("_9 C:\\my__game\\x.intel", RecentFiles.MenuCaption(8, "C:\\my_game\\x.intel"), "a path underscore is shown literally");
        }

        // ---------------- toolbar ----------------

        [Test]
        public void Toolbar_PreferenceStringRoundTripsOrder()
        {
            ToolbarLayout layout = ToolbarLayout.Parse("Research,Save,Find");
            CollectionAssert.AreEqual(new[] { "Research", "Save", "Find" }, layout.Ids);

            Assert.IsTrue(layout.MoveUp("Save"));
            Assert.IsTrue(layout.MoveDown("Research"));
            Assert.AreEqual("Save,Find,Research", layout.Format());
            Assert.IsFalse(layout.MoveUp("Save"), "already at the top");
            Assert.IsFalse(layout.MoveDown("Research"), "already at the bottom");
        }

        [Test]
        public void Toolbar_UnknownAndRepeatedIdsAreDropped()
        {
            ToolbarLayout layout = ToolbarLayout.Parse("Save,Bogus,Save,Find");
            CollectionAssert.AreEqual(new[] { "Save", "Find" }, layout.Ids);
        }

        [Test]
        public void Toolbar_AbsentPreferenceGivesTheFullSet_EmptyMeansNone()
        {
            Assert.AreEqual(ToolbarLayout.Catalog.Count, ToolbarLayout.Parse(null).Ids.Count);
            Assert.AreEqual(0, ToolbarLayout.Parse("").Ids.Count);
        }

        [Test]
        public void Toolbar_RemoveAddAndReset()
        {
            ToolbarLayout layout = ToolbarLayout.Default();
            Assert.IsTrue(layout.Remove("Find"));
            Assert.IsFalse(layout.Contains("Find"));
            Assert.IsTrue(layout.Hidden.Any(button => button.Id == "Find"));

            Assert.IsTrue(layout.Add("Find"));
            Assert.AreEqual("Find", layout.Ids.Last(), "a re-added button goes to the bottom");
            Assert.IsFalse(layout.Add("Find"), "no duplicates");
            Assert.IsFalse(layout.Add("Bogus"));

            layout.Reset();
            CollectionAssert.AreEqual(ToolbarLayout.Default().Ids, layout.Ids);
        }

        [Test]
        public void Toolbar_OffersTheWindowLayoutPresetAction()
        {
            Assert.IsNotNull(ToolbarLayout.Find(ToolbarLayout.LayoutPresetId), "spec: at least one action with a preset drop-down");
        }

        [Test]
        public void Toolbar_VisibilityFlagRoundTrips()
        {
            Assert.IsTrue(ToolbarLayout.ParseVisible(ToolbarLayout.FormatVisible(true)));
            Assert.IsFalse(ToolbarLayout.ParseVisible(ToolbarLayout.FormatVisible(false)));
        }

        // ---------------- window layout ----------------

        [Test]
        public void WindowLayout_ThreePresetsWithTheSpecCaptions()
        {
            CollectionAssert.AreEqual(new[] { "Large Screen", "Medium Screen", "Small Screen" }, WindowLayout.Labels);
            Assert.AreEqual(WindowLayoutPreset.SmallScreen, WindowLayout.Parse(WindowLayout.Format(WindowLayoutPreset.SmallScreen)));
            Assert.AreEqual(WindowLayoutPreset.MediumScreen, WindowLayout.Parse("1"));
            Assert.AreEqual(WindowLayout.Default, WindowLayout.Parse("3"), "only modes 0-2 exist");
            Assert.AreEqual(WindowLayout.Default, WindowLayout.Parse(null));
        }

        // ---------------- autosave interval ----------------

        [Test]
        public void Autosave_RangeAndDefault()
        {
            Assert.AreEqual(100, AutosaveInterval.Minimum);
            Assert.AreEqual(30000, AutosaveInterval.Maximum);
            Assert.AreEqual(5000, AutosaveInterval.Default);
        }

        [Test]
        public void Autosave_InRangeValueIsUsed()
        {
            Assert.AreEqual(100, AutosaveInterval.Resolve("100", 5000));
            Assert.AreEqual(30000, AutosaveInterval.Resolve("30000", 5000));
            Assert.AreEqual(12345, AutosaveInterval.Resolve("12345", 5000));
        }

        [Test]
        public void Autosave_OutOfRangeFallsBackToThePreviousValueNotTheDefault()
        {
            Assert.AreEqual(7000, AutosaveInterval.Resolve("99", 7000));
            Assert.AreEqual(7000, AutosaveInterval.Resolve("30001", 7000));
            Assert.AreEqual(7000, AutosaveInterval.Resolve("junk", 7000));
            Assert.AreEqual(7000, AutosaveInterval.Resolve(null, 7000));
        }

        // ---------------- sound toggles ----------------

        [Test]
        public void Sound_TogglesAreRefusedWithoutSoundSupport()
        {
            SoundPreferences none = new SoundPreferences(false, "1", "0");
            Assert.IsFalse(none.ToggleSoundEffects());
            Assert.IsFalse(none.ToggleMusic());
            Assert.IsTrue(none.SoundEffects, "a refused toggle leaves the stored bit alone");
            Assert.IsFalse(none.Music);

            SoundPreferences with = new SoundPreferences(true, "1", "1");
            Assert.IsTrue(with.ToggleMusic());
            Assert.IsFalse(with.Music);
            Assert.IsTrue(with.SoundEffects, "the two bits are independent");
        }

        // ---------------- unsaved changes / shutdown ----------------

        [Test]
        public void UnsavedChanges_TracksTheOrderList()
        {
            object first = new object();
            object second = new object();
            List<object> orders = new List<object> { first };
            UnsavedChangesTracker tracker = new UnsavedChangesTracker();
            tracker.MarkSaved(orders);
            Assert.IsFalse(tracker.IsDirty(orders));

            orders.Add(second);
            Assert.IsTrue(tracker.IsDirty(orders), "an added order");
            tracker.MarkSaved(orders);
            Assert.IsFalse(tracker.IsDirty(orders));

            orders[1] = new object();
            Assert.IsTrue(tracker.IsDirty(orders), "a replaced order");
        }

        [Test]
        public void Shutdown_CancelNeverCloses()
        {
            Assert.AreEqual((false, false), ShutdownConfirmation.Resolve(ShutdownChoice.Cancel));
            Assert.AreEqual((true, true), ShutdownConfirmation.Resolve(ShutdownChoice.Save));
            Assert.AreEqual((false, true), ShutdownConfirmation.Resolve(ShutdownChoice.Discard));
            Assert.IsFalse(ShutdownConfirmation.IsNeeded(false, true), "no game, nothing to decide");
            Assert.IsTrue(ShutdownConfirmation.IsNeeded(true, true));
        }

        // ---------------- progress surface ----------------

        [Test]
        public void Progress_CreatedOnceThenReusedUntilTeardown()
        {
            ProgressSurface surface = new ProgressSurface();
            int created = 0;
            int destroyed = 0;
            surface.Created += () => created++;
            surface.Destroyed += () => destroyed++;

            surface.Report("one", 1, 4);
            surface.Report("two", 2, 4);
            surface.Report("other operation", 0, 10);
            Assert.AreEqual(1, created, "a second operation before teardown reuses the window");
            Assert.IsTrue(surface.Exists);
            Assert.AreEqual("other operation", surface.Caption);

            surface.End();
            Assert.IsFalse(surface.Exists, "teardown clears the guard flag");
            Assert.AreEqual(1, destroyed);

            surface.Report("again", 1, 2);
            Assert.AreEqual(2, created, "after teardown a new window is created");
        }

        [Test]
        public void Progress_FailureIsNotReportedAsSuccess()
        {
            ProgressSurface surface = new ProgressSurface();
            surface.Report("working", 1, 4);
            surface.Fail("disk full");

            Assert.IsTrue(surface.HasFailed);
            Assert.AreEqual("disk full", surface.FailureMessage);
            Assert.AreNotEqual(100, surface.Percent);
            Assert.IsTrue(surface.Exists, "the failure surface stays until closed");
        }

        [Test]
        public void Progress_ValuesStayInRange()
        {
            ProgressSurface surface = new ProgressSurface();
            surface.Report("x", 9, 4);
            Assert.AreEqual(100, surface.Percent);
            surface.Report("x", -3, 4);
            Assert.AreEqual(0, surface.Percent);
        }

        // ---------------- hotkey relay ----------------

        [Test]
        public void Relay_BracketsStepMessages()
        {
            Assert.AreEqual(RelayAction.PreviousMessage, HotkeyRelay.Classify(RelayKey.LeftBracket, false, false, false));
            Assert.AreEqual(RelayAction.NextMessage, HotkeyRelay.Classify(RelayKey.RightBracket, false, false, false));
        }

        [Test]
        public void Relay_DeleteAndBackspaceOnlyWhileEditingARoute()
        {
            Assert.AreEqual(RelayAction.DeleteWaypoint, HotkeyRelay.Classify(RelayKey.Delete, false, false, true));
            Assert.AreEqual(RelayAction.DeleteWaypoint, HotkeyRelay.Classify(RelayKey.Backspace, false, false, true));
            Assert.AreEqual(RelayAction.None, HotkeyRelay.Classify(RelayKey.Delete, false, false, false));
        }

        [Test]
        public void Relay_EscapeClosesThePopup_DigitsGoToTheViewOptions()
        {
            Assert.AreEqual(RelayAction.ClosePopup, HotkeyRelay.Classify(RelayKey.Escape, false, false, false));
            Assert.AreEqual(RelayAction.ViewOptionDigit, HotkeyRelay.Classify(RelayKey.Digit, false, false, false));
        }

        [Test]
        public void Relay_KeysOutsideTheSetAreNotIntercepted()
        {
            Assert.AreEqual(RelayAction.None, HotkeyRelay.Classify(RelayKey.Other, false, false, true));
        }

        [Test]
        public void Relay_TheBoundedFieldStaysWithin0To11()
        {
            Assert.AreEqual(11, HotkeyRelay.StepField(11, true));
            Assert.AreEqual(0, HotkeyRelay.StepField(0, false));
            Assert.AreEqual(5, HotkeyRelay.StepField(4, true));
            Assert.AreEqual(3, HotkeyRelay.StepField(4, false));
        }

        // ---------------- race-name builder ----------------

        [Test]
        public void RaceName_ExplicitPluralIsUsed_ElseAnSIsAppended()
        {
            Assert.AreEqual("Humanoids", RaceNameText.Plural("Humanoid", "Humanoids"));
            Assert.AreEqual("Rabbitoids", RaceNameText.Plural("Rabbitoid", null));
            Assert.AreEqual("Rabbitoids", RaceNameText.Plural("Rabbitoid", "  "));
        }

        [Test]
        public void RaceName_SingularFormIsTheStoredName()
        {
            Assert.AreEqual("Humanoid", RaceNameText.Build("Humanoid", "Humanoids", false, false, false));
            Assert.AreEqual("Humanoids", RaceNameText.Build("Humanoid", "Humanoids", true, false, false));
        }

        // ---------------- scanner display ----------------

        [Test]
        public void Scanner_TypedValueIsCommittedWithin2To100()
        {
            Assert.AreEqual(40, ScannerPercentInput.Commit("40", 100));
            Assert.AreEqual(40, ScannerPercentInput.Commit("40%", 100));
            Assert.AreEqual(2, ScannerPercentInput.Commit("2", 100));
            Assert.AreEqual(100, ScannerPercentInput.Commit("100", 50));
            Assert.AreEqual("75%", ScannerPercentInput.Format(75));
        }

        [Test]
        public void Scanner_TextThatIsNotANumberReverts()
        {
            Assert.AreEqual(60, ScannerPercentInput.Commit("abc", 60));
        }

        [Test]
        public void Scanner_LiveTooltipShowsFor400Milliseconds()
        {
            DateTime changed = new DateTime(2026, 1, 1, 12, 0, 0);
            Assert.IsTrue(ScannerPercentInput.IsTooltipVisible(changed, changed.AddMilliseconds(100)));
            Assert.IsTrue(ScannerPercentInput.IsTooltipVisible(changed, changed.AddMilliseconds(399)));
            Assert.IsFalse(ScannerPercentInput.IsTooltipVisible(changed, changed.AddMilliseconds(400)));
        }

        [Test]
        public void Scanner_CommittingForceEnablesScanCircles()
        {
            MapViewOptions options = new MapViewOptions();
            options.ShowScanCircles = false;
            options.ScannerPercentage = ScannerPercentInput.Commit("50", options.ScannerPercentage);

            Assert.AreEqual(50, options.ScannerPercentage);
            Assert.IsTrue(options.ShowScanCircles);
        }

        // ---------------- component-category browser ----------------

        [Test]
        public void Stepper_WalksEntriesWithinACategory()
        {
            CategoryStepper<string> stepper = new CategoryStepper<string>(new[]
            {
                new[] { "a1", "a2", "a3" },
                new[] { "b1" },
            });

            Assert.AreEqual("a1", stepper.Current);
            stepper.Next();
            Assert.AreEqual("a2", stepper.Current);
            stepper.Previous();
            Assert.AreEqual("a1", stepper.Current);
        }

        [Test]
        public void Stepper_SkipsEmptyCategories()
        {
            CategoryStepper<string> stepper = new CategoryStepper<string>(new[]
            {
                new string[0],
                new[] { "b1" },
            });

            Assert.AreEqual(1, stepper.CategoryIndex, "the dead/empty slot is never current");
            stepper.SelectCategory(0);
            Assert.AreEqual("b1", stepper.Current);

            CategoryStepper<string> none = new CategoryStepper<string>(new[] { new string[0] });
            Assert.IsFalse(none.HasCurrent);
        }

        // ---------------- map printing ----------------

        [Test]
        public void Print_PagesCoverTheWholeMapOnce()
        {
            IReadOnlyList<MapPrintPage> pages = MapPrintLayout.Pages(1001, 799, 3, 2);

            Assert.AreEqual(6, pages.Count);
            Assert.AreEqual(1001L * 799L, pages.Sum(page => (long)page.Width * page.Height));
            Assert.AreEqual(1001, pages.Where(page => page.Y == 0).Sum(page => page.Width));
            CollectionAssert.AreEqual(Enumerable.Range(1, 6).ToList(), pages.Select(page => page.Number).ToList());
        }
    }
}
