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
// ============================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Xml;
    
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    using Nova.Common.Waypoints;

    public enum PlayerRelation
    {
        Enemy,
        Neutral,
        Friend
    }
 
    /// <summary>
    /// Race specific data that may change from year-to-year that must be passed to
    /// the Nova console/server. 
    /// </summary>
    [Serializable]
    public class EmpireData
    {
        private ushort      empireId;
        
        /// <summary>
        /// The year that corresponds to this data. Normally the current game year.
        /// </summary>
        public int          TurnYear                = Global.StartingYear;  

        /// <summary>
        /// Set to true when submit turn is selected in the client. Indicates when orders are ready for processing by the server.
        /// </summary>
        public bool         TurnSubmitted           = false;

        /// <summary>
        /// The last game year for which a turn was submitted. Should be the previous game year until the current year is submitted. May be several years previous if turns were skipped.
        /// </summary>
        public int          LastTurnSubmitted       = 0;

        /// <summary>
        /// The race's "eliminated" status bit (bit 0 of its score-record header), raised once by
        /// VictoryCheck when the race has no planets and no ships of any class, and persisted.
        /// behavior-specs-9/victory-conditions.md section 2, "Elimination flag".
        /// </summary>
        public bool         Eliminated              = false;

        /// <summary>
        /// Whether this empire has already picked up a traded tech level this turn (from
        /// scrapping, battle, or invasion — only one such gain is allowed per turn regardless of
        /// how many qualifying events occur). Reset at the start of each turn. See
        /// docs/behavior-specs/research-tech-tree.md §6.
        /// </summary>
        public bool         TechGainedThisTurn      = false;

        /// <summary>
        /// This empire's own random seed, for the decisions made on its behalf outside the
        /// server: the in-process AI derives each turn's Random from it and the turn year
        /// (DefaultAi), so an AI game is repeatable. Set by Gameinitializer from the game seed and
        /// the empire id (GameRandom.DeriveSeed - a one-way hash, so the turn file reveals nothing
        /// of the server's own streams). 0 (hand-built data, older saves) means none: the AI then
        /// uses an unseeded Random as before. Persisted (and written to the empire's turn file).
        /// </summary>
        public int          RandomSeed              = 0;

        private Race        race                    = new Race(); // This empire's race.
        
        public int          ResearchBudget          = 10; // % of resources allocated to research

        /// <summary>
        /// Current levels of technology.
        /// </summary>
        public TechLevel    ResearchLevels          = new TechLevel(); 
        public TechLevel    ResearchResources       = new TechLevel(); // current cumulative resources on technologies
        public TechLevel    ResearchTopics          = new TechLevel(); // order of researching

        /// <summary>The Research dialog's "next field to research" setting
        /// (research-tech-tree.md section 4): Research.NextFieldSame (stay, the default), a
        /// TechLevel.ResearchField index, or Research.NextFieldLowest.</summary>
        public int          ResearchNextField       = Research.NextFieldSame;
        
        public RaceComponents   AvailableComponents;
        public Dictionary<long, ShipDesign> Designs     = new Dictionary<long, ShipDesign>(); 
        
        public StarList OwnedStars = new StarList();
        public Dictionary<string, StarIntel> StarReports  = new Dictionary<string, StarIntel>();
        
        public FleetList OwnedFleets = new FleetList();
        public Dictionary<long, FleetIntel> FleetReports  = new Dictionary<long, FleetIntel>();

        /// <summary>
        /// Wormhole ends this empire has detected (last seen position and year). A wormhole with a
        /// record here has been discovered once and is no longer cloaked to this empire.
        /// Written by the server's ScanStep (behavior-specs-10/fleet-movement-scanning-cargo.md §3).
        /// </summary>
        public Dictionary<long, WormholeIntel> WormholeReports = new Dictionary<long, WormholeIntel>();

        /// <summary>
        /// Keys of the minefields this empire can see this year, recomputed by the server's
        /// ScanStep: its own fields, fields that have shown themselves by striking its fleets
        /// (Minefield.VisibleTo), and fields detected by its scanners. The turn file carries only
        /// these fields (IntelWriter). See <see cref="CanSeeMinefield"/>.
        /// </summary>
        public HashSet<long> VisibleMinefields = new HashSet<long>();

        /// <summary>
        /// The mineral packets this empire sees this year (copies, keyed by packet key),
        /// recomputed by the server's ScanStep: its own packets, every packet in flight for a
        /// Packet Physics race (race-traits.md §2), and packets inside a scanner's normal range.
        /// </summary>
        public Dictionary<long, MineralPacket> MineralPacketReports = new Dictionary<long, MineralPacket>();

        // This is Fleet Limbo~
        // ??? What is this for?
        public List<Fleet> TemporaryFleets = new List<Fleet>();

        /// <summary>
        /// Names of the 12 special components (see SpecialComponentGrants) this empire has
        /// already been randomly awarded via a won battle - behavior-specs-7/
        /// ship-design-and-components.md §14a's "one-time, per-race, per-component random grant"
        /// bitmask, tracked here as a set of names instead of raw bits so nothing outside
        /// BattleEngine/RaceComponents needs to know the original's specific bit-to-component
        /// mapping. A name in this set means the component is available to build (tech level
        /// permitting) even though it isn't tied to any PRT/LRT; permanent once granted, so this
        /// persists across turns/saves like OwnedStars/StarReports do.
        /// </summary>
        public HashSet<string> GrantedSpecialComponents = new HashSet<string>();
        
        public Dictionary<ushort, EmpireIntel>  EmpireReports   = new Dictionary<ushort, EmpireIntel>();
        
        public Dictionary<string, BattlePlan>   BattlePlans     = new Dictionary<string, BattlePlan>();

        /// <summary>The four saved production templates and the default one copied into new or
        /// captured colonies (production-queue.md sections 9 and 10f; ProductionTemplateCommand).</summary>
        public ProductionTemplateSet ProductionTemplates = new ProductionTemplateSet();
        
        public List<BattleReport> BattleReports = new List<BattleReport>();
        
        // See associated properties.
        private long        fleetCounter             = 0;
        private long        designCounter            = 0;
        private long        minefieldCounter         = 0;
        
        public Race Race
        {
            get
            {
                return this.race;
            }
            
            set
            {
                if (value != null)
                {
                    race = value;
                }
            }
        }
        
        /// <summary>
        /// Sets or gets this empires unique integer Id.
        /// </summary>
        public ushort Id
        {
            get
            {
                return empireId;
            }
            
            set
            {
                // Empire Id should only be set on game creation, from a simple 0-127 int.
                if (value > 127)    
                { 
                    throw new ArgumentException("EmpireId out of range"); 
                }  
                empireId = value;
            }
        }
        
        /// <summary>
        /// Fleet ids handed out by <see cref="GetNextFleetKey"/> that are not yet in use by an
        /// owned (or pending) fleet, so two keys issued before either fleet is added never
        /// collide. Not saved: once a fleet with the id exists, the fleet list tracks it.
        /// </summary>
        private readonly HashSet<uint> fleetIdsIssued = new HashSet<uint>();

        /// <summary>
        /// Gets the key for a new fleet: the smallest fleet id this empire is not using
        /// (behavior-specs-10/fleet-movement-scanning-cargo.md §4, "Fleet lifecycle": creating a
        /// fleet finds the smallest unused id for that owner, so ids freed by destroyed, merged or
        /// scrapped fleets are reused). FleetCounter is kept as the highest id issued.
        /// </summary>
        public long GetNextFleetKey()
        {
            HashSet<uint> inUse = new HashSet<uint>();
            foreach (long key in OwnedFleets.Keys)
            {
                inUse.Add(key.Id());
            }

            foreach (Fleet pending in TemporaryFleets)
            {
                inUse.Add(pending.Id);
            }

            fleetIdsIssued.RemoveWhere(inUse.Contains);

            uint id = 1;
            while (inUse.Contains(id) || fleetIdsIssued.Contains(id))
            {
                id++;
            }

            fleetIdsIssued.Add(id);
            fleetCounter = Math.Max(fleetCounter, id);
            return (long)id | ((long)empireId << 32);
        }

        /// <summary>
        /// Gets the next available Key for a new Minefield laid by this empire - resolves
        /// Minefield.cs's own "lacks a non-static unique id" TODO by mirroring GetNextFleetKey's
        /// per-empire-counter-plus-owner-bits scheme, rather than that class's previous shared
        /// static counter (which produced colliding keys across empires and was never actually
        /// wired into a real Minefield's Key at all).
        /// </summary>
        public long GetNextMinefieldKey()
        {
            ++minefieldCounter;
            return (long)minefieldCounter | ((long)empireId << 32);
        }

        /// <summary>
        /// Gets the next available Key for the empire.
        /// </summary>
        /// <summary>
        /// Puts this empire's collections into the order (and transient state) a save and reload
        /// would give them - see CanonicalOrder. Called by the server at the end of every
        /// generation, so a game kept in memory continues exactly like a reloaded one.
        /// </summary>
        public void CompactCollections()
        {
            CanonicalOrder.Compact(Designs);
            CanonicalOrder.Compact(OwnedStars);
            CanonicalOrder.Compact(StarReports);
            CanonicalOrder.Compact(OwnedFleets);

            // A report with no ships is not saved (ToXml), so it is gone after a reload too.
            List<long> emptyReports = new List<long>();
            foreach (KeyValuePair<long, FleetIntel> entry in FleetReports)
            {
                if (entry.Value.Composition == null || entry.Value.Composition.Count == 0)
                {
                    emptyReports.Add(entry.Key);
                }
            }

            foreach (long key in emptyReports)
            {
                FleetReports.Remove(key);
            }

            CanonicalOrder.Compact(FleetReports);
            CanonicalOrder.Compact(WormholeReports);
            CanonicalOrder.CompactSorted(VisibleMinefields);
            CanonicalOrder.Compact(MineralPacketReports);
            CanonicalOrder.Compact(GrantedSpecialComponents);
            CanonicalOrder.Compact(EmpireReports);
            foreach (EmpireIntel report in EmpireReports.Values)
            {
                CanonicalOrder.Compact(report.Designs);
            }

            CanonicalOrder.Compact(BattlePlans);
            CanonicalOrder.Compact(AvailableComponents);

            // Designs are re-linked as loading does (fresh master parts, figures recomputed for
            // the current race and tech), and ships point at the empire's own design objects.
            AllComponents allComponents = new AllComponents();
            foreach (ShipDesign design in Designs.Values)
            {
                LinkDesign(design, allComponents);
            }

            foreach (Fleet fleet in OwnedFleets.Values)
            {
                CanonicalOrder.Compact(fleet.Composition);
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    if (token.Design != null && Designs.TryGetValue(token.Design.Key, out ShipDesign own))
                    {
                        token.Design = own;
                    }
                }
            }

            // Not saved: a reloaded empire starts with none outstanding.
            fleetIdsIssued.Clear();
        }

        /// <summary>
        /// Links one of this empire's designs the way loading a save does: every module's part
        /// becomes a fresh copy of the master component (AllComponents), and the design's
        /// figures are recomputed for this race and tech. The server also does this to a design
        /// arriving in an order (TurnGenerator.ParseCommands), so it never runs on the component
        /// data the client sent and a game kept in memory computes exactly what a reloaded one
        /// does.
        /// </summary>
        public void LinkDesign(ShipDesign design, AllComponents allComponents)
        {
            if (design?.Hull?.Modules == null)
            {
                return;
            }

            foreach (HullModule module in design.Hull.Modules)
            {
                if (module.AllocatedComponent != null && module.AllocatedComponent.Name != null)
                {
                    module.AllocatedComponent = allComponents.Fetch(module.AllocatedComponent.Name);
                }
            }

            design.Update(Race, ResearchLevels);
        }

        /// <summary>
        /// Records a design key this empire now uses that was issued elsewhere (a design added
        /// by an order: the client numbered it from its own copy of the counter), so the counter
        /// stays at the highest id in use - exactly what loading a save re-derives
        /// (LinkReferences). Without it a server kept in memory issued lower ids than a reloaded
        /// one, and could even re-issue an id already in use.
        /// </summary>
        public void TrackDesignKey(long designKey)
        {
            if (designKey.Id() > designCounter)
            {
                designCounter = designKey.Id();
            }
        }

        public long GetNextDesignKey()
        {
            ++designCounter;
            return (long)designCounter | ((long)empireId << 32);
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        public EmpireData() 
        {
            Initialize();
            BattlePlans.Add("Default", new BattlePlan());
        }

        protected virtual void Initialize()
        {
            AvailableComponents = new RaceComponents();
        }

        /// <summary>
        /// Determine if this empire wishes to treat lamb as an enemy.
        /// </summary>
        /// <param name="lamb">The id of the empire who may be attacked.</param>
        /// <returns>true if lamb is one of this empire's enemies, otherwise false.</returns>
        public bool IsEnemy(ushort lamb)
        {
            return EmpireReports[lamb].Relation == PlayerRelation.Enemy;
        }

        /// <summary>
        /// Load: constructor to load EmpireData from an XmlNode representation.
        /// </summary>
        /// <param name="node">An XmlNode containing a EmpireData representation (from a save file).</param>
        public EmpireData(XmlNode node)
        {
            Initialize();
            XmlNode mainNode = node.FirstChild;
            XmlNode subNode;
            while (mainNode != null)
            {
                try
                {
                switch (mainNode.Name.ToLowerInvariant())
                {
                    case "id":
                        empireId = ushort.Parse(mainNode.FirstChild.Value, System.Globalization.NumberStyles.HexNumber);
                        break;
                        
                    case "fleetcounter":
                        fleetCounter = long.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                        
                    case "designcounter":
                        designCounter = long.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;

                    case "minefieldcounter":
                        minefieldCounter = long.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;

                    case "turnyear":
                        TurnYear = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;
                        
                    case "turnsubmitted":
                        TurnSubmitted = bool.Parse(mainNode.FirstChild.Value);
                        break;
                        
                    case "lastturnsubmitted":
                        LastTurnSubmitted = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;

                    case "eliminated":
                        Eliminated = bool.Parse(mainNode.FirstChild.Value);
                        break;

                    case "randomseed":
                        RandomSeed = int.Parse(mainNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        break;

                    case "race":
                        race = new Race();
                        Race.LoadRaceFromXml(mainNode);
                        break;
                        
                    case "research":
                        subNode = mainNode.SelectSingleNode("Budget");
                        ResearchBudget = int.Parse(subNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        subNode = mainNode.SelectSingleNode("AttainedLevels");
                        ResearchLevels = new TechLevel(subNode);
                        subNode = mainNode.SelectSingleNode("SpentResources");
                        ResearchResources = new TechLevel(subNode);
                        subNode = mainNode.SelectSingleNode("Topics");
                        ResearchTopics = new TechLevel(subNode);
                        subNode = mainNode.SelectSingleNode("NextField");
                        if (subNode != null && subNode.FirstChild != null)
                        {
                            ResearchNextField = int.Parse(subNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        break;
                        
                    case "grantedspecialcomponents":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            GrantedSpecialComponents.Add(subNode.FirstChild.Value);
                            subNode = subNode.NextSibling;
                        }
                        break;

                    case "starreports":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            StarIntel report = new StarIntel(subNode);
                            StarReports.Add(report.Name, report);
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "ownedstars":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            Star star = new Star(subNode);
                            OwnedStars.Add(star);
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "fleetreports":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            FleetIntel report = new FleetIntel(subNode);
                            FleetReports.Add(report.Key, report);
                            subNode = subNode.NextSibling;
                        }
                        break;

                    case "wormholereports":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            WormholeIntel wormholeReport = new WormholeIntel(subNode);
                            WormholeReports[wormholeReport.Key] = wormholeReport;
                            subNode = subNode.NextSibling;
                        }
                        break;

                    case "mineralpacketreports":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            MineralPacket packetReport = new MineralPacket(subNode);
                            MineralPacketReports[packetReport.Key] = packetReport;
                            subNode = subNode.NextSibling;
                        }
                        break;

                    case "visibleminefields":
                        if (mainNode.FirstChild != null)
                        {
                            foreach (string minefieldKey in mainNode.FirstChild.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                VisibleMinefields.Add(long.Parse(minefieldKey, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture));
                            }
                        }
                        break;

                    case "ownedfleets":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            Fleet fleet = new Fleet(subNode);
                            OwnedFleets.Add(fleet);
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "otherempires":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            EmpireIntel report = new EmpireIntel(subNode);
                            EmpireReports.Add(report.Id, report);
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "battleplan":
                        BattlePlan plan = new BattlePlan(mainNode);
                        BattlePlans[plan.Name] = plan;
                        break;

                    case "productiontemplates":
                        ProductionTemplates = ProductionTemplateSet.FromXml(mainNode);
                        break;
                        
                    case "availablecomponents":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        { 
                            AvailableComponents.Add(new Component(subNode));
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "designs":
                        subNode = mainNode.FirstChild;
                        while (subNode != null)
                        {
                            ShipDesign design = new ShipDesign(subNode);
                            Designs.Add(design.Key, design);
                            
                            subNode = subNode.NextSibling;
                        }
                        break;
                        
                    case "battlereport":
                        BattleReport battle = new BattleReport(mainNode);
                        BattleReports.Add(battle);
                        break;
                }
                }
                catch (Exception e)
                {
                    // Non-fatal, matching every sibling XML loader in this codebase (see
                    // Waypoint.cs's own comment) - this switch previously had no try/catch at
                    // all, so a single malformed field here (or in any nested Star/Fleet/
                    // FleetIntel/etc. this constructs) threw straight out of this constructor
                    // uncaught, which is worse than the "one bad field exits the whole app"
                    // pattern fixed elsewhere: an empire whose data has one bad field couldn't
                    // even be skipped by a caller, since there is no outer field to skip.
                    Report.Error(e.Message + "\n Details: \n" + e);
                }

                // If no orders have ever been turned in then ensure battle plans contain at least the default
                if (BattlePlans.Count == 0)
                {
                    BattlePlans.Add("Default", new BattlePlan());
                }

                mainNode = mainNode.NextSibling;
            }

            try
            {
                LinkReferences();
            }
            catch (Exception e)
            {
                // Non-fatal, same reasoning as the loop above - this resolves cross-references
                // (fleet.InOrbit, token.Design, star.Starbase, ...) via several unguarded
                // dictionary indexers; one legitimately mismatched reference (e.g. a design key
                // an empire doesn't actually have) would otherwise throw straight out of this
                // constructor uncaught. The XML fields already loaded above stay intact either
                // way - only the cross-linking pass is what's cut short.
                Report.Error(e.Message + "\n Details: \n" + e);
            }
        }

        /// <summary>
        /// Save: Generate an XmlElement representation of the EmpireData.
        /// </summary>
        /// <param name="xmldoc">The parent XmlDocument.</param>
        /// <returns>An XmlElement representing the EmpireData (to be written to file).</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelEmpireData = xmldoc.CreateElement("EmpireData");
            
            Global.SaveData(xmldoc, xmlelEmpireData, "Id", empireId.ToString("X"));
                        
            Global.SaveData(xmldoc, xmlelEmpireData, "FleetCounter", fleetCounter.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelEmpireData, "DesignCounter", designCounter.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelEmpireData, "MinefieldCounter", minefieldCounter.ToString(System.Globalization.CultureInfo.InvariantCulture));
            
            Global.SaveData(xmldoc, xmlelEmpireData, "TurnYear", TurnYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelEmpireData, "TurnSubmitted", TurnSubmitted.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelEmpireData, "LastTurnSubmitted", LastTurnSubmitted.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Eliminated)
            {
                Global.SaveData(xmldoc, xmlelEmpireData, "Eliminated", "True");
            }

            if (RandomSeed != 0)
            {
                Global.SaveData(xmldoc, xmlelEmpireData, "RandomSeed", RandomSeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            xmlelEmpireData.AppendChild(race.ToXml(xmldoc));
            
            XmlElement xmlelResearch = xmldoc.CreateElement("Research");
            Global.SaveData(xmldoc, xmlelResearch, "Budget", ResearchBudget.ToString(System.Globalization.CultureInfo.InvariantCulture));            
            xmlelResearch.AppendChild(ResearchLevels.ToXml(xmldoc, "AttainedLevels"));
            xmlelResearch.AppendChild(ResearchResources.ToXml(xmldoc, "SpentResources"));
            xmlelResearch.AppendChild(ResearchTopics.ToXml(xmldoc, "Topics"));
            if (ResearchNextField != Research.NextFieldSame)
            {
                Global.SaveData(xmldoc, xmlelResearch, "NextField", ResearchNextField.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            xmlelEmpireData.AppendChild(xmlelResearch);
            
            // Available Components
            XmlElement xmlelAvaiableComponents = xmldoc.CreateElement("AvailableComponents");
            foreach (Component component in AvailableComponents.Values)
            {
                xmlelAvaiableComponents.AppendChild(component.ToXml(xmldoc));
            }
            xmlelEmpireData.AppendChild(xmlelAvaiableComponents);

            XmlElement xmlelGrantedSpecialComponents = xmldoc.CreateElement("GrantedSpecialComponents");
            foreach (string name in GrantedSpecialComponents)
            {
                Global.SaveData(xmldoc, xmlelGrantedSpecialComponents, "Name", name);
            }
            xmlelEmpireData.AppendChild(xmlelGrantedSpecialComponents);

            // Own Designs
            XmlElement xmlelDesigns = xmldoc.CreateElement("Designs");
            foreach (ShipDesign design in Designs.Values)
            {
                xmlelDesigns.AppendChild(design.ToXml(xmldoc));                                             
            }            
            xmlelEmpireData.AppendChild(xmlelDesigns);
            
            XmlElement xmlelStarReports = xmldoc.CreateElement("StarReports");            
            foreach (StarIntel report in StarReports.Values)
            {
                xmlelStarReports.AppendChild(report.ToXml(xmldoc));    
            }
            xmlelEmpireData.AppendChild(xmlelStarReports);
            
            XmlElement xmlelOwnedStars = xmldoc.CreateElement("OwnedStars");            
            foreach (Star star in OwnedStars.Values)
            {
                xmlelOwnedStars.AppendChild(star.ToXml(xmldoc));    
            }
            xmlelEmpireData.AppendChild(xmlelOwnedStars);
            
            XmlElement xmlelFleetReports = xmldoc.CreateElement("FleetReports");            
            foreach (FleetIntel report in FleetReports.Values)
            {
                if (report.Composition.Count > 0)
                {
                    xmlelFleetReports.AppendChild(report.ToXml(xmldoc));
                }
                else
                {
                    // Game crashes if it tries to write out a fleet report for a fleet with no ships. 
                    // This has been added to avoid the crash, but still let us know if zero ship fleets get this far, so we can find the cause.
                    // Dan 04 May 17 - this is triggered after a battle (and each turn there after) in Rev# 871
                    // Dan 04 May 17 - I think I fixed this with Rev# 872 by updating the attacker's fleet reports after combat.
                    Report.Error("EmpireData.ToXml(): Fleet " + report.Name + " contains no ships.");
                }
            }
            xmlelEmpireData.AppendChild(xmlelFleetReports);

            if (WormholeReports.Count > 0)
            {
                XmlElement xmlelWormholeReports = xmldoc.CreateElement("WormholeReports");
                foreach (WormholeIntel wormholeReport in WormholeReports.Values)
                {
                    xmlelWormholeReports.AppendChild(wormholeReport.ToXml(xmldoc));
                }
                xmlelEmpireData.AppendChild(xmlelWormholeReports);
            }

            if (MineralPacketReports.Count > 0)
            {
                XmlElement xmlelPacketReports = xmldoc.CreateElement("MineralPacketReports");
                foreach (MineralPacket packetReport in MineralPacketReports.Values)
                {
                    xmlelPacketReports.AppendChild(packetReport.ToXml(xmldoc));
                }
                xmlelEmpireData.AppendChild(xmlelPacketReports);
            }

            if (VisibleMinefields.Count > 0)
            {
                Global.SaveData(xmldoc, xmlelEmpireData, "VisibleMinefields", string.Join(",", VisibleMinefields.OrderBy(minefieldKey => minefieldKey).Select(minefieldKey => minefieldKey.ToString("X"))));
            }

            XmlElement xmlelOnedFleets = xmldoc.CreateElement("OwnedFleets");            
            foreach (Fleet fleet in OwnedFleets.Values)
            {
                xmlelOnedFleets.AppendChild(fleet.ToXml(xmldoc));    
            }
            xmlelEmpireData.AppendChild(xmlelOnedFleets);
            
            XmlElement xmlelEnemyIntel = xmldoc.CreateElement("OtherEmpires");            
            foreach (EmpireIntel report in EmpireReports.Values)
            {
                xmlelEnemyIntel.AppendChild(report.ToXml(xmldoc));    
            }
            xmlelEmpireData.AppendChild(xmlelEnemyIntel);
            
            foreach (string key in BattlePlans.Keys)
            {
                xmlelEmpireData.AppendChild(BattlePlans[key].ToXml(xmldoc));
            }

            xmlelEmpireData.AppendChild(ProductionTemplates.ToXml(xmldoc));

            // Battles 
            if (BattleReports.Count > 0)
            {
                foreach (BattleReport battle in BattleReports)
                {
                    xmlelEmpireData.AppendChild(battle.ToXml(xmldoc));
                }
            }
            
            return xmlelEmpireData;
        }
        
        public void Clear()
        {
            TurnYear = Global.StartingYear;
        
            Race = new Race();
            
            ResearchBudget = 10;
            ResearchLevels          = new TechLevel();
            ResearchResources       = new TechLevel();
            ResearchTopics          = new TechLevel();
            ResearchNextField       = Research.NextFieldSame;

            AvailableComponents     = new RaceComponents();
            Designs                 = new Dictionary<long, ShipDesign>();
            
            OwnedStars.Clear();
            StarReports.Clear();
            OwnedFleets.Clear();
            FleetReports.Clear();
            WormholeReports.Clear();
            VisibleMinefields.Clear();
            MineralPacketReports.Clear();
            GrantedSpecialComponents.Clear();
            
            EmpireReports.Clear();
            
            BattlePlans.Clear();
            BattleReports.Clear();
            ProductionTemplates = new ProductionTemplateSet();
        }
        
        
        /// <summary>
        /// Adds a new fleet to this empire. Generates an appropriate report.
        /// </summary>
        /// <param name="fleet">Fleet to add.</param>
        /// <returns>False if the fleet already exists for this empire.</returns>
        public bool AddOrUpdateFleet(Fleet fleet)
        {
            if (OwnedFleets.ContainsKey(fleet.Key))
            {
                FleetReports[fleet.Key].Update(fleet, ScanLevel.Owned, TurnYear);
                return false;
            }
            
            OwnedFleets.Add(fleet);
            
            if (FleetReports.ContainsKey(fleet.Key))
            {
                FleetReports[fleet.Key].Update(fleet, ScanLevel.Owned, TurnYear);
            }
            else
            {
                FleetReports.Add(fleet.Key, fleet.GenerateReport(ScanLevel.Owned, TurnYear));
            }
            
            return true;
        }
        
        
        /// <summary>
        /// Creates a brand new Fleet at the position of
        /// an already existing one.
        /// </summary>
        /// <param name="existing">Fleet from which to take a position.</param>
        /// <returns></returns>
        public Fleet MakeNewFleet(Fleet existing)
        {
            Fleet newFleet = new Fleet(GetNextFleetKey());

            newFleet.Type = ItemType.Fleet;

            // Have one waypoint to reflect the fleet's current position and the
            // planet it is in orbit around.
            Waypoint w = new Waypoint();
            w.Position = existing.Waypoints[0].Position;
            w.Destination = existing.Waypoints[0].Destination;
            w.WarpFactor = 0;

            newFleet.Waypoints.Add(w);

            // Inititialise the fleet elements that come from the star.

            newFleet.Position = existing.Position;
            newFleet.InOrbit = existing.InOrbit;

            newFleet.Name = "New Fleet #" + newFleet.Id;

            return newFleet;
        }
        
        
        /// <summary>
        /// Removes an existing fleet from this empire. Deletes appropriate report.
        /// </summary>
        /// <param name="fleet">Fleet to remove.</param>
        /// <returns>False if empire does not own the fleet.</returns>
        public bool RemoveFleet(Fleet fleet)
        {
            return RemoveFleet(fleet.Key);                    
        }
        
        
        /// <summary>
        /// Removes an existing fleet from this empire. Deletes appropriate report.
        /// </summary>
        /// <param name="fleet">Fleet Key to remove.</param>
        /// <returns>False if empire does not own the fleet.</returns>
        public bool RemoveFleet(long fleetKey)
        {
            if (!OwnedFleets.ContainsKey(fleetKey))
            {
                return false;
            }

            OwnedFleets.Remove(fleetKey);            
            FleetReports.Remove(fleetKey);
            
            return true;
        }
        
        
        /// <summary>
        /// Iterates through all Mappables in this Empire, in order.
        /// </summary>
        /// <returns>An enumerator containing all Mappables belonging to this empire.</returns>
        public IEnumerable<Mappable> IterateAllMappables()
        {
            return OwnedFleets.Values.Select(fleet => fleet as Mappable).Concat(OwnedStars.Values.Select(star => star as Mappable));
        }

        /// <summary>
        /// True when this empire may be shown the minefield: it owns it, or the server's ScanStep
        /// detected it this year (<see cref="VisibleMinefields"/>). A field the race merely knows
        /// is not written unless it is detected again this generation
        /// (behavior-specs-11/fleet-movement-scanning-cargo.md section 3).
        /// </summary>
        public bool CanSeeMinefield(Minefield minefield)
        {
            if (minefield == null)
            {
                return false;
            }

            return minefield.Owner == Id || VisibleMinefields.Contains(minefield.Key);
        }

        
        /// <summary>
        /// When state is loaded from file, objects may contain references to other objects.
        /// As these may be loaded in any order (or be cross linked) it is necessary to tidy
        /// up these references once the state is fully loaded and all objects exist.
        /// In most cases a placeholder object has been created with the Key set from the file,
        /// and we need to find the actual reference using this Key.
        /// Objects can't do this themselves as they don't have access to the state data, 
        /// so we do it here.
        /// </summary>
        private void LinkReferences()
        {
            AllComponents allComponents = new AllComponents();

            // HullModule reference to a component
            foreach (ShipDesign design in Designs.Values)
            {
                LinkDesign(design, allComponents);

                // designCounter is the LAST id issued (GetNextDesignKey pre-increments), so it
                // only has to reach the highest loaded id; "+ 1" here bumped it on every first
                // load, so a reloaded save no longer re-saved identically and one id was skipped.
                if (design.Id > designCounter)
                {
                    designCounter = design.Id;
                }
            }
            
            // Link enemy designs too
            foreach (EmpireIntel enemy in EmpireReports.Values)
            {
                foreach (ShipDesign design in enemy.Designs.Values)
                {
                    foreach (HullModule module in design.Hull.Modules)
                    {
                        if (module.AllocatedComponent != null && module.AllocatedComponent.Name != null)
                        {
                            module.AllocatedComponent = allComponents.Fetch(module.AllocatedComponent.Name);
                        }
                    }
                    
                    design.Update();
                }
            }
            
            // Fleet reference to Star
            foreach (Fleet fleet in OwnedFleets.Values)
            {
                if (fleet.InOrbit != null)
                {
                    // fleet.InOrbit is still just the name-only placeholder Star the XML loader
                    // built (see Fleet's own load constructor) - resolve it to the real, shared
                    // Star/StarIntel object this empire already has, rather than assuming
                    // StarReports/OwnedStars must have an entry for it. Every star should already
                    // have a StarReports placeholder for every empire from AssembleEmpireData at
                    // game creation, but a save/load round trip is not the place to crash over a
                    // gap in that bookkeeping - better to leave this one fleet's InOrbit
                    // unresolved (falls back to the placeholder, which at least still carries the
                    // star's name) than fail loading this empire's entire state.
                    if (StarReports.TryGetValue(fleet.InOrbit.Name, out StarIntel inOrbitReport))
                    {
                        if (inOrbitReport.Owner == fleet.Owner && OwnedStars.ContainsKey(fleet.InOrbit.Name))
                        {
                            fleet.InOrbit = OwnedStars[fleet.InOrbit.Name];
                        }
                        else
                        {
                            fleet.InOrbit = inOrbitReport;
                        }
                    }
                }
                
                // Ship reference to Design
                foreach (ShipToken token in fleet.Composition.Values)
                {
                    token.Design = Designs[token.Design.Key];
                }
            }
            
            // Set designs in any Battle Reports
            foreach (BattleReport battle in BattleReports)
            {
                foreach (Stack stack in battle.Stacks.Values)
                {
                    if (stack.Owner == empireId)
                    {
                        stack.Token.Design = Designs[stack.Token.Key];
                    }
                    else
                    {
                        stack.Token.Design = EmpireReports[stack.Owner].Designs[stack.Token.Key];
                    }
                }
            }
            
            // Link reports to Designs to get accurate data.
            foreach (FleetIntel report in FleetReports.Values)
            {
                foreach (ShipToken token in report.Composition.Values)
                {
                    if (report.Owner == Id)
                    {
                        token.Design = Designs[token.Design.Key];
                    }
                    else
                    {
                        token.Design = EmpireReports[report.Owner].Designs[token.Design.Key];
                    }
                }   
            }
            
            // Link Star Races and Starbases
            foreach (Star star in OwnedStars.Values)
            {
                if (star.Owner == Id)
                {
                    star.ThisRace = Race;
                    star.EnergyTechLevel = ResearchLevels[TechLevel.ResearchField.Energy];
                }
                else
                {
                    star.ThisRace = null;
                }

                if (star.Starbase != null)
                {
                    star.Starbase = OwnedFleets[star.Starbase.Key];
                }
            }

            RemoveOrphanedStarbaseFleets();

            // Same fix-up, but for this empire's own StarReports entries - without it, a report's
            // Starbase.Composition stays permanently empty after a normal save/load round trip
            // (StarIntel's XML constructor only recovers the placeholder Fleet(long) stub - see
            // that constructor's own "Placeholder constructor" comment), which meant the star
            // map's Stargate/Mass-Driver indicator dots (see StarMapDocumentViewModel) could never
            // show for the player's own starbases, only their mere presence. Scoped to reports
            // this empire itself owns - an enemy star's report doesn't carry full starbase
            // component data to resolve against in the first place.
            foreach (StarIntel report in StarReports.Values)
            {
                if (report.Owner == Id && report.Starbase != null && OwnedFleets.ContainsKey(report.Starbase.Key))
                {
                    report.Starbase = OwnedFleets[report.Starbase.Key];
                }
            }
        }

        /// <summary>
        /// Self-heals a real, previously-shipped bug (Manufacture.CreateShips's starbase-
        /// replacement branch): building a REPLACEMENT starbase detached the OLD one from
        /// star.Starbase without ever removing it from OwnedFleets/FleetReports, leaving it to
        /// linger there forever - showing up as a stray extra "fleet in orbit" at that star in
        /// every UI fleet listing (the Inspector's own Overview correctly showed the NEW one as
        /// the star's starbase throughout). Fixing the bug going forward doesn't repair a save
        /// that already has the corruption baked in, so this runs on every load instead of
        /// requiring a one-off fix per affected save: any owned, starbase-SHAPED fleet sitting in
        /// orbit at a star this empire owns, but which isn't that star's own (already-resolved)
        /// Starbase reference, is exactly that leftover - remove it. A star can only ever have
        /// one real starbase, so anything else matching this shape is corruption, never a
        /// legitimate second starbase.
        ///
        /// "Starbase-shaped" is judged from the fleet's own COMPOSITION (its design's Type), not
        /// the fleet's own Type field - confirmed from a real affected save that a game's very
        /// first, game-creation-time starbase (StarMapInitialiser.AllocateStarbase, a second,
        /// separate bug now also fixed) was never actually stamped Type=Starbase on the fleet
        /// itself in the first place, only correctly wired via star.Starbase - so checking the
        /// fleet's own Type here would have missed it entirely, exactly as it did live.
        /// </summary>
        private void RemoveOrphanedStarbaseFleets()
        {
            List<Fleet> orphaned = OwnedFleets.Values
                .Where(fleet => IsStarbaseShaped(fleet)
                    && fleet.InOrbit is Star star
                    && star.Owner == Id
                    && !ReferenceEquals(fleet, star.Starbase))
                .ToList();

            foreach (Fleet fleet in orphaned)
            {
                RemoveFleet(fleet);
            }
        }

        /// <summary>True if this fleet is built from a Starbase-type design - either because its
        /// own Type field says so (the normal, Manufacture.CreateShips-built case), or because
        /// its (immobile, single-design) composition's design is one, covering the
        /// StarMapInitialiser-built starting-starbase case where the fleet's own Type was never
        /// actually set at all. Checking the design rather than trusting the fleet's own Type is
        /// what makes this reliable regardless of which path created the fleet.</summary>
        private static bool IsStarbaseShaped(Fleet fleet)
        {
            if (fleet.Type == ItemType.Starbase)
            {
                return true;
            }

            ShipToken token = fleet.Composition.Values.FirstOrDefault();
            return token?.Design.Type == ItemType.Starbase;
        }
    }
}


