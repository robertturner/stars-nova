using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Dock.Model.Mvvm.Controls;
using Nova.Common;

namespace Nova.Avalonia.ViewModels.Panels;

/// <summary>
/// In-game manual viewer - ports HelpForm.cs (the original Stars! Player's Guide, converted to
/// HTML by the Stars!AutoHost wiki community from the game's own stars.hlp; see HelpContent/
/// NOTICE-HelpContent.txt for provenance/licensing - copyrighted original-game text, distinct
/// from this project's own clean-room code/docs).
///
/// Unlike WinForms (a System.Windows.Forms.WebBrowser rendering the HTML directly), Avalonia has
/// no built-in HTML renderer and this port intentionally avoids pulling in a third-party WebView
/// package for one reference dialog - so each topic's HTML is stripped down to plain text
/// (StripHtml) rather than rendered with its original formatting/inter-topic links. This loses
/// bold/color styling and clickable cross-references, but keeps the actual manual content,
/// the topic list, and search - the three things a player actually needs from this dialog.
/// </summary>
public class HelpViewModel : Tool
{
    // FileSearcher.GetNovaRoot() rather than AppContext.BaseDirectory directly - the two agree on
    // every desktop host (Common.dll, which defines GetNovaRoot(), sits in the same output folder
    // as this app's own assemblies and the HelpContent/ copy), but only GetNovaRoot() also honors
    // PlatformHooks.NovaRootOverride, which the Android head needs (see that hook's own comment).
    private static readonly string HelpRoot = Path.Combine(FileSearcher.GetNovaRoot(), "HelpContent", "hlp");
    private static readonly string ManifestPath = Path.Combine(FileSearcher.GetNovaRoot(), "HelpContent", "topics.tsv");

    private readonly List<HelpTopicViewModel> allTopics = new();

    private IReadOnlyList<HelpTopicViewModel> topics = Array.Empty<HelpTopicViewModel>();

    public IReadOnlyList<HelpTopicViewModel> Topics
    {
        get => topics;
        private set => SetProperty(ref topics, value);
    }

    private HelpTopicViewModel? selectedTopic;

    public HelpTopicViewModel? SelectedTopic
    {
        get => selectedTopic;
        set
        {
            if (SetProperty(ref selectedTopic, value) && value != null)
            {
                NavigateTo(value.Number);
            }
        }
    }

    private string searchText = "";

    public string SearchText
    {
        get => searchText;
        set
        {
            if (SetProperty(ref searchText, value))
            {
                PopulateTopicList(value);
            }
        }
    }

    private string contentText = "";

    public string ContentText
    {
        get => contentText;
        private set => SetProperty(ref contentText, value);
    }

    public HelpViewModel(string id, string title)
    {
        Id = id;
        Title = title;

        LoadTopicList();
        PopulateTopicList(null);
        SelectTopic(1);
    }

    private void LoadTopicList()
    {
        if (!File.Exists(ManifestPath))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(ManifestPath))
        {
            int tab = line.IndexOf('\t');
            if (tab < 0)
            {
                continue;
            }

            if (int.TryParse(line.Substring(0, tab), out int number))
            {
                allTopics.Add(new HelpTopicViewModel(number, line.Substring(tab + 1)));
            }
        }
    }

    private void PopulateTopicList(string? filter)
    {
        IEnumerable<HelpTopicViewModel> filtered = allTopics;

        if (!string.IsNullOrEmpty(filter))
        {
            filtered = filtered.Where(t => t.Title.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        Topics = filtered.OrderBy(t => t.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void SelectTopic(int topicNumber)
    {
        HelpTopicViewModel? topic = allTopics.FirstOrDefault(t => t.Number == topicNumber);
        if (topic != null)
        {
            int index = Topics.ToList().IndexOf(topic);
            if (index < 0)
            {
                // Not visible under the current filter - show the full list instead.
                SearchText = "";
            }

            SelectedTopic = topic;
            return;
        }

        NavigateTo(topicNumber);
    }

    private void NavigateTo(int topicNumber)
    {
        string path = Path.Combine(HelpRoot, "index_web", topicNumber + ".html");
        ContentText = File.Exists(path) ? StripHtml(File.ReadAllText(path)) : "";
    }

    /// <summary>
    /// Reduces one topic's HTML page to readable plain text: drops the whole &lt;head&gt;
    /// (embedded CSS), turns block-level tags into line breaks so paragraphs/list items/table
    /// rows stay on their own lines, strips every remaining tag, then decodes the handful of
    /// entities this content actually uses. Not a general-purpose HTML-to-text converter - just
    /// enough for this one known, machine-generated content set.
    /// </summary>
    private static string StripHtml(string html)
    {
        string body = Regex.Replace(html, "<head[^>]*>.*?</head>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        body = Regex.Replace(body, "<(br|p|div|tr|li|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
        body = Regex.Replace(body, "<[^>]+>", "");

        // Handles every standard named/numeric entity (&diams;, &mdash;, &#39;, etc.) - this
        // content uses more than the handful worth hardcoding.
        body = System.Net.WebUtility.HtmlDecode(body);

        var lines = body.Split('\n').Select(l => l.TrimEnd());
        var result = new StringBuilder();
        bool lastWasBlank = false;
        foreach (string line in lines)
        {
            bool isBlank = string.IsNullOrWhiteSpace(line);
            if (isBlank && lastWasBlank)
            {
                continue;
            }

            result.AppendLine(line);
            lastWasBlank = isBlank;
        }

        return result.ToString().Trim();
    }
}
