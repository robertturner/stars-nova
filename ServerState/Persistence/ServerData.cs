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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Xml;
    
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;
    
    using Message = Nova.Common.Message;
    
    /// <summary>
    /// This file contains data that are persistent across multiple invocations of
    /// Nova Server. (It also holds the odd item that doesn't need to be persistent
    /// but it's just convenient to keep all "global" data in one place.
    /// </summary>
    [Serializable]
    public class ServerData
    {
        public Dictionary<int, Stack<ICommand>> AllCommands     = new Dictionary<int, Stack<ICommand>>();
        public List<PlayerSettings>             AllPlayers      = new List<PlayerSettings>(); // Player number, race, ai (program name or "Default AI" or "Human")
        public Dictionary<int, int>             AllTechLevels   = new Dictionary<int, int>(); // Sum of a player's techlevels, for scoring purposes.
        public Dictionary<int, EmpireData>      AllEmpires      = new Dictionary<int, EmpireData>(); // Game specific data about the race; relations, battle plans, research, etc.
        public Dictionary<string, Race>         AllRaces        = new Dictionary<string, Race>(); // Data about the race (traits etc)
        public Dictionary<string, Star>         AllStars        = new Dictionary<string, Star>();
        public Dictionary<long, Minefield>      AllMinefields   = new Dictionary<long, Minefield>();
        public Dictionary<long, Wormhole>       AllWormholes    = new Dictionary<long, Wormhole>();
        public List<Message>                    AllMessages     = new List<Message>(); // All messages generated this turn.

        /// <summary>
        /// The Mystery Traders now in the galaxy, keyed by MysteryTrader.Key (several can be alive
        /// at once: the spawn never checks for an existing one, behavior-specs-10/
        /// turn-generation-engine.md §5a). See MysteryTraderStep.
        /// </summary>
        public Dictionary<long, MysteryTrader> AllMysteryTraders = new Dictionary<long, MysteryTrader>();

        /// <summary>
        /// Keys of fleets given out by a Mystery Trader (the original's per-fleet "Trader-given"
        /// flag, §5a): such a fleet is not told off with message 264 when it meets a Trader with
        /// too few minerals. Keys of fleets that no longer exist are pruned by MysteryTraderStep.
        /// </summary>
        public HashSet<long> MysteryTraderGiftFleets = new HashSet<long>();

        /// <summary>
        /// The mineral packets in flight, keyed by MineralPacket.Key (owner in the high bits).
        /// Launched by Manufacture (PacketLaunch), moved by PacketMovementStep, decayed by
        /// PacketDecayStep (behavior-specs-10/production-queue.md §10b, §10k item 4).
        /// </summary>
        public Dictionary<long, MineralPacket> AllMineralPackets = new Dictionary<long, MineralPacket>();

        /// <summary>
        /// Decaying deep-space mineral concentrations left by battle salvage that didn't happen
        /// over a planet - keyed by NovaPoint.ToHashString() so a second battle at the same exact
        /// point merges into the existing concentration rather than creating a duplicate. See
        /// docs/behavior-specs-5/combat-resolution.md §7 and BattleEngine.Run.
        /// </summary>
        public Dictionary<string, DeepSpaceMinerals> AllDeepSpaceMinerals = new Dictionary<string, DeepSpaceMinerals>();

        /// <summary>Every race's score for each of the most recent 100 turns
        /// (save-turn-file-format.md section 3), recorded by TurnGenerator.</summary>
        public ScoreHistory ScoreHistory = new ScoreHistory();

        public bool GameInProgress      = false;
        public int TurnYear             = Global.StartingYear;
        public string GameFolder        = null; // The path&folder where client files are held.
        public string StatePathName     = null; // path&file name to the saved state data

        /// <summary>
        /// The game's master random seed (the resolved GameSettings.Seed, set by
        /// Gameinitializer), persisted with the state so a saved and reloaded game draws exactly
        /// the same numbers as one that kept running. Every random draw of turn generation comes
        /// from a stream derived from it - see <see cref="CreateRandom"/>. Null (a state built by
        /// hand, or a save from before seeds were stored) keeps the old unseeded behaviour.
        /// </summary>
        public int? Seed                = null;

        /// <summary>
        /// This game's own settings (map size, game options, victory conditions...), saved
        /// inside the state, so turn generation reads the game's settings rather than whatever
        /// the process-wide GameSettings.Data holds (another game, the New Game screen, a test).
        /// TurnGenerator.Generate installs them for the generation (<see cref="UseSettings"/>).
        /// Set by Gameinitializer; a save from before this was stored adopts the game folder's
        /// .settings file the first time it is needed, else stays null (the old behaviour).
        /// </summary>
        public GameSettings Settings    = null;

        /// <summary>The year random streams are derived from during a generation (fixed at its
        /// start by <see cref="BeginRandomTurn"/>, so the mid-generation year increment does not
        /// re-key the late steps); null outside a generation (then TurnYear is used).</summary>
        [NonSerialized]
        private int? randomEpochYear = null;

        /// <summary>How many streams of each (year, name, sub key) were handed out this epoch,
        /// so asking twice for the same stream gives two different but reproducible sequences.
        /// Transient: reset at the start of every generation, so it never depends on process
        /// history.</summary>
        [NonSerialized]
        private Dictionary<string, int> randomStreamUses = new Dictionary<string, int>();

        private Dictionary<string, Star> starPositionDictionary = null;
        
        /// <summary>
        /// Creates a new fresh server state.
        /// </summary>
        public ServerData()
        { 
        }
  
        /// <summary>
        /// Load <see cref="Intel">ServerState</see> from an xml document.
        /// </summary>
        /// <param name="xmldoc">Produced using XmlDocument.Load(filename).</param>
        public ServerData(XmlDocument xmldoc)
        {            
            XmlNode xmlnode = xmldoc.DocumentElement;
            XmlNode textNode;
            
            while (xmlnode != null)
            {
                try
                {
                    switch (xmlnode.Name.ToLowerInvariant())
                    {
                        case "root":
                            xmlnode = xmlnode.FirstChild;
                            continue;
                        case "serverstate":
                            xmlnode = xmlnode.FirstChild;
                            continue;
                        
                        case "gameinprogress":
                            GameInProgress = bool.Parse(xmlnode.FirstChild.Value);
                            break;                                                
                        case "turnyear":
                            TurnYear = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;                        
                        case "gamefolder":
                            GameFolder = xmlnode.FirstChild.Value;
                            break;                        
                        case "statepathname":
                            StatePathName = xmlnode.FirstChild.Value;
                            break;

                        case "seed":
                            Seed = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;

                        case "gamesettings":
                            Settings = GameSettings.FromXmlText(xmlnode.OuterXml);
                            break;

                        // The collections are retrieved via loops: we trust
                        // they are in the correct format.
                        
                        case "allplayers":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                AllPlayers.Add(new PlayerSettings(textNode));
                                textNode = textNode.NextSibling;
                            }
                            break;
                        
                        case "alltechlevels":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                AllTechLevels.Add(int.Parse(textNode.Attributes["Key"].Value, System.Globalization.NumberStyles.HexNumber), int.Parse(textNode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture));
                                textNode = textNode.NextSibling;
                            }
                            break;
                        
                        case "allempires":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                AllEmpires.Add(
                                    int.Parse(textNode.Attributes["Key"].Value, System.Globalization.NumberStyles.HexNumber),
                                    new EmpireData(textNode));
                                textNode = textNode.NextSibling;
                            }
                            break;
                        
                        case "allraces":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                Race race = new Race();
                                race.LoadRaceFromXml(textNode);
                                AllRaces.Add(textNode.Attributes["Key"].Value, race);
                                textNode = textNode.NextSibling;
                            }
                            break;
                        
                        case "allstars":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                AllStars.Add(textNode.Attributes["Key"].Value, new Star(textNode));
                                textNode = textNode.NextSibling;
                            }
                            break;
                        
                        case "allminefields":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                // A minefield laid by an owned empire (see EmpireData.GetNextMinefieldKey)
                                // encodes that owner in the key's high bits, so its hex text almost
                                // always exceeds int.MaxValue - re-parsing it here via int.Parse
                                // would throw OverflowException. Minefield's own XmlNode constructor
                                // (via Item's) already parses "Key" correctly as a long - reuse that
                                // instead of re-parsing the same attribute a second time, matching
                                // how Designs.Add(design.Key, design) is done elsewhere.
                                Minefield minefield = new Minefield(textNode);
                                AllMinefields.Add(minefield.Key, minefield);
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "allwormholes":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                Wormhole wormhole = new Wormhole(textNode);
                                AllWormholes.Add(wormhole.Key, wormhole);
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "allmysterytraders":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                MysteryTrader trader = new MysteryTrader(textNode);
                                AllMysteryTraders.Add(trader.Key, trader);
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "allmineralpackets":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                MineralPacket packet = new MineralPacket(textNode);
                                AllMineralPackets[packet.Key] = packet;
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "mysterytradergiftfleets":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                MysteryTraderGiftFleets.Add(long.Parse(textNode.FirstChild.Value, System.Globalization.NumberStyles.HexNumber));
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "allmessages":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                AllMessages.Add(new Message(textNode));
                                textNode = textNode.NextSibling;
                            }
                            break;

                        case "scorehistory":
                            ScoreHistory = new ScoreHistory(xmlnode);
                            break;

                        case "alldeepspaceminerals":
                            textNode = xmlnode.FirstChild;
                            while (textNode != null)
                            {
                                DeepSpaceMinerals deepSpaceMinerals = new DeepSpaceMinerals(textNode);

                                // Several wreckage objects can share one spot (each holds at most
                                // 30,000 kT, BattleEngine.AddWreckage): later ones get "#n" keys.
                                string wreckageKey = deepSpaceMinerals.Position.ToHashString();
                                for (int slot = 1; AllDeepSpaceMinerals.ContainsKey(wreckageKey); slot++)
                                {
                                    wreckageKey = deepSpaceMinerals.Position.ToHashString() + "#" + slot;
                                }

                                AllDeepSpaceMinerals[wreckageKey] = deepSpaceMinerals;
                                textNode = textNode.NextSibling;
                            }
                            break;
                    }
                    
                    xmlnode = xmlnode.NextSibling;
                }
                catch (Exception e)
                {
                    // Non-fatal - see Waypoint.cs's own comment for the live-reproduced crash
                    // this "one bad field exits the whole app" pattern caused. The advance above
                    // sits inside the try (unlike its siblings in other files' loaders), so it
                    // never ran when this fires - repeat it here too, or a node that keeps
                    // throwing would spin this loop forever instead of just being skipped.
                    Report.Error(e.Message + "\n Details: \n" + e);
                    xmlnode = xmlnode?.NextSibling;
                }
            }
        }

        /// <summary>
        /// Restore the persistent data. 
        /// </summary>
        public void Restore()
        {
            bool waitForFile = false;
            double waitTime = 0.0; // seconds
            do
            {
                try
                {
                    using (FileStream stateFile = new FileStream(StatePathName, FileMode.Open))
                    {
                        XmlDocument xmldoc = new XmlDocument();

                        xmldoc.Load(stateFile);
                
                        // Temporary data store only!
                        ServerData restoredState = new ServerData(xmldoc);
                
                        // We need to copy the restored values
                        AllCommands     = restoredState.AllCommands;
                        AllPlayers      = restoredState.AllPlayers;
                        AllTechLevels   = restoredState.AllTechLevels;
                        AllEmpires      = restoredState.AllEmpires;
                        AllRaces        = restoredState.AllRaces;
                        AllStars        = restoredState.AllStars;
                        AllMinefields   = restoredState.AllMinefields;
                        AllWormholes    = restoredState.AllWormholes;
                        AllMysteryTraders = restoredState.AllMysteryTraders;
                        MysteryTraderGiftFleets = restoredState.MysteryTraderGiftFleets;
                        AllMineralPackets = restoredState.AllMineralPackets;
                        AllMessages     = restoredState.AllMessages;
                        AllDeepSpaceMinerals = restoredState.AllDeepSpaceMinerals;
                        ScoreHistory    = restoredState.ScoreHistory;
        
                        GameInProgress    = restoredState.GameInProgress;
                        TurnYear          = restoredState.TurnYear;
                        GameFolder        = restoredState.GameFolder; // The path&folder where client files are held.
                        StatePathName     = restoredState.StatePathName;
                        Seed              = restoredState.Seed;
                        Settings          = restoredState.Settings;

                        LinkServerStateReferences();
                    }
                    waitForFile = false;
                }
                catch (System.IO.IOException)
                {
                    // IOException. Is the file locked? Try waiting.
                    if (waitTime < Global.TotalFileWaitTime)
                    {
                        waitForFile = true;
                        System.Threading.Thread.Sleep(Global.FileWaitRetryTime);
                        waitTime += 0.1;
                    }
                    else
                    {
                        // Give up, maybe something else is wrong?
                        throw;
                    }
                }
            } 
            while (waitForFile);
        }

        /// <summary>
        /// Save the console persistent data.
        /// </summary>
        public void Save()
        {
            if (StatePathName == null)
            {
                // TODO (priority 5) add the nicities. Update the game files location.
                string chosen = PlatformHooks.AskUserForSaveFile("Choose a location to save the game.");
                if (chosen != null)
                {
                    StatePathName = chosen;
                }
                else
                {
                    throw new System.IO.IOException("File dialog cancelled");
                }
            }

            ToXml();
        }
        
        /// <summary>
        /// Save: Serialize this object to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Intel</returns>
        public void ToXml()
        {            
            XmlDocument xmldoc = new XmlDocument();
            XmlElement xmlRoot = Global.InitializeXmlDocument(xmldoc);
            XmlElement child;
            
            // create the outer element
            XmlElement xmlelServerState = xmldoc.CreateElement("ServerState");
            xmlRoot.AppendChild(xmlelServerState);
            
            Global.SaveData(xmldoc, xmlelServerState, "GameInProgress", GameInProgress.ToString());
            // Global.SaveData(xmldoc, xmlelServerState, "FleetID", FleetID.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelServerState, "TurnYear", TurnYear.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Global.SaveData(xmldoc, xmlelServerState, "GameFolder", GameFolder);
            Global.SaveData(xmldoc, xmlelServerState, "StatePathName", StatePathName);
            if (Seed.HasValue)
            {
                Global.SaveData(xmldoc, xmlelServerState, "Seed", Seed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            if (Settings != null)
            {
                XmlDocument settingsDocument = new XmlDocument();
                settingsDocument.LoadXml(Settings.ToXmlText());
                xmlelServerState.AppendChild(xmldoc.ImportNode(settingsDocument.DocumentElement, true));
            }

            // Store the players
            XmlElement xmlelAllPlayers = xmldoc.CreateElement("AllPlayers");
            foreach (PlayerSettings playerSettings in AllPlayers)
            {
                xmlelAllPlayers.AppendChild(playerSettings.ToXml(xmldoc));
            }
            xmlelServerState.AppendChild(xmlelAllPlayers);        
            
            // Store the Races
            XmlElement xmlelAllRaces = xmldoc.CreateElement("AllRaces");
            foreach (KeyValuePair<string, Race> race in AllRaces)
            {
                child = race.Value.ToXml(xmldoc);
                child.SetAttribute("Key", race.Key);                
                xmlelAllRaces.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllRaces);
            
            // Store the Empire's Data
            XmlElement xmlelAllEmpires = xmldoc.CreateElement("AllEmpires");
            foreach (KeyValuePair<int, EmpireData> empireData in AllEmpires)
            {
                child = empireData.Value.ToXml(xmldoc);
                child.SetAttribute("Key", empireData.Key.ToString("X"));
                xmlelAllEmpires.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllEmpires);
            
            // Store the tech level sums.
            XmlElement xmlelAllTechLevels = xmldoc.CreateElement("AllTechLevels");
            foreach (KeyValuePair<int, int> techLevels in AllTechLevels)
            {
                child = xmldoc.CreateElement("TechLevels");                
                child.SetAttribute("Key", techLevels.Key.ToString("X"));
                child.InnerText = techLevels.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                xmlelAllTechLevels.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllTechLevels);  
            
            // Store the Stars
            XmlElement xmlelAllStars = xmldoc.CreateElement("AllStars");
            foreach (KeyValuePair<string, Star> star in AllStars)
            {
                child = star.Value.ToXml(xmldoc);
                child.SetAttribute("Key", star.Key);                
                xmlelAllStars.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllStars);

            // Store the Minefields
            XmlElement xmlelAllMinefields = xmldoc.CreateElement("AllMinefields");
            foreach (KeyValuePair<long, Minefield> minefield in AllMinefields)
            {
                child = minefield.Value.ToXml(xmldoc);
                child.SetAttribute("Key", minefield.Key.ToString("X"));                
                xmlelAllMinefields.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllMinefields);

            // Store the Wormholes
            XmlElement xmlelAllWormholes = xmldoc.CreateElement("AllWormholes");
            foreach (KeyValuePair<long, Wormhole> wormhole in AllWormholes)
            {
                child = wormhole.Value.ToXml(xmldoc);
                child.SetAttribute("Key", wormhole.Key.ToString("X"));
                xmlelAllWormholes.AppendChild(child);
            }
            xmlelServerState.AppendChild(xmlelAllWormholes);

            // Store the Mystery Traders and the Trader-given fleet flags
            XmlElement xmlelAllMysteryTraders = xmldoc.CreateElement("AllMysteryTraders");
            foreach (MysteryTrader trader in AllMysteryTraders.Values)
            {
                xmlelAllMysteryTraders.AppendChild(trader.ToXml(xmldoc));
            }
            xmlelServerState.AppendChild(xmlelAllMysteryTraders);

            XmlElement xmlelGiftFleets = xmldoc.CreateElement("MysteryTraderGiftFleets");
            foreach (long fleetKey in MysteryTraderGiftFleets)
            {
                Global.SaveData(xmldoc, xmlelGiftFleets, "Fleet", fleetKey.ToString("X"));
            }
            xmlelServerState.AppendChild(xmlelGiftFleets);

            // Store the mineral packets in flight
            XmlElement xmlelAllMineralPackets = xmldoc.CreateElement("AllMineralPackets");
            foreach (MineralPacket packet in AllMineralPackets.Values)
            {
                xmlelAllMineralPackets.AppendChild(packet.ToXml(xmldoc));
            }
            xmlelServerState.AppendChild(xmlelAllMineralPackets);

            // Store the Messages
            XmlElement xmlelAllMessages = xmldoc.CreateElement("AllMessages");
            foreach (Message message in AllMessages)
            {
                xmlelAllMessages.AppendChild(message.ToXml(xmldoc));
            }
            xmlelServerState.AppendChild(xmlelAllMessages);

            // Store the deep-space mineral concentrations
            XmlElement xmlelAllDeepSpaceMinerals = xmldoc.CreateElement("AllDeepSpaceMinerals");
            foreach (DeepSpaceMinerals deepSpaceMinerals in AllDeepSpaceMinerals.Values)
            {
                xmlelAllDeepSpaceMinerals.AppendChild(deepSpaceMinerals.ToXml(xmldoc));
            }
            xmlelServerState.AppendChild(xmlelAllDeepSpaceMinerals);

            xmlelServerState.AppendChild(ScoreHistory.ToXml(xmldoc));

            xmldoc.Save(StatePathName);
        }
  
        /// <summary>
        /// When state is loaded from file, objects may contain references to other objects.
        /// As these may be loaded in any order (or be cross linked) it is necessary to tidy
        /// up these references once the file is fully loaded and all objects exist.
        /// In most cases a placeholder object has been created with the Name set from the file,
        /// and we need to find the actual reference using this Name.
        /// Objects can't do this themselves as they don't have access to the state data, 
        /// so we do it here.
        /// </summary>
        private void LinkServerStateReferences()
        {
            AllComponents allComponents = new AllComponents();

            foreach (Star star in AllStars.Values)
            {
                // Star reference to the Race that owns it
                if (star.ThisRace != null)
                {
                    // A star that lost its owner (Owner 0, Nobody) can still carry its old race
                    // name: that used to throw here (no empire 0) and made the save unloadable.
                    // It has no race now (CompactCollections does the same in memory).
                    if (AllEmpires.TryGetValue(star.Owner, out EmpireData starOwner) && star.Owner == starOwner.Id
                        && AllRaces.TryGetValue(star.ThisRace.Name, out Race ownerRace))
                    {
                        star.ThisRace = ownerRace;
                        star.EnergyTechLevel = starOwner.ResearchLevels[TechLevel.ResearchField.Energy];
                    }
                    else
                    {
                        star.ThisRace = null;
                    }
                }

                // Star reference to it's Starbase if any
                if (star.Starbase != null)
                {
                    star.Starbase = AllEmpires[star.Starbase.Key.Owner()].OwnedFleets[star.Starbase.Key];
                }
            }
            
            // Link inside EmpireData
            foreach (EmpireData empire in AllEmpires.Values)
            {
                foreach (ShipDesign design in empire.Designs.Values)
                {
                    foreach (HullModule module in design.Hull.Modules)
                    {
                        if (module.AllocatedComponent != null && module.AllocatedComponent.Name != null)
                        {
                            module.AllocatedComponent = allComponents.Fetch(module.AllocatedComponent.Name);
                        }
                    }
                }
            
                foreach (Fleet fleet in empire.OwnedFleets.Values)
                {
                    // Fleet reference to Star it is orbiting
                    if (fleet.InOrbit != null)
                    {
                        fleet.InOrbit = AllStars[fleet.InOrbit.Name];
                    }
                    // Ship reference to Design
                    foreach (ShipToken token in fleet.Composition.Values)
                    {
                        token.Design = empire.Designs[token.Design.Key];
                    }
                }
                 
                foreach (Star star in empire.OwnedStars.Values)
                {
                    if (star.ThisRace != null)
                    {
                        // Reduntant, but works to check if race name is valid...
                        if (star.Owner == empire.Id)
                        {
                            star.ThisRace = empire.Race;
                            star.EnergyTechLevel = empire.ResearchLevels[TechLevel.ResearchField.Energy];
                        }
                        else
                        {
                            star.ThisRace = null;
                        }
                    }
    
                    if (star.Starbase != null)
                    {
                        star.Starbase = empire.OwnedFleets[star.Starbase.Key];
                    }
                }
            }
        }     

        
        /// <summary>
        /// Reset all values to the defaults.
        /// </summary>
        public void Clear()
        {   
            AllCommands.Clear();
            AllPlayers.Clear();  
            AllTechLevels.Clear();
            AllEmpires.Clear();
            AllRaces.Clear();
            AllStars.Clear();
            AllMinefields.Clear();
            AllWormholes.Clear();
            AllMysteryTraders.Clear();
            MysteryTraderGiftFleets.Clear();
            AllMineralPackets.Clear();
            AllMessages.Clear();
            AllDeepSpaceMinerals.Clear();
            ScoreHistory.Clear();

            GameFolder     = null;
            GameInProgress = false;
            TurnYear       = Global.StartingYear;
            StatePathName  = null;
            Seed           = null;
            Settings       = null;
            randomEpochYear = null;
            randomStreamUses = new Dictionary<string, int>();
        }

        /// <summary>
        /// Puts every collection of the game into the order (and keys) a save and reload would
        /// give it - see Nova.Common.CanonicalOrder. TurnGenerator.Generate calls it last, so a
        /// server kept in memory between turns (NovaConsole, a simulation harness) plays on
        /// exactly like one that reloads the saved state every turn (TurnHost).
        /// </summary>
        public void CompactCollections()
        {
            CanonicalOrder.Compact(AllCommands);
            CanonicalOrder.Compact(AllTechLevels);
            CanonicalOrder.Compact(AllEmpires);
            CanonicalOrder.Compact(AllRaces);
            CanonicalOrder.Compact(AllStars);
            CanonicalOrder.Compact(AllMinefields);
            CanonicalOrder.Compact(AllWormholes);
            CanonicalOrder.Compact(AllMysteryTraders);
            CanonicalOrder.Compact(MysteryTraderGiftFleets);
            CanonicalOrder.Compact(AllMineralPackets);

            foreach (Minefield minefield in AllMinefields.Values)
            {
                CanonicalOrder.CompactSorted(minefield.VisibleTo);
            }

            foreach (Wormhole wormhole in AllWormholes.Values)
            {
                CanonicalOrder.CompactSorted(wormhole.UsedBy);
            }

            foreach (MysteryTrader trader in AllMysteryTraders.Values)
            {
                CanonicalOrder.Compact(trader.ServedRaces);
            }

            // Wreckage is re-keyed exactly as the loader keys it (position, then "#n" for later
            // objects at the same spot), so a slot freed by decay is not kept in memory only.
            List<DeepSpaceMinerals> wreckage = new List<DeepSpaceMinerals>(AllDeepSpaceMinerals.Values);
            AllDeepSpaceMinerals.Clear();
            foreach (DeepSpaceMinerals deepSpaceMinerals in wreckage)
            {
                string wreckageKey = deepSpaceMinerals.Position.ToHashString();
                for (int slot = 1; AllDeepSpaceMinerals.ContainsKey(wreckageKey); slot++)
                {
                    wreckageKey = deepSpaceMinerals.Position.ToHashString() + "#" + slot;
                }

                AllDeepSpaceMinerals[wreckageKey] = deepSpaceMinerals;
            }

            foreach (EmpireData empire in AllEmpires.Values)
            {
                empire.CompactCollections();
            }

            // Star.EnergyTechLevel is not saved: loading re-derives it from the owner's current
            // research (LinkServerStateReferences). Do the same here, or a star kept in memory
            // keeps the level it had when it was last linked (Alternate Reality resources).
            // A star with no owning empire has no race after loading either.
            foreach (Star star in AllStars.Values)
            {
                if (star.ThisRace == null)
                {
                    continue;
                }

                if (AllEmpires.TryGetValue(star.Owner, out EmpireData owner) && AllRaces.ContainsKey(star.ThisRace.Name))
                {
                    star.EnergyTechLevel = owner.ResearchLevels[TechLevel.ResearchField.Energy];
                }
                else
                {
                    star.ThisRace = null;
                }
            }

            foreach (EmpireData empire in AllEmpires.Values)
            {
                foreach (Star star in empire.OwnedStars.Values)
                {
                    if (star.ThisRace != null && star.Owner == empire.Id)
                    {
                        star.EnergyTechLevel = empire.ResearchLevels[TechLevel.ResearchField.Energy];
                    }
                }
            }
        }

        /// <summary>
        /// Installs this game's <see cref="Settings"/> as GameSettings.Data until the returned
        /// scope is disposed (see GameSettings.Use). A state saved before settings were stored
        /// adopts its game folder's .settings file (the state file's name with the .settings
        /// extension, as Gameinitializer names it) the first time; with neither, Data is left
        /// alone, exactly as before.
        /// </summary>
        public IDisposable UseSettings()
        {
            if (Settings == null && !string.IsNullOrEmpty(StatePathName))
            {
                Settings = GameSettings.TryLoad(Path.ChangeExtension(StatePathName, Global.SettingsExtension));
            }

            return GameSettings.Use(Settings);
        }

        /// <summary>
        /// Starts a generation's random epoch (TurnGenerator.Generate calls it first): streams
        /// are keyed on the current (pre-increment) year until the next call, and the per-stream
        /// use counts start again from zero - so the draws of a generation depend only on the
        /// seed, the year and the stream keys, never on what ran earlier in this process.
        /// </summary>
        public void BeginRandomTurn()
        {
            randomEpochYear = TurnYear;
            randomStreamUses = new Dictionary<string, int>();
        }

        /// <summary>
        /// A random stream for one consumer of this game: derived from <see cref="Seed"/>, the
        /// epoch year, the stream name and <paramref name="subKey"/> (a fleet key, an empire id,
        /// a step key...), and the number of times that same stream was already asked for this
        /// epoch (GameRandom.DeriveSeed). Separate names give independent streams, so an extra
        /// draw in one step never shifts another's. With no seed (hand-built states, older
        /// saves) it is an unseeded Random, the old behaviour.
        /// </summary>
        public Random CreateRandom(string stream, long subKey = 0)
        {
            if (!Seed.HasValue)
            {
                return new Random();
            }

            int year = randomEpochYear ?? TurnYear;
            string useKey = year.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + stream + "|"
                + subKey.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int index;
            if (randomStreamUses == null)
            {
                randomStreamUses = new Dictionary<string, int>();
            }

            randomStreamUses.TryGetValue(useKey, out index);
            randomStreamUses[useKey] = index + 1;

            return GameRandom.Create(Seed.Value, year, stream, subKey, index);
        }
        
        
        /// <summary>
        /// Iterates through all Fleets in all Empires, in order.
        /// </summary>
        /// <returns>An enumerator containing all Fleets from all empires.</returns>
        public IEnumerable<Fleet> IterateAllFleets()
        {
            return AllEmpires.Values.SelectMany(empire => empire.OwnedFleets.Values);
        }

        /// <summary>
        /// This turn's empire processing order, reshuffled once per turn by TurnGenerator before
        /// fleet movement - behavior-specs-7/ai-opponent-behavior.md §8 and turn-generation-engine.md
        /// §1 both confirm a genuine Fisher-Yates shuffle of player-slot indices in the original
        /// game's master turn routine, run immediately before per-player turn-generation dispatch,
        /// rather than always processing empires in the same fixed (dictionary) order every turn.
        /// Transient, per-turn scratch state - not persisted, recomputed at the start of every
        /// Generate() call. Falls back to AllEmpires.Values' own order if never set (e.g. tests
        /// that construct a ServerData and call a turn step directly without going through
        /// TurnGenerator.Generate()).
        /// </summary>
        public List<EmpireData> ShuffledEmpireOrder;

        /// <summary>
        /// Fisher-Yates shuffle of this turn's empires - see <see cref="ShuffledEmpireOrder"/>.
        /// </summary>
        public List<EmpireData> ComputeShuffledEmpireOrder(Random random)
        {
            List<EmpireData> order = AllEmpires.Values.ToList();
            for (int i = order.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }

            return order;
        }

        /// <summary>
        /// Iterates through all Fleets in all Empires, in this turn's shuffled empire order (see
        /// <see cref="ShuffledEmpireOrder"/>) rather than the fixed dictionary order
        /// <see cref="IterateAllFleets"/> uses - for the specific turn-processing passes where
        /// which empire's fleet is considered first can affect a shared, contested outcome (e.g.
        /// RemoteMiningStep's per-star mining order). Falls back to <see cref="IterateAllFleets"/>'s
        /// own order if no shuffle has been computed yet this turn.
        /// </summary>
        public IEnumerable<Fleet> IterateAllFleetsInShuffledOrder()
        {
            IEnumerable<EmpireData> order = ShuffledEmpireOrder != null ? (IEnumerable<EmpireData>)ShuffledEmpireOrder : AllEmpires.Values;
            return order.SelectMany(empire => empire.OwnedFleets.Values);
        }


        /// <summary>
        /// Iterates through all Designs in all Empires, in order.
        /// </summary>
        /// <returns>An enumerator containing all Designs from all empires.</returns>
        public IEnumerable<ShipDesign> IterateAllDesigns()
        {
            return AllEmpires.Values.SelectMany(empire => empire.Designs.Values);
        }
        
        /// <summary>
        /// Iterates through all Mappables in all Empires/Universe, in order.
        /// </summary>
        /// <returns>An enumerator containing all Mappables from all empires/universe.</returns>
        public IEnumerable<Mappable> IterateAllMappables()
        {
            return AllStars.Values.Select(star => star as Mappable).Concat(AllEmpires.Values.SelectMany(empire => empire.OwnedFleets.Values.Select(fleet => fleet as Mappable)));
        }

        /// <summary>
        /// Remove fleets that no longer have ships.
        /// This needs to be done after each time the fleet list is processed, as fleets can not be destroyed until the iterator completes.
        /// </summary>
        public void CleanupFleets()
        {
            // create a list of all fleets that have been destroyed
            List<long> destroyedFleets = new List<long>();

            foreach (Fleet fleet in IterateAllFleets())
            {
                if (fleet.Composition.Count == 0)
                {
                    destroyedFleets.Add(fleet.Key);
                }
            }

            foreach (long key in destroyedFleets)
            {
                foreach (EmpireData empire in AllEmpires.Values)
                {
                    empire.RemoveFleet(key);
                }
            }

            // And remove stations too.
            List<string> destroyedStations = new List<string>();
            foreach (Star star in AllStars.Values)
            {
                if (star.Starbase != null && star.Starbase.Composition.Count == 0)
                {
                    destroyedStations.Add(star.Name);
                }
            }
            foreach (string key in destroyedStations)
            {
                Star station = AllStars[key];
                station.Starbase = null;
                DepopulateAlternateRealityPlanet(station);
            }

            // Get fleets out of limbo.
            foreach (EmpireData empire in AllEmpires.Values)
            {
                if (empire.TemporaryFleets.Count > 0)
                {
                    foreach (Fleet newFleet in empire.TemporaryFleets)
                    {
                        empire.AddOrUpdateFleet(newFleet);
                    }

                    // Promoted: drop them from limbo, or every later cleanup this turn (and every
                    // later turn) would re-add them - bringing back a split-off fleet that has
                    // since merged away, been destroyed or scrapped.
                    empire.TemporaryFleets.Clear();
                }
            }
        }

        /// <summary>
        /// Losing its starbase depopulates an Alternate Reality planet: its whole population lived
        /// in that orbital habitat, so the owner is cleared and the population set to 0
        /// (behavior-specs-9/population-growth.md section 3; messages 141/142 to the owner - 142 when
        /// under 1,001 population units - and 324 to the destroyer). The destroyer is not tracked
        /// here, so only the owner is told.
        /// </summary>
        private void DepopulateAlternateRealityPlanet(Star star)
        {
            EmpireData owner;
            if (star.Owner == Global.Nobody || !AllEmpires.TryGetValue(star.Owner, out owner)
                || owner.Race == null || !owner.Race.HasTrait("AR"))
            {
                return;
            }

            Message message = new Message();
            message.Audience = star.Owner;
            message.Text = "The orbital habitat at " + star.Name + " has been destroyed; its colony of "
                + star.Colonists + " colonists did not survive.";
            AllMessages.Add(message);

            owner.OwnedStars.Remove(star);
            star.ManufacturingQueue.Clear();
            star.Colonists = 0;
            star.Owner = Global.Nobody;
        }

        // See if the fleet is orbiting a star
        public void SetFleetOrbit(Fleet fleet)
        {
            try
            {
                fleet.InOrbit = GetStarAtPosition(fleet.Position);
            }
            catch 
            {
                fleet.InOrbit = null;
            }
        }

        public Star GetStarAtPosition(NovaPoint position)
        {
            if (starPositionDictionary == null)
            {
                starPositionDictionary = new Dictionary<string, Star>();
                foreach (Star star in AllStars.Values)
                {
                    starPositionDictionary.Add(star.Position.ToHashString(), star);
                }
            }

            return starPositionDictionary[position.ToHashString()];
        }
    }
}
