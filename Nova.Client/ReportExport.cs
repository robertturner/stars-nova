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

namespace Nova.Client
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// The "Dump to Text File" family (behavior-specs-10/client-ui-dialog-catalog.md "Reports",
    /// client-interface.md command table ids 83-85): plain-text exports with a tab-separated
    /// header row, written without a file prompt or confirmation.
    /// - Planets: per planet the name; the owner's race name (or a placeholder); the population;
    ///   the habitability percentage once colonised; a starbase figure when there is a starbase;
    ///   three surface minerals (kT); three concentrations.
    /// - Universe: the same columns over every known star.
    /// - Fleets: one row per owned fleet: a row number, the position, the name.
    /// SPEC GAP: the header captions (string-table entries), the "alternate estimate" printed
    /// for an uncolonised planet's population column, the exact starbase figure, the
    /// "extended" column variant and the file names are not given. Neutral choices: plain
    /// English captions, an empty cell where a figure is unknown, the starbase's name, no
    /// extended variant, and files named "race.planets.txt" / ".universe.txt" / ".fleets.txt" in
    /// the game folder (<see cref="FileName"/>).
    /// AMBIGUITY: the concentration columns are "computed through the core mining-engine
    /// function"; the stored concentration figure is written.
    /// </summary>
    public static class ReportExport
    {
        public enum Kind
        {
            Planets,
            Universe,
            Fleets,
        }

        public const string UnownedPlaceholder = "(unowned)";

        public const string UnexploredPlaceholder = "(unexplored)";

        public static readonly string[] PlanetHeader =
        {
            "Planet", "Owner", "Population", "Value %", "Starbase",
            "Surface Ironium", "Surface Boranium", "Surface Germanium",
            "Ironium Conc", "Boranium Conc", "Germanium Conc",
        };

        public static readonly string[] FleetHeader = { "#", "X", "Y", "Fleet" };

        /// <summary>The export's file name for a race (see the SPEC GAP note).</summary>
        public static string FileName(string raceName, Kind kind)
        {
            string suffix = kind == Kind.Planets ? "planets" : kind == Kind.Universe ? "universe" : "fleets";
            return (string.IsNullOrEmpty(raceName) ? "nova" : raceName) + "." + suffix + ".txt";
        }

        /// <summary>The export's whole text.</summary>
        public static string Build(EmpireData empire, Kind kind)
        {
            switch (kind)
            {
                case Kind.Planets: return Planets(empire);
                case Kind.Universe: return Universe(empire);
                default: return Fleets(empire);
            }
        }

        /// <summary>Writes the export into <paramref name="folder"/> and returns its path.</summary>
        public static string Write(EmpireData empire, Kind kind, string folder)
        {
            string path = Path.Combine(folder ?? string.Empty, FileName(empire?.Race?.Name, kind));
            File.WriteAllText(path, Build(empire, kind));
            return path;
        }

        /// <summary>The empire's own planets.</summary>
        public static string Planets(EmpireData empire)
        {
            StringBuilder text = new StringBuilder();
            AppendRow(text, PlanetHeader);
            if (empire == null)
            {
                return text.ToString();
            }

            foreach (Star star in empire.OwnedStars.Values.Where(star => star.Owner == empire.Id).OrderBy(star => star.Name, StringComparer.OrdinalIgnoreCase))
            {
                AppendRow(text, new[]
                {
                    star.Name,
                    OwnerName(empire, star.Owner),
                    Number(star.Colonists),
                    star.Colonists > 0 && empire.Race != null ? Number(empire.Race.HabPercent(star)) : string.Empty,
                    star.Starbase != null ? star.Starbase.Name : string.Empty,
                    Number(star.ResourcesOnHand.Ironium),
                    Number(star.ResourcesOnHand.Boranium),
                    Number(star.ResourcesOnHand.Germanium),
                    Number(star.MineralConcentration.Ironium),
                    Number(star.MineralConcentration.Boranium),
                    Number(star.MineralConcentration.Germanium),
                });
            }

            return text.ToString();
        }

        /// <summary>Every star the empire knows of (its reports), owned planets with full detail.</summary>
        public static string Universe(EmpireData empire)
        {
            StringBuilder text = new StringBuilder();
            AppendRow(text, PlanetHeader);
            if (empire == null)
            {
                return text.ToString();
            }

            foreach (StarIntel report in empire.StarReports.Values.OrderBy(report => report.Name, StringComparer.OrdinalIgnoreCase))
            {
                Star owned = empire.OwnedStars.Values.FirstOrDefault(star => star.Name == report.Name && star.Owner == empire.Id);
                bool explored = report.Year != Global.Unset;
                bool colonised = explored && report.Owner != Global.Nobody;

                string owner = !explored
                    ? UnexploredPlaceholder
                    : report.Owner == Global.Nobody ? UnownedPlaceholder : OwnerName(empire, report.Owner);

                AppendRow(text, new[]
                {
                    report.Name,
                    owner,
                    owned != null ? Number(owned.Colonists) : colonised ? Number(report.Colonists) : string.Empty,
                    owned != null && owned.Colonists > 0 && empire.Race != null
                        ? Number(empire.Race.HabPercent(owned))
                        : colonised && empire.Race != null ? Number((int)Math.Round(empire.Race.HabitalValue(report) * 100.0)) : string.Empty,
                    report.Starbase != null ? report.Starbase.Name : string.Empty,
                    owned != null ? Number(owned.ResourcesOnHand.Ironium) : string.Empty,
                    owned != null ? Number(owned.ResourcesOnHand.Boranium) : string.Empty,
                    owned != null ? Number(owned.ResourcesOnHand.Germanium) : string.Empty,
                    explored && report.MineralConcentration != null ? Number(report.MineralConcentration.Ironium) : string.Empty,
                    explored && report.MineralConcentration != null ? Number(report.MineralConcentration.Boranium) : string.Empty,
                    explored && report.MineralConcentration != null ? Number(report.MineralConcentration.Germanium) : string.Empty,
                });
            }

            return text.ToString();
        }

        /// <summary>The empire's own fleets: row number, position, name (starbases excluded).</summary>
        public static string Fleets(EmpireData empire)
        {
            StringBuilder text = new StringBuilder();
            AppendRow(text, FleetHeader);
            if (empire == null)
            {
                return text.ToString();
            }

            int row = 1;
            foreach (Fleet fleet in empire.OwnedFleets.Values.Where(fleet => !fleet.IsStarbase).OrderBy(fleet => fleet.Name, StringComparer.OrdinalIgnoreCase))
            {
                AppendRow(text, new[]
                {
                    Number(row++),
                    Number((long)fleet.Position.X),
                    Number((long)fleet.Position.Y),
                    fleet.Name,
                });
            }

            return text.ToString();
        }

        private static string OwnerName(EmpireData empire, ushort owner)
        {
            if (owner == Global.Nobody)
            {
                return UnownedPlaceholder;
            }

            if (owner == empire.Id)
            {
                return empire.Race?.Name ?? string.Empty;
            }

            return empire.EmpireReports.TryGetValue(owner, out EmpireIntel intel) ? intel.RaceName : "Empire " + owner.ToString(CultureInfo.InvariantCulture);
        }

        private static string Number(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void AppendRow(StringBuilder text, IEnumerable<string> cells)
        {
            text.Append(string.Join("\t", cells.Select(Clean)));
            text.Append(Environment.NewLine);
        }

        /// <summary>A cell never contains the separator or a line break.</summary>
        private static string Clean(string cell)
        {
            if (string.IsNullOrEmpty(cell))
            {
                return string.Empty;
            }

            return cell.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
