#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Common.Waypoints
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    /// <summary>
    /// The five Transport slots, in the handler's fixed order (behavior-specs-10/
    /// fleet-movement-scanning-cargo.md §4, "Conditional transfers, traced in full": ironium,
    /// boranium, germanium, colonists, fuel). Units: kT for minerals and colonists, mg for fuel.
    /// </summary>
    public enum CargoSlot
    {
        Ironium = 0,
        Boranium = 1,
        Germanium = 2,
        Colonists = 3,
        Fuel = 4
    }

    /// <summary>
    /// One Transport instruction code. The spec names every instruction but gives only one numeric
    /// code (7); the order below is the Waypoint dialog's list order and is this port's
    /// assumption for the other codes.
    /// </summary>
    public enum CargoAction
    {
        None = 0,
        LoadAll = 1,
        UnloadAll = 2,
        LoadExactly = 3,
        UnloadExactly = 4,

        /// <summary>Fill up to N%: load floor(N% x capacity) minus what is carried.</summary>
        FillToPercent = 5,

        /// <summary>Wait for N%: as Fill, but the task stays until the level or a full hold.</summary>
        WaitForPercent = 6,

        /// <summary>
        /// Code 7: Load Dunnage on a cargo slot (a second sweep fills the remaining space once
        /// everything else is done); Load Optimal on the fuel slot (keep only the fuel the
        /// remaining route needs and hand the excess to the target).
        /// </summary>
        LoadDunnageOrOptimal = 7,

        /// <summary>Set amount to N: the fleet ends with N (shortfall loaded, excess unloaded).</summary>
        SetAmountTo = 8,

        /// <summary>Set waypoint to N: the source ends with N (surplus loaded, deficit unloaded).</summary>
        SetWaypointTo = 9
    }

    /// <summary>
    /// One slot's Transport instruction: an action and a 12-bit amount (0-4095; a percentage for
    /// Fill/Wait).
    /// </summary>
    public class CargoInstruction
    {
        /// <summary>The largest amount the 12-bit field holds.</summary>
        public const int MaxAmount = 4095;

        private int amount;

        public CargoInstruction()
        {
        }

        public CargoInstruction(CargoAction action, int amount = 0)
        {
            Action = action;
            Amount = amount;
        }

        public CargoInstruction(CargoInstruction copy)
        {
            Action = copy.Action;
            Amount = copy.Amount;
        }

        public CargoAction Action { get; set; }

        /// <summary>The amount, clamped to the 12-bit range 0-4095.</summary>
        public int Amount
        {
            get { return amount; }
            set { amount = Math.Max(0, Math.Min(MaxAmount, value)); }
        }

        public bool IsUnloadOnly
        {
            get { return Action == CargoAction.UnloadAll || Action == CargoAction.UnloadExactly; }
        }
    }

    /// <summary>
    /// The Transport task handler for a <see cref="CargoTask"/> that carries per-slot
    /// instructions (behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Conditional
    /// transfers, traced in full"). Each instruction moves its whole computed amount at once, in
    /// whole units, with no chunk size or divisor; free space is recomputed after every slot.
    /// The original runs the handler four times a turn (unload, load, unload, load); this port
    /// runs one unload pass then one load pass each time the task is performed (on arrival, then
    /// once a turn while the task is pending).
    /// </summary>
    public static class TransportHandler
    {
        public const int SlotCount = 5;

        /// <summary>
        /// One unload pass then one load pass. Returns true when the task is complete (the load
        /// pass was allowed everywhere, every Set amount to N found enough at the source and every
        /// Wait for N% reached its level or filled the hold), or when a blocked load ended it.
        /// Unload-only instructions carried out are struck off (set to None) either way.
        /// </summary>
        public static bool Run(CargoInstruction[] instructions, Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver, List<Message> messages)
        {
            List<string> moved = new List<string>();

            UnloadPass(instructions, fleet, target, sender, receiver, messages, moved);

            bool blocked;
            bool complete = LoadPass(instructions, fleet, target, sender, messages, moved, out blocked);

            if (moved.Count > 0)
            {
                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Text = "Fleet " + fleet.Name + " at " + target.Name + ": " + string.Join(", ", moved) + ".";
                messages.Add(message);
            }

            if (blocked)
            {
                return true;
            }

            if (!complete)
            {
                Message waiting = new Message();
                waiting.Audience = fleet.Owner;
                waiting.Text = "Fleet " + fleet.Name + " is waiting at " + target.Name + " to complete its transport orders.";
                messages.Add(waiting);
            }

            return complete;
        }

        // ----------------------------------------------------------------------------------
        // Passes
        // ----------------------------------------------------------------------------------

        private static void UnloadPass(CargoInstruction[] instructions, Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver, List<Message> messages, List<string> moved)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                CargoInstruction instruction = instructions[i];
                if (instruction == null || instruction.Action == CargoAction.None)
                {
                    continue;
                }

                CargoSlot slot = (CargoSlot)i;
                int carried = FleetAmount(fleet, slot);
                int amount = 0;

                switch (instruction.Action)
                {
                    case CargoAction.UnloadAll:
                        amount = carried;
                        break;
                    case CargoAction.UnloadExactly:
                        amount = Math.Min(instruction.Amount, carried);
                        break;
                    case CargoAction.SetAmountTo:
                        // An excess over N is unloaded.
                        amount = Math.Max(0, carried - instruction.Amount);
                        break;
                    case CargoAction.SetWaypointTo:
                        // A deficit below N at the source is unloaded, never more than carried.
                        amount = Math.Min(carried, Math.Max(0, instruction.Amount - SourceAmount(target, slot)));
                        break;
                }

                bool refused = false;
                if (amount > 0)
                {
                    int done = Unload(fleet, target, slot, amount, sender, receiver, messages, out refused);
                    if (done > 0)
                    {
                        moved.Add("unloaded " + done + " " + Unit(slot) + " " + slot);
                    }
                }

                // Instructions already carried out in an unload pass are struck off.
                if (instruction.IsUnloadOnly)
                {
                    instruction.Action = CargoAction.None;
                    instruction.Amount = 0;
                }

                // A refused unload leaves the cargo aboard, posts its message and stops the
                // fleet's sweep for the pass.
                if (refused)
                {
                    return;
                }
            }
        }

        private static bool LoadPass(CargoInstruction[] instructions, Fleet fleet, Mappable target, EmpireData sender, List<Message> messages, List<string> moved, out bool blocked)
        {
            bool complete = true;
            blocked = false;

            for (int i = 0; i < SlotCount; i++)
            {
                CargoInstruction instruction = instructions[i];
                if (instruction == null)
                {
                    continue;
                }

                CargoSlot slot = (CargoSlot)i;
                int carried = FleetAmount(fleet, slot);
                int source = SourceAmount(target, slot);
                int want;

                switch (instruction.Action)
                {
                    case CargoAction.LoadAll:
                        want = source;
                        break;
                    case CargoAction.LoadExactly:
                        // Finding less loads what there is and counts as done.
                        want = instruction.Amount;
                        break;
                    case CargoAction.FillToPercent:
                    case CargoAction.WaitForPercent:
                        want = Math.Max(0, PercentLevel(fleet, slot, instruction.Amount) - carried);
                        break;
                    case CargoAction.SetAmountTo:
                        want = Math.Max(0, instruction.Amount - carried);
                        break;
                    case CargoAction.SetWaypointTo:
                        want = Math.Max(0, source - instruction.Amount);
                        break;
                    default:
                        continue;
                }

                if (want > 0)
                {
                    if (!MayTakeFrom(fleet, target, slot))
                    {
                        // A blocked load in the post-movement pass: the owner is told and the
                        // task ends.
                        Message message = new Message();
                        message.Audience = fleet.Owner;
                        message.Text = "Fleet " + fleet.Name + " could not load " + slot + " from " + target.Name
                            + " because it belongs to another race.";
                        messages.Add(message);
                        blocked = true;
                        return false;
                    }

                    int got = Math.Min(want, Math.Min(FreeSpace(fleet, slot), source));
                    if (got > 0)
                    {
                        Transfer(fleet, target, slot, got);
                        moved.Add("loaded " + got + " " + Unit(slot) + " " + slot);
                    }

                    if (instruction.Action == CargoAction.SetAmountTo && source < want)
                    {
                        complete = false;
                    }
                }

                if (instruction.Action == CargoAction.WaitForPercent
                    && FleetAmount(fleet, slot) < PercentLevel(fleet, slot, instruction.Amount)
                    && FreeSpace(fleet, slot) > 0)
                {
                    complete = false;
                }
            }

            // Load Dunnage: only when every other condition was met and the hold still has room;
            // each dunnage slot, in slot order, loads as much as the source has and space allows.
            if (complete)
            {
                for (int i = 0; i < (int)CargoSlot.Fuel; i++)
                {
                    CargoInstruction instruction = instructions[i];
                    if (instruction == null || instruction.Action != CargoAction.LoadDunnageOrOptimal)
                    {
                        continue;
                    }

                    CargoSlot slot = (CargoSlot)i;
                    int got = Math.Min(FreeSpace(fleet, slot), SourceAmount(target, slot));
                    if (got > 0 && MayTakeFrom(fleet, target, slot))
                    {
                        Transfer(fleet, target, slot, got);
                        moved.Add("loaded " + got + " " + Unit(slot) + " " + slot + " as dunnage");
                    }
                }
            }

            // Load Optimal (code 7 on the fuel slot): runs after the sweep.
            CargoInstruction fuel = instructions[(int)CargoSlot.Fuel];
            if (fuel != null && fuel.Action == CargoAction.LoadDunnageOrOptimal)
            {
                LoadOptimal(fleet, target, sender, messages, moved);
            }

            return complete;
        }

        /// <summary>
        /// Load Optimal: the fleet keeps only the fuel its remaining route needs and hands the
        /// excess to the target (all of it when there is no further waypoint). It never takes fuel
        /// in; if the route needs more than the fleet carries, the owner is warned instead. Nova's
        /// fleet mass does not include fuel, so shedding fuel never changes the need and the
        /// original's "recompute until stable" loop settles at once. Planets have no fuel slot, so
        /// only another fleet can receive the excess (capped to its free tank space).
        /// </summary>
        private static void LoadOptimal(Fleet fleet, Mappable target, EmpireData sender, List<Message> messages, List<string> moved)
        {
            int carried = FleetAmount(fleet, CargoSlot.Fuel);
            int need = fleet.FuelRequiredForRoute(sender?.Race, 1);

            if (need > carried)
            {
                Message warning = new Message();
                warning.Audience = fleet.Owner;
                warning.Text = "Fleet " + fleet.Name + " needs " + need + "mg of fuel for its remaining route but carries only "
                    + carried + "mg.";
                messages.Add(warning);
                return;
            }

            Fleet receiving = target as Fleet;
            if (receiving == null)
            {
                return;
            }

            int excess = Math.Min(carried - need, FreeSpace(receiving, CargoSlot.Fuel));
            if (excess > 0)
            {
                fleet.FuelAvailable -= excess;
                receiving.FuelAvailable += excess;
                moved.Add("handed over " + excess + "mg Fuel");
            }
        }

        // ----------------------------------------------------------------------------------
        // Slot accessors
        // ----------------------------------------------------------------------------------

        /// <summary>What the fleet carries in a slot.</summary>
        public static int FleetAmount(Fleet fleet, CargoSlot slot)
        {
            switch (slot)
            {
                case CargoSlot.Ironium:
                    return fleet.Cargo.Ironium;
                case CargoSlot.Boranium:
                    return fleet.Cargo.Boranium;
                case CargoSlot.Germanium:
                    return fleet.Cargo.Germanium;
                case CargoSlot.Colonists:
                    return fleet.Cargo.ColonistsInKilotons;
                default:
                    return (int)Math.Floor(fleet.FuelAvailable);
            }
        }

        /// <summary>What the target (a planet or another fleet) holds in a slot. Planets have no
        /// fuel slot; a planet's colonists are counted in whole kT (100 colonists).</summary>
        public static int SourceAmount(Mappable target, CargoSlot slot)
        {
            if (target is Fleet other)
            {
                return FleetAmount(other, slot);
            }

            if (target is Star star)
            {
                switch (slot)
                {
                    case CargoSlot.Ironium:
                        return Math.Max(0, star.ResourcesOnHand.Ironium);
                    case CargoSlot.Boranium:
                        return Math.Max(0, star.ResourcesOnHand.Boranium);
                    case CargoSlot.Germanium:
                        return Math.Max(0, star.ResourcesOnHand.Germanium);
                    case CargoSlot.Colonists:
                        return Math.Max(0, star.Colonists / Global.ColonistsPerKiloton);
                }
            }

            return 0;
        }

        /// <summary>Hold space left (cargo slots share the hold; fuel has its own tank).</summary>
        public static int FreeSpace(Fleet fleet, CargoSlot slot)
        {
            if (slot == CargoSlot.Fuel)
            {
                return Math.Max(0, fleet.TotalFuelCapacity - FleetAmount(fleet, CargoSlot.Fuel));
            }

            return Math.Max(0, fleet.TotalCargoCapacity - fleet.Cargo.Mass);
        }

        /// <summary>floor(N% x capacity): the whole cargo hold for the four cargo slots, the tank
        /// for fuel - the handler's only rounding step.</summary>
        public static int PercentLevel(Fleet fleet, CargoSlot slot, int percent)
        {
            int capacity = slot == CargoSlot.Fuel ? fleet.TotalFuelCapacity : fleet.TotalCargoCapacity;
            return (int)((long)capacity * percent / 100);
        }

        /// <summary>
        /// May the fleet load this slot from the target? Its own planets and fleets, and unowned
        /// planets, always. Another race's planet or fleet only with the cargo-theft scanner ability
        /// (§4, "Caps on a load"; "Theft"): a Robber Baron Scanner (full theft) takes from a planet,
        /// either theft scanner takes from a fleet. No slot is excluded in theft mode - minerals,
        /// colonists and (from fleets) fuel can all be taken; a planet has no fuel slot.
        /// </summary>
        public static bool MayTakeFrom(Fleet fleet, Mappable target, CargoSlot slot)
        {
            if (target is Star star)
            {
                if (slot == CargoSlot.Fuel)
                {
                    return false;
                }

                if (star.Owner == fleet.Owner || star.Owner == Global.Nobody)
                {
                    return true;
                }

                return fleet.CanStealFromPlanets;
            }

            if (target is Fleet other)
            {
                if (other.Owner == fleet.Owner)
                {
                    return true;
                }

                return fleet.CanStealFromFleets;
            }

            return false;
        }

        /// <summary>Moves a positive amount from the target into the fleet (the caps are the
        /// caller's).</summary>
        private static void Transfer(Fleet fleet, Mappable target, CargoSlot slot, int amount)
        {
            Add(fleet, slot, amount);

            if (target is Fleet other)
            {
                Add(other, slot, -amount);
            }
            else if (target is Star star)
            {
                AddToStar(star, slot, -amount);
            }
        }

        /// <summary>
        /// Unloads up to <paramref name="amount"/> of a slot onto the target and returns what was
        /// moved. Onto a planet: minerals always (a gift to another race); colonists onto an own
        /// planet join the population, onto any other planet they follow the colonist unload
        /// outcome table (85 / 86 / 309 refusals leave them aboard, otherwise an invasion); fuel
        /// cannot go onto a planet. Into a fleet: cut to its free space, and colonists never go
        /// into another race's fleet.
        /// </summary>
        private static int Unload(Fleet fleet, Mappable target, CargoSlot slot, int amount, EmpireData sender, EmpireData receiver, List<Message> messages, out bool refused)
        {
            refused = false;

            if (target is Fleet other)
            {
                if (slot == CargoSlot.Colonists && other.Owner != fleet.Owner)
                {
                    return 0;
                }

                int room = FreeSpace(other, slot);
                int done = Math.Min(amount, room);
                if (done > 0)
                {
                    Add(fleet, slot, -done);
                    Add(other, slot, done);
                }

                return done;
            }

            Star star = target as Star;
            if (star == null || slot == CargoSlot.Fuel)
            {
                return 0;
            }

            if (slot == CargoSlot.Colonists && star.Owner != fleet.Owner)
            {
                InvadeTask invade = new InvadeTask();
                bool invaded = false;
                if (invade.IsValid(fleet, star, sender, receiver))
                {
                    invaded = invade.Invade(fleet, star, sender, receiver, amount);
                }
                else
                {
                    refused = true;
                }

                messages.AddRange(invade.Messages);
                return invaded ? amount : 0;
            }

            Add(fleet, slot, -amount);
            AddToStar(star, slot, amount);
            return amount;
        }

        private static void Add(Fleet fleet, CargoSlot slot, int amount)
        {
            switch (slot)
            {
                case CargoSlot.Ironium:
                    fleet.Cargo.Ironium += amount;
                    break;
                case CargoSlot.Boranium:
                    fleet.Cargo.Boranium += amount;
                    break;
                case CargoSlot.Germanium:
                    fleet.Cargo.Germanium += amount;
                    break;
                case CargoSlot.Colonists:
                    fleet.Cargo.ColonistsInKilotons += amount;
                    break;
                default:
                    fleet.FuelAvailable += amount;
                    break;
            }
        }

        private static void AddToStar(Star star, CargoSlot slot, int amount)
        {
            switch (slot)
            {
                case CargoSlot.Ironium:
                    star.ResourcesOnHand.Ironium += amount;
                    break;
                case CargoSlot.Boranium:
                    star.ResourcesOnHand.Boranium += amount;
                    break;
                case CargoSlot.Germanium:
                    star.ResourcesOnHand.Germanium += amount;
                    break;
                case CargoSlot.Colonists:
                    star.Colonists += amount * Global.ColonistsPerKiloton;
                    break;
            }
        }

        private static string Unit(CargoSlot slot)
        {
            return slot == CargoSlot.Fuel ? "mg" : "kT";
        }

        // ----------------------------------------------------------------------------------
        // Persistence
        // ----------------------------------------------------------------------------------

        public static CargoInstruction[] Copy(CargoInstruction[] instructions)
        {
            if (instructions == null)
            {
                return null;
            }

            CargoInstruction[] copy = new CargoInstruction[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                copy[i] = instructions.Length > i && instructions[i] != null
                    ? new CargoInstruction(instructions[i])
                    : new CargoInstruction();
            }

            return copy;
        }

        public static XmlElement ToXml(XmlDocument xmldoc, CargoInstruction instruction, CargoSlot slot)
        {
            XmlElement element = xmldoc.CreateElement("Instruction");
            Global.SaveData(xmldoc, element, "Slot", slot.ToString());
            Global.SaveData(xmldoc, element, "Action", instruction.Action.ToString());
            Global.SaveData(xmldoc, element, "Amount", instruction.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return element;
        }

        public static void LoadInstruction(XmlNode node, CargoInstruction[] into)
        {
            CargoSlot slot = CargoSlot.Ironium;
            CargoInstruction instruction = new CargoInstruction();

            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                switch (subnode.Name.ToLowerInvariant())
                {
                    case "slot":
                        slot = (CargoSlot)Enum.Parse(typeof(CargoSlot), subnode.FirstChild.Value);
                        break;
                    case "action":
                        instruction.Action = (CargoAction)Enum.Parse(typeof(CargoAction), subnode.FirstChild.Value);
                        break;
                    case "amount":
                        instruction.Amount = int.Parse(subnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                }

                subnode = subnode.NextSibling;
            }

            into[(int)slot] = instruction;
        }
    }
}
