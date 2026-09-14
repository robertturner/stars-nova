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
    using System.Linq;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Picks a colonization target for one AI-owned fleet at its current position - ports
    /// docs/behavior-specs-3/ai-opponent-behavior.md section 2 ("Colonization and fleet-
    /// destination selection").
    ///
    /// That spec's own figures are explicitly approximate ("roughly 0-100", "roughly 50, 100,
    /// 150, and 200 light-years") since it was reconstructed from decompiled logic, not exact
    /// source. The additive habitability-plus-declining-distance-bonus formula below reproduces
    /// the *described shape* (closer candidates score higher, habitability dominates, banded at
    /// the spec's own stated squared-distance thresholds) - it is not a bit-verified original
    /// formula, and every specific constant introduced here beyond what the spec states outright
    /// (the distance-bonus magnitudes, the "too eager" score threshold, the exploratory-search
    /// radii) is called out in its own comment as a reasonable reconstruction, not a known fact.
    /// </summary>
    public class ColonizationTargetSelector
    {
        /// <summary>Past this turn, the late-game distance-conservatism rule (relative to the
        /// empire's nearest owned colony) kicks in - the spec's own "roughly 59".</summary>
        private const int LateGameTurn = 59;

        /// <summary>Score at/above which a candidate is flagged "too eager" and gets an
        /// additional independent rejection roll (see Accept) - not given numerically by the
        /// spec itself, just described as applying to "some candidates"; picked so it only
        /// affects genuinely strong candidates, matching the spec's stated purpose of stopping
        /// every AI fleet rushing the single best-looking planet at once.</summary>
        private const double TooEagerScoreThreshold = 80;

        /// <summary>Expanding search radii (light-years) tried in order by the exploratory
        /// fallback until at least one reachable, unowned star is found - not given by the spec
        /// itself beyond "an expanding search radius"; the last entry is unbounded so a fleet is
        /// never left with genuinely nothing to do while any unowned star is known at all.</summary>
        private static readonly double[] ExploratorySearchRadii = { 100, 200, 400, 800, double.MaxValue };

        private readonly ClientData clientState;
        private readonly Random random;
        private readonly List<NovaPoint> ownedColonyPositions;

        public ColonizationTargetSelector(ClientData clientState, Random random)
        {
            this.clientState = clientState;
            this.random = random;
            ownedColonyPositions = clientState.EmpireState.OwnedStars.Values
                .Select(star => star.Position)
                .ToList();
        }

        /// <summary>
        /// Picks a target for the given fleet from the known, unowned, currently-habitable stars,
        /// applying distance-band scoring, probabilistic acceptance, "too eager" extra rejection,
        /// and (past <see cref="LateGameTurn"/>) distance-to-nearest-colony conservatism -
        /// falling back to a reservoir-sampled random target within an expanding search radius if
        /// nothing is accepted, so a fleet doesn't sit idle just because nothing scored well this
        /// turn. Returns null only if literally no unowned star is known at all yet.
        /// </summary>
        public StarIntel SelectTarget(Fleet fleet, int currentTurn)
        {
            var candidates = new List<(StarIntel Star, double Score)>();

            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (report.Owner != Global.Nobody)
                {
                    continue;
                }

                double habitability = clientState.EmpireState.Race.HabitalValue(report) * 100;
                if (habitability <= 0)
                {
                    continue;
                }

                double score = Math.Min(100, habitability + DistanceBonus(PointUtilities.DistanceSquare(fleet.Position, report.Position)));
                candidates.Add((report, score));
            }

            // "Best match": highest score first, ties broken by distance to the fleet - the
            // spec's own two named search primitives ("nearest match"/"best match") combined,
            // since scoring already folds distance in as a tiebreaker once scores tie exactly.
            candidates.Sort((a, b) =>
            {
                int byScore = b.Score.CompareTo(a.Score);
                if (byScore != 0)
                {
                    return byScore;
                }

                return PointUtilities.DistanceSquare(fleet.Position, a.Star.Position)
                    .CompareTo(PointUtilities.DistanceSquare(fleet.Position, b.Star.Position));
            });

            foreach ((StarIntel star, double score) in candidates)
            {
                if (Accept(star, score, currentTurn))
                {
                    return star;
                }
            }

            return SelectExploratoryTarget(fleet);
        }

        /// <summary>Declining bonus the closer a candidate is, banded at the spec's own stated
        /// squared-distance thresholds (50/100/150/200 ly). The specific bonus magnitudes below
        /// aren't given by the spec itself, only that closer scores higher within a combined
        /// "roughly 0-100" total; picked to keep habitability the dominant term rather than
        /// letting a nearby wasteland outscore a good but distant world.</summary>
        private static double DistanceBonus(double distanceSquared)
        {
            if (distanceSquared <= 50.0 * 50.0)
            {
                return 20;
            }

            if (distanceSquared <= 100.0 * 100.0)
            {
                return 15;
            }

            if (distanceSquared <= 150.0 * 150.0)
            {
                return 10;
            }

            if (distanceSquared <= 200.0 * 200.0)
            {
                return 5;
            }

            return 0;
        }

        private bool Accept(StarIntel star, double score, int currentTurn)
        {
            // Probabilistic acceptance: a roll under the candidate's own score succeeds - so a
            // weak candidate is rarely picked and even a strong one isn't a certainty.
            if (random.Next(100) >= score)
            {
                return false;
            }

            // "Too eager": an additional flat 25% rejection chance for candidates that already
            // scored strongly, adding jitter so multiple idle fleets don't all beeline for the
            // same best-looking planet the instant it's discovered.
            if (score >= TooEagerScoreThreshold && random.Next(100) < 25)
            {
                return false;
            }

            if (currentTurn > LateGameTurn && !PassesLateGameDistanceCheck(star))
            {
                return false;
            }

            return true;
        }

        /// <summary>Past <see cref="LateGameTurn"/>, a candidate far from the empire's *nearest
        /// already-owned colony* (not the traveling fleet's own position - a fleet may already be
        /// scouting far afield) is increasingly likely to be passed over, so the AI stops
        /// overreaching into contested/exposed territory as the game goes on - three graduated
        /// squared-distance bands, each its own independent roll (a candidate beyond 300ly must
        /// pass both the 250ly and 300ly checks, so it's rejected more often than one only just
        /// past 250ly - the spec doesn't state whether these bands are cumulative or mutually
        /// exclusive; cumulative was chosen since "certain rejection beyond 350" reads as the
        /// limit these graduated checks are building toward, not a fourth independent tier).</summary>
        private bool PassesLateGameDistanceCheck(StarIntel star)
        {
            if (ownedColonyPositions.Count == 0)
            {
                return true;
            }

            double nearestColonyDistanceSquared = ownedColonyPositions
                .Select(position => PointUtilities.DistanceSquare(position, star.Position))
                .Min();

            if (nearestColonyDistanceSquared > 350.0 * 350.0)
            {
                return false;
            }

            if (nearestColonyDistanceSquared > 300.0 * 300.0 && random.Next(100) < 50)
            {
                return false;
            }

            if (nearestColonyDistanceSquared > 250.0 * 250.0 && random.Next(100) < 50)
            {
                return false;
            }

            return true;
        }

        /// <summary>Reservoir-sampled random target among every known star not already owned by
        /// this empire, searched at an expanding radius from the fleet until at least one
        /// candidate is found - so a fleet with no strong colonization candidate nearby still
        /// gets *something* to do rather than idling indefinitely.</summary>
        private StarIntel SelectExploratoryTarget(Fleet fleet)
        {
            foreach (double radius in ExploratorySearchRadii)
            {
                StarIntel picked = null;
                int seen = 0;

                foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
                {
                    if (report.Owner == clientState.EmpireState.Id)
                    {
                        continue;
                    }

                    if (radius != double.MaxValue && PointUtilities.DistanceSquare(fleet.Position, report.Position) > radius * radius)
                    {
                        continue;
                    }

                    // Reservoir sampling: the Nth candidate seen replaces the current pick with
                    // probability 1/N, leaving every candidate seen so far with equal final odds
                    // without needing to know the total count up front.
                    seen++;
                    if (random.Next(seen) == 0)
                    {
                        picked = report;
                    }
                }

                if (picked != null)
                {
                    return picked;
                }
            }

            return null;
        }
    }
}
