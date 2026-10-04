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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The yearly wormhole step (behavior-specs-11/fleet-movement-scanning-cargo.md "Wormhole
    /// lifecycle, complete rule" item 4, turn-generation step 21 mode 1): every end separately, in
    /// table order. A 0-99 roll below the end's stability tier makes it jump - the located mask is
    /// cleared for every race, the age resets to 0 (the base is kept) and the end is placed
    /// anywhere in the galaxy by the 100-draw rule. Otherwise the age goes up by 1 and the end
    /// drifts up to 12 ly per axis by the same rule. The traversed mask and the pairing are never
    /// cleared, and no wormhole ever disappears.
    /// </summary>
    public class WormholeDriftStep : ITurnStep
    {
        // The injected test random, or null: each Process then takes the game's seeded
        // "WormholeDrift" stream (ServerData.CreateRandom), so the drift is repeatable.
        private readonly Random injectedRandom;

        public WormholeDriftStep() : this(null)
        {
        }

        /// <summary>Overload for deterministic testing.</summary>
        public WormholeDriftStep(Random random)
        {
            injectedRandom = random;
        }

        public void Process(ServerData serverState)
        {
            Random random = injectedRandom ?? serverState.CreateRandom("WormholeDrift");

            int mapWidth = GameSettings.Data.MapWidth;
            int mapHeight = GameSettings.Data.MapHeight;
            List<Star> planets = serverState.AllStars.Values.ToList();
            List<Wormhole> ends = serverState.AllWormholes.Values.ToList();
            List<Fleet> fleets = serverState.IterateAllFleets().ToList();

            foreach (Wormhole wormhole in serverState.AllWormholes.Values)
            {
                if (random.Next(100) < wormhole.StabilityTier)
                {
                    // Jump: clears every race's located bit, resets the age (base kept) and
                    // relocates the end anywhere in the galaxy.
                    wormhole.Located.Clear();
                    wormhole.Age = 0;
                    NovaPoint jump = WormholePlacement.DrawAnywhere(
                        random, mapWidth, mapHeight, planets, ends, fleets, wormhole.PairedKey, wormhole.Key);
                    if (jump != null)
                    {
                        wormhole.Position = jump;
                    }

                    continue;
                }

                wormhole.Age = (wormhole.Age + 1) % 1024;
                NovaPoint drift = WormholePlacement.DrawDrift(
                    wormhole.Position, random, mapWidth, mapHeight, planets, ends, fleets, wormhole.PairedKey, wormhole.Key);
                if (drift != null)
                {
                    wormhole.Position = drift;
                }
            }
        }
    }
}
