#region Copyright Notice
// ============================================================================
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
    using System.Collections;
    using System.Collections.Generic;

    /// <summary>
    /// This module returns details of a planet's Defenses
    /// TODO (priority 5) - Transfer this to the Defense component property.
    /// </summary>
    public static class Defenses
    {
        public static Dictionary<string, double> DefenseTypes = new Dictionary<string, double>();
        public static double PopulationCoverage;
        public static double BuildingCoverage;
        public static double InvasionCoverage;
        public static double SmartBombCoverage;
        public static int SummaryCoverage;

        /// <summary>
        /// Constructor.
        /// </summary>
        static Defenses()
        {
            DefenseTypes["SDI"]     = 0.0099;   // 0.99%
            DefenseTypes["Missile"] = 0.0199;   // 1.99%
            DefenseTypes["Laser"]   = 0.0239;   // 2.39%
            DefenseTypes["Planet"]  = 0.0299;   // 2.99%
            DefenseTypes["Neutron"] = 0.0379;   // 3.79%
        }

        /// <summary>
        /// The planet's coverage against invading troops (normalized, 1.0 = 100%): the same
        /// value <see cref="ComputeDefenseCoverage"/> leaves in <see cref="InvasionCoverage"/>,
        /// but returned rather than written to the shared statics, so turn generation does not
        /// race with a UI thread computing another planet's coverage in the same process.
        /// </summary>
        public static double InvasionCoverageOf(Star star)
        {
            if (star.DefenseType == "None")
            {
                return 0;
            }

            double baseLevel = DefenseTypes[star.DefenseType];
            return (1.0 - Math.Pow(1.0 - baseLevel, star.Defenses)) * 0.75;
        }

        /// <summary>
        /// Determine the Defenses of a planet. Note: results are normalized so that
        /// 100% = 1.0.
        /// </summary>
        /// <param name="star">The <see cref="Star"/> to calculate defenses for.</param>
        public static void ComputeDefenseCoverage(Star star)
        {
            if (star.DefenseType == "None")
            {
                PopulationCoverage = 0;
                BuildingCoverage   = 0;
                InvasionCoverage   = 0;
                SmartBombCoverage  = 0;
                SummaryCoverage    = 0;
                return;
            }

            double baseLevel   = DefenseTypes[star.DefenseType];

            PopulationCoverage = 1.0 - Math.Pow(1.0 - baseLevel, star.Defenses);
            BuildingCoverage   = PopulationCoverage * 0.5;
            InvasionCoverage   = PopulationCoverage * 0.75;

            // Coverage against smart bombs is calculated slightly differently

            baseLevel *= 0.5;
            SmartBombCoverage = 1.0 - Math.Pow(1.0 - baseLevel, star.Defenses);

            double summary = ((BuildingCoverage + PopulationCoverage
                              + InvasionCoverage) / 3) * 100;

            SummaryCoverage = (int)summary;
        }
    }
}

