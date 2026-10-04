#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009, 2010, 2011 The Stars-Nova Project
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

namespace Nova.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.Serialization;
    using System.Xml;

    using Nova.Common;
    using Nova.Common.Components;
    using Nova.Common.DataStructures;

    /// <summary>
    /// This module converts the console's state into Intel and saves it, thereby 
    /// generating the next turn to be played.
    /// </summary>
    public class IntelWriter
    {
        private readonly ServerData serverState;
        private readonly Scores scores;
        private Intel turnData;
        
        public IntelWriter(ServerData serverState, Scores scores)
        {
            this.serverState = serverState;
            this.scores = scores;
        }


        /// <summary>
        /// Save the turn data.
        /// </summary>
        /// <remarks>
        /// We have to be very careful that we have a consistent and self-contained data set in the turn file.
        /// For example, we write out "AllStars" but turnData.AllStars is not the same as stateData.AllStars.
        /// So make sure any pointers to AllStars refer to the copy in turnData otherwise we'll get
        /// duplicated (but separate) star objects.
        /// </remarks>
        /// <summary>
        /// The minefields one player's turn file carries: only those the player can see
        /// (EmpireData.CanSeeMinefield - its own, those that struck its fleets and those its
        /// scanners detected this year), the "other races' visible minefields" of a player's
        /// snapshot (behavior-specs-10/save-turn-file-format.md), rather than every field in the
        /// game.
        /// </summary>
        public static Dictionary<long, Minefield> VisibleMinefieldsFor(ServerData serverState, EmpireData empire)
        {
            Dictionary<long, Minefield> visible = new Dictionary<long, Minefield>();
            foreach (KeyValuePair<long, Minefield> entry in serverState.AllMinefields)
            {
                if (empire.CanSeeMinefield(entry.Value))
                {
                    visible.Add(entry.Key, entry.Value);
                }
            }

            return visible;
        }

        /// <summary>
        /// The score records one player's turn file carries (behavior-specs-11/
        /// save-turn-file-format.md §3, "Score records"): the viewer's own race, any eliminated
        /// race (its final standing is public), every race once the game is over (the winner mark
        /// is set), and every race under "Public Player Scores" once the new turn counter exceeds
        /// 19 (the file for 2420 onward). Otherwise another race's score never reaches the player.
        /// </summary>
        public List<ScoreRecord> VisibleScores(EmpireData viewer, List<ScoreRecord> allScores)
        {
            bool gameOver = false;
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                if (empire.Winner)
                {
                    gameOver = true;
                    break;
                }
            }

            bool publicScores = GameSettings.Data.PublicPlayerScores
                && serverState.TurnYear - Global.StartingYear > 19;

            List<ScoreRecord> visible = new List<ScoreRecord>();
            foreach (ScoreRecord record in allScores)
            {
                bool own = record.EmpireId == viewer.Id;
                bool eliminated = serverState.AllEmpires.TryGetValue((ushort)record.EmpireId, out EmpireData scored)
                    && scored.Eliminated;

                if (own || eliminated || gameOver || publicScores)
                {
                    visible.Add(record);
                }
            }

            return visible;
        }

        public void WriteIntel()
        {
            foreach (EmpireData empire in serverState.AllEmpires.Values)
            {
                turnData = new Intel();
                turnData.AllMinefields = VisibleMinefieldsFor(serverState, empire);
                turnData.EmpireState = serverState.AllEmpires[empire.Id];
                
                
                // Copy any messages
                foreach (Message message in serverState.AllMessages)
                {
                    if (message.Audience == Global.Everyone || message.Audience == empire.Id)
                    {
                        turnData.Messages.Add(message);
                    }
                }

                // Don't try and generate a scores report on the very start of a new
                // game.

                if (serverState.TurnYear > Global.StartingYear)
                {
                    turnData.AllScores = VisibleScores(empire, scores.GetScores());
                }
                else
                {
                    turnData.AllScores = new List<ScoreRecord>();
                }

                // The game's own folder; nova.conf's ServerFolder (the last game created or
                // opened, a process- and user-wide setting) only stands in when the state has no
                // usable folder. Overwriting it here wrote this game's turn files into whatever
                // game nova.conf pointed at, and made two games in one process trample each other.
                if (string.IsNullOrEmpty(serverState.GameFolder) || !Directory.Exists(serverState.GameFolder))
                {
                    serverState.GameFolder = FileSearcher.GetFolder(Global.ServerFolderKey, Global.ServerFolderName);
                }

                if (serverState.GameFolder == null)
                {
                    Report.Error("Intel Writer: WriteIntel() - Unable to create file \"Nova.intel\".");
                    return;
                }
                string turnFileName = Path.Combine(serverState.GameFolder, empire.Race.Name + Global.IntelExtension);

                // Write out the intel file, as xml
                bool waitForFile = false;
                double waitTime = 0.0; // seconds
                do
                {
                    try
                    {
                        using (Stream turnFile = new FileStream(turnFileName /*+ ".xml"*/, FileMode.Create))
                        {
                            // Setup the XML document
                            XmlDocument xmldoc = new XmlDocument();
                            Global.InitializeXmlDocument(xmldoc);

                            // add the Intel to the document
                            xmldoc.ChildNodes.Item(1).AppendChild(turnData.ToXml(xmldoc));

                            xmldoc.Save(turnFile);
                        }
                        waitForFile = false;
                    }
                    catch (System.IO.IOException)
                    {
                        // IOException. Is the file locked? Try waiting.
                        if (waitTime < Global.TotalFileWaitTime)
                        {
                            waitForFile = true;
                            System.Threading.Thread.Sleep(Global.FileWaitRetryTime);
                            waitTime += 0.1;
                        }
                        else
                        {
                            // Give up, maybe something else is wrong?
                            throw;
                        }
                    }
                } 
                while (waitForFile);
            }
        }
    }
}


