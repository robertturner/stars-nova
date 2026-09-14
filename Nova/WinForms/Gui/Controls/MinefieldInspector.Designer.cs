namespace Nova.WinForms.Gui
{
    public partial class MinefieldInspector
    {
        private void InitializeComponent()
        {
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label1 = new System.Windows.Forms.Label();
            this.owner = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.position = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.radius = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.numberOfMines = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.safeSpeed = new System.Windows.Forms.Label();
            this.label6 = new System.Windows.Forms.Label();
            this.displayMode = new System.Windows.Forms.ComboBox();
            this.groupBox1.SuspendLayout();
            this.SuspendLayout();
            //
            // groupBox1
            //
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.owner);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.position);
            this.groupBox1.Controls.Add(this.label3);
            this.groupBox1.Controls.Add(this.radius);
            this.groupBox1.Controls.Add(this.label4);
            this.groupBox1.Controls.Add(this.numberOfMines);
            this.groupBox1.Controls.Add(this.label5);
            this.groupBox1.Controls.Add(this.safeSpeed);
            this.groupBox1.Controls.Add(this.label6);
            this.groupBox1.Controls.Add(this.displayMode);
            this.groupBox1.Location = new System.Drawing.Point(8, 8);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(344, 210);
            this.groupBox1.TabIndex = 0;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Minefield";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(11, 25);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(38, 13);
            this.label1.TabIndex = 0;
            this.label1.Text = "Owner";
            //
            // owner
            //
            this.owner.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.owner.Location = new System.Drawing.Point(160, 22);
            this.owner.Name = "owner";
            this.owner.Size = new System.Drawing.Size(170, 18);
            this.owner.TabIndex = 1;
            this.owner.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(11, 51);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(48, 13);
            this.label2.TabIndex = 2;
            this.label2.Text = "Position";
            //
            // position
            //
            this.position.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.position.Location = new System.Drawing.Point(160, 48);
            this.position.Name = "position";
            this.position.Size = new System.Drawing.Size(170, 18);
            this.position.TabIndex = 3;
            this.position.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label3
            //
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(11, 77);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(38, 13);
            this.label3.TabIndex = 4;
            this.label3.Text = "Radius";
            //
            // radius
            //
            this.radius.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.radius.Location = new System.Drawing.Point(160, 74);
            this.radius.Name = "radius";
            this.radius.Size = new System.Drawing.Size(170, 18);
            this.radius.TabIndex = 5;
            this.radius.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label4
            //
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(11, 103);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(74, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "Number of mines";
            //
            // numberOfMines
            //
            this.numberOfMines.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numberOfMines.Location = new System.Drawing.Point(160, 100);
            this.numberOfMines.Name = "numberOfMines";
            this.numberOfMines.Size = new System.Drawing.Size(170, 18);
            this.numberOfMines.TabIndex = 7;
            this.numberOfMines.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label5
            //
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(11, 129);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(59, 13);
            this.label5.TabIndex = 8;
            this.label5.Text = "Safe speed";
            //
            // safeSpeed
            //
            this.safeSpeed.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.safeSpeed.Location = new System.Drawing.Point(160, 126);
            this.safeSpeed.Name = "safeSpeed";
            this.safeSpeed.Size = new System.Drawing.Size(170, 18);
            this.safeSpeed.TabIndex = 9;
            this.safeSpeed.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            //
            // label6
            //
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(11, 158);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(76, 13);
            this.label6.TabIndex = 10;
            this.label6.Text = "Map overlay";
            //
            // displayMode
            //
            // Local, unpersisted display preference for how this minefield is highlighted on the
            // Star Map while it's the selected item - purely a view choice, never written to the
            // save file or the Minefield object itself. See StarMap.cs's minefield drawing loop
            // and MinefieldInspector.cs's DisplayMode property/StarmapChanged event.
            this.displayMode.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.displayMode.Items.AddRange(new object[] {
            "Radius circle",
            "Mine count label",
            "Safe speed label"});
            this.displayMode.Location = new System.Drawing.Point(160, 155);
            this.displayMode.Name = "displayMode";
            this.displayMode.Size = new System.Drawing.Size(170, 21);
            this.displayMode.TabIndex = 11;
            this.displayMode.SelectedIndexChanged += new System.EventHandler(this.DisplayMode_SelectedIndexChanged);
            //
            // MinefieldInspector
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBox1);
            this.Margin = new System.Windows.Forms.Padding(0);
            this.Name = "MinefieldInspector";
            this.Size = new System.Drawing.Size(361, 399);
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            this.ResumeLayout(false);
        }

        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label owner;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label position;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label radius;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label numberOfMines;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label safeSpeed;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.ComboBox displayMode;
    }
}
