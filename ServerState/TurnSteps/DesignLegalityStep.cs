#region Copyright Notice
// ============================================================================
// Copyright (C) 2009-2012 The Stars-Nova Project
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
    using System.Collections.Generic;

    using Nova.Common;
    using Nova.Common.Components;

    /// <summary>
    /// The end-of-generation design legality pass (turn-generation step 36; behavior-specs-10/
    /// fleet-movement-scanning-cargo.md §5 Scrap Fleet, "The flag", and ship-design-and-
    /// components.md §16, the +0x7c row).
    /// </summary>
    /// <remarks>
    /// Every in-use ship design of every empire is tested; a design passes only when its hull and
    /// every non-empty slot are "fully available" to the owner now: the owner's primary and lesser
    /// traits allow the part (<see cref="RaceComponents.IsRestrictedFor"/>), the owner has every
    /// listed tech level, and - for the one-time-grant specials, which no amount of research makes
    /// available - the owner was actually given the part (it is in AvailableComponents). A design
    /// that fails gets <see cref="ShipDesign.FailedLegality"/> (bit 0x80). Starbase designs are
    /// never tested (step 36's starbase walk does not call the test). The flag is only ever set
    /// here, never cleared, so a flagged design stays at Scrap Fleet's quarter rate even after the
    /// owner reaches the tech. It runs at the end of a generation, so the Scrap/Colonize valuation
    /// in the NEXT generation reflects the owner's position at the end of this year.
    /// "In use" is read as "occupies a design slot" (EmpireData.Designs), plus any design still
    /// referenced by one of the empire's ship stacks.
    /// </remarks>
    public class DesignLegalityStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                HashSet<ShipDesign> designs = new HashSet<ShipDesign>(empire.Designs.Values);
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    foreach (ShipToken token in fleet.Composition.Values)
                    {
                        if (token.Design != null)
                        {
                            designs.Add(token.Design);
                        }
                    }
                }

                foreach (ShipDesign design in designs)
                {
                    if (design.FailedLegality || design.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
                    {
                        continue;
                    }

                    if (design.IsStarbase)
                    {
                        continue;
                    }

                    if (!IsFullyAvailable(design, empire))
                    {
                        design.FailedLegality = true;
                    }
                }
            }
        }

        /// <summary>
        /// True when the design's hull and every installed part are fully available to the
        /// empire now (the original's FUN_1038_4e50 returning 1).
        /// </summary>
        public static bool IsFullyAvailable(ShipDesign design, EmpireData empire)
        {
            if (!IsComponentAvailable(design.Blueprint, empire))
            {
                return false;
            }

            foreach (HullModule module in design.Hull.Modules)
            {
                if (module.AllocatedComponent == null || module.ComponentCount <= 0)
                {
                    continue;
                }

                if (!IsComponentAvailable(module.AllocatedComponent, empire))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsComponentAvailable(Component component, EmpireData empire)
        {
            if (empire.Race != null && component.Restrictions != null && RaceComponents.IsRestrictedFor(component, empire.Race))
            {
                return false;
            }

            if (component.RequiredTech != null && empire.ResearchLevels < component.RequiredTech)
            {
                return false;
            }

            // A gift-only part (battle/event grant) is available only once actually given.
            if (component.Name != null && SpecialComponentGrants.IsSpecialGrant(component.Name)
                && (empire.AvailableComponents == null || !empire.AvailableComponents.Contains(component.Name)))
            {
                return false;
            }

            return true;
        }
    }
}
