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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server.TurnSteps
{
    using System;

    using Nova.Common;

    /// <summary>
    /// Applies each orbiting remote-mining fleet's contribution to its star's mineral
    /// concentration - docs/behavior-specs-4/population-growth.md's Example 5 ("five separate
    /// remote-mining fleets... dropping a planet from concentration 100 to 34 in a single turn").
    ///
    /// Unlike the planet's own mines (StarUpdateStep.UpdateMinerals, only ever run for owned,
    /// colonized stars), remote mining applies at ANY star a qualifying fleet is orbiting -
    /// including unowned, uninhabited ones, which is the whole point of a remote-mining fleet -
    /// so this runs as its own turn step over every star rather than folding into that gated loop.
    /// Mined minerals go into the FLEET's own cargo hold, not a planet stockpile, since an unowned
    /// planet has none to add to anyway; a fleet with no free cargo capacity left doesn't mine at
    /// all this turn (nothing gained from depleting concentration it can't carry away), and a
    /// fleet with SOME room loads minerals Ironium/Boranium/Germanium in that order until full -
    /// whatever's mined beyond that is lost, the same way a real ship simply can't hold more.
    ///
    /// When two different empires each have a qualifying fleet at the SAME star, whichever is
    /// considered first gets the (higher, undepleted) concentration and the other mines whatever's
    /// left - a genuinely contested, order-sensitive outcome. Iterating in this turn's shuffled
    /// empire order (see ServerData.ShuffledEmpireOrder) rather than fixed dictionary order means
    /// that isn't systematically biased toward the same empire every game.
    /// </summary>
    public class RemoteMiningStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            foreach (Star star in serverState.AllStars.Values)
            {
                foreach (Fleet fleet in serverState.IterateAllFleetsInShuffledOrder())
                {
                    if (fleet.InOrbit == null || fleet.InOrbit.Name != star.Name)
                    {
                        continue;
                    }

                    int mineEquivalents = fleet.MineEquivalents;
                    if (mineEquivalents <= 0)
                    {
                        continue;
                    }

                    int freeCapacity = fleet.TotalCargoCapacity - fleet.Cargo.Mass;
                    if (freeCapacity <= 0)
                    {
                        continue;
                    }

                    serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData miningEmpire);
                    Race miningRace = miningEmpire?.Race;

                    // Remote-mining eligibility (behavior-specs-11/population-growth.md section 5,
                    // "Which planets carry the home-world bit, and who gets the floor"): the task
                    // pass refuses remote mining of any inhabited planet by a non-AR fleet (message
                    // 118, order cancelled) and does nothing for an AR fleet at a planet it does
                    // not own. This port has no Remote-Mining order object to cancel, so an
                    // ineligible fleet simply does not mine.
                    bool miningIsAlternateReality = miningRace != null && miningRace.HasTrait("AR");
                    if (miningIsAlternateReality)
                    {
                        if (star.Owner != fleet.Owner)
                        {
                            continue; // AR mines only planets it owns
                        }
                    }
                    else if (star.Owner != Global.Nobody)
                    {
                        continue; // non-AR remote mining is refused at any inhabited planet
                    }

                    // The depletion-threshold side of mining scales with the MINING race's own
                    // efficiency (Star.KtToDropOnePoint), not the star owner's (this fleet may be
                    // mining an unowned or foreign star) - see Star.MineForFleet's own comment.
                    // An unset/zero MineProductionRate (e.g. a Race that was never loaded from a
                    // real race file) falls back to the baseline 10, same as Star.Mine - a genuine
                    // 0 would make KtToDropOnePoint return 0, collapsing concentration to 1 in a
                    // single application.
                    int mineProductionRate = miningRace != null && miningRace.MineProductionRate > 0 ? miningRace.MineProductionRate : 10;
                    int yieldFloor = star.RemoteMiningYieldConcentrationFloor();

                    int ironium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Ironium, ref star.MineralMiningProgress.Ironium, mineProductionRate, yieldFloor);
                    int loaded = Math.Min(ironium, freeCapacity);
                    fleet.Cargo.Ironium += loaded;
                    freeCapacity -= loaded;

                    int boranium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Boranium, ref star.MineralMiningProgress.Boranium, mineProductionRate, yieldFloor);
                    loaded = Math.Min(boranium, freeCapacity);
                    fleet.Cargo.Boranium += loaded;
                    freeCapacity -= loaded;

                    int germanium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Germanium, ref star.MineralMiningProgress.Germanium, mineProductionRate, yieldFloor);
                    loaded = Math.Min(germanium, freeCapacity);
                    fleet.Cargo.Germanium += loaded;
                }
            }
        }
    }
}
