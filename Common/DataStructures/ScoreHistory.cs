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
// along with this program. If not, see <http://www.gnu.org/licenses/>
// ===========================================================================
#endregion

namespace Nova.Common
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// The per-turn score history (behavior-specs-10/save-turn-file-format.md section 3, "a per-
    /// turn score record, retained only for the most recent 100 turns"): one set of score
    /// records per year, the oldest years dropped once more than <see cref="RetainedTurns"/> are
    /// held. Which players may see which rows (save-turn-file-format.md row 17) is not decided
    /// here.
    /// </summary>
    [Serializable]
    public sealed class ScoreHistory
    {
        /// <summary>How many turns of history are kept.</summary>
        public const int RetainedTurns = 100;

        private readonly SortedDictionary<int, List<ScoreRecord>> byYear = new SortedDictionary<int, List<ScoreRecord>>();

        public ScoreHistory()
        {
        }

        /// <summary>The years held, oldest first.</summary>
        public IEnumerable<int> Years => byYear.Keys;

        /// <summary>How many years are held.</summary>
        public int Count => byYear.Count;

        /// <summary>The records of one year, or an empty list.</summary>
        public IReadOnlyList<ScoreRecord> For(int year)
        {
            return byYear.TryGetValue(year, out List<ScoreRecord> records) ? records : new List<ScoreRecord>();
        }

        /// <summary>
        /// Stores a year's scores (replacing any already stored for that year) and drops every
        /// year older than the most recent <see cref="RetainedTurns"/>.
        /// </summary>
        public void Record(int year, IEnumerable<ScoreRecord> scores)
        {
            byYear[year] = scores == null ? new List<ScoreRecord>() : scores.ToList();

            while (byYear.Count > RetainedTurns)
            {
                byYear.Remove(byYear.Keys.First());
            }
        }

        public void Clear()
        {
            byYear.Clear();
        }

        /// <summary>Load from XML.</summary>
        public ScoreHistory(XmlNode node)
        {
            XmlNode yearNode = node.FirstChild;
            while (yearNode != null)
            {
                if (yearNode.Name.Equals("Turn", StringComparison.OrdinalIgnoreCase)
                    && yearNode.Attributes?["Year"] != null
                    && int.TryParse(yearNode.Attributes["Year"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int year))
                {
                    List<ScoreRecord> records = new List<ScoreRecord>();
                    XmlNode recordNode = yearNode.FirstChild;
                    while (recordNode != null)
                    {
                        if (recordNode.Name.Equals("ScoreRecord", StringComparison.OrdinalIgnoreCase))
                        {
                            records.Add(new ScoreRecord(recordNode));
                        }

                        recordNode = recordNode.NextSibling;
                    }

                    byYear[year] = records;
                }

                yearNode = yearNode.NextSibling;
            }

            while (byYear.Count > RetainedTurns)
            {
                byYear.Remove(byYear.Keys.First());
            }
        }

        /// <summary>Save to XML.</summary>
        public XmlElement ToXml(XmlDocument xmldoc)
        {
            XmlElement element = xmldoc.CreateElement("ScoreHistory");
            foreach (KeyValuePair<int, List<ScoreRecord>> entry in byYear)
            {
                XmlElement turn = xmldoc.CreateElement("Turn");
                turn.SetAttribute("Year", entry.Key.ToString(CultureInfo.InvariantCulture));
                foreach (ScoreRecord record in entry.Value)
                {
                    turn.AppendChild(record.ToXml(xmldoc));
                }

                element.AppendChild(turn);
            }

            return element;
        }
    }
}
