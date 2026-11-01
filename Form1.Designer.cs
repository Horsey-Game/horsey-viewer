namespace horsey
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private Panel toolPanel;
        private Button refreshButton;
        private Label statusLabel;
        private MapCanvas mapPanel;
        private System.Windows.Forms.Timer pollTimer;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            toolPanel = new Panel();
            refreshButton = new Button();
            statusLabel = new Label();
            mapPanel = new MapCanvas();
            pollTimer = new System.Windows.Forms.Timer(components);
            toolPanel.SuspendLayout();
            SuspendLayout();
            // 
            // toolPanel
            // 
            toolPanel.Controls.Add(refreshButton);
            toolPanel.Controls.Add(statusLabel);
            toolPanel.Dock = DockStyle.Top;
            toolPanel.Location = new Point(0, 0);
            toolPanel.Name = "toolPanel";
            toolPanel.Size = new Size(900, 40);
            toolPanel.TabIndex = 1;
            // 
            // refreshButton
            // 
            refreshButton.Location = new Point(8, 6);
            refreshButton.Name = "refreshButton";
            refreshButton.Size = new Size(100, 28);
            refreshButton.TabIndex = 0;
            refreshButton.Text = "Refresh";
            refreshButton.UseVisualStyleBackColor = true;
            // 
            // statusLabel
            // 
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(116, 12);
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(0, 15);
            statusLabel.TabIndex = 4;
            // 
            // mapPanel
            // 
            mapPanel.BackColor = Color.FromArgb(40, 40, 40);
            mapPanel.Dock = DockStyle.Fill;
            mapPanel.Location = new Point(0, 40);
            mapPanel.Name = "mapPanel";
            mapPanel.Size = new Size(900, 560);
            mapPanel.TabIndex = 0;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(900, 600);
            Controls.Add(mapPanel);
            Controls.Add(toolPanel);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MinimumSize = new Size(500, 300);
            Name = "Form1";
            Text = "Horsey Map Editor";
            WindowState = FormWindowState.Maximized;
            Load += Form1_Load;
            toolPanel.ResumeLayout(false);
            toolPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
    }
}
