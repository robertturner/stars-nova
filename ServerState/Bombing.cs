#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 The Stars-Nova Project
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Orbital bombardment - behavior-specs-10/turn-generation-engine.md §4, "Orbital
    /// bombardment, complete rule" (FUN_10f0_6ea2 with its gate FUN_10f0_6e2e, the fleet
    /// totaller FUN_1038_0dae and the defence routine FUN_1038_0262). Once per turn, after the
    /// battle pass: every orbiting fleet whose battle plan allows it to act against the owner of
    /// the (foreign, starbase-less) planet it orbits bombs it, pooled with every later fleet of
    /// the same owner at the same planet. Population is in units of 100 colonists throughout.
    /// No tech-gain roll follows a bombardment: combat-resolution.md §9's older note that bombing
    /// invokes the battle tech-gain check is superseded by turn-generation-engine.md §5, which
    /// lists all six call sites of the salvage/tech roller FUN_10f0_61a2 (battle salvage
    /// :103191/:103316/:103433, Scrap Fleet :75713/:75813, planet capture :77896) - none inside
    /// FUN_10f0_6ea2 (:103680-103989) - and §4's complete bombardment rule has no such step.
    /// </summary>
    public class Bombing
    {
        /// <summary>Most terraforming points one bombardment can undo (§4, "Terraform reversal").</summary>
        public const int MaxRetroBombPoints = 500;

        /// <summary>Name of the one Retro Bomb part; it carries no Bomb property of its own.</summary>
        public const string RetroBombName = "Retro Bomb";

        private readonly ServerData serverState;
        // The injected random, or null: the rolls then come from the ambient game stream
        // (GameRandom.Current - the bombing step's own seeded stream in a generation).
        private readonly Random random;

        public Bombing(ServerData serverState)
            : this(serverState, null)
        {
        }

        /// <summary>
        /// As <see cref="Bombing(ServerData)"/>, with the random source used for the stochastic
        /// rounding of deaths and installation losses (injectable for tests).
        /// </summary>
        public Bombing(ServerData serverState, Random random)
        {
            this.serverState = serverState;
            this.random = random;
        }

        /// <summary>
        /// A fleet group's bombing totals, §4 "Fleet totals" (per ship, times the ship count of
        /// each design).
        /// </summary>
        public class BombTotals
        {
            /// <summary>N: normal kill rate, tenths of a percent.</summary>
            public int NormalKill;

            /// <summary>M: minimum kill, population units (100 colonists).</summary>
            public int MinimumKill;

            /// <summary>D: installations destroyed.</summary>
            public int Installations;

            /// <summary>S: compounded smart kill rate, tenths of a percent (0-1000).</summary>
            public int SmartKill;

            /// <summary>R: Retro Bomb count (terraform points undone).</summary>
            public int RetroCount;

            /// <summary>True if anything in the group bombs at all.</summary>
            public bool AnyBombs
            {
                get { return NormalKill > 0 || MinimumKill > 0 || Installations > 0 || SmartKill > 0 || RetroCount > 0; }
            }
        }

        /// <summary>
        /// What one bombardment did, for messages and tests.
        /// </summary>
        public class BombingResult
        {
            public int ColonistsKilled;
            public int FactoriesDestroyed;
            public int DefensesDestroyed;
            public int MinesDestroyed;
            public int TerraformPointsUndone;
            public bool Depopulated;

            /// <summary>Normal pass-through F (1 with no working defences).</summary>
            public double PassThrough = 1.0;

            public int InstallationsDestroyed
            {
                get { return FactoriesDestroyed + DefensesDestroyed + MinesDestroyed; }
            }
        }

        /// <summary>
        /// The whole bombardment pass (§1 step 23b). Fleets are taken in fleet-table order; a
        /// fleet already pooled into an earlier fleet's bombardment this pass is skipped.
        /// </summary>
        public void BombAll()
        {
            List<Fleet> allFleets = serverState.IterateAllFleets().ToList();
            HashSet<Fleet> pooled = new HashSet<Fleet>();

            for (int i = 0; i < allFleets.Count; i++)
            {
                Fleet fleet = allFleets[i];
                if (pooled.Contains(fleet) || fleet.Composition.Count == 0 || fleet.IsStarbase)
                {
                    continue;
                }

                Star star = OrbitedStar(fleet);
                if (star == null || !MayBomb(fleet, star))
                {
                    continue;
                }

                // Pooling: every later fleet of the same owner orbiting the same planet joins
                // in. Only the first fleet's battle plan was tested.
                List<Fleet> group = new List<Fleet> { fleet };
                for (int j = i + 1; j < allFleets.Count; j++)
                {
                    Fleet other = allFleets[j];
                    if (other.Owner == fleet.Owner && !pooled.Contains(other) && other.Composition.Count > 0
                        && !other.IsStarbase && OrbitedStar(other) == star)
                    {
                        group.Add(other);
                    }
                }

                BombTotals totals = Totals(group);
                if (!totals.AnyBombs)
                {
                    // "Finally the fleet must carry something that bombs, or nothing happens."
                    continue;
                }

                foreach (Fleet member in group)
                {
                    pooled.Add(member);
                }

                Bomb(group, star, totals);
            }
        }

        /// <summary>
        /// Single-fleet entry point: bombs <paramref name="star"/> with this one fleet if the
        /// gate allows it.
        /// </summary>
        public BombingResult Bomb(Fleet fleet, Star star)
        {
            if (fleet == null || star == null || !MayBomb(fleet, star))
            {
                return null;
            }

            List<Fleet> group = new List<Fleet> { fleet };
            BombTotals totals = Totals(group);
            return totals.AnyBombs ? Bomb(group, star, totals) : null;
        }

        /// <summary>
        /// The gate (FUN_10f0_6e2e): the planet must be owned by another race and have no
        /// starbase (any starbase, armed or not, prevents all bombing), and the fleet's battle
        /// plan "Attack Who" applied to the fleet owner's OWN opinion of the planet owner must
        /// allow it - the shared <see cref="BattleEngine.IsLegitimateTarget"/> rule.
        /// </summary>
        public bool MayBomb(Fleet fleet, Star star)
        {
            if (star.Owner == Global.Nobody || star.Owner == fleet.Owner || star.Starbase != null)
            {
                return false;
            }

            return BattleEngine.IsLegitimateTarget(serverState, fleet.Owner, fleet.BattlePlan, star.Owner);
        }

        /// <summary>
        /// The fleet totaller (FUN_1038_0dae), §4 "Fleet totals": N sums each non-smart bomb's
        /// kill figure (tenths of a percent) times its quantity, plus 20 per Multi Contained
        /// Munition; M is the minimum kill in population units (3 per bomb of subtypes 0-4 and
        /// per Munition, 20 per Orbital Construction Module, nothing for LBUs or Hush-a-Boom -
        /// read from each part's own MinimumKill, stored in colonists); D sums installation
        /// figures, plus 5 per Munition; every smart bomb unit multiplies a survival figure by
        /// (1 - kill / 1000) and S = 1000 x (1 - survival) rounded to nearest, capped at 1000;
        /// R counts Retro Bombs.
        /// </summary>
        public static BombTotals Totals(IEnumerable<Fleet> fleets)
        {
            BombTotals totals = new BombTotals();
            double survival = 1.0;

            foreach (Fleet fleet in fleets)
            {
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    ShipDesign design = token.Design;
                    if (design?.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
                    {
                        continue;
                    }

                    foreach (HullModule module in design.Hull.Modules)
                    {
                        Component part = module.AllocatedComponent;
                        if (part == null || module.ComponentCount <= 0)
                        {
                            continue;
                        }

                        int units = module.ComponentCount * token.Quantity;

                        if (part.Name == Fleet.MultiContainedMunitionName)
                        {
                            totals.NormalKill += 20 * units;
                            totals.MinimumKill += 3 * units;
                            totals.Installations += 5 * units;
                        }

                        if (part.Name == RetroBombName)
                        {
                            totals.RetroCount += units;
                        }

                        if (part.Properties.TryGetValue("Bomb", out ComponentProperty property) && property is Bomb bomb)
                        {
                            // PopKill is stored as a percentage (2.5 = 2.5%); the rule works in
                            // tenths of a percent.
                            int killTenths = (int)Math.Round(bomb.PopKill * 10.0);
                            totals.MinimumKill += (bomb.MinimumKill / 100) * units;

                            if (bomb.IsSmart)
                            {
                                for (int i = 0; i < units && killTenths > 0; i++)
                                {
                                    survival *= 1.0 - (killTenths / 1000.0);
                                }
                            }
                            else
                            {
                                totals.NormalKill += killTenths * units;
                                totals.Installations += bomb.Installations * units;
                            }
                        }
                    }
                }
            }

            totals.SmartKill = Math.Min(1000, (int)Math.Floor((1000.0 * (1.0 - survival)) + 0.5));
            return totals;
        }

        /// <summary>
        /// The per-defence coverage figure c, in tenths of a percent, of the best planetary
        /// defence the planet's owner can build: SDI 10, Missile Battery 20, Laser Battery 24,
        /// Planetary Shield 30, Neutron Shield 38 (§4 "Planetary defences"). Read from the owner's
        /// available components; when the owner has no component list at all (e.g. a bare test
        /// world) the planet's stored DefenseType names the defence instead.
        /// </summary>
        public static int DefenceCoverage(Star star, EmpireData owner)
        {
            if (owner?.AvailableComponents != null && owner.AvailableComponents.Count > 0)
            {
                if (owner.AvailableComponents.Contains("Neutron Shield"))
                {
                    return 38;
                }

                if (owner.AvailableComponents.Contains("Planetary Shield"))
                {
                    return 30;
                }

                if (owner.AvailableComponents.Contains("Laser Battery"))
                {
                    return 24;
                }

                if (owner.AvailableComponents.Contains("Missile Battery"))
                {
                    return 20;
                }

                return owner.AvailableComponents.Contains("SDI") ? 10 : 0;
            }

            switch (star.DefenseType)
            {
                case "Neutron":
                    return 38;
                case "Planet":
                    return 30;
                case "Laser":
                    return 24;
                case "Missile":
                    return 20;
                case "SDI":
                    return 10;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// K, the working defences: the smaller of the defences built and the operable maximum
        /// (FUN_1048_4f18), §4 "Planetary defences". A planet with no race information at all
        /// (no ThisRace, so no operable figure can be worked out) counts every defence built.
        /// </summary>
        public static int WorkingDefences(Star star)
        {
            if (star.ThisRace == null)
            {
                return Math.Max(0, star.Defenses);
            }

            return Math.Max(0, Math.Min(star.Defenses, star.GetOperableDefenses()));
        }

        /// <summary>
        /// Applies one (possibly pooled) bombardment to a planet: defence scaling, population,
        /// installations, terraform reversal and messages, §4.
        /// </summary>
        private BombingResult Bomb(List<Fleet> group, Star star, BombTotals totals)
        {
            BombingResult result = new BombingResult();
            int owner = star.Owner;
            serverState.AllEmpires.TryGetValue(owner, out EmpireData ownerData);

            // Planetary defences: F = (1 - c/1000)^K, F_s = (1 - c/2000)^K. When F is below 1
            // the totals become N x F, M x F, S x F_s and D x (1 - (1 - F)/2), each rounded half
            // up; R loses (1 - F) x R / 2 rounded down.
            int coverage = DefenceCoverage(star, ownerData);
            int working = WorkingDefences(star);
            double passThrough = 1.0;
            double smartPassThrough = 1.0;
            if (coverage > 0 && working > 0)
            {
                passThrough = Math.Pow(1.0 - (coverage / 1000.0), working);
                smartPassThrough = Math.Pow(1.0 - (coverage / 2000.0), working);
            }

            int normalKill = totals.NormalKill;
            int minimumKill = totals.MinimumKill;
            int smartKill = totals.SmartKill;
            int installations = totals.Installations;
            int retro = totals.RetroCount;

            if (passThrough < 1.0)
            {
                normalKill = RoundHalfUp(normalKill * passThrough);
                minimumKill = RoundHalfUp(minimumKill * passThrough);
                smartKill = RoundHalfUp(smartKill * smartPassThrough);
                installations = RoundHalfUp(installations * (1.0 - ((1.0 - passThrough) / 2.0)));
                retro -= (int)Math.Floor((1.0 - passThrough) * retro / 2.0);
            }

            result.PassThrough = passThrough;

            // Population (units of 100 colonists): smart deaths first, then normal deaths with
            // the remainder resolved stochastically on the 0-999 draw, then the minimums.
            int population = star.Colonists / 100;
            if ((normalKill > 0 || minimumKill > 0 || smartKill > 0) && population > 0)
            {
                int smartDeaths = (int)((long)population * smartKill / 1000);
                if (smartDeaths >= population)
                {
                    smartDeaths = population - 1;
                }

                long normalProduct = (long)(population - smartDeaths) * normalKill;
                int normalDeaths = (int)(normalProduct / 1000);
                int remainder = (int)(normalProduct % 1000);
                if (remainder != 0 && (random ?? GameRandom.Current).Next(0, 1000) <= remainder)
                {
                    normalDeaths++;
                }

                int kill = smartDeaths + normalDeaths;
                if (normalKill > 0 && kill < 1)
                {
                    kill = 1;
                }

                if (kill < minimumKill)
                {
                    kill = minimumKill;
                }

                kill = Math.Min(kill, population);

                if (kill >= population)
                {
                    result.ColonistsKilled = star.Colonists;
                    star.Colonists = 0;
                }
                else
                {
                    result.ColonistsKilled = kill * 100;
                    star.Colonists -= kill * 100;
                }
            }

            // Installations: factories f x D / T and defences d x D / T, each rounded down plus
            // one more with probability remainder / T, capped at what is present; mines lose the
            // rest, capped, never negative.
            int mines = star.Mines;
            int factories = star.Factories;
            int defenses = star.Defenses;
            int total = mines + factories + defenses;
            if (installations > 0 && total > 0)
            {
                result.FactoriesDestroyed = Math.Min(factories, ProportionalLoss(factories, installations, total));
                result.DefensesDestroyed = Math.Min(defenses, ProportionalLoss(defenses, installations, total));
                result.MinesDestroyed = Math.Max(0, Math.Min(mines, installations - result.FactoriesDestroyed - result.DefensesDestroyed));

                star.Factories -= result.FactoriesDestroyed;
                star.Defenses -= result.DefensesDestroyed;
                star.Mines -= result.MinesDestroyed;
            }

            // Terraform reversal: R (capped at 500) moves each environment axis back toward its
            // original value by at most R, never past it.
            if (retro > 0)
            {
                int step = Math.Min(retro, MaxRetroBombPoints);
                int gravity = star.Gravity;
                int temperature = star.Temperature;
                int radiation = star.Radiation;
                star.Gravity = TowardOriginal(gravity, star.OriginalGravity, step);
                star.Temperature = TowardOriginal(temperature, star.OriginalTemperature, step);
                star.Radiation = TowardOriginal(radiation, star.OriginalRadiation, step);
                result.TerraformPointsUndone = Math.Abs(gravity - star.Gravity)
                    + Math.Abs(temperature - star.Temperature)
                    + Math.Abs(radiation - star.Radiation);
            }

            result.Depopulated = star.Colonists <= 0;

            // Messages are addressed while the planet still has its owner - previously the owner
            // message was addressed after Owner was cleared, so the former owner never got it.
            SendMessages(group, star, owner, result);

            if (result.Depopulated)
            {
                // A planet whose population reaches 0 loses its owner (FUN_1048_56fc).
                ownerData?.OwnedStars.Remove(star);
                star.ManufacturingQueue.Clear();
                star.Colonists = 0;
                star.Mines = 0;
                star.Factories = 0;
                star.Owner = Global.Nobody;
            }

            return result;
        }

        /// <summary>count x D / T rounded down, plus one with probability (remainder / T).</summary>
        private int ProportionalLoss(int count, int destroyed, int total)
        {
            long product = (long)count * destroyed;
            int loss = (int)(product / total);
            int remainder = (int)(product % total);
            if (remainder > 0 && (random ?? GameRandom.Current).Next(0, total) < remainder)
            {
                loss++;
            }

            return loss;
        }

        private static int TowardOriginal(int current, int original, int step)
        {
            if (current > original)
            {
                return Math.Max(original, current - step);
            }

            return Math.Min(original, current + step);
        }

        private static int RoundHalfUp(double value)
        {
            return (int)Math.Floor(value + 0.5);
        }

        /// <summary>
        /// §4 "Messages": retro bombing that moved anything sends the retro message (302 for one
        /// fleet, 378/379 for several); otherwise an emptied planet gets the "everyone killed"
        /// pair (143/144, 380/381), installations lost (with or without colonists) the
        /// installation forms (97-100/107-110, 359-362/369-372) - the defence variant when F is
        /// not exactly 1, reporting the stopped share - and colonists alone the plain kill form
        /// (96/106, 358/368) with no defence variant. Wording is Nova's own.
        /// </summary>
        private void SendMessages(List<Fleet> group, Star star, int owner, BombingResult result)
        {
            Fleet first = group[0];
            bool several = group.Count > 1;
            string bomberSubject = several ? "Your fleets" : "Your fleet " + first.Name;
            string attackerName = serverState.AllEmpires.TryGetValue(first.Owner, out EmpireData attacker) && attacker.Race != null
                ? attacker.Race.Name
                : "Enemy";
            string ownerSubject = several ? attackerName + " fleets have" : "A " + attackerName + " fleet has";

            string bomberText = null;
            string ownerText = null;

            if (result.TerraformPointsUndone > 0)
            {
                bomberText = bomberSubject + (several ? " have" : " has") + " retro-bombed " + star.Name
                    + ", undoing " + result.TerraformPointsUndone + "% of its terraforming.";
                ownerText = ownerSubject + " retro-bombed " + star.Name
                    + ", undoing " + result.TerraformPointsUndone + "% of its terraforming.";
            }
            else if (result.Depopulated && result.ColonistsKilled > 0)
            {
                bomberText = bomberSubject + (several ? " have" : " has") + " bombed " + star.Name + ", killing all of its colonists.";
                ownerText = ownerSubject + " bombed " + star.Name + ", killing all of your colonists there.";
            }
            else if (result.InstallationsDestroyed > 0)
            {
                string installationsText = result.InstallationsDestroyed == 1
                    ? "one installation"
                    : result.InstallationsDestroyed + " installations";
                string killedText = result.ColonistsKilled > 0
                    ? "killing " + result.ColonistsKilled.ToString(CultureInfo.InvariantCulture) + " colonists and destroying "
                    : "destroying ";

                bomberText = bomberSubject + (several ? " have" : " has") + " bombed " + star.Name + ", " + killedText + installationsText + ".";
                ownerText = ownerSubject + " bombed " + star.Name + ", " + killedText + installationsText + ".";

                if (result.PassThrough != 1.0)
                {
                    // The stopped share is passed as (1 - F) x 10,000, hundredths of a percent.
                    int stopped = (int)((1.0 - result.PassThrough) * 10000);
                    string percent = (stopped / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + "%";
                    bomberText += " Planetary defenses stopped " + percent + " of the bombs.";
                    ownerText += " Your planetary defenses destroyed " + percent + " of the incoming bombs.";
                }
            }
            else if (result.ColonistsKilled > 0)
            {
                bomberText = bomberSubject + (several ? " have" : " has") + " bombed " + star.Name + ", killing "
                    + result.ColonistsKilled.ToString(CultureInfo.InvariantCulture) + " colonists.";
                ownerText = ownerSubject + " bombed " + star.Name + ", killing "
                    + result.ColonistsKilled.ToString(CultureInfo.InvariantCulture) + " colonists.";
            }

            if (bomberText == null)
            {
                return;
            }

            serverState.AllMessages.Add(new Message(first.Owner, bomberText, "Bombing", null));
            serverState.AllMessages.Add(new Message(owner, ownerText, "Bombing", null));
        }

        private Star OrbitedStar(Fleet fleet)
        {
            if (fleet.InOrbit == null)
            {
                return null;
            }

            serverState.AllStars.TryGetValue(fleet.InOrbit.Name, out Star star);
            return star;
        }
    }
}
