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
    using System.Runtime.CompilerServices;

    /// <summary>
    /// A token's packed 16-bit damage word - behavior-specs-10/combat-resolution.md §8 ("The
    /// armor-quantization scheme ... settled by the gap-report pass", FUN_10f0_52c4) and
    /// turn-generation-engine.md §11. The low 7 bits are the percentage of the token's surviving
    /// ships that carry damage; the high 9 bits are the damage EACH damaged ship carries, in
    /// 1/500 of the design's per-ship armor (0-499). A stack never tracks two damage levels.
    /// </summary>
    /// <remarks>
    /// Nova stores a token's state as a pooled armor total (<see cref="ShipToken.Armor"/>). This
    /// type is the exact original state; the battle engine reads it with <see cref="For"/> and
    /// writes it back with <see cref="Store"/>, which also sets the pooled armor to the word's own
    /// value. The word is persisted on the token (<see cref="ShipToken.PackedDamage"/>, saved
    /// with the game) and also remembered per token for the life of the process; when it is missing or
    /// no longer matches the token's pooled armor (damage applied elsewhere - repair, minefields,
    /// overgating - or a reloaded game) it is re-derived from the pooled armor as "every ship
    /// damaged", the per-ship damage rounded up to the next 1/500 (the only information lost is a
    /// percentage below 100).
    /// </remarks>
    public struct DamageWord
    {
        /// <summary>Most 1/500 units a damaged ship can carry (the high 9 bits are clamped to 0-499).</summary>
        public const int MaxUnits = 499;

        /// <summary>The armor quantum: damage is stored in 1/500 of one ship's armor.</summary>
        public const int UnitsPerArmor = 500;

        private static readonly ConditionalWeakTable<ShipToken, Remembered> Cache = new ConditionalWeakTable<ShipToken, Remembered>();

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
        /// Builds a word from a pooled damage total: every ship damaged (100%), the per-ship damage
        /// (total / ships, rounded up, at least 1) converted to 1/500 units rounding up (at least 1,
        /// at most 499). Zero damage gives the zero word.
        /// </summary>
        public static DamageWord FromPooledDamage(long totalDamage, int ships, int armorPerShip)
        {
            if (totalDamage <= 0 || ships <= 0 || armorPerShip <= 0)
            {
                return new DamageWord(0, 0);
            }

            long perShip = Math.Max(1L, (totalDamage + ships - 1) / ships);
            return new DamageWord(100, ToUnits(perShip, armorPerShip));
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
        /// The token's current word: the remembered one if it still matches the token's ship
        /// count and pooled armor, otherwise one derived from the pooled armor
        /// (<see cref="FromPooledDamage"/>).
        /// </summary>
        public static DamageWord For(ShipToken token)
        {
            if (token == null || token.Design == null || token.Quantity <= 0)
            {
                return new DamageWord(0, 0);
            }

            int armorPerShip = token.Design.Armor;

            // The persisted word (ShipToken.PackedDamage) wins while it still reproduces the
            // token's pooled armor - i.e. nothing has changed Armor or Quantity behind its back.
            if (token.PackedDamage != 0)
            {
                DamageWord stored = FromPacked(token.PackedDamage);
                if (Math.Abs(stored.PooledArmor(token.Quantity, armorPerShip) - token.Armor) < 1e-6)
                {
                    return stored;
                }
            }

            if (Cache.TryGetValue(token, out Remembered remembered)
                && remembered.Quantity == token.Quantity
                && remembered.Armor == token.Armor)
            {
                return remembered.Word;
            }

            long full = (long)token.Quantity * armorPerShip;
            long damage = full - (long)Math.Ceiling(token.Armor);
            return FromPooledDamage(damage, token.Quantity, armorPerShip);
        }

        /// <summary>Remembers the token's word and sets its pooled armor to the word's value.</summary>
        public static void Store(ShipToken token, DamageWord word)
        {
            if (token == null || token.Design == null)
            {
                return;
            }

            token.Armor = word.PooledArmor(token.Quantity, token.Design.Armor);
            token.PackedDamage = word.Packed;
            Cache.AddOrUpdate(token, new Remembered { Word = word, Quantity = token.Quantity, Armor = token.Armor });
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

        private sealed class Remembered
        {
            public DamageWord Word;
            public int Quantity;
            public double Armor;
        }
    }
}
