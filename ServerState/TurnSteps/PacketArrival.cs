#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// A mineral packet reaching its target planet (behavior-specs-10/production-queue.md §10k
    /// item 4, FUN_10b0_0f9a; ship-design-and-components.md "Set, Packet Physics packets"):
    /// <list type="number">
    /// <item>R is the receiving starbase's catch rating (best driver warp, +1 when that rating is
    ///   in two slots; 0 with no driver); the strength R² is halved for an Interstellar Traveler
    ///   receiver; S is the packet's warp.</item>
    /// <item>Caught share (thousandths): 1,000 when strength >= S², 0 with no driver, else
    ///   1,000 x strength / S². Each mineral deposits amount x (caught + uncaught / 9) / 1,000.</item>
    /// <item>A full catch: message 213 to the owner, nothing else.</item>
    /// <item>Otherwise damage = (S² - strength) x total kT / 160, times the share the planet's
    ///   working defenses leave uncovered on an owned planet (Bombing's pass-through F).</item>
    /// <item>No harm (damage 0, or an Alternate Reality owner): 213 with a driver, 326 without; an
    ///   unowned planet gets the deposit and no message.</item>
    /// <item>Harm: no colonists, 385. Otherwise max(P x damage / 1,000, damage) population units
    ///   die (P in hundreds); at the whole population the planet is wiped (218). If not, defenses
    ///   lost are max(D x damage / 1,000, damage / 20), or one with chance damage / 20 when both
    ///   are 0, never more than built. Messages 214/215 (driver), 216/217 (none).</item>
    /// </list>
    /// When the starbase carries a mass driver and the packet's owner is Packet Physics, the owner
    /// learns that starbase's design. The Packet Physics terraform-on-arrival chance (production-
    /// queue.md §10m, messages 305-308) runs after the deposit and before the damage, for any
    /// planet owned or not, whenever the sender is Packet Physics and the packet is not fully
    /// caught.
    /// </summary>
    public static class PacketArrival
    {
        /// <summary>
        /// Resolves the arrival of <paramref name="packet"/> at its target. The caller removes the
        /// packet from the table. <paramref name="random"/> draws the one-in-20 defense loss.
        /// </summary>
        public static void Arrive(ServerData serverState, MineralPacket packet, Random random)
        {
            if (packet == null || string.IsNullOrEmpty(packet.TargetName)
                || !serverState.AllStars.TryGetValue(packet.TargetName, out Star star))
            {
                return;
            }

            bool owned = star.Owner != Global.Nobody;
            EmpireData ownerData = null;
            if (owned)
            {
                serverState.AllEmpires.TryGetValue(star.Owner, out ownerData);
            }

            Race ownerRace = ownerData?.Race ?? star.ThisRace;
            int rating = MineralPacketRules.LaunchRating(star.Starbase);
            bool hasDriver = rating > 0;
            int strength = MineralPacketRules.CatchStrength(rating, owned && ownerRace != null && ownerRace.HasTrait("IT"));
            int speed = packet.Warp;

            LearnStarbaseDesign(serverState, packet, star, hasDriver);

            int caught = MineralPacketRules.CaughtShare(rating, strength, speed);
            int depositShare = MineralPacketRules.DepositShare(caught);
            int totalKilotons = packet.TotalKilotons;

            int ironium = MineralPacketRules.Deposit(packet.Minerals.Ironium, depositShare);
            int boranium = MineralPacketRules.Deposit(packet.Minerals.Boranium, depositShare);
            int germanium = MineralPacketRules.Deposit(packet.Minerals.Germanium, depositShare);
            if (star.ResourcesOnHand == null)
            {
                star.ResourcesOnHand = new Resources();
            }

            star.ResourcesOnHand.Ironium += ironium;
            star.ResourcesOnHand.Boranium += boranium;
            star.ResourcesOnHand.Germanium += germanium;
            int deposited = ironium + boranium + germanium;

            if (caught >= 1000)
            {
                // Message 213.
                PacketLaunch.Post(serverState, star.Owner, "The mass driver at " + star.Name + " has caught a mineral packet of "
                    + deposited + "kT of minerals.");
                return;
            }

            // Packet Physics terraforming on arrival (production-queue.md 10m): after the deposit,
            // before the damage, when the sender is Packet Physics and the packet was not fully
            // caught. It applies to any planet, owned or not.
            ApplyPacketTerraforming(serverState, packet, star, caught, random);

            int damage = MineralPacketRules.RawDamage(speed, strength, totalKilotons);

            if (!owned)
            {
                return;
            }

            damage = (int)(damage * UncoveredShare(star, ownerData));

            if (damage <= 0 || (ownerRace != null && ownerRace.HasTrait("AR")))
            {
                if (hasDriver)
                {
                    // Message 213.
                    PacketLaunch.Post(serverState, star.Owner, "The mass driver at " + star.Name + " has caught a mineral packet; "
                        + deposited + "kT of minerals were recovered.");
                }
                else
                {
                    // Message 326.
                    PacketLaunch.Post(serverState, star.Owner, star.Name + " was bombarded by a mineral packet; no harm was done and "
                        + deposited + "kT of minerals were recovered.");
                }

                return;
            }

            if (star.Colonists <= 0)
            {
                // Message 385.
                PacketLaunch.Post(serverState, star.Owner, star.Name + " was struck by a mineral packet; there were no colonists to harm.");
                return;
            }

            int populationUnits = star.Colonists / Global.ColonistsPerKiloton;
            int killedUnits = MineralPacketRules.ColonistUnitsKilled(populationUnits, damage);
            if (killedUnits >= populationUnits)
            {
                // Message 218: the planet is wiped (FUN_1048_56fc, as for bombing).
                PacketLaunch.Post(serverState, star.Owner, "A mineral packet has struck " + star.Name
                    + ", killing all of your colonists there.");
                ownerData?.OwnedStars.Remove(star);
                star.ManufacturingQueue.Clear();
                star.Colonists = 0;
                star.Mines = 0;
                star.Factories = 0;
                star.Owner = Global.Nobody;
                return;
            }

            star.Colonists -= killedUnits * Global.ColonistsPerKiloton;

            int defenses = Math.Max(0, star.Defenses);
            int defensesLost = MineralPacketRules.DefensesDestroyed(defenses, damage);
            if (defensesLost == 0 && defenses > 0 && (damage >= 20 || random.Next(20) < damage))
            {
                defensesLost = 1;
            }

            defensesLost = Math.Min(defensesLost, defenses);
            star.Defenses = defenses - defensesLost;

            int colonistsKilled = killedUnits * Global.ColonistsPerKiloton;
            string text;
            if (hasDriver)
            {
                // Messages 214 (colonists only) / 215 (colonists and defenses).
                text = "The mass driver at " + star.Name + " only partly captured a mineral packet ("
                    + deposited + "kT recovered). " + colonistsKilled + " colonists were killed"
                    + (defensesLost > 0 ? " and " + defensesLost + " defenses were destroyed." : ".");
            }
            else
            {
                // Messages 216 (colonists only) / 217 (colonists and defenses).
                text = star.Name + " was bombarded by a mineral packet (" + deposited + "kT recovered). "
                    + colonistsKilled + " colonists were killed"
                    + (defensesLost > 0 ? " and " + defensesLost + " defenses were destroyed." : ".");
            }

            PacketLaunch.Post(serverState, star.Owner, text);
        }

        /// <summary>
        /// Packet Physics terraforming on arrival (behavior-specs-11/production-queue.md 10m,
        /// FUN_10b0_0f9a): when the sender is Packet Physics and the packet was not fully caught,
        /// each uncaught mineral amount drives one environment axis (Ironium gravity, Boranium
        /// temperature, Germanium radiation), each handled completely before the next. Per 100 kT
        /// chunk a 0-199 roll below the chunk size scores an ordinary hit and a further 0-9 roll of
        /// 0 makes it permanent. Permanent hits move the planet's ORIGINAL value towards the
        /// sender's ideal (or, when the sender is immune, towards the nearer end); ordinary hits
        /// then move the CURRENT value towards the sender's own terraform target (or, when immune,
        /// by half the hits towards the nearer end). Messages 305/307 go to the sender; per the
        /// spec's reimplementation note their owner-addressed twins 306/308 go to the planet owner.
        /// </summary>
        private static void ApplyPacketTerraforming(ServerData serverState, MineralPacket packet, Star star, int caught, Random random)
        {
            if (!serverState.AllEmpires.TryGetValue(packet.Owner, out EmpireData sender)
                || sender.Race == null || !sender.Race.HasTrait("PP"))
            {
                return;
            }

            TerraformReach reach = TerraformReach.For(sender);
            int[] minerals = { packet.Minerals.Ironium, packet.Minerals.Boranium, packet.Minerals.Germanium };

            for (int axis = 0; axis < TerraformReach.AxisCount; axis++)
            {
                int uncaught = minerals[axis] * (1000 - caught) / 1000;
                if (uncaught <= 0)
                {
                    continue;
                }

                int hits = 0;
                int permanent = 0;
                int remaining = uncaught;
                while (remaining > 0)
                {
                    int chunk = Math.Min(remaining, 100);
                    if (random.Next(200) < chunk)
                    {
                        hits++;
                        if (random.Next(10) == 0)
                        {
                            permanent++;
                        }
                    }

                    remaining -= chunk;
                }

                EnvironmentTolerance tolerance = Tolerance(sender.Race, axis);
                if (tolerance == null)
                {
                    continue;
                }

                int ideal = tolerance.OptimumLevel;

                if (permanent > 0)
                {
                    int original = OriginalValue(star, axis);
                    int moved;
                    if (tolerance.Immune)
                    {
                        int direction = original < 50 ? -1 : 1;
                        moved = Math.Max(1, Math.Min(99, original + (direction * permanent)));
                    }
                    else
                    {
                        moved = ideal > original ? Math.Min(ideal, original + permanent)
                            : ideal < original ? Math.Max(ideal, original - permanent)
                            : original;
                    }

                    if (moved != original)
                    {
                        SetOriginalValue(star, axis, moved);
                        PostTerraform(serverState, packet, star, axis, permanentChange: true);
                    }
                }

                if (hits > 0)
                {
                    int original = OriginalValue(star, axis);
                    int current = CurrentValue(star, axis);
                    int moved = current;

                    if (tolerance.Immune)
                    {
                        int direction = original < 50 ? -1 : 1;
                        moved = Math.Max(1, Math.Min(99, current + (direction * (hits / 2))));
                    }
                    else
                    {
                        int target = TerraformProductionUnit.AxisTarget(sender.Race, axis, original, reach[axis]);
                        if (target >= 0)
                        {
                            if (target > current)
                            {
                                moved = Math.Min(target, current + hits);
                            }
                            else if (target < current)
                            {
                                moved = Math.Max(target, current - hits);
                            }
                        }
                    }

                    if (moved != current)
                    {
                        SetCurrentValue(star, axis, moved);
                        PostTerraform(serverState, packet, star, axis, permanentChange: false);
                    }
                }
            }
        }

        private static void PostTerraform(ServerData serverState, MineralPacket packet, Star star, int axis, bool permanentChange)
        {
            string axisName = axis == TerraformReach.GravityAxis ? "gravity"
                : axis == TerraformReach.TemperatureAxis ? "temperature"
                : "radiation";
            string text = "A Packet Physics mineral packet " + (permanentChange ? "permanently " : string.Empty)
                + "changed " + star.Name + "'s " + axisName + ".";
            PacketLaunch.Post(serverState, packet.Owner, text);
            if (star.Owner != Global.Nobody && star.Owner != packet.Owner)
            {
                PacketLaunch.Post(serverState, star.Owner, text);
            }
        }

        private static EnvironmentTolerance Tolerance(Race race, int axis)
        {
            if (race == null)
            {
                return null;
            }

            switch (axis)
            {
                case TerraformReach.GravityAxis: return race.GravityTolerance;
                case TerraformReach.TemperatureAxis: return race.TemperatureTolerance;
                default: return race.RadiationTolerance;
            }
        }

        private static int CurrentValue(Star star, int axis)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: return star.Gravity;
                case TerraformReach.TemperatureAxis: return star.Temperature;
                default: return star.Radiation;
            }
        }

        private static int OriginalValue(Star star, int axis)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: return star.OriginalGravity;
                case TerraformReach.TemperatureAxis: return star.OriginalTemperature;
                default: return star.OriginalRadiation;
            }
        }

        private static void SetCurrentValue(Star star, int axis, int value)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: star.Gravity = value; break;
                case TerraformReach.TemperatureAxis: star.Temperature = value; break;
                default: star.Radiation = value; break;
            }
        }

        private static void SetOriginalValue(Star star, int axis, int value)
        {
            switch (axis)
            {
                case TerraformReach.GravityAxis: star.OriginalGravity = value; break;
                case TerraformReach.TemperatureAxis: star.OriginalTemperature = value; break;
                default: star.OriginalRadiation = value; break;
            }
        }

        /// <summary>
        /// The share of the planet its working defenses leave uncovered (FUN_1038_0262): Bombing's
        /// normal pass-through F = (1 - c / 1000) ^ K, 1 with no defenses or no defense technology.
        /// </summary>
        public static double UncoveredShare(Star star, EmpireData owner)
        {
            int coverage = Bombing.DefenceCoverage(star, owner);
            int working = Bombing.WorkingDefences(star);
            if (coverage <= 0 || working <= 0)
            {
                return 1.0;
            }

            return Math.Pow(1.0 - (coverage / 1000.0), working);
        }

        /// <summary>
        /// The design-reveal rule for Packet Physics (ship-design-and-components.md, design record
        /// +0x8b, "Set, Packet Physics packets"): a packet reaching a planet whose starbase carries
        /// a mass driver reveals that starbase's design, in full, to a Packet Physics owner.
        /// </summary>
        private static void LearnStarbaseDesign(ServerData serverState, MineralPacket packet, Star star, bool hasDriver)
        {
            if (!hasDriver || star.Starbase == null || star.Owner == packet.Owner || star.Owner == Global.Nobody)
            {
                return;
            }

            if (!serverState.AllEmpires.TryGetValue(packet.Owner, out EmpireData sender)
                || sender.Race == null || !sender.Race.HasTrait("PP")
                || !sender.EmpireReports.TryGetValue(star.Owner, out EmpireIntel intel))
            {
                return;
            }

            foreach (ShipToken token in star.Starbase.Composition.Values)
            {
                if (token.Design == null)
                {
                    continue;
                }

                // The copy constructor needs an icon; a design without one is recorded as is.
                ShipDesign learned = token.Design.Icon != null ? new ShipDesign(token.Design) : token.Design;
                learned.Key = token.Design.Key;
                intel.Designs[learned.Key] = learned;
            }
        }
    }
}
