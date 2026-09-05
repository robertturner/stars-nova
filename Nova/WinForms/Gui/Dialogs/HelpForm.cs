#region Copyright Notice
// ============================================================================
// Copyright (C) 2009, 2010 The Stars-Nova Project
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

#region Module Description
// ===========================================================================
// In-game manual viewer. Displays the original Stars! Player's Guide, as
// converted to HTML by the Stars!AutoHost wiki community from the game's own
// stars.hlp. This content ships under HelpContent/ - see NOTICE-HelpContent.txt
// there for provenance and licensing; it is the original game's own copyrighted
// manual text, distinct from this project's own clean-room GPL/CC-BY-SA code
// and documentation.
// ===========================================================================
#endregion

namespace Nova.WinForms.Gui
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Windows.Forms;

    public partial class HelpForm : Form
    {
        private sealed class Topic
        {
            public readonly int Number;
            public readonly string Title;

            public Topic(int number, string title)
            {
                Number = number;
                Title = title;
            }

            public override string ToString()
            {
                return Title;
            }
        }

        private static readonly string HelpRoot = Path.Combine(Application.StartupPath, "HelpContent", "hlp");

        private readonly List<Topic> allTopics = new List<Topic>();

        /// <Summary>
        /// Opens the manual at its default "Contents" topic.
        /// </Summary>
        public HelpForm() : this(1)
        {
        }

        /// <Summary>
        /// Opens the manual at a specific topic number (matching HelpContent/topics.tsv
        /// and the corresponding HelpContent/hlp/index_web/{number}.html file).
        /// </Summary>
        public HelpForm(int openAtTopicNumber)
        {
            InitializeComponent();

            LoadTopicList();
            PopulateTopicList(null);
            SelectTopic(openAtTopicNumber);
        }

        private void LoadTopicList()
        {
            string manifestPath = Path.Combine(Application.StartupPath, "HelpContent", "topics.tsv");

            if (!File.Exists(manifestPath))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(manifestPath))
            {
                int tab = line.IndexOf('\t');
                if (tab < 0)
                {
                    continue;
                }

                int number;
                if (int.TryParse(line.Substring(0, tab), out number))
                {
                    allTopics.Add(new Topic(number, line.Substring(tab + 1)));
                }
            }
        }

        private void PopulateTopicList(string filter)
        {
            IEnumerable<Topic> topics = allTopics;

            if (!string.IsNullOrEmpty(filter))
            {
                topics = topics.Where(t => t.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            topicList.BeginUpdate();
            topicList.Items.Clear();
            foreach (Topic topic in topics.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase))
            {
                topicList.Items.Add(topic);
            }
            topicList.EndUpdate();
        }

        private void SelectTopic(int topicNumber)
        {
            Topic topic = allTopics.FirstOrDefault(t => t.Number == topicNumber);
            if (topic != null)
            {
                int index = topicList.Items.IndexOf(topic);
                if (index < 0)
                {
                    // Not visible under the current filter - show the full list instead.
                    searchBox.Text = string.Empty;
                    index = topicList.Items.IndexOf(topic);
                }

                if (index >= 0)
                {
                    topicList.SelectedIndex = index;
                    return;
                }
            }

            NavigateTo(topicNumber);
        }

        private void NavigateTo(int topicNumber)
        {
            string path = Path.Combine(HelpRoot, "index_web", topicNumber + ".html");
            if (File.Exists(path))
            {
                contentBrowser.Navigate(new Uri(path));
            }
        }

        private void TopicList_SelectedIndexChanged(object sender, EventArgs e)
        {
            Topic topic = topicList.SelectedItem as Topic;
            if (topic != null)
            {
                NavigateTo(topic.Number);
            }
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            PopulateTopicList(searchBox.Text);
        }
    }
}
