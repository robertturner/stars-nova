 #region Copyright Notice
 // ============================================================================
 // Copyright (C) 2012 The Stars-Nova Project
 //
 // This file is part of Stars-Nova.
 // See <http://sourceforge.net/projects/stars-nova/>;.
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
 // along with this program. If not, see <http://www.gnu.org/licenses/>;
 // ===========================================================================
 #endregion
 
namespace Nova.Common.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Xml;
    
    using Nova.Common.Components;
    
    /// <summary>
    /// Description of WaypointCommand.
    /// </summary>
    public class DesignCommand : ICommand
    {        
        public ShipDesign Design
        {
            private set;
            get;
        }
        
        public CommandMode Mode
        {
            private set;
            get;
        }
        
        
        // Create a blank design command.
        public DesignCommand()
        {
            Design = new ShipDesign(Global.None);
            Mode = CommandMode.Add;
        }
        
        /// <summary>
        /// Creates a design command with a design key. Useful to delete designs without
        /// bloating the orders file when all that is needed is the numeric Key instead of
        /// the full design.
        /// </summary>
        public DesignCommand(CommandMode mode, long designKey)
        {
            Design = new ShipDesign(designKey);
            Mode = mode;
        }
        

        /// <summary>
        /// Creates a design command by providing a full design object. Use when adding or
        /// modifying designs.
        /// </summary>
        public DesignCommand(CommandMode mode, ShipDesign design)
        {
            Design = design;
            Mode = mode;
        }
                
        
        /// <summary>
        /// Load from XML: Initializing constructor from an XML node.
        /// </summary>
        /// <param name="node">An <see cref="XmlNode"/> within
        /// a Nova component definition file (xml document).
        /// </param>
        public DesignCommand(XmlNode node)
        {
            XmlNode mainNode = node.FirstChild;
            
            while (mainNode != null)
            {
                switch (mainNode.Name.ToLowerInvariant())
                {
                    case "mode":
                        Mode = (CommandMode)Enum.Parse(typeof(CommandMode), mainNode.FirstChild.Value);
                        break;                   
                    
                    case "design":
                        Design = new ShipDesign(mainNode);
                        break;
                    
                    case "shipdesign":
                        Design = new ShipDesign(mainNode);
                        break;

                    case "key": // occurs if CommandMode is Delete
                        Design = new ShipDesign(long.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture));
                        break;
                }
            
                mainNode = mainNode.NextSibling;
            }   
        }
        
        
        
        /// <summary>The design-name limit: the record's name field is 32 bytes, 31 characters and
        /// a terminator (save-turn-file-format.md section 3; the designer's own edit box is limited
        /// to 31, and a host record whose stored length exceeds 32 fails). NOT enforced in
        /// <see cref="IsValid"/> yet: the AI design builder names designs longer than this (e.g.
        /// "Medium Freighter [colonizer] T2105"), so rejecting them would break the AI. Once the
        /// builder truncates its names, add the length test to the Add branch.</summary>
        public const int MaxDesignNameLength = 31;

        public bool IsValid(EmpireData empire)
        {
            if (empire == null)
            {
                return false;
            }

            switch (Mode)
            {
                case CommandMode.Add:
                    if (empire.Designs.ContainsKey(Design.Key))
                    {
                        // Cant re-add same design.
                        return false;
                    }
                break;
                case CommandMode.Delete: // Botch cases check for existing design before editing/deleting.
                case CommandMode.Edit:
                    if (!empire.Designs.ContainsKey(Design.Key))
                    {
                        return false;
                    }

                    if (IsProtectedAlternateRealityDesign(empire, empire.Designs[Design.Key], Mode))
                    {
                        return false;
                    }

                    // Host step 6 (save-turn-file-format.md section 3): replacing a design that
                    // is in use and has ships in existence fails. Deleting it is still allowed
                    // (the delete path scraps the ships).
                    if (Mode == CommandMode.Edit
                        && empire.Designs[Design.Key].Type != ItemType.Starbase
                        && IsShipDesignInUse(empire, Design.Key))
                    {
                        return false;
                    }
                break;
            }

            return true;
        }

        /// <summary>
        /// True when the empire already holds the cap of designs of the incoming design's kind
        /// (16 hull designs or 10 starbase designs, save-turn-file-format.md section 3). NOT
        /// enforced in <see cref="IsValid"/> yet: the AI design planner still adds a new design
        /// each turn instead of reusing a slot (reported bug SIM-2), so enforcing the cap here
        /// would reject legitimate AI role designs. Once the planner manages slots, call this from
        /// the Add branch.
        /// </summary>
        public static bool IsAtDesignCap(EmpireData empire, ShipDesign design)
        {
            if (empire == null || design == null)
            {
                return false;
            }

            bool starbase = design.Type == ItemType.Starbase;
            int cap = starbase ? Global.MaxStarbaseDesignsAmount : Global.MaxDesignsAmount;

            int count = 0;
            foreach (ShipDesign existing in empire.Designs.Values)
            {
                if ((existing.Type == ItemType.Starbase) == starbase)
                {
                    count++;
                    if (count >= cap)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The name of Alternate Reality's design slot 0 (ServerState StarterColony).</summary>
        public const string StarterColonyDesignName = "Starter Colony";

        /// <summary>
        /// Alternate Reality's starbase designs are protected (behavior-specs-10/population-
        /// growth.md section 3, the designer's starbase view for PRT 8): slot 0, the "Starter
        /// Colony" every new AR colony is given, can be neither deleted nor edited, and no
        /// starbase design with existing starbases can be deleted - an owned AR planet must always
        /// keep its starbase, its only source of population capacity. Other races are unaffected
        /// (deleting a design still scraps the ships built to it).
        /// </summary>
        public static bool IsProtectedAlternateRealityDesign(EmpireData empire, ShipDesign design, CommandMode mode)
        {
            if (empire == null || design == null || empire.Race == null || !empire.Race.HasTrait("AR"))
            {
                return false;
            }

            if (design.Type != ItemType.Starbase)
            {
                return false;
            }

            if (design.Name == StarterColonyDesignName)
            {
                return mode == CommandMode.Delete || mode == CommandMode.Edit;
            }

            if (mode != CommandMode.Delete)
            {
                return false;
            }

            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (fleet.Composition.ContainsKey(design.Key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Host step 6 (save-turn-file-format.md section 3): a design record that replaces an
        /// existing design which is in use and has ships in existence fails. The port rejects the
        /// order rather than aborting the whole turn generation. "In use with ships in existence"
        /// is read from the empire's own fleet compositions: any owned fleet carrying a token of
        /// the design's key. A star's starbase is an owned fleet too, but only ever carries a
        /// starbase-design token, so this test is scoped to hull (non-starbase) designs;
        /// save-turn-file-format.md section 3 uses "ships" for the 0-15 hull-design slots in the
        /// same sentence that uses "starbases" for the 16-25 starbase slots. An in-use starbase
        /// design is instead governed by <see cref="IsProtectedAlternateRealityDesign"/>.
        /// </summary>
        public static bool IsShipDesignInUse(EmpireData empire, long designKey)
        {
            if (empire == null)
            {
                return false;
            }

            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                if (fleet.Composition != null && fleet.Composition.ContainsKey(designKey))
                {
                    return true;
                }
            }

            return false;
        }
        
        
        
        public void ApplyToState(EmpireData empire)
        {
            switch (Mode)
            {
                case CommandMode.Add:
                    LinkComponents(empire);
                    empire.Designs.Add(Design.Key, Design);
                    empire.TrackDesignKey(Design.Key);
                break;
                case CommandMode.Delete:
                    empire.Designs.Remove(Design.Key);                
                    UpdateFleetCompositions(empire);
                break;
                case CommandMode.Edit:
                    empire.Designs.Remove(Design.Key);
                    UpdateFleetCompositions(empire);
                    LinkComponents(empire);
                    empire.Designs.Add(Design.Key, Design);
                break;
            }
        }


        /// <summary>
        /// A design read from an orders file holds its parts by name only (placeholder
        /// components with no properties); EmpireData.LinkReferences swaps in the real
        /// components and recomputes the design for the race and tech on every load. Do the same
        /// when the design arrives, so a newly ordered design has its real fuel, cargo, engine and
        /// weapon figures from its first turn rather than only after the next reload (found by
        /// Nova.Sim: the in-memory design re-saved with FuelCapacity 650, the reloaded one 900,
        /// and fleets of it burned fuel differently until a reload).
        /// </summary>
        private void LinkComponents(EmpireData empire)
        {
            if (Design == null || Design.Blueprint == null || !(Design.Blueprint.Properties.ContainsKey("Hull")))
            {
                return;
            }

            AllComponents allComponents = new AllComponents(false);
            Hull hull = Design.Hull;
            if (hull != null && hull.Modules != null)
            {
                foreach (HullModule module in hull.Modules)
                {
                    if (module.AllocatedComponent != null && module.AllocatedComponent.Name != null && allComponents.Contains(module.AllocatedComponent.Name))
                    {
                        module.AllocatedComponent = allComponents.Fetch(module.AllocatedComponent.Name);
                    }
                }
            }

            if (empire.Race != null)
            {
                Design.Update(empire.Race, empire.ResearchLevels);
            }
        }
        
        
        /// <summary>
        /// Handle destroying ships of the deleted/edited design.
        /// </summary>
        private void UpdateFleetCompositions(EmpireData empire)
        {
            // Note that we are not allowed to delete the ships or fleets on the
            // iteration as that is not allowed (it
            // destroys the validity of the iterator). Consequently we identify
            // anything that needs deleting and remove them separately from their
            // identification.
            List<Fleet> fleetsToRemove = new List<Fleet>();
            
            foreach (Fleet fleet in empire.OwnedFleets.Values)
            {
                List<ShipToken> tokensToRemove = new List<ShipToken>();
    
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    if (token.Design.Key == Design.Key)
                    {
                        tokensToRemove.Add(token);
                    }
                }
    
                foreach (ShipToken token in tokensToRemove)
                {
                    fleet.Composition.Remove(token.Design.Key);
                }
    
                if (fleet.Composition.Count == 0)
                {
                    fleetsToRemove.Add(fleet);
                }
            }
    
            foreach (Fleet fleet in fleetsToRemove)
            {
                empire.OwnedFleets.Remove(fleet.Key);
                empire.FleetReports.Remove(fleet.Key);
            }
        }
        
        
        /// <summary>
        /// Save: Serialize this property to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Property.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelCom = xmldoc.CreateElement("Command");
            xmlelCom.SetAttribute("Type", "Design");
            Global.SaveData(xmldoc, xmlelCom, "Mode", Mode.ToString());
            if (Mode != CommandMode.Delete)
            {
                // serialise a normal design
                xmlelCom.AppendChild(Design.ToXml(xmldoc));
            }
            else
            {
                // For CommandMode.Delete the design only contains a valid Tag
                Global.SaveData(xmldoc, xmlelCom, "Key", Design.Key);
            }
            
            return xmlelCom;    
        }
    }
}
