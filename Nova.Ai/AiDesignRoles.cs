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
    using System.Text.RegularExpressions;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// What the planet-side AI needs to know about its own designs without the original's fixed
    /// design slots (behavior-specs-10/ai-opponent-behavior.md §16-§17): a design's creation year
    /// and which role it fills.
    /// </summary>
    /// <remarks>
    /// Nova's ShipDesign has no creation-year field (the original stamps it at design `+0x7d`).
    /// The AI already records the creation turn in an AI-built design's name (" T2142", see
    /// ShipDesignRefresher.NameWithTurnSuffix); a design without that suffix is one of the game's
    /// starting designs, created when the game was (year 0). The role stand-in follows the same
    /// convention: an AI design and its refreshed successor share a name apart from the suffix.
    /// </remarks>
    public static class AiDesignRoles
    {
        private static readonly Regex TurnSuffix = new Regex(@" T(\d+)$");

        /// <summary>The year (in TurnYear units) the design was created: the " T&lt;year&gt;"
        /// suffix, else the game's starting year.</summary>
        public static int CreationYear(ShipDesign design)
        {
            if (design == null || design.Name == null)
            {
                return Global.StartingYear;
            }

            Match match = TurnSuffix.Match(design.Name);
            return match.Success ? int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : Global.StartingYear;
        }

        /// <summary>The design's name without its creation-turn suffix.</summary>
        public static string BaseName(ShipDesign design)
        {
            if (design == null || design.Name == null)
            {
                return string.Empty;
            }

            return TurnSuffix.Replace(design.Name, string.Empty);
        }

        /// <summary>Whether <paramref name="successor"/> replaces <paramref name="older"/> in the
        /// same role: the same owner and item type, a later design (Nova's design keys rise with
        /// every design an empire creates) and the same role - the same §17 role tag when both
        /// carry one (<see cref="AiDesignRoleTag"/>, whatever the hull), else the same base
        /// name.</summary>
        public static bool Supersedes(ShipDesign successor, ShipDesign older)
        {
            if (successor == null || older == null || successor.Key == older.Key)
            {
                return false;
            }

            if (successor.Owner != older.Owner || successor.Type != older.Type || successor.Key < older.Key)
            {
                return false;
            }

            string successorTag = AiDesignRoleTag.TagOf(successor);
            string olderTag = AiDesignRoleTag.TagOf(older);
            if (successorTag != null || olderTag != null)
            {
                return successorTag == olderTag;
            }

            return BaseName(successor) == BaseName(older);
        }

        /// <summary>A starbase design (Manufacture tells starbases apart by the item type).</summary>
        public static bool IsStarbaseDesign(ShipDesign design)
        {
            return design != null && design.Type == ItemType.Starbase;
        }
    }
}
