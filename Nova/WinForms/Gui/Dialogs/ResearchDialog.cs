#region Copyright Notice
// ============================================================================
// Copyright (C) 2008 Ken Reed
// Copyright (C) 2009-2012 The Stars-Nova Project
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
// ============================================================================
#endregion


namespace Nova.WinForms.Gui
{
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Windows.Forms;

    using Nova.Client;
    using Nova.Common;
    using Nova.Common.Commands;
    using Nova.Common.Components;

    /// <Summary>
    /// This is the hook to listen for changes in research budget.
    /// Objects who subscribe to this should respond by
    /// udpating their related values. We don't specify arguments
    /// because relevant data can be read on ClientState.
    /// </Summary>
    public delegate bool ResearchAllocationChanged();

    /// <Summary>
    /// One row of the "Expected Research Benefits" list: a component together with how
    /// many additional levels of the target field are needed to unlock it, and the display
    /// color that distance maps to (see BenefitColor).
    /// </Summary>
    internal sealed class ResearchBenefitEntry
    {
        public readonly string Text;
        public readonly Color Color;

        public ResearchBenefitEntry(string text, Color color)
        {
            Text = text;
            Color = color;
        }

        public override string ToString()
        {
            return Text;
        }
    }
    
    /// <Summary>
    /// Dialog for displaying current research levels and allocating resources to
    /// further research.
    /// </Summary>
    public partial class ResearchDialog : Form
    {
        /// <Summary>
        /// This event should be fired when the global research budget is changed.
        /// Note that it's more apropiate to fire this when all changes are done,
        /// for example, on closing the Research Dialog instead of each Point change.
        /// </Summary>
        public event ResearchAllocationChanged ResearchAllocationChangedEvent;

        private readonly bool dialoginitialized;
        
        private readonly ClientData clientState;

        private readonly Dictionary<string, RadioButton> buttons = new Dictionary<string, RadioButton>();
        private readonly TechLevel currentLevel;
        private TechLevel.ResearchField targetArea;
        private readonly int availableEnergy;         

        /// <Summary>
        /// Initializes a new instance of the ResearchDialog class.
        /// </Summary>
        public ResearchDialog(ClientData clientState)
        {
            InitializeComponent();

            this.clientState = clientState;
            currentLevel = this.clientState.EmpireState.ResearchLevels;

            // Provide a convienient way of getting a button from it's name.
            buttons.Add("Energy", energyButton);
            buttons.Add("Weapons", weaponsButton);
            buttons.Add("Propulsion", propulsionButton);
            buttons.Add("Construction", constructionButton);
            buttons.Add("Electronics", electronicsButton);
            buttons.Add("Biotechnology", biotechButton);

            // Set the currently attained research levels
            energyLevel.Text        = currentLevel[TechLevel.ResearchField.Energy].ToString();
            weaponsLevel.Text       = currentLevel[TechLevel.ResearchField.Weapons].ToString();
            propulsionLevel.Text    = currentLevel[TechLevel.ResearchField.Propulsion].ToString();
            constructionLevel.Text  = currentLevel[TechLevel.ResearchField.Construction].ToString();
            electronicsLevel.Text   = currentLevel[TechLevel.ResearchField.Electronics].ToString();
            biotechLevel.Text       = currentLevel[TechLevel.ResearchField.Biotechnology].ToString();

            // Ensure that the correct RadioButton is checked to reflect the
            // current research selection and the budget up-down control is
            // initialized with the correct value.
            
            // Find the first research priority
            // TODO: Implement a proper hierarchy of research ("next research field") system.
            foreach (TechLevel.ResearchField area in Enum.GetValues(typeof(TechLevel.ResearchField)))
            {
                if (this.clientState.EmpireState.ResearchTopics[area] == 1)
                {
                    targetArea = area;
                    break;        
                }
            }

            RadioButton button = buttons[Enum.GetName(typeof(TechLevel.ResearchField), targetArea)];

            button.Checked = true;

            availableEnergy = CountEnergy();
            availableResources.Text = this.availableEnergy.ToString(System.Globalization.CultureInfo.InvariantCulture);
            budgetPercentage.Value = this.clientState.EmpireState.ResearchBudget;
            dialoginitialized = true;

            ParameterChanged(null, null);
        }


        /// <Summary>
        /// A new area has been selected for research. Make a note of where the research
        /// resources are now going to be spent.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void CheckChanged(object sender, EventArgs e)
        {
            if (dialoginitialized == false)
            {
                return;
            }

            RadioButton button = sender as RadioButton;

            if (button  != null && button.Checked == true)
            {
                try
                {
                    clientState.EmpireState.ResearchTopics[targetArea] = 0;
                    targetArea = (TechLevel.ResearchField)Enum.Parse(typeof(TechLevel.ResearchField), button.Text, true);
                    clientState.EmpireState.ResearchTopics[targetArea] = 1;
                }
                catch (System.ArgumentException)
                {
                    Report.Error("ResearchDialog.cs : CheckChanged() - unrecognised field of research.");
                }
            }
            
            PopulateResearchBenefits();

            ParameterChanged(null, null);
        }


        /// <Summary>
        /// Colors for Expected Research Benefits entries, matching the original Stars!
        /// help ("Research dialog features / Expected Research Benefits"): green for an
        /// item unlocked by completing the level currently being researched, blue for one
        /// unlocked 2-4 levels further out, black for 5 or more levels further out.
        /// </Summary>
        private static readonly Color BenefitColorNextLevel = Color.Green;
        private static readonly Color BenefitColorNearLevels = Color.Blue;
        private static readonly Color BenefitColorFarLevels = Color.Black;

        /// <Summary>
        /// Populate the Expected Research Benefits list with every component not yet
        /// available, that further research in the target field alone (holding every
        /// other field at its current level) would eventually unlock - not just the very
        /// next level. Each entry is colored by how many additional levels of the target
        /// field are needed, per the original game's color legend. A component whose
        /// requirements can never be met by raising the target field alone (because it
        /// also needs a higher level in some other, unrelated field) is omitted, since
        /// researching this field won't unlock it.
        /// </Summary>
        private void PopulateResearchBenefits()
        {
            AllComponents allComponents = new AllComponents();

            int currentFieldLevel = currentLevel[targetArea];

            var benefits = new List<Tuple<int, ResearchBenefitEntry>>();

            foreach (Nova.Common.Components.Component component in allComponents.GetAll.Values)
            {
                if (currentLevel.Meets(component.RequiredTech))
                {
                    // Already available - nothing more to research for it.
                    continue;
                }

                int levelsAway = -1;

                for (int candidate = currentFieldLevel + 1; candidate <= TechLevel.MaxLevel; candidate++)
                {
                    TechLevel trialLevel = new TechLevel(currentLevel);
                    trialLevel[targetArea] = candidate;

                    if (trialLevel.Meets(component.RequiredTech))
                    {
                        levelsAway = candidate - currentFieldLevel;
                        break;
                    }
                }

                if (levelsAway < 0)
                {
                    // Needs a higher level in some other field too - researching
                    // targetArea alone will never unlock this, so don't list it here.
                    continue;
                }

                Color color;
                if (levelsAway == 1)
                {
                    color = BenefitColorNextLevel;
                }
                else if (levelsAway <= 4)
                {
                    color = BenefitColorNearLevels;
                }
                else
                {
                    color = BenefitColorFarLevels;
                }

                string text = component.Name + " " + component.Type;
                benefits.Add(Tuple.Create(levelsAway, new ResearchBenefitEntry(text, color)));
            }

            benefits.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            researchBenefits.Items.Clear();
            foreach (Tuple<int, ResearchBenefitEntry> benefit in benefits)
            {
                researchBenefits.Items.Add(benefit.Item2);
            }
        }


        /// <Summary>
        /// Owner-draw handler for the Expected Research Benefits list, so each entry can
        /// be painted in the color that indicates how far away it is (see
        /// PopulateResearchBenefits).
        /// </Summary>
        private void ResearchBenefits_DrawItem(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();

            if (e.Index >= 0 && e.Index < researchBenefits.Items.Count)
            {
                ResearchBenefitEntry entry = researchBenefits.Items[e.Index] as ResearchBenefitEntry;
                Color color = entry != null ? entry.Color : e.ForeColor;

                using (Brush brush = new SolidBrush(color))
                {
                    e.Graphics.DrawString(researchBenefits.Items[e.Index].ToString(), e.Font, brush, e.Bounds);
                }
            }

            e.DrawFocusRectangle();
        }


        /// <Summary>
        /// The Help button has been pressed. Open the manual at the Research dialog topic.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void HelpClicked(object sender, EventArgs e)
        {
            const int researchDialogTopic = 297;

            using (HelpForm helpForm = new HelpForm(researchDialogTopic))
            {
                helpForm.ShowDialog();
            }
        }

        /// <Summary>
        /// The OK button has been pressed. Just exit the dialog.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void OKClicked(object sender, EventArgs e)
        {
            //Generate a research command to describe the changes.
            ResearchCommand command = new ResearchCommand();
            command.Budget = (int)budgetPercentage.Value;
            command.Topics.Zero();
            command.Topics[targetArea] = 1;    
            
            if (command.IsValid(clientState.EmpireState))
            {
                clientState.Commands.Push(command);
                command.ApplyToState(clientState.EmpireState);
            }
            
            // This is done for synchronization. We wait for the event handlers
            // to return something (and thus complete) before closing the Form and invalidating
            // all event handlers and delegates thus crashing everything into the fiery void of despair.
            if (ResearchAllocationChangedEvent())
            {
            }
            
            Close();
        }
        

        /// <Summary>
        /// The resource budget has been changed. Update all relevant fields.
        /// </Summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">A <see cref="EventArgs"/> that contains the event data.</param>
        private void ParameterChanged(object sender, EventArgs e)
        {
        	int resourcesRequired = 0;
            int yearsToComplete = 0;

            //stateData.EmpireState.ResearchBudget = (int)resourceBudget.Value;
            int budgetedEnergy = (availableEnergy * (int)budgetPercentage.Value) / 100;
            int bankedResources = clientState.EmpireState.ResearchResources[targetArea];

            if (currentLevel[targetArea] >= TechLevel.MaxLevel)
            {
                // Research.Cost's base-cost table only has entries for levels 1..MaxLevel;
                // asking for MaxLevel + 1 would throw. Nothing more to research here.
                completionResources.Text = "Maxed";
            }
            else
            {
                int targetCost = Research.Cost(targetArea, clientState.EmpireState.Race, currentLevel, currentLevel[targetArea] + 1);
                resourcesRequired = targetCost - currentLevel[targetArea] - bankedResources;
                completionResources.Text = resourcesRequired.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (budgetPercentage.Value != 0 &&
                budgetedEnergy > 0 &&
                currentLevel[targetArea] < TechLevel.MaxLevel)
            {
                yearsToComplete = (int)Math.Ceiling((double)resourcesRequired / budgetedEnergy);
                completionTime.Text = yearsToComplete.ToString();
            }
            else
            {
                completionTime.Text = "Never";
            }

            numericResources.Text = budgetedEnergy.ToString(System.Globalization.CultureInfo.InvariantCulture);            
        }


        /// <Summary>
        /// Return the total number of energy resources available to the current race
        /// </Summary>
        /// <returns>Total energy being invested in research.</returns>
        private int CountEnergy()
        {
            int totalEnergy = 0;

            foreach (Star star in clientState.EmpireState.OwnedStars.Values)
            {
                if (star.Owner == clientState.EmpireState.Id)
                {
                    totalEnergy += star.GetResourceRate();
                }
            }
            return totalEnergy;
        }

        private void ResearchDialog_Load(object sender, EventArgs e)
        {
            CheckChanged(sender, e);
        }
    }
}
