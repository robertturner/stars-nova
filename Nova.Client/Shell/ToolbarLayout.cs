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

namespace Nova.Client.Shell
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One toolbar action: its id (the persisted token), short glyph and tooltip.</summary>
    public sealed class ToolbarButtonInfo
    {
        public ToolbarButtonInfo(string id, string glyph, string tooltip)
        {
            Id = id;
            Glyph = glyph;
            Tooltip = tooltip;
        }

        public string Id { get; }

        /// <summary>Short text drawn on the button (the original's icon art is not available).</summary>
        public string Glyph { get; }

        public string Tooltip { get; }
    }

    /// <summary>
    /// The toolbar's button set and order (behavior-specs-10/client-interface.md "Navigation
    /// controls": a vertically arranged, finite action strip with hover/press feedback, disabled
    /// actions and tooltips, "at least one action with a finite preset-value drop-down" (the
    /// window-layout preset); "the toolbar's button set and ordering are user-configurable and
    /// persisted across sessions via a saved preference string"; View > Toolbar (179) toggles it;
    /// client-ui-dialog-catalog.md "Toolbar, tooltip, and contextual popup").
    /// The preference string is the visible button ids, in order, separated by commas.
    /// SPEC GAP: which commands the toolbar offers, their default order, their icons and tooltip
    /// texts, how the user customises the set, and whether the toolbar starts shown are not given.
    /// Stand-ins: <see cref="Catalog"/> (one button per frequent menu command, glyph + menu-caption
    /// tooltip), all shown by default in catalog order, customised from a right-click menu, shown
    /// by default (<see cref="DefaultVisible"/>).
    /// </summary>
    public sealed class ToolbarLayout
    {
        public const string PreferenceKey = "ToolbarButtons";

        public const string VisiblePreferenceKey = "ToolbarVisible";

        /// <summary>SPEC GAP seam: whether the toolbar starts shown.</summary>
        public const bool DefaultVisible = true;

        /// <summary>The window-layout preset drop-down's id (spec: the toolbar's preset action).</summary>
        public const string LayoutPresetId = "Layout";

        /// <summary>SPEC GAP seam: every button the toolbar can show, in default order.</summary>
        public static readonly IReadOnlyList<ToolbarButtonInfo> Catalog = new List<ToolbarButtonInfo>
        {
            new ToolbarButtonInfo("Save", "Sav", "Save (Ctrl+S)"),
            new ToolbarButtonInfo("Generate", "Gen", "Generate (F9)"),
            new ToolbarButtonInfo("Find", "Find", "Find (Ctrl+F)"),
            new ToolbarButtonInfo("ZoomIn", "Z+", "Zoom in"),
            new ToolbarButtonInfo("ZoomOut", "Z-", "Zoom out"),
            new ToolbarButtonInfo("ShipDesign", "Des", "Ship Design (F4)"),
            new ToolbarButtonInfo("Research", "Res", "Research (F5)"),
            new ToolbarButtonInfo("BattlePlans", "BP", "Battle Plans (F6)"),
            new ToolbarButtonInfo("PlayerRelations", "Rel", "Player Relations (F7)"),
            new ToolbarButtonInfo("PlanetReport", "Pla", "Planet report (F3)"),
            new ToolbarButtonInfo("FleetReport", "Flt", "Fleet report"),
            new ToolbarButtonInfo("ScoreReport", "Scr", "Score (F10)"),
            new ToolbarButtonInfo("TechnologyBrowser", "Tech", "Technology Browser (F2)"),
            new ToolbarButtonInfo(LayoutPresetId, "Lay", "Window layout"),
        };

        private readonly List<string> ids;

        private ToolbarLayout(IEnumerable<string> ids)
        {
            this.ids = ids.ToList();
        }

        /// <summary>The shown buttons' ids, top to bottom.</summary>
        public IReadOnlyList<string> Ids => ids;

        public static ToolbarLayout Default()
        {
            return new ToolbarLayout(Catalog.Select(button => button.Id));
        }

        /// <summary>
        /// Reads the stored string. Unknown ids and repeats are dropped. An absent value gives the
        /// default set; an empty (but present) value means the user removed every button.
        /// </summary>
        public static ToolbarLayout Parse(string stored)
        {
            if (stored == null)
            {
                return Default();
            }

            List<string> result = new List<string>();
            foreach (string part in stored.Split(','))
            {
                string id = part.Trim();
                if (Find(id) != null && !result.Contains(id))
                {
                    result.Add(id);
                }
            }

            return new ToolbarLayout(result);
        }

        public string Format()
        {
            return string.Join(",", ids);
        }

        public static ToolbarButtonInfo Find(string id)
        {
            return Catalog.FirstOrDefault(button => button.Id == id);
        }

        public bool Contains(string id) => ids.Contains(id);

        /// <summary>Catalog buttons not currently shown, in catalog order.</summary>
        public IReadOnlyList<ToolbarButtonInfo> Hidden =>
            Catalog.Where(button => !ids.Contains(button.Id)).ToList();

        public bool MoveUp(string id)
        {
            int index = ids.IndexOf(id);
            if (index <= 0)
            {
                return false;
            }

            ids.RemoveAt(index);
            ids.Insert(index - 1, id);
            return true;
        }

        public bool MoveDown(string id)
        {
            int index = ids.IndexOf(id);
            if (index < 0 || index >= ids.Count - 1)
            {
                return false;
            }

            ids.RemoveAt(index);
            ids.Insert(index + 1, id);
            return true;
        }

        public bool Remove(string id) => ids.Remove(id);

        /// <summary>Shows a hidden catalog button at the bottom of the strip.</summary>
        public bool Add(string id)
        {
            if (Find(id) == null || ids.Contains(id))
            {
                return false;
            }

            ids.Add(id);
            return true;
        }

        public void Reset()
        {
            ids.Clear();
            ids.AddRange(Catalog.Select(button => button.Id));
        }

        /// <summary>The stored visibility flag ("1"/"0"); absent or unreadable gives the default.</summary>
        public static bool ParseVisible(string stored)
        {
            if (stored == "1")
            {
                return true;
            }

            if (stored == "0")
            {
                return false;
            }

            return DefaultVisible;
        }

        public static string FormatVisible(bool visible) => visible ? "1" : "0";
    }
}
