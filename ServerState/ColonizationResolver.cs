#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
//
// This file is part of Stars! Nova.
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// Resolves every star's pending colonization landings for the current turn - called once
    /// every fleet has had a chance to move and arrive, so every simultaneous contender for a
    /// given star is known before any of them are decided. ColoniseTask.Perform has already
    /// dismantled each colonizing fleet into the planet (minerals credited at once, losers
    /// included) and recorded its colonists here (Common can't reference this project, so the
    /// resolver lives here instead of alongside that task).
    ///
    /// behavior-specs-9/turn-generation-engine.md §11 ("Colonisation and invasion") and
    /// fleet-movement-scanning-cargo.md §5 ("Colonize resolution, fleet side", step 4):
    /// - an uncontested landing makes the race the owner with exactly the colonists carried
    ///   (no scaling, minimum 1 unit);
    /// - when two or more races land in the same pass, each race's strength is its colonists
    ///   x 110% (165% for War Monger, 0 for Alternate Reality); an exact tie for first place
    ///   destroys everyone; otherwise the winner keeps colonists x (largest - runner-up) /
    ///   largest, truncated, at least 1 unit, where the runner-up is the strongest contender
    ///   with a LOWER race index than the winner (see <see cref="ResolveContest"/>).
    /// </summary>
    public static class ColonizationResolver
    {
        /// <summary>One race's combined landing at a planet (pending-landing ledger record).</summary>
        private sealed class Landing
        {
            public EmpireData Sender;
            public List<Fleet> Fleets = new List<Fleet>();
            public int ColonistUnits;
            public long Strength;
        }

        public static void ResolvePendingColonizations(ServerData serverState)
        {
            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.PendingColonizations.Count == 0)
                {
                    continue;
                }

                // Records are (planet, race, colonists); races are scanned in race-index order.
                List<Landing> landings = star.PendingColonizations
                    .GroupBy(attempt => attempt.Sender.Id)
                    .OrderBy(group => group.Key)
                    .Select(group => new Landing
                    {
                        Sender = group.First().Sender,
                        Fleets = group.Select(attempt => attempt.Fleet).ToList(),
                        ColonistUnits = group.Sum(attempt => attempt.ColonistUnits),
                    })
                    .ToList();

                star.PendingColonizations.Clear();

                if (star.Owner != Global.Nobody)
                {
                    // Defensive: ColoniseTask.IsValid cancels any order on an owned planet, so
                    // no colonize record for an owned planet should reach here.
                    foreach (Landing landing in landings)
                    {
                        Notify(serverState, landing, "The colonists from " + FleetNames(landing) + " were lost: " + star.Name + " is already inhabited.");
                    }

                    continue;
                }

                if (landings.Count == 1)
                {
                    // Uncontested: the colonists carried, no scaling, minimum 1 unit.
                    Landing only = landings[0];
                    ApplyColonization(serverState, only, star, Math.Max(1, only.ColonistUnits));
                    continue;
                }

                ResolveContest(serverState, star, landings);
            }
        }

        /// <summary>
        /// Strength of one race's landing, in hundredths so 110%/165% stay exact integers:
        /// colonists x 110% (165% for War Monger, 0 for Alternate Reality) -
        /// turn-generation-engine.md §11.
        /// </summary>
        private static long Strength(Landing landing)
        {
            Race race = landing.Sender.Race;
            if (race != null && race.HasTrait("AR"))
            {
                return 0;
            }

            int percent = (race != null && race.HasTrait("WM")) ? 165 : 110;
            return (long)landing.ColonistUnits * percent;
        }

        /// <summary>
        /// The contest of turn-generation-engine.md §11, as the original codes it
        /// (FUN_10b8_1ea6, :77815-77843, :77967-77971): races are scanned in index order keeping
        /// a running maximum. SPEC QUIRK, implemented as written: the "runner-up" is the maximum
        /// the winner DISPLACED, so it is the strongest contender with a LOWER race index than
        /// the winner, not the true second-largest force - a weaker contender with a higher
        /// index never becomes the runner-up, and when every weaker contender has a higher index
        /// the runner-up is 0 and the winner's colonists are not scaled at all. An exact tie for
        /// first place (or no positive strength at all) destroys everyone (messages 5, 6).
        /// </summary>
        private static void ResolveContest(ServerData serverState, Star star, List<Landing> landings)
        {
            Landing winner = null;
            long largest = 0;
            long runnerUp = 0;
            bool tie = false;

            foreach (Landing landing in landings)
            {
                landing.Strength = Strength(landing);
                if (landing.Strength > largest)
                {
                    runnerUp = largest;
                    largest = landing.Strength;
                    winner = landing;
                    tie = false;
                }
                else if (landing.Strength == largest && largest > 0)
                {
                    tie = true;
                }
            }

            if (winner == null || tie)
            {
                foreach (Landing landing in landings)
                {
                    bool tiedForFirst = winner != null && landing.Strength == largest;
                    Notify(serverState, landing, tiedForFirst
                        ? FleetNames(landing) + "'s attempt to colonise " + star.Name + " ended in an exact tie with another race's fleet - all the colonists were destroyed."
                        : FleetNames(landing) + " lost the race to colonise " + star.Name + "; its colonists were destroyed.");
                }

                return;
            }

            int survivors = (int)((long)winner.ColonistUnits * (largest - runnerUp) / largest);
            ApplyColonization(serverState, winner, star, Math.Max(1, survivors));

            foreach (Landing landing in landings)
            {
                if (landing != winner)
                {
                    Notify(serverState, landing, FleetNames(landing) + " lost the race to colonise " + star.Name + " to another race's fleet; its colonists were destroyed.");
                }
            }
        }

        /// <summary>
        /// Installs the winning race on the planet with <paramref name="colonistUnits"/> units
        /// (100 colonists each). The fleet was already dismantled by ColoniseTask.Perform, which
        /// ADDED its minerals to the planet's stockpile - nothing here touches the stockpile.
        /// </summary>
        private static void ApplyColonization(ServerData serverState, Landing winner, Star star, int colonistUnits)
        {
            EmpireData sender = winner.Sender;

            star.Colonists = colonistUnits * Global.ColonistsPerKiloton;
            star.Owner = sender.Id;
            star.ThisRace = sender.Race;
            star.EnergyTechLevel = sender.ResearchLevels[TechLevel.ResearchField.Energy];

            sender.OwnedStars.Add(star);

            // An Alternate Reality winner also gets its starbase design slot 0 ("Starter Colony",
            // Orbital Fort hull) installed - its population capacity comes entirely from the
            // starbase orbiting the planet and is 0 without one (turn-generation-engine.md section
            // 11; population-growth.md section 3).
            if (sender.Race != null && sender.Race.HasTrait("AR"))
            {
                StarterColony.Install(star, sender);
            }

            // The new owner's default production template becomes the planet's queue
            // (production-queue.md 10f).
            ProductionTemplateSet.ApplyDefault(star, sender);

            Notify(serverState, winner, "You have colonised " + star.Name + ".");

            // Every star should already have a StarReports placeholder for every empire from
            // AssembleEmpireData at game creation (FirstStep.cs) - but colonizing one this empire
            // never had a report for at all (rather than merely an unscanned ScanLevel.None one)
            // shouldn't crash the whole turn generation over it. Matches the same
            // ContainsKey-or-Add pattern ScanStep.AddStars already uses for the equivalent case.
            if (sender.StarReports.ContainsKey(star.Name))
            {
                sender.StarReports[star.Name].Update(star, ScanLevel.Owned, sender.TurnYear);
            }
            else
            {
                sender.StarReports.Add(star.Name, star.GenerateReport(ScanLevel.Owned, sender.TurnYear));
            }
        }

        private static string FleetNames(Landing landing)
        {
            return string.Join(", ", landing.Fleets.Select(fleet => fleet.Name));
        }

        private static void Notify(ServerData serverState, Landing landing, string text)
        {
            serverState.AllMessages.Add(new Message { Audience = landing.Sender.Id, Text = text });
        }
    }
}
