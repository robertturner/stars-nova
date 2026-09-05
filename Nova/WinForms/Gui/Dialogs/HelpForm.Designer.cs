namespace Nova.WinForms.Gui
{
    public partial class HelpForm
    {
        /// <Summary>
        /// Required designer variable.
        /// </Summary>
        private System.ComponentModel.IContainer components = null;

        /// <Summary>
        /// Clean up any resources being used.
        /// </Summary>
        /// <param name="disposing">Set to true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <Summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </Summary>
        private void InitializeComponent()
        {
            this.splitContainer = new System.Windows.Forms.SplitContainer();
            this.searchBox = new System.Windows.Forms.TextBox();
            this.searchLabel = new System.Windows.Forms.Label();
            this.topicList = new System.Windows.Forms.ListBox();
            this.contentBrowser = new System.Windows.Forms.WebBrowser();
            this.attributionLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).BeginInit();
            this.splitContainer.Panel1.SuspendLayout();
            this.splitContainer.Panel2.SuspendLayout();
            this.splitContainer.SuspendLayout();
            this.SuspendLayout();
            //
            // splitContainer
            //
            this.splitContainer.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer.Location = new System.Drawing.Point(0, 0);
            this.splitContainer.Name = "splitContainer";
            //
            // splitContainer.Panel1
            //
            this.splitContainer.Panel1.Controls.Add(this.topicList);
            this.splitContainer.Panel1.Controls.Add(this.searchBox);
            this.splitContainer.Panel1.Controls.Add(this.searchLabel);
            //
            // splitContainer.Panel2
            //
            this.splitContainer.Panel2.Controls.Add(this.contentBrowser);
            this.splitContainer.Size = new System.Drawing.Size(860, 560);
            this.splitContainer.SplitterDistance = 260;
            this.splitContainer.TabIndex = 0;
            //
            // searchLabel
            //
            this.searchLabel.AutoSize = true;
            this.searchLabel.Location = new System.Drawing.Point(6, 8);
            this.searchLabel.Name = "searchLabel";
            this.searchLabel.Size = new System.Drawing.Size(43, 13);
            this.searchLabel.TabIndex = 0;
            this.searchLabel.Text = "Search:";
            //
            // searchBox
            //
            this.searchBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) | System.Windows.Forms.AnchorStyles.Right));
            this.searchBox.Location = new System.Drawing.Point(55, 5);
            this.searchBox.Name = "searchBox";
            this.searchBox.Size = new System.Drawing.Size(200, 20);
            this.searchBox.TabIndex = 1;
            this.searchBox.TextChanged += new System.EventHandler(this.SearchBox_TextChanged);
            //
            // topicList
            //
            this.topicList.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
                        | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.topicList.FormattingEnabled = true;
            this.topicList.IntegralHeight = false;
            this.topicList.Location = new System.Drawing.Point(6, 31);
            this.topicList.Name = "topicList";
            this.topicList.Size = new System.Drawing.Size(249, 500);
            this.topicList.TabIndex = 2;
            this.topicList.SelectedIndexChanged += new System.EventHandler(this.TopicList_SelectedIndexChanged);
            //
            // contentBrowser
            //
            this.contentBrowser.Dock = System.Windows.Forms.DockStyle.Fill;
            this.contentBrowser.Location = new System.Drawing.Point(0, 0);
            this.contentBrowser.MinimumSize = new System.Drawing.Size(20, 20);
            this.contentBrowser.Name = "contentBrowser";
            this.contentBrowser.ScriptErrorsSuppressed = true;
            this.contentBrowser.Size = new System.Drawing.Size(596, 560);
            this.contentBrowser.TabIndex = 0;
            //
            // attributionLabel
            //
            this.attributionLabel.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.attributionLabel.ForeColor = System.Drawing.SystemColors.GrayText;
            this.attributionLabel.Location = new System.Drawing.Point(0, 560);
            this.attributionLabel.Name = "attributionLabel";
            this.attributionLabel.Padding = new System.Windows.Forms.Padding(6, 4, 6, 4);
            this.attributionLabel.Size = new System.Drawing.Size(860, 22);
            this.attributionLabel.TabIndex = 1;
            this.attributionLabel.Text = "Original Stars! Player\'s Guide content - see HelpContent/NOTICE-HelpContent.txt" +
    " for source and licensing.";
            //
            // HelpForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(860, 582);
            this.Controls.Add(this.splitContainer);
            this.Controls.Add(this.attributionLabel);
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.Name = "HelpForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Stars! Nova - Manual";
            this.splitContainer.Panel1.ResumeLayout(false);
            this.splitContainer.Panel1.PerformLayout();
            this.splitContainer.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer)).EndInit();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainer;
        private System.Windows.Forms.TextBox searchBox;
        private System.Windows.Forms.Label searchLabel;
        private System.Windows.Forms.ListBox topicList;
        private System.Windows.Forms.WebBrowser contentBrowser;
        private System.Windows.Forms.Label attributionLabel;
    }
}
