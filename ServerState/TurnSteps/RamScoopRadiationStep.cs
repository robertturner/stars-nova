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
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The Radiating Hydro-Ram Scoop's colonist hazard (behavior-specs-10/
    /// fleet-movement-scanning-cargo.md section 2 "Radiation hazard of low-tier ramscoops";
    /// ship-design-and-components.md, engine footnote 42: "its radiation kills some colonists
    /// unless the centre of the race's Radiation tolerance band is at least 85 mR", a literal
    /// engine-subtype check in the client).
    /// </summary>
    /// <remarks>
    /// Implemented: once a year, a fleet any of whose occupied stacks has a Radiating Hydro-Ram
    /// Scoop loses part of the colonists it carries, unless its owner is radiation-immune or the
    /// centre of its radiation band is 85 or more. The yearly loss is NOT given by the specs (the
    /// in-flight hazard code was "not re-read for the number"); this uses the Stars!wiki figure
    /// int((86 - C) / 2) percent a year for a radiation centre C (20 -> 33%, 50 -> 18%, 80 -> 3%,
    /// 85 -> 0, matching the spec's 85 mR threshold), applied to the hold in 100-colonist units
    /// and truncated, so a load too small for one unit of loss loses nothing. It applies whether
    /// or not the fleet moved (the spec does not tie it to movement), and runs with the post-
    /// movement steps before Inner Strength breeding (step 19).
    /// </remarks>
    public class RamScoopRadiationStep : ITurnStep
    {
        public const string RadiatingEngineName = "Radiating Hydro-Ram Scoop";

        /// <summary>Radiation-band centre at and above which the engine is harmless.</summary>
        public const int SafeRadiationCentre = 85;

        public void Process(ServerData serverState)
        {
            foreach (Fleet fleet in serverState.IterateAllFleets().ToList())
            {
                if (fleet.IsStarbase || fleet.Cargo == null || fleet.Cargo.ColonistsInKilotons <= 0 || !HasRadiatingEngine(fleet))
                {
                    continue;
                }

                if (!serverState.AllEmpires.TryGetValue(fleet.Owner, out var owner) || owner.Race == null)
                {
                    continue;
                }

                int percent = LossPercent(owner.Race);
                int lost = fleet.Cargo.ColonistsInKilotons * percent / 100;
                if (lost <= 0)
                {
                    continue;
                }

                fleet.Cargo.ColonistsInKilotons -= lost;

                Message message = new Message();
                message.Audience = fleet.Owner;
                message.Type = "Cargo";
                message.Text = (lost * Global.ColonistsPerKiloton) + " colonists aboard " + fleet.Name
                    + " were killed by radiation from its " + RadiatingEngineName + ".";
                serverState.AllMessages.Add(message);
            }
        }

        /// <summary>The yearly colonist loss in percent: 0 when radiation-immune or the band's
        /// centre is at least 85, otherwise int((86 - centre) / 2).</summary>
        public static int LossPercent(Race race)
        {
            if (race == null || race.IsImmune(2))
            {
                return 0;
            }

            int centre = race.CenterHab(2);
            if (centre >= SafeRadiationCentre)
            {
                return 0;
            }

            return Math.Max(0, (86 - centre) / 2);
        }

        /// <summary>True when an occupied stack's design mounts the Radiating Hydro-Ram Scoop.</summary>
        public static bool HasRadiatingEngine(Fleet fleet)
        {
            foreach (ShipToken token in fleet.Composition.Values)
            {
                if (token.Quantity > 0 && MountsRadiatingEngine(token.Design))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MountsRadiatingEngine(ShipDesign design)
        {
            if (design?.Hull?.Modules == null)
            {
                return false;
            }

            foreach (HullModule module in design.Hull.Modules)
            {
                Component part = module?.AllocatedComponent;
                if (part != null && module.ComponentCount > 0 && part.Name == RadiatingEngineName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
