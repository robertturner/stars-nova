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

    /// <summary>
    /// Resting-packet (salvage) decay, part of step 18 (FUN_10b8_433a) of behavior-specs-10/
    /// turn-generation-engine.md §1 and §3: a stationary salvage record removes 10% of each
    /// mineral (at least 10 kT) per year after a one-turn grace flag is used up, and is deleted
    /// at zero. The wreckage routine (BattleEngine.AddWreckage, FUN_10f0_1850) sets the grace
    /// flag; this pass clears it instead of decaying that year. Step 18 runs after fleet
    /// movement and BEFORE the production hub and the battle, so wreckage made in a battle is
    /// untouched until the next generation, which only spends the grace, and first decays in
    /// the generation after that. Planet-side salvage never becomes a record and never decays.
    /// </summary>
    public class DeepSpaceMineralDecayStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            List<string> emptied = new List<string>();

            foreach (KeyValuePair<string, DeepSpaceMinerals> entry in serverState.AllDeepSpaceMinerals)
            {
                entry.Value.DecayOrUseGrace();

                if (entry.Value.IsEmpty)
                {
                    emptied.Add(entry.Key);
                }
            }

            foreach (string key in emptied)
            {
                serverState.AllDeepSpaceMinerals.Remove(key);
            }
        }
    }
}
