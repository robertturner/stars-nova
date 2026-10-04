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

namespace Nova.Common
{
    /// <summary>
    /// Message.Type strings of the production-queue notices (production-queue.md 10i). Every
    /// production notice carries the planet's name as its Message.Event, the original's "subject
    /// word = planet id" (client-ui-dialog-catalog.md, message record), so a click can find it.
    /// The two queue-empty notices get their own types because the message-click handler treats
    /// exactly those (62 and 63, plus the never-posted 175-180) as "open the production queue of
    /// the named planet" (client-ui-dialog-catalog.md "Message-click routing").
    /// </summary>
    public static class ProductionNoticeTypes
    {
        /// <summary>Every other production notice (built, cut back, terraformed, ...).</summary>
        public const string Production = "Production";

        /// <summary>Message 63: an owned planet has no production queue at all.</summary>
        public const string QueueEmpty = "ProductionQueueEmpty";

        /// <summary>Message 62: the planet finished its orders and its queue is now empty.</summary>
        public const string OrdersCompleted = "ProductionOrdersCompleted";

        /// <summary>True for a production notice of any kind (its Event is a planet name).</summary>
        public static bool IsProductionNotice(string messageType)
        {
            return messageType == Production || messageType == QueueEmpty || messageType == OrdersCompleted;
        }

        /// <summary>True for the notices whose click opens the planet's production queue.</summary>
        public static bool OpensProductionQueue(string messageType)
        {
            return messageType == QueueEmpty || messageType == OrdersCompleted;
        }
    }
}
