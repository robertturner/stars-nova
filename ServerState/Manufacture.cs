#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// Class to manufacture the items in a star's queue.
    /// </summary>
    public class Manufacture
    {
        private ServerData serverState;
        // Null unless injected: then the game's seeded "Manufacture" stream is taken on first use
        // (ServerData.CreateRandom), so a Genesis Device re-roll is repeatable from the seed.
        private Random random;

        public Manufacture(ServerData serverState) : this(serverState, null)
        {
        }

        /// <summary>Overload for deterministic testing of the Genesis Device's planetary re-roll.</summary>
        public Manufacture(ServerData serverState, Random random)
        {
            this.serverState = serverState;
            this.random = random;
        }

        /// <summary>
        /// Manufacture the items in a production queue (resources permitting).
        /// </summary>
        /// <param name="star">The star doing production.</param>
        /// <remarks>
        /// Dont't preserve resource count as resource depletion is needed to
        /// contribute with leftover resources for research.
        ///
        /// Mineral Alchemy's position rule (behavior-specs-10/production-queue.md section 7, "How
        /// the code does it"): an AUTO Alchemy entry that is not last is skipped and arms a
        /// shortfall conversion for the NEXT entry only (ProductionOrder.Process(Star,
        /// AlchemyShortfallConversion)); when that served entry leaves the queue (completed or cut
        /// to zero by a quantity clamp) the auto Alchemy entry in front of it goes with it. In last
        /// position the auto entry buys as many units as the resources pay for (quantity 1,000).
        /// Any part-paid alchemy unit - the last-position entry's remainder or a conversion's
        /// leftover - becomes a one-unit MANUAL Mineral Alchemy entry at the top of the queue once
        /// the walk ends. Message 140 is posted once per purchase that made any alchemy kT.
        /// </remarks>
        public void Items(Star star)
        {
            // Every Terraform entry measures its headroom against the owner's CURRENT best
            // terraform components (production-queue.md 10k item 3); stamped here each turn.
            if (serverState.AllEmpires.TryGetValue(star.Owner, out EmpireData owner))
            {
                TerraformReach reach = TerraformReach.For(owner);
                foreach (ProductionOrder order in star.ManufacturingQueue.Queue)
                {
                    if (order.Unit is TerraformProductionUnit terraform)
                    {
                        terraform.Reach = reach;
                    }
                }
            }

            // The shared walk (ProductionQueue.ProcessYear) - the completion estimator runs the
            // same one - with this step's side effects attached at the point the original has
            // them: ships and starbases, the Genesis Device, and the planet owner's notices.
            ProductionQueue.YearOutcome outcome = star.ManufacturingQueue.ProcessYear(star, entry =>
            {
                ProductionOrder productionOrder = entry.Order;
                int done = entry.Done;

                if (entry.QuantityCut)
                {
                    ReportQuantityCut(star, productionOrder, entry.QuantityAfterCut);
                }

                if (entry.AlchemyKilotons > 0)
                {
                    ReportAlchemy(star, entry.AlchemyKilotons);
                }

                foreach (TerraformStep step in entry.TerraformSteps)
                {
                    ReportTerraformStep(star, step);
                }

                if (done > 0 && productionOrder.Unit is ShipProductionUnit)
                {
                    long designKey = (productionOrder.Unit as ShipProductionUnit).DesignKey;
                    ushort designOwner = (productionOrder.Unit as ShipProductionUnit).DesignKey.Owner();

                    CreateShips(serverState.AllEmpires[designOwner].Designs[designKey], star, done);
                }

                if (done > 0 && productionOrder.Unit is GenesisDeviceProductionUnit)
                {
                    ApplyGenesisDevice(star);
                }

                // Mineral packets: the completion routine is called once with the unit count
                // (production-queue.md §10, §10b), so this entry's units form one launch.
                if (done > 0 && productionOrder.Unit is PacketProductionUnit packetUnit)
                {
                    Nova.Server.TurnSteps.PacketLaunch.Launch(serverState, star, packetUnit, done);
                }
            });

            if (!outcome.HadEntries)
            {
                // Message 63: an owned planet with no queue at all.
                PostToOwner(star, "The production queue on " + star.Name + " is empty.", ProductionNoticeTypes.QueueEmpty);
            }
            else if (outcome.FinishedOrders)
            {
                // Message 62: the planet finished its orders (production-queue.md 10i).
                PostToOwner(star, star.Name + " has completed all of its production orders.", ProductionNoticeTypes.OrdersCompleted);
            }
        }

        /// <summary>A production notice for the planet's owner (production-queue.md 10i: every
        /// production message goes to the planet's owner), whose subject (Event) is the planet's
        /// name; 62 and 63 have their own types so a click can open the queue
        /// (ProductionNoticeTypes).</summary>
        private void PostToOwner(Star star, string text, string messageType = ProductionMessageType)
        {
            Message message = new Message();
            message.Audience = star.Owner;
            message.Text = text;
            message.Type = messageType;
            message.Event = star.Name;
            serverState.AllMessages.Add(message);
        }

        /// <summary>The Message.Type of the production-queue notices posted here.</summary>
        public const string ProductionMessageType = ProductionNoticeTypes.Production;

        /// <summary>
        /// Message 298 (an installations order exceeded the allowed maximum and was cut back) or
        /// 303 (the same for terraforming), posted when the hub cut a manual order to its room;
        /// "deleted" when the room was below 1.
        /// </summary>
        private void ReportQuantityCut(Star star, ProductionOrder order, int cutTo)
        {
            if (order.Unit is PacketProductionUnit)
            {
                // Message 297: a manual packet order with no mass driver or no destination is
                // cancelled before purchase (production-queue.md 10i).
                PostToOwner(star, "The mineral packet order on " + star.Name
                    + " has been cancelled because the planet has no mass driver or the driver has no destination.");
                return;
            }

            bool terraform = order.Unit is TerraformProductionUnit;
            string what = terraform ? "terraforming" : order.Unit.Name;
            string text = cutTo == 0
                ? "The " + what + " order on " + star.Name + " exceeded the allowed maximum and has been removed."
                : "The " + what + " order on " + star.Name + " exceeded the allowed maximum and has been reduced to " + cutTo + ".";
            PostToOwner(star, text);
        }

        /// <summary>Message 123, once per environment point moved: which factor went up or down,
        /// and to what value.</summary>
        private void ReportTerraformStep(Star star, TerraformStep step)
        {
            PostToOwner(star, "Your terraforming on " + star.Name + " has " + (step.Increased ? "increased" : "decreased")
                + " the " + step.AxisName + " to " + step.NewValue + ".");
        }

        /// <summary>
        /// Message 140: the scientists on the planet transmuted common materials into a stated
        /// number of kT of each mineral (production-queue.md section 10, type 3/11 row). Posted
        /// once per purchase that created any alchemy kilotons, by queued units or by a shortfall
        /// conversion.
        /// </summary>
        private void ReportAlchemy(Star star, int kilotons)
        {
            PostToOwner(star, "Your scientists on " + star.Name + " have transmuted common materials into "
                + kilotons + "kT each of Ironium, Boranium and Germanium.");
        }


        /// <summary>
        /// A Genesis Device completed on this planet: reset it (Star.ApplyGenesisDevice) and tell
        /// every player - message 283, "Strong fundamental forces have rebirthed (planet).", goes to
        /// every race in the game, not just the planet's owner (Audience 0 means everyone).
        /// </summary>
        private void ApplyGenesisDevice(Star star)
        {
            star.ApplyGenesisDevice(random ?? (random = serverState.CreateRandom("Manufacture")));

            Message message = new Message();
            message.Audience = 0;
            message.Text = "Strong fundamental forces have rebirthed " + star.Name + ".";
            serverState.AllMessages.Add(message);
        }

        /// <summary>The hard limit of fleets per race (production-queue.md 10e).</summary>
        public const int MaxFleetsPerRace = 512;

        /// <summary>The most ships one design stack holds (production-queue.md 10e).</summary>
        public const int MaxShipsPerStack = 32766;

        /// <summary>The race's fleets, not counting starbases (which orbit a planet rather than
        /// occupy a fleet slot in the original).</summary>
        private static int CountShipFleets(EmpireData empire)
        {
            int count = 0;
            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (!fleet.IsStarbase)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// A race that already owns 512 fleets: the new ships join a fleet of the same design at
        /// the same planet if one has room in its stack (message 313), otherwise they are lost
        /// (message 186). The resources and minerals were spent before this check
        /// (production-queue.md 10e).
        /// </summary>
        private void MergeOrLoseShips(ShipDesign design, Star star, int count, EmpireData empire)
        {
            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (fleet.IsStarbase || fleet.InOrbit != star || !fleet.Composition.TryGetValue(design.Key, out ShipToken token))
                {
                    continue;
                }

                if (token.Quantity + count > MaxShipsPerStack)
                {
                    continue;
                }

                token.Quantity += count;
                token.Armor += design.Armor * count;
                token.Shields += design.Shield * count;
                PostToOwner(star, count + " new " + design.Name + " built at " + star.Name
                    + " have joined " + fleet.Name + " because your fleet limit has been reached.");
                return;
            }

            PostToOwner(star, count + " new " + design.Name + " built at " + star.Name
                + " have been lost because you already have " + MaxFleetsPerRace + " fleets.");
        }

        /// <summary>
        /// Messages 205-207 (production-queue.md 10i), on every starbase completion: 205 for a
        /// base with no dock, 206 with the dock's stated hull-weight limit in kT, 207 when the dock
        /// takes ships of any size (stored as 10,000 kT or more in this project's hull data).
        /// </summary>
        private void ReportStarbaseBuilt(Star star, ShipDesign design)
        {
            int dock = 0;
            if (design.Blueprint != null && design.Blueprint.Properties.TryGetValue("Hull", out ComponentProperty property) && property is Hull hull)
            {
                dock = hull.DockCapacity;
            }

            string text = star.Name + " has built a new " + design.Name + " starbase.";
            if (dock >= UnlimitedDockCapacity)
            {
                text += " Ships of any size can now be built there.";
            }
            else if (dock > 0)
            {
                text += " Ships up to " + dock + "kT total hull weight can now be built there.";
            }

            PostToOwner(star, text);
        }

        /// <summary>The dock capacity this project's hull data uses for "any size".</summary>
        private const int UnlimitedDockCapacity = 10000;

        /// <summary>
        /// Create a new ship or starbase at the specified location. Starbases are
        /// handled just like ships except that they cannot move.
        /// </summary>
        /// <param name="design">A ShipDesign to be constructed.</param>
        /// <param name="star">The star system producing the ship.</param>
        private void CreateShips(ShipDesign design, Star star, int countToBuild)
        {
            EmpireData empire = serverState.AllEmpires[star.Owner];

            if (design.Type == ItemType.Starbase)
            {
                ReportStarbaseBuilt(star, design);
            }
            else if (CountShipFleets(empire) >= MaxFleetsPerRace)
            {
                // The resources were already spent; the ships are merged or lost (10e).
                MergeOrLoseShips(design, star, countToBuild, empire);
                return;
            }

            ShipToken token = new ShipToken(design, countToBuild);
            
            Fleet fleet = new Fleet(token, star, empire.GetNextFleetKey());
            
            fleet.Name = design.Name + " #" + fleet.Id;
            fleet.FuelAvailable = fleet.TotalFuelCapacity;

            if (design.Type != ItemType.Starbase)
            {
                // A starbase is announced by messages 205-207 instead (ReportStarbaseBuilt).
                Message message = new Message();
                message.Audience = star.Owner;
                message.Text = star.Name + " has produced " + countToBuild + " new " + design.Name;
                // message.Event = fleet; // will not be persisted unless the Type is implemented.
                // message.Type = "Fleet"; // TODO (priority 5) - need to add a fleet type message so it can save/load.
                serverState.AllMessages.Add(message);
            }
            
            // Add the fleet to the state data so it can be tracked.
            serverState.AllEmpires[fleet.Owner].AddOrUpdateFleet(fleet);
            
            if (design.Type == ItemType.Starbase)
            {
                // production-queue.md 10e: if the planet had no mass driver before, the packet
                // target and speed are cleared when the new base has none, or the speed is
                // initialised to the new driver's rating when it has one; a planet that already
                // had a driver keeps its settings.
                bool hadMassDriver = MineralPacketRules.HasAccelerator(star);

                if (star.Starbase != null)
                {
                    // Old starbases are not scrapped (no scrap-value refund) - the reduced
                    // upgrade cost should have already been factored when first queuing the
                    // "upgrade", so the old SB is just discarded and replaced at this point.
                    // -Aeglos 2 Aug 11
                    //
                    // "Discarded" has to mean actually removed from the empire's own
                    // bookkeeping (OwnedFleets/FleetReports), not just detached from
                    // star.Starbase - left in OwnedFleets with nothing pointing back to it, the
                    // old fleet kept showing up everywhere "the fleets in orbit at this star"
                    // are listed (e.g. the mobile Map's switcher), since those only ever
                    // exclude whichever fleet star.Starbase CURRENTLY points to, never a stale
                    // one that used to hold that role.
                    empire.RemoveFleet(star.Starbase);
                    star.Starbase = null;
                }

                star.Starbase = fleet;
                fleet.Type = ItemType.Starbase;
                fleet.Name = star.Name + " " + fleet.Type;
                fleet.InOrbit = star;

                if (!hadMassDriver)
                {
                    int newRating = MineralPacketRules.LaunchRating(fleet);
                    if (newRating <= 0)
                    {
                        star.PacketDestination = null;
                        star.PacketWarp = 0;
                    }
                    else
                    {
                        star.PacketWarp = newRating;
                    }
                }

                // Improved Starbases' cloak baseline is now sourced from ShipDesign.Update's own
                // raw-cloak-unit mechanism (see CloakCalculator/Fleet.RecalculateCloak) and
                // recomputed fresh every turn by ScanStep before this fleet is ever used as a scan
                // target, so this flat, build-time-only 20 - which composed with nothing (an
                // installed cloak device on the starbase would have just overwritten it, not
                // combined) - is now redundant.
            }
            else
            {
                fleet.InOrbit = star;
            }
        }
    }
}
