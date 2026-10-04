#region Copyright Notice
// ============================================================================
// Copyright (C) 2010, 2011 stars-nova
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
    using System.Diagnostics;
    using System.Globalization;
    using System.Xml;

    using Nova.Common.Components;

    /// <summary>
    /// Details of a design in the queue.
    /// </summary>
    [Serializable]
    public class ProductionOrder 
    {        
        // Number to build
        public int Quantity
        {
            set;
            get;
        }
        
        public bool IsAutoBuild
        {
            private set;
            get;
        }
        
        public IProductionUnit Unit
        {
            private set;
            get;
        }
        
        public string Name
        {
            get {return Unit.Name;}
        }

        
        /// <summary>
        /// initializing constructor.
        /// </summary>
        /// <param name="quantity">The number of items to produce.</param>
        /// <param name="design">The <see cref="Design"/> to build.</param>
        public ProductionOrder(int quantity, IProductionUnit productionUnit, bool isAutoBuild)
        {
            Quantity = quantity;
            Unit = productionUnit;
            IsAutoBuild = isAutoBuild;            
        }

        
        /// <summary>
        /// Return the resources needed to complete this whole order.
        /// </summary>
        /// <returns></returns>
        public Resources NeededResources()
        {
            Resources neededResources = new Resources();
            
            if (Unit.RemainingCost != Unit.Cost)
            {
                neededResources = Unit.RemainingCost + (Unit.Cost * (Quantity - 1));
            }
            else
            {
                neededResources = Unit.Cost * Quantity;
            }
            
            return neededResources;
        }
        
        
        /// <summary>
        /// True when this order stops the queue walk this year: a manual order that cannot be
        /// processed. A manual Factory/Mine/Defenses order with no room left under its build cap
        /// is NOT blocking - behavior-specs-10/production-queue.md section 10a: its quantity is cut
        /// to the room (build cap minus built) and "the entry is deleted if the room is zero or
        /// negative" (message 298 table row: deleted, and the walk goes on), so
        /// <see cref="Process(Star)"/> cuts it to zero and the queue drops it.
        /// </summary>
        public bool IsBlocking(Star star)
        {
            if (IsAutoBuild)
            {
                return false;
            }

            if (HasNoRoomUnderBuildCap(star))
            {
                return false;
            }

            return Unit.IsSkipped(star);
        }

        /// <summary>A manual order for a capped unit (Factories/Mines/Defenses on a planet that
        /// already holds its build cap or more, Terraform Environment with no headroom).</summary>
        private bool HasNoRoomUnderBuildCap(Star star)
        {
            if (IsAutoBuild)
            {
                return false;
            }

            int? room = Unit.RoomForManualOrder(star);
            return room.HasValue && room.Value <= 0;
        }

        /// <summary>
        /// The hub's pre-purchase quantity clamp for a MANUAL order (behavior-specs-10/
        /// production-queue.md 10a and 10i): when the quantity exceeds the room the order may
        /// still use (<see cref="IProductionUnit.RoomForManualOrder"/>: build cap minus built for
        /// Factory / Mine / Defenses, the terraform headroom for Terraform Environment) it is cut
        /// to the room, or to zero - deleted - when the room is below 1. Returns true when the
        /// order was cut, i.e. when the original posts message 298 (installations) or 303
        /// (terraforming); nothing is reported when the order fits. Auto-build entries are
        /// clamped silently at purchase instead (<see cref="PerTurnAllowance"/>).
        /// </summary>
        public bool ApplyRoomClamp(Star star)
        {
            if (IsAutoBuild)
            {
                return false;
            }

            int? room = Unit.RoomForManualOrder(star);
            if (!room.HasValue || Quantity <= room.Value)
            {
                return false;
            }

            Quantity = Math.Max(0, room.Value);
            return true;
        }

        /// <summary>
        /// For a standing auto-build order, the most it may buy this turn: its quantity N (or the
        /// unit's forced per-turn limit, auto Alchemy's 1,000) clamped to the room left -
        /// SupportableCount minus CurrentCount, never below 0 (production-queue.md 10a). Null for
        /// a manual order or an auto order that is not a standing order (ships).
        /// </summary>
        public int? PerTurnAllowance(Star star)
        {
            if (!IsAutoBuild || !Unit.AutoBuildIsStandingOrder)
            {
                return null;
            }

            int allowance = Unit.AutoBuildPerTurnLimit ?? Quantity;

            int? currentCount = Unit.CurrentCount(star);
            int? supportable = Unit.SupportableCount(star);
            if (currentCount.HasValue && supportable.HasValue)
            {
                allowance = Math.Min(allowance, Math.Max(0, supportable.Value - currentCount.Value));
            }

            return allowance;
        }

        /// <summary>An auto-build Mineral Alchemy entry, whose behaviour depends on its position
        /// in the queue (production-queue.md section 7).</summary>
        public bool IsAutoAlchemy
        {
            get { return IsAutoBuild && Unit is AlchemyProductionUnit; }
        }

        /// <summary>
        /// True if this order is in <paramref name="star"/>'s queue with another entry behind it.
        /// An order that isn't queued there at all counts as alone (i.e. last).
        /// </summary>
        public bool HasEntryBehindIt(Star star)
        {
            if (star.ManufacturingQueue == null)
            {
                return false;
            }

            int index = star.ManufacturingQueue.Queue.IndexOf(this);
            return index >= 0 && index < star.ManufacturingQueue.Queue.Count - 1;
        }

        
        /// <summary>
        /// Processes this order.
        /// </summary>
        /// <param name="star">The star where this unit is processed</param>
        /// <returns>The number of units completed</returns>
        /// <remarks>
        /// An AUTO-BUILD order is a persistent standing order and its Quantity is the most it may
        /// buy EACH TURN - not a total the planet is topped up to. behavior-specs-8/production-
        /// queue.md section 10h confirms this by live test: "Factories (Auto Build) Up to 12" on a
        /// planet that already had 10 factories kept buying every year (13, 17, 22, 28...) and
        /// never went idle merely because N or more already existed; the built count is never
        /// subtracted from N. The entry's own text and quantity never change and it never leaves
        /// the queue. The only clamp is the operable room: min(N, what next year's projected
        /// population can operate - what is already built) (section 10a). An auto Mineral Alchemy
        /// entry is bought only in LAST position (or alone), where its per-turn quantity is forced
        /// to 1,000 (IProductionUnit.AutoBuildPerTurnLimit); anywhere else it buys nothing itself
        /// (behavior-specs-10/production-queue.md section 7) - see the overload taking an
        /// <see cref="AlchemyShortfallConversion"/>. Only Ship orders keep the legacy one-off
        /// consume-Quantity-to-zero behaviour (the original has no auto-build ship item).
        /// A MANUAL order is a one-shot batch, but for units with a build cap (Factories, Mines,
        /// Defenses) its quantity is first cut to cap - built, and left at zero (so the queue
        /// drops it) if there is no room (section 10a).
        /// </remarks>
        public int Process(Star star)
        {
            return Process(star, null);
        }

        /// <summary>
        /// Processes this order, optionally with the Mineral Alchemy shortfall conversion armed by
        /// an auto Alchemy entry directly in front of it (behavior-specs-10/production-queue.md
        /// section 7, "How the code does it"): while <paramref name="alchemy"/> is non-null, each
        /// time this order cannot afford its next unit and its scarcest cost component (lowest
        /// affordable percentage, ironium/boranium/germanium/resources in that order, a later one
        /// replacing an earlier only when strictly lower) is a MINERAL, a manual order first makes
        /// its proportional partial payment (an auto order does not), then the remaining resources
        /// are converted - min(units affordable, that mineral's shortfall), 1 kT of each mineral
        /// per unit. A covered shortfall retries the purchase (and may convert again for the next
        /// scarce mineral); otherwise the leftover resources go into the conversion's part-paid
        /// unit and this order stops. Resources being the scarcest converts nothing.
        /// </summary>
        /// <returns>The number of units completed.</returns>
        public int Process(Star star, AlchemyShortfallConversion alchemy)
        {
            if (IsAutoAlchemy && HasEntryBehindIt(star))
            {
                // Not last: never bought as itself, only arms the next entry's conversion.
                return 0;
            }

            int done = 0;
            Resources lastResourcesOnHand = new Resources(star.ResourcesOnHand);
            bool standingOrder = IsAutoBuild && Unit.AutoBuildIsStandingOrder;
            int remaining;

            if (standingOrder)
            {
                remaining = PerTurnAllowance(star).Value;
            }
            else
            {
                // Idempotent when the queue walk already applied it (ProductionQueue.ProcessYear).
                ApplyRoomClamp(star);
                remaining = Quantity;
            }

            while (remaining > 0)
            {
                if (alchemy != null)
                {
                    ShortfallOutcome outcome = ConvertShortfall(star, alchemy);
                    if (outcome == ShortfallOutcome.Retry)
                    {
                        continue;
                    }

                    if (outcome == ShortfallOutcome.Stop)
                    {
                        break;
                    }
                }

                if (Unit.IsSkipped(star))
                {
                    break;
                }

                if (Unit.Construct(star))
                {
                    remaining--;
                    done++;
                    if (!standingOrder)
                    {
                        Quantity--;
                    }
                }
                else if (!MadeProgress(star, ref lastResourcesOnHand))
                {
                    // A real, live-reproduced infinite loop: IsSkipped()==false ("there's
                    // something to spend") and Construct()==false ("not a whole unit yet")
                    // can both hold forever on a genuine rounding edge case in a partial
                    // build (Resources' own int-rounded * double operator) - neither ever
                    // flips, so nothing here ever changes and this would otherwise spin at
                    // 100% CPU permanently (confirmed live via adb: an ANR with the main
                    // thread pegged inside this exact assembly for 12+ seconds straight,
                    // reordering a large queue with a permanently-unaffordable item in it).
                    // A tick that neither completes a unit nor actually spends any resources
                    // is never going to start doing either just by repeating - bail out for
                    // this year instead of hanging.
                    break;
                }
            }

            return done;
        }

        private enum ShortfallOutcome
        {
            /// <summary>Nothing to convert - buy normally.</summary>
            Proceed,

            /// <summary>The conversion covered the shortfall - retry the purchase.</summary>
            Retry,

            /// <summary>The shortfall could not be covered; the leftover resources were banked.</summary>
            Stop
        }

        /// <summary>One step of the armed shortfall conversion - see Process(Star,
        /// AlchemyShortfallConversion). Each Retry converts at least one unit, spending
        /// resources, so the caller's loop always terminates.</summary>
        private ShortfallOutcome ConvertShortfall(Star star, AlchemyShortfallConversion alchemy)
        {
            if (star.ResourcesOnHand >= Unit.RemainingCost)
            {
                return ShortfallOutcome.Proceed;
            }

            int scarcest = ScarcestComponent(Unit.RemainingCost, star.ResourcesOnHand);
            if (scarcest < 0 || scarcest == ResourcesComponent)
            {
                return ShortfallOutcome.Proceed;
            }

            if (!IsAutoBuild && !Unit.IsSkipped(star))
            {
                // A manual entry first spends its proportional partial payment (section 10's
                // percent-complete rule); the unit cannot complete here, it is short.
                Unit.Construct(star);
            }

            int shortfall = ComponentOf(Unit.RemainingCost, scarcest) - ComponentOf(star.ResourcesOnHand, scarcest);
            if (shortfall <= 0)
            {
                return ShortfallOutcome.Proceed;
            }

            int units = alchemy.Convert(star, shortfall);
            if (units >= shortfall)
            {
                return ShortfallOutcome.Retry;
            }

            alchemy.BankLeftover(star);
            return ShortfallOutcome.Stop;
        }

        /// <summary>
        /// True when the unit's next purchase is held back by a MINERAL rather than by resources:
        /// the scarcest cost component (lowest affordable percentage, ironium / boranium /
        /// germanium / resources, a later one replacing an earlier only when strictly lower) is a
        /// mineral. This is the difference between the original's status codes 3/4 (an auto
        /// entry stopped by minerals: the walk continues) and 5-7 (stopped by resources).
        /// </summary>
        public bool IsShortOfAMineral(Star star)
        {
            if (Unit.RemainingCost == null || star.ResourcesOnHand >= Unit.RemainingCost)
            {
                return false;
            }

            int scarcest = ScarcestComponent(Unit.RemainingCost, star.ResourcesOnHand);
            return scarcest >= 0 && scarcest != ResourcesComponent;
        }

        /// <summary>Index of the resources component in the original's cost order (ironium 0,
        /// boranium 1, germanium 2, resources 3).</summary>
        private const int ResourcesComponent = 3;

        private static int ComponentOf(Resources resources, int index)
        {
            switch (index)
            {
                case 0: return resources.Ironium;
                case 1: return resources.Boranium;
                case 2: return resources.Germanium;
                default: return resources.Energy;
            }
        }

        /// <summary>
        /// The cost component with the lowest affordable whole-percent completion (on hand x 100 /
        /// cost, at most 100), over the components the unit still needs, in the order ironium,
        /// boranium, germanium, resources; a later component replaces an earlier one only when
        /// strictly lower. -1 if nothing is needed.
        /// </summary>
        private static int ScarcestComponent(Resources cost, Resources onHand)
        {
            int scarcest = -1;
            long lowest = long.MaxValue;
            for (int index = 0; index <= ResourcesComponent; index++)
            {
                int needed = ComponentOf(cost, index);
                if (needed <= 0)
                {
                    continue;
                }

                long percent = Math.Min(100L, Math.Max(0L, (long)ComponentOf(onHand, index)) * 100 / needed);
                if (percent < lowest)
                {
                    lowest = percent;
                    scarcest = index;
                }
            }

            return scarcest;
        }

        /// <summary>
        /// True if the star's resources actually changed since <paramref name="last"/> was taken
        /// (which is then updated to the current amount) - see Process's own comment for why a
        /// Construct() call that neither completes a unit nor spends anything at all can never
        /// resolve itself by simply being called again.
        /// </summary>
        private static bool MadeProgress(Star star, ref Resources last)
        {
            if (star.ResourcesOnHand == last)
            {
                return false;
            }

            last = new Resources(star.ResourcesOnHand);
            return true;
        }

        /// <summary>
        /// Load: Read in a ProductionQueue.Item from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionQueue.Item.</param>
        public ProductionOrder(XmlNode node)
        {
            XmlNode subnode = node.FirstChild;
            while (subnode != null)
            {
                try
                {
                    switch (subnode.Name.ToLowerInvariant())
                    {
                        case "quantity":
                            Quantity = int.Parse(subnode.FirstChild.Value, CultureInfo.InvariantCulture);
                        break;
                            
                        case "isautobuild":
                            IsAutoBuild = bool.Parse(subnode.FirstChild.Value);
                        break;
                        
                        case "factoryunit":
                            Unit = new FactoryProductionUnit(subnode);
                        break;

                        case "mineunit":
                        Unit = new MineProductionUnit(subnode);
                        break;

                        case "defenseunit":
                        Unit = new DefenseProductionUnit(subnode);
                        break;

                        case "shipunit":
                            Unit = new ShipProductionUnit(subnode);
                        break;

                        case "alchemyunit":
                            Unit = new AlchemyProductionUnit(subnode);
                        break;

                        case "terraformunit":
                            Unit = new TerraformProductionUnit(subnode);
                        break;

                        case "genesisdeviceunit":
                            Unit = new GenesisDeviceProductionUnit(subnode);
                        break;

                        case "packetunit":
                            Unit = new PacketProductionUnit(subnode);
                        break;

                        case "scannerunit":
                            Unit = new ScannerProductionUnit(subnode);
                        break;
                    }
                }
                catch (Exception e)
                {
                    Report.Error(e.Message);
                }
                subnode = subnode.NextSibling;
            }
        }

        
        /// <summary>
        /// Save: Generate an XmlElement representation of the ProductionQueue.Item for saving.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representation of the ProductionQueue.Item.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelProductionOrder = xmldoc.CreateElement("ProductionOrder");
            
            Global.SaveData(xmldoc, xmlelProductionOrder, "Quantity", Quantity.ToString(CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelProductionOrder, "IsAutoBuild", IsAutoBuild.ToString(CultureInfo.InvariantCulture));
            xmlelProductionOrder.AppendChild(Unit.ToXml(xmldoc));

            return xmlelProductionOrder;
        }
    }
}