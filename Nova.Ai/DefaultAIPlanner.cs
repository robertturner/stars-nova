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
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

 

    /// <summary>
    /// An AI sub-component to manage planning AI moves. 
    /// </summary>
    /// <remarks>
    /// The default AI is stateless - it does not persist any information between turns other than what is in an ordinary player's state.
    /// </remarks>
    public class DefaultAIPlanner
    {
        public const int EarlyScouts = 5;
        public const int LowProduction = 100;

        public int ScoutCount = 0;
        public int ColonizerCount = 0;
        public int TransportCount = 0;
        public int BomberCount = 0;
        public int WarfleetCount = 0;

        /// <summary>
        /// The colony-ship gate's verdict for this run (behavior-specs-10/ai-opponent-behavior.md
        /// §4, `FUN_1090_2baa`): evaluated once by DefaultAi before the planet pass, and applied
        /// to every planet that turn.
        /// </summary>
        public bool ColonyShipGateAllows = true;

        /// <summary>
        /// Backing store for the TotalTransportKt property.
        /// </summary>
        private int totalTransportKt = 0;

        /// <summary>The base name (before the " T&lt;year&gt;" stamp) of the fallback transport
        /// design.</summary>
        public const string TransportBaseName = "AI Transport";

        private ClientData clientState = null;

        /// <summary>The spec category the AI plays (AiCategory): selects the §17 role table.</summary>
        private readonly int category;

        /// <summary>
        /// The ShipDesign to use for building scouts.
        /// </summary>
        private ShipDesign scoutDesign = null;

        /// <summary>
        /// The ShipDesign to use for building colonizers.
        /// </summary>
        private ShipDesign colonizerDesign = null;

        /// <summary>
        /// The ShipDesign to use for building transports.
        /// </summary>
        private ShipDesign transportDesign = null;

        /// <summary>
        /// The current design to be used for building scouts: the current design of the
        /// personality's explorer role (behavior-specs-10/ai-opponent-behavior.md §17,
        /// personalities 2 and 3, slot 0) when it has one, else the last design named "Scout".
        /// Personalities 0, 1, 4 and 5 put a minelayer or a hunter in slot 0, not a scout, so they
        /// keep the name search.
        /// </summary>
        public ShipDesign ScoutDesign
        {
            get
            {
                if (scoutDesign == null)
                {
                    scoutDesign = RoleDesign(AiDesignRoleKind.Explorer);
                }

                if (scoutDesign == null)
                {
                    foreach (ShipDesign design in clientState.EmpireState.Designs.Values)
                    {
                        if (design.Name.Contains("Scout"))
                        {
                            scoutDesign = design;
                        }
                    }
                }
                return scoutDesign;
            }
        }

        /// <summary>
        /// The current design to be used for building colonizers: the current design of the
        /// personality's slot-1 colonizer role (§17; the starting colonizer until the design
        /// builder replaces it), with personality 5's slot-7 colony phase (§12), else the last
        /// design named "Santa Maria".
        /// </summary>
        public ShipDesign ColonizerDesign
        {
            get
            {
                if (colonizerDesign == null)
                {
                    colonizerDesign = ColonyRoleDesign();
                }

                if (colonizerDesign == null)
                {
                    foreach (ShipDesign design in clientState.EmpireState.Designs.Values)
                    {
                        if (design.Name.Contains("Santa Maria"))
                        {
                            colonizerDesign = design;
                        }
                    }
                }
                return colonizerDesign;
            }
        }

        /// <summary>
        /// Initializing constructor.
        /// </summary>
        public DefaultAIPlanner(ClientData newClientState)
            : this(newClientState, AiCategory.Automitrons)
        {
        }

        /// <summary>
        /// Initializing constructor for a spec category (AiCategory).
        /// </summary>
        public DefaultAIPlanner(ClientData newClientState, int category)
        {
            clientState = newClientState;
            this.category = category;
        }

        /// <summary>
        /// Property to track the total capacity of transport fleets.
        /// </summary>
        public int TotalTransportKt
        {
            get
            {
                return totalTransportKt;
            }
        }


        /// <summary>
        /// The design to use for building transports: the newest current design of the
        /// personality's freighter or hauler roles (§17: personality 0 slots 11-13, 1 slots 8-9,
        /// 2 slots 4-5, 4 slots 2-3 "the newest slot-2/3 design", 5 slots 10-11) when one exists,
        /// else Nova's own fallback transport (<see cref="FallbackTransportDesign"/>).
        /// </summary>
        public ShipDesign TransportDesign
        {
            get
            {
                if (transportDesign == null)
                {
                    transportDesign = NewestRoleDesign(AiDesignRoleKind.Freighter, AiDesignRoleKind.Hauler) ?? FallbackTransportDesign();
                }

                return transportDesign;
            }
        }

        /// <summary>
        /// Nova's own transport (a Large Freighter with two of the best engine), for a
        /// personality whose role designs hold no freighter. The planner is rebuilt every turn,
        /// so the empire's existing "AI Transport" design is looked up first and reused until it
        /// is due for refresh; a refresh that would build the same design again reuses the
        /// existing one, so an identical design is never added twice.
        /// </summary>
        private ShipDesign FallbackTransportDesign()
        {
            // Tech order: Bio, Elec, Energy, Prop, Weap, Cons
            {
                Component freighterHull = null;
                Component engine = null;

                ShipDesign existing = clientState.EmpireState.Designs.Values
                    .Where(design => design.Type == ItemType.Ship && AiDesignRoles.BaseName(design) == TransportBaseName)
                    .OrderBy(design => AiDesignRoles.CreationYear(design))
                    .ThenBy(design => design.Key)
                    .LastOrDefault();
                if (existing != null && !ShipDesignRefresher.IsDueForRefresh(existing, clientState.EmpireState.TurnYear))
                {
                    // already have a design, and it isn't old enough to be worth replacing
                    return existing;
                }
                /* TODO - a better transport?
                else if (clientState.EmpireState.ResearchLevels > new TechLevel(0, 0, 0, 11, 0, 8))
                {
                    // build a really good transport
                    // Super Freighter (Cons 13)? - Not cost effective
                    // Large Freighter (cons 8), Interspace 10 engine (prop 11), 
                }
                 */
                else if (clientState.EmpireState.ResearchLevels > new TechLevel(0, 0, 0, 7, 0, 8))
                {
                    // build a good transport
                    // Large Freighter (cons 8), Alpha Drive 8 (prop 7)

                    if (!clientState.EmpireState.AvailableComponents.TryGetValue("Large Freighter", out freighterHull) || freighterHull == null)
                    {
                        return existing;
                    }

                    // Best-available engine by tech level, rather than a hardcoded "Alpha Drive 8"
                    // lookup, so this design keeps pace as research progresses - part of the
                    // mechanic 7 (ship auto-design refresh) rebuild, see ShipDesignRefresher.
                    engine = ShipDesignRefresher.BestAvailableEngine(clientState);
                    if (engine == null)
                    {
                        return existing;
                    }

                    // The key is drawn only once the design is known to be new. The hull is
                    // copied, so the available component's own modules are never written to.
                    ShipDesign candidate = new ShipDesign(0);
                    candidate.Blueprint = new Component(freighterHull);
                    foreach (HullModule module in candidate.Hull.Modules)
                    {
                        if (module.ComponentType == "Engine")
                        {
                            module.AllocatedComponent = engine;
                            module.ComponentCount = 2;
                        } /* TODO Cargo Pod?
                        else if (module.ComponentType == "Mechanical")
                        {
                            module.AllocatedComponent = cargoPod;
                            module.ComponentCount = 1;
                        }*/
                    }
                    candidate.Icon = new ShipIcon(freighterHull.ImageFile, freighterHull.ComponentImage);

                    candidate.Type = ItemType.Ship;
                    candidate.Name = ShipDesignRefresher.NameWithTurnSuffix(TransportBaseName, clientState.EmpireState.TurnYear);
                    candidate.Update();

                    if (existing != null && DesignBuilder.SameBuild(existing, candidate))
                    {
                        // a refresh would only rebuild the same design
                        return existing;
                    }

                    candidate.Key = clientState.EmpireState.GetNextDesignKey();
                    DesignCommand command = new DesignCommand(CommandMode.Add, candidate);

                    if (command.IsValid(clientState.EmpireState))
                    {
                        clientState.Commands.Push(command);
                        command.ApplyToState(clientState.EmpireState);
                    }

                    return candidate;
                }
                    /* TODO - a medium transport?
                else if (clientState.EmpireState.ResearchLevels > new TechLevel(0, 0, 0, 3, 0, 3))
                {
                    // build a minimal transport
                    // Medium Freighter (cons 3), Long Hump 6 (prop 3)
                }
                     */
                else
                {
                    // do not build transports - tech too low
                    return existing;
                }
            }
        }

        /// <summary>The current design (AiDesignPlanner.CurrentDesigns) of the first of this
        /// personality's roles of the given kind that has one, or null.</summary>
        private ShipDesign RoleDesign(AiDesignRoleKind kind)
        {
            Dictionary<string, ShipDesign> current = AiDesignPlanner.CurrentDesigns(category, clientState.EmpireState.Designs.Values);
            foreach (AiDesignRole role in AiDesignRoleTable.ForCategory(category).Where(role => role.Kind == kind))
            {
                if (current.TryGetValue(role.Tag, out ShipDesign design) && design != null)
                {
                    return design;
                }
            }

            return null;
        }

        /// <summary>The newest (creation year, then key) current design of this personality's
        /// roles of the given kinds, or null.</summary>
        private ShipDesign NewestRoleDesign(params AiDesignRoleKind[] kinds)
        {
            Dictionary<string, ShipDesign> current = AiDesignPlanner.CurrentDesigns(category, clientState.EmpireState.Designs.Values);
            return AiDesignRoleTable.ForCategory(category)
                .Where(role => kinds.Contains(role.Kind) && current.ContainsKey(role.Tag))
                .Select(role => current[role.Tag])
                .Where(design => design != null)
                .OrderBy(design => AiDesignPlanner.CreationYear(design, Global.StartingYear))
                .ThenBy(design => design.Key)
                .LastOrDefault();
        }

        /// <summary>
        /// The slot-1 colonizer role's current design, except for personality 5 during its
        /// colony phase (§12, personality 5 "Colony phase and the colony slot"): while the
        /// slot-7 role ("colonizer-early") holds a colony design, colony ships come from it unless
        /// the slot-1 design's engine is the Galaxy Scoop. The phase ends (the original deletes
        /// the slot-7 design) once the Galaxy Scoop is available and no slot-7 ship exists.
        /// </summary>
        private ShipDesign ColonyRoleDesign()
        {
            Dictionary<string, ShipDesign> current = AiDesignPlanner.CurrentDesigns(category, clientState.EmpireState.Designs.Values);
            current.TryGetValue("colonizer", out ShipDesign slotOne);
            if (category != AiCategory.Macinti
                || !current.TryGetValue("colonizer-early", out ShipDesign slotSeven)
                || slotSeven == null
                || !slotSeven.CanColonize)
            {
                return slotOne;
            }

            if (slotOne != null && DesignPartGroups.NovaNames("Galaxy Scoop").Contains(EngineName(slotOne)))
            {
                return slotOne;
            }

            bool galaxyScoop = DesignPartGroups.NovaNames("Galaxy Scoop").Any(name => clientState.EmpireState.AvailableComponents.ContainsKey(name));
            if (galaxyScoop && ShipsInExistence(slotSeven) == 0)
            {
                return slotOne ?? slotSeven;
            }

            return slotSeven;
        }

        private static string EngineName(ShipDesign design)
        {
            if (design.Blueprint == null || !design.Blueprint.Properties.ContainsKey("Hull"))
            {
                return null;
            }

            HullModule engine = design.Hull.Modules.FirstOrDefault(module => module.ComponentType == "Engine" && module.AllocatedComponent != null);
            return engine?.AllocatedComponent.Name;
        }

        private int ShipsInExistence(ShipDesign design)
        {
            return clientState.EmpireState.OwnedFleets.Values
                .Where(fleet => fleet.Composition != null)
                .SelectMany(fleet => fleet.Composition.Values)
                .Where(token => token.Design != null && token.Design.Key == design.Key)
                .Sum(token => token.Quantity);
        }

        public int TransportKtRequired
        {
            get
            {
                // TODO come up with a better way to determine how much transport to build.
                return 5000;
            }
        }

        /// <summary>
        /// Count and classify owned fleets.
        /// </summary>
        /// <param name="fleet"></param>
        public void CountFleet(Fleet fleet)
        {
            // Work out what we have
            if (fleet.CanColonize)
            {
                this.ColonizerCount++;
            }
            else if (fleet.Name.Contains("Scout"))
            {
                this.ScoutCount++;
            }
            else if (fleet.HasBombers)
            {
                this.BomberCount++;
            }
            else if (fleet.TotalCargoCapacity > 0)
            {
                this.TransportCount++;
                this.totalTransportKt += fleet.TotalCargoCapacity;
            }
            else if (fleet.IsArmed)
            {
                this.WarfleetCount++;
            }
        }
    }
}
