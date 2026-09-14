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
    /// </summary>
    public class RemoteMiningStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            foreach (Star star in serverState.AllStars.Values)
            {
                foreach (Fleet fleet in serverState.IterateAllFleets())
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

                    int ironium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Ironium, ref star.MineralMiningProgress.Ironium);
                    int loaded = Math.Min(ironium, freeCapacity);
                    fleet.Cargo.Ironium += loaded;
                    freeCapacity -= loaded;

                    int boranium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Boranium, ref star.MineralMiningProgress.Boranium);
                    loaded = Math.Min(boranium, freeCapacity);
                    fleet.Cargo.Boranium += loaded;
                    freeCapacity -= loaded;

                    int germanium = Star.MineForFleet(mineEquivalents, ref star.MineralConcentration.Germanium, ref star.MineralMiningProgress.Germanium);
                    loaded = Math.Min(germanium, freeCapacity);
                    fleet.Cargo.Germanium += loaded;
                }
            }
        }
    }
}
