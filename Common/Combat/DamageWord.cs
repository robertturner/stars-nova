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

namespace Nova.Common.Combat
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A token's packed 16-bit damage word - behavior-specs-10/combat-resolution.md §8 ("The
    /// armor-quantization scheme ... settled by the gap-report pass", FUN_10f0_52c4) and
    /// turn-generation-engine.md §11. The low 7 bits are the percentage of the token's surviving
    /// ships that carry damage; the high 9 bits are the damage EACH damaged ship carries, in
    /// 1/500 of the design's per-ship armor (0-499). A stack never tracks two damage levels.
    /// </summary>
    /// <remarks>
    /// Spec-11 makes the packed word the only damage state (combat-resolution.md §8, "The word is
    /// the only damage state"): the persisted <see cref="ShipToken.PackedDamage"/> is authoritative,
    /// and <see cref="ShipToken.Armor"/> is a pooled value derived from it. <see cref="For"/> reads
    /// the word and <see cref="Store"/> writes it, keeping the pooled armor in sync. A save that
    /// predates the field (or any token built with a pooled armor and no word) is converted on
    /// demand with <see cref="FromPooledDamage"/> - the documented "every ship damaged, figure =
    /// total x 500 / (ships x armor) rounded up, 1..499" conversion. The original spec word also
    /// gives the merge, move-between-fleets and new-ship-join formulas, implemented here so all
    /// three sites share one testable definition.
    /// </remarks>
    public struct DamageWord
    {
        /// <summary>Most 1/500 units a damaged ship can carry (the high 9 bits are clamped to 0-499).</summary>
        public const int MaxUnits = 499;

        /// <summary>The armor quantum: damage is stored in 1/500 of one ship's armor.</summary>
        public const int UnitsPerArmor = 500;

        public DamageWord(int percent, int units)
        {
            Percent = units <= 0 ? 0 : Math.Max(0, Math.Min(100, percent));
            Units = Math.Max(0, Math.Min(MaxUnits, units));
            if (Units == 0)
            {
                Percent = 0;
            }
        }

        /// <summary>Low 7 bits: percentage of the surviving ships that carry damage.</summary>
        public int Percent { get; private set; }

        /// <summary>High 9 bits: damage each damaged ship carries, in 1/500 of its armor.</summary>
        public int Units { get; private set; }

        /// <summary>The packed 16-bit value: units shifted above the 7-bit percentage.</summary>
        public int Packed
        {
            get { return (Units << 7) | Percent; }
        }

        public bool IsUndamaged
        {
            get { return Units == 0; }
        }

        /// <summary>Unpacks a stored 16-bit word.</summary>
        public static DamageWord FromPacked(int packed)
        {
            return new DamageWord(packed & 0x7f, (packed >> 7) & 0x1ff);
        }

        /// <summary>
        /// Unpack, step (1): damaged ships = percent x ships / 100, truncated but at least 1
        /// (none when the units are zero).
        /// </summary>
        public int DamagedShips(int ships)
        {
            if (Units == 0 || ships <= 0)
            {
                return 0;
            }

            return Math.Min(ships, Math.Max(1, Percent * ships / 100));
        }

        /// <summary>
        /// Unpack, step (1): each damaged ship carries units x armor / 500 points, truncated but
        /// at least 1 (zero when undamaged).
        /// </summary>
        public long DamagePerShip(int armorPerShip)
        {
            if (Units == 0)
            {
                return 0;
            }

            return Math.Max(1L, (long)Units * armorPerShip / UnitsPerArmor);
        }

        /// <summary>Total damage points the word represents for a token.</summary>
        public long TotalDamage(int ships, int armorPerShip)
        {
            return DamagedShips(ships) * DamagePerShip(armorPerShip);
        }

        /// <summary>The token's pooled remaining armor under this word.</summary>
        public double PooledArmor(int ships, int armorPerShip)
        {
            return Math.Max(0L, ((long)ships * armorPerShip) - TotalDamage(ships, armorPerShip));
        }

        /// <summary>
        /// Builds a word from a pooled damage total: every ship damaged (100%), the per-ship figure
        /// is <c>totalDamage x 500 / (ships x armor)</c> rounded up, at least 1, at most 499
        /// (combat-resolution.md §8, the reimplementation note). Zero damage gives the zero word.
        /// </summary>
        public static DamageWord FromPooledDamage(long totalDamage, int ships, int armorPerShip)
        {
            if (totalDamage <= 0 || ships <= 0 || armorPerShip <= 0)
            {
                return new DamageWord(0, 0);
            }

            long full = (long)ships * armorPerShip;
            long units = ((totalDamage * UnitsPerArmor) + full - 1) / full;
            return new DamageWord(100, (int)Math.Max(1L, Math.Min(MaxUnits, units)));
        }

        /// <summary>Per-ship damage points to 1/500 units, rounded up, at least 1, capped at 499.</summary>
        public static int ToUnits(long perShipDamage, int armorPerShip)
        {
            if (armorPerShip <= 0)
            {
                return MaxUnits;
            }

            long units = ((perShipDamage * UnitsPerArmor) + armorPerShip - 1) / armorPerShip;
            return (int)Math.Max(1L, Math.Min(MaxUnits, units));
        }

        /// <summary>
        /// The token's current word. The persisted <see cref="ShipToken.PackedDamage"/> is
        /// authoritative (spec-11: "the word is the only damage state"); nothing is re-derived while
        /// a word is stored. With no stored word, a token still carrying a pooled armor below full
        /// (an older save, or a token built by code that set <see cref="ShipToken.Armor"/> directly)
        /// is converted with <see cref="FromPooledDamage"/>.
        /// </summary>
        public static DamageWord For(ShipToken token)
        {
            if (token == null || token.Design == null || token.Quantity <= 0)
            {
                return new DamageWord(0, 0);
            }

            if (token.PackedDamage != 0)
            {
                return FromPacked(token.PackedDamage);
            }

            int armorPerShip = token.Design.Armor;
            long full = (long)token.Quantity * armorPerShip;
            long damage = full - (long)Math.Ceiling(token.Armor);
            return damage > 0 ? FromPooledDamage(damage, token.Quantity, armorPerShip) : new DamageWord(0, 0);
        }

        /// <summary>Stores the word as the token's damage state and sets the pooled armor from it.</summary>
        public static void Store(ShipToken token, DamageWord word)
        {
            if (token == null || token.Design == null)
            {
                return;
            }

            token.Armor = word.PooledArmor(token.Quantity, token.Design.Armor);
            token.PackedDamage = word.Packed;
        }

        /// <summary>
        /// The fleet-merge formula (combat-resolution.md §8, "Merging fleets", FUN_1038_2274): over
        /// every source stack being merged (the target included), the damaged-ship count
        /// <c>D = sum of max(1, percent x ships / 100)</c> and the damage units
        /// <c>U = sum of those counts x each source's per-ship figure</c>. The merged word is
        /// percent <c>ceil(D x 100 / N)</c> (N = the merged ship count) and figure
        /// <c>floor(U / D)</c>, or zero when D is zero.
        /// </summary>
        public static DamageWord Merge(int mergedShips, IEnumerable<KeyValuePair<int, DamageWord>> sources)
        {
            long damaged = 0;
            long units = 0;
            foreach (KeyValuePair<int, DamageWord> source in sources)
            {
                DamageWord word = source.Value;
                if (source.Key <= 0 || word.IsUndamaged)
                {
                    continue;
                }

                int count = Math.Max(1, word.Percent * source.Key / 100);
                damaged += count;
                units += (long)count * word.Units;
            }

            if (damaged == 0 || mergedShips <= 0)
            {
                return new DamageWord(0, 0);
            }

            int percent = (int)((damaged * 100 + mergedShips - 1) / mergedShips);
            return new DamageWord(percent, (int)Math.Min(MaxUnits, units / damaged));
        }

        /// <summary>
        /// Moving <paramref name="moving"/> ships from a giver to a receiver, per design
        /// (combat-resolution.md §8, "Moving ships between two fleets", FUN_1050_6e52). Splitting a
        /// new fleet off is the same operation with a fresh, empty receiver. With d_g and d_r the
        /// two stacks' damaged-ship counts (percent x old count / 100, truncated, no minimum) and
        /// <c>m = min(d_g, moving)</c>, damaged ships move first; every percentage is rounded up
        /// against the stack's new ship count. The four receiver cases and the giver's clearing rule
        /// are the spec's.
        /// </summary>
        public static void MoveShips(
            DamageWord giver, int oldGiverShips,
            DamageWord receiver, int oldReceiverShips,
            int moving,
            out DamageWord newGiver, out DamageWord newReceiver)
        {
            if (moving <= 0)
            {
                newGiver = giver;
                newReceiver = receiver;
                return;
            }

            int newGiverShips = oldGiverShips - moving;
            int newReceiverShips = oldReceiverShips + moving;
            int giverDamaged = giver.Percent * oldGiverShips / 100;
            int receiverDamaged = receiver.Percent * oldReceiverShips / 100;
            int movedDamaged = Math.Min(giverDamaged, moving);

            if (giverDamaged == 0 && receiverDamaged > 0)
            {
                newReceiver = new DamageWord(CeilPercent(receiverDamaged, newReceiverShips), receiver.Units);
            }
            else if (giverDamaged == 0)
            {
                newReceiver = new DamageWord(0, 0);
            }
            else if (receiverDamaged == 0)
            {
                newReceiver = new DamageWord(CeilPercent(movedDamaged, newReceiverShips), giver.Units);
            }
            else
            {
                int units = (int)Math.Min(
                    MaxUnits,
                    ((long)receiver.Units * receiverDamaged + (long)giver.Units * movedDamaged + newReceiverShips - 1)
                        / newReceiverShips);
                newReceiver = new DamageWord(CeilPercent(receiverDamaged + movedDamaged, newReceiverShips), units);
            }

            if (giverDamaged == 0)
            {
                newGiver = giver;
            }
            else if (movedDamaged >= giverDamaged)
            {
                newGiver = new DamageWord(0, 0);
            }
            else
            {
                newGiver = new DamageWord(CeilPercent(giverDamaged - movedDamaged, newGiverShips), giver.Units);
            }
        }

        /// <summary>
        /// Newly built (undamaged) ships joining a stack of <paramref name="existingShips"/>
        /// (combat-resolution.md §8, "Newly built ships joining a fleet", FUN_10b8_0e68):
        /// <c>D = max(1, percent x n / 100)</c>, <c>T = (figure x armor / 10) x D / 50</c>, the new
        /// percentage is <c>D x 100 / (n + k)</c> truncated (at least 1), <c>D' </c> comes from it,
        /// and the new figure is <c>(T x 5 / D') x 100 / armor</c>, every division truncating. An
        /// undamaged stack stays undamaged.
        /// </summary>
        public static DamageWord JoinNewShips(DamageWord existing, int existingShips, int newShips, int armorPerShip)
        {
            if (newShips <= 0 || existingShips <= 0 || armorPerShip <= 0)
            {
                return existing;
            }

            if (existing.IsUndamaged)
            {
                return new DamageWord(0, 0);
            }

            int totalShips = existingShips + newShips;
            int damaged = Math.Max(1, existing.Percent * existingShips / 100);
            long sharedDamage = ((long)existing.Units * armorPerShip / 10) * damaged / 50;
            int newPercent = Math.Max(1, damaged * 100 / totalShips);
            int newDamaged = Math.Max(1, newPercent * totalShips / 100);
            int newUnits = (int)(((sharedDamage * 5 / newDamaged) * 100) / armorPerShip);
            return new DamageWord(newPercent, newUnits);
        }

        private static int CeilPercent(int damagedShips, int newShips)
        {
            if (damagedShips <= 0 || newShips <= 0)
            {
                return 0;
            }

            return (damagedShips * 100 + newShips - 1) / newShips;
        }

        /// <summary>
        /// The battle damage routine (FUN_10f0_52c4 :102345-102453), applied to a token of
        /// <paramref name="ships"/> ships of <paramref name="armorPerShip"/> armor carrying this
        /// word.
        /// (1) Unpack. (2) Kill damaged ships first, each needing armor minus its carried damage,
        /// then undamaged ships at full armor, while the damage left covers one more ship and the
        /// kill limit allows it; once the limit is used up the rest of the damage is thrown away.
        /// (3) Repack when ships survive: with no damage left over the units stay and the
        /// percentage becomes surviving damaged x 100 / survivors rounded up (the word is zero when
        /// no damaged ship survives); with damage left over it is added to what the surviving
        /// damaged ships carry, shared evenly over all survivors rounded up (at least 1), converted
        /// to 1/500 units rounding up (at least 1, at most 499), at 100%.
        /// </summary>
        /// <param name="damage">Armor damage of this shot (integer points).</param>
        /// <param name="ships">Ships in the token.</param>
        /// <param name="armorPerShip">The design's per-ship armor.</param>
        /// <param name="killLimit">Most ships the shot may destroy (missiles); int.MaxValue for beams.</param>
        public DamageResult Apply(long damage, int ships, int armorPerShip, int killLimit = int.MaxValue)
        {
            DamageResult result = new DamageResult();
            if (ships <= 0)
            {
                result.Word = new DamageWord(0, 0);
                result.Leftover = Math.Max(0L, damage);
                return result;
            }

            int damaged = DamagedShips(ships);
            long carried = DamagePerShip(armorPerShip);
            int undamaged = ships - damaged;
            long left = Math.Max(0L, damage);
            int kills = 0;

            long damagedNeed = Math.Max(0L, armorPerShip - carried);
            while (damaged > 0 && kills < killLimit && left >= damagedNeed)
            {
                left -= damagedNeed;
                damaged--;
                kills++;
            }

            if (damaged == 0)
            {
                while (undamaged > 0 && kills < killLimit && left >= armorPerShip)
                {
                    left -= armorPerShip;
                    undamaged--;
                    kills++;
                }
            }

            int survivors = damaged + undamaged;
            result.ShipsKilled = kills;

            if (survivors == 0)
            {
                result.Word = new DamageWord(0, 0);
                result.Leftover = left;
                return result;
            }

            if (kills >= killLimit)
            {
                // The kill limit is used up: whatever damage is left is thrown away.
                result.Discarded = left;
                left = 0;
            }

            if (left == 0)
            {
                if (damaged == 0)
                {
                    result.Word = new DamageWord(0, 0);
                }
                else
                {
                    int percent = ((damaged * 100) + survivors - 1) / survivors;
                    result.Word = new DamageWord(percent, Units);
                }

                return result;
            }

            long total = left + (damaged * carried);
            long perShip = Math.Max(1L, (total + survivors - 1) / survivors);
            result.Word = new DamageWord(100, ToUnits(perShip, armorPerShip));
            return result;
        }

        public override string ToString()
        {
            return Percent + "% x " + Units + "/500";
        }

        /// <summary>The outcome of <see cref="Apply"/>.</summary>
        public struct DamageResult
        {
            /// <summary>Whole ships destroyed by the shot.</summary>
            public int ShipsKilled;

            /// <summary>The new word for the survivors (zero when none survive).</summary>
            public DamageWord Word;

            /// <summary>Damage left after the whole token died (what a beam may overflow with).</summary>
            public long Leftover;

            /// <summary>Damage thrown away because the kill limit was reached.</summary>
            public long Discarded;
        }
    }
}
