#region Copyright Notice
// ============================================================================
// Copyright (C) 2009 - 2017 stars-nova
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

namespace Nova.Ai
{
    using System;

    /// <summary>What the end-of-pass top-up decided for one planet (see
    /// <see cref="PlanetAdvisors.TopUp"/>).</summary>
    public struct TopUpResult
    {
        public int Terraform;
        public int Factories;
        public int Mines;
        public int Alchemy;

        /// <summary>Alchemy goes to the top of the queue after step 2a, to the bottom after
        /// step 2b.</summary>
        public bool AlchemyAtTop;
    }

    /// <summary>
    /// The arithmetic of the AI's per-planet production rules, behavior-specs-10/
    /// ai-opponent-behavior.md §4 and §6, kept free of game objects so each rule can be tested
    /// on its own. Every population figure here is in the original's stored units of 100
    /// colonists (§13); every division truncates, as in the original (all operands are
    /// non-negative where it matters).
    /// </summary>
    public static class PlanetAdvisors
    {
        /// <summary>§6 "Production-queue insertion cap".</summary>
        public const int MaxQueuedItems = 200;

        /// <summary>
        /// §6 population gate of the advisor chain (`FUN_1090_59e6`): category 0 takes planets of
        /// 40 units or more (4,000 colonists), every other category 60 units (6,000 colonists)
        /// or a planet carrying the urgent-supply flag.
        /// </summary>
        public static bool PassesAdvisorPopulationGate(int category, int populationUnits, bool urgentSupply)
        {
            if (category == AiCategory.Robotoids)
            {
                return populationUnits >= 40;
            }

            return populationUnits >= 60 || urgentSupply;
        }

        /// <summary>
        /// §6 Defenses advisor (`FUN_1090_58dc`): at 1,600 units (160,000 colonists) or more,
        /// with fewer defences than population ÷ 80 (one per 8,000 colonists), no manual
        /// Defenses item queued and a remaining cap C = limit - built - queued above 0, it
        /// queues min(C, 4) at the bottom of the queue, with no affordability check. Returns
        /// the quantity to queue (0 for none).
        /// </summary>
        public static int DefensesAdvisor(int populationUnits, int defenses, int defenseLimit, int queuedManualDefenses, bool manualDefensesQueued)
        {
            if (populationUnits < 1600 || defenses >= populationUnits / 80 || manualDefensesQueued)
            {
                return 0;
            }

            int cap = defenseLimit - defenses - queuedManualDefenses;
            return cap > 0 ? Math.Min(cap, 4) : 0;
        }

        /// <summary>
        /// §6 Terraform advisor (`FUN_1090_55b0`): never for category 0 (which does not call it)
        /// or category 4 (for which it returns at once). At 200 units (20,000 colonists) or
        /// more, with no manual Terraform item queued, some habitability axis off the race's
        /// ideal and a remaining cap C = terraform headroom - queued above 0, it queues
        /// min(C, 4) at the bottom of the queue, with no affordability check.
        /// </summary>
        public static int TerraformAdvisor(int category, int populationUnits, bool manualTerraformQueued, bool anyAxisOffIdeal, int terraformHeadroom, int queuedTerraform)
        {
            if (category == AiCategory.Robotoids || category == AiCategory.Cybertrons)
            {
                return 0;
            }

            if (populationUnits < 200 || manualTerraformQueued || !anyAxisOffIdeal)
            {
                return 0;
            }

            int cap = terraformHeadroom - queuedTerraform;
            return cap > 0 ? Math.Min(cap, 4) : 0;
        }

        /// <summary>
        /// §6 "Defences against bombers" (`FUN_1090_420c`), for a planet a foreign bomber fleet
        /// orbits. R is the planet's resources this year, the minerals are the projected stock
        /// (surface plus this year's mining), C the remaining Defenses cap. m = min(100, each
        /// mineral ÷ 5); n = R ÷ 25, less n ÷ 6 when above 5, capped at C; R' = R - R ÷ 10. If
        /// n ≤ m it queues n Defenses; otherwise e = max(0, (R' - a × m) ÷ 150) with a = 100, or
        /// 25 with Mineral Alchemy, and it queues m + e Defenses plus 5e Mineral Alchemy. Both
        /// go to the top of the queue (the caller's job).
        /// </summary>
        public static (int Defenses, int Alchemy) BomberDefence(
            int resources, int ironium, int boranium, int germanium, int defenseCap, bool manualDefensesQueued, bool mineralAlchemy)
        {
            if (manualDefensesQueued || resources <= 49 || defenseCap <= 0)
            {
                return (0, 0);
            }

            int m = Math.Min(100, Math.Min(ironium / 5, Math.Min(boranium / 5, germanium / 5)));
            m = Math.Max(0, m);

            int n = resources / 25;
            if (n > 5)
            {
                n -= n / 6;
            }

            n = Math.Min(n, defenseCap);

            int reducedResources = resources - (resources / 10);

            if (n <= m)
            {
                return (n, 0);
            }

            int a = mineralAlchemy ? 25 : 100;
            int e = Math.Max(0, (reducedResources - (a * m)) / 150);
            return (m + e, 5 * e);
        }

        /// <summary>The first turn the bomber-defence pass runs: (galaxy-size index + 2) × 10
        /// (§6).</summary>
        public static int BomberDefenceFirstTurn(int galaxySizeIndex)
        {
            return (galaxySizeIndex + 2) * 10;
        }

        /// <summary>
        /// The end-of-pass top-up (`FUN_10a8_1e8a`, §6, every step). The surplus figures are the
        /// projected availability (stock plus this year's mining, this year's resources) minus
        /// the full cost of everything already queued. The rooms are already floored at 0.
        /// </summary>
        public static TopUpResult TopUp(
            int category,
            long surplusIronium,
            long surplusBoranium,
            long surplusGermanium,
            long surplusResources,
            int factoryRoom,
            int mineRoom,
            int factoryResourceCost,
            int factoryGermaniumCost,
            int mineResourceCost,
            int alchemyResourceCost,
            int terraformAllowance,
            bool queueHeadIsAlchemy,
            bool allTechsAt26,
            int yearCounter)
        {
            TopUpResult result = new TopUpResult();
            long sR = surplusResources;

            if (sR < 0)
            {
                return result;
            }

            // Step 1, category 5 only: Terraform first, n = min(T, ceil(S_R / 70)).
            if (category == AiCategory.Macinti && terraformAllowance > 0)
            {
                long n = Math.Min(terraformAllowance, (sR + 69) / 70);
                if (n >= 1)
                {
                    result.Terraform = (int)n;
                    return result;
                }
            }

            bool afterStep2a;
            if (surplusIronium <= 0 || surplusBoranium <= 0 || surplusGermanium <= 0)
            {
                // Step 2a: no factories; nothing at all behind a queued Mineral Alchemy.
                if (queueHeadIsAlchemy)
                {
                    return result;
                }

                result.Mines = Units(mineRoom, sR, mineResourceCost);
                sR -= (long)result.Mines * mineResourceCost;
                afterStep2a = true;
            }
            else
            {
                // Step 2b: factories capped by germanium only, then by resources; then mines,
                // which are never checked against minerals.
                long f0 = Math.Max(0, factoryRoom);
                if (f0 >= 1 && factoryGermaniumCost > 0)
                {
                    f0 = Math.Min(f0, surplusGermanium / factoryGermaniumCost);
                }

                result.Factories = f0 >= 1 ? Units((int)f0, sR, factoryResourceCost) : 0;
                sR -= (long)result.Factories * factoryResourceCost;

                result.Mines = Units(mineRoom, sR, mineResourceCost);
                sR -= (long)result.Mines * mineResourceCost;
                afterStep2a = false;
            }

            // Step 3: one Mineral Alchemy unit more than the leftover pays for, once every
            // tech level is 26 after year 100.
            if (allTechsAt26 && yearCounter > 100 && alchemyResourceCost > 0)
            {
                result.Alchemy = (int)Math.Min(int.MaxValue, (sR / alchemyResourceCost) + 1);
                result.AlchemyAtTop = afterStep2a;
            }

            return result;
        }

        /// <summary>min(room, budget ÷ unit cost), floored at 0; a free unit is limited by its
        /// room alone.</summary>
        private static int Units(int room, long budget, int unitCost)
        {
            if (room <= 0 || budget < 0)
            {
                return 0;
            }

            if (unitCost <= 0)
            {
                return room;
            }

            return (int)Math.Min(room, budget / unitCost);
        }

        /// <summary>
        /// The colony-ship gate `FUN_1090_2baa` (§4, steps 1-8; the first step that decides
        /// wins). Step 6, the per-design "built" counter test, is treated as failing: Nova keeps
        /// no such counter, and the spec notes the difference B - (F + P) is usually negative
        /// anyway.
        /// </summary>
        /// <param name="skill">The skill field (0 Easy .. 3 Expert).</param>
        /// <param name="yearCounter">The turn counter, 0 in the first year.</param>
        /// <param name="hasColonyDesign">Whether any of the AI's designs is built on hull 14
        /// or 15 (Mini-Colony Ship, Colony Ship).</param>
        /// <param name="colonyFleetCount">F: own fleets holding at least one such ship.</param>
        /// <param name="galaxySizeIndex">0 Tiny .. 4 Huge.</param>
        /// <param name="knownOwnedPlanets">P: planets known to be owned by anyone.</param>
        /// <param name="galaxyPlanetCount">Every planet in the galaxy.</param>
        public static bool ColonyShipGate(
            int skill,
            int yearCounter,
            bool hasColonyDesign,
            int colonyFleetCount,
            int galaxySizeIndex,
            int knownOwnedPlanets,
            int galaxyPlanetCount,
            Random random)
        {
            // 1. Easy AI, odd years: refuse (from the very first year).
            if (skill == 0 && (yearCounter % 2) == 1)
            {
                return false;
            }

            // 2. Early game: allow.
            if (yearCounter < 30)
            {
                return true;
            }

            // 3. No colony design at all: refuse.
            if (!hasColonyDesign)
            {
                return false;
            }

            // 4. Too many colony fleets: 10 in a Tiny galaxy up to 90 in a Huge one.
            if (colonyFleetCount > (20 * galaxySizeIndex) + 10)
            {
                return false;
            }

            // 5. Colony fleets plus known owned planets past four fifths of the galaxy.
            if (colonyFleetCount + knownOwnedPlanets > (galaxyPlanetCount * 4) / 5)
            {
                return false;
            }

            // 6. The built-counter test: not available here, treated as failing.

            // 7. Small-fleet coin.
            if (colonyFleetCount < 5 && random.Next(2) == 0)
            {
                return true;
            }

            // 8. Otherwise refuse.
            return false;
        }

        /// <summary>
        /// The isolation check `FUN_1090_6012` (§2, §6) gating colony-ship production once the
        /// turn counter exceeds 59: measured from the producing planet to the nearest planet the
        /// AI does not own, it refuses beyond 350 ly (squared distance above 122,500), faces a
        /// 50% refusal roll beyond 300 ly and another beyond 250 ly, and always passes below.
        /// </summary>
        public static bool PassesIsolationCheck(int yearCounter, double nearestNonOwnedDistanceSquared, Random random)
        {
            if (yearCounter <= 59)
            {
                return true;
            }

            if (nearestNonOwnedDistanceSquared > 122500)
            {
                return false;
            }

            if (nearestNonOwnedDistanceSquared > 90000 && random.Next(2) == 0)
            {
                return false;
            }

            if (nearestNonOwnedDistanceSquared > 62500 && random.Next(2) == 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The galaxy-size index s (0 Tiny .. 4 Huge), recovered from the map width the same way
        /// the server's RandomEventsStep.GalaxySizeIndex does (the original's diameter is
        /// (s + 1) × 400). Duplicated because Nova.Ai cannot reference the server assembly.
        /// </summary>
        public static int GalaxySizeIndex(int mapWidth)
        {
            return Math.Max(0, Math.Min(4, (mapWidth / 400) - 1));
        }
    }
}
