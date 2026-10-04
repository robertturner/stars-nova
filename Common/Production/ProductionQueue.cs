#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010 stars-nova
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
    using System;
    using System.Collections.Generic;
    using System.Xml;

    /// <summary>
    /// Class for a star's production queue.
    /// </summary>
    [Serializable]
    public class ProductionQueue
    {
        /// <summary>
        /// The production queue itself.
        /// </summary>
        public List<ProductionOrder> Queue = new List<ProductionOrder>();

        /// <summary>
        /// Default constructor.
        /// </summary>
        public ProductionQueue() 
        {  

        }

        /// <summary>
        /// Read in a ProductionQueue from an XmlElement representation.
        /// </summary>
        /// <param name="node">A ProductionQueue XmlNode, normally read from a nova data file.</param>
        public ProductionQueue(XmlNode node)
        {                         
            XmlNode subNode = node.FirstChild;
            while (subNode != null)
            {
                try
                {
                    if (subNode.Name.ToLowerInvariant() == "productionorder")
                    {
                        ProductionOrder order = new ProductionOrder(subNode);
                        if (order != null)
                        {
                            Queue.Add(order); // TODO (priority 6) ensure they load in the correct order.
                        }
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }
                subNode = subNode.NextSibling;
            }
        }
        
        
        /// <summary>
        /// Empties the Production Queue.
        /// </summary>
        public void Clear()
        {
            Queue.Clear();
        }

        /// <summary>
        /// One queue entry's outcome during <see cref="ProcessYear"/>, reported to its caller
        /// before the walk moves on (so a caller's side effects, such as building ships or a
        /// Genesis Device reset, happen at the same point in the walk as in the original).
        /// </summary>
        public sealed class ProcessedEntry
        {
            public ProcessedEntry(ProductionOrder order, int done, int alchemyKilotons, Resources remainingCostBefore)
                : this(order, done, alchemyKilotons, remainingCostBefore, false, null)
            {
            }

            public ProcessedEntry(ProductionOrder order, int done, int alchemyKilotons, Resources remainingCostBefore, bool quantityCut, List<TerraformStep> terraformSteps)
                : this(order, done, alchemyKilotons, remainingCostBefore, quantityCut, order.Quantity, terraformSteps)
            {
            }

            public ProcessedEntry(ProductionOrder order, int done, int alchemyKilotons, Resources remainingCostBefore, bool quantityCut, int quantityAfterCut, List<TerraformStep> terraformSteps)
            {
                QuantityAfterCut = quantityAfterCut;
                Order = order;
                Done = done;
                AlchemyKilotons = alchemyKilotons;
                RemainingCostBefore = remainingCostBefore;
                QuantityCut = quantityCut;
                TerraformSteps = terraformSteps ?? new List<TerraformStep>();
            }

            /// <summary>The hub cut this MANUAL entry's quantity to its room before purchase
            /// (<see cref="ProductionOrder.ApplyRoomClamp"/>): message 298 for an installation
            /// order, 303 for Terraform Environment. When the room was below 1 the entry's
            /// quantity is now 0 and it was deleted without being processed.</summary>
            public bool QuantityCut { get; }

            /// <summary>The quantity the hub cut the entry to (0: deleted), before anything was
            /// bought; only meaningful when <see cref="QuantityCut"/> is set.</summary>
            public int QuantityAfterCut { get; }

            /// <summary>The terraform steps this entry made, one message 123 each.</summary>
            public List<TerraformStep> TerraformSteps { get; }

            /// <summary>The entry just processed.</summary>
            public ProductionOrder Order { get; }

            /// <summary>Units the entry completed.</summary>
            public int Done { get; }

            /// <summary>kT of each mineral made by alchemy for this entry: its own units if it is
            /// an Alchemy entry, plus any shortfall conversion an auto Alchemy entry in front of it
            /// armed (message 140 is posted once per purchase with any).</summary>
            public int AlchemyKilotons { get; }

            /// <summary>The entry's unit RemainingCost just before it was processed.</summary>
            public Resources RemainingCostBefore { get; }
        }

        /// <summary>
        /// Walks the queue once for one year's production, exactly as the turn's Manufacture step
        /// does, so the completion estimator simulates the same rules (behavior-specs-10/
        /// production-queue.md sections 7, 8 and 10a): an AUTO Mineral Alchemy entry that is not
        /// last is skipped and arms the shortfall conversion (<see cref="AlchemyShortfallConversion"/>)
        /// for the next entry only; a manual entry that cannot be processed blocks the rest of the
        /// queue unless it is being served by such a conversion (it gets its chance to convert
        /// first); an entry whose quantity reaches zero (completed, or cut to zero by a build-cap
        /// clamp) leaves the queue and takes the auto Alchemy entry that served it along; and every
        /// part-paid alchemy unit (a last-position auto entry's remainder, or a conversion's
        /// leftover) becomes a one-unit MANUAL Mineral Alchemy entry at the top of the queue once the
        /// walk ends. <paramref name="onProcessed"/> (may be null) is called after each processed
        /// entry, before the walk continues.
        /// </summary>
        public YearOutcome ProcessYear(Star star, Action<ProcessedEntry> onProcessed)
        {
            List<ProductionOrder> completed = new List<ProductionOrder>();
            List<ProductionOrder> insertAtTop = new List<ProductionOrder>();
            ProductionOrder armingAlchemy = null;
            bool hadEntries = Queue.Count > 0;

            // Message 62's second condition: the walk reached the end of the list without an
            // early stop (status 5-7) and without any auto entry held back by minerals (3/4).
            bool stoppedEarly = false;
            bool mineralBlockedAutoEntry = false;

            for (int index = 0; index < Queue.Count; index++)
            {
                ProductionOrder productionOrder = Queue[index];

                // The flag is armed for the next entry only, and cleared once that entry is handled.
                ProductionOrder servingAlchemy = armingAlchemy;
                armingAlchemy = null;

                if (productionOrder.IsAutoAlchemy && productionOrder.HasEntryBehindIt(star))
                {
                    armingAlchemy = productionOrder;
                    continue;
                }

                // The hub's quantity clamp, applied as the walk reaches a MANUAL entry and before
                // anything is bought (section 10a / 10i): cut to the room with message 298 / 303,
                // deleted when the room is below 1 - and the walk goes on.
                bool quantityCut = productionOrder.ApplyRoomClamp(star);
                int quantityAfterCut = productionOrder.Quantity;
                if (quantityCut && productionOrder.Quantity == 0)
                {
                    onProcessed?.Invoke(new ProcessedEntry(productionOrder, 0, 0, productionOrder.Unit.RemainingCost, true, null));
                    completed.Add(productionOrder);
                    if (servingAlchemy != null)
                    {
                        completed.Add(servingAlchemy);
                    }

                    continue;
                }

                AlchemyShortfallConversion conversion = servingAlchemy == null
                    ? null
                    : new AlchemyShortfallConversion((AlchemyProductionUnit)servingAlchemy.Unit);

                if (conversion == null && productionOrder.IsBlocking(star))
                {
                    stoppedEarly = true;
                    if (quantityCut)
                    {
                        onProcessed?.Invoke(new ProcessedEntry(productionOrder, 0, 0, productionOrder.Unit.RemainingCost, true, null));
                    }

                    break;
                }

                Resources remainingBefore = productionOrder.Unit.RemainingCost == null
                    ? null
                    : new Resources(productionOrder.Unit.RemainingCost);

                int? allowance = productionOrder.PerTurnAllowance(star);

                int done = productionOrder.Process(star, conversion);

                int alchemyKilotons = (productionOrder.Unit is AlchemyProductionUnit ? done : 0)
                    + (conversion == null ? 0 : conversion.UnitsConverted);

                if (productionOrder.IsAutoAlchemy)
                {
                    AlchemyProductionUnit partial = ((AlchemyProductionUnit)productionOrder.Unit).TakePartialProgress();
                    if (partial != null)
                    {
                        insertAtTop.Add(new ProductionOrder(1, partial, false));
                    }
                }

                if (conversion != null && conversion.Leftover != null)
                {
                    insertAtTop.Add(new ProductionOrder(1, conversion.Leftover, false));
                }

                List<TerraformStep> steps = productionOrder.Unit is TerraformProductionUnit terraform
                    ? terraform.TakeSteps()
                    : null;

                onProcessed?.Invoke(new ProcessedEntry(productionOrder, done, alchemyKilotons, remainingBefore, quantityCut, quantityAfterCut, steps));

                // A standing auto-build order never reaches zero (ProductionOrder.Process).
                if (productionOrder.Quantity == 0)
                {
                    completed.Add(productionOrder);

                    // The auto Alchemy entry is consumed with the item it served (section 7).
                    if (servingAlchemy != null)
                    {
                        completed.Add(servingAlchemy);
                    }

                    continue;
                }

                // Status codes (section 8): a standing auto entry that bought its whole allowance
                // (1) or had none (2) lets the walk go on; one held back by a MINERAL (3/4) lets it
                // go on too but keeps message 62 quiet; anything else - a manual entry with
                // quantity left, or an auto entry stopped by resources (5-7) - ends the walk.
                if (allowance.HasValue && done >= allowance.Value)
                {
                    continue;
                }

                if (productionOrder.IsAutoBuild && productionOrder.IsShortOfAMineral(star))
                {
                    mineralBlockedAutoEntry = true;
                    continue;
                }

                stoppedEarly = true;
                break;
            }

            foreach (ProductionOrder done in completed)
            {
                Queue.Remove(done);
            }

            Queue.InsertRange(0, insertAtTop);

            bool emptied = hadEntries && Queue.Count == 0;
            return new YearOutcome(hadEntries, emptied || (hadEntries && !stoppedEarly && !mineralBlockedAutoEntry));
        }

        /// <summary>The queue-level result of one <see cref="ProcessYear"/> walk.</summary>
        public sealed class YearOutcome
        {
            public YearOutcome(bool hadEntries, bool finishedOrders)
            {
                HadEntries = hadEntries;
                FinishedOrders = finishedOrders;
            }

            /// <summary>False when the queue was empty before the walk (message 63, "the
            /// planet's production queue is empty").</summary>
            public bool HadEntries { get; }

            /// <summary>Message 62 (the planet has finished its orders): the queue was emptied
            /// during the walk, or the walk reached the end of the list with no early stop and no
            /// auto entry held back by minerals (production-queue.md 10i).</summary>
            public bool FinishedOrders { get; }
        }

        /// <summary>
        /// The disaster queue cleanup (behavior-specs-10/production-queue.md section 8,
        /// FUN_10b8_371a), run after a comet strike or an environment shift (never after a
        /// Genesis Device): every auto-build planetary entry (types 0-6: Mines, Factories,
        /// Defenses, Alchemy, Min/Max Terraform, Mineral Packets) is kept in its original order
        /// and every other entry - manual installations, terraforming, Genesis Devices and ALL
        /// ship and starbase orders, auto-build or not (category 2 has no auto type in the
        /// original) - is deleted with any progress invested in it.
        /// </summary>
        public void CleanupAfterDisaster()
        {
            Queue.RemoveAll(order => !order.IsAutoBuild || order.Unit is ShipProductionUnit);
        }
        
        /// <summary>
        /// Save: Generate an XmlElement representation of the ProductionQueue to save to file.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representing the ProductionQueue.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelProductionQueue = xmldoc.CreateElement("ProductionQueue");
            foreach (ProductionOrder item in Queue)
            {                
                xmlelProductionQueue.AppendChild(item.ToXml(xmldoc));
            }
            return xmlelProductionQueue;
        }
    }
}
