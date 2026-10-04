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

    /// <summary>
    /// Inner Strength colonist growth aboard fleets: step 19 of behavior-specs-9/
    /// turn-generation-engine.md section 1 (after movement and minefield decay, before the
    /// production hub), rules in section 11 "Inner Strength colonist growth".
    /// </summary>
    /// <remarks>
    /// For every fleet of an Inner Strength race with colonists aboard (C units, 1 unit = 1 kT =
    /// 100 colonists): growth = floor(R x C / 200) units, R being the race's growth-rate setting
    /// (1-20; no habitability term, nothing else modifies it). When that is 0, one draw of 0-2 is
    /// made and only a 0 (1 chance in 3) gives 1 unit. The growth is added up to the fleet's free
    /// cargo space (message 251 reports what fitted); any excess lands on the planet the fleet
    /// orbits only when that planet is owned by the same race (message 344), and is otherwise lost.
    /// There is no random draw on the non-zero branch and no dependence on whether the fleet moved.
    /// </remarks>
    public class ColonistBreedingStep : ITurnStep
    {
        // The injected test random, or null: each Process then takes the game's seeded
        // "ColonistBreeding" stream (ServerData.CreateRandom), so breeding is repeatable.
        private readonly Random injectedRandom;
        private Random random;

        public ColonistBreedingStep() : this(null)
        {
        }

        /// <summary>Injectable random source, for deterministic tests of the 1-in-3 zero case.</summary>
        public ColonistBreedingStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }

        public void Process(ServerData serverState)
        {
            random = injectedRandom ?? serverState.CreateRandom("ColonistBreeding");

            // Materialise first: nothing here adds or removes fleets, but the planet updates should
            // not depend on enumeration order of a live collection.
            List<Fleet> fleets = serverState.IterateAllFleets().ToList();

            foreach (Fleet fleet in fleets)
            {
                if (fleet == null || fleet.IsStarbase || fleet.Cargo.ColonistsInKilotons <= 0)
                {
                    continue;
                }

                if (!serverState.AllEmpires.TryGetValue(fleet.Owner, out EmpireData empire)
                    || empire.Race == null || !empire.Race.HasTrait("IS"))
                {
                    continue;
                }

                Breed(serverState, fleet, empire.Race);
            }
        }

        private void Breed(ServerData serverState, Fleet fleet, Race race)
        {
            int colonistUnits = fleet.Cargo.ColonistsInKilotons;
            int growthRate = (int)race.GrowthRate;

            int growth = growthRate * colonistUnits / 200;
            if (growth == 0)
            {
                // 1 chance in 3 of a single unit; 2 in 3 of nothing.
                if (random.Next(3) != 0)
                {
                    return;
                }
                growth = 1;
            }

            int freeSpace = Math.Max(0, fleet.TotalCargoCapacity - fleet.Cargo.Mass);
            int fitted = Math.Min(growth, freeSpace);

            if (fitted > 0)
            {
                fleet.Cargo.ColonistsInKilotons = colonistUnits + fitted;

                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = (fitted * Global.ColonistsPerKiloton) + " colonists were born aboard " + fleet.Name + " this year.";
                message.Type = "Inner Strength";
                serverState.AllMessages.Add(message);
            }

            int overflow = growth - fitted;
            if (overflow <= 0 || fleet.InOrbit == null)
            {
                return;
            }

            if (!serverState.AllStars.TryGetValue(fleet.InOrbit.Name, out Star planet) || planet.Owner != fleet.Owner)
            {
                // Orbiting someone else's planet (or an unowned one): the excess is lost.
                return;
            }

            planet.Colonists += overflow * Global.ColonistsPerKiloton;

            Message overflowMessage = new Message();
            overflowMessage.Audience = fleet.Owner;
            overflowMessage.Text = "Breeding aboard " + fleet.Name + " has outgrown its living space; "
                + (overflow * Global.ColonistsPerKiloton) + " colonists were beamed down to " + planet.Name + ".";
            overflowMessage.Type = "Inner Strength";
            serverState.AllMessages.Add(overflowMessage);
        }
    }
}
