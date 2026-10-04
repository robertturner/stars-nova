#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011, 2012 The Stars-Nova Project
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
    using System.IO;
    using System.Xml;

    using Nova.Common.RaceDefinition;

    /// <summary>
    /// This Class defines all the parameters that define the characteristics of a
    /// race. These values are all set in the race designer. This object also manages
    /// the loading and saving of race data to a file.
    /// </summary>
    [Serializable]
    public class Race
    {
        public EnvironmentTolerance GravityTolerance        = new GravityTolerance();
        public EnvironmentTolerance RadiationTolerance      = new RadiationTolerance();
        public EnvironmentTolerance TemperatureTolerance    = new TemperatureTolerance();

        public TechLevel ResearchCosts = new TechLevel(0);

        public RacialTraits Traits = new RacialTraits(); // Collection of all the race's traits, including the primary.

        public string PluralName;
        public string Name;
        public string Password;
        public RaceIcon Icon = new RaceIcon();

        // These parameters affect the production rate of each star (used in the
        // Star class Update method).
        public int FactoryBuildCost;        // defined in the Race Designer as the amount of Resourcesrequired to build one factory
        public int ColonistsPerResource;
        public int FactoryProduction;    // defined in the Race Designer as the amount of resources produced by 10 factories
        public int OperableFactories;

        public int MineBuildCost;
        public int MineProductionRate;   // defined in the Race Designer as the amount of minerals (kT) mined by every 10 mines
        public int OperableMines;

        public string LeftoverPointTarget;

        // Growth goes from 1 to 20 (percent) and is not normalized here.
        public double GrowthRate;

        // required for searializable class
        public Race() 
        { 
        }

        /// <summary>
        /// Constructor for Race. 
        /// Reads all the race data in from an xml formatted save file.
        /// </summary>
        /// <param name="fileName">A nova save file containing a race.</param>
        public Race(string fileName)
        {
            XmlDocument xmldoc = new XmlDocument();
            bool waitForFile = false;
            double waitTime = 0; // seconds
            do
            {
                try
                {
                    using (FileStream fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read))
                    {
                        xmldoc.Load(fileName);
                        XmlNode xmlnode = xmldoc.DocumentElement;
                        LoadRaceFromXml(xmlnode);
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
        /// Load a Race from an already-open stream, rather than a local file path - needed on
        /// platforms where a user-picked file may only be reachable via a content:// handle
        /// (e.g. Android's SAF document picker for a location outside this app's own storage),
        /// which a plain FileStream/File.Exists-based path can't open at all.
        /// </summary>
        /// <param name="stream">An open, readable stream over a race's saved XML data.</param>
        public static Race LoadFromStream(System.IO.Stream stream)
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.Load(stream);

            Race race = new Race();
            race.LoadRaceFromXml(xmldoc.DocumentElement);
            return race;
        }



        /// <summary>
        /// Calculate this race's Habitability for a given star.
        /// </summary>
        /// <param name="star">The star for which the Habitability is being determined.</param>
        /// <returns>The habitability as a fraction: <see cref="HabPercent"/> / 100, so -0.45 to
        /// +1.00 in whole-percent steps.</returns>
        /// <remarks>
        /// The original has no separate fractional evaluator (behavior-specs-10/population-
        /// growth.md section 2, gap-report note): every consumer sees the integer -45..100 result
        /// of FUN_1048_490e, so this is derived from it rather than from the community's
        /// floating-point approximation.
        /// </remarks>
        public double HabValue(Star star)
        {
            return HabPercent(star) / 100.0;
        }

        /// <summary>
        /// This race's habitability percentage for <paramref name="star"/>, exactly as the
        /// original's integer evaluator FUN_1048_490e computes it (behavior-specs-10/
        /// population-growth.md section 2, race-traits.md section 1b), -45 to 100:
        /// - Closeness sum: each immune axis adds 10,000; each in-band axis adds
        ///   (100 - floor(100d/h))^2, d being the planet's distance from the race's centre and h
        ///   the centre-to-edge distance ON THE PLANET'S SIDE of the centre.
        /// - Ideality: starts at 10,000 and, for each in-band axis with 2d > h, is scaled by
        ///   (3h - 2d) / (2h) in integer arithmetic (the "1.5 - distance" edge factor).
        /// - Out-of-band penalty: each axis outside the band adds min(distance outside, 15).
        /// - Result: the negated penalty sum if non-zero, otherwise
        ///   floor(sqrt(closeness / 3) + 0.9) x ideality / 10,000.
        /// </summary>
        public int HabPercent(Star star)
        {
            long closeness = 0;
            long ideality = 10000;
            int penalty = 0;
            int maxMalus = GetMaxMalus();

            AccumulateHabitabilityAxis(GravityTolerance, star.Gravity, maxMalus, ref closeness, ref ideality, ref penalty);
            AccumulateHabitabilityAxis(TemperatureTolerance, star.Temperature, maxMalus, ref closeness, ref ideality, ref penalty);
            AccumulateHabitabilityAxis(RadiationTolerance, star.Radiation, maxMalus, ref closeness, ref ideality, ref penalty);

            if (penalty != 0)
            {
                return -penalty;
            }

            // The original multiplies by the stored double 1/3 (DS 0x1d02) and adds 0.9
            // (DS 0x1d0a) before truncating (FUN_1120_0e40 truncates toward zero).
            long root = (long)(Math.Sqrt(closeness * (1.0 / 3.0)) + 0.9);
            return (int)(root * ideality / 10000);
        }

        /// <summary>One axis's contribution to <see cref="HabPercent"/>'s three running sums.</summary>
        private static void AccumulateHabitabilityAxis(EnvironmentTolerance tolerance, int starValue, int maxMalus, ref long closeness, ref long ideality, ref int penalty)
        {
            if (tolerance.Immune)
            {
                closeness += 10000;
                return;
            }

            int outside = GetMalusForEnvironment(tolerance, starValue, int.MaxValue);
            if (outside > 0)
            {
                penalty += Math.Min(outside, maxMalus);
                return;
            }

            int centre = tolerance.OptimumLevel;
            int d = Math.Abs(starValue - centre);
            int h = starValue < centre ? centre - tolerance.MinimumValue : tolerance.MaximumValue - centre;
            if (h <= 0)
            {
                // A zero-width band side: the planet can only be in band here by sitting exactly on
                // the centre, which is as close as it gets.
                closeness += 10000;
                return;
            }

            long closenessTerm = 100 - ((100L * d) / h);
            closeness += closenessTerm * closenessTerm;

            if (2 * d > h)
            {
                ideality = ideality * ((3 * h) - (2 * d)) / (2 * h);
            }
        }

        /// <summary>
        /// Calculate this race's Habitability for a given star report.
        /// </summary>
        /// <param name="report">The star report for which the Habitability is being determined.</param>
        /// <returns>The normalized habitability of the star (-1 to +1).</returns>
        public double HabitalValue(StarIntel report)
        {
            Star star = new Star();
            star.Gravity = report.Gravity;
            star.Radiation = report.Radiation;
            star.Temperature = report.Temperature;

            return HabValue(star);
        }

        /// <summary>
        /// This race's Habitability for <paramref name="star"/> if it were terraformed to the
        /// maximum extent this race can reach - each of Gravity/Temperature/Radiation nudged
        /// toward this race's own optimum level, up to this race's total terraform allowance
        /// (15%, or 30% with Total Terraforming) measured from the star's ORIGINAL (pre-terraform)
        /// value, mirroring TerraformProductionUnit's own per-1%-step algorithm without actually
        /// stepping through it turn by turn. Same -1..+1 normalized range as <see cref="HabValue"/>.
        /// Only meaningful for a star this race actually owns (terraforming anything else isn't
        /// possible) - callers are expected to gate on that themselves, same as
        /// <see cref="HabValue"/> itself doesn't check ownership.
        /// </summary>
        public double HabitalValueAfterTerraform(Star star)
        {
            int maxPercent = HasTrait("TT") ? 30 : 15;

            Star projected = new Star
            {
                Gravity = TerraformedAxis(star.Gravity, star.OriginalGravity, GravityTolerance.OptimumLevel, maxPercent),
                Temperature = TerraformedAxis(star.Temperature, star.OriginalTemperature, TemperatureTolerance.OptimumLevel, maxPercent),
                Radiation = TerraformedAxis(star.Radiation, star.OriginalRadiation, RadiationTolerance.OptimumLevel, maxPercent),
            };

            return HabValue(projected);
        }

        /// <summary>One environment axis's best reachable value: <paramref name="current"/> moved
        /// toward <paramref name="optimum"/> by whatever terraform allowance (out of
        /// <paramref name="maxPercent"/> total) hasn't already been used getting from
        /// <paramref name="original"/> to <paramref name="current"/>.</summary>
        private static int TerraformedAxis(int current, int original, int optimum, int maxPercent)
        {
            int alreadyUsed = Math.Abs(current - original);
            int remaining = Math.Max(0, maxPercent - alreadyUsed);
            int distanceToOptimum = Math.Abs(optimum - current);
            int step = Math.Min(remaining, distanceToOptimum);
            return current + (Math.Sign(optimum - current) * step);
        }

        public virtual int GetAdvantagePoints()
        {
            RaceAdvantagePointCalculator calculator = new RaceAdvantagePointCalculator();
            return calculator.calculateAdvantagePoints(this);
        }

        public int GetLeftoverAdvantagePoints()
        {
            int advantagePoints = GetAdvantagePoints();
            advantagePoints = Math.Max(0, advantagePoints); // return Advantage Points only if >= 0
            advantagePoints = Math.Min(50, advantagePoints); // return not more than 50
            return advantagePoints;
        }

        /// <summary>docs/behavior-specs-4/population-growth.md confirms the single-axis
        /// habitability penalty is capped at exactly 15, full stop - a hard, unconditional
        /// constant, unlike Total Terraforming's genuinely-doubled 15/30 max terraform-step count
        /// (see TerraformProductionUnit.cs) which this method previously (and incorrectly)
        /// mirrored.</summary>
        private int GetMaxMalus()
        {
            return 15;
        }

        private static int GetMalusForEnvironment(EnvironmentTolerance tolerance, int starValue, int maxMalus)
        {
            if (starValue > tolerance.MaximumValue)
            {
                return Math.Min(maxMalus, starValue - tolerance.MaximumValue);
            }
            else if (starValue < tolerance.MinimumValue)
            {
                return Math.Min(maxMalus, tolerance.MinimumValue - starValue);
            }
            else
            {
                return 0;
            }
        }
        
        /// <summary>
        /// Calculate the number of resources this race requires to construct a factory.
        /// </summary>
        /// <returns>The number of resources this race requires to construct a factory.</returns>
        public Resources GetFactoryResources()
        {
            int factoryBuildCostGerm = HasTrait("CF") ? 3 : 4;
            return new Resources(0, 0, factoryBuildCostGerm, FactoryBuildCost);
        }

        /// <summary>
        /// Calculate the number of resources this race requires to construct a mine.
        /// </summary>
        public Resources GetMineResources()
        {
            return new Resources(0, 0, 0, MineBuildCost);
        }

        /// <summary>
        /// Determine if this race has a given trait.
        /// </summary>
        /// <param name="trait">A string representing a primary or secondary trait. 
        /// See AllTraits.TraitKeys for examples.</param>
        /// <returns>true if this race has the given trait.</returns>
        public bool HasTrait(string trait)
        {
            if (trait == Traits.Primary)
            {
                return true;
            }

            if (Traits == null)
            {
                return false;
            }
            return this.Traits.Contains(trait);
        }

        /// <summary>
        /// The maximum planetary population for this race.
        /// </summary>
        public int MaxPopulation
        {
            get
            {
                int maxPop = Global.NominalMaximumPlanetaryPopulation;
                if (HasTrait("HE"))
                {
                    maxPop = (int)(maxPop * Global.PopulationFactorHyperExpansion);
                }
                if (HasTrait("JOAT"))
                { 
                    maxPop = (int)(maxPop * Global.PopulationFactorJackOfAllTrades);
                }
                if (HasTrait("OBRM"))
                {
                    maxPop = (int)(maxPop * Global.PopulationFactorOnlyBasicRemoteMining);
                }
                return maxPop;
            }
        }

        /// <summary>
        /// Get the starting population for this race.
        /// </summary>
        /// <returns>The starting population (colonists) of the home planet before any Packet
        /// Physics / Interstellar Traveler second-planet split.</returns>
        /// <param name="expertComputerPlayer">
        /// True for a computer player at the Expert skill tier (PlayerSettings.AiSkill 3, "the
        /// computer-player bit together with a skill field above 2"); humans and the Easy,
        /// Standard and Tough tiers pass false.
        /// </param>
        /// <remarks>
        /// behavior-specs-11/new-game-setup.md, "Starting population, exact order", in units of
        /// 100 colonists with every division truncating: (1) base 250 units, or 175 with Low
        /// Starting Population; (2) an Expert-tier computer player gets +10%, the population plus
        /// a tenth of itself truncated, whatever the game options (`:51000`-`51006`); (3)
        /// Accelerated BBS Play multiplies by (g + 5) x 2 and then divides by 10, g being the
        /// growth-rate setting doubled for Hyper Expansion. Step (4), the 2/5 + 4/5 split for a
        /// second home planet, is StarMapinitializer.SplitStartingPopulation.
        /// </remarks>
        public int GetStartingPopulation(bool expertComputerPlayer = false)
        {
            int units = HasTrait("LSP") ? 175 : Global.StartingColonists / 100;

            if (expertComputerPlayer)
            {
                units += units / 10;
            }

            if (GameSettings.Data.AcceleratedStart)
            {
                int g = (int)GrowthRate;
                if (HasTrait("HE"))
                {
                    g *= 2;
                }
                units = units * (g + 5) * 2 / 10;
            }

            return units * 100;
        }

        // Quick and dirty way to clone a race but has the big advantage
        // of picking up XML changes automagically
        public Race Clone()
        {
            XmlDocument doc = new XmlDocument();
            XmlElement ele = ToXml(doc);
            Race ret = new Race();
            ret.LoadRaceFromXml(ele);
            return ret;
        }

        /// <summary>
        /// Save: Serialize this Race to an <see cref="XmlElement"/>.
        /// </summary>
        /// <param name="xmldoc">The parent <see cref="XmlDocument"/>.</param>
        /// <returns>An <see cref="XmlElement"/> representation of the Race.</returns>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement xmlelRace = xmldoc.CreateElement("Race");

            xmlelRace.AppendChild(GravityTolerance.ToXml(xmldoc, "GravityTolerance"));
            xmlelRace.AppendChild(RadiationTolerance.ToXml(xmldoc, "RadiationTolerance"));
            xmlelRace.AppendChild(TemperatureTolerance.ToXml(xmldoc, "TemperatureTolerance"));
            // Tech
            xmlelRace.AppendChild(ResearchCosts.ToXml(xmldoc));

            // Type; // Primary Racial Trait.
            Global.SaveData(xmldoc, xmlelRace, "PRT", Traits.Primary.Code);
            // Traits
            foreach (TraitEntry trait in Traits)
            {
                if (AllTraits.Data.Primary.Contains(trait.Code))
                {
                    continue; // Skip the PRT, just add LRTs here.
                }
                Global.SaveData(xmldoc, xmlelRace, "LRT", trait.Code);
            }

            // MineBuildCost
            Global.SaveData(xmldoc, xmlelRace, "MineBuildCost", MineBuildCost.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // Plural Name
            if (!string.IsNullOrEmpty(PluralName))
            {
                Global.SaveData(xmldoc, xmlelRace, "PluralName", PluralName);
            }
            // Name
            if (!string.IsNullOrEmpty(Name))
            {
                Global.SaveData(xmldoc, xmlelRace, "Name", Name);
            }
            // Password 
            if (!string.IsNullOrEmpty(Password))
            {
                Global.SaveData(xmldoc, xmlelRace, "Password", Password);
            }
            // RaceIconName
            if (!string.IsNullOrEmpty(Icon.Source))
            {
                Global.SaveData(xmldoc, xmlelRace, "RaceIconName", Icon.Source);
            }
            // Factory Build Cost
            Global.SaveData(xmldoc, xmlelRace, "FactoryBuildCost", FactoryBuildCost.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // ColonistsPerResource
            Global.SaveData(xmldoc, xmlelRace, "ColonistsPerResource", ColonistsPerResource.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // FactoryProduction
            Global.SaveData(xmldoc, xmlelRace, "FactoryProduction", FactoryProduction.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // OperableFactories
            Global.SaveData(xmldoc, xmlelRace, "OperableFactories", OperableFactories.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // MineProductionRate
            Global.SaveData(xmldoc, xmlelRace, "MineProductionRate", MineProductionRate.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // OperableMines
            Global.SaveData(xmldoc, xmlelRace, "OperableMines", OperableMines.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // MaxPopulation
            Global.SaveData(xmldoc, xmlelRace, "MaxPopulation", MaxPopulation.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // GrowthRate
            Global.SaveData(xmldoc, xmlelRace, "GrowthRate", GrowthRate.ToString(System.Globalization.CultureInfo.InvariantCulture));

            // LeftoverPointTarget
            if ("".Equals(LeftoverPointTarget) || LeftoverPointTarget == null)
            {
                LeftoverPointTarget = "Surface minerals";
            }
            Global.SaveData(xmldoc, xmlelRace, "LeftoverPoints", LeftoverPointTarget.ToString(System.Globalization.CultureInfo.InvariantCulture));

            return xmlelRace;
        }

        /// <summary>
        /// Load a Race from an xml document.
        /// </summary>
        /// <param name="xmlnode">An XmlNode, see Race constructor for generation.</param>
        public void LoadRaceFromXml(XmlNode xmlnode)
        {
            while (xmlnode != null)
            {
                try
                {
                    switch (xmlnode.Name.ToLowerInvariant())
                    {
                        case "root":
                            xmlnode = xmlnode.FirstChild;
                            continue;
                        case "race":
                            xmlnode = xmlnode.FirstChild;
                            continue;
                        case "gravitytolerance":
                            GravityTolerance.FromXml(xmlnode);
                            break;
                        case "radiationtolerance":
                            RadiationTolerance.FromXml(xmlnode);
                            break;
                        case "temperaturetolerance":
                            TemperatureTolerance.FromXml(xmlnode);
                            break;
                        case "tech":
                            ResearchCosts = new TechLevel(xmlnode);
                            break;

                        case "lrt":
                            Traits.Add(xmlnode.FirstChild.Value);
                            break;

                        case "minebuildcost":
                            MineBuildCost = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "prt":
                            Traits.SetPrimary(xmlnode.FirstChild.Value);
                            break;
                        case "pluralname":
                            if (xmlnode.FirstChild != null)
                            {
                                PluralName = xmlnode.FirstChild.Value;
                            }
                            break;
                        case "name":
                            if (xmlnode.FirstChild != null)
                            {
                                Name = xmlnode.FirstChild.Value;
                            }
                            break;
                        case "password":
                            if (xmlnode.FirstChild != null)
                            {
                                Password = xmlnode.FirstChild.Value;
                            }
                            break;

                        // TODO (priority 5) - load the RaceIcon
                        case "raceiconname":
                            if (xmlnode.FirstChild != null)
                            {
                                Icon.Source = xmlnode.FirstChild.Value;
                            }
                            break;

                        case "factorybuildcost":
                            FactoryBuildCost = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "colonistsperresource":
                            ColonistsPerResource = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "factoryproduction":
                            FactoryProduction = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "operablefactories":
                            OperableFactories = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "mineproductionrate":
                            MineProductionRate = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "operablemines":
                            OperableMines = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "growthrate":
                            GrowthRate = int.Parse(xmlnode.FirstChild.Value, System.Globalization.CultureInfo.InvariantCulture);
                            break;
                        case "leftoverpoints":
                            this.LeftoverPointTarget = xmlnode.FirstChild.Value;
                            break;

                        default: break;
                    }
                }
                catch (Exception e)
                {
                    // Non-fatal - see Waypoint.cs's own comment for the live-reproduced crash
                    // this "one bad field exits the whole app" pattern caused.
                    Report.Error(e.Message + "\n Details: \n" + e);
                }

                xmlnode = xmlnode.NextSibling;
            }

            // if an old version of the race file is loaded and there is no leftover point target then select standard leftover point target.
            if ("".Equals(LeftoverPointTarget) || LeftoverPointTarget == null)
            {
                this.LeftoverPointTarget = "Surface minerals";
            }
        }

        public int LowerHab(int habIndex)
        {
            switch (habIndex)
            {
                case 0:
                    return GravityTolerance.MinimumValue;
                case 1:
                    return TemperatureTolerance.MinimumValue;
                case 2:
                    return RadiationTolerance.MinimumValue;
            }
            return 0;
        }

        public int UpperHab(int habIndex)
        {
            switch (habIndex)
            {
                case 0:
                    return GravityTolerance.MaximumValue;
                case 1:
                    return TemperatureTolerance.MaximumValue;
                case 2:
                    return RadiationTolerance.MaximumValue;
            }
            return 0;
        }

        public int CenterHab(int habIndex)
        {
            switch (habIndex)
            {
                case 0:
                    return GravityTolerance.OptimumLevel;
                case 1:
                    return TemperatureTolerance.OptimumLevel;
                case 2:
                    return RadiationTolerance.OptimumLevel;
            }
            return 0;
        }

        public bool IsImmune(int habIndex)
        {
            switch (habIndex)
            {
                case 0:
                    return GravityTolerance.Immune;
                case 1:
                    return TemperatureTolerance.Immune;
                case 2:
                    return RadiationTolerance.Immune;
            }
            return false;
        }
    }
}
