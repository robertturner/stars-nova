#region Copyright Notice
// ============================================================================
// Copyright (C) 2012 The Stars-Nova Project
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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server
{
    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// Alternate Reality's "Starter Colony" starbase. behavior-specs-9/population-growth.md section 3
    /// and turn-generation-engine.md section 11: an Alternate Reality planet's population capacity
    /// comes entirely from the starbase orbiting it (and is 0 with none), so a won Alternate Reality
    /// colonisation installs design slot 0, "Starter Colony" - an Orbital Fort hull, 250,000
    /// colonists - on the new colony. At game setup an AR race's slot 0 is the Starter Colony and
    /// slot 1 the full "Starbase" (a Space Station) its homeworld uses.
    /// </summary>
    /// <remarks>
    /// The spec gives only the hull of the Starter Colony, so the design carries no components.
    /// </remarks>
    public static class StarterColony
    {
        public const string DesignName = "Starter Colony";

        /// <summary>The empire's Starter Colony design, created on the Orbital Fort hull if it has none.</summary>
        public static ShipDesign EnsureDesign(EmpireData empire)
        {
            foreach (ShipDesign design in empire.Designs.Values)
            {
                if (design.Name == DesignName)
                {
                    return design;
                }
            }

            Component hull = new AllComponents().Fetch("Orbital Fort");

            ShipDesign starter = new ShipDesign(empire.GetNextDesignKey());
            starter.Name = DesignName;
            starter.Blueprint = hull;
            starter.Type = ItemType.Starbase;
            starter.Icon = new ShipIcon(hull.ImageFile, hull.ComponentImage);
            starter.Update();
            empire.Designs[starter.Key] = starter;
            return starter;
        }

        /// <summary>
        /// Installs the Starter Colony starbase on a newly won colony. Does nothing if the planet
        /// already has a starbase.
        /// </summary>
        public static void Install(Star star, EmpireData empire)
        {
            if (star.Starbase != null)
            {
                return;
            }

            ShipDesign design = EnsureDesign(empire);
            ShipToken token = new ShipToken(design, 1);

            Fleet starbase = new Fleet(token, star, empire.GetNextFleetKey());
            starbase.Name = star.Name + " Starbase";
            starbase.Type = ItemType.Starbase;
            starbase.InOrbit = star;

            star.Starbase = starbase;
            empire.AddOrUpdateFleet(starbase);
        }
    }
}
