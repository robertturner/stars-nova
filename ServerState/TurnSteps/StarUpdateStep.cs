#region Copyright Notice
// ============================================================================
// Copyright (C) 2011 The Stars-Nova Project
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

namespace Nova.Server.TurnSteps
{
    using System;
    using System.Collections.Generic;
    
    using Nova.Common;
    using Nova.Common.Components;
    
    /// <summary>
    /// Updates Stars, Manufacturing and Research.
    /// </summary>
    public class StarUpdateStep : ITurnStep
    {
        private ServerData serverState;
        private Manufacture manufacture;
        // The injected test random, or null: each Process then derives this step's own seeded
        // streams from the game (ServerData.CreateRandom), so the step is repeatable.
        private readonly Random injectedRandom;
        private Random random;

        public StarUpdateStep() : this(null)
        {
        }

        /// <summary>Overload for deterministic testing of Claim Adjuster's probabilistic planet
        /// drift (see ApplyClaimAdjusterPlanetDrift) - a test can pass a Random subclass whose
        /// NextDouble()/Next(int) are overridden to fixed values instead of depending on a real
        /// seed's exact output sequence.</summary>
        public StarUpdateStep(Random random)
        {
            this.injectedRandom = random;
            this.random = random;
        }

        public void Process(ServerData serverState)
        {
            this.serverState = serverState;
            random = injectedRandom ?? serverState.CreateRandom("StarUpdate");
            manufacture = new Manufacture(serverState, injectedRandom ?? serverState.CreateRandom("Manufacture"));
            spentThisTurn.Clear();

            foreach (Star star in serverState.AllStars.Values)
            {                
                if (star.Owner == Global.Nobody || star.Colonists == 0)
                {
                    continue;
                }
                
                star.UpdateMinerals();
                
                // According to the allocated budget submited, update star resources.
                // Note that this sets the allocation for research to zero for all stars
                // which have "contribute only leftover resources to research". This
                // makes those stars be handled after manufacturing.
                star.UpdateResearch(serverState.AllEmpires[star.Owner].ResearchBudget);
                star.UpdateResources();

                // Ultimate Recycling (behavior-specs-9/production-queue.md 10g, "Resource
                // funding"): this generation's recycled accumulator d (filled by ScrapFleetStep,
                // which runs before this step) is blended into the planet's output r as
                // r + d x r / (d + r) BEFORE the research share and queue funding; the remainder
                // is lost. See Star.RecycledScrapResources.
                if (star.RecycledScrapResources != 0)
                {
                    int blended = Nova.Common.Waypoints.ScrapTask.BlendRecycledResources(star.GetResourceRate(), star.RecycledScrapResources);
                    int budget = serverState.AllEmpires[star.Owner].ResearchBudget;
                    if (!star.OnlyLeftover && budget >= 0 && budget <= 100)
                    {
                        star.ResearchAllocation = (blended * budget) / 100;
                    }

                    star.ResourcesOnHand.Energy = blended - star.ResearchAllocation;
                }

                ContributeAllocatedResearch(star);
                
                int initialPopulation = star.Colonists;
                star.UpdatePopulation(serverState.AllEmpires[star.Owner].Race);
                int finalPopulation = star.Colonists;
    
                if (finalPopulation < initialPopulation)
                {
                    int died = initialPopulation - finalPopulation;
                    Message message = new Message();
                    message.Audience = star.Owner;
                    message.Text = died.ToString(System.Globalization.CultureInfo.InvariantCulture)
                       + " of your colonists have been killed"
                       + " by the environment on " + star.Name;
                    serverState.AllMessages.Add(message);
                }
                
                if (serverState.AllEmpires[star.Owner].Race.HasTrait("AR"))
                {
                    // Alternate Reality's scan range is population-derived, not tech/component
                    // driven, and so is recomputed every turn rather than only on tech unlock.
                    // See docs/behavior-specs/race-traits.md §5.
                    star.ScanRange = (int)Math.Sqrt(star.Colonists / 10.0);
                }

                if (serverState.AllEmpires[star.Owner].Race.HasTrait("CA"))
                {
                    ApplyClaimAdjusterTerraforming(star, serverState.AllEmpires[star.Owner].Race);
                    ApplyClaimAdjusterPlanetDrift(star, serverState.AllEmpires[star.Owner].Race);
                }

                manufacture.Items(star);

                ContributeLeftoverResearch(star);

                star.UpdateResearch(serverState.AllEmpires[star.Owner].ResearchBudget);
                star.UpdateResources();

                // The Ultimate Recycling accumulator lives for this generation only: it was spent
                // (blended) above and nothing carries into the next turn (production-queue.md 10g).
                star.RecycledScrapResources = 0;
            }

            ApplySuperStealthResearchBonus(serverState);
        }

        /// <summary>
        /// Claim Adjuster's automatic, free terraforming - "instantaneous every year up to
        /// current tech" (behavior-specs-7/race-traits.md §2). Reuses the same worst-axis-first
        /// selection and flat 15%/30% cap TerraformProductionUnit's paid version already applies
        /// (this codebase's own disclosed simplification for "up to current tech", since tech-
        /// level-based terraform caps aren't modeled anywhere here) - the only difference is CA
        /// pays no resource cost at all and needs no queued order, so one axis improves by 1%
        /// every single turn rather than only once enough resources accumulate.
        ///
        /// Not implemented: the spec's parenthetical "(reverts if the planet changes hands)" -
        /// that would require tracking how much of a star's current environment delta came from
        /// CA's free ability specifically (as opposed to ordinary paid terraforming, which does
        /// NOT revert), separately from every other terraform source, and hooking every place
        /// ownership can change hands (colonization, invasion). Disclosed gap, not attempted here.
        /// </summary>
        private static void ApplyClaimAdjusterTerraforming(Star star, Race race)
        {
            string axis = TerraformProductionUnit.SelectAxisToImprove(star, race);
            if (axis != null)
            {
                TerraformProductionUnit.ImproveAxis(star, race, axis);
            }
        }

        /// <summary>
        /// Claim Adjuster's separate "planet drift": a 10%-per-year chance (behavior-specs-7/
        /// race-traits.md §3a's recovered client text) that ONE randomly-chosen environment axis
        /// (not necessarily the worst one - unlike the deterministic terraforming above) nudges
        /// 1% toward the race's ideal, permanently. No cap is given in the spec for this
        /// (unlike the terraforming above), so it isn't capped here either - over a long enough
        /// game this can in principle push a stat past the 15%/30% terraforming ceiling.
        /// </summary>
        private void ApplyClaimAdjusterPlanetDrift(Star star, Race race)
        {
            if (random.NextDouble() >= 0.10)
            {
                return;
            }

            string[] axes = { "Gravity", "Temperature", "Radiation" };
            string axis = axes[random.Next(axes.Length)];
            TerraformProductionUnit.ImproveAxis(star, race, axis);
        }

        /// <summary>
        /// Contributes allocated research from the star.
        /// </summary>
        /// <param name="star">Star to process.</param>
        /// <remarks>
        /// Note that stars which contribute only leftovers are not accounted for.
        /// </remarks>
        private void ContributeAllocatedResearch(Star star)
        {
            if (star.Owner == Global.Nobody)
            {
                return;
            }

            int amount = star.ResearchAllocation;
            star.ResearchAllocation = 0;
            ContributeResearch(star, amount);
        }

        private void ContributeLeftoverResearch(Star star)
        {
            if (star.Owner == Global.Nobody)
            {
                return;
            }

            int amount = star.ResourcesOnHand.Energy;
            star.ResourcesOnHand.Energy = 0;
            ContributeResearch(star, amount);
        }

        /// <summary>
        /// Splits a pool of research resources across tech fields and applies any level-ups
        /// this immediately affords. Normally all of <paramref name="amount"/> goes to the
        /// empire's single selected field. With the Generalized Research (GR) trait, only half
        /// goes to the selected field, and 15% of the same amount is additionally applied to
        /// each of the other five fields — see docs/behavior-specs/race-traits.md §3 and
        /// research-tech-tree.md §4. This yields 125% total research value for the same
        /// resource spend, at the cost of not being able to rush a single field.
        /// </summary>
        private void ContributeResearch(Star star, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            EmpireData empire = serverState.AllEmpires[star.Owner];

            TechLevel targetAreas = empire.ResearchTopics;
            TechLevel.ResearchField targetArea = TechLevel.ResearchField.Energy; // default to Energy.

            // Find the first research priority
            // TODO: Implement a proper hierarchy of research ("next research field") system.
            foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (targetAreas[area] == 1)
                {
                    targetArea = area;
                    break;
                }
            }

            int targetLevelBefore = empire.ResearchLevels[targetArea];

            if (empire.Race.HasTrait("GR"))
            {
                foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
                {
                    double share = (area == targetArea) ? 0.5 : 0.15;
                    int spent = (int)(amount * share);
                    empire.ResearchResources[area] += spent;
                    RecordSpending(empire, area, spent);
                    ApplyLevelUps(area, empire);
                }
            }
            else
            {
                empire.ResearchResources[targetArea] += amount;
                RecordSpending(empire, targetArea, amount);
                ApplyLevelUps(targetArea, empire);
            }

            if (empire.ResearchLevels[targetArea] > targetLevelBefore)
            {
                SwitchToNextField(empire, targetArea);
            }
        }

        /// <summary>
        /// The "next field to research" setting (research-tech-tree.md section 4): once the
        /// current target gains a level, research moves to the chosen next field, or to the
        /// lowest field (the PRT exclusions of section 7 applied); "same field" stays put.
        /// </summary>
        private static void SwitchToNextField(EmpireData empire, TechLevel.ResearchField current)
        {
            TechLevel.ResearchField? next = Research.NextTarget(empire.ResearchNextField, empire.ResearchLevels, empire.Race);
            if (next == null || next.Value == current)
            {
                return;
            }

            TechLevel topics = new TechLevel();
            topics[next.Value] = 1;
            empire.ResearchTopics = topics;
        }

        /// <summary>Research resources each empire put into each field this generation, for
        /// Super Stealth's passive bonus (see <see cref="ApplySuperStealthResearchBonus"/>).</summary>
        private readonly Dictionary<int, long[]> spentThisTurn = new Dictionary<int, long[]>();

        private void RecordSpending(EmpireData empire, TechLevel.ResearchField field, int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            if (!spentThisTurn.TryGetValue(empire.Id, out long[] spent))
            {
                spent = new long[6];
                spentThisTurn[empire.Id] = spent;
            }

            spent[(int)field] += amount;
        }

        /// <summary>
        /// Super Stealth's passive research (research-tech-tree.md section 4 and Open Questions,
        /// FUN_10b8_4ce4): after the ordinary research of every race, and only when more than
        /// one race is in the game, each Super Stealth race gains in every field half the
        /// average every race (itself included) spent in that field this year - the field's
        /// total divided by the number of races, then halved - and, if anything was granted, the
        /// buy loop runs again so the bonus can buy levels the same year.
        /// </summary>
        public void ApplySuperStealthResearchBonus(ServerData serverState)
        {
            this.serverState = serverState;
            int raceCount = serverState.AllEmpires.Count;
            if (raceCount <= 1)
            {
                return;
            }

            long[] totals = new long[6];
            foreach (long[] spent in spentThisTurn.Values)
            {
                for (int field = 0; field < totals.Length; field++)
                {
                    totals[field] += spent[field];
                }
            }

            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (empire.Race == null || !empire.Race.HasTrait("SS"))
                {
                    continue;
                }

                bool granted = false;
                foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
                {
                    int bonus = (int)(totals[(int)area] / raceCount / 2);
                    if (bonus > 0)
                    {
                        empire.ResearchResources[area] += bonus;
                        granted = true;
                    }
                }

                if (granted)
                {
                    foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
                    {
                        ApplyLevelUps(area, empire);
                    }
                }
            }
        }

        /// <summary>
        /// The research buy loop on what is already banked, with no new income (the original's
        /// FUN_10b8_4ce4(0), behavior-specs-10/turn-generation-engine.md §1 steps 12e/23g), for
        /// one empire's six fields - the seam the Mystery Trader's technology reward uses (§5a).
        /// </summary>
        public void SpendBankedResearch(ServerData serverState, EmpireData empire)
        {
            this.serverState = serverState;
            foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                ApplyLevelUps(area, empire);
            }
        }

        /// <summary>
        /// Raises one field by one level at no research cost, with the usual tech-advance message
        /// and component unlocks (the Mystery Trader's planet trade, turn-generation-engine.md §5a).
        /// Does nothing at the research cap.
        /// </summary>
        public void RaiseTechLevel(ServerData serverState, EmpireData empire, TechLevel.ResearchField area)
        {
            this.serverState = serverState;
            if (empire.ResearchLevels[area] < TechLevel.MaxLevel)
            {
                TechLevelUp(area, empire);
            }
        }

        /// <summary>
        /// Applies as many tech-level-ups in <paramref name="area"/> as the empire's banked
        /// research resources for that field can currently afford.
        /// </summary>
        private void ApplyLevelUps(TechLevel.ResearchField area, EmpireData empire)
        {
            while (true)
            {
                if (empire.ResearchLevels[area] >= TechLevel.MaxLevel)
                {
                    // Research.Cost's base-cost table only has entries for levels
                    // 1..MaxLevel; asking for MaxLevel + 1 would throw. Nothing more to
                    // research here - any banked resources for this field just sit unused.
                    break;
                }

                int cost = Research.Cost(area, empire.Race, empire.ResearchLevels, empire.ResearchLevels[area] + 1);

                // A race record with no research cost class for the field (cost 0) never buys
                // levels for free: the buy loop now also runs on banked pools only
                // (ResearchBuyLoopStep), where a zero price would otherwise climb to the cap.
                if (cost <= 0)
                {
                    break;
                }

                if (empire.ResearchResources[area] >= cost)
                {
                    empire.ResearchResources[area] -= cost;
                    TechLevelUp(area, empire);
                }
                else
                {
                    break;
                }
            }
        }
        
        /// <summary>
        /// Report an update in tech level and any new components that have became
        /// available.
        /// </summary>
        private void TechLevelUp(TechLevel.ResearchField area, EmpireData empire)
        {
            TechLevel oldResearchLevel = empire.ResearchLevels.Clone();
            empire.ResearchLevels[area]++;
            TechLevel newResearchLevel = empire.ResearchLevels;
            
            Message techAdvanceMessage = new Message(
                empire.Id,
                "Your race has advanced to Tech Level " + empire.ResearchLevels[area] + " in the " + area.ToString() + " field",
                "TechAdvance",
                null);
            
            serverState.AllMessages.Add(techAdvanceMessage);

            AllComponents allComponents = new AllComponents();

            // Stable name order (GetAllInNameOrder), so the new-component messages and the
            // available-component list are written in the same order in every process.
            foreach (Component component in allComponents.GetAllInNameOrder)
            {
                // The 12 one-time-battle-grant specials (see SpecialComponentGrants) additionally
                // require the empire to have actually been awarded that specific component - tech
                // level alone crossing their (often very high) RequiredTech threshold isn't enough.
                bool isUngrantedSpecial = SpecialComponentGrants.IsSpecialGrant(component.Name)
                    && !empire.GrantedSpecialComponents.Contains(component.Name);

                // Trait gates run before, and never replace, the tech check: crossing a component's
                // threshold must not hand a race a part its traits bar (Space Dock without Improved
                // Starbases, a ram scoop to No Ram Scoop Engines, ...).
                if (oldResearchLevel < component.RequiredTech && newResearchLevel >= component.RequiredTech && !isUngrantedSpecial
                    && !RaceComponents.IsRestrictedFor(component, empire.Race))
                {
                    empire.AvailableComponents.Add(component);
                    Message newComponentMessage = null;
                    
                    if (component.Properties.ContainsKey("Scanner") && component.Type == ItemType.PlanetaryInstallations)
                    {
                        newComponentMessage = new Message(
                            empire.Id,
                            "All existing planetary scanners has been replaced by " + component.Name + " " + component.Type,
                            "NewComponentMessage",
                            null);
                        
                        Scanner newScanner = component.Properties["Scanner"] as Scanner;

                        foreach (Star star in empire.OwnedStars.Values)
                        {
                            if (star.Owner == empire.Id &&
                                star.ScannerType != string.Empty)
                            {
                                star.ScannerType = component.Name;

                                // Upgrading a planetary scanner's type without also updating its
                                // range left the old (or default) range in effect. See
                                // docs/behavior-specs/fleet-movement-scanning-cargo.md §3.
                                if (newScanner != null)
                                {
                                    star.ScanRange = empire.Race.HasTrait("NAS") ? newScanner.NormalScan * 2 : newScanner.NormalScan;
                                }
                            }
                        }
                    }
                    else
                    {
                        newComponentMessage = new Message(
                           empire.Id,
                           "You now have available the " + component.Name + " " + component.Type + " component",
                           "NewComponentMessage", // TODO (priority 4) - Is this used? Is it documented somewhere? Why a string and not an enum?
                           null);
                    }
                    serverState.AllMessages.Add(newComponentMessage);
                }
            }            
        }

    }
}
