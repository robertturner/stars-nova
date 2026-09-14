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
    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Ports the two concrete, unambiguous gates from docs/behavior-specs-4/ai-opponent-behavior.md
    /// section 9 ("Colonizer commitment and design tech-upgrade budgeting") - a decision distinct
    /// from ColonizationTargetSelector's target-picking (section 2): given a fleet and a star
    /// ColonizationTargetSelector already picked, whether the fleet is actually capable of
    /// founding a viable colony there, and whether the attempt is close enough to whatever is
    /// funding it to be worth committing to.
    ///
    /// Deliberately NOT ported here (both left for a future pass, should the spec's own
    /// uncertainty ever resolve):
    /// - The tech-field-sum-vs-59/71/84/95/108-ladder "design upgrade ambition" computation and
    ///   its research-cost budget (capped at 5,000, or 3,500 "for one specific design category"
    ///   that the spec itself couldn't identify). The spec gives no formula for how cargo-capacity
    ///   surplus modulates the ladder result, and - more importantly - no clear mechanism this
    ///   would actually wire into (there's no existing "request a research retarget for this
    ///   design" action anywhere in this codebase to hand the result to), so implementing it now
    ///   would mean inventing both a modulation formula AND its game effect from nothing.
    /// - The persistent per-decision "audit trail" bitmask ("used to avoid re-deciding the same
    ///   case identically every turn"). Nova's AI runs as a fresh, stateless process each turn
    ///   (see DefaultAIPlanner's own remark) - there's nowhere to persist such a bitmask across
    ///   turns without a larger architectural change. Recomputing this decision fresh every turn
    ///   is functionally equivalent (same inputs, same outputs) - it just forgoes the original's
    ///   log/audit bookkeeping, not any actual decision-making capability.
    /// </summary>
    public static class ColonizerCommitmentAdvisor
    {
        /// <summary>A fleet's cargo CAPACITY (not however much is currently loaded - the decision
        /// this ports happens before Colonise() has loaded anything) below this threshold means
        /// the fleet can never carry a viable colony regardless of what's loaded, so it isn't
        /// worth committing at all.</summary>
        public const int CargoCapacityThreshold = 5000;

        /// <summary>~100 units, as the spec states directly ("roughly 100 units (squared-distance
        /// 10,000)").</summary>
        public const double FundingSourceMaxDistanceSquared = 10000;

        /// <summary>Is this fleet's cargo hold even big enough to found a viable colony?</summary>
        public static bool IsColonizerCapable(Fleet fleet)
        {
            return fleet.TotalCargoCapacity >= CargoCapacityThreshold;
        }

        /// <summary>Is the candidate star close enough to whatever is funding this colonization
        /// attempt to be worth committing to? The spec doesn't pin down exactly what "the
        /// resource/production source funding the decision" refers to beyond that phrase - the
        /// fleet's own current position (where it's being dispatched from, and so implicitly
        /// where its cargo/upgrades were funded from) is the most direct, available stand-in.
        /// </summary>
        public static bool IsWithinFundingRange(NovaPoint candidateStarPosition, NovaPoint fundingSourcePosition)
        {
            return PointUtilities.DistanceSquare(candidateStarPosition, fundingSourcePosition) <= FundingSourceMaxDistanceSquared;
        }
    }
}
