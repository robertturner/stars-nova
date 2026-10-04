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
    using Nova.Common;

    /// <summary>
    /// The research buy loop on what is already banked, with no new income (the original's
    /// FUN_10b8_4ce4(0)). The loop runs three times a generation
    /// (behavior-specs-10/turn-generation-engine.md §1, "Loops and repeated work"): step 12e
    /// before movement and step 23g after the battle's colonisation pass spend only banked pools
    /// (salvage, artifact and event bounties); only the production hub's call (step 20, inside
    /// <see cref="StarUpdateStep"/>) adds the year's income. Every field of every race is
    /// priced, not only the field under research, so a bounty banked in any field is spent.
    /// </summary>
    public class ResearchBuyLoopStep : ITurnStep
    {
        public void Process(ServerData serverState)
        {
            StarUpdateStep buyLoop = new StarUpdateStep();
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (empire.Race == null)
                {
                    continue;
                }

                buyLoop.SpendBankedResearch(serverState, empire);
            }
        }
    }
}
