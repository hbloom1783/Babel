﻿namespace Babel
{
    partial class Viewfinder
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // Viewfinder
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.SystemColors.ControlDark;
            this.ClientSize = new System.Drawing.Size(568, 303);
            this.ControlBox = false;
            this.DoubleBuffered = true;
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.MinimumSize = new System.Drawing.Size(130, 80);
            this.Name = "Viewfinder";
            this.Opacity = 0.7D;
            this.ShowInTaskbar = false;
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Show;
            this.Text = "Viewfinder";
            this.TopMost = true;
            this.TransparencyKey = System.Drawing.SystemColors.ControlDark;
            this.Paint += new System.Windows.Forms.PaintEventHandler(this.Viewfinder_Paint);
            this.MouseClick += new System.Windows.Forms.MouseEventHandler(this.Viewfinder_MouseClick);
            this.Move += new System.EventHandler(this.Viewfinder_Move);
            this.Resize += new System.EventHandler(this.Viewfinder_Resize);
            this.ResumeLayout(false);

        }

        #endregion
    }
}