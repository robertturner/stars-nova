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
    using System.Globalization;

    using Nova.Common;

    /// <summary>
    /// The planet-artifact research bounty (behavior-specs-10/turn-generation-engine.md §11,
    /// "Colonisation and invasion", and §1b): when a planet carrying the artifact flag changes
    /// owner and "No Random Events" is off, the new owner immediately receives
    /// 100 + random(0..300) research resources in a random one of the six fields (scaled down
    /// when the founding population is under 1,000 colonists), message 94; the flag is cleared on
    /// use. The resources are banked in the field's research pool, which the research buy loop
    /// (<see cref="ResearchBuyLoopStep"/>, run right after this step) spends.
    /// </summary>
    /// <remarks>
    /// The original pays the bounty inside the colonisation/invasion resolver. Flags are only ever
    /// placed on planets nobody owns (<see cref="AssignAtCreation"/>), so "an owned planet that
    /// still carries the flag" is exactly "a flagged planet that just changed owner"; this step
    /// pays it right after this port's colonisation pass (and invasions, which resolve during
    /// movement). Draws: Next(301) for the amount, then Next(6) for the field (in the original's
    /// Energy, Weapons, Propulsion, Construction, Electronics, Biotechnology order); the order of
    /// the two draws is not given by the spec.
    /// SPEC GAP (reported): the scaling "when the founding population is under 1,000 colonists" is
    /// not given; <see cref="ScaleForFoundingPopulation"/> scales linearly (amount x colonists /
    /// 1,000, truncated) as a stand-in.
    /// Ambiguity: "random(0..300)" is read as 0 to 300 inclusive.
    /// </remarks>
    public class PlanetArtifactStep : ITurnStep
    {
        public const int BaseBounty = 100;

        /// <summary>The largest random addition (inclusive).</summary>
        public const int RandomBounty = 300;

        /// <summary>Below this founding population the bounty is scaled down.</summary>
        public const int FullBountyColonists = 1000;

        /// <summary>At galaxy creation 1 planet in this many carries the artifact flag.</summary>
        public const int CreationOdds = 3;

        public const string MessageType = "Artifact";

        // The injected test random, or null: Process then draws from the game's own seeded
        // stream (ServerData.CreateRandom), so the bounty is repeatable from the seed.
        private readonly Random injectedRandom;

        public PlanetArtifactStep() : this(null)
        {
        }

        public PlanetArtifactStep(Random random)
        {
            this.injectedRandom = random;
        }

        public void Process(ServerData serverState)
        {
            Random random = injectedRandom ?? serverState.CreateRandom("PlanetArtifact");

            // "No Random Events" removes the bounty (§1b, bit 0x80).
            if (GameSettings.Data.NoRandomEvents)
            {
                return;
            }

            foreach (Star star in serverState.AllStars.Values)
            {
                if (!star.HasArtifact || star.Owner == Global.Nobody)
                {
                    continue;
                }

                star.HasArtifact = false;

                if (!serverState.AllEmpires.TryGetValue(star.Owner, out EmpireData owner))
                {
                    continue;
                }

                int amount = BaseBounty + random.Next(RandomBounty + 1);
                amount = ScaleForFoundingPopulation(amount, star.Colonists);
                TechLevel.ResearchField field = MysteryTraderStep.FieldOrder[random.Next(MysteryTraderStep.FieldOrder.Length)];

                owner.ResearchResources[field] += amount;

                // Message 94: the colonists found an alien artifact that boosts research.
                serverState.AllMessages.Add(new Message(
                    owner.Id,
                    "Your colonists on " + star.Name + " have found an alien artifact. Studying it has given you "
                        + amount.ToString(CultureInfo.InvariantCulture) + " resources of research in " + field + ".",
                    MessageType,
                    null));
            }
        }

        /// <summary>
        /// Stand-in for the unspecified scaling below 1,000 founding colonists: linear,
        /// amount x colonists / 1,000 (truncated); 1,000 colonists or more keep the full amount.
        /// </summary>
        public static int ScaleForFoundingPopulation(int amount, int colonists)
        {
            if (colonists >= FullBountyColonists)
            {
                return amount;
            }

            return (int)((long)amount * Math.Max(0, colonists) / FullBountyColonists);
        }

        /// <summary>
        /// Galaxy creation (new-game-setup.md §1 option table, bit 0x80): unless "No Random Events"
        /// is set, 1 planet in 3 carries the artifact flag - one Next(3) roll per planet, flagged
        /// on 0. Only unowned planets are flagged, so a home world never carries one. Call it after
        /// the home worlds are assigned.
        /// </summary>
        public static void AssignAtCreation(ServerData serverState, Random random)
        {
            if (GameSettings.Data.NoRandomEvents)
            {
                return;
            }

            foreach (Star star in serverState.AllStars.Values)
            {
                if (star.Owner == Global.Nobody && random.Next(CreationOdds) == 0)
                {
                    star.HasArtifact = true;
                }
            }
        }
    }
}
