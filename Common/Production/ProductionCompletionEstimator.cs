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

namespace Nova.Common
{
    using System.Collections.Generic;
    using System.Xml;

    /// <summary>
    /// The color a queue line should be shown in - docs/behavior-specs-5/production-queue.md §8's
    /// "Full queue-item text-color scheme" (identified by inspection of the exported client):
    /// reading the completion estimator's (start, finish) pair (the simulated year the item first
    /// receives resources, and the year it completes - both forced to 100 if never reached within
    /// a 100-year simulated window), a fixed set of rules picks one of these five buckets.
    /// </summary>
    public enum ProductionQueueColor
    {
        /// <summary>2-99: will start within the simulated window, but not right away. No special
        /// marker in the original - the listbox's own ordinary text color.</summary>
        Default,

        /// <summary>(start, finish) = (1, 1): will both begin and fully complete next turn.</summary>
        Green,

        /// <summary>start = 0 (already receiving resources, before this turn even ends), or
        /// start = 1 with finish > 1 (starts next turn but needs further turns after that).</summary>
        Blue,

        /// <summary>start >= 100: never begins within the simulated window - the "practically
        /// never" warning. Also the fallback for any (start, finish) this scheme doesn't
        /// otherwise recognize.</summary>
        Red,

        /// <summary>start = finish = 0, or start = finish = -1: a zero-remaining-cost or
        /// not-applicable item - e.g. an auto-build order already at its "up to N" target, with
        /// nothing left to build at all right now.</summary>
        Gray,
    }

    /// <summary>One queue line's estimated progress, in a form the UI can show directly.</summary>
    public class ProductionCompletionEstimate
    {
        /// <summary>0-100: how much of the NEXT single unit's cost has already been paid (an
        /// order's Quantity/RemainingCost distinction already means a multi-unit batch's earlier
        /// units are fully paid for - this is specifically progress toward the one still in
        /// progress). Matches the original's own "9% Done" wording (docs/behavior-specs-5/
        /// production-queue.md's Open Questions section) - a plain resources-spent fraction.</summary>
        public double PercentComplete { get; }

        /// <summary>Estimated years until this line next completes a unit - 100 means "at or
        /// beyond the simulated window" (docs/behavior-specs-5/production-queue.md §8's
        /// "practically never" case), not literally exactly 100 years.</summary>
        public int YearsToFinish { get; }

        public ProductionQueueColor Color { get; }

        public ProductionCompletionEstimate(double percentComplete, int yearsToFinish, ProductionQueueColor color)
        {
            PercentComplete = percentComplete;
            YearsToFinish = yearsToFinish;
            Color = color;
        }
    }

    /// <summary>
    /// Estimates a production-queue line's progress and years-to-completion by literally
    /// simulating the star's own future turns - the same approach docs/behavior-specs-5/
    /// production-queue.md §8 confirms the original client itself uses ("the completion-time
    /// estimator itself literally simulates up to 100 yearly iterations"), rather than a
    /// closed-form formula (blocking, auto-build-never-blocks, and partial multi-resource
    /// shortfalls don't have a simple one).
    /// </summary>
    public static class ProductionCompletionEstimator
    {
        private const int MaxSimulatedYears = 100;

        public static ProductionCompletionEstimate Estimate(Star star, int queueIndex, Race race, int researchBudget)
        {
            return EstimateAll(star, race, researchBudget)[queueIndex];
        }

        /// <summary>
        /// Same estimate as <see cref="Estimate"/>, for every line in the queue at once - a real,
        /// live-reproduced ANR: <see cref="Estimate"/> used to be called once per row from
        /// ProductionViewModel.RebuildQueueRows (every row's own estimate depends on everything
        /// ahead of it, so every rebuild recomputed every row), and each call ran its own
        /// independent <see cref="Simulate"/> - a full XML clone of the star plus up to 100
        /// simulated years, redundantly re-simulating the exact same queue from year 1 for every
        /// single row, differing only in which line's (start, finish) got kept. On a real device,
        /// reordering one item in a several-dozen-line queue meant several dozen full 100-year
        /// resimulations synchronously on the UI thread - long enough to trip Android's 10s ANR
        /// watchdog (confirmed live: "Input dispatching timed out" while the main thread sat at a
        /// steady ~100% CPU). One shared simulation pass computes every line's (start, finish) in
        /// a single 100-year run instead - the redundant N-way reclone/resimulate is what made
        /// this expensive, not the simulation itself.
        /// </summary>
        public static IReadOnlyList<ProductionCompletionEstimate> EstimateAll(Star star, Race race, int researchBudget)
        {
            int count = star.ManufacturingQueue.Queue.Count;
            var estimates = new ProductionCompletionEstimate[count];

            var toSimulate = new List<int>(count);
            for (int i = 0; i < count; i++)
            {
                ProductionOrder order = star.ManufacturingQueue.Queue[i];
                IProductionUnit unit = order.Unit;

                // An auto-build order (Factories/Mines/Defenses) already at or above its own "up
                // to N" target has nothing left to build right now at all - distinct from an
                // ordinary item that simply hasn't started yet.
                int? currentCount = order.IsAutoBuild ? unit.CurrentCount(star) : null;
                bool autoBuildAlreadySatisfied = currentCount.HasValue && currentCount.Value >= order.Quantity;

                if (autoBuildAlreadySatisfied)
                {
                    estimates[i] = new ProductionCompletionEstimate(100.0, 0, ProductionQueueColor.Gray);
                }
                else
                {
                    toSimulate.Add(i);
                }
            }

            if (toSimulate.Count > 0)
            {
                IReadOnlyDictionary<int, (int start, int finish)> simulated = SimulateAll(star, toSimulate, race, researchBudget);
                foreach (int i in toSimulate)
                {
                    ProductionOrder order = star.ManufacturingQueue.Queue[i];
                    IProductionUnit unit = order.Unit;
                    double percentComplete = PercentComplete(unit);

                    // Already partially paid for from a previous turn, before this preview even
                    // runs - this is the doc's "start = 0" case, a state fact rather than
                    // something the simulation needs to discover.
                    bool alreadyInProgress = unit.RemainingCost != unit.Cost;

                    (int simulatedStart, int finish) = simulated[i];
                    int start = alreadyInProgress ? 0 : simulatedStart;

                    ProductionQueueColor color;
                    if (start == 1 && finish == 1)
                    {
                        color = ProductionQueueColor.Green;
                    }
                    else if (start == 0 || (start == 1 && finish > 1))
                    {
                        color = ProductionQueueColor.Blue;
                    }
                    else if (start >= MaxSimulatedYears)
                    {
                        color = ProductionQueueColor.Red;
                    }
                    else
                    {
                        color = ProductionQueueColor.Default;
                    }

                    estimates[i] = new ProductionCompletionEstimate(percentComplete, finish, color);
                }
            }

            return estimates;
        }

        /// <summary>Fraction of the next single unit's Energy (resource) cost already paid -
        /// Energy is the one cost component every unit type always has a nonzero amount of
        /// (resources "pay for everything" - docs/behavior-specs-5/production-queue.md §Overview),
        /// unlike Ironium/Boranium/Germanium, which several unit types need none of.</summary>
        private static double PercentComplete(IProductionUnit unit)
        {
            if (unit.Cost.Energy <= 0)
            {
                return 0.0;
            }

            double remaining = unit.RemainingCost.Energy;
            return 100.0 * (1.0 - (remaining / unit.Cost.Energy));
        }

        /// <summary>
        /// Runs the star's own real per-turn update methods (UpdateMinerals/UpdateResearch/
        /// UpdateResources/UpdatePopulation - see Star.cs) against a single independent clone,
        /// year by year, applying each queue item's own Process(star) exactly as Manufacture.
        /// Items does - except never converting a completed ShipProductionUnit into a real Fleet
        /// (Manufacture.CreateShips' job), since this simulation only cares whether/when a unit
        /// completes, not what results from it, and that step would need a full ServerData/
        /// EmpireData for no benefit here. Tracks (start, finish) for every index in
        /// <paramref name="queueIndexes"/> simultaneously in this one pass, rather than one
        /// independent 100-year simulation per index (see EstimateAll's own comment for why that
        /// used to be expensive enough to freeze the whole app). Both values are forced to
        /// MaxSimulatedYears if never reached within the window, matching the original client's
        /// own confirmed 100-year simulation cap.
        /// </summary>
        private static IReadOnlyDictionary<int, (int start, int finish)> SimulateAll(Star star, IReadOnlyList<int> queueIndexes, Race race, int researchBudget)
        {
            // Star's own XML round-trip already deep-clones everything needed, including a fresh
            // ManufacturingQueue of independent ProductionOrder/IProductionUnit objects (both
            // carry mutable Quantity/RemainingCost state that must not alias the real queue) -
            // reusing it here avoids writing new clone constructors for every unit type. Just one
            // clone total for every index being estimated, not one per index.
            Star simulated = new Star(star.ToXml(new XmlDocument()));

            // ThisRace/EnergyTechLevel/Starbase are "stored as references only" across a real
            // save/load round-trip (see Star.cs's own comment on ToXml) - normally re-linked by
            // EmpireData.LinkReferences after a full game load, which this standalone clone never
            // goes through. Restored directly from the real, already-linked objects instead.
            simulated.ThisRace = race;
            simulated.EnergyTechLevel = star.EnergyTechLevel;
            simulated.Starbase = star.Starbase;

            var targets = new Dictionary<ProductionOrder, int>();
            var starts = new Dictionary<int, int>();
            var finishes = new Dictionary<int, int>();
            foreach (int index in queueIndexes)
            {
                targets[simulated.ManufacturingQueue.Queue[index]] = index;
                starts[index] = -1;
                finishes[index] = -1;
            }

            for (int year = 1; year <= MaxSimulatedYears; year++)
            {
                simulated.UpdateMinerals();
                simulated.UpdateResearch(researchBudget);
                simulated.UpdateResources();
                simulated.UpdatePopulation(race);

                var completed = new List<ProductionOrder>();
                foreach (ProductionOrder queued in simulated.ManufacturingQueue.Queue)
                {
                    if (queued.IsBlocking(simulated))
                    {
                        break;
                    }

                    Resources remainingBefore = new Resources(queued.Unit.RemainingCost);
                    int done = queued.Process(simulated);

                    if (targets.TryGetValue(queued, out int index))
                    {
                        if (starts[index] < 0 && (done > 0 || queued.Unit.RemainingCost != remainingBefore))
                        {
                            starts[index] = year;
                        }

                        // "Finish" means the whole line is done, not just its next unit - a
                        // multi-unit manual batch (e.g. "Factory x100") only truly finishes once
                        // its full Quantity is consumed, matching the doc's own "fully complete
                        // production" wording (not e.g. year 1 for a 500-unit order, just because
                        // year 1 happened to complete a couple of them). A persistent auto-build
                        // order (Factories/Mines/Defenses "up to N") never reaches Quantity 0 at
                        // all (see ProductionOrder.Process's own comment) - for those, completing
                        // even one unit IS the meaningful "finish" event, since there's no whole
                        // batch to wait for.
                        bool isPersistentCount = queued.IsAutoBuild && queued.Unit.CurrentCount(simulated).HasValue;
                        bool lineFinished = isPersistentCount ? done > 0 : queued.Quantity == 0;
                        if (lineFinished)
                        {
                            finishes[index] = year;
                        }
                    }

                    // Matches Manufacture.Items' own cleanup exactly - an auto-build order for a
                    // persistent-count unit (Factories/Mines/Defenses) never reaches Quantity 0
                    // (see ProductionOrder.Process's own comment), so it's never removed here
                    // either, same as the real engine.
                    if (queued.Quantity == 0)
                    {
                        completed.Add(queued);
                    }
                }

                foreach (ProductionOrder done in completed)
                {
                    simulated.ManufacturingQueue.Queue.Remove(done);
                }

                bool allFinished = true;
                foreach (int index in queueIndexes)
                {
                    if (finishes[index] <= 0)
                    {
                        allFinished = false;
                        break;
                    }
                }

                if (allFinished)
                {
                    break;
                }
            }

            var result = new Dictionary<int, (int start, int finish)>();
            foreach (int index in queueIndexes)
            {
                int start = starts[index] < 0 ? MaxSimulatedYears : starts[index];
                int finish = finishes[index] < 0 ? MaxSimulatedYears : finishes[index];
                result[index] = (start, finish);
            }

            return result;
        }
    }
}
