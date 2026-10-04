using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using Nova.Ai;
using Nova.Common;
using Nova.Common.Components;

namespace Nova.Tests.UnitTests
{
    /// <summary>
    /// behavior-specs-10/ai-opponent-behavior.md §15 (the AI design builder: 45 part groups of
    /// 139 entries, per-personality templates), §17 (role tags instead of fixed design slots,
    /// starting designs mapped onto roles by content) and §14 (AI starting designs).
    /// </summary>
    [TestFixture]
    public class AiDesignBuilderTest
    {
        private static readonly AllComponents Components = new AllComponents();

        private static Dictionary<string, Component> Available(params string[] names)
        {
            Dictionary<string, Component> available = new Dictionary<string, Component>();
            foreach (string name in names)
            {
                Component component = null;
                foreach (string candidate in DesignPartGroups.NovaNames(name))
                {
                    component = component ?? Components.Fetch(candidate);
                }

                Assert.IsNotNull(component, name);
                available[component.Name] = component;
            }

            return available;
        }

        private static Dictionary<string, Component> Everything()
        {
            return Components.GetAll.ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        private static HullModule ModuleHolding(ShipDesign design, string part)
        {
            return design.Hull.Modules.Single(module => module.AllocatedComponent != null && module.AllocatedComponent.Name == part);
        }

        // ---- part groups -------------------------------------------------------------------

        [Test]
        public void PartGroups_45Groups_139Entries()
        {
            int entries = 0;
            for (int group = 0; group < DesignPartGroups.GroupCount; group++)
            {
                entries += DesignPartGroups.Entries(group).Count;
            }

            Assert.AreEqual(45, DesignPartGroups.GroupCount);
            Assert.AreEqual(DesignPartGroups.EntryCount, entries);
            Assert.AreEqual(139, entries);
        }

        [Test]
        public void PartGroup0_TriesAntiMatterTorpedoDownToAlpha()
        {
            CollectionAssert.AreEqual(
                new[] { "Anti Matter Torpedo", "Omega Torpedo", "Upsilon Torpedo", "Rho Torpedo", "Epsilon Torpedo", "Delta Torpedo", "Beta Torpedo", "Alpha Torpedo" },
                DesignPartGroups.Candidates(0).Select(c => c.Name).ToArray());
        }

        [Test]
        public void PartGroup9_ArmourOrderIsNotTableOrder()
        {
            CollectionAssert.AreEqual(
                new[] { "Superlatanium", "Mega Poly Shell", "Valanium", "Depleted Neutronium", "Neutronium", "Fielded Kelarium", "Kelarium",
                        "Organic Armor", "Strobnium", "Carbonic Armor", "Crobmnium", "Tritanium" },
                DesignPartGroups.Candidates(9).Select(c => c.Name).ToArray());
        }

        [Test]
        public void PartGroup44_NeverTriesSettlersDelight()
        {
            string[] names = DesignPartGroups.Candidates(44).Select(c => c.Name).ToArray();

            Assert.AreEqual("Galaxy Scoop", names.First());
            Assert.AreEqual("Fuel Mizer", names.Last());
            CollectionAssert.DoesNotContain(names, "Settler's Delight");
            Assert.AreEqual(3, DesignPartGroups.Entries(44).Count, "the zero-step entry is still one of the 139");
        }

        [Test]
        public void EveryGroupPart_ExistsInTheComponentData()
        {
            for (int group = 0; group < DesignPartGroups.GroupCount; group++)
            {
                foreach (PartCandidate candidate in DesignPartGroups.Candidates(group))
                {
                    Assert.IsTrue(DesignPartGroups.NovaNames(candidate.Name).Any(Components.Contains), "group " + group + ": " + candidate.Name);
                }
            }
        }

        // ---- templates ---------------------------------------------------------------------

        [Test]
        public void EveryRoleTemplate_HasOneGroupPerHullSlot()
        {
            foreach (Tuple<string, int[]> pair in AiDesignRoleTable.AllTemplates())
            {
                Assert.IsTrue(DesignBuilderHulls.IsKnown(pair.Item1), pair.Item1);
                Assert.AreEqual(DesignBuilderHulls.Slots(pair.Item1).Count, pair.Item2.Length, pair.Item1 + " " + string.Join(".", pair.Item2));
                Assert.IsTrue(pair.Item2.All(g => g >= 0 && g < DesignPartGroups.GroupCount));
            }
        }

        [Test]
        public void EveryRoleTemplate_BuildsWhenEverythingIsAvailable()
        {
            DesignBuilder builder = new DesignBuilder(Everything(), new Random(1));
            foreach (Tuple<string, int[]> pair in AiDesignRoleTable.AllTemplates())
            {
                ShipDesign design = builder.Build(pair.Item1, pair.Item2, 1, "t");
                Assert.IsNotNull(design, pair.Item1 + " " + string.Join(".", pair.Item2));
                int filled = design.Hull.Modules.Count(module => module.AllocatedComponent != null);
                Assert.AreEqual(pair.Item2.Length, filled, pair.Item1 + " " + string.Join(".", pair.Item2));
            }
        }

        [Test]
        public void TheSeventeenRoleHulls_AllHaveSlotLists()
        {
            string[] hulls = AiDesignRoleTable.AllTemplates().Select(p => p.Item1).Distinct().ToArray();
            foreach (string hull in hulls)
            {
                Assert.IsTrue(DesignPartGroups.NovaNames(hull).Any(Components.Contains), hull);
            }

            Assert.AreEqual(DesignBuilderHulls.Slots("Battleship").Count, 11);
            Assert.AreEqual(DesignBuilderHulls.Slots("Nubian").Count, 13);
        }

        // ---- the builder -------------------------------------------------------------------

        [Test]
        public void Resolver_InstallsTheFirstBuildablePart()
        {
            DesignBuilder builder = new DesignBuilder(Available("Laser", "X-Ray Laser", "Mini Blaster", "Tritanium", "Neutronium", "Depleted Neutronium"), new Random(1));

            Assert.AreEqual("Mini Blaster", builder.ResolvePart(4, SlotMask.Beam).Value.Name);
            Assert.AreEqual("Depleted Neutronium", builder.ResolvePart(9, SlotMask.Armor).Value.Name);
            Assert.IsNull(builder.ResolvePart(0, SlotMask.Torpedo));
            Assert.IsNull(builder.ResolvePart(9, SlotMask.Shield), "a shield slot takes no armour");
        }

        [Test]
        public void Build_FillsEverySlotToCapacity_MinelayerFrigate()
        {
            DesignBuilder builder = new DesignBuilder(
                Available("Frigate", "Radiating Hydro-Ram Scoop", "Possum Scanner", "Bat Scanner", "Mine Dispenser 40", "Mine Dispenser 50", "Mole-skin Shield"),
                new Random(1));

            ShipDesign design = builder.Build("Frigate", DesignBuilder.ParseTemplate("24.26.25.10"), 77, "Frigate");

            Assert.IsNotNull(design);
            Assert.AreEqual(77, design.Key);
            Assert.AreEqual("Frigate", design.Blueprint.Name);
            Assert.AreEqual(1, ModuleHolding(design, "Radiating Hydro-Ram Scoop").ComponentCount);
            Assert.AreEqual(2, ModuleHolding(design, "Possum Scanner").ComponentCount, "group 26 skips the Bat Scanner");
            Assert.AreEqual(3, ModuleHolding(design, "Mine Dispenser 50").ComponentCount);
            Assert.AreEqual(2, ModuleHolding(design, "Mole-skin Shield").ComponentCount);
            Assert.AreEqual("General Purpose", ModuleHolding(design, "Mine Dispenser 50").ComponentType);
        }

        [Test]
        public void Build_FailsWhenAnySlotFindsNothing()
        {
            DesignBuilder builder = new DesignBuilder(
                Available("Frigate", "Radiating Hydro-Ram Scoop", "Bat Scanner", "Mine Dispenser 40", "Mole-skin Shield"),
                new Random(1));

            Assert.IsNull(builder.Build("Frigate", DesignBuilder.ParseTemplate("24.26.25.10"), 1, "Frigate"), "group 26 does not include the Bat Scanner");
            Assert.IsNull(new DesignBuilder(Available("Radiating Hydro-Ram Scoop"), new Random(1)).Build("Frigate", DesignBuilder.ParseTemplate("24.26.25.10"), 1, "x"), "no hull");
        }

        [Test]
        public void Build_SkipsAPartTheSlotCannotTake()
        {
            // Group 41 starts with Mega Poly Shell, an armour; the Mini-Miner's second slot takes
            // scanners, electrical and mechanical parts only.
            DesignBuilder builder = new DesignBuilder(
                Available("Mini-Miner", "Galaxy Scoop", "Mega Poly Shell", "Jammer 10", "Robo-Miner", "Alien Miner"),
                new Random(1));

            ShipDesign design = builder.Build("Mini-Miner", DesignBuilder.ParseTemplate("24.41.42.42"), 1, "Mini-Miner");

            Assert.IsNotNull(design);
            Assert.AreEqual(1, ModuleHolding(design, "Jammer 10").ComponentCount);
            Assert.IsFalse(design.Hull.Modules.Any(m => m.AllocatedComponent != null && m.AllocatedComponent.Name == "Mega Poly Shell"));
            Assert.AreEqual(2, design.Hull.Modules.Count(m => m.AllocatedComponent != null && m.AllocatedComponent.Name == "Alien Miner"));
        }

        [Test]
        public void Build_GivesEachDesignItsOwnModules()
        {
            Dictionary<string, Component> available = Available("Frigate", "Radiating Hydro-Ram Scoop", "Possum Scanner", "Mine Dispenser 40", "Mole-skin Shield");
            DesignBuilder builder = new DesignBuilder(available, new Random(1));

            ShipDesign first = builder.Build("Frigate", DesignBuilder.ParseTemplate("24.26.25.10"), 1, "a");
            ShipDesign second = builder.Build("Frigate", DesignBuilder.ParseTemplate("24.26.25.10"), 2, "b");

            Assert.AreNotSame(first.Hull.Modules[0], second.Hull.Modules[0]);
            Assert.IsTrue(((Hull)available["Frigate"].Properties["Hull"]).Modules.All(m => m.AllocatedComponent == null), "the available hull is untouched");
            Assert.IsTrue(DesignBuilder.SameBuild(first, second));
        }

        [Test]
        public void BuildAny_IsDeterministicForASeed_AndFallsBackToAnotherTemplate()
        {
            Dictionary<string, Component> available = Available("Destroyer", "Fuel Mizer", "Laser", "Alpha Torpedo", "Tritanium", "Maneuvering Jet", "Battle Computer", "Jammer 10");
            int[][] templates = new[] { "8.4.4.4.17.18.19", "8.3.3.14.17.18.19", "8.4.3.2.17.18.20", "8.4.4.5.17.18.20" }
                .Select(DesignBuilder.ParseTemplate).ToArray();

            ShipDesign a = new DesignBuilder(available, new Random(5)).BuildAny("Destroyer", templates, 1, "a");
            ShipDesign b = new DesignBuilder(available, new Random(5)).BuildAny("Destroyer", templates, 1, "b");
            Assert.IsNotNull(a);
            Assert.IsTrue(DesignBuilder.SameBuild(a, b));

            // Only the Laser (group 4) is available among the beams, so every template but
            // "8.4.4.4.17.18.19" fails on a group-3 or group-5 slot, whatever the shuffle.
            for (int seed = 0; seed < 10; seed++)
            {
                ShipDesign design = new DesignBuilder(available, new Random(seed)).BuildAny("Destroyer", templates, 1, "d");
                Assert.IsNotNull(design);
                Assert.AreEqual(3, design.Hull.Modules.Count(m => m.AllocatedComponent != null && m.AllocatedComponent.Name == "Laser"), "seed " + seed);
            }
        }

        // ---- role tags and starting designs ------------------------------------------------

        [Test]
        public void RoleTag_RoundTripsThroughTheName()
        {
            string name = AiDesignRoleTag.Name("Battleship", "strike2-a", 2134);
            ShipDesign design = new ShipDesign(1) { Name = name };

            Assert.AreEqual("Battleship [strike2-a] T2134", name);
            Assert.AreEqual("strike2-a", AiDesignRoleTag.TagOf(design));
            Assert.AreEqual(2134, ShipDesignRefresher.GetCreationTurn(design));
            Assert.IsNull(AiDesignRoleTag.TagOf(new ShipDesign(2) { Name = "Santa Maria" }));
        }

        [Test]
        public void StartingDesigns_MapOntoTheSlot0AndSlot1Roles()
        {
            Assert.AreEqual("minelayer", AiStartingDesigns.RoleTagFor(AiCategory.Robotoids, AiStartingRole.Scout));
            Assert.AreEqual("colonizer", AiStartingDesigns.RoleTagFor(AiCategory.Robotoids, AiStartingRole.Colonizer));
            Assert.AreEqual("explorer", AiStartingDesigns.RoleTagFor(AiCategory.Automitrons, AiStartingRole.Scout));
            Assert.AreEqual("hunter-a", AiStartingDesigns.RoleTagFor(AiCategory.Turindrones, AiStartingRole.Scout));
            Assert.AreEqual("miner", AiStartingDesigns.RoleTagFor(AiCategory.Turindrones, AiStartingRole.Miner));
            Assert.AreEqual("miner-a", AiStartingDesigns.RoleTagFor(AiCategory.Macinti, AiStartingRole.Miner));
            Assert.IsNull(AiStartingDesigns.RoleTagFor(AiCategory.Robotoids, AiStartingRole.Miner));
        }

        [Test]
        public void StartingDesigns_PerArchetype_Slot0ScoutSlot1Colonizer()
        {
            for (int archetype = 0; archetype < 6; archetype++)
            {
                IReadOnlyList<AiStartingDesign> list = AiStartingDesigns.ForArchetype(archetype, AiRaceTemplates.Standard);
                Assert.AreEqual(AiStartingRole.Scout, list[0].Role, "archetype " + archetype);
                Assert.AreEqual(0, list[0].Slot);
                Assert.AreEqual(AiStartingRole.Colonizer, list[1].Role, "archetype " + archetype);
                Assert.AreEqual(1, list[1].Slot);
            }

            Assert.AreEqual("Spore Cloud", AiStartingDesigns.ForArchetype(AiCategory.Robotoids, 0)[1].Name);
            Assert.AreEqual(3, AiStartingDesigns.ForArchetype(AiCategory.Robotoids, 0)[1].Ships);
            Assert.AreEqual("Long Range Scout", AiStartingDesigns.ForArchetype(AiCategory.Cybertrons, 0)[0].Name);
            Assert.AreEqual("Shadow Sleuth", AiStartingDesigns.ForArchetype(AiCategory.Turindrones, 0, startingEnergy: 3)[0].Name);
            Assert.AreEqual(2, AiStartingDesigns.ForArchetype(AiCategory.Macinti, AiRaceTemplates.Standard).Count);
            Assert.AreEqual("Potato Bug", AiStartingDesigns.ForArchetype(AiCategory.Macinti, AiRaceTemplates.Tough)[2].Name);
        }

        [Test]
        public void Classify_JudgesStartingDesignsByContent()
        {
            DesignBuilder builder = new DesignBuilder(Everything(), new Random(1));
            ShipDesign scout = builder.Build("Scout", DesignBuilder.ParseTemplate("30.26.26"), 1, "Peeping Tom");
            ShipDesign colony = builder.Build("Colony Ship", DesignBuilder.ParseTemplate("8.31"), 2, "Santa Maria");
            ShipDesign miner = builder.Build("Miner", DesignBuilder.ParseTemplate("8.13.28.28.28.28"), 3, "Digger");

            Assert.AreEqual(AiStartingRole.Scout, AiStartingDesigns.Classify(scout));
            Assert.AreEqual(AiStartingRole.Colonizer, AiStartingDesigns.Classify(colony));
            Assert.AreEqual(AiStartingRole.Miner, AiStartingDesigns.Classify(miner));

            Dictionary<string, ShipDesign> current = AiDesignPlanner.CurrentDesigns(AiCategory.Turindrones, new[] { miner, colony, scout });
            Assert.AreSame(scout, current["hunter-a"]);
            Assert.AreSame(colony, current["colonizer"]);
            Assert.AreSame(miner, current["miner"]);
        }

        // ---- the planner -------------------------------------------------------------------

        private sealed class Harness
        {
            public readonly List<ShipDesign> Designs = new List<ShipDesign>();
            public readonly Dictionary<long, int> Ships = new Dictionary<long, int>();
            private long nextKey = 100;

            public AiDesignPlanInput Input(int category, int skill, int year, TechLevel levels, IDictionary<string, Component> available = null)
            {
                return new AiDesignPlanInput
                {
                    Category = category,
                    Skill = skill,
                    TurnYear = Global.StartingYear + year,
                    Levels = levels,
                    Available = available ?? Everything(),
                    Designs = Designs,
                    ShipsInExistence = design => Ships.TryGetValue(design.Key, out int n) ? n : 0,
                    NextDesignKey = () => nextKey++,
                };
            }

            public List<PlannedDesign> Run(AiDesignPlanInput input, int seed = 3)
            {
                List<PlannedDesign> planned = new AiDesignPlanner(new Random(seed)).Plan(input);
                Designs.AddRange(planned.Select(p => p.Design));
                return planned;
            }

            public ShipDesign AddStarting(string hull, string template, string name, int ships)
            {
                ShipDesign design = new DesignBuilder(Everything(), new Random(1)).Build(hull, DesignBuilder.ParseTemplate(template), nextKey++, name);
                Designs.Add(design);
                Ships[design.Key] = ships;
                return design;
            }
        }

        private static TechLevel Levels(int energy, int weapons, int propulsion, int construction, int electronics, int biotechnology)
        {
            TechLevel levels = new TechLevel();
            levels[TechLevel.ResearchField.Energy] = energy;
            levels[TechLevel.ResearchField.Weapons] = weapons;
            levels[TechLevel.ResearchField.Propulsion] = propulsion;
            levels[TechLevel.ResearchField.Construction] = construction;
            levels[TechLevel.ResearchField.Electronics] = electronics;
            levels[TechLevel.ResearchField.Biotechnology] = biotechnology;
            return levels;
        }

        [Test]
        public void Planner_AtStartingTech_BuildsNothingForPersonality0()
        {
            Harness harness = new Harness();
            harness.AddStarting("Scout", "30.26.26", "Scout", 1);
            harness.AddStarting("Colony Ship", "8.31", "Santa Maria", 1);

            Assert.IsEmpty(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Expert, 5, new TechLevel(0))));
        }

        private static string[] Tags(IEnumerable<PlannedDesign> planned)
        {
            return planned.Select(p => p.Role.Tag).ToArray();
        }

        [Test]
        public void Planner_Personality0Minelayer_NeedsSkill2_TheGate_AndAnUnusedScout()
        {
            TechLevel gate = Levels(6, 0, 6, 6, 5, 4);
            Harness harness = new Harness();
            ShipDesign scout = harness.AddStarting("Scout", "30.26.26", "Scout", 1);

            CollectionAssert.DoesNotContain(Tags(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Expert, 5, gate))), "minelayer", "a ship of the starting scout exists");

            harness.Ships[scout.Key] = 0;
            CollectionAssert.DoesNotContain(Tags(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 5, gate))), "minelayer", "skill 2+ only");
            CollectionAssert.DoesNotContain(Tags(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Expert, 5, Levels(6, 0, 6, 6, 4, 4)))), "minelayer", "El>4");

            List<PlannedDesign> planned = harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Expert, 5, gate));
            PlannedDesign minelayer = planned.Single(p => p.Role.Tag == "minelayer");
            Assert.AreEqual("Frigate", minelayer.Design.Blueprint.Name);
            Assert.AreEqual("Frigate [minelayer] T2105", minelayer.Design.Name);
            Assert.AreEqual(harness.Designs.Count, harness.Designs.Select(d => d.Key).Distinct().Count(), "every design gets its own key");

            CollectionAssert.DoesNotContain(Tags(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Expert, 6, gate))), "minelayer", "an identical build is not added again");
        }

        [Test]
        public void Planner_LadderRungs_WaitForTheRungBelowToAge()
        {
            TechLevel strike1 = Levels(6, 10, 9, 10, 0, 0);
            Harness harness = new Harness();

            List<PlannedDesign> first = harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 30, strike1));
            CollectionAssert.AreEqual(new[] { "strike1-a" }, first.Where(p => p.Role.Kind == AiDesignRoleKind.StrikeWarship1).Select(p => p.Role.Tag).ToArray());

            Assert.IsFalse(harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 42, strike1)).Any(p => p.Role.Tag == "strike1-b"), "12 years is not older than 12");

            List<PlannedDesign> later = harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 43, strike1));
            CollectionAssert.AreEqual(new[] { "strike1-b" }, later.Where(p => p.Role.Kind == AiDesignRoleKind.StrikeWarship1).Select(p => p.Role.Tag).ToArray());
            Assert.AreEqual("Meta Morph", later.Single(p => p.Role.Tag == "strike1-b").Design.Blueprint.Name);
        }

        [Test]
        public void Planner_Personality0Freighters_PrivateerBelowConstruction10()
        {
            Harness harness = new Harness();
            List<PlannedDesign> planned = harness.Run(harness.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 10, Levels(0, 0, 2, 4, 0, 0)));

            PlannedDesign freighter = planned.Single(p => p.Role.Tag == "freighter-a");
            Assert.AreEqual("Privateer", freighter.Design.Blueprint.Name);

            Harness rich = new Harness();
            List<PlannedDesign> later = rich.Run(rich.Input(AiCategory.Robotoids, AiRaceTemplates.Standard, 10, Levels(0, 0, 2, 10, 0, 0)));
            Assert.AreEqual("Meta Morph", later.Single(p => p.Role.Tag == "freighter-a").Design.Blueprint.Name);
        }

        [Test]
        public void Planner_Personality4Destroyer_UsesTheFixedTemplateBeforeYear75()
        {
            Harness harness = new Harness();
            List<PlannedDesign> early = harness.Run(harness.Input(AiCategory.Cybertrons, AiRaceTemplates.Standard, 31, new TechLevel(26)));
            ShipDesign hunter = early.Single(p => p.Role.Tag == "hunter-a").Design;
            ShipDesign fixedDesign = new DesignBuilder(Everything(), new Random(1)).Build("Destroyer", DesignBuilder.ParseTemplate("8.4.4.18.17.18.20"), 1, "x");
            Assert.IsTrue(DesignBuilder.SameBuild(fixedDesign, hunter));

            Assert.IsFalse(early.Any(p => p.Role.Tag == "group1-a"), "battle groups start after year 40");
            Assert.IsFalse(early.Any(p => p.Role.Tag == "hunter-b"), "slot 5 waits 20 years on slot 4");
        }

        [Test]
        public void Planner_Personality4BattleGroup_FillsWithItsLeader()
        {
            Harness harness = new Harness();
            List<PlannedDesign> planned = harness.Run(harness.Input(AiCategory.Cybertrons, AiRaceTemplates.Standard, 41, new TechLevel(26)));

            string[] tags = planned.Select(p => p.Role.Tag).ToArray();
            CollectionAssert.IsSubsetOf(new[] { "group1-a", "group1-b", "group1-c", "group1-d" }, tags);
            CollectionAssert.DoesNotContain(tags, "group2-a");
            Assert.AreEqual("Cruiser", planned.Single(p => p.Role.Tag == "group1-a").Design.Blueprint.Name);
            Assert.AreEqual("Nubian", planned.Single(p => p.Role.Tag == "group1-d").Design.Blueprint.Name);
        }

        [Test]
        public void Planner_Personality5Colonizer_RebuiltForTheGalaxyScoop()
        {
            Harness harness = new Harness();
            Dictionary<string, Component> noScoop = Everything();
            noScoop.Remove("Galaxy Scoop");
            ShipDesign withoutScoop = new DesignBuilder(noScoop, new Random(1)).Build("Colony Ship", DesignBuilder.ParseTemplate("8.40"), 50, "Pinta");
            Assert.AreEqual("Enigma Pulsar", withoutScoop.Hull.Modules.First(m => m.ComponentType == "Engine").AllocatedComponent.Name);
            harness.Designs.Add(withoutScoop);
            harness.Ships[withoutScoop.Key] = 1;

            Assert.IsFalse(harness.Run(harness.Input(AiCategory.Macinti, AiRaceTemplates.Standard, 45, new TechLevel(26))).Any(p => p.Role.Tag == "colonizer"), "a ship of the old design exists");

            harness.Ships[withoutScoop.Key] = 0;
            PlannedDesign rebuilt = harness.Run(harness.Input(AiCategory.Macinti, AiRaceTemplates.Standard, 46, new TechLevel(26))).Single(p => p.Role.Tag == "colonizer");
            Assert.AreEqual("Galaxy Scoop", rebuilt.Design.Hull.Modules.First(m => m.ComponentType == "Engine").AllocatedComponent.Name);
        }

        [TestCase(AiCategory.Rototills)]
        [TestCase(AiCategory.NoDriver)]
        [TestCase(AiCategory.EconomyOnly)]
        public void Planner_CategoriesThatBuildNoDesigns(int category)
        {
            Harness harness = new Harness();
            Assert.IsEmpty(harness.Run(harness.Input(category, AiRaceTemplates.Expert, 90, new TechLevel(26))));
        }

        // ---- research cost to reach ---------------------------------------------------------

        [Test]
        public void ResearchCostToReach_SumsEveryMissingLevelLessWhatIsBanked()
        {
            Race race = new Race();
            foreach (TechLevel.ResearchField field in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                race.ResearchCosts[field] = 100;
            }

            race.ResearchCosts[TechLevel.ResearchField.Weapons] = 50;

            TechLevel current = new TechLevel(0);
            TechLevel target = Levels(2, 1, 0, 0, 0, 0);

            // Energy 1 at total 0: 50; Energy 2 at total 1: 80 + 10; Weapons 1 at total 2: (50 + 20) / 2.
            Assert.AreEqual(50 + 90 + 35, ResearchCostToReach.Cost(race, current, null, target));

            TechLevel banked = new TechLevel(0);
            banked[TechLevel.ResearchField.Energy] = 100;
            banked[TechLevel.ResearchField.Weapons] = 500;
            Assert.AreEqual(40, ResearchCostToReach.Cost(race, current, banked, target), "never below zero per field");
            Assert.AreEqual(0, current[TechLevel.ResearchField.Energy], "the caller's levels are restored");

            Assert.AreEqual(0, ResearchCostToReach.Cost(race, Levels(5, 5, 5, 5, 5, 5), null, target));
            Assert.AreEqual(ResearchCostToReach.Unreachable, ResearchCostToReach.Cost(race, current, null, Levels(27, 0, 0, 0, 0, 0)));
        }
    }
}
