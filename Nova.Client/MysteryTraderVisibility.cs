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
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// Which Mystery Traders a player sees (behavior-specs-11/turn-generation-engine.md §5a,
    /// "Who sees a Trader"): the Trader-visibility pass marks every Trader entry as seen with no
    /// test at all, and the scanner sweeps skip entries already marked, so distance, scanner
    /// range, penetrating scanners, cloaking, the Tachyon Detector and the observer's race play no
    /// part. Every player sees every Mystery Trader anywhere in the galaxy, every turn. There is
    /// therefore no filter here - the whole collection is visible - but the rule lives in one
    /// place so the map/UI and tests share it rather than each inventing a visibility test.
    /// </summary>
    public static class MysteryTraderVisibility
    {
        /// <summary>
        /// The Traders visible to <paramref name="observer"/>: all of them, in a stable order.
        /// The <paramref name="observer"/> argument is deliberately unused (the spec's rule has no
        /// test), and is kept so call sites and tests read as a visibility question.
        /// </summary>
        public static List<MysteryTrader> VisibleTraders(IEnumerable<MysteryTrader> traders, EmpireData observer)
        {
            if (traders == null)
            {
                return new List<MysteryTrader>();
            }

            return traders.OrderBy(trader => trader.Key).ToList();
        }

        /// <summary>The Traders visible to this client (all of the turn file's Traders).</summary>
        public static List<MysteryTrader> VisibleTraders(ClientData client)
        {
            if (client == null)
            {
                return new List<MysteryTrader>();
            }

            return VisibleTraders(client.AllMysteryTraders.Values, client.EmpireState);
        }
    }
}
