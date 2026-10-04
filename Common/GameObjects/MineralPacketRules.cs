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

namespace Nova.Common
{
    using System;

    using Nova.Common.Components;

    /// <summary>The mineral a packet item carries: all three (the mixed packet) or just one.</summary>
    public enum PacketMineral
    {
        Mixed,
        Ironium,
        Boranium,
        Germanium
    }

    /// <summary>
    /// The pure arithmetic of mineral packets (behavior-specs-10/production-queue.md §10, §10a,
    /// §10b and §10k item 4), kept free of server state so the production unit, the server steps
    /// and the tests share one copy.
    /// </summary>
    public static class MineralPacketRules
    {
        /// <summary>Each mineral in a packet is capped at 32,760 kT (§10b, §10g).</summary>
        public const int MaxKilotonsPerMineral = 32760;

        /// <summary>A new launch merges into an existing packet only while the merged total stays
        /// under 16,300 kT (§10b).</summary>
        public const int MergeMassLimit = 16300;

        /// <summary>The lowest packet speed a planet may choose (§10b: 5..(best driver warp + 3)).</summary>
        public const int MinimumChosenSpeed = 5;

        /// <summary>A chosen speed may exceed the best driver's warp by at most 3 (§10b).</summary>
        public const int MaximumSpeedAboveDriver = 3;

        /// <summary>The overspeed class is capped at 3 (§10b).</summary>
        public const int MaxOverspeedClass = 3;

        /// <summary>Per-year in-flight loss by overspeed class 0-3, in percent (§10b).</summary>
        private static readonly int[] DecayPercentByClass = { 0, 10, 25, 50 };

        /// <summary>Minimum in-flight loss per mineral per year: 10 kT, 5 kT for Packet Physics.</summary>
        public const int MinimumDecayKilotons = 10;
        public const int MinimumDecayKilotonsPacketPhysics = 5;

        /// <summary>Damage divisor: (S² - strength) x total kT / 160 (§10k item 4).</summary>
        public const int DamageDivisor = 160;

        // ---------------------------------------------------------------------------------
        // Mass drivers.
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The warp rating of the best mass driver installed on a starbase (any occupied slot
        /// whose component has a "Mass Driver" property), or 0 with no starbase or no driver.
        /// </summary>
        public static int BestDriverWarp(Fleet starbase)
        {
            int best;
            int slotsAtBest;
            ScanDrivers(starbase, out best, out slotsAtBest);
            return best;
        }

        /// <summary>
        /// The launch (and catch) rating, FUN_1048_5138: the best driver's warp, plus one if that
        /// best rating appears in two different starbase slots; 0 with no driver
        /// (production-queue.md §10b, §10k item 4). Several drivers in ONE slot do not add.
        /// </summary>
        public static int LaunchRating(Fleet starbase)
        {
            int best;
            int slotsAtBest;
            ScanDrivers(starbase, out best, out slotsAtBest);
            if (best <= 0)
            {
                return 0;
            }

            return slotsAtBest >= 2 ? best + 1 : best;
        }

        private static void ScanDrivers(Fleet starbase, out int best, out int slotsAtBest)
        {
            best = 0;
            slotsAtBest = 0;
            if (starbase == null || starbase.Composition == null)
            {
                return;
            }

            foreach (ShipToken token in starbase.Composition.Values)
            {
                ShipDesign design = token?.Design;
                if (design?.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
                {
                    continue;
                }

                Hull hull = design.Hull;
                if (hull?.Modules == null)
                {
                    continue;
                }

                foreach (HullModule module in hull.Modules)
                {
                    if (module?.AllocatedComponent?.Properties == null || module.ComponentCount <= 0)
                    {
                        continue;
                    }

                    if (!module.AllocatedComponent.Properties.TryGetValue("Mass Driver", out ComponentProperty property))
                    {
                        continue;
                    }

                    int warp = property is MassDriver driver ? driver.Value
                        : property is IntegerProperty integer ? integer.Value : 0;

                    if (warp > best)
                    {
                        best = warp;
                        slotsAtBest = 1;
                    }
                    else if (warp == best && warp > 0)
                    {
                        slotsAtBest++;
                    }
                }
            }
        }

        /// <summary>True when the planet's starbase carries a mass driver (an "accelerator").</summary>
        public static bool HasAccelerator(Star star)
        {
            return star != null && LaunchRating(star.Starbase) > 0;
        }

        /// <summary>True when the planet has a packet destination set.</summary>
        public static bool HasTarget(Star star)
        {
            return star != null && !string.IsNullOrEmpty(star.PacketDestination) && star.PacketDestination != star.Name;
        }

        /// <summary>A packet order can be bought: an accelerator and a target (§10a).</summary>
        public static bool CanLaunch(Star star)
        {
            return HasAccelerator(star) && HasTarget(star);
        }

        /// <summary>
        /// The packet's speed (§10b): the planet's chosen packet speed when it lies in
        /// 5..(best driver warp + 3), otherwise the launch rating. "Best driver warp" is read as
        /// the best single driver's warp, without the two-slot bonus (Ambiguity, see report).
        /// </summary>
        public static int LaunchSpeed(int chosenSpeed, int bestDriverWarp, int launchRating)
        {
            if (chosenSpeed >= MinimumChosenSpeed && chosenSpeed <= bestDriverWarp + MaximumSpeedAboveDriver)
            {
                return chosenSpeed;
            }

            return launchRating;
        }

        /// <summary>
        /// The overspeed class (§10b): how far the speed exceeds the driver rating (the launch
        /// rating), 0-3, one more for an Interstellar Traveler owner, capped at 3.
        /// </summary>
        public static int OverspeedClass(int speed, int launchRating, bool interstellarTraveler)
        {
            int exceed = Math.Max(0, speed - launchRating);
            if (interstellarTraveler)
            {
                exceed++;
            }

            return Math.Min(MaxOverspeedClass, exceed);
        }

        // ---------------------------------------------------------------------------------
        // Costs and payloads (production-queue.md §10, types 6, 14-17; §10b).
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The unit cost: a mixed packet 44 kT of each mineral + 10 resources (Packet Physics 25 kT
        /// + 5, Interstellar Traveler 48 kT + 10); a single-mineral packet 110 kT of that mineral
        /// + 10 resources (Packet Physics 70 + 5, Interstellar Traveler 120 + 10).
        /// </summary>
        public static Resources UnitCost(Race race, PacketMineral mineral)
        {
            bool packetPhysics = race != null && race.HasTrait("PP");
            bool interstellarTraveler = race != null && race.HasTrait("IT");
            int resources = packetPhysics ? 5 : 10;

            if (mineral == PacketMineral.Mixed)
            {
                int each = packetPhysics ? 25 : interstellarTraveler ? 48 : 44;
                return new Resources(each, each, each, resources);
            }

            int kilotons = packetPhysics ? 70 : interstellarTraveler ? 120 : 110;
            return Single(mineral, kilotons, resources);
        }

        /// <summary>
        /// What one unit puts into the packet: a mixed packet 40 kT of each mineral (Packet
        /// Physics 25), a single-mineral packet 100 kT (Packet Physics 70) (§10b).
        /// </summary>
        public static Resources UnitPayload(Race race, PacketMineral mineral)
        {
            bool packetPhysics = race != null && race.HasTrait("PP");

            if (mineral == PacketMineral.Mixed)
            {
                int each = packetPhysics ? 25 : 40;
                return new Resources(each, each, each, 0);
            }

            return Single(mineral, packetPhysics ? 70 : 100, 0);
        }

        private static Resources Single(PacketMineral mineral, int kilotons, int resources)
        {
            switch (mineral)
            {
                case PacketMineral.Ironium: return new Resources(kilotons, 0, 0, resources);
                case PacketMineral.Boranium: return new Resources(0, kilotons, 0, resources);
                default: return new Resources(0, 0, kilotons, resources);
            }
        }

        // ---------------------------------------------------------------------------------
        // Decay in flight (production-queue.md §10b, FUN_10b8_4200).
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// One year's in-flight loss for one mineral amount: class 0 loses nothing; classes 1, 2
        /// and 3 lose 10%, 25% and 50%, at least 10 kT; a Packet Physics owner loses half the
        /// percentage, at least 5 kT. Never more than the amount. The percentage is taken rounded
        /// down (amount x percent / 100, or / 200 for Packet Physics - Ambiguity, see report).
        /// </summary>
        public static int DecayLoss(int amount, int overspeedClass, bool packetPhysics)
        {
            if (amount <= 0 || overspeedClass <= 0)
            {
                return 0;
            }

            int percent = DecayPercentByClass[Math.Min(MaxOverspeedClass, overspeedClass)];
            long proportional = (long)amount * percent / (packetPhysics ? 200 : 100);
            int minimum = packetPhysics ? MinimumDecayKilotonsPacketPhysics : MinimumDecayKilotons;
            return (int)Math.Min(amount, Math.Max(proportional, minimum));
        }

        /// <summary>Applies one year's in-flight decay to every mineral of the packet.</summary>
        public static void Decay(MineralPacket packet, bool ownerIsPacketPhysics)
        {
            packet.Minerals.Ironium -= DecayLoss(packet.Minerals.Ironium, packet.OverspeedClass, ownerIsPacketPhysics);
            packet.Minerals.Boranium -= DecayLoss(packet.Minerals.Boranium, packet.OverspeedClass, ownerIsPacketPhysics);
            packet.Minerals.Germanium -= DecayLoss(packet.Minerals.Germanium, packet.OverspeedClass, ownerIsPacketPhysics);
        }

        // ---------------------------------------------------------------------------------
        // Arrival (production-queue.md §10k item 4, FUN_10b0_0f9a).
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// The catch strength: the receiving rating squared, halved (rounded down) when the
        /// receiving planet's owner is an Interstellar Traveler.
        /// </summary>
        public static int CatchStrength(int receivingRating, bool receiverIsInterstellarTraveler)
        {
            int strength = receivingRating * receivingRating;
            return receiverIsInterstellarTraveler ? strength / 2 : strength;
        }

        /// <summary>
        /// The caught share in thousandths: 1,000 when the strength is at least S², 0 with no
        /// driver, otherwise 1,000 x strength / S² rounded down.
        /// </summary>
        public static int CaughtShare(int receivingRating, int strength, int packetWarp)
        {
            int speedSquared = packetWarp * packetWarp;
            if (strength >= speedSquared)
            {
                return 1000;
            }

            if (receivingRating <= 0)
            {
                return 0;
            }

            return (int)(1000L * strength / speedSquared);
        }

        /// <summary>The deposited share in thousandths: the caught share plus one ninth of the
        /// uncaught share, rounded down. 111 for a packet nobody catches.</summary>
        public static int DepositShare(int caughtShare)
        {
            return caughtShare + ((1000 - caughtShare) / 9);
        }

        /// <summary>One mineral's deposit: amount x deposit share / 1,000, rounded down.</summary>
        public static int Deposit(int amount, int depositShare)
        {
            return (int)((long)Math.Max(0, amount) * depositShare / 1000);
        }

        /// <summary>
        /// The raw damage before defenses: (S² minus the strength) x the packet's total kT / 160,
        /// rounded down (the strength counts as 0 without a driver).
        /// </summary>
        public static int RawDamage(int packetWarp, int strength, int totalKilotons)
        {
            long excess = Math.Max(0, (packetWarp * packetWarp) - strength);
            return (int)(excess * Math.Max(0, totalKilotons) / DamageDivisor);
        }

        /// <summary>
        /// Colonists killed, in population units of 100 colonists: the larger of population x
        /// damage / 1,000 and the damage itself (damage x 100 colonists).
        /// </summary>
        public static int ColonistUnitsKilled(int populationUnits, int damage)
        {
            return (int)Math.Max((long)populationUnits * damage / 1000, damage);
        }

        /// <summary>Defenses destroyed (before the one-in-20 fallback and the cap): the larger of
        /// defenses x damage / 1,000 and damage / 20, both rounded down.</summary>
        public static int DefensesDestroyed(int defenses, int damage)
        {
            return (int)Math.Max((long)defenses * damage / 1000, damage / 20);
        }
    }
}
