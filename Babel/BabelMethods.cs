﻿using Babel.Windows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Configuration;
using Babel.Async;

namespace Babel
{
    public partial class frmBabel : Form
    {

        // Contains a rectangle the user has drawn around one or more words to be translated as a single phrase
        // Put this in another file
        public class PhraseRect
        {
            public Rectangle Location;
            public IAsyncTranslation atrans;
            public bool Hovered;
            public bool Clicked;
            public bool Selected;

            public PhraseRectMode mode;
            public bool breakLines = false;

            public frmBabel BabelForm;

            public PhraseRect(Rectangle Location, IAsyncOCR OCRResult, frmBabel BabelForm, TranslationCallback callback = null)
                : this(Location, OCRResult, NewPhraseMode, BabelForm, callback)
            {
                // nothing to do
            }

            public PhraseRect(Rectangle Location, IAsyncOCR OCRResult, PhraseRectMode Mode, frmBabel BabelForm, TranslationCallback callback = null)
            {
                this.Location = Location;

                this.BabelForm = BabelForm;

                this.mode = Mode;
                if (Autofit) DoAutoFit(OCRResult);
                UpdateText(OCRResult, callback);
            }

            public void DoAutoFit(IAsyncOCR OCRResult)
            {
                if (OCRResult != null)
                {
                    IEnumerable<OCRBox> FitRects = GetBoxes(OCRResult);
                    if (FitRects.Count() > 0)
                        Location = FitRects.Select(box => box.rect).FitRect();
                }
            }

            public void UpdateText(IAsyncOCR OCRResult, TranslationCallback callback = null)
            {
                // Only reevaluate if the underlying text actually changed
                if (atrans == null || this.GetText(OCRResult) != this.atrans.rawText)
                {
                    string NewText = GetText(OCRResult);
                    BabelForm.Invoke(BabelForm.SafeIncrementOdometer, new object[] { 0, NewText.Length }); // Update odometer
                    atrans = AsyncStatic.MakeTranslation(NewText, callback);
                }
            }

            // Get the combined text content of all boxes under this rect
            private string GetText(IAsyncOCR OCRResult)
            {
                var myBoxes = GetBoxes(OCRResult);
                if (myBoxes.Any())
                {
                    var myRects = AutoPhraseLineRects(myBoxes)
                        .OrderBy(box =>box.Top);
                    var myTexts = myRects
                        .Select(rect => GetBoxesInRect(rect, myBoxes, PhraseRectMode.contains))
                        .Select(boxenum => boxenum
                            .Select(box => box.text)
                            .Aggregate((l, r) => l + " " + r));
                    if (breakLines)
                        return myTexts.Aggregate((l, r) => l + Environment.NewLine + r);
                    else
                        return myTexts.Aggregate((l, r) => l + " " + r);
                }
                else
                {
                    return "";
                }
            }

            // The static functions don't really need to be in here, they're just utilities - but since this class is the only thing that uses them...
            private static string GetTextInBoxes(IEnumerable<OCRBox> boxes)
            {
                return boxes
                    .OrderBy(box => box.rect.Left)
                    .Select(box => box.text)
                    .Aggregate((l, r) => l + " " + r);
            }

            private static IEnumerable<OCRBox> GetBoxesInRect(Rectangle rect, IEnumerable<OCRBox> boxes, PhraseRectMode mode)
            {
                switch (mode)
                {
                    default:
                    case PhraseRectMode.intersects:
                        return boxes.Where(box => rect.IntersectsWith(box.rect));
                    case PhraseRectMode.contains:
                        return boxes.Where(box => rect.Contains(box.rect));
                }
            }

            IEnumerable<OCRBox> GetBoxes(IEnumerable<OCRBox> boxes)
            {
                return GetBoxesInRect(Location, boxes, mode);
            }

            IEnumerable<OCRBox> GetBoxes(IAsyncOCR ocrResult)
            {
                if (ocrResult == null) return null;
                return GetBoxes(ocrResult.smallBoxes);
            }

            public Font FitFont(Graphics g, Font originalFont, int maxFontSize, int minFontSize, bool smallestOnFail = true)
            {
                Font testFont = null;
                string targetString;
                if (atrans.isDone)
                    targetString = atrans.translatedText;
                else
                    targetString = atrans.rawText;

                // We utilize MeasureString which we get via a control instance           
                for (int adjustedSize = maxFontSize; adjustedSize >= minFontSize; adjustedSize--)
                {
                    testFont = new Font(originalFont.Name, adjustedSize, originalFont.Style);

                    // Test the string with the new size
                    SizeF adjustedSizeNew;
                    if (breakLines)
                        adjustedSizeNew = g.MeasureString(targetString, testFont);
                    else
                        adjustedSizeNew = g.MeasureString(targetString, testFont, Location.Width);

                    if (Location.Width > Convert.ToInt32(adjustedSizeNew.Width) &&
                        Location.Height > Convert.ToInt32(adjustedSizeNew.Height))
                    {
                        // Good font, return it
                        return testFont;
                    }
                }

                // If you get here there was no fontsize that worked
                // return minimumSize or original?
                if (smallestOnFail)
                {
                    return testFont;
                }
                else
                {
                    return originalFont;
                }
            }
        }

        public static string AppVersion()
        {
            return (String.Join(".", Application.ProductVersion.Split('.').Take(2)));
        }

        // Keeps all our buttons enabled/disabled as needed
        private void ChangeState(State newState)
        {
            AppState = newState;
            switch (newState)
            {
                case State.ready:
                    Text = "Babel " + AppVersion() + " - Ready";
                    tsbRevert.Enabled = false;
                    tsbSave.Enabled = false;
                    tsbOCR.Enabled = false;
                    tsbAutophrase.Enabled = false;
                    break;

                case State.snapped:
                    Text = "Babel " + AppVersion() + " - Captured";
                    if (!tsbAutoOCR.Checked) tsbOCR.Enabled = true;
                    tsbAutophrase.Enabled = false;
                    tsbOCR.Enabled = true;
                    tsbRevert.Enabled = false;
                    break;

                case State.OCRing:
                    Text = "Babel " + AppVersion() + " - Recognizing...";
                    tsbRevert.Enabled = false;
                    tsbSave.Enabled = false;
                    tsbOCR.Enabled = false;
                    tsbAutophrase.Enabled = false;
                    break;

                case State.OCRed:
                    Text = "Babel " + AppVersion() + " - Select text";
                    tsbRevert.Enabled = false;
                    tsbSave.Enabled = true;
                    tsbOCR.Enabled = false;
                    tsbAutophrase.Enabled = true;
                    break;

                case State.translating:
                    Text = "Babel " + AppVersion() + " - Translating...";
                    tsbRevert.Enabled = false;
                    tsbSave.Enabled = false;
                    break;

                case State.translated:
                    Text = "Babel " + AppVersion() + " - Translated";
                    tsbRevert.Enabled = true;
                    tsbSave.Enabled = true;
                    break;
            }
            SaveForStreaming();
        }

        // Called whenever settings should be reevaluated
        void LoadSettings()
        {
            Properties.Settings.Default.Reload();
            SnapsTaken = Properties.Settings.Default.snapsTaken;
            CharsTranslated = Properties.Settings.Default.charsTranslated;
            UpdateOdometer();
            var path = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath;
            DebugLog.Log("Settings loaded from " + path);
            //tsbOCR.Enabled = !Properties.Settings.Default.autoOCR;
            //tsbAutoOCR.Checked = Properties.Settings.Default.autoOCR;

        }

        // Update odometer reading in statusbar
        public void UpdateOdometer()
        {
            string odoReading = SnapsTaken.ToString() + " / " + CharsTranslated.ToString();
            statusBarRight.Text = odoReading;
            statusBarRight.ToolTipText = "OCR requests: " + SnapsTaken.ToString() + "\nChars translated: " + CharsTranslated.ToString();
        }

        // Increment odometer, then save and update
        public delegate void SafeIncrementOdometer_Delegate(int snaps, int chars);
        public SafeIncrementOdometer_Delegate SafeIncrementOdometer;
        public void IncrementOdometer(int snaps, int chars)
        {
            if (InvokeRequired)
            {
                Invoke(SafeIncrementOdometer, new object[] { snaps, chars });
            }
            else
            {
                if (Properties.Settings.Default.OCRDataSource != DataSource.Dummy) // Don't increment odometer if we're not really sending requests
                {
                    SnapsTaken += snaps;
                    CharsTranslated += chars;
                    Properties.Settings.Default.snapsTaken = SnapsTaken;
                    Properties.Settings.Default.charsTranslated = CharsTranslated;
                    Properties.Settings.Default.Save();
                    DebugLog.Log("Odometer += " + snaps.ToString() + "/" + chars.ToString());
                }
                UpdateOdometer(); // Leave this outside the if, otherwise we might never get an odo reading even on program start
            }
        }
      

        // Clear all phrases
        void ClearPhrases()
        {
            PhraseRects.Clear();
            pbxDisplay.Invalidate();
        }

        // Clear everything to prep for another snap
        void ClearAll()
        {
            OCRResult = null;
            PhraseRects.Clear();
            pbxDisplay.Invalidate();
        }

        #region Image capture routines
        // Takes a screenshot of the SnapRegion and returns it
        private Image Snap()
        {
            bool VfwWasVisible = vfw.Visible;
            if (VfwWasVisible) vfw.Visible = false; // Hide viewfinder if appropriate
            this.Visible = false; // Hide self (nobody wants to translate Babel)

            try
            {
                if ((int) TrackingWindow != 0)
                {
                    // Put this into a general function later
                    Rectangle WindowLoc = WindowFunctions.GetRectFromHwnd(TrackingWindow);
                    vfw.Location = new Point(WindowLoc.Left, WindowLoc.Top);
                    vfw.Size = new Size(WindowLoc.Width, WindowLoc.Height);
                    vfw.Flicker();
                }

                Image result = GDI32.Grab(SnapRegion);

                if (VfwWasVisible) vfw.Visible = true; // Reshow viewfinder if appropriate
                this.Visible = true; // Show self again
                this.Focus(); // Return focus to the main form

                DebugLog.Log("Took snap");

                return result;
            } catch (Exception ex)
            {
                DebugLog.Log(ex.Message);
                return null;
            }
        }

        // All ingest methods call this with an image to take a new snap
        private void GetSnap(Image image)
        {
            ClearAll();

            pbxDisplay.Image = edit = snap = image.Copy();
            pbxDisplay.Visible = true;
            txtPlaceholder.Visible = false;
            ChangeState(State.snapped);
            DoOCR(true); // Trigger AutoOCR, if enabled.
        }

        // Display or hide the viewfinder
        private void ToggleVFW()
        {
            if (!vfw.Visible)
            {
                vfw.Show();
            }
            else
            {
                vfw.Hide();
            }
        }
        #endregion

        #region Image recognition routines
        // Trigger OCR recognition if appropriate
        bool DoOCR(bool Auto = false)
        {
            if (AppState != State.snapped) return false; // Prevent double OCR

            // Proceed if it's a manual request, or if it's an auto request and autoOCR is on
            if (!Auto || AutoOCR) //Properties.Settings.Default.autoOCR)
            {
                ChangeState(State.OCRing);
                IncrementOdometer(1, 0); // Add one snap to the odometer
                OCRResult = AsyncStatic.MakeOCR(snap, AsyncOCR_callback);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void AsyncTranslation_callback(IAsyncTranslation result)
        {
            if(PhraseRects.All(x => x.atrans.isDone == true)) SaveForStreaming();
            pbxDisplay.Invalidate();
        }

        public class WorkerError
        {
            public string message;
            public string url;
            public bool read;
            public string timestamp;

            public WorkerError(string message, string url, string timestamp)
            {
                this.message = message;
                this.url = url;
                this.timestamp = timestamp;
                this.read = false;
            }
        }
        
        private static Action<string, string> SafeLogWorkerError = LogWorkerError;
        public static void LogWorkerError(string message, string url)
        {
            // Put an error in the queue
            WorkerErrors.Add(new WorkerError(message, url, DateTime.Now.ToString()));
            // Display worker error interface
            ErrorWindow.UpdateLog();
            ErrorWindow.ShowDialog();
        }
        
        public OCRCallback SafeAsyncOCR_Callback;
        public void AsyncOCR_callback(IAsyncOCR result)
        {
            if (InvokeRequired)
            {
                Invoke(SafeAsyncOCR_Callback, new object[] { result });
            }
            else
            {
                if (Auto_Autophrase) MakeAutoPhrases();

                ChangeState(State.OCRed);
                statusBarLeft.Text = "Recognition complete [" + result.timeStamp + " elapsed]";
                pbxDisplay.Invalidate();
            }
        }

        // Variant on the autophraser that doesn't care about horizontal spacing
        private static IEnumerable<Rectangle> AutoPhraseLineRects(IEnumerable<OCRBox> boxes)
        {
            // Put all smallboxes into a queue, left to right
            Queue<OCRBox> boxQueue = new Queue<OCRBox>(boxes.OrderBy(box => box.rect.Left));

            // We're planning to cut items out of this queue as we go,
            // so I don't think we can safely foreach over it
            while (boxQueue.Count > 0)
            {
                // Pick the current leftmost box, and start with its rect exactly.
                OCRBox firstBox = boxQueue.Dequeue();
                Rectangle growingRect = firstBox.rect;
                int charWidth = firstBox.CharWidth();

                List<OCRBox> phraseCandidates = new List<OCRBox> { firstBox };

                // While there are any boxes in the queue that are aligned with my current rect:
                while (boxQueue.Any(other => growingRect.IsOnSameLine(other.rect)))
                {
                    // Pick out the leftmost aligned box
                    OCRBox next = boxQueue.First(other => growingRect.IsOnSameLine(other.rect));

                    // Add it to our phrase
                    phraseCandidates.Add(next);

                    // Remake the queue without any elements from the phrase
                    // (this is the only way to delete from the middle of a queue)
                    boxQueue = new Queue<OCRBox>(boxQueue.Except(phraseCandidates));

                    // Expand the rect to include the new smallbox
                    growingRect = growingRect.Include(next.rect);
                }

                yield return growingRect;
            }
        }

        // Generate all the phraserects for a certain enumerable of OCRBoxes
        private static IEnumerable<Rectangle> AutoPhraseRects(IEnumerable<OCRBox> boxes)
        {
            // Put all smallboxes into a queue, left to right
            Queue<OCRBox> boxQueue = new Queue<OCRBox>(boxes.OrderBy(box => box.rect.Left));

            // We're planning to cut items out of this queue as we go,
            // so I don't think we can safely foreach over it
            while (boxQueue.Count > 0)
            {
                // Pick the current leftmost box, and start with its rect exactly.
                OCRBox firstBox = boxQueue.Dequeue();
                Rectangle growingRect = firstBox.rect;
                int charWidth = firstBox.CharWidth();

                List<OCRBox> phraseCandidates = new List<OCRBox> { firstBox };

                // While there are any boxes in the queue that are aligned with my current rect:
                while (boxQueue.Any(other => growingRect.CouldBeNextRect(other.rect, charWidth)))
                {
                    // Pick out the leftmost aligned box
                    OCRBox next = boxQueue.First(other => growingRect.CouldBeNextRect(other.rect, charWidth));

                    // Add it to our phrase
                    phraseCandidates.Add(next);

                    // Remake the queue without any elements from the phrase
                    // (this is the only way to delete from the middle of a queue)
                    boxQueue = new Queue<OCRBox>(boxQueue.Except(phraseCandidates));

                    // Expand the rect to include the new smallbox
                    growingRect = growingRect.Include(next.rect);
                }

                yield return growingRect;
            }
        }

        // Use the above code to generate phraserects
        public void MakeAutoPhrases()
        {
            if (PhraseRects.Count() > 0)
            {
                switch (MessageBox.Show("Do you want to clear phrases before running the autophraser?", "Existing phrases", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning))
                {
                    case (DialogResult.Yes):
                        PhraseRects.Clear();
                        break;
                    case (DialogResult.Cancel):
                        return;
                }
            }

            foreach (Rectangle rect in AutoPhraseRects(OCRResult.smallBoxes))
                PhraseRects.Add(new PhraseRect(rect, OCRResult, PhraseRectMode.contains, this, AsyncTranslation_callback));
        }
        #endregion

        #region Graphics routines
        // Draw all the graphics on top of the image
        // This is generalized so it can be used either by onpaint or by the image save routine
        private void DrawImage(Graphics g)
        {
            // Draw identified words
            if (OCRResult != null && OCRResult.isDone)
            {
                foreach (OCRBox ocr in OCRResult.smallBoxes)
                {
                    g.FillPolygon(new SolidBrush(Color.FromArgb(100, 128, 50, 128)), ocr.points);
                    g.DrawPolygon(new Pen(Color.Purple, 1.0f), ocr.points);
                }
            }

            // Draw user bounding box
            if (Marking)
            {
                Rectangle Rect = MouseStart.RectTo(MouseEnd);
                switch (BoundingBoxState)
                {
                    case BoundingState.Normal:
                        g.DrawRectangle(Pens.White, Rect);
                        break;
                    case BoundingState.RectsFound:
                        g.DrawRectangle(Pens.Green, Rect);
                        break;
                    case BoundingState.TooSmall:
                        g.DrawRectangle(Pens.Red, Rect);
                        break;
                }
                // Draw a black rectangle around the others to help with contrast
                Rect.Inflate(1, 1);
                g.DrawRectangle(Pens.Black, Rect);
            }


            // Draw phrases
            foreach (PhraseRect PRect in PhraseRects)
            {
                //Rectangle DisplayRect = QuantizeRect(PRect.Location, 8, 8); // This was quantizing the display location of the box, but this is a bad idea as it turns out and may not ever be practical

                g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), PRect.Location); // Background

                // Pick color for outline
                Pen BoxColor = Pens.Green;
                // The order of these statements is critical, leave them as-is
                if (PRect.Hovered) BoxColor = Pens.LightGreen;
                if (PRect.Selected) BoxColor = Pens.LightBlue;
                if (PRect.Clicked) BoxColor = Pens.DarkBlue;
                BoxColor = (Pen)BoxColor.Clone(); // Clone the pen prototype so we can modify it if need be
                // If the box is an intersect box, draw it dashed
                if (PRect.mode == PhraseRectMode.intersects)
                {
                    BoxColor.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                    //BoxColor.DashStyle = System.Drawing.Drawing2D.DashStyle.Custom;
                    //BoxColor.DashPattern = new float[] { 5, 2, 15, 4 };
                }
                g.DrawRectangle(BoxColor, PRect.Location); // Draw outline

                string TextToRender = PRect.atrans.rawText;
                Brush ColorToRender = Brushes.Gray;
                if (PRect.atrans.isDone)
                {
                    // Draw translated text if available
                    TextToRender = PRect.atrans.translatedText;
                    ColorToRender = Brushes.White;
                }

                // Draw text

                // Fit font to bounding box
                //Font LargeFont = GetAdjustedFont(g, TextToRender, DefaultFont, PRect.Location, 32, 6, true);
                Font LargeFont = PRect.FitFont(g, DefaultFont, 32, 6);

                // Center-justify text
                // TODO: Currently disabled to enable wordwrap, fix this
                int JustifySpace = (int)(PRect.Location.Width - g.MeasureString(TextToRender, LargeFont).Width) / 2;
                Rectangle AdjustedPosition = new Rectangle(
                    PRect.Location.Left + JustifySpace, PRect.Location.Top,
                    PRect.Location.Width, PRect.Location.Height);

                // Draw translated text
                g.DrawString(
                        TextToRender,
                        LargeFont,
                        ColorToRender,
                        PRect.Location);
                


                if (Properties.Settings.Default.displayTimes)
                {
                    // Draw the translation time
                    Font BoldFont = new Font(DefaultFont, FontStyle.Bold);
                    SizeF TimeLength = g.MeasureString(PRect.atrans.timeStamp, BoldFont);

                    g.DrawString(PRect.atrans.timeStamp,
                        BoldFont,
                        Brushes.Gray,
                        new Point(PRect.Location.Right - (int)TimeLength.Width, PRect.Location.Bottom - (int)TimeLength.Height));
                }

                // Write name of translation service
                Font SvcFont = new Font(FontFamily.GenericSansSerif, 8.0f);
                SizeF SvcLength = g.MeasureString(PRect.atrans.name, SvcFont);
                g.DrawString(PRect.atrans.name, SvcFont, Brushes.Gray, 
                    new Point(PRect.Location.Right - (int)SvcLength.Width, PRect.Location.Top));
            }

            if (OCRResult != null)
            {
                // Write name of OCR service
                Font OCRFont = new Font(FontFamily.GenericSansSerif, 8.0f);
                SizeF OCRLength = g.MeasureString(OCRResult.name, OCRFont);
                g.DrawString(OCRResult.name, OCRFont, Brushes.Gray,
                    new Point((int)(g.ClipBounds.Width - OCRLength.Width), 0));
            }
        }


        // Special case of drawimage intended only to draw text for overlay in OBS
        private void DrawOBS(Graphics g)
        {
            // Draw phrases
            foreach (PhraseRect PRect in PhraseRects)
            {

                g.FillRectangle(new SolidBrush(Color.FromArgb(180, 0, 0, 0)), PRect.Location); // Background

                string TextToRender = PRect.atrans.rawText;
                Brush ColorToRender = Brushes.Gray;
                if (PRect.atrans.isDone)
                {
                    // Draw translated text if available
                    TextToRender = PRect.atrans.translatedText;
                    ColorToRender = Brushes.White;
                }

                // Draw text

                // Fit font to bounding box
                //Font LargeFont = GetAdjustedFont(g, TextToRender, DefaultFont, PRect.Location, 32, 6, true);
                Font LargeFont = PRect.FitFont(g, DefaultFont, 32, 6);

                // Center-justify text
                // TODO: Currently disabled to enable wordwrap, fix this
                int JustifySpace = (int)(PRect.Location.Width - g.MeasureString(TextToRender, LargeFont).Width) / 2;
                Rectangle AdjustedPosition = new Rectangle(
                    PRect.Location.Left + JustifySpace, PRect.Location.Top,
                    PRect.Location.Width, PRect.Location.Height);

                // Draw translated text
                g.DrawString(
                        TextToRender,
                        LargeFont,
                        ColorToRender,
                        PRect.Location);
            }
        }


        // Find the biggest font to fit a given rect
        public Font GetAdjustedFont(Graphics g, string graphicString, Font originalFont, Rectangle Container, int maxFontSize, int minFontSize, bool smallestOnFail)
        {
            Font testFont = null;
            // We utilize MeasureString which we get via a control instance           
            for (int adjustedSize = maxFontSize; adjustedSize >= minFontSize; adjustedSize--)
            {
                testFont = new Font(originalFont.Name, adjustedSize, originalFont.Style);

                // Test the string with the new size
                SizeF adjustedSizeNew = g.MeasureString(graphicString, testFont, Container.Width);

                if (Container.Width > Convert.ToInt32(adjustedSizeNew.Width) &&
                    Container.Height > Convert.ToInt32(adjustedSizeNew.Height))
                {
                    // Good font, return it
                    return testFont;
                }
            }

            // If you get here there was no fontsize that worked
            // return minimumSize or original?
            if (smallestOnFail)
            {
                return testFont;
            }
            else
            {
                return originalFont;
            }
        }

        public Font WrapText(Graphics g, string graphicString, Font originalFont, Rectangle Container, int maxFontSize, int minFontSize, bool smallestOnFail)
        {
            Font testFont = null;
            // We utilize MeasureString which we get via a control instance           
            for (int adjustedSize = maxFontSize; adjustedSize >= minFontSize; adjustedSize--)
            {
                testFont = new Font(originalFont.Name, adjustedSize, originalFont.Style);

                // Test the string with the new size
                SizeF adjustedSizeNew = g.MeasureString(graphicString, testFont, Container.Width);

                if (Container.Width > Convert.ToInt32(adjustedSizeNew.Width) &&
                    Container.Height > Convert.ToInt32(adjustedSizeNew.Height)
                    )
                {
                    // Good font, return it
                    return testFont;
                }
            }

            // If you get here there was no fontsize that worked
            // return minimumSize or original?
            if (smallestOnFail)
            {
                return testFont;
            }
            else
            {
                return originalFont;
            }
        }
        #endregion

        #region Helper functions

        // Find a phrase at a given point, for mouse collision etc.
        PhraseRect GetPhraseAtPoint(Point Location)
        {
            // It's important to select the last occurrance in the list, since that'll be the one most recently
            // added or clicked, thus it's on the top of the Z stack.
            if (PhraseRects.Count() < 1) return null;
            List<PhraseRect> FoundRects = PhraseRects.FindAll(x => x.Location.Contains(Location));
            if (FoundRects.Count() < 1) return null;
            return FoundRects.Last();
        }

        // Check whether there are any text boxes underneath this rect
        private bool CheckForText(Rectangle rect)
        {
            if (OCRResult == null) return false;
            return OCRResult.smallBoxes
                .Where(ocr => ocr.rect.IntersectsWith(rect)).Count() > 0;
        }

        // Get the largest difference between coords in a pair of points
        int GetPointDiff(Point p1, Point p2)
        {
            int xdiff = Math.Abs(p1.X - p2.X);
            int ydiff = Math.Abs(p1.Y - p2.Y);
            if (xdiff > ydiff) { return xdiff; } else { return ydiff; }
        }

        // Get the largest dimension of a rectangle
        int GetRectMax(Rectangle Rect)
        {
            if (Rect.Width > Rect.Height) { return Rect.Width; } else { return Rect.Height; }
        }

        // Round a number to its closest multiple
        int roundToMultiple(int d, int multiple)
        {
            return (int)Math.Round((double)(d / multiple)) * multiple;
        }
        Rectangle QuantizeRect(Rectangle Rect, int quantX, int quantY)
        {
            return new Rectangle(roundToMultiple(Rect.X, quantX),
                                roundToMultiple(Rect.Y, quantY),
                                roundToMultiple(Rect.Width, quantX),
                                roundToMultiple(Rect.Height, quantY));
        }
        #endregion
    }

    // Image extension methods to make various things better.
    static class BitmapExt
    {
        public static Image Copy(this Image original)
        {
            Image copy = new Bitmap(original.Width, original.Height);
            Graphics.FromImage(copy).DrawImage(original, 0, 0);
            return copy;
        }
    }
}