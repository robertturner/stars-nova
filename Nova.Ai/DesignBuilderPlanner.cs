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
    using System.Text.RegularExpressions;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The role tag of an AI-built design, kept in the design's name so no save-format change is
    /// needed: "&lt;hull&gt; [&lt;tag&gt;] T&lt;year&gt;". The trailing " T&lt;year&gt;" is the
    /// creation-year stamp (§15: design +0x7d) in the form <see cref="ShipDesignRefresher"/>
    /// already reads.
    /// </summary>
    public static class AiDesignRoleTag
    {
        private static readonly Regex TagPattern = new Regex(@" \[([A-Za-z0-9-]+)\] T\d+$");

        public static string Name(string baseName, string tag, int year)
        {
            return ShipDesignRefresher.NameWithTurnSuffix(baseName + " [" + tag + "]", year);
        }

        /// <summary>The role tag in a design's name, or null for a design the AI did not
        /// build.</summary>
        public static string TagOf(ShipDesign design)
        {
            if (design == null || design.Name == null)
            {
                return null;
            }

            Match match = TagPattern.Match(design.Name);
            return match.Success ? match.Groups[1].Value : null;
        }
    }

    /// <summary>A design the planner wants added, and the role it fills.</summary>
    public sealed class PlannedDesign
    {
        public AiDesignRole Role;
        public string Hull;
        public ShipDesign Design;
    }

    /// <summary>Everything the planner reads, so it can be driven without a live client.</summary>
    public sealed class AiDesignPlanInput
    {
        public int Category;
        public int Skill = AiCategory.StandardSkill;
        public int TurnYear;
        public int StartingYear = Global.StartingYear;
        public TechLevel Levels;
        public IDictionary<string, Component> Available;

        /// <summary>For cost modifiers on the built design; may be null.</summary>
        public Race Race;

        public IEnumerable<ShipDesign> Designs;

        /// <summary>The number of ships of a design in existence (design +0x83).</summary>
        public Func<ShipDesign, int> ShipsInExistence;

        public Func<long> NextDesignKey;

        /// <summary>Reads an empire's own state (designs, levels, components, fleets).</summary>
        public static AiDesignPlanInput From(EmpireData empire, int category, int skill)
        {
            return new AiDesignPlanInput
            {
                Category = category,
                Skill = skill,
                TurnYear = empire.TurnYear,
                Levels = empire.ResearchLevels,
                Available = empire.AvailableComponents,
                Race = empire.Race,
                Designs = empire.Designs.Values.ToList(),
                ShipsInExistence = design => empire.OwnedFleets.Values
                    .Where(fleet => fleet.Composition != null)
                    .SelectMany(fleet => fleet.Composition.Values)
                    .Where(token => token.Design != null && token.Design.Key == design.Key)
                    .Sum(token => token.Quantity),
                NextDesignKey = empire.GetNextDesignKey,
            };
        }
    }

    /// <summary>
    /// The per-personality design refresh, slot-free (behavior-specs-10/ai-opponent-behavior.md
    /// §15 templates, §17 role tags). Each turn it walks the personality's role entries in
    /// order; for a role whose tech gate, skill and year conditions hold and whose refresh rule
    /// asks for a design, it builds one with <see cref="DesignBuilder"/> from the first hull
    /// option that succeeds. As §17 recommends, a role holds one current design (its newest
    /// tagged design, or the starting design mapped onto it); older designs keep their ships but
    /// are no longer the role's design. A build identical to the role's current design is not
    /// added again. Pure apart from the Random it is given.
    /// </summary>
    public sealed class AiDesignPlanner
    {
        /// <summary>An AI-built design of a once-only role is rebuilt at this age (§7's "high
        /// urgency" 50-turn replacement threshold).</summary>
        public const int StaleRebuildYears = 50;

        private readonly Random random;

        public AiDesignPlanner(Random random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public List<PlannedDesign> Plan(AiDesignPlanInput input)
        {
            List<PlannedDesign> planned = new List<PlannedDesign>();
            IReadOnlyList<AiDesignRole> roles = AiDesignRoleTable.ForCategory(input.Category);
            if (roles.Count == 0)
            {
                return planned;
            }

            int years = input.TurnYear - input.StartingYear;
            List<ShipDesign> designs = input.Designs.ToList();
            Dictionary<string, ShipDesign> current = CurrentDesigns(input.Category, designs);
            HashSet<string> names = new HashSet<string>(designs.Where(d => d.Name != null).Select(d => d.Name));
            DesignBuilder builder = new DesignBuilder(input.Available, random);

            foreach (AiDesignRole role in roles)
            {
                if (role.Hulls.Length == 0 || !Eligible(role, input, years))
                {
                    continue;
                }

                current.TryGetValue(role.Tag, out ShipDesign existing);
                if (!WantsBuild(role, existing, current, input, builder))
                {
                    continue;
                }

                PlannedDesign build = BuildFor(role, input, years, builder);
                if (build == null || (existing != null && DesignBuilder.SameBuild(existing, build.Design)))
                {
                    continue;
                }

                string name = AiDesignRoleTag.Name(build.Hull, role.Tag, input.TurnYear);
                for (int attempt = 2; names.Contains(name); attempt++)
                {
                    name = AiDesignRoleTag.Name(build.Hull + " " + attempt.ToString(CultureInfo.InvariantCulture), role.Tag, input.TurnYear);
                }

                build.Design.Name = name;
                build.Design.Key = input.NextDesignKey();
                if (input.Race != null)
                {
                    build.Design.Update(input.Race, input.Levels);
                }

                names.Add(name);
                current[role.Tag] = build.Design;
                planned.Add(build);
            }

            return planned;
        }

        /// <summary>The year a design was made: its " T&lt;year&gt;" stamp, or the game's start
        /// for a starting design.</summary>
        public static int CreationYear(ShipDesign design, int startingYear)
        {
            return ShipDesignRefresher.GetCreationTurn(design) ?? startingYear;
        }

        /// <summary>Each role's current design: its newest tagged design, else the starting
        /// design §17 maps onto it.</summary>
        public static Dictionary<string, ShipDesign> CurrentDesigns(int category, IEnumerable<ShipDesign> designs)
        {
            Dictionary<string, ShipDesign> current = new Dictionary<string, ShipDesign>();
            List<ShipDesign> untagged = new List<ShipDesign>();
            foreach (ShipDesign design in designs.OrderBy(d => CreationYear(d, 0)).ThenBy(d => d.Key))
            {
                string tag = AiDesignRoleTag.TagOf(design);
                if (tag == null)
                {
                    untagged.Add(design);
                }
                else
                {
                    current[tag] = design;
                }
            }

            foreach (KeyValuePair<AiStartingRole, ShipDesign> starting in AiStartingDesigns.AssignRoles(untagged))
            {
                string tag = AiStartingDesigns.RoleTagFor(category, starting.Key);
                if (tag != null && !current.ContainsKey(tag))
                {
                    current[tag] = starting.Value;
                }
            }

            return current;
        }

        private static bool Eligible(AiDesignRole role, AiDesignPlanInput input, int years)
        {
            return input.Skill >= role.MinimumSkill
                && years > role.AfterYear
                && years < role.BeforeYear
                && role.Gate.Passes(input.Levels);
        }

        private static bool WantsBuild(AiDesignRole role, ShipDesign existing, Dictionary<string, ShipDesign> current, AiDesignPlanInput input, DesignBuilder builder)
        {
            int Age(ShipDesign design) => input.TurnYear - CreationYear(design, input.StartingYear);
            int Created(ShipDesign design) => CreationYear(design, input.StartingYear);
            ShipDesign Find(string tag) => tag != null && current.TryGetValue(tag, out ShipDesign found) ? found : null;

            switch (role.Refresh)
            {
                case AiDesignRefresh.WhenUnused:
                    return existing == null || input.ShipsInExistence(existing) == 0;

                case AiDesignRefresh.Ladder:
                    if (role.PreviousTag == null)
                    {
                        if (existing == null)
                        {
                            return true;
                        }

                        ShipDesign top = Find(role.LadderTopTag);
                        return top != null && top != existing && Created(top) >= Created(existing) && Age(top) > role.LadderYears;
                    }
                    else
                    {
                        ShipDesign below = Find(role.PreviousTag);
                        if (below == null || Age(below) <= role.LadderYears)
                        {
                            return false;
                        }

                        return existing == null || Created(existing) < Created(below);
                    }

                case AiDesignRefresh.FollowsLeader:
                    ShipDesign leader = Find(role.PreviousTag);
                    return leader != null && (existing == null || Created(existing) < Created(leader));

                case AiDesignRefresh.GalaxyScoopUpgrade:
                    if (existing == null)
                    {
                        return true;
                    }

                    return builder.FindAvailable("Galaxy Scoop") != null
                        && input.ShipsInExistence(existing) == 0
                        && !HasPart(existing, "Galaxy Scoop");

                default:
                    return existing == null
                        || (AiDesignRoleTag.TagOf(existing) != null && Age(existing) >= StaleRebuildYears);
            }
        }

        private PlannedDesign BuildFor(AiDesignRole role, AiDesignPlanInput input, int years, DesignBuilder builder)
        {
            foreach (AiHullOption option in role.Hulls)
            {
                if (input.Skill < option.MinimumSkill || !option.Gate.Passes(input.Levels) || !builder.CanBuildHull(option.Hull))
                {
                    continue;
                }

                if (option.ChanceDenominator > 0 && random.Next(option.ChanceDenominator) >= option.ChanceNumerator)
                {
                    continue;
                }

                IReadOnlyList<int[]> templates = option.Templates;
                if (option.FixedFirstTemplateBeforeYear > 0)
                {
                    templates = years < option.FixedFirstTemplateBeforeYear
                        ? option.Templates.Take(1).ToList()
                        : option.Templates.Skip(1).ToList();
                }

                ShipDesign design = builder.BuildAny(option.Hull, templates, 0, option.Hull);
                if (design != null)
                {
                    return new PlannedDesign { Role = role, Hull = option.Hull, Design = design };
                }
            }

            return null;
        }

        private static bool HasPart(ShipDesign design, string partName)
        {
            return design.Blueprint != null
                && design.Blueprint.Properties.ContainsKey("Hull")
                && design.Hull.Modules.Any(module => module.AllocatedComponent != null && module.AllocatedComponent.Name == partName);
        }
    }
}
