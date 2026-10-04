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
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;

    /// <summary>The three rename prompts (client-ui-dialog-catalog.md "Rename surfaces").</summary>
    public enum RenameSurface
    {
        /// <summary>RENAMEDLG: the fleet panel's rename control.</summary>
        Fleet,

        /// <summary>RENAMEZIPDLG while the four-slot production-template manager is open.</summary>
        ProductionTemplate,

        /// <summary>RENAMEZIPDLG while the zip-order manager is open.</summary>
        ZipOrder,

        /// <summary>NEWPLANNAMEDLG: the Battle Plans dialog.</summary>
        BattlePlan,
    }

    /// <summary>
    /// The client's rename rules (behavior-specs-11/client-ui-dialog-catalog.md "Rename surfaces",
    /// traced from segment 17). All three prompts use one template (a "New Name" label, edit
    /// control 268, OK, Cancel, Help); only the battle-plan prompt keeps the template's own
    /// caption. The spec's rules, which replace the earlier "the ordinary surface filters and
    /// rewrites" reading:
    /// <list type="bullet">
    /// <item>No prompt removes or rewrites any character: spaces, punctuation and ampersands are
    /// all kept. The only edit-time restriction is the length limit, and for a fleet also a
    /// rendered-width limit (characters are cut from the end until the text is no wider than 160
    /// pixels in the small panel font).</item>
    /// <item>Length limits: fleet 31 and at most 160 px; production-template / zip-order name 12
    /// (the edit limit is 12; OK reads at most 13); battle plan 31.</item>
    /// <item>Accept rules: a fleet's empty name removes the custom name (the fleet falls back to
    /// its default generated name); a production-template / zip-order empty name becomes the
    /// slot's default "custom number N" (0-based index); a battle-plan name is copied unchanged,
    /// with no width cut.</item>
    /// <item>The production-template manager's slot 0 cannot be renamed; the zip-order manager
    /// has no such exception.</item>
    /// <item>A slot's button caption doubles every ampersand so it shows literally.</item>
    /// </list>
    /// SEAMS for spec-silent details (reported):
    /// <list type="bullet">
    /// <item><see cref="FleetWidthLimit"/> is 160 (spec), but the exact font metrics are unknown,
    /// so the caller supplies the pixel measurement (<see cref="LimitFleetForEdit"/>); a caller
    /// with no metrics may pass null and keep only the 31-character limit (the spec's own
    /// reimplementation note allows this).</item>
    /// <item>Dynamic strings 732 / 1218 / 1219 / 1215 are not in the surviving string data; the
    /// captions and the "custom number N" wording are neutral placeholders
    /// (<see cref="Caption"/>, <see cref="DefaultTemplateName"/>).</item>
    /// <item>The spec mentions no duplicate-name rule for any surface. The port's duplicate and
    /// blank checks are kept apart in <see cref="ValidatePortSafety"/> and are NOT part of the
    /// spec's accept rules.</item>
    /// </list>
    /// </summary>
    public static class RenameRules
    {
        /// <summary>Fleet name limit: 31 characters (and at most <see cref="FleetWidthLimit"/> px).</summary>
        public const int FleetMaxLength = 31;

        /// <summary>Production-template / zip-order name limit: 12 characters (OK reads 13).</summary>
        public const int TemplateMaxLength = 12;

        /// <summary>Battle-plan name limit: 31 characters.</summary>
        public const int BattlePlanMaxLength = 31;

        /// <summary>The fleet prompt's rendered-width limit, in the small panel font.</summary>
        public const int FleetWidthLimit = 160;

        /// <summary>The dynamic-string-1215 placeholder for an unnamed slot ("custom number N").</summary>
        public const string TemplateDefaultPrefix = "custom number ";

        /// <summary>The largest name the surface accepts (0 = no limit).</summary>
        public static int MaxLength(RenameSurface surface)
        {
            switch (surface)
            {
                case RenameSurface.Fleet:
                    return FleetMaxLength;
                case RenameSurface.BattlePlan:
                    return BattlePlanMaxLength;
                case RenameSurface.ProductionTemplate:
                case RenameSurface.ZipOrder:
                    return TemplateMaxLength;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// The edit-time restriction for a surface: the length limit only. No character is removed
        /// or rewritten.
        /// </summary>
        public static string LimitForEdit(string text, RenameSurface surface)
        {
            return Truncate(text ?? string.Empty, MaxLength(surface));
        }

        /// <summary>
        /// The fleet surface's edit-time restriction: both the 31-character limit and the 160-pixel
        /// width limit (characters cut from the end). <paramref name="measureWidth"/> returns the
        /// rendered width of a string in the panel font; when it is null only the 31-character limit
        /// applies (the spec's documented reimplementation fallback).
        /// </summary>
        public static string LimitFleetForEdit(string text, Func<string, int> measureWidth)
        {
            string result = Truncate(text ?? string.Empty, FleetMaxLength);
            if (measureWidth != null)
            {
                while (result.Length > 0 && measureWidth(result) > FleetWidthLimit)
                {
                    result = result.Substring(0, result.Length - 1);
                }
            }

            return result;
        }

        /// <summary>
        /// The value a fleet rename commits. The spec keeps every character; an empty name removes
        /// the fleet's custom name (so it shows its default generated name again).
        /// </summary>
        public static string AcceptFleetName(string text)
        {
            return text ?? string.Empty;
        }

        /// <summary>True when the committed fleet name clears the custom name (empty).</summary>
        public static bool FleetNameClearsCustom(string text)
        {
            return string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// The value a production-template / zip-order rename commits. An empty text becomes the
        /// slot's default "custom number N" (N is the 0-based slot index).
        /// </summary>
        public static string AcceptTemplateName(string text, int slotIndex)
        {
            return string.IsNullOrEmpty(text) ? DefaultTemplateName(slotIndex) : text;
        }

        /// <summary>The default placeholder name of a template / zip-order slot (string 1215 seam).</summary>
        public static string DefaultTemplateName(int slotIndex)
        {
            return TemplateDefaultPrefix + slotIndex.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>The value a battle-plan rename commits: the text, copied unchanged (no width cut).</summary>
        public static string AcceptBattlePlanName(string text)
        {
            return text ?? string.Empty;
        }

        /// <summary>The production-template manager protects slot 0; the zip-order manager does not.</summary>
        public static bool CanRename(RenameSurface surface, int slotIndex)
        {
            return surface != RenameSurface.ProductionTemplate || slotIndex != 0;
        }

        /// <summary>A slot's button caption: every ampersand is doubled so it shows literally.</summary>
        public static string ButtonCaption(string name)
        {
            return (name ?? string.Empty).Replace("&", "&&");
        }

        /// <summary>The prompt caption (dynamic strings 732 / 1218 / 1219 / template seam).</summary>
        public static string Caption(RenameSurface surface)
        {
            switch (surface)
            {
                case RenameSurface.Fleet:
                    return "Rename Fleet";
                case RenameSurface.ProductionTemplate:
                    return "Rename Production Template";
                case RenameSurface.ZipOrder:
                    return "Rename Zip Order";
                case RenameSurface.BattlePlan:
                    return "Rename Battle Plan";
                default:
                    return "Rename";
            }
        }

        /// <summary>
        /// PORT SAFETY RULE, not original behavior (the spec states no duplicate or blank rule for
        /// any rename surface): an all-empty result is refused and, when the caller passes the
        /// sibling names, a duplicate is refused. Kept so the port does not create unnamed or
        /// colliding records; callers that follow the spec's own accept rules should not call this.
        /// </summary>
        public static string ValidatePortSafety(string text, IEnumerable<string> siblingNames = null)
        {
            string name = text ?? string.Empty;
            if (name.Trim().Length == 0)
            {
                return "A name is required.";
            }

            if (siblingNames != null && siblingNames.Any(sibling => string.Equals(sibling, name, StringComparison.Ordinal)))
            {
                return "That name is already in use.";
            }

            return null;
        }

        private static string Truncate(string text, int maxLength)
        {
            if (maxLength > 0 && text.Length > maxLength)
            {
                return text.Substring(0, maxLength);
            }

            return text;
        }
    }
}
