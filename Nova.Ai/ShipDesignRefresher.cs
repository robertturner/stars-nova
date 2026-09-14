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
    using System.Linq;
    using System.Text.RegularExpressions;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// Tracks and refreshes the AI's own working ship designs - ports
    /// docs/behavior-specs-3/ai-opponent-behavior.md section 7 ("Ship auto-design pipeline") to
    /// the extent this codebase's data model and the spec's own admitted unknowns allow.
    ///
    /// **Not implemented, deliberately, and explicitly out of scope for this pass** (rather than
    /// a silent gap): a separate 16-slot vs 10-slot "AI bookkeeping" design table - this codebase
    /// has no such split, `ShipDesign`s all live in one shared per-empire table; an "obsolete"
    /// flag bit - no such field exists on `ShipDesign`, and adding one would change the shared
    /// save-file format every player/UI reads, not just the AI, which is a bigger change than an
    /// AI-focused pass should make unilaterally; and the "two five-category groups, single
    /// most-outdated category past turn 49" rotation rule - the spec's own Open Questions admit
    /// the underlying component categories were never matched to named systems, so which
    /// "categories" this would even mean in this codebase isn't recoverable.
    ///
    /// **What IS implemented**: age-based refresh using the spec's own stated turn thresholds (35
    /// = moderate, 50 = high - collapsed to one "due for refresh" boolean here, since this AI
    /// doesn't yet distinguish urgency levels in how it acts on the recommendation; 26-turn grace
    /// for a starting hull), tracked without any change to the shared `ShipDesign` schema by
    /// encoding the creation turn directly in the design's own `Name` (e.g. "AI Transport T42") -
    /// an AI-internal naming convention, not a save-format change, since `ShipDesign.Name` was
    /// already a free-form string with no schema attached to it.
    /// </summary>
    public static class ShipDesignRefresher
    {
        public const int ModerateAgeThreshold = 35;
        public const int HighAgeThreshold = 50;
        public const int StartingHullGraceTurns = 26;

        private static readonly Regex TurnSuffixPattern = new Regex(@" T(\d+)$");

        /// <summary>
        /// True once a design is old enough to be due for replacement. A design with no turn
        /// suffix (a game's starting hull, or anything not created via
        /// <see cref="NameWithTurnSuffix"/>) is never treated as "due" here - its age can't be
        /// known, and per the spec a starting hull gets its own grace period rather than being
        /// assumed instantly ancient.
        /// </summary>
        public static bool IsDueForRefresh(ShipDesign design, int currentTurn)
        {
            int? creationTurn = GetCreationTurn(design);
            if (creationTurn == null)
            {
                return false;
            }

            int age = currentTurn - creationTurn.Value;
            return age >= ModerateAgeThreshold;
        }

        /// <summary>Extracts the creation turn encoded in a design's name, or null if this
        /// design wasn't created via <see cref="NameWithTurnSuffix"/> (e.g. a game's starting
        /// hull, which has no such suffix).</summary>
        public static int? GetCreationTurn(ShipDesign design)
        {
            Match match = TurnSuffixPattern.Match(design.Name);
            return match.Success ? int.Parse(match.Groups[1].Value) : (int?)null;
        }

        public static string NameWithTurnSuffix(string baseName, int currentTurn)
        {
            return baseName + " T" + currentTurn;
        }

        /// <summary>The highest-tech-level component in <paramref name="availableComponents"/>
        /// carrying an "Engine" property (the same `Properties.ContainsKey("Engine")` check
        /// `ShipDesign.cs`/`ComponentEditor.cs` already use elsewhere in this codebase to
        /// identify an engine component) - "tech level" ranked by the simple sum of its
        /// `RequiredTech` across all six fields, a reasonable proxy for "more advanced" absent
        /// any more specific ranking in the source data.</summary>
        public static Component BestAvailableEngine(ClientData clientState)
        {
            return clientState.EmpireState.AvailableComponents.Values
                .Where(component => component.Properties.ContainsKey("Engine"))
                .OrderByDescending(component => TechLevelSum(component.RequiredTech))
                .FirstOrDefault();
        }

        private static int TechLevelSum(TechLevel level)
        {
            int sum = 0;
            foreach (TechLevel.ResearchField field in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                sum += level[field];
            }

            return sum;
        }
    }
}
