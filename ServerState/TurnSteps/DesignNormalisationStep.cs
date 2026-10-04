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
    /// Design normalisation (behavior-specs-10/turn-generation-engine.md §1 step 8, run right after
    /// the players' orders are applied): for every in-use ship design, if the first installed
    /// component entry is not an engine (category 1), it is set to category 1, subtype 1 -
    /// "Quick Jump 5", idx 1 of the engine table in component-stats.tsv (a slot's subtype is that
    /// table's idx, ship-design-and-components.md / combat-resolution.md §11) - with a quantity of
    /// at least 1.
    /// </summary>
    /// <remarks>
    /// The original's slot 0 is the hull's engine slot. This port's hull module lists are not in
    /// the original's slot order (a Destroyer lists a weapon slot first), so "the first entry" is
    /// read as the hull's first engine-typed module. Ship hulls with no engine module, and every
    /// starbase design (the original walks only the ship-design table), are left alone. No
    /// message is posted (the spec names none).
    /// </remarks>
    public class DesignNormalisationStep : ITurnStep
    {
        /// <summary>Engine idx 1 of component-stats.tsv (category 1, subtype 1).</summary>
        public const string DefaultEngineName = "Quick Jump 5";

        public void Process(ServerData serverState)
        {
            Component engine = null;

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                foreach (ShipDesign design in empire.Designs.Values.ToList())
                {
                    if (design == null || design.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull") || design.IsStarbase)
                    {
                        continue;
                    }

                    HullModule slot = design.Hull.Modules.FirstOrDefault(module => module.ComponentType == "Engine");
                    if (slot == null || IsEngine(slot.AllocatedComponent))
                    {
                        continue;
                    }

                    if (engine == null)
                    {
                        engine = new AllComponents().Fetch(DefaultEngineName);
                        if (engine == null)
                        {
                            return;
                        }
                    }

                    Normalise(design, slot, engine, empire);
                }
            }
        }

        /// <summary>Puts <paramref name="engine"/> in the slot with a quantity of at least 1 (and at most the slot's capacity).</summary>
        public static void Normalise(ShipDesign design, HullModule slot, Component engine, EmpireData empire)
        {
            slot.AllocatedComponent = engine;
            slot.ComponentCount = Math.Max(1, Math.Min(slot.ComponentCount, Math.Max(1, slot.ComponentMaximum)));
            design.Update(empire.Race, empire.ResearchLevels);
        }

        private static bool IsEngine(Component component)
        {
            return component != null && component.Properties.ContainsKey("Engine");
        }
    }
}
