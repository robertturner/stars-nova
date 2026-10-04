#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
//
// This file is part of Stars! Nova.
// See <http://sourceforge.net/projects/stars-nova/>.
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
// along with this program. If not, see <http://www.gnu.org/licenses/>.
// ===========================================================================
#endregion

namespace Nova.Ai
{
    using Nova.Common;

    /// <summary>
    /// The AI's research share p (behavior-specs-11/ai-opponent-behavior.md §6, `FUN_1090_2900`
    /// and `FUN_1090_355c`): the percentage of a planet's annual resource output that goes to
    /// research before production, deducted from the "projected resources" every AI test reads.
    /// </summary>
    public static class AiResearchShare
    {
        /// <summary>
        /// The percentage p for a category and turn counter, or 0 once every one of the AI's six
        /// tech levels is 24 or more (§6): personality 0 uses 0% before turn 10 and 15% from then
        /// on, personality 1 15%, personality 2 0% before turn 10 then 20%, personality 3 and
        /// category 7 0% before turn 20 then 15%, personality 4 17% and personality 5 15%.
        /// </summary>
        public static int PercentFor(int category, int yearCounter, TechLevel techLevels)
        {
            if (AllTechLevelsAtLeast24(techLevels))
            {
                return 0;
            }

            switch (category)
            {
                case AiCategory.Robotoids:
                    return yearCounter < 10 ? 0 : 15;
                case AiCategory.Turindrones:
                    return 15;
                case AiCategory.Automitrons:
                    return yearCounter < 10 ? 0 : 20;
                case AiCategory.Rototills:
                case AiCategory.EconomyOnly:
                    return yearCounter < 20 ? 0 : 15;
                case AiCategory.Cybertrons:
                    return 17;
                case AiCategory.Macinti:
                    return 15;
                default:
                    return 0;
            }
        }

        /// <summary>R − (p × R ÷ 100), the division truncating toward zero.</summary>
        public static int AfterShare(int resourceOutput, int percent)
        {
            return resourceOutput - (percent * resourceOutput / 100);
        }

        private static bool AllTechLevelsAtLeast24(TechLevel levels)
        {
            if (levels == null)
            {
                return false;
            }

            foreach (TechLevel.ResearchField field in System.Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (levels[field] < 24)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
