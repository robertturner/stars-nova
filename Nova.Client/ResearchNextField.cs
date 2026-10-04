#region Copyright Notice
// ============================================================================
// Copyright (C) 2026 The Stars-Nova Project
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

namespace Nova.Client
{
    using System.Collections.Generic;
    using System.Linq;

    using Nova.Common;

    /// <summary>
    /// The Research panel's "next field" combo (behavior-specs-10/research-tech-tree.md section 4:
    /// a second, independent setting whose values are the six named fields plus a "lowest field"
    /// option; EmpireData.ResearchNextField / ResearchCommand.NextField). The engine's own default,
    /// "stay on the same field" (Research.NextFieldSame), is offered first. Fields are listed in
    /// the original's order (Research.OriginalFieldOrder); every field can be picked by hand,
    /// and the automatic lowest-field choice considers all six fields for every race.
    /// </summary>
    public static class ResearchNextField
    {
        public const string SameLabel = "Same field";

        public const string LowestLabel = "Lowest field";

        /// <summary>The combo's (label, setting value) pairs, in display order.</summary>
        public static List<KeyValuePair<string, int>> Choices()
        {
            List<KeyValuePair<string, int>> choices = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>(SameLabel, Research.NextFieldSame),
            };

            choices.AddRange(Research.OriginalFieldOrder.Select(field => new KeyValuePair<string, int>(field.ToString(), (int)field)));
            choices.Add(new KeyValuePair<string, int>(LowestLabel, Research.NextFieldLowest));
            return choices;
        }

        /// <summary>The combo index of a stored setting (unknown values show as Same).</summary>
        public static int IndexOf(int setting)
        {
            int index = Choices().FindIndex(choice => choice.Value == setting);
            return index < 0 ? 0 : index;
        }
    }
}
