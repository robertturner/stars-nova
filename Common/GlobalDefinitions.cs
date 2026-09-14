#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011 The Stars-Nova Project
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

#region Module Description
// ===========================================================================
// This module holds definitions that are global across all Nova application
// programs. 
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.IO;
    using System.Xml;

    public static class Global
   {
       public const string NovaWebSite = "https://sourceforge.net/p/stars-nova/wiki/Home/";

       // Note: Client/SeverState.GameFolder vs ServerFolder vs ClientFolder
       // In a single player game the ServerFolder == ClientFolder == "GameFiles"
       // The only reason to save seperate keys for ServerFolder and ClientFolder
       // is so that we can simulate a network game on a single PC. When this
       // is done it will be necessary to transfer the game files from one
       // folder to the other. In Stars! this is done manually. Nova may (eventually)
       // implement an automated solution (e.g. using TCP/IP sockets to conect the
       // client and server.)

       #region Nova Resources

       // These config file keys are used to loacte files and folders where Nova stores game data.
       #region Files and Folders
       public const string ComponentFileKey     = "ComponentFile"; // where components.xml is (possibly re-named), installation relative path is '.'. Should only change for modified game play.
       public const string GraphicsFolderKey    = "GraphicsFolder"; // where pictures are stored, installation relative path is './Graphics'. Should only be changed for modding the game interface.
       public const string ClientFolderKey      = "ClientFolder"; // where client side game files are, nominally './GameFiles'. Likely to be different for each active game.
       public const string ServerFolderKey      = "ServerFolder"; // where server side game files are, nominally './GameFiles'. May be a network path.
       public const string RaceFolderKey        = "RaceFolder";  // where player race files are stored, nominally './GameFiles'. Players may save races elswhere to create a reusable library.
       public const string ServerStateKey       = "ServerStateFile"; // the server's saved data from the most recent game, if any. Nominally ./GameFiles/Constole.state. Players may save game files anywhere, e.g. My Document\Saved Games\Nova\A Bare Foot Jay-Walk\
       public const string ClientStateKey       = "ClientStateFile"; // the client's saved data from the most recent game, if any. Nominally ./GameFiles/RaceName.state. Players may save game files anywhere, e.g. My Document\Saved Games\Nova\A Bare Foot Jay-Walk\
       public const string SettingsKey          = "GameSettingsFile"; // where the game settings are stored (on the server, but a copy should be avaialble to the client).
       #endregion Files and Folders

       #region File Extensions
       public const string ClientStateExtension = ".cstate";
       public const string ServerStateExtension = ".sstate";
       public const string OrdersExtension      = ".orders";
       public const string RaceExtension        = ".race";
       public const string IntelExtension       = ".intel";
       public const string SettingsExtension    = ".settings";
       #endregion

       // default folder and file names, used with FileSearcher.GetFolder(). 
       #region Default Folders
       // Follows the above naming conventions
       public const string NovaFolderName           = ".";
       public const string ComponentFolderName      = ".";
       public const string ComponentFileName        = "components.xml";
       public const string ConfigFileName           = "nova.conf";
       public const string GraphicsFolderName       = "Graphics";
       public const string ClientFolderName         = "GameFiles";
       public const string ServerFolderName         = "GameFiles";
       public static readonly string RaceFolderName = "DefaultRaces";
       #endregion Default Folders

       #endregion Nova Resources

       #region Numeric Constants

       // Colonists
       public const int     ColonistsPerKiloton                     = 100;
       public const double  LowStartingPopulationFactor             = 0.7;
       public const double  BaseCrowdingFactor                      = 16.0 / 9.0; // Taken from the Stars technical faq.
       public const int     StartingColonists                       = 25000;
       public const int     StartingColonistsAcceleratedBBS         = 100000;
       public const int     NominalMaximumPlanetaryPopulation       = 1000000; // use Race.MaxPopulation to get the maximum for a particular race.
       public const double  PopulationFactorHyperExpansion          = 0.5;
       public const double  GrowthFactorHyperExpansion              = 2;
       public const double  PopulationFactorJackOfAllTrades         = 1.2;
       public const double  PopulationFactorOnlyBasicRemoteMining   = 1.1;

       // Combat
       public const int MaxWeaponRange  = 7; // Doom/Armegeddon on station.
       public const int MaxDefenses     = 100;
       
       // Environment
       public const double GravityMinimum       = 0; // FIXME (priority 3) - Stars! gravity range is 0.2 - 6.0 with 1.0 in the middle! Will need to revise all current race builds once changed.
       public const double GravityMaximum       = 8;
       public const double RadiationMinimum     = 0;
       public const double RadiationMaximum     = 100;
       public const double TemperatureMinimum   = -200;
       public const double TemperatureMaximum   = 200;

       // Production constants
       public const int ColonistsPerOperableFactoryUnit     = 10000;
       public const int FactoriesPerFactoryProductionUnit   = 10;
       public const int ColonistsPerOperableMiningUnit      = 10000;
       public const int MinesPerMineProductionUnit          = 10;

       // docs/behavior-specs-4/production-queue.md's Overview: "mines, defenses, and terraforming
       // typically need only resources" - defenses previously (and incorrectly) also charged
       // minerals here. The exact no-trait resource total is a genuine open discrepancy in that
       // same spec (the exported client's decompiled cost calculator shows three race-derived
       // branches of 25/44/48 resources, none matching this 15 - but which branch is the
       // no-trait default wasn't determined, so 15 is left as-is rather than guessing).
       public const int DefenseIroniumCost = 0;
       public const int DefenseBoraniumCost = 0;
       public const int DefenseGermaniumCost = 0;
       public const int DefenseEnergyCost = 15;
        
       // Research constants
       public const int DefaultResearchPercentage = 10;

       // Format
       public const int ShipIconNumberingLength = 4;
        
       // Turn data
       public const int StartingYear = 2100;
       public const int DiscardFleetReportAge = 1;
       
       // Limits
       public const int MaxFleetAmount              = 512;
       public const int MaxDesignsAmount            = 16;
       public const int MaxStarbaseDesignsAmount    = 10;
       // docs/behavior-specs-4/client-interface.md confirms the applicable plan limit is exactly
       // 15 ADDITIONAL plans per race on top of the un-removable first plan (a v4 finding - v3 had
       // no concrete number here) - so 16 total, since this constant is checked as a total count
       // (battlePlans.Count &gt;= MaxBattlePlans) that already includes that first plan.
       public const int MaxBattlePlans               = 16;
       // docs/behavior-specs-4/population-growth.md's "Remote mining fleet cap": a single fleet's
       // remote-mining contribution is capped at 4,000 mine-equivalents - stacking more mining
       // capacity into one fleet beyond that produces no extra minerals.
       public const int MaxRemoteMiningEquivalents  = 4000;

       // Defaults
       public const int Nobody = 0x00000000; // As an empire Id cannot be 0, it is used for no owner.
       public const int Everyone = Nobody;
       public const int None = Nobody;
       public const int Unset = -10000;

       // System
       public const double TotalFileWaitTime = 8.0; // (s) Maximum time to wait for a file to become available.
       public const int FileWaitRetryTime = 100; // (ms) Time to wait before trying again to access the file.

       #endregion

       #region Methods

       #region Xml

       private static readonly Random StochasticRoundingRandom = new Random();

       /// <summary>Random proportional rounding: the fractional remainder becomes the
       /// probability of rounding up by one, rather than always being truncated away - the
       /// long-run expected value stays equal to the exact (fractional) input instead of
       /// systematically under-delivering every time this is applied. Confirmed by inspection of
       /// the exported client for both planetary bombing (docs/behavior-specs-4/combat-resolution.md
       /// §9) and mineral mining (docs/behavior-specs-4/population-growth.md) as "the same...
       /// shape as other percentage-based mechanics" in the original game.</summary>
       public static int StochasticRound(double value)
       {
           int whole = (int)value;
           double remainder = value - whole;
           return StochasticRoundingRandom.NextDouble() < remainder ? whole + 1 : whole;
       }

       /// <summary>
       /// Do some common setup work for creating a new xml document.
       /// </summary>
       /// <param name="xmldoc">An XmlDocument variable, may be null, which will be the new document.</param>
       /// <returns>An XmlElement that is the root node of xmldoc.</returns>
       public static XmlElement InitializeXmlDocument(XmlDocument xmldoc)
       {
           if (xmldoc == null)
           {
               xmldoc = new XmlDocument();
           }

           // Write down the XML declaration
           XmlDeclaration xmlDeclaration = xmldoc.CreateXmlDeclaration("1.0", "utf-8", null);

           // Create the root element
           XmlElement xmlRoot = xmldoc.CreateElement("ROOT");
           xmldoc.InsertBefore(xmlDeclaration, xmldoc.DocumentElement);
           xmldoc.AppendChild(xmlRoot);

           return xmlRoot;
       }

       /// <summary>Create an xml node for a save file.</summary>
       /// <param name="xmldoc">The XmlDocument data is being saved to.</param>
       /// <param name="parent">The element this data will be saved under.</param>
       /// <param name="tag">A name that describes the data, usually a variable name.</param>
       /// <param name="value">A String representation of the data, usually variable.ToString.</param>
       public static void SaveData(XmlDocument xmldoc, XmlElement parent, string tag, string value)
       {
           XmlElement xmlelData = xmldoc.CreateElement(tag);
           XmlText xmltxtData = xmldoc.CreateTextNode(value);
           xmlelData.AppendChild(xmltxtData);
           parent.AppendChild(xmlelData);
       }

       public static void SaveData(XmlDocument xmldoc, XmlElement parent, string tag, double value)
       {
           Global.SaveData(xmldoc, parent, tag, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
       }

       public static double ParseDoubleSubnode(XmlNode node, string tag)
       {
           XmlNode subnode = (XmlText)node.SelectSingleNode("descendant::" + tag).FirstChild;
           return double.Parse(subnode.Value, System.Globalization.CultureInfo.InvariantCulture);
       }

       #endregion Xml

       #region Paths

       /// <summary>Derive a relative path from two absolute paths.</summary>
       /// <param name="baseDir">The path from which the relative path will start. Must not be null.</param>
       /// <param name="targetPath">The absolute or relative path to be converted to a relative path. Must not be null.</param>
       public static string EvaluateRelativePath(string baseDir, string targetPath)
       {
           Uri baseUrl = new Uri(AddDirSeparator(baseDir));
           Uri targetUri = new Uri(targetPath);
           Uri relativeUri = baseUrl.MakeRelativeUri(targetUri);
           string unescaped = Uri.UnescapeDataString(relativeUri.OriginalString);
           return unescaped.Replace('/', Path.DirectorySeparatorChar);
       }

       /// <summary>
       /// Add the local directory separator character (\ or /) to a path, if required.
       /// </summary>
       /// <param name="path">A file path. Must not be null.</param>
       /// <returns>Returns path + the (local) directory separator character added to the end, if required.</returns>
       private static string AddDirSeparator(string path)
       {
           if (!path.EndsWith(Path.DirectorySeparatorChar + ""))
           {
               return path + Path.DirectorySeparatorChar;
           }
           return path;
       }

       #endregion Paths

       #endregion Methods
   }
}

