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

    using Nova.Common;

    /// <summary>One packet production item the AI queues: a mineral and a unit count.</summary>
    public struct AiPacketOrder
    {
        public AiPacketOrder(PacketMineral mineral, int count)
        {
            Mineral = mineral;
            Count = count;
        }

        public PacketMineral Mineral { get; }

        public int Count { get; }
    }

    /// <summary>
    /// The arithmetic of the AI's mineral-packet rules (behavior-specs-10/ai-opponent-behavior.md
    /// §6 shared advisor `FUN_1090_4d10`, §12 personality 4 `FUN_10a8_123e` with its border
    /// chooser `FUN_10a8_1aec`, §12 personality 5's planet-pass packets, §16 packet word), kept
    /// free of AI state so the tests can assert the spec's numbers directly.
    /// </summary>
    public static class AiPacketRules
    {
        // ------------------------------------------------------------------ §6 shared advisor

        public const int SharedMinimumSkill = 2;
        public const int SharedMineralTotalAbove = 3000;
        public const int SharedMinimumLaunchRating = 10;
        public const int SharedRollSides = 4;
        public const int SharedReportMaxAge = 2;
        public const int SharedCoarseDefenceBelow = 14;
        public const int SharedFigureBelow = 750;
        public const int SharedRichMineralAbove = 12500;
        public const double SharedRichRangeFactor = 3;

        /// <summary>The speed the shared advisor writes into the planet's packet settings,
        /// whatever the driver: field 9 = warp 13 (ai-opponent-behavior.md §6).</summary>
        public const int SharedPacketWarp = 13;

        /// <summary>The advisor's squared range indexed by the doubled-driver flag, NOT by the warp
        /// or launch rating (ai-opponent-behavior.md §6): 7,056 (84 ly) when the best driver warp
        /// sits in one starbase slot, 50,625 (225 ly) when it appears in two.</summary>
        public const int SharedRangeSquaredSingleDriver = 7056;

        public const int SharedRangeSquaredDoubledDriver = 50625;

        /// <summary>The squared-distance limit: by the doubled-driver flag, three times larger when
        /// any of the planet's three surface minerals exceeds 12,500 kT (ai-opponent-behavior.md
        /// §6; the 12,500 test reads the surface stock alone).</summary>
        public static double SharedRangeSquared(bool doubledDriver, bool anySurfaceMineralAbove12500)
        {
            double range = doubledDriver ? SharedRangeSquaredDoubledDriver : SharedRangeSquaredSingleDriver;
            return anySurfaceMineralAbove12500 ? range * SharedRichRangeFactor : range;
        }

        /// <summary>
        /// The advisor's gates before its 1-in-4 roll: skill 2 or more, no packet item (types
        /// 14-17) queued yet, projected Ironium + Boranium + Germanium above 3,000 kT, and a
        /// starbase best mass-driver warp of at least 10 (the plain best warp, without the +1 for
        /// a doubled driver).
        /// </summary>
        public static bool SharedAdvisorGates(int skill, bool packetItemQueued, Resources projected, int bestDriverWarp)
        {
            return skill >= SharedMinimumSkill
                && !packetItemQueued
                && (long)projected.Ironium + projected.Boranium + projected.Germanium > SharedMineralTotalAbove
                && bestDriverWarp >= SharedMinimumLaunchRating;
        }

        /// <summary>The target test's report fields: coarse defence below 14 or reported figure
        /// below 750.</summary>
        public static bool SharedTargetWeakEnough(int coarseDefence, int reportedFigure)
        {
            return coarseDefence < SharedCoarseDefenceBelow || reportedFigure < SharedFigureBelow;
        }

        /// <summary>
        /// What the advisor queues (§6): if Germanium exceeds 20,000 kT, two times in three
        /// (a 0-2 roll below 2), 80 Germanium packets; then 30 mixed packets when Ironium,
        /// Boranium and Germanium exceed 3,000, 4,000 and 3,000 kT; else 15 mixed when they
        /// exceed 1,500, 2,250 and 1,500 kT; else, for each mineral above 1,250 kT (2,500 for
        /// Boranium), the excess ÷ 200 packets of that mineral, at least 1 and at most 25.
        /// </summary>
        public static List<AiPacketOrder> SharedOrders(Resources projected, Random random)
        {
            List<AiPacketOrder> orders = new List<AiPacketOrder>();
            int ironium = projected.Ironium;
            int boranium = projected.Boranium;
            int germanium = projected.Germanium;

            if (germanium > 20000 && random.Next(3) < 2)
            {
                orders.Add(new AiPacketOrder(PacketMineral.Germanium, 80));
            }

            if (ironium > 3000 && boranium > 4000 && germanium > 3000)
            {
                orders.Add(new AiPacketOrder(PacketMineral.Mixed, 30));
            }
            else if (ironium > 1500 && boranium > 2250 && germanium > 1500)
            {
                orders.Add(new AiPacketOrder(PacketMineral.Mixed, 15));
            }
            else
            {
                AddExcess(orders, PacketMineral.Ironium, ironium, 1250);
                AddExcess(orders, PacketMineral.Boranium, boranium, 2500);
                AddExcess(orders, PacketMineral.Germanium, germanium, 1250);
            }

            return orders;
        }

        private static void AddExcess(List<AiPacketOrder> orders, PacketMineral mineral, int amount, int floor)
        {
            if (amount > floor)
            {
                orders.Add(new AiPacketOrder(mineral, Math.Max(1, Math.Min(25, (amount - floor) / 200))));
            }
        }

        // ------------------------------------------------------- §12 personality 5 planet pass

        /// <summary>Only after this year counter.</summary>
        public const int MacintiAfterYear = 120;
        public const int MacintiRollSides = 4;
        public const int MacintiMinimumDriverWarp = 10;
        public const int MacintiPopulationAbove = 10000;
        public const int MacintiStockAbove = 5000;
        public const int MacintiShareDivisor = 5;
        public const int MacintiMaxShipped = 20000;
        public const int MacintiKilotonsPerPacket = 100;

        /// <summary>
        /// "within about 160 ly": read as a squared distance of at most 160² = 25,600 (the spec
        /// gives no exact squared constant here - Ambiguity, see report).
        /// </summary>
        public const double MacintiRangeSquared = 160.0 * 160.0;

        /// <summary>
        /// The mineral personality 5 ships and how much: the first of Ironium, Boranium,
        /// Germanium (Ambiguity: the spec names no order when several qualify) held above
        /// 5,000 kT; a fifth of that stock, at most 20,000 kT. False when no mineral qualifies.
        /// </summary>
        public static bool MacintiShipment(Resources stock, out PacketMineral mineral, out int amount)
        {
            int[] held = { stock.Ironium, stock.Boranium, stock.Germanium };
            PacketMineral[] minerals = { PacketMineral.Ironium, PacketMineral.Boranium, PacketMineral.Germanium };
            for (int index = 0; index < 3; index++)
            {
                if (held[index] > MacintiStockAbove)
                {
                    mineral = minerals[index];
                    amount = Math.Min(held[index] / MacintiShareDivisor, MacintiMaxShipped);
                    return true;
                }
            }

            mineral = PacketMineral.Mixed;
            amount = 0;
            return false;
        }

        /// <summary>The packet count: the amount ÷ 100.</summary>
        public static int MacintiPacketCount(int amount)
        {
            return amount / MacintiKilotonsPerPacket;
        }

        // ------------------------------------------------------------ §12 personality 4

        public const int HubSourceAbove = 700;
        public const int HubMinimumPackets = 7;
        public const int HubMaxPacketsPerMineral = 7;
        public const int HubShortBelow = 10;
        public const int OtherStarbaseShortBelow = 1000;
        public const double HubRangeYears = 3.5;
        public const int AttackSurplusAllowance = 210;
        public const int AttackSurplusAtMost = 150;
        public const int AttackBudgetPerPacket = 70;
        public const double AttackRangeYears = 2.5;
        public const int AttackPacketKilotons = 70;
        public const int CoolDownTurns = 3;
        public const int FallbackSurplusAbove = 169;
        public const int SkillOneRollSides = 3;

        /// <summary>The packet-hub starbase slots (§12: slot 1, 3, 6 or 8).</summary>
        public static bool IsPacketHubSlot(int slot)
        {
            return slot == 1 || slot == 3 || slot == 6 || slot == 8;
        }

        /// <summary>The shortage-flag threshold: below 10 kT at a hub, below 1,000 kT at any
        /// other starbase planet.</summary>
        public static int ShortBelow(bool hub)
        {
            return hub ? HubShortBelow : OtherStarbaseShortBelow;
        }

        /// <summary>Hub supply's n = R ÷ 2 ÷ 5, rounded down at each step.</summary>
        public static int HubPacketBudget(int resources)
        {
            return resources / 2 / 5;
        }

        /// <summary>S, the three mineral surpluses summed, minus 210 kT.</summary>
        public static long AttackSurplus(Resources surplus)
        {
            return (long)surplus.Ironium + surplus.Boranium + surplus.Germanium - AttackSurplusAllowance;
        }

        /// <summary>The budget: the smaller of S and 70 × ((R ÷ 2 − 5) ÷ 5, rounded down).</summary>
        public static long AttackBudget(long surplusS, int resources)
        {
            long perPacket = FloorDiv((resources / 2) - 5, 5);
            return Math.Min(surplusS, AttackBudgetPerPacket * perPacket);
        }

        /// <summary>q, the share surviving one year at the attack's overspeed: 0.75, or 0.875
        /// when the driver is doubled (DGROUP 0x1e7e, 0x1e76).</summary>
        public static double SurvivingShare(bool doubled)
        {
            return doubled ? 0.875 : 0.75;
        }

        /// <summary>1 ÷ q: 4/3, or 8/7 when the driver is doubled (DGROUP 0x1e4e, 0x1e46).</summary>
        public static double InverseSurvivingShare(bool doubled)
        {
            return doubled ? 8.0 / 7.0 : 4.0 / 3.0;
        }

        /// <summary>
        /// K, the mass needed: min(4 × (f + 25), 1,000) ÷ ((s² − r²) × (95 − c) × 6.25 × 10⁻⁵),
        /// truncated. The product (s² − r²) × (95 − c) is formed in 16-bit unsigned arithmetic, so
        /// it wraps when r exceeds s. A product of zero gives int.MaxValue (no budget covers it).
        /// </summary>
        public static int MassNeeded(int reportedFigure, int targetRating, int speed, int coarseDefence)
        {
            int population = Math.Min(4 * (reportedFigure + 25), 1000);
            ushort product = unchecked((ushort)(((speed * speed) - (targetRating * targetRating)) * (95 - coarseDefence)));
            if (product == 0)
            {
                return int.MaxValue;
            }

            double mass = population / (product * 6.25e-5);
            return mass >= int.MaxValue ? int.MaxValue : (int)Math.Truncate(mass);
        }

        /// <summary>The budget left after the flight's decay: budget × q^(d ÷ s²), truncated.</summary>
        public static long BudgetAfterDecay(long budget, double surviving, double distance, int speed)
        {
            return (long)Math.Truncate(budget * Math.Pow(surviving, distance / (speed * speed)));
        }

        /// <summary>M = min(S, K) × (1 ÷ q)^(d ÷ s²), truncated.</summary>
        public static long AmountSent(long surplusS, int massNeeded, double inverseSurviving, double distance, int speed)
        {
            return (long)Math.Truncate(Math.Min(surplusS, massNeeded) * Math.Pow(inverseSurviving, distance / (speed * speed)));
        }

        /// <summary>
        /// Splits M into 70-kT single-mineral packets: each takes the mineral with the largest
        /// remaining surplus (ties to the earlier of Ironium, Boranium, Germanium) and lowers it by
        /// 70, while any of M is left, so M ÷ 70 packets rounded up. Returns the counts for
        /// Ironium, Boranium and Germanium.
        /// </summary>
        public static int[] SplitIntoPackets(long amount, Resources surplus)
        {
            long[] left = { surplus.Ironium, surplus.Boranium, surplus.Germanium };
            int[] counts = new int[3];
            for (long remaining = amount; remaining > 0; remaining -= AttackPacketKilotons)
            {
                int best = 0;
                for (int index = 1; index < 3; index++)
                {
                    if (left[index] > left[best])
                    {
                        best = index;
                    }
                }

                counts[best]++;
                left[best] -= AttackPacketKilotons;
            }

            return counts;
        }

        /// <summary>The fallback's new direction index: a 0-6 roll, plus 1 when it equals the
        /// stored index, so 0-7 and never the same twice running.</summary>
        public static int NextDirectionIndex(int roll, int storedIndex)
        {
            return roll == storedIndex ? roll + 1 : roll;
        }

        /// <summary>The chooser's frame side L = (galaxy-size index + 1) × 400.</summary>
        public static int FrameSide(int galaxySizeIndex)
        {
            return (galaxySizeIndex + 1) * 400;
        }

        /// <summary>
        /// The border point (§12 `FUN_10a8_1aec`): where the ray from (x, y) in direction
        /// <paramref name="index"/> (0 = +x, turning from +x towards −y in 45° steps) meets the
        /// edge of the square 0..L.
        /// </summary>
        public static void BorderPoint(int index, int x, int y, int side, out int px, out int py)
        {
            switch (index)
            {
                case 0:
                    px = side; py = y;
                    break;
                case 1:
                    if (x + y < side) { px = x + y; py = 0; } else { px = side; py = x + y - side; }
                    break;
                case 2:
                    px = x; py = 0;
                    break;
                case 3:
                    if (x > y) { px = x - y; py = 0; } else { px = 0; py = y - x; }
                    break;
                case 4:
                    px = 0; py = y;
                    break;
                case 5:
                    if (x + y < side) { px = 0; py = x + y; } else { px = x + y - side; py = side; }
                    break;
                case 6:
                    px = x; py = side;
                    break;
                default:
                    if (x > y) { px = side; py = y + side - x; } else { px = x + side - y; py = side; }
                    break;
            }
        }

        /// <summary>The slide's range: j runs from −⌊0.15 L⌋ to ⌊0.3 L⌋ − ⌊0.15 L⌋ − 1, i.e. a
        /// draw below ⌊0.3 L⌋ minus ⌊0.15 L⌋.</summary>
        public static int SlideDrawRange(int side)
        {
            return (int)Math.Floor(0.3 * side);
        }

        /// <summary>The slide's offset subtracted from the draw: ⌊0.15 L⌋.</summary>
        public static int SlideOffset(int side)
        {
            return (int)Math.Floor(0.15 * side);
        }

        /// <summary>
        /// Slides the border point j along its edge: on a vertical edge (x = 0 or L) along y,
        /// otherwise along x. An overshoot past a corner is clamped to the corner and the excess
        /// carried round onto the adjacent edge, measured inward from the corner.
        /// </summary>
        public static void Slide(int j, int side, ref int px, ref int py)
        {
            if (px == 0 || px == side)
            {
                int ny = py + j;
                int excess = ny > side ? ny - side : ny < 0 ? -ny : 0;
                excess = Math.Min(excess, side);
                if (ny > side || ny < 0)
                {
                    py = ny > side ? side : 0;
                    px = px == 0 ? excess : side - excess;
                }
                else
                {
                    py = ny;
                }
            }
            else
            {
                int nx = px + j;
                int excess = nx > side ? nx - side : nx < 0 ? -nx : 0;
                excess = Math.Min(excess, side);
                if (nx > side || nx < 0)
                {
                    px = nx > side ? side : 0;
                    py = py == 0 ? excess : side - excess;
                }
                else
                {
                    px = nx;
                }
            }
        }

        /// <summary>Moves the point k straight in from its edge: from y = 0 or y = L if it lies on
        /// one (so a corner moves along y), otherwise from x = 0 or x = L.</summary>
        public static void StepInward(int k, int side, ref int px, ref int py)
        {
            if (py == 0)
            {
                py = Math.Min(side, k);
            }
            else if (py == side)
            {
                py = Math.Max(0, side - k);
            }
            else if (px == 0)
            {
                px = Math.Min(side, k);
            }
            else if (px == side)
            {
                px = Math.Max(0, side - k);
            }
        }

        /// <summary>
        /// The chooser's distance test with its quirk: the squared distance is compared with s⁴
        /// formed in 16-bit arithmetic and sign-extended, so from s = 14 a negative (or small)
        /// threshold lets every non-owned nearest object pass.
        /// </summary>
        public static bool ChooserDistancePasses(double distanceSquared, int chooserSpeed)
        {
            short threshold = unchecked((short)(chooserSpeed * chooserSpeed * chooserSpeed * chooserSpeed));
            return distanceSquared >= threshold;
        }

        private static long FloorDiv(long value, long divisor)
        {
            long quotient = value / divisor;
            return (value % divisor != 0 && (value < 0) != (divisor < 0)) ? quotient - 1 : quotient;
        }
    }

    /// <summary>
    /// Personality 4's per-planet packet word (§16): bits 0-2 the fallback direction index,
    /// bit 3 the colony alternation (kept by the planet pass, not here), bit 4 the fallback rest
    /// flag, bits 5-6 the target cool-down, bit 7 the follow-up flag.
    /// </summary>
    public static class CybertronPacketWord
    {
        public const int RestBit = 0x10;
        public const int FollowUpBit = 0x80;

        public static int DirectionIndex(int word)
        {
            return word & 0x7;
        }

        public static int WithDirectionIndex(int word, int index)
        {
            return (word & ~0x7) | (index & 0x7);
        }

        public static int CoolDown(int word)
        {
            return (word >> 5) & 0x3;
        }

        public static int WithCoolDown(int word, int coolDown)
        {
            return (word & ~0x60) | ((coolDown & 0x3) << 5);
        }

        public static bool Has(int word, int bit)
        {
            return (word & bit) != 0;
        }

        public static int With(int word, int bit, bool set)
        {
            return set ? word | bit : word & ~bit;
        }
    }
}
