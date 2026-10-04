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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using Nova.Common;
    using Nova.Common.Waypoints;

    public class ScrapFleetStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            // Ultimate Recycling's per-planet accumulator exists for one turn generation only:
            // zero-filled when the generation starts (behavior-specs-9/production-queue.md 10g,
            // "Lifetime"), before this pre-movement pass is the only thing that writes it.
            foreach (Star star in serverState.AllStars.Values)
            {
                star.RecycledScrapResources = 0;
            }

            foreach (Fleet fleet in serverState.IterateAllFleets())
            {
                if (fleet.Waypoints.Count > 0)
                {
                    Waypoint waypointZero = fleet.Waypoints[0];
                    ScrapTask scrapTask = waypointZero.Task as ScrapTask;
                    if (scrapTask != null && scrapTask.IsValid(fleet, null, null))
                    {
                        Star targetStar = null;
                        serverState.AllStars.TryGetValue(waypointZero.Destination, out targetStar);

                        // Waypoint 0 normally names the orbited planet (turn-generation-engine.md
                        // §1 step 10). If it does not (a "Space at" label, or a fleet name left by
                        // a follow order), a fleet in orbit is still scrapped at its planet rather
                        // than as deep-space wreckage over it.
                        if (targetStar == null && fleet.InOrbit != null && fleet.InOrbit.Name != null)
                        {
                            serverState.AllStars.TryGetValue(fleet.InOrbit.Name, out targetStar);
                        }

                        if (targetStar != null)
                        {
                            fleet.InOrbit = targetStar;
                        }

                        EmpireData sender = serverState.AllEmpires[fleet.Owner];

                        // The planet owner's race decides Ultimate Recycling and receives the
                        // starbase tech-salvage roll (fleet-movement-scanning-cargo.md §5).
                        EmpireData receiver = null;
                        if (targetStar != null && targetStar.Owner != Global.Nobody)
                        {
                            serverState.AllEmpires.TryGetValue(targetStar.Owner, out receiver);
                        }

                        scrapTask.Perform(fleet, targetStar, sender, receiver);

                        // Message 78 (behavior-specs-11/turn-generation-engine.md §5a): a scrapped
                        // fleet leaves play, so any earlier "completed its orders" notice for it is
                        // withdrawn, with nothing posted.
                        FleetOrdersNotice.Withdraw(serverState.AllMessages, fleet.Key);

                        // Deep space: the salvage is left as a wreckage object, the same decaying
                        // DeepSpaceMinerals concentration the battle pass leaves (reading note 3),
                        // made by the same wreckage routine: at most 30,000 kT per object, the rest
                        // overflowing into further objects at the spot (combat-resolution.md §5/§7).
                        if (scrapTask.Wreckage != null && scrapTask.Wreckage.Mass > 0)
                        {
                            BattleEngine.AddWreckage(serverState, scrapTask.WreckagePosition, scrapTask.Wreckage, forceNew: true);
                        }

                        serverState.AllMessages.AddRange(scrapTask.Messages);
                        scrapTask.Messages.Clear();
                    }
                }
            }

            serverState.CleanupFleets();
        }
    }
}
