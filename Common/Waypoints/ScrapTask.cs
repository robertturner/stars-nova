using System.Linq;
#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009-2012 The Stars-Nova Project
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

namespace Nova.Common.Waypoints
{
    using System;
    using System.Collections.Generic;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.DataStructures;

    /// <summary>
    /// Performs the Scrap Fleet waypoint task.
    /// </summary>
    public class ScrapTask : IWaypointTask
    {
        /// <summary>
        /// The per-fleet recycled-resources figure is clamped to a 16-bit word before it is added
        /// to the planet's accumulator (behavior-specs-9/production-queue.md 10g, "Write").
        /// </summary>
        public const int MaxRecycledResourcesPerFleet = 65535;

        private List<Message> messages = new List<Message>();

        public List<Message> Messages
        {
            get{ return messages;}
        }

        public string Name
        {
            get{return "Scrap";}
        }

        /// <summary>
        /// Minerals the last <see cref="Perform"/> left as a wreckage object in deep space (null
        /// when the fleet was scrapped at a planet). Common cannot reach ServerData's
        /// AllDeepSpaceMinerals, so the caller (ScrapFleetStep) deposits this at
        /// <see cref="WreckagePosition"/> - the same mechanism the battle pass uses for wreckage
        /// (behavior-specs-9/fleet-movement-scanning-cargo.md §5, Scrap Fleet reading note 3).
        /// </summary>
        public Resources Wreckage { get; private set; }

        /// <summary>Where <see cref="Wreckage"/> is to be left.</summary>
        public NovaPoint WreckagePosition { get; private set; }

        public ScrapTask()
        {

        }

        /// <summary>
        /// Load: Read in a ColoniseTask from and XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a representation of a ProductionUnit</param>
        public ScrapTask(XmlNode node)
        {
            if (node == null)
            {
                return;
            }
        }

        public bool IsValid(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            return true; // fleet.GetTravelStatus() == Fleet.TravelStatus.Arrived;
        }

        /// <summary>
        /// The fleet's total cost, summed per design stack as ship count x the design's stored
        /// cost (behavior-specs-9/fleet-movement-scanning-cargo.md §5, Scrap Fleet decision
        /// table: "S ... for each occupied design stack, ship count x the design's stored cost of
        /// that mineral"; the resource cost the same way). A stack whose design carries the
        /// "failed legality" flag (ShipDesign.FailedLegality, design +0x7c bit 0x80, set by
        /// DesignLegalityStep) counts at ship count x cost / 4, integer, for every mineral and the
        /// resources (behavior-specs-10, same section, "The flag"), whoever scraps it and whether
        /// or not Ultimate Recycling applies. Shared by Colonize (SalvageMinerals) and the
        /// Ultimate Recycling accumulator. The base is the design's stored (miniaturized) cost.
        /// </summary>
        public static Resources FleetCost(Fleet fleet)
        {
            Resources cost = new Resources();
            foreach (ShipToken token in fleet.Composition.Values)
            {
                // Spec-10 ("The base") values scrap/colonize at the miniaturized cost WITHOUT
                // Bleeding Edge Technology's doubling (the original raises a "suppress doubling"
                // flag for this computation) - race-traits.md section 7 step 7.
                Resources designCost = token.Design.CostWithoutBleedingEdgeDoubling;
                long ironium = (long)token.Quantity * designCost.Ironium;
                long boranium = (long)token.Quantity * designCost.Boranium;
                long germanium = (long)token.Quantity * designCost.Germanium;
                long energy = (long)token.Quantity * designCost.Energy;

                if (token.Design.FailedLegality)
                {
                    ironium /= 4;
                    boranium /= 4;
                    germanium /= 4;
                    energy /= 4;
                }

                cost.Ironium += (int)ironium;
                cost.Boranium += (int)boranium;
                cost.Germanium += (int)germanium;
                cost.Energy += (int)energy;
            }

            return cost;
        }

        /// <summary>
        /// The minerals a dismantled fleet yields: floor(numerator x S / denominator) of each
        /// mineral's total cost S (multiplied out first, then truncated), plus the fleet's whole
        /// cargo of that mineral. Energy (resources) and fuel are never part of it. Shared by
        /// Scrap Fleet and Colonize, which use the same code block in the original
        /// (behavior-specs-9/fleet-movement-scanning-cargo.md §5).
        /// </summary>
        public static Resources SalvageMinerals(Fleet fleet, int numerator, int denominator)
        {
            Resources cost = FleetCost(fleet);
            return new Resources(
                (int)((long)cost.Ironium * numerator / denominator) + fleet.Cargo.Ironium,
                (int)((long)cost.Boranium * numerator / denominator) + fleet.Cargo.Boranium,
                (int)((long)cost.Germanium * numerator / denominator) + fleet.Cargo.Germanium,
                0);
        }

        /// <summary>
        /// Ultimate Recycling's same-generation resource blend (behavior-specs-9/
        /// production-queue.md 10g, "Resource funding", "Read"): for a planet's ordinary output
        /// r and recycled accumulator d the planet's total is r when either is zero, otherwise
        /// r + d x r / (d + r) in integer arithmetic. The remainder d^2 / (d + r) is lost.
        /// </summary>
        public static int BlendRecycledResources(int output, int recycled)
        {
            if (output <= 0 || recycled <= 0)
            {
                return output;
            }

            return output + (int)((long)recycled * output / ((long)recycled + output));
        }

        /// <summary>
        /// Scrap a fleet (fleets include starbases).
        /// </summary>
        /// <param name="fleet">The fleet being scrapped.</param>
        /// <param name="target">The planet the fleet orbits, or null in deep space.</param>
        /// <param name="sender">The fleet's owner.</param>
        /// <param name="receiver">The owner of the planet, if any (looked up from the planet's
        /// owner when not supplied).</param>
        /// <remarks>
        /// behavior-specs-9/fleet-movement-scanning-cargo.md §5, "Scrap Fleet recovery, complete
        /// decision table". Per mineral, recovered = floor(f x S) + the fleet's cargo of it:
        ///
        /// Deep space                                   - f = 1/3, left as a wreckage object
        /// Planet without a starbase                    - f = 1/3  (9/20 if the PLANET OWNER has UR)
        /// Planet with a starbase                       - f = 4/5  (9/10 if the PLANET OWNER has UR)
        ///
        /// Both conditions belong to the planet: the starbase is the planet's, and Ultimate
        /// Recycling is tested on the planet owner's race, never the scrapper's (an unowned
        /// planet or deep space never qualifies). With UR, the fleet's whole resource cost (100%)
        /// is also added to the planet's one-generation accumulator
        /// (Star.RecycledScrapResources), blended into this same generation's production
        /// (production-queue.md 10g). When the fleet's owner owns the planet, its colonists join
        /// the planet's population. Fuel and anything else aboard is lost. The fleet is scrapped
        /// whatever happens.
        /// </remarks>
        public bool Perform(Fleet fleet, Mappable target, EmpireData sender, EmpireData receiver = null)
        {
            Wreckage = null;
            WreckagePosition = null;

            Star star = target as Star;

            if (fleet.InOrbit == null || star == null)
            {
                // Deep space: one third of the minerals (plus mineral cargo) are left as wreckage
                // at the fleet's position (reading note 3, message 91).
                Wreckage = SalvageMinerals(fleet, 1, 3);
                WreckagePosition = new NovaPoint(fleet.Position);

                Messages.Add(new Message
                {
                    Audience = fleet.Owner,
                    Text = fleet.Name + " has been scrapped in deep space, leaving "
                        + Wreckage.Mass + "kT of minerals as wreckage."
                });
            }
            else
            {
                ScrapAtPlanet(fleet, star, sender, receiver);
            }

            fleet.Composition.Clear(); // disapear the ships. The (now empty) fleet will be cleaned up latter.
            return true;
        }

        private void ScrapAtPlanet(Fleet fleet, Star star, EmpireData sender, EmpireData receiver)
        {
            // The planet's owner (reading note 2): UR is tested on that race only.
            if (receiver != null && receiver.Id != star.Owner)
            {
                receiver = null;
            }

            Race planetOwnerRace = null;
            if (star.Owner != Global.Nobody)
            {
                planetOwnerRace = receiver != null ? receiver.Race : star.ThisRace;
            }

            bool hasStarbase = star.Starbase != null;
            bool ultimateRecycling = planetOwnerRace != null && planetOwnerRace.HasTrait("UR");

            int numerator;
            int denominator;
            if (hasStarbase)
            {
                numerator = ultimateRecycling ? 9 : 4;
                denominator = ultimateRecycling ? 10 : 5;
            }
            else
            {
                numerator = ultimateRecycling ? 9 : 1;
                denominator = ultimateRecycling ? 20 : 3;
            }

            Resources recovered = SalvageMinerals(fleet, numerator, denominator);
            star.ResourcesOnHand.Ironium += recovered.Ironium;
            star.ResourcesOnHand.Boranium += recovered.Boranium;
            star.ResourcesOnHand.Germanium += recovered.Germanium;

            // Reading note 3: the fleet's colonists join the population only when the scrapper
            // owns the planet; otherwise they are lost with the fleet.
            if (star.Owner != Global.Nobody && star.Owner == fleet.Owner)
            {
                star.Colonists += fleet.Cargo.ColonistNumbers;
            }

            // Reading note 4 / production-queue.md 10g "Write": UR adds the fleet's whole resource
            // cost, clamped to 65,535 per fleet, to the planet's accumulator, which is a 16-bit
            // word that wraps (no saturation across fleets) - replicated as written.
            int recycledResources = 0;
            if (ultimateRecycling)
            {
                recycledResources = Math.Min(FleetCost(fleet).Energy, MaxRecycledResourcesPerFleet);
                star.RecycledScrapResources = (star.RecycledScrapResources + recycledResources) & 0xFFFF;
            }

            string kilotons = recovered.Mass.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string text = fleet.Name + " has been scrapped " + (hasStarbase ? "at the starbase orbiting " : "at ")
                + star.Name + ", recovering " + kilotons + "kT of minerals.";
            if (ultimateRecycling)
            {
                // Messages 92/93: the per-fleet figure is t x r / (t + r) from this fleet's own
                // total and the planet's output now, so it understates several fleets scrapped at
                // one planet in one year (the real credit uses the summed accumulator).
                int output = star.GetResourceRate();
                int immediate = BlendRecycledResources(output, recycledResources) - output;
                text += " " + immediate.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " resources have been made available for immediate use (less if other ships were also scrapped there this year).";
            }

            Messages.Add(new Message { Audience = fleet.Owner, Text = text });

            // The planet owner gets its own notice from the same handler (messages 320-323),
            // even when it is the scrapper.
            if (star.Owner != Global.Nobody)
            {
                Messages.Add(new Message
                {
                    Audience = star.Owner,
                    Text = fleet.Name + " was scrapped at " + star.Name + ", depositing " + kilotons + "kT of minerals there."
                });
            }

            // Reading note 5: the starbase research salvage roll runs only when the planet has a
            // starbase, and for the PLANET OWNER's race (research-tech-tree.md §6). It is a
            // tech-trading opportunity when the fleet was built with higher tech than that race
            // has (e.g. a gifted or foreign ship).
            if (hasStarbase && receiver != null && fleet.Composition.Count > 0)
            {
                // The shared salvage dispatcher (turn-generation-engine.md §5): the scrapped
                // fleet's designs fill the tables, then the one-success roll runs for the planet
                // owner's race.
                SalvageTables tables = new SalvageTables();
                tables.AddFleet(fleet);
                SalvageResult result = SalvageDispatcher.TryGain(receiver, tables, GameRandom.Current);
                if (result != null)
                {
                    Message techMessage = new Message();
                    techMessage.Audience = receiver.Id;
                    techMessage.Text = result.PartName != null
                        ? "Scrapping " + fleet.Name + " at " + star.Name + " has revealed the plans for the " + result.PartName + "."
                        : "Scrapping " + fleet.Name + " has added " + result.BankedResources
                            + " research points to your " + result.Field + " research.";
                    Messages.Add(techMessage);
                }
            }
        }

        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelTask = xmldoc.CreateElement("ScrapTask");

            return xmlelTask;
        }
    }
}
