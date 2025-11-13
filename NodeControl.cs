using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace browse_nodes
{
    public class NodeControl : Panel
    {
        private TextBox textBox;
        private ContextMenuStrip contextMenu;
        private bool dragging;
        private Point dragOffset;

        // resizing (no visible corner panels)
        private const int GripSize = 10;
        private enum GripType { None, Top, Bottom, Left, Right, TopLeft, TopRight, BottomLeft, BottomRight }
        private GripType currentGripType = GripType.None;
        private bool resizing;
        private Point resizeStartMouse; // screen coords
        private Rectangle resizeStartBounds;
        private readonly Size minSize = new Size(80, 40);

    public event EventHandler? LinkInitiated;
    public event EventHandler? NodeClicked;
    public event EventHandler? PositionChanged;

        // selection visual
        private SolidBrush selectBrush = new SolidBrush(Color.FromArgb(100, Color.Blue));
        private bool isSelected = false;

        public NodeControl(string text = "Node")
        {
            Size = new Size(140, 90);
            MinimumSize = minSize;
            BorderStyle = BorderStyle.FixedSingle;

            // enable double buffering for smoother rendering
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();

            // notes should be white
            BackColor = Color.White;

            Padding = new Padding(4);

            textBox = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Text = text,
                ReadOnly = false,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White, // white note background
                ForeColor = Color.Black, // ensure readable text
                Font = new Font("Segoe UI", 10F) // slightly larger font
            };
            Controls.Add(textBox);

            // improve text rendering (smoothing via larger font + ClearType where possible)
            textBox.Font = new Font(textBox.Font.FontFamily, textBox.Font.Size, FontStyle.Regular);

            // forward mouse events from textbox so dragging/resizing works when clicking text area
            textBox.MouseDown += (s, e) => OnMouseDown(e);
            textBox.MouseMove += (s, e) => OnMouseMove(e);
            textBox.MouseUp += (s, e) => OnMouseUp(e);
            textBox.Click += (s, e) => OnClick(e);

            contextMenu = new ContextMenuStrip();
            var editItem = new ToolStripMenuItem("Edit Text");
            var linkItem = new ToolStripMenuItem("Link to Node");
            var removeItem = new ToolStripMenuItem("Remove Node");

            editItem.Click += (s, e) =>
            {
                textBox.Focus();
                textBox.SelectAll();
            };

            linkItem.Click += (s, e) =>
            {
                LinkInitiated?.Invoke(this, EventArgs.Empty);
            };

            removeItem.Click += (s, e) =>
            {
                var parent = this.Parent as Control;
                if (parent != null)
                {
                    parent.Controls.Remove(this);
                    PositionChanged?.Invoke(this, EventArgs.Empty);
                }
            };

            contextMenu.Items.Add(editItem);
            contextMenu.Items.Add(linkItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(removeItem);

            this.ContextMenuStrip = contextMenu;
        }

        // create a rounded region for nicer corners
        private void UpdateRoundedRegion()
        {
            int radius = 8;
            var rect = new Rectangle(0, 0, this.Width, this.Height);
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
            path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
            path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
            path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
            path.CloseFigure();
            this.Region = new Region(path);
        }

        private GripType GetGripAt(Point localPt)
        {
            var w = Width;
            var h = Height;
            var halfX = (w - GripSize) / 2;
            var halfY = (h - GripSize) / 2;

            var rectTop = new Rectangle(halfX, 0, GripSize, GripSize);
            var rectBottom = new Rectangle(halfX, h - GripSize, GripSize, GripSize);
            var rectLeft = new Rectangle(0, halfY, GripSize, GripSize);
            var rectRight = new Rectangle(w - GripSize, halfY, GripSize, GripSize);

            var rectTopLeft = new Rectangle(0, 0, GripSize, GripSize);
            var rectTopRight = new Rectangle(w - GripSize, 0, GripSize, GripSize);
            var rectBottomLeft = new Rectangle(0, h - GripSize, GripSize, GripSize);
            var rectBottomRight = new Rectangle(w - GripSize, h - GripSize, GripSize, GripSize);

            if (rectTopLeft.Contains(localPt)) return GripType.TopLeft;
            if (rectTopRight.Contains(localPt)) return GripType.TopRight;
            if (rectBottomLeft.Contains(localPt)) return GripType.BottomLeft;
            if (rectBottomRight.Contains(localPt)) return GripType.BottomRight;
            if (rectTop.Contains(localPt)) return GripType.Top;
            if (rectBottom.Contains(localPt)) return GripType.Bottom;
            if (rectLeft.Contains(localPt)) return GripType.Left;
            if (rectRight.Contains(localPt)) return GripType.Right;
            return GripType.None;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            var grip = GetGripAt(e.Location);
            if (grip != GripType.None)
            {
                currentGripType = grip;
                resizing = true;
                resizeStartMouse = Cursor.Position;
                resizeStartBounds = this.Bounds;
                this.BringToFront();
                return;
            }

            if (!resizing)
            {
                dragging = true;
                dragOffset = e.Location;
                this.BringToFront();
                Cursor = Cursors.SizeAll;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (resizing && this.Parent != null)
            {
                var delta = new Point(Cursor.Position.X - resizeStartMouse.X, Cursor.Position.Y - resizeStartMouse.Y);
                int newX = resizeStartBounds.X;
                int newY = resizeStartBounds.Y;
                int newW = resizeStartBounds.Width;
                int newH = resizeStartBounds.Height;

                bool left = currentGripType == GripType.Left || currentGripType == GripType.TopLeft || currentGripType == GripType.BottomLeft;
                bool right = currentGripType == GripType.Right || currentGripType == GripType.TopRight || currentGripType == GripType.BottomRight;
                bool top = currentGripType == GripType.Top || currentGripType == GripType.TopLeft || currentGripType == GripType.TopRight;
                bool bottom = currentGripType == GripType.Bottom || currentGripType == GripType.BottomLeft || currentGripType == GripType.BottomRight;

                if (right)
                    newW = Math.Max(minSize.Width, resizeStartBounds.Width + delta.X);
                if (bottom)
                    newH = Math.Max(minSize.Height, resizeStartBounds.Height + delta.Y);
                if (left)
                {
                    newX = resizeStartBounds.X + delta.X;
                    newW = Math.Max(minSize.Width, resizeStartBounds.Width - delta.X);
                    if (newW == minSize.Width)
                        newX = resizeStartBounds.Right - newW;
                }
                if (top)
                {
                    newY = resizeStartBounds.Y + delta.Y;
                    newH = Math.Max(minSize.Height, resizeStartBounds.Height - delta.Y);
                    if (newH == minSize.Height)
                        newY = resizeStartBounds.Bottom - newH;
                }

                var parent = this.Parent;
                newX = Math.Max(0, Math.Min(newX, parent.ClientSize.Width - newW));
                newY = Math.Max(0, Math.Min(newY, parent.ClientSize.Height - newH));
                newW = Math.Max(minSize.Width, Math.Min(newW, parent.ClientSize.Width - newX));
                newH = Math.Max(minSize.Height, Math.Min(newH, parent.ClientSize.Height - newY));

                this.Bounds = new Rectangle(newX, newY, newW, newH);
                PositionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (dragging && !resizing && this.Parent != null)
            {
                var parent = this.Parent;
                var cursorScreen = Cursor.Position;
                var parentClient = parent.PointToClient(cursorScreen);
                var newLocation = new Point(parentClient.X - dragOffset.X, parentClient.Y - dragOffset.Y);

                newLocation.X = Math.Max(0, Math.Min(newLocation.X, parent.ClientSize.Width - this.Width));
                newLocation.Y = Math.Max(0, Math.Min(newLocation.Y, parent.ClientSize.Height - this.Height));

                this.Location = newLocation;
                PositionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var hoverGrip = GetGripAt(e.Location);
            switch (hoverGrip)
            {
                case GripType.Top:
                case GripType.Bottom:
                    Cursor = Cursors.SizeNS;
                    break;
                case GripType.Left:
                case GripType.Right:
                    Cursor = Cursors.SizeWE;
                    break;
                case GripType.TopLeft:
                case GripType.BottomRight:
                    Cursor = Cursors.SizeNWSE;
                    break;
                case GripType.TopRight:
                case GripType.BottomLeft:
                    Cursor = Cursors.SizeNESW;
                    break;
                default:
                    Cursor = Cursors.Default;
                    break;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (resizing)
            {
                resizing = false;
                currentGripType = GripType.None;
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
            if (dragging)
            {
                dragging = false;
                Cursor = Cursors.Default;
                PositionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            NodeClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            PositionChanged?.Invoke(this, EventArgs.Empty);
            UpdateRoundedRegion();
        }

        public PointF GetCenter()
        {
            return new PointF(Location.X + Width / 2f, Location.Y + Height / 2f);
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string NodeText
        {
            get { return textBox.Text; }
            set { textBox.Text = value; }
        }

        public void SetSelected(bool selected)
        {
            isSelected = selected;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // paint background with rounded corners
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(this.BackColor))
            {
                e.Graphics.FillRectangle(brush, this.ClientRectangle);
            }
            // still call base for child controls
            // base.OnPaintBackground(e);
            if (isSelected)
            {
                e.Graphics.FillRectangle(selectBrush, ClientRectangle);
            }
        }
    }
}