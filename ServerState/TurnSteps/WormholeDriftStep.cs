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
    using System;

    using Nova.Common;

    /// <summary>
    /// Each Wormhole end "independently drifts a little every year" - docs/behavior-specs-4/
    /// fleet-movement-scanning-cargo.md's "Wormholes" section, whose own stability scale (0
    /// "Rock Solid" - 6 "Very Unstable") is confirmed but whose exact per-tier drift chance/
    /// magnitude and full placement-rescoring-on-relocation are not ("compared against a 0-99
    /// random roll to decide whether the object moves this year... a new candidate position is
    /// picked... re-validated before committing"). This is a disclosed simplification of that:
    /// a linear, tier-scaled chance-to-drift and a small random nudge clamped to the map bounds,
    /// without re-running the full minimum-distance placement check StarMapinitializer.
    /// GenerateWormholes uses when a wormhole is first created - acceptable for a cosmetic yearly
    /// wobble, unlike initial placement where landing on top of a star would matter a lot more.
    /// </summary>
    public class WormholeDriftStep : ITurnStep
    {
        private readonly Random random = new Random();

        public void Process(ServerData serverState)
        {
            foreach (Wormhole wormhole in serverState.AllWormholes.Values)
            {
                int driftChancePercent = Math.Min(70, (wormhole.StabilityTier + 1) * 10);
                if (random.Next(100) >= driftChancePercent)
                {
                    continue;
                }

                int driftMagnitude = 5 + (wormhole.StabilityTier * 3);
                wormhole.Position.X = Clamp(wormhole.Position.X + random.Next(-driftMagnitude, driftMagnitude + 1), 0, GameSettings.Data.MapWidth);
                wormhole.Position.Y = Clamp(wormhole.Position.Y + random.Next(-driftMagnitude, driftMagnitude + 1), 0, GameSettings.Data.MapHeight);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
