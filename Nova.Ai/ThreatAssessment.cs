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

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Ports docs/behavior-specs-3/ai-opponent-behavior.md section 4 ("Threat assessment and
    /// defense/minefield decisions"): a per-target threat rating built from nearby enemy fleet
    /// presence, feeding a defense-need evaluator that recommends whether the AI should build up
    /// defensive minelaying capacity this turn.
    ///
    /// Two of the spec's own formula inputs - the base rating's "+1 bump gated on a racial-trait
    /// check", and the defense-need threshold's "(raceTraitValue * 20) + 10" - reference an
    /// unnamed per-race trait value that this decompile-derived spec never identifies (its own
    /// Open Questions admit several trait/component-category mappings are unrecoverable from the
    /// decompile). The base rating's own +1 bump is still not applied, for that reason. The
    /// defense-need threshold's <c>raceTraitValue</c>, though, is exactly the kind of per-player
    /// tuning constant personality dispatch (spec section 1) is described as varying between
    /// personalities - so `NeedsDefensiveMinelaying` now takes it as a real parameter, driven by
    /// `DefaultAi.AggressivenessScalar` (see that class), rather than only ever using
    /// <see cref="PlaceholderRaceTraitValue"/> as a fixed stand-in; callers with no personality
    /// context (e.g. earlier tests) can still pass the placeholder explicitly.
    ///
    /// The "skipped for passive personalities, or once a global aggressiveness flag is off past
    /// turn 30" gating is handled by the caller (`DefaultAi.HandleDefense` is simply never called
    /// at all for the Disabled/Passive personalities - see DefaultAi.DoMove) rather than inside
    /// this class.
    /// </summary>
    public static class ThreatAssessment
    {
        public const int MinBaseThreatRating = 4;
        public const int MaxBaseThreatRating = 6;

        /// <summary>See this class's own comment for why this is a placeholder rather than a
        /// real per-race trait value.</summary>
        public const int PlaceholderRaceTraitValue = 1;

        /// <summary>Base per-target threat rating (roughly 4-6, per the spec) with jitter.</summary>
        public static int BaseThreatRating(Random random)
        {
            return MinBaseThreatRating + random.Next(MaxBaseThreatRating - MinBaseThreatRating + 1);
        }

        /// <summary>Aggregate threat rating for one owned planet: the base rating plus a
        /// contribution from every enemy fleet reported within <paramref name="scanRadius"/> of
        /// it, larger nearby fleets contributing more - ports the spec's "sweeps its own
        /// multi-ship fleets, builds a scratch list of nearby enemy... strength indicators feeding
        /// the rating".</summary>
        public static int AggregateThreatRating(
            NovaPoint planetPosition,
            IEnumerable<FleetIntel> allFleetReports,
            ushort ownerId,
            double scanRadius,
            Random random)
        {
            int rating = BaseThreatRating(random);
            double radiusSquared = scanRadius * scanRadius;

            foreach (FleetIntel report in allFleetReports)
            {
                if (report.Owner == ownerId || report.Owner == Global.Nobody)
                {
                    continue;
                }

                if (PointUtilities.DistanceSquare(planetPosition, report.Position) > radiusSquared)
                {
                    continue;
                }

                rating += Math.Max(1, report.Count / 5);
            }

            return rating;
        }

        /// <summary>Whether the AI should recommend building defensive minelaying capacity for a
        /// planet with the given aggregate threat rating, per the spec's stated thresholds.
        /// <paramref name="raceTraitValue"/> defaults to <see cref="PlaceholderRaceTraitValue"/>
        /// for callers with no personality context; DefaultAi passes its own
        /// AggressivenessScalar-derived value instead.</summary>
        public static bool NeedsDefensiveMinelaying(int threatRating, int ownedPlanetCount, int existingMinefieldUnits, Random random, int raceTraitValue = PlaceholderRaceTraitValue)
        {
            if (threatRating < 5)
            {
                // Below the spec's stated threat-level-5 cutoff, the normal threshold check is
                // bypassed in favor of a flat coin flip ("flips a 50/50 coin on whether to build
                // anyway").
                return random.Next(2) == 0;
            }

            int threatThreshold = (raceTraitValue * 20) + 10;
            int minefieldCap = (ownedPlanetCount * 4) / 5;

            return threatRating <= threatThreshold && existingMinefieldUnits <= minefieldCap;
        }
    }
}
