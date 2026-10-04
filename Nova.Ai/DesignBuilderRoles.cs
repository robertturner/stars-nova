#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
//
// This file is part of Stars-Nova.
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

namespace Nova.Ai
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;

    /// <summary>What the fleet code does with a role's ships (ai-opponent-behavior.md §12 and
    /// §17's "Role" column).</summary>
    public enum AiDesignRoleKind
    {
        Explorer,
        Hunter,
        Minelayer,
        Colonizer,
        StrikeWarship1,
        StrikeWarship2,
        HalfStrengthWarship,
        Bomber,
        Freighter,
        Hauler,
        LineWarship,
        RemoteMiner,
        Garrison,
        BattleGroup,
        PlanetaryGuard,

        /// <summary>Built but no fleet branch uses it (personality 1 slot 15, personality 2
        /// slot 14).</summary>
        Reserve
    }

    /// <summary>When a role gets a (new) design.</summary>
    public enum AiDesignRefresh
    {
        /// <summary>Built once while the role has no design (and, for an AI-built design,
        /// again once it is <see cref="AiDesignPlanner.StaleRebuildYears"/> old).</summary>
        WhenEmpty,

        /// <summary>Rebuilt when the role has no design or no ship of its design exists ("rebuilt
        /// when unused", "slot unused").</summary>
        WhenUnused,

        /// <summary>A ladder rung: the bottom rung builds while empty; every other rung builds
        /// once the rung below holds a design older than <see cref="AiDesignRole.LadderYears"/>.
        /// The bottom rung is rebuilt once the top rung is that old (this rebuild's reading of how
        /// the ladder keeps cycling).</summary>
        Ladder,

        /// <summary>Built together with (and rebuilt after) the role named by
        /// <see cref="AiDesignRole.PreviousTag"/>: the other slots of a battle group.</summary>
        FollowsLeader,

        /// <summary>Personality 5's colony slot: rebuilt once the Galaxy Scoop is available, no
        /// ship of the old design exists and its engine is not already a Galaxy Scoop.</summary>
        GalaxyScoopUpgrade
    }

    /// <summary>A tech condition on the AI's levels, written as in §17: "En>5 Pr>5" (strictly
    /// greater) and "Co<10" (strictly less).</summary>
    public sealed class TechGate
    {
        public static readonly TechGate None = new TechGate(string.Empty);

        private static readonly Dictionary<string, TechLevel.ResearchField> Fields = new Dictionary<string, TechLevel.ResearchField>
        {
            { "En", TechLevel.ResearchField.Energy },
            { "We", TechLevel.ResearchField.Weapons },
            { "Pr", TechLevel.ResearchField.Propulsion },
            { "Co", TechLevel.ResearchField.Construction },
            { "El", TechLevel.ResearchField.Electronics },
            { "Bi", TechLevel.ResearchField.Biotechnology },
        };

        private readonly List<Tuple<TechLevel.ResearchField, bool, int>> terms = new List<Tuple<TechLevel.ResearchField, bool, int>>();

        public TechGate(string text)
        {
            Text = text;
            foreach (string term in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                bool greater = term.Contains(">");
                string[] parts = term.Split(greater ? '>' : '<');
                terms.Add(Tuple.Create(Fields[parts[0]], greater, int.Parse(parts[1], CultureInfo.InvariantCulture)));
            }
        }

        public string Text { get; private set; }

        public bool Passes(TechLevel levels)
        {
            foreach (Tuple<TechLevel.ResearchField, bool, int> term in terms)
            {
                int level = levels[term.Item1];
                if (term.Item2 ? level <= term.Item3 : level >= term.Item3)
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString()
        {
            return Text;
        }
    }

    /// <summary>One hull a role may be built on, with its templates (§15) and the conditions
    /// on trying it.</summary>
    public sealed class AiHullOption
    {
        public string Hull;
        public int[][] Templates;
        public TechGate Gate = TechGate.None;
        public int MinimumSkill;

        /// <summary>Tried only with this chance (numerator / denominator), e.g. personality 5's
        /// Nubian "tried first two times in three"; 0/0 means always.</summary>
        public int ChanceNumerator;
        public int ChanceDenominator;

        /// <summary>Personality 4's Destroyers: before this year (years since the start) only
        /// the first template is used; from it on, the others. 0 means no such rule.</summary>
        public int FixedFirstTemplateBeforeYear;
    }

    /// <summary>
    /// A role entry (behavior-specs-10/ai-opponent-behavior.md §17's reimplementation note): the
    /// original slot it replaces, its preferred hulls in order with their §15 templates, the
    /// tech gate, and the refresh/ladder rule. Designs carry the role as a tag
    /// (<see cref="AiDesignRoleTag"/>) instead of living in a fixed slot.
    /// </summary>
    public sealed class AiDesignRole
    {
        public string Tag;
        public int Slot;
        public AiDesignRoleKind Kind;
        public TechGate Gate = TechGate.None;
        public int MinimumSkill;

        /// <summary>Only after this many years since the start (strictly greater); -1 none.</summary>
        public int AfterYear = -1;

        /// <summary>Only before this many years since the start (strictly less).</summary>
        public int BeforeYear = int.MaxValue;

        public AiDesignRefresh Refresh = AiDesignRefresh.WhenEmpty;

        /// <summary>The rung below (Ladder) or the leader (FollowsLeader).</summary>
        public string PreviousTag;

        /// <summary>For the bottom rung of a ladder: the top rung, whose age restarts the
        /// ladder.</summary>
        public string LadderTopTag;

        public int LadderYears;

        /// <summary>Preferred hulls in order; empty for a role that only ever holds a starting
        /// design.</summary>
        public AiHullOption[] Hulls = new AiHullOption[0];

        public override string ToString()
        {
            return Tag + " (slot " + Slot + ", " + Kind + ")";
        }
    }

    /// <summary>
    /// The per-personality role lists of §17, each entry carrying the §15 templates of its slot.
    /// Personality 3 builds nothing (its roles hold only the starting designs); categories 6 and
    /// 7 have none.
    /// </summary>
    public static class AiDesignRoleTable
    {
        // §15 template sets shared between personalities.
        private static readonly string[] P0BattleshipFirst =
        {
            "8.11.10.1.1.1.1.2.9.11.19", "8.13.10.1.1.0.0.0.9.11.19", "8.13.10.0.0.1.1.0.9.11.19", "8.13.10.0.0.0.0.3.9.11.19"
        };

        private static readonly string[] P0BattleshipSecond =
        {
            "8.13.10.4.4.4.4.4.9.20.19", "8.13.10.4.3.3.7.2.9.20.19", "8.13.10.2.3.7.7.3.9.20.19", "8.13.10.4.4.3.3.5.9.20.19"
        };

        private const string P0Nubian = "8.10.10.7.5.20.20.4.4.19.4.2.3";

        private static readonly string[] P0DestroyerSlot14 = { "8.4.4.4.17.18.19", "8.3.3.14.17.18.19", "8.4.3.2.17.18.20", "8.4.4.5.17.18.20" };

        private static readonly string[] P0DestroyerSlot15 = { "8.0.0.10.17.18.19", "8.0.0.11.17.18.19", "8.1.1.11.17.18.19", "8.1.1.11.17.18.11" };

        private static readonly string[] P4DestroyerSlot4 = { "8.4.4.18.17.18.20", "8.4.4.5.17.18.20", "8.4.4.4.17.18.19", "8.3.3.14.17.18.19", "8.4.3.2.17.18.20" };

        private static readonly string[] P4DestroyerSlot5 = { "8.0.0.18.17.18.19", "8.0.0.10.17.18.19", "8.0.0.11.17.18.19", "8.1.1.11.17.18.19", "8.1.1.11.17.18.11" };

        private static readonly string[] P4BattleshipFirst =
        {
            "8.18.10.1.1.0.0.1.17.11.11", "8.11.10.1.1.0.0.1.17.11.11", "8.20.10.1.1.2.2.1.17.11.11", "8.11.10.1.1.1.1.1.17.11.11"
        };

        private static readonly string[] P4BattleshipSecond = { "8.18.10.2.2.3.3.2.17.20.20", "8.20.10.2.2.3.3.2.17.20.20", "8.18.10.0.0.3.3.2.17.20.11" };

        private static readonly string[] P4NubianThird =
        {
            "8.11.11.1.1.1.20.20.2.3.3.15.19", "8.11.11.1.1.1.1.1.1.19.19.15.19", "8.20.20.2.2.2.3.3.3.19.19.15.19"
        };

        private static readonly string[] P4CruiserLowest = { "8.20.19.4.4.13.17", "8.20.19.4.3.3.17", "8.20.19.3.2.10.17" };
        private static readonly string[] P4CruiserMiddle = { "8.19.11.0.0.0.17", "8.19.11.0.0.18.17", "8.19.11.0.0.10.17" };
        private static readonly string[] P4CruiserUpper = { "8.19.11.1.1.11.17", "8.19.11.1.1.0.17", "8.19.11.1.1.10.17" };

        private static readonly string[] P1P2BattleshipCore =
        {
            ".6.3.5.3.7.9.20.20", ".0.0.0.0.0.9.19.11", ".6.3.4.2.7.17.20.20", ".1.1.1.1.1.17.19.11"
        };

        private const string MinelayerFrigate = "24.26.25.10";

        private static readonly Dictionary<int, AiDesignRole[]> Roles = new Dictionary<int, AiDesignRole[]>
        {
            { AiCategory.Robotoids, Personality0() },
            { AiCategory.Turindrones, Personality1() },
            { AiCategory.Automitrons, Personality2() },
            { AiCategory.Rototills, Personality3() },
            { AiCategory.Cybertrons, Personality4() },
            { AiCategory.Macinti, Personality5() },
        };

        /// <summary>The role list of a category, in the order the refresh considers them.</summary>
        public static IReadOnlyList<AiDesignRole> ForCategory(int category)
        {
            return Roles.TryGetValue(category, out AiDesignRole[] roles) ? roles : new AiDesignRole[0];
        }

        /// <summary>Every (hull, template) pair used by any role, for validation.</summary>
        public static IEnumerable<Tuple<string, int[]>> AllTemplates()
        {
            foreach (AiDesignRole[] roles in Roles.Values)
            {
                foreach (AiDesignRole role in roles)
                {
                    foreach (AiHullOption option in role.Hulls)
                    {
                        foreach (int[] template in option.Templates)
                        {
                            yield return Tuple.Create(option.Hull, template);
                        }
                    }
                }
            }
        }

        private static AiHullOption H(string hull, params string[] templates)
        {
            return new AiHullOption { Hull = hull, Templates = templates.Select(DesignBuilder.ParseTemplate).ToArray() };
        }

        private static AiHullOption Gated(string hull, string gate, params string[] templates)
        {
            AiHullOption option = H(hull, templates);
            option.Gate = new TechGate(gate);
            return option;
        }

        private static string[] Join(params string[][] sets)
        {
            return sets.SelectMany(s => s).ToArray();
        }

        private static AiDesignRole R(string tag, int slot, AiDesignRoleKind kind, string gate, params AiHullOption[] hulls)
        {
            return new AiDesignRole { Tag = tag, Slot = slot, Kind = kind, Gate = new TechGate(gate), Hulls = hulls };
        }

        private static AiDesignRole Unused(AiDesignRole role)
        {
            role.Refresh = AiDesignRefresh.WhenUnused;
            return role;
        }

        private static AiDesignRole Skill(int minimum, AiDesignRole role)
        {
            role.MinimumSkill = minimum;
            return role;
        }

        private static AiDesignRole After(int year, AiDesignRole role)
        {
            role.AfterYear = year;
            return role;
        }

        /// <summary>Makes a run of roles one ladder of the given age.</summary>
        private static AiDesignRole[] Ladder(int years, params AiDesignRole[] rungs)
        {
            for (int i = 0; i < rungs.Length; i++)
            {
                rungs[i].Refresh = AiDesignRefresh.Ladder;
                rungs[i].LadderYears = years;
                rungs[i].PreviousTag = i == 0 ? null : rungs[i - 1].Tag;
                rungs[i].LadderTopTag = i == 0 ? rungs[rungs.Length - 1].Tag : null;
            }

            return rungs;
        }

        private static AiDesignRole Follows(string leaderTag, AiDesignRole role)
        {
            role.Refresh = AiDesignRefresh.FollowsLeader;
            role.PreviousTag = leaderTag;
            return role;
        }

        /// <summary>Personality 0 (Robotoids).</summary>
        private static AiDesignRole[] Personality0()
        {
            string[] metaEven = { "8.4.10.10.13.9.9", "8.10.5.4.13.12.15", "8.10.4.7.13.12.14", "8.10.3.3.13.12.14" };
            string[] metaOdd = { "8.9.1.1.11.11.12", "8.0.9.10.13.11.12", "8.9.0.0.10.11.12", "8.1.9.12.12.11.11" };
            string[] metaFreighter =
            {
                "8.10.16.16.3.12.2", "8.16.4.3.14.12.13", "8.3.16.10.16.12.14", "8.16.1.11.12.10.10", "8.16.11.12.16.1.0", "8.10.16.16.11.0.0"
            };
            const string strike1 = "We>9 Co>9 Pr>8 En>5";
            const string strike2 = "We>14 Co>11 Pr>11 El>9 En>5 Bi>3";

            List<AiDesignRole> roles = new List<AiDesignRole>
            {
                Skill(2, Unused(R("minelayer", 0, AiDesignRoleKind.Minelayer, "En>5 Pr>5 Co>5 El>4 Bi>3", H("Frigate", MinelayerFrigate)))),
                R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty),
            };
            roles.AddRange(Ladder(
                12,
                R("strike1-a", 2, AiDesignRoleKind.StrikeWarship1, strike1, H("Meta Morph", metaEven)),
                R("strike1-b", 3, AiDesignRoleKind.StrikeWarship1, strike1, H("Meta Morph", metaOdd)),
                R("strike1-c", 4, AiDesignRoleKind.StrikeWarship1, strike1, H("Meta Morph", metaEven)),
                R("strike1-d", 5, AiDesignRoleKind.StrikeWarship1, strike1, H("Meta Morph", metaOdd))));
            roles.AddRange(Ladder(
                20,
                R("strike2-a", 6, AiDesignRoleKind.StrikeWarship2, strike2, H("Battleship", P0BattleshipFirst)),
                R("strike2-b", 7, AiDesignRoleKind.StrikeWarship2, strike2, H("Battleship", P0BattleshipSecond))));
            roles.AddRange(Ladder(
                15,
                R("bomber-a", 9, AiDesignRoleKind.Bomber, "We>13", H("Battleship", "8.13.10.33.33.33.33.33.17.20.19"), H("B-52 Bomber", "24.21.23.23.23.12.10")),
                R("bomber-b", 10, AiDesignRoleKind.Bomber, "We>13", H("Battleship", "8.13.10.33.33.33.33.33.17.20.19"), H("B-52 Bomber", "24.21.22.22.22.12.10"))));
            roles.AddRange(Ladder(
                14,
                R("freighter-a", 11, AiDesignRoleKind.Freighter, "Pr>1 Co>3", Gated("Privateer", "Co<10", "8.10.15.4.4"), Gated("Meta Morph", "Co>9", metaFreighter)),
                R("freighter-b", 12, AiDesignRoleKind.Freighter, "Pr>1 Co>6", Gated("Privateer", "Co<10", "8.9.11.0.0"), Gated("Meta Morph", "Co>9", metaFreighter)),
                R("freighter-c", 13, AiDesignRoleKind.Freighter, "Pr>1 Co>9", Gated("Privateer", "Co<10", "8.9.11.0.0"), Gated("Meta Morph", "Co>9", metaFreighter))));
            roles.Add(R("line-a", 14, AiDesignRoleKind.LineWarship, "We>4 El>5 Co>5 Pr>5 En>1", H("Nubian", P0Nubian), H("Destroyer", P0DestroyerSlot14)));
            roles.Add(R("line-b", 15, AiDesignRoleKind.LineWarship, "El>9 Co>7 Pr>8 We>13", H("Nubian", P0Nubian), H("Destroyer", P0DestroyerSlot15)));
            return roles.ToArray();
        }

        /// <summary>Personality 1 (Turindrones).</summary>
        private static AiDesignRole[] Personality1()
        {
            string[] battleship = P1P2BattleshipCore.Select(core => "8.12.37" + core).ToArray();
            const string rogue = "8.10.16.27.17.0.13.39.11";
            return new[]
            {
                Unused(R("hunter-a", 0, AiDesignRoleKind.Hunter, string.Empty, H("Frigate", "8.26.0.37"))),
                Unused(R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty, H("Colony Ship", "8.31"))),
                R("miner", 2, AiDesignRoleKind.RemoteMiner, "Co>6 El>3", H("Miner", "8.13.28.28.28.28")),
                R("half-warship", 4, AiDesignRoleKind.HalfStrengthWarship, "We>4 El>5 Co>12 Pr>6", H("Battleship", battleship)),
                R("freighter-a", 8, AiDesignRoleKind.Freighter, "Pr>4 Co>7", H("Rogue", rogue)),
                R("freighter-b", 9, AiDesignRoleKind.Freighter, "Pr>6 Co>10", H("Galleon", "8.37.17.0.13.11.16.27")),
                R("hunter-b", 10, AiDesignRoleKind.Hunter, "We>4 El>4 Co>3 Pr>4", H("Destroyer", "8.0.0.0.9.18.11")),
                R("minelayer", 12, AiDesignRoleKind.Minelayer, "Co>3 Bi>3", H("Privateer", "8.10.12.25.25")),
                R("bomber-a", 13, AiDesignRoleKind.Bomber, "We>7 El>6 Co>5", H("Stealth Bomber", "8.21.22.12.39")),
                R("bomber-b", 14, AiDesignRoleKind.Bomber, "We>10 El>11 Co>14 Pr>8", H("Stealth Bomber", "8.21.22.12.39")),
                R("reserve", 15, AiDesignRoleKind.Reserve, "We>4 El>5 Co>12 Pr>6", H("Rogue", rogue)),
            };
        }

        /// <summary>Personality 2 (Automitrons).</summary>
        private static AiDesignRole[] Personality2()
        {
            string[] battleship = P1P2BattleshipCore.Select(core => "8.12.10" + core).ToArray();
            return new[]
            {
                Unused(R("explorer", 0, AiDesignRoleKind.Explorer, string.Empty, H("Scout", "30.26.4"))),
                Unused(R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty, H("Medium Freighter", "30.31.10"))),
                R("bomber-a", 2, AiDesignRoleKind.Bomber, "We>7 El>6 Co>5 Pr>6", H("B-17 Bomber", "8.21.23.12")),
                R("bomber-b", 3, AiDesignRoleKind.Bomber, "We>10 El>11 Co>14 Pr>8", H("B-52 Bomber", "8.21.23.23.23.12.10")),
                R("freighter-a", 4, AiDesignRoleKind.Freighter, "Pr>4", H("Medium Freighter", "8.16.10")),
                R("freighter-b", 5, AiDesignRoleKind.Freighter, "Pr>6", H("Super Freighter", "8.16.10.19")),
                R("minelayer", 6, AiDesignRoleKind.Minelayer, "Co>3 Pr>4 Bi>5", H("Privateer", "30.10.12.25.32")),
                R("garrison", 9, AiDesignRoleKind.Garrison, "We>4 El>5 Co>12 Pr>6", H("Battleship", battleship)),
                R("reserve", 14, AiDesignRoleKind.Reserve, "We>4 El>5 Co>3 Pr>4", H("Destroyer", "30.0.0.13.9.18.11")),
            };
        }

        /// <summary>Personality 3 (Rototills) builds nothing: its roles hold the starting
        /// designs only.</summary>
        private static AiDesignRole[] Personality3()
        {
            return new[]
            {
                R("explorer", 0, AiDesignRoleKind.Explorer, string.Empty),
                R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty),
            };
        }

        /// <summary>Personality 4 (Cybertrons).</summary>
        private static AiDesignRole[] Personality4()
        {
            AiHullOption[] TopSlot()
            {
                return new[]
                {
                    H("Nubian", "8.18.20.33.33.33.33.33.33.33.33.33.33"),
                    H("Battleship", "8.14.10.33.33.33.33.33.17.20.19"),
                    H("B-52 Bomber", "8.21.22.22.22.12.10"),
                    H("B-52 Bomber", "8.21.23.23.23.12.10"),
                };
            }

            AiHullOption Destroyer(string[] templates)
            {
                AiHullOption option = H("Destroyer", templates);
                option.FixedFirstTemplateBeforeYear = 75;
                return option;
            }

            AiHullOption[] Guard()
            {
                return new[]
                {
                    H("Battleship", P4BattleshipSecond),
                    H("Battleship", P4BattleshipFirst),
                    H("Cruiser", P4CruiserUpper),
                    H("Destroyer", Join(P4DestroyerSlot4, P4DestroyerSlot5)),
                };
            }

            List<AiDesignRole> roles = new List<AiDesignRole>
            {
                After(5, Skill(2, Unused(R("minelayer", 0, AiDesignRoleKind.Minelayer, string.Empty, H("Frigate", MinelayerFrigate))))),
                R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty),
            };
            roles.AddRange(Ladder(
                20,
                After(20, R("hauler-a", 2, AiDesignRoleKind.Hauler, string.Empty, H("Privateer", "44.10.15.4.4"))),
                R("hauler-b", 3, AiDesignRoleKind.Hauler, string.Empty, H("Privateer", "44.17.11.0.0"))));
            roles.AddRange(Ladder(
                20,
                After(30, R("hunter-a", 4, AiDesignRoleKind.Hunter, string.Empty, Destroyer(P4DestroyerSlot4))),
                R("hunter-b", 5, AiDesignRoleKind.Hunter, string.Empty, Destroyer(P4DestroyerSlot5))));

            AiDesignRole[] leaders = Ladder(
                30,
                After(40, R("group1-a", 6, AiDesignRoleKind.BattleGroup, string.Empty, H("Cruiser", P4CruiserLowest))),
                R("group2-a", 10, AiDesignRoleKind.BattleGroup, string.Empty, H("Cruiser", P4CruiserLowest)));
            roles.Add(leaders[0]);
            roles.Add(Follows("group1-a", R("group1-b", 7, AiDesignRoleKind.BattleGroup, string.Empty, H("Cruiser", P4CruiserMiddle))));
            roles.Add(Follows("group1-a", R("group1-c", 8, AiDesignRoleKind.BattleGroup, string.Empty, H("Nubian", P4NubianThird), H("Battleship", P4BattleshipFirst))));
            roles.Add(Follows("group1-a", R("group1-d", 9, AiDesignRoleKind.BattleGroup, string.Empty, TopSlot())));
            roles.Add(leaders[1]);
            roles.Add(Follows("group2-a", R("group2-b", 11, AiDesignRoleKind.BattleGroup, string.Empty, H("Cruiser", P4CruiserMiddle))));
            roles.Add(Follows("group2-a", R("group2-c", 12, AiDesignRoleKind.BattleGroup, string.Empty,
                H("Nubian", P4NubianThird), H("Battleship", P4BattleshipSecond), H("Battleship", P4BattleshipFirst))));
            roles.Add(Follows("group2-a", R("group2-d", 13, AiDesignRoleKind.BattleGroup, string.Empty, TopSlot())));

            roles.AddRange(Ladder(
                20,
                After(30, R("guard-a", 14, AiDesignRoleKind.PlanetaryGuard, string.Empty, Guard())),
                R("guard-b", 15, AiDesignRoleKind.PlanetaryGuard, string.Empty, Guard())));
            return roles.ToArray();
        }

        /// <summary>Personality 5 (Macinti).</summary>
        private static AiDesignRole[] Personality5()
        {
            string[] cruiser = { "8.13.12.3.3.2.10", "8.37.12.1.1.11.9", "8.13.12.4.4.7.10", "8.11.12.0.0.0.17" };
            const string bomberBattleship = "8.14.10.33.33.33.33.33.17.20.19";

            AiHullOption Nubian()
            {
                AiHullOption option = H("Nubian", "8.9.9.3.3.33.33.10.17.10.6.7.20");
                option.ChanceNumerator = 2;
                option.ChanceDenominator = 3;
                return option;
            }

            AiHullOption UltraMiner(string gate)
            {
                AiHullOption option = Gated("Ultra-Miner", gate, "24.41.43.43.43.43");
                option.MinimumSkill = 2;
                return option;
            }

            AiDesignRole colonizer = R("colonizer", 1, AiDesignRoleKind.Colonizer, string.Empty, H("Colony Ship", "8.40"));
            colonizer.Refresh = AiDesignRefresh.GalaxyScoopUpgrade;

            AiDesignRole earlyColonizer = R("colonizer-early", 7, AiDesignRoleKind.Colonizer, string.Empty, H("Colony Ship", "8.40"));
            earlyColonizer.BeforeYear = 40;

            List<AiDesignRole> roles = new List<AiDesignRole>
            {
                Skill(2, Unused(R("minelayer", 0, AiDesignRoleKind.Minelayer, "En>5 Pr>5 Co>5 El>4 Bi>3", H("Frigate", MinelayerFrigate)))),
                colonizer,
                earlyColonizer,
            };
            roles.AddRange(Ladder(
                20,
                R("strike1-a", 2, AiDesignRoleKind.StrikeWarship1, string.Empty, H("Cruiser", cruiser)),
                R("strike1-b", 3, AiDesignRoleKind.StrikeWarship1, string.Empty, H("Cruiser", cruiser)),
                R("strike1-c", 4, AiDesignRoleKind.StrikeWarship1, string.Empty, H("Cruiser", cruiser))));
            roles.AddRange(Ladder(
                20,
                R("strike2-a", 5, AiDesignRoleKind.StrikeWarship2, string.Empty, Nubian(), H("Battleship", P0BattleshipFirst)),
                R("strike2-b", 6, AiDesignRoleKind.StrikeWarship2, string.Empty, Nubian(), H("Battleship", P0BattleshipSecond)),
                R("strike2-c", 7, AiDesignRoleKind.StrikeWarship2, string.Empty, Nubian(), H("Battleship", Join(P0BattleshipFirst, P0BattleshipSecond)))));
            roles.AddRange(Ladder(
                15,
                R("bomber-a", 8, AiDesignRoleKind.Bomber, "We>13", H("Battleship", bomberBattleship), H("B-52 Bomber", "24.21.23.23.23.12.10")),
                R("bomber-b", 9, AiDesignRoleKind.Bomber, "We>13", H("Battleship", bomberBattleship), H("B-52 Bomber", "24.21.22.22.22.12.10"))));
            roles.Add(R("hauler-a", 10, AiDesignRoleKind.Hauler, string.Empty, H("Large Freighter", "44.16.37"), H("Medium Freighter", "44.16.37")));
            roles.Add(R("hauler-b", 11, AiDesignRoleKind.Hauler, string.Empty, H("Large Freighter", "44.16.37")));
            roles.Add(R("line-a", 12, AiDesignRoleKind.LineWarship, "We>4 Pr>5", H("Nubian", P0Nubian), H("Destroyer", P0DestroyerSlot14)));
            roles.Add(R("line-b", 13, AiDesignRoleKind.LineWarship, "We>9 Pr>8", H("Nubian", P0Nubian), H("Destroyer", P0DestroyerSlot15)));
            roles.Add(R("miner-a", 14, AiDesignRoleKind.RemoteMiner, string.Empty,
                UltraMiner(string.Empty), H("Maxi-Miner", "24.41.42.42.42.42"), H("Miner", "24.41.43.43.43.43"), H("Mini-Miner", "24.41.42.42")));
            roles.Add(R("miner-b", 15, AiDesignRoleKind.RemoteMiner, string.Empty, UltraMiner("Co>14"), H("Maxi-Miner", "24.41.42.42.42.42")));
            return roles.ToArray();
        }
    }
}
