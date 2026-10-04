#region Copyright Notice
// ============================================================================
// Copyright (C) 2010 stars-nova
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
    using System.Xml;

    /// <summary>
    /// This interface is to be used in ProductionOrder for specifying what is
    /// the result of construction (a ship, a factory and so on).
    /// Generic interface for any single production unit: 1 ship, 1 factory, 
    /// 1 mine, 1% terraform, 1 alchemy and so on.
    /// The implementation should contain all the needed information 
    /// in order to perform actual construction (creating/changing game 
    /// objects).
    /// </summary>
    public interface IProductionUnit
    {
        /// <summary>
        /// The total Cost of this unit.
        /// TODO (priority 6): Maybe this could be removed. It's convenient to have
        /// this data here, but it leads to extra data on the XML files and can lead
        /// to potential exploits. -Aeglos 13 Mar 12
        /// </summary>
        Resources Cost {get;}
        
        /// <summary>
        /// Resources still needed to complete this unit
        /// </summary>
        Resources RemainingCost {get;}
        
        /// <summary>
        /// Returns this unit's name, for display on the GUI.
        /// </summary>
        string Name {get;}
        
        /// <summary>
        /// Method which checks whether another one unit can be constructed.
        /// The unit cannot be constructed either because of lack
        /// of minerals/resources or because of other game restrictions
        /// (for example another factory cannot be constructed if maximum
        /// factory number limit is reached).
        /// </summary>
        /// <returns>Returns true in case unit can be constructed, false otherwise.</returns>
        bool IsSkipped(Star star);

        /// <summary>
        /// How many of this unit the star already has, for the types where that's a stable,
        /// countable planetary stat (Factories/Mines/Defenses) - null for anything else (Ships,
        /// Alchemy, Terraform), which have no such persistent count to check against.
        /// </summary>
        int? CurrentCount(Star star);

        /// <summary>
        /// How many of this unit the star's population (projected to next year, for the auto-build
        /// clamp) can actually operate - null for a unit type with no population-scaled limit.
        /// behavior-specs-8/production-queue.md �10a: an auto-build entry buys at most
        /// min(N, SupportableCount - CurrentCount) units per turn. A manual order is never subject
        /// to this (a colony may own more than it can operate; the surplus simply idles); only its
        /// separate <see cref="BuildCap"/> applies.
        /// </summary>
        int? SupportableCount(Star star);

        /// <summary>
        /// The most of this unit the planet may CONTAIN, set by its maximum population (Factories,
        /// Mines) or habitability (Defenses) rather than its current one - null when unlimited.
        /// A manual order's quantity is cut to BuildCap - CurrentCount (production-queue.md �10a).
        /// </summary>
        int? BuildCap(Star star)
        {
            return null;
        }

        /// <summary>
        /// How many units a MANUAL order for this unit may still buy, or null when unlimited
        /// (behavior-specs-10/production-queue.md 10a and 10i): Factory / Mine / Defenses orders
        /// are cut to the build cap minus what is built (message 298), a Terraform Environment
        /// order to the planet's remaining terraform headroom (message 303); the entry is deleted
        /// when the room is below 1. The default is <see cref="BuildCap"/> minus
        /// <see cref="CurrentCount"/> when both are known.
        /// </summary>
        int? RoomForManualOrder(Star star)
        {
            int? buildCap = BuildCap(star);
            int? built = CurrentCount(star);
            if (buildCap.HasValue && built.HasValue)
            {
                return buildCap.Value - built.Value;
            }

            return null;
        }

        /// <summary>
        /// Whether an auto-build order for this unit is a persistent standing order that is never
        /// edited or removed and re-buys every turn (true, the default), or behaves like a one-off
        /// batch (ships - the original has no auto-build ship item, so the legacy consume-to-zero
        /// behaviour is kept for them).
        /// </summary>
        bool AutoBuildIsStandingOrder
        {
            get { return true; }
        }

        /// <summary>
        /// When non-null, replaces the order's own quantity as the per-turn maximum for an
        /// auto-build order. Auto Mineral Alchemy in LAST position "consumes all remaining
        /// resources" - the original forces its quantity to 1,000 (production-queue.md section
        /// 10a); anywhere else ProductionOrder.Process does not buy it at all (section 7).
        /// </summary>
        int? AutoBuildPerTurnLimit
        {
            get { return null; }
        }

        /// <summary>
        /// Method which performs actual construction.
        /// </summary>
        /// <returns>Returns true if the unit is done constructing, false otherwise</returns>
        bool Construct(Star star);
        
        /// <summary>
        /// Save: Generate an XmlElement representation of the ProductionUnit for saving.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representation of the ProductionQueue.Item.</returns>
        XmlElement ToXml(XmlDocument xmldoc);
    }
}
