namespace Nova.Tests.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml.Serialization;

    using NUnit.Framework;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Server;

    /// <summary>
    /// Spec-driven coverage of behavior-specs-10/victory-conditions.md (coverage rows 1, 6, 7, 8,
    /// 9, 11, 14, 15, 17 and the broader conformance pass): the ten configurable items and their
    /// defaults (section 1), condition 3 riding on condition 2, the derived "must meet N of the
    /// above" = min(raw, enabled count), the year gate, the seven per-condition checks met and
    /// just not met, ties, and elimination (section 2).
    /// </summary>
    /// <remarks>
    /// Not pinned here, because the spec leaves them open (reported as questions): whether
    /// conditions 4 and 5 are met at exact equality ("exceeds"), whether condition 8 is met in
    /// exactly year N ("after N years"), whether condition 8 counts towards the derived
    /// "N of the above" ("among 1-7 excluding condition 3" vs the stated range 1-7), what a tie
    /// announcement contains, and the stand-in year-gate seed of question 3.2.
    /// </remarks>
    [TestFixture]
    public class VictoryCoverageTest
    {
        private GameSettings savedSettings;
        private ServerData serverData;
        private uint nextFleetId;

        /// <summary>A private, all-disabled settings instance swapped in as the singleton
        /// (GameSettings.Data is process-wide), restored afterwards.</summary>
        [SetUp]
        public void SwapInFreshSettings()
        {
            savedSettings = GameSettings.Data;
            GameSettings fresh = NewSettings();
            fresh.PlanetsOwned.IsChecked = false;
            fresh.TechLevels.IsChecked = false;
            fresh.NumberOfFields.IsChecked = false;
            fresh.TotalScore.IsChecked = false;
            fresh.SecondPlaceScore.IsChecked = false;
            fresh.ProductionCapacity.IsChecked = false;
            fresh.CapitalShips.IsChecked = false;
            fresh.HighestScore.IsChecked = false;
            fresh.TargetsToMeet = 1;
            fresh.MinimumGameTime = 0;
            GameSettings.Data = fresh;

            serverData = new ServerData();
            serverData.TurnYear = Global.StartingYear + 100;
            nextFleetId = 1;
        }

        [TearDown]
        public void RestoreSettings()
        {
            GameSettings.Data = savedSettings;
        }

        private static GameSettings NewSettings()
        {
            return (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
        }

        private static GameSettings S
        {
            get { return GameSettings.Data; }
        }

        // ------------------------------------------------------------------------ fixtures

        private EmpireData AddEmpire(ushort id)
        {
            EmpireData empire = new EmpireData();
            empire.Id = id;
            empire.Race = new Race();
            empire.Race.ColonistsPerResource = int.MaxValue; // Resources term 0 unless a test sets it
            empire.Race.PluralName = "Race" + id;
            serverData.AllEmpires.Add(id, empire);
            return empire;
        }

        private Star AddStar(EmpireData owner, string name, int colonists)
        {
            Star star = new Star();
            star.Name = name;
            star.Owner = owner == null ? (ushort)Global.Nobody : owner.Id;
            star.Colonists = colonists;
            star.Factories = 0;
            star.ThisRace = owner?.Race;
            serverData.AllStars.Add(star.Key, star);
            return star;
        }

        private void AddStars(EmpireData owner, int count, int colonists)
        {
            for (int i = 0; i < count; i++)
            {
                AddStar(owner, (owner == null ? "Free" : owner.Race.PluralName) + "-" + i + "-" + serverData.AllStars.Count, colonists);
            }
        }

        /// <summary>A design whose weapon rating is (range + 3) x power x count / 4 (one beam slot).</summary>
        private static ShipDesign BeamDesign(long key, int power, int range, int count)
        {
            Component beam = new Component();
            beam.Properties.Add("Weapon", new Weapon { Power = power, Range = range, Accuracy = 75, Group = WeaponType.standardBeam });
            Hull hull = new Hull { FuelCapacity = 100, ArmorStrength = 50, Modules = new List<HullModule>() };
            hull.Modules.Add(new HullModule { AllocatedComponent = beam, ComponentCount = count, ComponentMaximum = count });
            Component blueprint = new Component { Cost = new Resources(1, 1, 1, 1), Mass = 10 };
            blueprint.Properties.Add("Hull", hull);
            ShipDesign design = new ShipDesign(key) { Name = "Design" + key, Blueprint = blueprint };
            design.Update();
            return design;
        }

        private void AddShips(EmpireData owner, ShipDesign design, int quantity)
        {
            Fleet fleet = new Fleet("fleet" + nextFleetId, owner.Id, nextFleetId++, new NovaPoint(0, 0));
            ShipToken token = new ShipToken(design, quantity);
            fleet.Composition.Add(token.Key, token);
            owner.OwnedFleets.Add(fleet);
        }

        private static void SetAllTech(EmpireData empire, int level)
        {
            empire.ResearchLevels = new TechLevel(level);
        }

        private void RunVictoryCheck()
        {
            new VictoryCheck(serverData, new Scores(serverData)).Victor();
        }

        private List<Message> WinMessages()
        {
            return serverData.AllMessages.FindAll(m => m.Text != null && m.Text.Contains("have won the game"));
        }

        private bool Won(EmpireData empire)
        {
            return WinMessages().Exists(m => m.Text.Contains(empire.Race.PluralName + " have won the game"));
        }

        private int ScoreOf(EmpireData empire)
        {
            return new Scores(serverData).GetScores().Single(r => r.EmpireId == empire.Id).Score;
        }

        // ================================================================ row 1, row 7: the items

        [Test]
        public void Row1_TenConfigurableItems_SevenToggles_ThePairedFieldCount_AndTwoMetaSettings()
        {
            GameSettings settings = NewSettings();

            EnabledValue[] toggleable =
            {
                settings.PlanetsOwned, settings.TechLevels, settings.TotalScore, settings.SecondPlaceScore,
                settings.ProductionCapacity, settings.CapitalShips, settings.HighestScore,
            };
            Assert.AreEqual(7, toggleable.Distinct().Count(), "seven independently toggleable conditions, each a flag plus a threshold");
            Assert.IsNotNull(settings.NumberOfFields, "condition 3: the numeric value riding on condition 2");
            Assert.AreEqual(1, settings.TargetsToMeet, "the 'must meet N of the above' meta-setting");
            Assert.AreEqual(50, settings.MinimumGameTime, "the year-gate meta-setting");

            List<VictorySummaryRow> rows = VictorySummary.Rows(settings);
            Assert.AreEqual(7, rows.Count, "the setup and in-session summary list exactly the seven toggleable conditions");
        }

        [Test]
        public void Row7_DefaultValues_AreTheOnesReadOffTheRealDialog()
        {
            // victory-conditions.md section 1, "Confirmed by direct observation of the running client".
            GameSettings settings = NewSettings();

            Assert.IsTrue(settings.PlanetsOwned.IsChecked, "condition 1 enabled");
            Assert.AreEqual(60, settings.PlanetsOwned.NumericValue, "at 60%");
            Assert.IsTrue(settings.TechLevels.IsChecked, "conditions 2/3 enabled");
            Assert.AreEqual(22, settings.TechLevels.NumericValue, "tech level 22");
            Assert.AreEqual(4, settings.NumberOfFields.NumericValue, "in 4 fields");
            Assert.IsFalse(settings.TotalScore.IsChecked, "condition 4 disabled");
            Assert.AreEqual(11000, settings.TotalScore.NumericValue, "default threshold 11000");
            Assert.IsTrue(settings.SecondPlaceScore.IsChecked, "condition 5 enabled");
            Assert.AreEqual(100, settings.SecondPlaceScore.NumericValue, "at 100%");
            Assert.IsFalse(settings.ProductionCapacity.IsChecked, "condition 6 disabled");
            Assert.AreEqual(100, settings.ProductionCapacity.NumericValue, "100 thousand");
            Assert.IsFalse(settings.CapitalShips.IsChecked, "condition 7 disabled");
            Assert.AreEqual(100, settings.CapitalShips.NumericValue, "100 capital ships");
            Assert.IsFalse(settings.HighestScore.IsChecked, "condition 8 disabled");
            Assert.AreEqual(100, settings.HighestScore.NumericValue, "100 years");
            Assert.AreEqual(1, settings.TargetsToMeet, "must meet 1 of the above");
            Assert.AreEqual(50, settings.MinimumGameTime, "year gate 50");
        }

        // ================================================================ row 6: condition 3

        [Test]
        public void Row6_Condition3_OnItsOwn_NeverCountsWhenCondition2IsOff()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(b, "B1", 100000);
            SetAllTech(a, 26);

            S.TechLevels = new EnabledValue(false, 22);
            S.NumberOfFields = new EnabledValue(true, 2);

            RunVictoryCheck();

            Assert.IsEmpty(WinMessages(), "condition 3 has no meaning without condition 2");
            Assert.AreEqual(0, VictorySummary.EnabledCount(S), "condition 3 is not one of the enabled conditions");
        }

        [Test]
        public void Row6_Condition3_HasNoToggleOfItsOwn_ItsFieldCountAppliesWheneverCondition2IsOn()
        {
            // Section 1: "Condition 3 deliberately has no checkbox of its own"; Nova's settings
            // still carry a flag on NumberOfFields, which must not change the rule.
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(b, "B1", 100000);
            SetAllTech(a, 10);
            a.ResearchLevels[TechLevel.ResearchField.Energy] = 22;
            a.ResearchLevels[TechLevel.ResearchField.Weapons] = 22;
            a.ResearchLevels[TechLevel.ResearchField.Propulsion] = 22;

            S.TechLevels = new EnabledValue(true, 22);
            S.NumberOfFields = new EnabledValue(false, 4);

            RunVictoryCheck();

            Assert.IsEmpty(WinMessages(), "level 22 in three fields does not meet 'level 22 in 4 fields'");
        }

        // ================================================================ row 14: per-condition checks

        [Test]
        public void Condition1_PlanetsOwned_MetAtThePercentage_NotOnePlanetShort()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStars(a, 60, 1000);
            AddStars(b, 1, 1000);
            AddStars(null, 39, 0); // 100 planets in the galaxy
            S.PlanetsOwned = new EnabledValue(true, 60);

            RunVictoryCheck();
            Assert.IsTrue(Won(a), "60 of 100 planets = 60%");

            serverData.AllMessages.Clear();
            Star given = serverData.AllStars.Values.First(s => s.Owner == a.Id);
            given.Owner = Global.Nobody;
            given.Colonists = 0;

            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "59% is short of 60%");
        }

        [Test]
        public void Condition2_TechLevel_MetWithTheLevelInTheRequiredNumberOfFields_NotOneFieldShort()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(b, "B1", 100000);
            SetAllTech(a, 21);
            a.ResearchLevels[TechLevel.ResearchField.Energy] = 22;
            a.ResearchLevels[TechLevel.ResearchField.Weapons] = 25;
            a.ResearchLevels[TechLevel.ResearchField.Propulsion] = 22;

            S.TechLevels = new EnabledValue(true, 22);
            S.NumberOfFields = new EnabledValue(true, 4);

            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "three fields at 22 or more, a fourth at 21");

            a.ResearchLevels[TechLevel.ResearchField.Construction] = 22;
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "four fields at 22 or more");
        }

        [Test]
        public void Condition4_Score_MetAboveTheThreshold_NotBelowIt()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 600000);           // 6 points
            Star second = AddStar(a, "A2", 300000); // 3 points: 9 in all
            AddStar(b, "B1", 100000);
            S.TotalScore = new EnabledValue(true, 10);

            Assert.AreEqual(9, ScoreOf(a));
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "9 is below 10");

            second.Colonists = 500000; // 5 points: 11 in all
            Assert.AreEqual(11, ScoreOf(a));
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "11 exceeds 10");
        }

        [Test]
        public void Condition5_SecondPlace_MetAboveTheMargin_NotBelowIt()
        {
            // 50% over a second place of 10 is 15.
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            EmpireData c = AddEmpire(3);
            AddStar(a, "A1", 600000);
            AddStar(a, "A2", 800000);               // 6 + 6 = 12
            Star tuning = AddStar(a, "A3", 200000); // + 2 = 14
            AddStar(b, "B1", 600000);
            AddStar(b, "B2", 400000);               // 10
            AddStar(c, "C1", 300000);               // 3
            S.SecondPlaceScore = new EnabledValue(true, 50);

            Assert.AreEqual(14, ScoreOf(a));
            Assert.AreEqual(10, ScoreOf(b));
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "14 is not 50% above 10");

            tuning.Colonists = 400000; // 6 + 6 + 4 = 16
            Assert.AreEqual(16, ScoreOf(a));
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "16 is more than 50% above 10");
        }

        [Test]
        public void Condition5_IsAPercentageMargin_NotAFlatMultiple()
        {
            // Row 15: the default 100 means "double second place", not "100 x second place".
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 600000);
            AddStar(a, "A2", 600000);
            AddStar(a, "A3", 100000);  // 13
            AddStar(b, "B1", 600000);  // 6: 2 x 6 = 12 < 13 (a flat x100 would need 600)
            S.SecondPlaceScore = new EnabledValue(true, 100);

            RunVictoryCheck();

            Assert.IsTrue(Won(a));
        }

        [Test]
        public void Condition6_ProductionCapacity_IsResourceOutputOver1000_MetAtTheThreshold_NotOneResourceShort()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            a.Race.ColonistsPerResource = 1000;
            Star a1 = AddStar(a, "A1", 999000); // 999 resources a year
            AddStar(b, "B1", 100000);
            S.ProductionCapacity = new EnabledValue(true, 1);

            Assert.AreEqual(999, a1.GetResourceRate(), "fixture");
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "999 resources is 0 thousand");

            a1.Colonists = 1000000; // 1,000 resources a year
            Assert.AreEqual(1000, a1.GetResourceRate(), "fixture");
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "1,000 resources is 1 thousand");
        }

        [Test]
        public void Condition7_CapitalShips_MetAtTheCount_NotOneShipShort_AndEscortsDoNotCount()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(b, "B1", 100000);
            ShipDesign capital = BeamDesign(1, 500, 1, 4);   // (1 + 3) x 500 x 4 / 4 = 2,000
            ShipDesign escort = BeamDesign(2, 1999, 1, 1);   // 4 x 1,999 / 4 = 1,999
            Assert.AreEqual(2000, Scores.DesignWeaponRating(capital));
            Assert.AreEqual(1999, Scores.DesignWeaponRating(escort));
            AddShips(a, capital, 2);
            AddShips(a, escort, 10);
            S.CapitalShips = new EnabledValue(true, 3);

            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "two capital ships (escorts are not capital ships)");

            AddShips(a, capital, 1);
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "three capital ships");
        }

        [Test]
        public void Condition8_HighestScore_NeedsTheSoleLeadAfterTheYears()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 600000);
            AddStar(b, "B1", 100000);
            S.HighestScore = new EnabledValue(true, 30);

            serverData.TurnYear = Global.StartingYear + 29;
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "the sole leader before 30 years have passed");

            serverData.TurnYear = Global.StartingYear + 31;
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "the sole leader after 30 years");
        }

        // ================================================================ row 8: N of the above

        [Test]
        public void Row8_ConditionsToMeet_IsTheStoredFigureCappedAtTheEnabledCount()
        {
            GameSettings settings = NewSettings();
            foreach (EnabledValue value in new[] { settings.PlanetsOwned, settings.TechLevels, settings.NumberOfFields, settings.TotalScore, settings.SecondPlaceScore, settings.ProductionCapacity, settings.CapitalShips, settings.HighestScore })
            {
                value.IsChecked = false;
            }

            settings.PlanetsOwned.IsChecked = true;
            settings.TechLevels.IsChecked = true;
            settings.NumberOfFields.IsChecked = true; // condition 3 is not counted

            settings.TargetsToMeet = 3;
            Assert.AreEqual(2, VictorySummary.EnabledCount(settings));
            Assert.AreEqual(2, VictorySummary.ConditionsToMeet(settings), "min(3, 2 enabled)");

            settings.TargetsToMeet = 1;
            Assert.AreEqual(1, VictorySummary.ConditionsToMeet(settings), "min(1, 2 enabled)");
        }

        [Test]
        public void Row8_TheRuntimeUsesTheDerivedFigure_AStoredFigureAboveTheEnabledCountStillAllowsAWinner()
        {
            // The runtime compares each race's met-condition count with the DERIVED
            // min(raw, enabled count) (section 1 table, section 2), so a raw 3 with only one
            // condition enabled requires that one condition.
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(a, "A2", 100000);
            AddStar(b, "B1", 100000);
            S.PlanetsOwned = new EnabledValue(true, 60); // 2 of 3 = 66%
            S.TargetsToMeet = 3;

            RunVictoryCheck();

            Assert.IsTrue(Won(a));
        }

        [Test]
        public void NumberOfConditionsToMeet_TwoOfThree_OneIsNotEnough_TwoAre()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            a.Race.ColonistsPerResource = 1000;
            AddStar(a, "A1", 1000000); // 1,000 resources, 6 points
            AddStar(a, "A2", 100000);
            AddStar(b, "B1", 100000);

            S.PlanetsOwned = new EnabledValue(true, 60);       // 2 of 3 planets: met
            S.ProductionCapacity = new EnabledValue(true, 5);  // 1 thousand: not met
            S.CapitalShips = new EnabledValue(true, 1);        // none: not met
            S.TargetsToMeet = 2;

            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "one of the three enabled conditions");

            S.ProductionCapacity.NumericValue = 1;
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "two of the three");
        }

        // ================================================================ rows 9 and 11: the gate

        private EmpireData MeetEverything(out EmpireData runnerUp)
        {
            // A race meeting all seven toggleable conditions at once.
            EmpireData a = AddEmpire(1);
            runnerUp = AddEmpire(2);
            a.Race.ColonistsPerResource = 1000;
            AddStars(a, 9, 1000000);     // 90% of the planets, 9,000 resources, 54 points
            AddStar(runnerUp, "B1", 100000);
            SetAllTech(a, 22);
            AddShips(a, BeamDesign(1, 500, 1, 4), 1);

            S.PlanetsOwned = new EnabledValue(true, 60);
            S.TechLevels = new EnabledValue(true, 22);
            S.NumberOfFields = new EnabledValue(true, 4);
            S.TotalScore = new EnabledValue(true, 50);
            S.SecondPlaceScore = new EnabledValue(true, 100);
            S.ProductionCapacity = new EnabledValue(true, 1);
            S.CapitalShips = new EnabledValue(true, 1);
            S.HighestScore = new EnabledValue(true, 10);
            S.TargetsToMeet = 7;
            return a;
        }

        [Test]
        public void Row9_NoVictoryBeforeTheYearGate_EvenWithEveryConditionMet_VictoryInTheGateYear()
        {
            EmpireData a = MeetEverything(out EmpireData unused);
            S.MinimumGameTime = 40;

            serverData.TurnYear = Global.StartingYear + 39;
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "one year short of the gate");

            serverData.TurnYear = Global.StartingYear + 40;
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "the gate year itself");
        }

        [Test]
        public void Row11_AVictoryIsAnnouncedToEveryRace()
        {
            EmpireData a = MeetEverything(out EmpireData unused);

            RunVictoryCheck();

            List<Message> wins = WinMessages();
            Assert.AreEqual(1, wins.Count);
            Assert.AreEqual(Global.Everyone, wins[0].Audience, "all races are notified");
            Assert.IsTrue(Won(a));
        }

        [Test]
        public void Ties_TwoRacesClearingTheBarTheSameTurn_StillNotifyEveryRace()
        {
            // Section 2: "When a sole eligible leader emerges (or a tie is detected), all races
            // are notified." The content of a tie notice is not specified, so only the
            // notification itself is asserted.
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(b, "B1", 100000);
            S.PlanetsOwned = new EnabledValue(true, 50);

            RunVictoryCheck();

            List<Message> wins = WinMessages();
            Assert.IsNotEmpty(wins, "both races own 50%: the bar is cleared");
            Assert.IsTrue(wins.TrueForAll(m => m.Audience == Global.Everyone));
        }

        // ================================================================ row 17: one store

        [Test]
        public void Row17_TheRuntimeCheckReadsTheSameSettingsStoreTheSetupWrites()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            AddStar(a, "A1", 100000);
            AddStar(a, "A2", 100000);
            AddStar(b, "B1", 100000);

            // The setup path writes GameSettings.Data (NewGameSetup.ResetToDefaults here: 60%
            // planets, second place by 100%, 1 of the above, year gate 50).
            NewGameSetup.ResetToDefaults(GameSettings.Data);
            serverData.TurnYear = Global.StartingYear + 49;
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "the reset's year gate of 50 is in force");

            serverData.TurnYear = Global.StartingYear + 50;
            RunVictoryCheck();
            Assert.IsTrue(Won(a), "2 of 3 planets meets the reset's 60% condition");

            serverData.AllMessages.Clear();
            GameSettings.Data.PlanetsOwned.NumericValue = 70;
            GameSettings.Data.SecondPlaceScore.IsChecked = false;
            RunVictoryCheck();
            Assert.IsEmpty(WinMessages(), "a change to the shared store is seen by the next check");
        }

        [Test]
        public void Row17_TheTenItemsSurviveTheSettingsFileRoundTrip()
        {
            GameSettings settings = NewSettings();
            settings.PlanetsOwned = new EnabledValue(false, 35);
            settings.TechLevels = new EnabledValue(true, 18);
            settings.NumberOfFields = new EnabledValue(true, 5);
            settings.TotalScore = new EnabledValue(true, 7000);
            settings.SecondPlaceScore = new EnabledValue(false, 40);
            settings.ProductionCapacity = new EnabledValue(true, 30);
            settings.CapitalShips = new EnabledValue(true, 20);
            settings.HighestScore = new EnabledValue(true, 80);
            settings.TargetsToMeet = 3;
            settings.MinimumGameTime = 70;

            XmlSerializer serializer = new XmlSerializer(typeof(GameSettings));
            GameSettings loaded;
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.Serialize(stream, settings);
                stream.Position = 0;
                loaded = (GameSettings)serializer.Deserialize(stream);
            }

            Assert.AreEqual((false, 35), (loaded.PlanetsOwned.IsChecked, loaded.PlanetsOwned.NumericValue));
            Assert.AreEqual((true, 18), (loaded.TechLevels.IsChecked, loaded.TechLevels.NumericValue));
            Assert.AreEqual(5, loaded.NumberOfFields.NumericValue);
            Assert.AreEqual((true, 7000), (loaded.TotalScore.IsChecked, loaded.TotalScore.NumericValue));
            Assert.AreEqual((false, 40), (loaded.SecondPlaceScore.IsChecked, loaded.SecondPlaceScore.NumericValue));
            Assert.AreEqual((true, 30), (loaded.ProductionCapacity.IsChecked, loaded.ProductionCapacity.NumericValue));
            Assert.AreEqual((true, 20), (loaded.CapitalShips.IsChecked, loaded.CapitalShips.NumericValue));
            Assert.AreEqual((true, 80), (loaded.HighestScore.IsChecked, loaded.HighestScore.NumericValue));
            Assert.AreEqual(3, loaded.TargetsToMeet);
            Assert.AreEqual(70, loaded.MinimumGameTime);
        }

        // ================================================================ elimination

        [Test]
        public void Elimination_AnyOneOfTheFourWords_KeepsARaceInTheGame()
        {
            // Planets, Unarmed, Escort and Capital must ALL be zero (section 2).
            EmpireData a = AddEmpire(1);
            EmpireData escortOnly = AddEmpire(2);
            EmpireData capitalOnly = AddEmpire(3);
            EmpireData planetOnly = AddEmpire(4);
            EmpireData nothing = AddEmpire(5);
            AddStar(a, "A1", 100000);
            AddStar(planetOnly, "P1", 0);
            AddShips(escortOnly, BeamDesign(1, 100, 1, 1), 1);
            AddShips(capitalOnly, BeamDesign(2, 500, 1, 4), 1);

            RunVictoryCheck();

            Assert.IsFalse(escortOnly.Eliminated, "an escort ship");
            Assert.IsFalse(capitalOnly.Eliminated, "a capital ship");
            Assert.IsFalse(planetOnly.Eliminated, "an owned planet");
            Assert.IsTrue(nothing.Eliminated, "no planets and no ships of any class");
        }

        [Test]
        public void Elimination_TwoRacesWipedOutTheSameTurn_EachIsAnnounced_AndTheLastRaceIsToldOnce()
        {
            EmpireData a = AddEmpire(1);
            EmpireData b = AddEmpire(2);
            EmpireData c = AddEmpire(3);
            AddStar(a, "A1", 100000);

            RunVictoryCheck();

            Assert.IsTrue(b.Eliminated);
            Assert.IsTrue(c.Eliminated);
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == a.Id && m.Text.Contains("All traces of the Race2")), "message 187 for the first");
            Assert.IsTrue(serverData.AllMessages.Exists(m => m.Audience == a.Id && m.Text.Contains("All traces of the Race3")), "message 187 for the second");
            Assert.AreEqual(1, serverData.AllMessages.Count(m => m.Text != null && m.Text.Contains("alone remain")), "message 188 once");
            Assert.AreEqual(a.Id, serverData.AllMessages.Single(m => m.Text.Contains("alone remain")).Audience, "to the survivor");
        }

        [Test]
        public void Elimination_IsTheExactFourWordTest()
        {
            ScoreRecord record = new ScoreRecord();
            Assert.IsTrue(VictoryCheck.IsEliminated(record));

            record.Score = 50;
            record.Resources = 900;
            record.TechLevel = 30;
            record.Starbases = 0;
            Assert.IsTrue(VictoryCheck.IsEliminated(record), "score, resources and tech do not count");

            foreach (Action<ScoreRecord> set in new Action<ScoreRecord>[] { r => r.Planets = 1, r => r.UnarmedShips = 1, r => r.EscortShips = 1, r => r.CapitalShips = 1 })
            {
                ScoreRecord one = new ScoreRecord();
                set(one);
                Assert.IsFalse(VictoryCheck.IsEliminated(one));
            }
        }
    }
}
