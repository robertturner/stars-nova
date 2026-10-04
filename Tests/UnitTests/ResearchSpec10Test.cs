namespace Nova.Tests.UnitTests
{
    using System.Reflection;
    using System.Xml;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    /// <summary>
    /// behavior-specs-11/research-tech-tree.md rows 8, 14, 16 and 26: Slow Tech Advance doubles
    /// the finished cost; the "next field" setting with the "lowest field" auto-target and the
    /// level-cap switch (the PRT-conditional field exclusions are withdrawn); Super Stealth's
    /// passive research bonus.
    /// </summary>
    [TestFixture]
    public class ResearchSpec10Test
    {
        // ---- Slow Tech Advance (row 8) ----

        [Test]
        public void SlowTechAdvance_DoublesTheCost_AfterTheCostFactor()
        {
            Race race = new Race();
            race.ResearchCosts[TechLevel.ResearchField.Weapons] = 175;
            TechLevel levels = new TechLevel(0, 0, 0, 0, 0, 0);

            // 130 x 175 / 100 = 227, doubled = 454 (doubling first would give 455).
            Assert.AreEqual(227, Research.Cost(TechLevel.ResearchField.Weapons, race, levels, 3, false));
            Assert.AreEqual(454, Research.Cost(TechLevel.ResearchField.Weapons, race, levels, 3, true));
        }

        [Test]
        public void SlowTechAdvance_IsReadFromTheGameSettings()
        {
            Race race = new Race();
            race.ResearchCosts[TechLevel.ResearchField.Energy] = 100;
            TechLevel levels = new TechLevel(0, 0, 0, 0, 0, 0);
            bool saved = GameSettings.Data.SlowTechAdvance;
            try
            {
                GameSettings.Data.SlowTechAdvance = false;
                Assert.AreEqual(50, Research.Cost(TechLevel.ResearchField.Energy, race, levels, 1));
                GameSettings.Data.SlowTechAdvance = true;
                Assert.AreEqual(100, Research.Cost(TechLevel.ResearchField.Energy, race, levels, 1));
            }
            finally
            {
                GameSettings.Data.SlowTechAdvance = saved;
            }
        }

        // ---- Lowest field (rows 14, 26) ----

        private static TechLevel Levels(int energy, int weapons, int propulsion, int construction, int electronics, int biotech)
        {
            TechLevel levels = new TechLevel();
            levels[TechLevel.ResearchField.Energy] = energy;
            levels[TechLevel.ResearchField.Weapons] = weapons;
            levels[TechLevel.ResearchField.Propulsion] = propulsion;
            levels[TechLevel.ResearchField.Construction] = construction;
            levels[TechLevel.ResearchField.Electronics] = electronics;
            levels[TechLevel.ResearchField.Biotechnology] = biotech;
            return levels;
        }

        private static Race RaceWith(string primaryTrait)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primaryTrait);
            return race;
        }

        [Test]
        public void LowestField_IsTheLowestLevel()
        {
            Assert.AreEqual(TechLevel.ResearchField.Construction,
                Research.LowestField(Levels(5, 4, 3, 1, 2, 6)));
        }

        [Test]
        public void LowestField_TiesGoToTheEarlierFieldInTheOriginalOrder()
        {
            // Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology.
            Assert.AreEqual(TechLevel.ResearchField.Propulsion,
                Research.LowestField(Levels(3, 3, 2, 2, 2, 2)));
        }

        [Test]
        public void LowestField_SkipsAFieldAtTheTopLevel()
        {
            Assert.AreEqual(TechLevel.ResearchField.Weapons,
                Research.LowestField(Levels(26, 25, 26, 26, 26, 26)));
            Assert.IsNull(Research.LowestField(Levels(26, 26, 26, 26, 26, 26)));
        }

        /// <summary>
        /// research-tech-tree.md section 7: the AR/CA field-list exclusion is withdrawn - the
        /// lowest-field choice is the lowest of all six fields for every race.
        /// </summary>
        [Test]
        public void EveryRace_UsesTheLowestOfAllSixFields_WithNoPrtExclusion()
        {
            Assert.AreEqual(TechLevel.ResearchField.Energy,
                Research.LowestField(Levels(0, 0, 0, 3, 1, 2)), "AR no longer skips Energy/Weapons/Propulsion");
            Assert.AreEqual(TechLevel.ResearchField.Electronics,
                Research.LowestField(Levels(5, 5, 5, 4, 0, 0)), "CA no longer skips Electronics/Biotechnology");
        }

        // ---- The switch after a level is gained ----

        private static int TopicOf(EmpireData empire)
        {
            foreach (TechLevel.ResearchField field in Research.OriginalFieldOrder)
            {
                if (empire.ResearchTopics[field] == 1)
                {
                    return (int)field;
                }
            }

            return -1;
        }

        private static void Contribute(StarUpdateStep step, ServerData server, Star star, int amount)
        {
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(step, server);
            typeof(StarUpdateStep).GetMethod("ContributeResearch", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(step, new object[] { star, amount });
        }

        private static EmpireData NewEmpire(ServerData server, int id, string primaryTrait)
        {
            EmpireData empire = new SimpleEmpireData();
            empire.Id = (ushort)id;
            empire.Race = RaceWith(primaryTrait);
            foreach (TechLevel.ResearchField field in Research.OriginalFieldOrder)
            {
                empire.Race.ResearchCosts[field] = 100;
            }

            empire.AvailableComponents = new Nova.Common.Components.RaceComponents();
            empire.ResearchTopics = new TechLevel();
            empire.ResearchTopics[TechLevel.ResearchField.Energy] = 1;
            server.AllEmpires.Add(id, empire);
            return empire;
        }

        [Test]
        public void NextFieldLowest_ResearchMovesToTheLowestFieldOnceTheTargetGainsALevel()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "JOAT");
            empire.ResearchLevels = Levels(0, 1, 1, 1, 1, 0);
            empire.ResearchNextField = Research.NextFieldLowest;
            Star star = new Star { Name = "Home", Owner = 1 };

            // Energy level 1 costs 50 + 10 x 4 = 90.
            Contribute(new StarUpdateStep(), server, star, 90);

            Assert.AreEqual(1, empire.ResearchLevels[TechLevel.ResearchField.Energy]);
            Assert.AreEqual((int)TechLevel.ResearchField.Biotechnology, TopicOf(empire));
        }

        [Test]
        public void NextFieldSame_StaysOnTheCurrentField()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "JOAT");
            Star star = new Star { Name = "Home", Owner = 1 };

            Contribute(new StarUpdateStep(), server, star, 50);

            Assert.AreEqual(1, empire.ResearchLevels[TechLevel.ResearchField.Energy]);
            Assert.AreEqual((int)TechLevel.ResearchField.Energy, TopicOf(empire));
        }

        /// <summary>
        /// research-tech-tree.md section 4: a current field at level 26 with "same field" moves
        /// research to the lowest field, even though no level was gained this turn.
        /// </summary>
        [Test]
        public void NextFieldSame_AtTheLevelCap_MovesToTheLowestField()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "JOAT");
            empire.ResearchNextField = Research.NextFieldSame;
            empire.ResearchLevels = Levels(26, 5, 5, 5, 5, 5);
            Star star = new Star { Name = "Home", Owner = 1 };

            Contribute(new StarUpdateStep(), server, star, 10);

            Assert.AreEqual((int)TechLevel.ResearchField.Weapons, TopicOf(empire),
                "Energy is capped, so research moves to the lowest field");
        }

        [Test]
        public void NextFieldChosen_MovesThereAfterALevel_ButNotBefore()
        {
            ServerData server = new ServerData();
            EmpireData empire = NewEmpire(server, 1, "JOAT");
            empire.ResearchNextField = (int)TechLevel.ResearchField.Biotechnology;
            Star star = new Star { Name = "Home", Owner = 1 };
            StarUpdateStep step = new StarUpdateStep();

            Contribute(step, server, star, 30);
            Assert.AreEqual((int)TechLevel.ResearchField.Energy, TopicOf(empire), "No level yet");

            Contribute(step, server, star, 20);
            Assert.AreEqual((int)TechLevel.ResearchField.Biotechnology, TopicOf(empire));
        }

        [Test]
        public void NextField_SurvivesAnXmlRoundTrip_AndTravelsInTheResearchCommand()
        {
            EmpireData empire = new EmpireData();
            empire.ResearchNextField = Research.NextFieldLowest;
            XmlDocument doc = new XmlDocument();
            EmpireData copy = new EmpireData(empire.ToXml(doc));
            Assert.AreEqual(Research.NextFieldLowest, copy.ResearchNextField);

            ResearchCommand command = new ResearchCommand { Budget = copy.ResearchBudget, Topics = copy.ResearchTopics, NextField = 3 };
            ResearchCommand read = new ResearchCommand(command.ToXml(doc));
            Assert.AreEqual(3, read.NextField);
            Assert.IsTrue(read.IsValid(copy), "Only the next-field setting changed");
            read.ApplyToState(copy);
            Assert.AreEqual(3, copy.ResearchNextField);
        }

        // ---- Super Stealth passive research (row 16) ----

        [Test]
        public void SuperStealth_GainsHalfTheAverageEveryRaceSpentInEachField()
        {
            ServerData server = new ServerData();
            EmpireData other = NewEmpire(server, 1, "JOAT");
            EmpireData stealth = NewEmpire(server, 2, "SS");
            other.ResearchLevels = Levels(26, 26, 26, 26, 26, 26);
            stealth.ResearchLevels = Levels(26, 26, 26, 26, 26, 26);

            StarUpdateStep step = new StarUpdateStep();
            Contribute(step, server, new Star { Name = "A", Owner = 1 }, 1000);
            Contribute(step, server, new Star { Name = "B", Owner = 2 }, 200);

            int before = stealth.ResearchResources[TechLevel.ResearchField.Energy];
            step.ApplySuperStealthResearchBonus(server);

            // (1000 + 200) / 2 races / 2 = 300 in Energy; nothing in the other fields.
            Assert.AreEqual(before + 300, stealth.ResearchResources[TechLevel.ResearchField.Energy]);
            Assert.AreEqual(0, stealth.ResearchResources[TechLevel.ResearchField.Weapons]);
            Assert.AreEqual(1000, other.ResearchResources[TechLevel.ResearchField.Energy], "Only Super Stealth gains");
        }

        [Test]
        public void SuperStealth_BonusCanBuyALevelTheSameYear()
        {
            ServerData server = new ServerData();
            EmpireData other = NewEmpire(server, 1, "JOAT");
            EmpireData stealth = NewEmpire(server, 2, "SS");
            other.ResearchLevels = Levels(26, 26, 26, 26, 26, 26);

            StarUpdateStep step = new StarUpdateStep();
            Contribute(step, server, new Star { Name = "A", Owner = 1 }, 4000);

            step.ApplySuperStealthResearchBonus(server);

            // 4000 / 2 / 2 = 1000 in Energy, enough for Energy 1 (50) and more.
            Assert.Greater(stealth.ResearchLevels[TechLevel.ResearchField.Energy], 0);
        }

        [Test]
        public void SuperStealth_AloneInTheGame_GainsNothing()
        {
            ServerData server = new ServerData();
            EmpireData stealth = NewEmpire(server, 2, "SS");
            stealth.ResearchLevels = Levels(26, 26, 26, 26, 26, 26);

            StarUpdateStep step = new StarUpdateStep();
            Contribute(step, server, new Star { Name = "B", Owner = 2 }, 1000);
            step.ApplySuperStealthResearchBonus(server);

            Assert.AreEqual(1000, stealth.ResearchResources[TechLevel.ResearchField.Energy]);
        }
    }
}
