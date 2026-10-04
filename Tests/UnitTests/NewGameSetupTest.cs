using System;
using System.Collections.Generic;
using System.Xml;

using NUnit.Framework;

using Nova.Ai;
using Nova.Client;
using Nova.Common;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// The New Game screen's pure rules (Nova.Client.NewGameSetup): behavior-specs-10/
    /// new-game-setup.md sections 2 (Simplified player count), 3/13/14 (preset helpers the screen
    /// shows), 8 (reset to defaults) and victory-conditions.md section 1 (year gate); and the AI
    /// opponent picker's plumbing (ai-opponent-behavior.md section 1a: category carried on
    /// PlayerSettings and turned into DefaultAi's -n code).
    /// </summary>
    [TestFixture]
    public class NewGameSetupTest
    {
        [Test]
        public void YearGate_IsRawPlusThreeTimesTen()
        {
            Assert.AreEqual(30, NewGameSetup.YearGateYears(0));
            Assert.AreEqual(500, NewGameSetup.YearGateYears(47), "raw 47 is the 500-year ceiling of the batch key");
        }

        [Test]
        public void SimplifiedYearGate_GrowsWithGalaxySize()
        {
            int previous = 0;
            foreach (GalaxySize size in Enum.GetValues(typeof(GalaxySize)))
            {
                int years = NewGameSetup.SimplifiedYearGate(size);
                Assert.Greater(years, previous, size.ToString());
                Assert.AreEqual(0, years % 10, "the gate is (raw + 3) x 10");
                previous = years;
            }
        }

        // The Simplified player-count table (by difficulty and size, sequential "1 in n" draws)
        // is NewGameSetupSpec11Test; the earlier uniform "2 / 2-4 / 12-15" ranges are superseded.

        [Test]
        public void SimplifiedPlayerCount_EasyAndStandard_AreDeterministic_AndNeverExceedTheMaximum()
        {
            Random random = new Random(7);
            for (int i = 0; i < 50; i++)
            {
                Assert.AreEqual(2, NewGameSetup.ChooseSimplifiedPlayerCount(GalaxySize.Tiny, NewGameSetup.Easy, random));
                Assert.AreEqual(3, NewGameSetup.ChooseSimplifiedPlayerCount(GalaxySize.Small, NewGameSetup.Standard, random));
                Assert.AreEqual(12, NewGameSetup.ChooseSimplifiedPlayerCount(GalaxySize.Large, NewGameSetup.Standard, random));

                foreach (GalaxySize size in Enum.GetValues(typeof(GalaxySize)))
                {
                    for (int difficulty = NewGameSetup.Easy; difficulty <= NewGameSetup.Expert; difficulty++)
                    {
                        Assert.That(NewGameSetup.ChooseSimplifiedPlayerCount(size, difficulty, random), Is.AtMost(NewGameSetup.MaximumPlayers));
                    }
                }
            }
        }

        [Test]
        public void ResetToDefaults_RestoresTheSimplifiedDialogsState()
        {
            // A private instance (the constructor is private) so the shared singleton is untouched.
            GameSettings settings = (GameSettings)Activator.CreateInstance(typeof(GameSettings), nonPublic: true);
            {
                settings.ApplyGalaxyPreset(GalaxySize.Huge, GalaxyDensity.Packed);
                settings.StartingDistanceSetting = StartingDistance.Close;
                settings.MaximumMinerals = true;
                settings.GalaxyClumping = true;
                settings.AcceleratedStart = true;
                settings.NoRandomEvents = true;
                settings.SlowTechAdvance = true;
                settings.PlanetsOwned.IsChecked = false;
                settings.PlanetsOwned.NumericValue = 99;
                settings.TotalScore.IsChecked = true;
                settings.TargetsToMeet = 4;
                settings.MinimumGameTime = 200;

                NewGameSetup.ResetToDefaults(settings);

                // new-game-setup.md section 8: no reset exists in the original; the Simplified
                // dialog's state (section 2: default size Small, density Normal, Moderate for the
                // default Standard difficulty, flags cleared) is what the button restores.
                Assert.IsTrue(settings.UseGalaxyPresets);
                Assert.AreEqual(GalaxySize.Small, settings.GalaxySizeSetting, "the Simplified dialog's default size");
                Assert.AreEqual(GalaxyDensity.Normal, settings.StarDensitySetting, "density Normal");
                Assert.AreEqual(StartingDistance.Moderate, settings.StartingDistanceSetting, "Moderate for Standard");
                Assert.AreEqual(800, settings.MapWidth);
                Assert.AreEqual(128, settings.NumberOfStars, "Small Normal = 800^2 / 5000");
                Assert.IsFalse(settings.MaximumMinerals || settings.GalaxyClumping || settings.AcceleratedStart
                    || settings.NoRandomEvents || settings.SlowTechAdvance);

                NewGameSetup.ResetToDefaults(settings, NewGameSetup.Expert);
                Assert.AreEqual(StartingDistance.Farther, settings.StartingDistanceSetting, "Farther for Harder and Expert");

                // victory-conditions.md section 1, read off the real dialog.
                Assert.IsTrue(settings.PlanetsOwned.IsChecked);
                Assert.AreEqual(60, settings.PlanetsOwned.NumericValue);
                Assert.IsTrue(settings.TechLevels.IsChecked);
                Assert.AreEqual(22, settings.TechLevels.NumericValue);
                Assert.AreEqual(4, settings.NumberOfFields.NumericValue);
                Assert.IsFalse(settings.TotalScore.IsChecked);
                Assert.AreEqual(11000, settings.TotalScore.NumericValue);
                Assert.IsTrue(settings.SecondPlaceScore.IsChecked);
                Assert.AreEqual(100, settings.SecondPlaceScore.NumericValue);
                Assert.IsFalse(settings.ProductionCapacity.IsChecked);
                Assert.IsFalse(settings.CapitalShips.IsChecked);
                Assert.IsFalse(settings.HighestScore.IsChecked);
                Assert.AreEqual(100, settings.HighestScore.NumericValue);
                Assert.AreEqual(1, settings.TargetsToMeet);
                Assert.AreEqual(50, settings.MinimumGameTime);
            }
        }

        [Test]
        public void UniqueRaceName_SuffixesOnlyOnCollision()
        {
            List<string> taken = new List<string> { "Humanoid", "Robotoids" };
            Assert.AreEqual("Macinti", NewGameSetup.UniqueRaceName("Macinti", taken));
            Assert.AreEqual("Robotoids 2", NewGameSetup.UniqueRaceName("Robotoids", taken));
            taken.Add("Robotoids 2");
            Assert.AreEqual("Robotoids 3", NewGameSetup.UniqueRaceName("Robotoids", taken));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public void PersonalityCode_RoundTripsThroughDefaultAisMapping(int category)
        {
            int code = NewGameSetup.PersonalityCodeForCategory(category);
            Assert.AreEqual(category, AiCategory.ForPersonality(code));
        }

        [Test]
        public void PersonalityCode_ArchetypesAreTheStandardRange()
        {
            Assert.AreEqual(DefaultAi.MinStandardPersonality, NewGameSetup.PersonalityCodeForCategory(AiCategory.Robotoids));
            Assert.AreEqual(DefaultAi.MaxStandardPersonality, NewGameSetup.PersonalityCodeForCategory(AiCategory.Macinti));
            Assert.AreEqual(DefaultAi.DisabledPersonality, NewGameSetup.PersonalityCodeForCategory(AiCategory.NoDriver));
            Assert.AreEqual(DefaultAi.PassivePersonality, NewGameSetup.PersonalityCodeForCategory(AiCategory.EconomyOnly));
        }

        [Test]
        public void PlayerSettings_AiCategory_RoundTripsThroughXml()
        {
            PlayerSettings settings = new PlayerSettings { PlayerNumber = 2, RaceName = "Cybertrons", AiProgram = "Default AI", AiCategory = 4 };
            XmlDocument doc = new XmlDocument();
            XmlElement element = settings.ToXml(doc);

            PlayerSettings loaded = new PlayerSettings(element);
            Assert.AreEqual(4, loaded.AiCategory);
            Assert.AreEqual("Cybertrons", loaded.RaceName);
        }

        [Test]
        public void PlayerSettings_AiCategory_IsUnsetByDefaultAndNotWritten()
        {
            PlayerSettings settings = new PlayerSettings { PlayerNumber = 1, RaceName = "Humanoid", AiProgram = "Human" };
            XmlDocument doc = new XmlDocument();
            XmlElement element = settings.ToXml(doc);

            Assert.IsNull(element.SelectSingleNode("AiCategory"), "older-format files are unchanged when no category is chosen");
            Assert.AreEqual(-1, new PlayerSettings(element).AiCategory);
        }
    }
}
