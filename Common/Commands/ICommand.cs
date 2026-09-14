 #region Copyright Notice
 // ============================================================================
 // Copyright (C) 2008 Ken Reed
 // Copyright (C) 2011 The Stars-Nova Project
 //
 // This file is part of Stars-Nova.
 // See <http://sourceforge.net/projects/stars-nova/>;.
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
 // along with this program. If not, see <http://www.gnu.org/licenses/>;
 // ===========================================================================
 #endregion 

namespace Nova.Common.Commands
{
    using System;
    using System.Xml;

    /// <summary>
    /// Description of ICommand.
    /// </summary>
    /// <remarks>
    /// Note that OrderReader.cs ReadPlayersTurn() must be modified when new commands are added to recognize the command type in the xml.
    /// </remarks>
    public interface ICommand
    {
        bool IsValid(EmpireData empire);
        
        void ApplyToState(EmpireData empire);
        
        XmlElement ToXml(XmlDocument xmldoc);
    }
    
    public enum CommandMode
    {
        Add,
        Edit,
        Delete,

        // Only handled by WaypointCommand (see its ApplyToState) - inserts at a specific list
        // index rather than always appending, so a new waypoint can be placed in the middle of
        // an existing route. Kept separate from Add rather than making Add respect Index, since
        // several existing Add call sites pass an Index value that ApplyToState has always
        // ignored - changing Add's meaning would silently change their behavior.
        Insert,

        // Only handled by ProductionCommand (see its ApplyToState/OtherIndex) - atomically
        // exchanges the two queue entries at Index and OtherIndex. Deliberately NOT implemented
        // as two paired Edit commands (swap A into B's slot, then B into A's): ProductionCommand.
        // IsValid's Edit case blocks any edit that would *decrease* the remaining/total cost at
        // an index (an anti-cheat guard against quietly substituting a cheaper order) - which
        // also blocks a perfectly legitimate reorder whenever the two adjacent orders have
        // different costs, since exactly one of the two paired Edits would then be moving a
        // cheaper order into a pricier order's slot. Since a real swap changes no order's cost
        // at all, it needs its own validity rule instead of going through Edit's.
        Swap
    }
}
