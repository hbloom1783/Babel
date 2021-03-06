﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Babel.Windows;
using System.Reflection;
using System.IO;
using Babel.Async;

namespace Babel
{
    public partial class frmBabel : Form
    {
        #region Header
        public List<PhraseRect> PhraseRects; // Track user-selected phrases
        public IAsyncOCR OCRResult;

        // For bounding box code
        public bool Marking;
        Point MouseStart;
        Point MouseEnd;
        public bool StartingDrag;
        public bool Dragging;
        public PhraseRect DrugPhrase;
        public bool CtrlDown;
        PhraseRect SelectedRect;

        // Image buffers
        private Image snap = null; // Exact image captured from screenshot
        private Image edit = null; // Modified image

        Viewfinder vfw; // Persistent viewfinder window
        public Rectangle SnapRegion; // Position of capture
        public bool AutoScaleVFW; // Whether viewfinder size should always follow main form

        public static bool AutoOCR;
        public static bool Auto_Autophrase;
        public static bool Autofit;
        public static PhraseRectMode NewPhraseMode;

        // Odometer readings
        public static long SnapsTaken;
        public static long CharsTranslated;

        BoundingState BoundingBoxState;
        enum BoundingState
        {
            Normal,
            RectsFound,
            TooSmall
        }

        public IntPtr TrackingWindow;

        public enum State
        {
            ready,
            snapped,
            OCRing,
            OCRed,
            translating,
            translated,
        }
        State AppState;

        private frmWindowPicker Picker;

        public enum PhraseRectMode
        {
            intersects,
            contains,
        }

        public static List<WorkerError> WorkerErrors;

        public static ErrorLog ErrorWindow = new ErrorLog();
        #endregion

        public frmBabel()
        {
            InitializeComponent();
        }

        private static Assembly MyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            string AssName = args.Name.Split(',')[0];
            string AssPath = Application.StartupPath + "\\DLLs\\" + AssName + ".dll";
            if (File.Exists(AssPath))
            {
                return (Assembly.LoadFile(AssPath));
            } else
            {
                // This may be a problem if we actually can compile on Mac or Linux
                String[] IgnoredModules =
                {
                    "Xamarin",
                    "Unity",
                    "Mono",
                    ".resources"
                };
                bool IgnoredModule = false;
                foreach(string module in IgnoredModules)
                { if (args.Name.Contains(module)) IgnoredModule = true; }
                if (!IgnoredModule) DebugLog.Log("Couldn't find assembly: " + args.Name);
                return null;
            }
            //return typeof(MyType).Assembly;
        }

        private void Viewfinder_Load(object sender, EventArgs e)
        {
            DebugLog.Log("==============================================");
            DebugLog.Log("Babel starting");
            LoadSettings();

            AppDomain currentDomain = AppDomain.CurrentDomain;

            currentDomain.AssemblyResolve += new ResolveEventHandler(MyResolveEventHandler);
            

            if (Properties.Settings.Default.WaiverSigned != true)
            {
                Disclaimer dc = new Disclaimer();
                dc.ShowDialog(this);
            }

            OCRResult = null;
            PhraseRects = new List<PhraseRect>();

            SnapRegion = new Rectangle(0, 0, 640, 480);

            Text = "Viewfinder - Ready";
            ChangeState(State.ready);

            vfw = new Viewfinder();
            vfw.MainForm = this;
            vfw.StartPosition = FormStartPosition.Manual;
            vfw.Location = new Point(this.Left + 50, this.Top + 50);

            Picker = new frmWindowPicker();

            AutoOCR = false;
            Auto_Autophrase = false;
            Autofit = true;

            NewPhraseMode = PhraseRectMode.intersects;

            SafeAsyncOCR_Callback = AsyncOCR_callback;
            
            WorkerErrors = new List<WorkerError>();

            SafeIncrementOdometer = new SafeIncrementOdometer_Delegate(IncrementOdometer);

            TrackingWindow = (IntPtr) 0;

            DebugLog.Log("Babel started");

#if DEBUG
            //ToggleVFW(); // Show viewfinder immediately
#endif
        }

        private void frmBabel_Resize(object sender, EventArgs e)
        {
            if (tsbVFWAutoSize.Checked) vfw.Size = panel1.Size;
        }

        #region Toolbar events

        // Ingest image from viewfinder
        private void btnSnap_Click(object sender, EventArgs e)
        {
            GetSnap(Snap());
        }

        // Ingest image from clipboard
        private void tsbClipboard_Click(object sender, EventArgs e)
        {
            if (Clipboard.ContainsImage())
            {
                GetSnap(Clipboard.GetImage());
            }
        }

        private void tsbOCR_Click(object sender, EventArgs e)
        {
            DoOCR();
        }

        private void tsbVFW_Click(object sender, EventArgs e)
        {
            ToggleVFW();
            vfw.Flicker();
        }

        
        private void tsbAutoOCR_CheckedChanged(object sender, EventArgs e)
        {
            if(tsbAutoOCR.Checked)
            {
                tsbOCR.Enabled = false;
                //Properties.Settings.Default.autoOCR = false;
                AutoOCR = true;
                if (AppState == State.snapped) DoOCR(true);
            } else
            {
                //Properties.Settings.Default.autoOCR = true;
                AutoOCR = false;
                tsbOCR.Enabled = true;
            }
            //Properties.Settings.Default.Save();
        }

        // Clear all identified phrases
        private void btnRevert_Click(object sender, EventArgs e)
        {
            ClearPhrases();
            ChangeState(State.OCRed);
        }

        // Save image to disk
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (sfdDisplay.ShowDialog() == DialogResult.OK)
            {
                // Copy the current snap, run the graphics draw routine on it and save it
                Image tempImage = snap.Copy();
                Graphics g = Graphics.FromImage(tempImage);
                DrawImage(g);
                tempImage.Save(sfdDisplay.FileName);
            }
        }
        // Save unmodified screenshot to disk
        private void tsbSaveRaw_Click(object sender, EventArgs e)
        {
            if (sfdDisplay.ShowDialog() == DialogResult.OK)
            {
                // Copy the current snap and save it as-is
                snap.Save(sfdDisplay.FileName);
            }
        }

        private void SaveForStreaming()
        {
            Image tempImage;

            if (snap != null)
            {
                // Copy the current snap, run the graphics draw routine on it and save it
                tempImage = new Bitmap(snap.Width, snap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                Graphics g = Graphics.FromImage(tempImage);
                DrawOBS(g);
            } else
            {
                tempImage = new Bitmap(1, 1);
            }

            string streampath = Application.StartupPath + "\\stream.png";
            try
            {
                tempImage.Save(streampath);
            }
            catch //(Exception err)
            {
                if (snap != null)
                {
                    MessageBox.Show("Failed to write file to " + streampath + ".\r\n\r\nThe file may be in use. Wait a moment and try again.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        private void saveForStreamingToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveForStreaming();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Settings settings = new Settings();
            settings.ShowDialog(ActiveForm); // Open as modal so CenterParent will work
            LoadSettings();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            Text2Text text2Text = new Text2Text();
            text2Text.BabelForm = this;
            text2Text.StartPosition = FormStartPosition.Manual;
            text2Text.Location = new Point(this.Location.X + 50, this.Location.Y + 50);
            text2Text.Show();
        }

        private void tsbMaxVFW_Click(object sender, EventArgs e)
        {
            Screen screen = Screen.FromControl(vfw);
            vfw.Size = new Size(screen.WorkingArea.Width, screen.WorkingArea.Height);
            vfw.Location = new Point(screen.Bounds.X, screen.Bounds.Y);

            vfw.Flicker();
        }

        private void scaleViewfinderToWindowToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AutoScaleVFW = !AutoScaleVFW;
            vfw.SizeGripStyle = AutoScaleVFW ? SizeGripStyle.Hide : SizeGripStyle.Show;
            vfw.Size = panel1.Size;
            vfw.Flicker();
            vfw.Invalidate();
        }

        // Summon window picker
        private void tsbCrosshair_MouseDown(object sender, MouseEventArgs e)
        {
            Picker.Show();
            // Make a temporary timer that updates the position of the window picker until the user lets off the mouse
            // This could maybe be moved to the Picker class
            Timer PickerTimer = new Timer();
            PickerTimer.Interval = 60;
            MouseStart = MousePosition;
            PickerTimer.Tick += delegate (object ssender, EventArgs ee)
            {
                Timer t = ((Timer)ssender);

                if (MouseStart != MousePosition) // Only update if the mouse has moved
                {
                    Picker.GoPoint(MousePosition); // Tell the picker to look for a window at the mouse location
                    MouseStart = MousePosition;
                }
                if (MouseButtons != MouseButtons.Left) // When the user lets off the mouse, set viewfinder size/loc
                {
                    vfw.Size = Picker.Size;
                    vfw.Location = Picker.Location;
                    this.TrackingWindow = Picker.TrackedWindow;
                    Picker.Hide();
                    vfw.Flicker();
                    t.Dispose();
                }
            };
            PickerTimer.Enabled = true;
        }

        private void tsbAutophrase_Click(object sender, EventArgs e)
        {
            MakeAutoPhrases();
        }
        private void tsbAutoAutophrase_Click(object sender, EventArgs e)
        {
            if (tsbAutoAutophrase.Checked)
            {
                tsbAutophrase.Enabled = false;
                Auto_Autophrase = true;
                if (AppState == State.OCRed) MakeAutoPhrases();
            }
            else
            {
                Auto_Autophrase = false;
                tsbAutophrase.Enabled = true;
            }
        }

        
        private void tsbAutofit_Click(object sender, EventArgs e)
        {
            Autofit = tsbAutofit.Checked;
        }

        private void tsbIntersectsMode_Click(object sender, EventArgs e)
        {
            NewPhraseMode = PhraseRectMode.intersects;
            tsbContainsMode.Checked = false;
            tsbIntersectsMode.Checked = true;
        }
        private void tsbContainsMode_Click(object sender, EventArgs e)
        {
            NewPhraseMode = PhraseRectMode.contains;
            tsbIntersectsMode.Checked = false;
            tsbContainsMode.Checked = true;
        }

        private void tsbAbout_Click(object sender, EventArgs e)
        {
            About abt = new About();
            abt.ShowDialog();
        }

        private void tsbHelp_Click(object sender, EventArgs e)
        {
            string HelpLoc = Application.StartupPath + "\\Help\\index.html";
            try
            {
                System.Diagnostics.Process.Start(HelpLoc);
            } catch (System.ComponentModel.Win32Exception err) {
                if (err.NativeErrorCode == 2)
                {
                    MessageBox.Show("The Help file could not be found at " + HelpLoc);
                }
                else
                {
                    MessageBox.Show("There was an error opening Help: \r\n\r\n" + err.Message);
                }
            }
        }
        #endregion

        #region Keyboard events

        // Handle keyboard input
        private void frmViewFinder_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                // Delete any selected phrase
                PhraseRects.RemoveAll(t => t.Selected == true);
            } else if (e.KeyCode == Keys.ControlKey)
            {
                CtrlDown = true; // For drawing overlapping bounding boxes
            } else if (e.KeyCode == Keys.V && e.Modifiers == Keys.Control)
            {
                // Paste in an image
                if (Clipboard.ContainsImage())
                {
                    GetSnap(Clipboard.GetImage());
                }
            } else if (e.KeyCode == Keys.A && e.Modifiers == Keys.Control)
            {
                // Select all phrases
                PhraseRects.ForEach(x => x.Selected = true);
            }
        }

        private void frmViewFinder_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ControlKey)
            {
                CtrlDown = false; // For drawing overlapping bounding boxes
            }
        }
#endregion

        #region Graphics events

        private void pbxDisplay_Paint(object sender, PaintEventArgs e)
        {
            // Call the image drawing routine against the canvas
            Graphics g = e.Graphics;
            DrawImage(g);
        }

        #endregion

        #region Mouse events
        //======= Mouse events


        // Begin drawing or dragging bounding box
        private void pbxDisplay_MouseDown(object sender, MouseEventArgs e)
        {
            PhraseRect PRect = GetPhraseAtPoint(e.Location);

            if (e.Button == MouseButtons.Right)
            {
                if (PRect != null) PRect.Selected = true;

                return;
            }

            // Don't permit any mouse actions unless OCR is complete
            // This is a crowbar and we may need a better solution when we implement retained phrases between snaps
            if (OCRResult == null || !OCRResult.isDone) return;

            if (PRect != null && !CtrlDown)
            {
                // There was a phrase under the mouse
                // Select it, then set up for a drag if desired

                if (PRect.Selected != true) // If the box wasn't already selected
                {
                    // If the user wasn't holding shift, clear all other selections
                    if (!WindowFunctions.IsPressed((int)WindowFunctions.VirtualKeyStates.VK_LSHIFT))
                    {
                        foreach (PhraseRect TPRect in PhraseRects) { TPRect.Clicked = false; TPRect.Selected = false; }

                        // And begin a drag process on that one item
                        if (e.Button == MouseButtons.Left)
                        {
                            MouseStart = e.Location;
                            StartingDrag = true;
                        }
                        PRect.Clicked = true;
                    }
                    PRect.Selected = true;
                } else // If the box was already selected
                {
                    if (WindowFunctions.IsPressed((int)WindowFunctions.VirtualKeyStates.VK_LSHIFT))
                    {
                        // If the user is holding shift, deselect the current box and do nothing else.
                        PRect.Selected = false;
                    }
                    else
                    {
                        // If not, then start a drag, which may be multi-drag.
                        PRect.Clicked = true;
                        if (e.Button == MouseButtons.Left)
                        {
                            MouseStart = e.Location;
                            StartingDrag = true;
                        }
                    }
                }

                // Pop the clicked phrase to the top (technically the bottom) of the stack
                PhraseRects.Remove(PRect);
                PhraseRects.Add(PRect);
            }
            else // There was no phrase under the mouse, start a bounding box
            {
                if (e.Button == MouseButtons.Left)
                {
                    MouseStart = e.Location;
                    MouseEnd = e.Location;
                    Marking = true;
                    BoundingBoxState = BoundingState.TooSmall;
                }
            }
            pbxDisplay.Invalidate();
        }

        // Finish drawing/dragging
        private void pbxDisplay_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) // Right click
            {
                // Summon a context menu
                SelectedRect = GetPhraseAtPoint(e.Location); // Find all selected phrases
                if (PhraseRects.FindAll(x => x.Selected == true).Count() < 1) return; // Don't display menu if nothing's selected
                ctxPhrase.Show(MousePosition); // Display menu
            } else // Left click
            {
                if (Marking == true) // We were drawing a bounding box
                {
                    // Create a phrase box
                    Rectangle TestRect = MouseStart.RectTo(MouseEnd);
                    if (TestRect.Width > 25 && TestRect.Height > 15)
                    {
                        ChangeState(State.translated);
                        PhraseRect NewPRect = new PhraseRect(TestRect, OCRResult, this, AsyncTranslation_callback);
                        PhraseRects.Add(NewPRect);
                    }
                    Marking = false;
                }
                else
                { // We were selecting/dragging
                    PhraseRect PRect = GetPhraseAtPoint(e.Location);
                    if (PRect != null)
                    {
                        PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.UpdateText(OCRResult, AsyncTranslation_callback));
                    }

                    PhraseRects.ForEach(x => x.Clicked = false);
                }

                if (!Dragging && !StartingDrag)
                {
                    // If the user wasn't holding shift, clear all other selections
                    if (!WindowFunctions.IsPressed((int)WindowFunctions.VirtualKeyStates.VK_LSHIFT))
                    {
                        PhraseRects.ForEach(x => { x.Clicked = false; x.Selected = false; });
                    }
                }
            }

            Dragging = false;
            StartingDrag = false;
            pbxDisplay.Invalidate();
        }

        
        private void pbxDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            if (StartingDrag == true) Dragging = true;

            if (Marking) // We're drawing a bounding box, set the second endpoint
            {
                MouseEnd = e.Location;

                // Draw the phrase box
                Rectangle TestRec = MouseStart.RectTo(MouseEnd);
                if (TestRec.Width < 25 || TestRec.Height < 15) // If it's too small, draw it red
                {
                    BoundingBoxState = BoundingState.TooSmall;
                } else if (CheckForText(TestRec))
                {
                    // TODO: This needs to be extended to understand the intersect vs bounding modes
                    BoundingBoxState = BoundingState.RectsFound; // If it's over any text, draw it green
                } else
                {
                    BoundingBoxState = BoundingState.Normal; // Otherwise draw it white
                }

                pbxDisplay.Invalidate();
            } else if (Dragging) { // We're dragging, move the selected bounding box
                int diffX = MouseStart.X - e.X;
                int diffY = MouseStart.Y - e.Y;
                PhraseRects.FindAll(x => x.Selected).ForEach(x => { x.Location.X -= diffX; x.Location.Y -= diffY; });
                MouseStart = e.Location;
                pbxDisplay.Invalidate();
            } else { // Just highlight whatever box the mouse is over
                foreach(PhraseRect TPRect in PhraseRects) { TPRect.Hovered = false; } // Clear all phrase hover states
                PhraseRect PRect = GetPhraseAtPoint(e.Location); // Check if we're over a phrase
                if (PRect != null)
                {
                    PRect.Hovered = true; // Mark it as hovered
                }
                pbxDisplay.Invalidate();
            }
        }
        #endregion

        #region Context menu events
        private void copyTranslatedToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> cText = new List<string>();
            foreach (PhraseRect PRect in PhraseRects.FindAll(x => x.Selected == true))
            {
                cText.Add(PRect.atrans.translatedText);
            }
            Clipboard.SetText(String.Join("\n\n", cText));
        }

        private void copyOriginalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> cText = new List<string>();
            foreach(PhraseRect PRect in PhraseRects.FindAll(x => x.Selected == true))
            {
                cText.Add(PRect.atrans.rawText);
            }
            Clipboard.SetText(String.Join("\n\n", cText));
        }

        private void copyBothToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<string> cText = new List<string>();
            foreach (PhraseRect PRect in PhraseRects.FindAll(x => x.Selected == true))
            {
                cText.Add("[" + PRect.atrans.rawText + "] " + PRect.atrans.translatedText);
            }
            Clipboard.SetText(String.Join("\n\n", cText));
        }

        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.RemoveAll(x => x.Selected == true);
            pbxDisplay.Invalidate();
        }

        private void tsmiBreakLines_Click(object sender, EventArgs e)
        {
            SelectedRect.breakLines = !tsmiBreakLines.Checked;
            SelectedRect.UpdateText(OCRResult, AsyncTranslation_callback);
            pbxDisplay.Invalidate();
        }

        private void ctxPhrase_Opening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If the mouse cursor wasn't over a phrase, disable some phrase-specific options
            if(SelectedRect == null)
            {
                alignToolStripMenuItem.Enabled = false;
                fitToolStripMenuItem.Enabled = false;
                tsmiBreakLines.Enabled = false;
            } else
            {
                alignToolStripMenuItem.Enabled = true;
                fitToolStripMenuItem.Enabled = true;
                tsmiBreakLines.Enabled = true;
                tsmiBreakLines.Checked = SelectedRect.breakLines;
            }
        }

        private void alignLeftEdgesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.X = SelectedRect.Location.X);
            pbxDisplay.Invalidate();
        }

        private void topEdgesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.Y = SelectedRect.Location.Y);
            pbxDisplay.Invalidate();
        }

        private void rightEdgesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.X = (SelectedRect.Location.X + SelectedRect.Location.Width) - x.Location.Width);
            pbxDisplay.Invalidate();
        }

        private void bottomEdgesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.Y = (SelectedRect.Location.Y + SelectedRect.Location.Height) - x.Location.Height);
            pbxDisplay.Invalidate();
        }

        private void horizontalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.Width = SelectedRect.Location.Width);
            pbxDisplay.Invalidate();
        }

        private void verticalToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PhraseRects.FindAll(x => x.Selected == true).ForEach(x => x.Location.Height = SelectedRect.Location.Height);
            pbxDisplay.Invalidate();
        }

        private void verticallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int TopEdge = PhraseRects.FindAll(x => x.Selected == true).Min(x => x.Location.Y);
            int BottomEdge = PhraseRects.FindAll(x => x.Selected == true).Max(x => x.Location.Y + x.Location.Height);
            int Height = BottomEdge - TopEdge;
            IOrderedEnumerable<PhraseRect> PRects = PhraseRects.FindAll(x => x.Selected == true)
                .OrderBy(x => x.Location.Y);

            int Spacing = Height / PRects.Count();

            for (int x = 1; x < PRects.Count() - 1; x++)
            {
                PhraseRect TRect = PRects.ElementAt(x);
                TRect.Location.Y = TopEdge + (Spacing * (x)) + (TRect.Location.Height / 2);
            }
        }

        private void horizontallyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            int LeftEdge = PhraseRects.FindAll(x => x.Selected == true).Min(x => x.Location.X);
            int RightEdge = PhraseRects.FindAll(x => x.Selected == true).Max(x => x.Location.X + x.Location.Width);
            int Width = RightEdge - LeftEdge;
            IOrderedEnumerable<PhraseRect> PRects = PhraseRects.FindAll(x => x.Selected == true)
                .OrderBy(x => x.Location.X);

            int Spacing = Width / PRects.Count();

            for (int x = 1; x < PRects.Count() - 1; x++)
            {
                PhraseRect TRect = PRects.ElementAt(x);
                TRect.Location.X = LeftEdge + (Spacing * (x));
            }
        }
        #endregion


        private void pbxDisplay_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Experimental approach for creating a text box when you double click a phrase, to make copying
            // part of a translation easier. It has problems right now.
            /*
            PhraseRect PRect = GetPhraseAtPoint(e.Location);
            if (PRect != null)
            {
                TextBox TBox = new TextBox();
                TBox.Text = PRect.atrans.translatedText;
                TBox.Multiline = true;
                Point ScreenPoint = pbxDisplay.PointToScreen(PRect.Location.Location);
                TBox.Location = this.PointToClient(ScreenPoint);
                TBox.Size = PRect.Location.Size;
                TBox.LostFocus += delegate (object ssender, EventArgs ee) { ((Control)sender).Dispose(); };
                this.Controls.Add(TBox);
                TBox.CreateControl();
                TBox.Show();
                TBox.BringToFront();
            }*/
        }

        private void frmBabel_FormClosing(object sender, FormClosingEventArgs e)
        {
            DebugLog.Log("Babel exiting.");
            DebugLog.Log("Odometer: " + SnapsTaken + "/" + CharsTranslated);
            DebugLog.Log("==============================================");
        }

        private void tsbClear_Click(object sender, EventArgs e)
        {
            OCRResult = null;
            PhraseRects.Clear();
            pbxDisplay.Image = snap = edit = null;
            ChangeState(State.ready);
        }

        private void toolStripButton1_Click_1(object sender, EventArgs e)
        {

        }
    }
}
