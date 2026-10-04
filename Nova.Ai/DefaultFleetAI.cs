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
    using System.Text;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    /// <summary>
    /// A sub AI to manage a <see cref="Fleet"/>.
    /// </summary>
    public class DefaultFleetAI
    {
        /// <summary>
        /// The <see cref="Fleet"/> managed by this AI.
        /// </summary>
        private Fleet fleet;

        private ClientData clientState;

        public FleetList FuelStations = null;

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        /// <param name="newFleet">The Fleet to be managed.</param>
        /// <param name="newState">The AbstractAI.clientState.</param>
        public DefaultFleetAI(Fleet newFleet, ClientData newState, FleetList newFuelStations)
        {
            fleet = newFleet;
            clientState = newState;
            FuelStations = newFuelStations;
        }

        public StarIntel Scout(List<StarIntel> excludedStars)
        {
            bool missionAccepted = false;

            // The explorers of §12 fly at the computed speed of `FUN_1050_69c2`, without the
            // free-speed preference (behavior-specs-10/ai-opponent-behavior.md §12).
            int scoutWarp = Math.Max(1, EfficientWarp.ForFleet(this.fleet, false));

            // Find a star to scout
            StarIntel starToScout = CloesestStar(this.fleet, excludedStars);
            if (starToScout != null)
            {
                // Do we need fuel first?
                double fuelRequired = 0.0;
                Fleet nearestFuel = ClosestFuel(fleet);
                if (!fleet.CanRefuel)
                {
                    // Can not make fuel, so how much fuel is required to scout and then refuel?
                    if (nearestFuel != null)
                    {
                        int bestWarp = scoutWarp;
                        double bestSpeed = bestWarp * bestWarp;
                        double speedSquared = bestSpeed * bestSpeed;
                        double fuelConsumption = fleet.FuelConsumption(bestWarp, clientState.EmpireState.Race);
                        double distanceSquared = PointUtilities.DistanceSquare(fleet.Position, starToScout.Position); // to the stars
                        distanceSquared += PointUtilities.DistanceSquare(starToScout.Position, nearestFuel.Position); // and back to fuel (minimum)
                        double time = Math.Sqrt(distanceSquared / speedSquared);
                        fuelRequired = time * fuelConsumption;
                    }
                    else
                    {
                        // OMG there is no fuel! - just keep scouting then?
                    }
                }


                if (fleet.FuelAvailable > fuelRequired)
                {
                    // Fuel is no problem
                    SendFleet(starToScout, fleet, new NoTask(), scoutWarp);
                    missionAccepted = true;
                }
                else if (nearestFuel != null)
                {
                    // Refuel before scouting further
                    SendFleet(nearestFuel, fleet, new NoTask());
                }
            }

            if (missionAccepted)
            {
                return starToScout;
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Colonists to load before a colonization run (behavior-specs-10/ai-opponent-behavior.md
        /// §12, units of §13): the personality's figure in stored units (1 unit = 100 colonists =
        /// 1 kT), capped by the free hold and the planet's own population. Only a fleet standing
        /// at one of its own planets loads.
        /// </summary>
        public static int ColonistsToLoadKt(Fleet fleet, int loadUnits)
        {
            Star ourStar = fleet.InOrbit as Star;
            if (loadUnits <= 0 || ourStar == null || ourStar.Owner != fleet.Owner)
            {
                return 0;
            }

            int freeHold = Math.Max(0, fleet.TotalCargoCapacity - fleet.Cargo.Mass);
            int available = Math.Max(0, ourStar.Colonists / Global.ColonistsPerKiloton);
            return Math.Min(loadUnits, Math.Min(freeHold, available));
        }

        /// <summary>
        /// Send the fleet to colonise a planet (§12 colony-ship flow): load
        /// <paramref name="loadUnits"/> units of colonists where it stands if that is an own
        /// planet, then fly to the target with the Colonize task at the efficient warp of
        /// `FUN_1050_69c2` (without the free-speed preference, as the AI's colonizer orders ask).
        /// </summary>
        /// <param name="targetStar">The <see cref="StarIntel"/> for the target star to colonise.</param>
        /// <param name="loadUnits">The personality's colonist load (AiCategory.ColonistLoadUnits).</param>
        public void Colonise(StarIntel targetStar, int loadUnits)
        {
            int colonistsToLoadKt = ColonistsToLoadKt(this.fleet, loadUnits);
            if (colonistsToLoadKt > 0)
            {
                Star ourStar = (Star)this.fleet.InOrbit;

                CargoTask wpTask = new CargoTask();
                wpTask.Mode = CargoMode.Load;
                wpTask.Amount.ColonistsInKilotons = colonistsToLoadKt;

                Waypoint wp = new Waypoint();
                wp.Task = wpTask;
                wp.Position = ourStar.Position;
                wp.WarpFactor = this.fleet.FreeWarpSpeed;
                wp.Destination = ourStar.Name;

                WaypointCommand loadCommand = new WaypointCommand(CommandMode.Add, wp, this.fleet.Key);
                loadCommand.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(loadCommand);
            }

            SendFleet(targetStar, this.fleet, new ColoniseTask(), EfficientWarp.ForFleet(this.fleet, false));
        }

        /// <summary>Scrap Fleet where the fleet stands (§12: a colony fleet at a planet with no
        /// target, for the personalities that scrap it).</summary>
        public void Scrap()
        {
            Waypoint waypoint = new Waypoint();
            waypoint.Task = new ScrapTask();
            waypoint.Position = this.fleet.Position;
            waypoint.WarpFactor = 0;
            waypoint.Destination = this.fleet.InOrbit != null ? this.fleet.InOrbit.Name : this.fleet.Name;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, waypoint, this.fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        /// <summary>
        /// Carries out a decided <see cref="FleetOrder"/> (behavior-specs-10/ai-opponent-
        /// behavior.md §12's order shapes): unless the order keeps the fleet's orders, the route
        /// is cut back to the current position (a single destination "replaces every later
        /// waypoint"), the current task is cleared when asked, colonist transfers become
        /// zero-distance cargo waypoints at the current planet, and then the scrap, pursuit or
        /// destination leg (and any return leg) is written, at the order's warp or the efficient
        /// warp of `FUN_1050_69c2` without the free-speed preference. Exploring is the caller's
        /// (DefaultAi) business.
        /// </summary>
        public void Apply(FleetOrder order)
        {
            if (order == null || order.Keep)
            {
                return;
            }

            CutRoute();

            if (order.ClearTask && this.fleet.Waypoints.Count > 0 && !(this.fleet.Waypoints[0].Task is NoTask))
            {
                Waypoint cleared = new Waypoint(this.fleet.Waypoints[0]);
                cleared.Task = new NoTask();
                WaypointCommand edit = new WaypointCommand(CommandMode.Edit, cleared, this.fleet.Key, 0);
                edit.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(edit);
            }

            Star here = this.fleet.InOrbit as Star;
            if (here != null && order.LoadColonistsKt > 0)
            {
                CargoTask load = new CargoTask();
                load.Mode = CargoMode.Load;
                load.Amount.ColonistsInKilotons = order.LoadColonistsKt;
                AddWaypoint(here, load, Math.Max(1, this.fleet.FreeWarpSpeed));
            }

            if (here != null && order.UnloadColonistsKt > 0)
            {
                CargoTask unload = new CargoTask();
                unload.Mode = CargoMode.Unload;
                unload.Amount.ColonistsInKilotons = order.UnloadColonistsKt;
                AddWaypoint(here, unload, Math.Max(1, this.fleet.FreeWarpSpeed));
            }

            if (order.Scrap)
            {
                Scrap();
                return;
            }

            int warp = order.Warp > 0 ? order.Warp : Math.Max(1, EfficientWarp.ForFleet(this.fleet, false));

            if (order.PursueFleet != null)
            {
                Waypoint pursuit = new Waypoint();
                pursuit.AimAtFleet(order.PursueFleet);
                pursuit.Position = new NovaPoint(order.PursueFleet.Position);
                pursuit.Task = new NoTask();
                pursuit.WarpFactor = warp;

                WaypointCommand command = new WaypointCommand(CommandMode.Add, pursuit, this.fleet.Key);
                command.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(command);
            }
            else if (order.Destination != null)
            {
                AddWaypoint(order.Destination, order.DestinationTask ?? new NoTask(), warp);
            }

            if (order.ReturnLeg != null)
            {
                AddWaypoint(order.ReturnLeg, new NoTask(), warp);
            }
        }

        /// <summary>Cancels the route: cuts the waypoint list back to the current position.</summary>
        public void CutRoute()
        {
            while (this.fleet.Waypoints.Count > 1)
            {
                WaypointCommand command = new WaypointCommand(CommandMode.Delete, this.fleet.Key, this.fleet.Waypoints.Count - 1);
                command.ApplyToState(clientState.EmpireState);
                clientState.Commands.Push(command);
            }
        }

        /// <summary>
        /// The splitter's order (`FUN_1090_5bda`, §10/§16 item 3): moves the given stacks out of
        /// this fleet into a new fleet of their own, as a Split Fleet task on a zero-distance
        /// waypoint at the current position (turn generation performs it on arrival there).
        /// </summary>
        public void SplitOff(ICollection<long> designKeysToMove)
        {
            Dictionary<long, ShipToken> keep = new Dictionary<long, ShipToken>();
            Dictionary<long, ShipToken> moved = new Dictionary<long, ShipToken>();
            foreach (ShipToken token in this.fleet.Composition.Values)
            {
                bool moves = designKeysToMove.Contains(token.Design.Key);
                keep[token.Key] = new ShipToken(token.Design, moves ? 0 : token.Quantity);
                moved[token.Key] = new ShipToken(token.Design, moves ? token.Quantity : 0);
            }

            Waypoint waypoint = new Waypoint();
            waypoint.Position = new NovaPoint(this.fleet.Position);
            waypoint.Destination = this.fleet.InOrbit != null ? this.fleet.InOrbit.Name : this.fleet.Name;
            waypoint.WarpFactor = Math.Max(1, this.fleet.FreeWarpSpeed);
            waypoint.Task = new SplitMergeTask(keep, moved);

            WaypointCommand command = new WaypointCommand(CommandMode.Insert, waypoint, this.fleet.Key, Math.Min(1, this.fleet.Waypoints.Count));
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        /// <summary>Fly to a planet with no task (e.g. a minelayer wandering, §12).</summary>
        public void MoveTo(Mappable destination, int warp)
        {
            Waypoint w = new Waypoint();
            w.Position = destination.Position;
            w.Destination = destination.Name;
            w.Task = new NoTask();
            w.WarpFactor = Math.Max(1, warp);

            WaypointCommand command = new WaypointCommand(CommandMode.Add, w, this.fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        /// <summary>
        /// Carry out a decided freighter order (see FreighterRoutingSelector, §5 step 6): a single
        /// next waypoint at the destination with a Transport task, loading the chosen minerals
        /// away from the hub or unloading them at the hub, at the efficient warp (the original
        /// writes warp 4 and lets its end-of-pass speed setter replace it).
        /// </summary>
        /// <remarks>
        /// Colonists (categories 0 and 1): the hub load is a zero-distance Load waypoint at the
        /// hub, which turn generation performs before the freighter moves on (as for the colony
        /// ships' load). Nova's CargoTask has one mode for all cargo, so a run that both unloads
        /// colonists and loads minerals at the destination gets two waypoints there, colonists
        /// first; the mineral load is then performed the year after arrival.
        /// </remarks>
        public void DeliverCargo(FreighterRun run)
        {
            int warp = Math.Max(1, EfficientWarp.ForFleet(this.fleet, true));

            if (run.ColonistsToLoadAtHubKt > 0 && this.fleet.InOrbit is Star hub)
            {
                CargoTask loadColonists = new CargoTask();
                loadColonists.Mode = CargoMode.Load;
                loadColonists.Amount.ColonistsInKilotons = run.ColonistsToLoadAtHubKt;
                AddWaypoint(hub, loadColonists, warp); // zero distance: the warp does not matter
            }

            if (run.UnloadColonistsAtDestination)
            {
                CargoTask unloadColonists = new CargoTask();
                unloadColonists.Mode = CargoMode.Unload;
                unloadColonists.Amount.ColonistsInKilotons = this.fleet.Cargo.ColonistsInKilotons + run.ColonistsToLoadAtHubKt;
                AddWaypoint(run.Destination, unloadColonists, warp);

                if (run.Amount.Mass <= 0)
                {
                    return;
                }
            }

            CargoTask task = new CargoTask();
            task.Mode = run.Mode;
            task.Amount.Ironium = run.Amount.Ironium;
            task.Amount.Boranium = run.Amount.Boranium;
            task.Amount.Germanium = run.Amount.Germanium;
            AddWaypoint(run.Destination, task, warp);
        }

        private void AddWaypoint(Mappable destination, IWaypointTask task, int warp)
        {
            Waypoint waypoint = new Waypoint();
            waypoint.Task = task;
            waypoint.Position = destination.Position;
            waypoint.WarpFactor = warp;
            waypoint.Destination = destination.Name;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, waypoint, this.fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        /// <summary>
        /// Orders this fleet to lay mines at its current position - §12's minelayer branch
        /// ("otherwise, if it has no task, it gets Lay Mines with both task parameters set to
        /// 5"; Nova's LayMinesTask has one duration parameter, whose default is 5,
        /// "indefinitely").
        /// </summary>
        public void LayMines()
        {
            Waypoint waypoint = new Waypoint();
            waypoint.Task = new LayMinesTask();
            waypoint.Position = this.fleet.Position;
            waypoint.WarpFactor = 0;
            waypoint.Destination = this.fleet.Name;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, waypoint, this.fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        /// <summary>
        /// Return closest star to current fleet.
        /// </summary>
        /// <param name="fleet"></param>
        /// <returns></returns>
        private StarIntel CloesestStar(Fleet fleet, List<StarIntel> excludedStars)
        {
            StarIntel target = null;
            double distance = double.MaxValue;
            foreach (StarIntel report in clientState.EmpireState.StarReports.Values)
            {
                if (excludedStars.Contains(report) != true)
                {
                    if (distance > Math.Sqrt(Math.Pow(fleet.Position.X - report.Position.X, 2) + Math.Pow(fleet.Position.Y - report.Position.Y, 2)))
                    {
                        target = report;
                        distance = Math.Sqrt(Math.Pow(fleet.Position.X - target.Position.X, 2) + Math.Pow(fleet.Position.Y - target.Position.Y, 2));
                    }
                }
            }
            return target;
        }

        /// <summary>
        /// Return the closest refuelling point.
        /// </summary>
        /// <param name="fleet">The fleet looking for fuel.</param>
        /// <returns>The closest fleet that can refuel (normally a star base).</returns>
        private Fleet ClosestFuel(Fleet customer)
        {
            if (customer == null)
            {
                return null;
            }

            // initialise the list of fuel stations, if null.
            if (FuelStations == null)
            {
                FuelStations = new FleetList();
                foreach (Fleet pump in clientState.EmpireState.OwnedFleets.Values)
                {
                    if (pump.CanRefuel)
                    {
                        FuelStations.Add(pump);
                    }
                }
            }

            // if there are still no fuel stations, bug out
            if (FuelStations.Count == 0)
            {
                return null;
            }

            Fleet closestFuelSoFar = null;
            double minRefulerDistance = double.MaxValue;

            foreach (Fleet pump in FuelStations.Values)
            {
                double distSquare = PointUtilities.DistanceSquare(pump.Position, customer.Position);
                if (distSquare < minRefulerDistance)
                {
                    minRefulerDistance = distSquare;
                    closestFuelSoFar = pump;
                }
            }

            return closestFuelSoFar;
        }


        private void SendFleet(NovaPoint position, Fleet fleet, IWaypointTask task)
        {
            Waypoint w = new Waypoint();
            w.Position = position;
            w.Destination = position.ToString();
            w.Task = task;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, w, fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        private void SendFleet(FleetIntel target, Fleet fleet, IWaypointTask task)
        {
            Waypoint w = new Waypoint();
            w.Position = target.Position;
            w.Destination = target.Name;
            w.Task = task;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, w, fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        private void SendFleet(Fleet target, Fleet fleet, IWaypointTask task)
        {
            Waypoint w = new Waypoint();
            w.Position = target.Position;
            w.Destination = target.Name;
            w.Task = task;

            WaypointCommand command = new WaypointCommand(CommandMode.Add, w, fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }

        private void SendFleet(StarIntel star, Fleet fleet, IWaypointTask task, int? warp = null)
        {
            Waypoint w = new Waypoint();
            w.Position = star.Position;

            w.Destination = star.Name;
            w.Task = task;
            if (warp.HasValue)
            {
                // A fleet whose designs have no engine gets 0; keep it at least 1 so the order
                // stays a legal movement order.
                w.WarpFactor = Math.Max(1, warp.Value);
            }

            WaypointCommand command = new WaypointCommand(CommandMode.Add, w, fleet.Key);
            command.ApplyToState(clientState.EmpireState);
            clientState.Commands.Push(command);
        }
    }
}
