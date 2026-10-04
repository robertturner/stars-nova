namespace Nova.Tests.UnitTests
{
    using System.Reflection;

    using NUnit.Framework;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Server;
    using Nova.Server.TurnSteps;

    // behavior-specs-9: trait/PRT gates run BEFORE the tech-level check and never replace it
    // (research-tech-tree.md), and the gate table per (category, subtype) is pinned in
    // ship-design-and-components.md section 5. Covers the data fixes in components.xml (mining
    // robots, mining hulls, mine layers, Energy Dampener, the NRSE key mismatch) and the level-up path.
    [TestFixture]
    public class ComponentTraitGateTest
    {
        private static Component Fetch(string name)
        {
            Component component = new AllComponents().Fetch(name);
            Assert.NotNull(component, name);
            return component;
        }

        private static Race RaceWith(string primary, params string[] lesser)
        {
            Race race = new Race();
            race.Traits.SetPrimary(primary);
            foreach (string trait in lesser)
            {
                race.Traits.Add(trait);
            }
            return race;
        }

        private static bool Buildable(string component, Race race)
        {
            return !RaceComponents.IsRestrictedFor(Fetch(component), race);
        }

        [TestCase("Robo-Midget Miner")]
        [TestCase("Robo-Miner")]
        [TestCase("Robo-Maxi Miner")]
        [TestCase("Robo-Super Miner")]
        [TestCase("Robo-Ultra Miner")]
        public void OnlyBasicRemoteMining_LosesTheFiveLargerRobots(string robot)
        {
            Assert.IsFalse(Buildable(robot, RaceWith("HE", "OBRM")), robot);
        }

        [Test]
        public void OnlyBasicRemoteMining_KeepsTheRoboMiniMiner()
        {
            Assert.IsTrue(Buildable("Robo-Mini Miner", RaceWith("HE", "OBRM")));
        }

        [TestCase("Robo-Midget Miner")]
        [TestCase("Robo-Ultra Miner")]
        public void AdvancedRemoteMining_IsRequiredForTheMidgetAndUltraRobots(string robot)
        {
            Assert.IsFalse(Buildable(robot, RaceWith("HE")), robot + " needs ARM");
            Assert.IsTrue(Buildable(robot, RaceWith("HE", "ARM")), robot);
        }

        [TestCase("Midget Miner")]
        [TestCase("Miner")]
        [TestCase("Ultra Miner")]
        [TestCase("Maxi Miner")]
        public void OnlyBasicRemoteMining_LosesTheMiningHulls(string hull)
        {
            Assert.IsFalse(Buildable(hull, RaceWith("HE", "OBRM")), hull);
        }

        [TestCase("Mine Dispenser 40")]
        [TestCase("Mine Dispenser 80")]
        [TestCase("Mine Dispenser 130")]
        [TestCase("Speed Trap 30")]
        [TestCase("Speed Trap 50")]
        [TestCase("Heavy Dispenser 50")]
        [TestCase("Energy Dampener")]
        public void SpaceDemolitionOnlyComponents(string name)
        {
            Assert.IsFalse(Buildable(name, RaceWith("HE")), name + " is Space Demolition only");
            Assert.IsTrue(Buildable(name, RaceWith("SD")), name);
        }

        [Test]
        public void MineDispenser50_IsOpenToEveryPrtExceptWarMonger()
        {
            Assert.IsTrue(Buildable("Mine Dispenser 50", RaceWith("HE")));
            Assert.IsFalse(Buildable("Mine Dispenser 50", RaceWith("WM")));
        }

        // behavior-specs-10/race-traits.md section 2 mine-layer table: Speed Trap 20 is "PRT is
        // Space Demolition or Inner Strength". PRTs are mutually exclusive, so the OR gate is
        // the eight other PRT keys set to 0 in components.xml.
        [TestCase("SD", true)]
        [TestCase("IS", true)]
        [TestCase("HE", false)]
        [TestCase("SS", false)]
        [TestCase("WM", false)]
        [TestCase("CA", false)]
        [TestCase("PP", false)]
        [TestCase("IT", false)]
        [TestCase("AR", false)]
        [TestCase("JOAT", false)]
        public void SpeedTrap20_IsSpaceDemolitionOrInnerStrengthOnly(string primary, bool buildable)
        {
            Assert.AreEqual(buildable, Buildable("Speed Trap 20", RaceWith(primary)), primary);
        }

        [Test]
        public void NoRamScoopEngines_GatesRamScoopsOutAndInterspace10In()
        {
            // The NRSE restrictions used an NRSE tag while the trait key is "NRS", so every one was
            // silently dropped: ram-scoop races kept the scoops and Interspace-10 was open to all.
            Race nrs = RaceWith("HE", "NRS");
            Race other = RaceWith("HE");

            Assert.IsFalse(Buildable("Radiating Hydro-Ram Scoop", nrs));
            Assert.IsTrue(Buildable("Radiating Hydro-Ram Scoop", other));
            Assert.IsTrue(Buildable("Interspace 10", nrs));
            Assert.IsFalse(Buildable("Interspace 10", other));
        }

        [Test]
        public void GravityTerraformPlusMinus11_Exists_WithTheExeRequirements()
        {
            Component c = Fetch("Gravity ±11");

            Assert.AreEqual(10, c.RequiredTech[TechLevel.ResearchField.Propulsion]);
            Assert.AreEqual(3, c.RequiredTech[TechLevel.ResearchField.Biotechnology]);
            Assert.AreEqual(4, Fetch("Gravity ±15").RequiredTech[TechLevel.ResearchField.Biotechnology]);
        }

        [TestCase("Chameleon Scanner", 40)]
        [TestCase("Shadow Shield", 70)]
        [TestCase("Depleted Neutronium", 50)]
        [TestCase("Orbital Adjuster", 50)]
        [TestCase("Enigma Pulsar", 20)]
        [TestCase("Langston Shell", 20)]
        [TestCase("Mega Poly Shell", 40)]
        [TestCase("Multi Contained Munition", 20)]
        [TestCase("Alien Miner", 60)]
        [TestCase("Multi Cargo Pod", 20)]
        public void RawCloakPoints_MatchTheSpecTable(string name, int points)
        {
            Component c = Fetch(name);

            Assert.IsTrue(c.Properties.ContainsKey("Cloak"), name + " has no Cloak property");
            Assert.AreEqual(points, ((ProbabilityProperty)c.Properties["Cloak"]).Value, name);
        }

        private static void LevelUp(StarUpdateStep step, TechLevel.ResearchField area, EmpireData empire)
        {
            typeof(StarUpdateStep).GetMethod("TechLevelUp", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(step, new object[] { area, empire });
        }

        [Test]
        public void TechLevelUp_DoesNotHandOutAComponentTheRacesTraitsBar()
        {
            Component spaceDock = Fetch("Space Dock"); // requires Improved Starbases

            TechLevel.ResearchField area = TechLevel.ResearchField.Construction;
            foreach (TechLevel.ResearchField f in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (spaceDock.RequiredTech[f] > spaceDock.RequiredTech[area])
                {
                    area = f;
                }
            }
            Assume.That(spaceDock.RequiredTech[area], Is.GreaterThan(0));

            ServerData serverState = new SimpleServerData();
            StarUpdateStep step = new StarUpdateStep();
            typeof(StarUpdateStep).GetField("serverState", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(step, serverState);

            EmpireData plain = new SimpleEmpireData { Id = 1, Race = RaceWith("HE") };
            EmpireData isb = new SimpleEmpireData { Id = 2, Race = RaceWith("HE", "ISB") };
            foreach (EmpireData empire in new[] { plain, isb })
            {
                foreach (TechLevel.ResearchField f in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
                {
                    empire.ResearchLevels[f] = spaceDock.RequiredTech[f];
                }
                empire.ResearchLevels[area] = spaceDock.RequiredTech[area] - 1;
                empire.AvailableComponents = new RaceComponents();
                serverState.AllEmpires.Add(empire.Id, empire);
                LevelUp(step, area, empire);
            }

            Assert.IsFalse(plain.AvailableComponents.Contains("Space Dock"),
                "A race without Improved Starbases must not receive the Space Dock by researching its levels");
            Assert.IsTrue(isb.AvailableComponents.Contains("Space Dock"), "...while one with the trait still does");
        }
    }
}
