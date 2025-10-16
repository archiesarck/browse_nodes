using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace browse_nodes
{
    public class MainForm : Form
    {
    private Panel? canvas;
    private List<NodeControl> nodes = new List<NodeControl>();
    private List<Link> links = new List<Link>();
    private NodeControl? linkingSource;

        // keyboard linking state
        private bool keyboardLinkingMode = false;
    private NodeControl? keyboardLinkFirst = null;
    // delete mode state
    private bool deleteMode = false;
        private string originalTitle;
    private Label? helpLabelRef = null;

        public MainForm()
        {
            Text = "browse_nodes";
            originalTitle = Text;
            KeyPreview = true;
            KeyDown += MainForm_KeyDown;

            Size = new Size(480 * 4, 320 * 4);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Controls.Clear();

            // set main window background to dark grey
            this.BackColor = Color.FromArgb(45, 45, 48);

            // small help panel at the top with keyboard shortcuts
            var helpPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(60, 60, 64)
            };
            var helpLabel = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                Text = "Shortcuts: Ctrl+N = New node, Ctrl+L = Link nodes (select parent then child), Esc = Cancel linking"
            };
            helpLabelRef = helpLabel;
            helpPanel.Controls.Add(helpLabel);
            Controls.Add(helpPanel);

            // canvas where nodes live and links are drawn behind nodes
            canvas = new Panel
            {
                Location = new Point(0, 40),
                Size = new Size(ClientSize.Width, ClientSize.Height - 40),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(45, 45, 48), // dark grey canvas
            };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseClick += Canvas_MouseClick;
            Controls.Add(canvas);
        }

        private void Canvas_MouseClick(object? sender, MouseEventArgs e)
        {
            if (deleteMode && e.Button == MouseButtons.Left)
            {
                // check for node under cursor first
                if (canvas == null) return;
                var pt = e.Location;
                // iterate nodes (canvas.Controls contains NodeControl instances)
                NodeControl? hitNode = null;
                foreach (Control c in canvas.Controls)
                {
                    if (c is NodeControl nc)
                    {
                        if (nc.Bounds.Contains(pt)) { hitNode = nc; break; }
                    }
                }

                if (hitNode != null)
                {
                    // remove links referencing this node
                    links.RemoveAll(l => l.From == hitNode || l.To == hitNode);
                    canvas.Controls.Remove(hitNode);
                    nodes.Remove(hitNode);
                    canvas.Invalidate();
                    // if no nodes remain, exit delete mode so Esc will work and UI isn't stuck
                    if (nodes.Count == 0)
                    {
                        ExitDeleteMode();
                    }
                    return;
                }

                // otherwise check for link near click
                // simple hit-test: distance to segment < threshold
                const float thresh = 6f;
                for (int i = links.Count - 1; i >= 0; i--)
                {
                    var link = links[i];
                    if (link.From == null || link.To == null) continue;
                    var p1 = link.From.GetCenter();
                    var p2 = link.To.GetCenter();
                    var local = pt; // pt is already in canvas coords
                    if (DistancePointToSegment(local, p1, p2) <= thresh)
                    {
                        links.RemoveAt(i);
                        canvas.Invalidate();
                        break;
                    }
                }
            }
        }

        private float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            // from https://stackoverflow.com/questions/849211/shortest-distance-between-a-point-and-a-line-segment
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            if (dx == 0 && dy == 0)
            {
                dx = p.X - a.X;
                dy = p.Y - a.Y;
                return (float)Math.Sqrt(dx * dx + dy * dy);
            }
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0, Math.Min(1, t));
            float projX = a.X + t * dx;
            float projY = a.Y + t * dy;
            float distX = p.X - projX;
            float distY = p.Y - projY;
            return (float)Math.Sqrt(distX * distX + distY * distY);
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            // clicking empty canvas cancels linking mode
            if (e.Button == MouseButtons.Left && linkingSource != null)
            {
                linkingSource = null;
                Cursor = Cursors.Default;
                canvas?.Invalidate();
            }
        }

        private void Canvas_Paint(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // use a slightly dull white for links
            var linkColor = Color.FromArgb(220, 220, 220);
            using (var pen = new Pen(linkColor, 2))
            {
                foreach (var link in links)
                {
                    if (link.From != null && link.To != null)
                        DrawArrow(e.Graphics, pen, link.From.GetCenter(), link.To.GetCenter());
                }
            }

            // hint line when linking from context-menu initiated link
            if (linkingSource != null && canvas != null)
            {
                var p1 = linkingSource.GetCenter();
                var p2 = canvas.PointToClient(Cursor.Position);
                using (var pen = new Pen(Color.FromArgb(180, 180, 180), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                {
                    e.Graphics.DrawLine(pen, p1, p2);
                }
            }
        }

        private void DrawArrow(Graphics g, Pen pen, PointF p1, PointF p2)
        {
            g.DrawLine(pen, p1, p2);

            const float headSize = 10f;
            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len == 0) return;
            var ux = dx / len;
            var uy = dy / len;

            var left = new PointF(p2.X + ux * headSize - uy * (headSize / 2f),
                                  p2.Y + uy * headSize + ux * (headSize / 2f));
            var right = new PointF(p2.X + ux * headSize + uy * (headSize / 2f),
                                   p2.Y + uy * headSize - ux * (headSize / 2f));

            // fill arrowhead using the pen color (dull white)
            using (var brush = new SolidBrush(pen.Color))
            {
                g.FillPolygon(brush, new[] { p2, left, right });
            }
        }

        private void Node_PositionChanged(object? sender, EventArgs e)
        {
            canvas?.Invalidate();
        }

        private void Node_LinkInitiated(object? sender, EventArgs e)
        {
            linkingSource = sender as NodeControl;
            Cursor = Cursors.Cross;
            canvas?.Invalidate();
        }

        private void Node_NodeClicked(object? sender, EventArgs e)
        {
            var clicked = sender as NodeControl;
            if (clicked == null) return;

            // if in delete mode, remove the clicked node (this handles clicks that go to the node control)
            if (deleteMode)
            {
                // remove links referencing this node
                links.RemoveAll(l => l.From == clicked || l.To == clicked);
                if (canvas != null)
                {
                    canvas.Controls.Remove(clicked);
                }
                nodes.Remove(clicked);
                canvas?.Invalidate();
                // if no nodes remain, exit delete mode so Esc will work and UI isn't stuck
                if (nodes.Count == 0)
                {
                    ExitDeleteMode();
                }
                return;
            }

            // keyboard linking mode (Ctrl+L)
            if (keyboardLinkingMode)
            {
                if (keyboardLinkFirst == null)
                {
                    keyboardLinkFirst = clicked;
                    keyboardLinkFirst?.SetSelected(true);
                }
                else
                {
                    if (clicked != keyboardLinkFirst)
                    {
                        if (keyboardLinkFirst != null)
                            links.Add(new Link { From = keyboardLinkFirst, To = clicked });
                    }
                    keyboardLinkFirst?.SetSelected(false);
                    CancelKeyboardLinkMode();
                    canvas?.Invalidate();
                }
                return;
            }

            // context-menu linking
            if (linkingSource != null)
            {
                if (clicked != linkingSource)
                {
                    links.Add(new Link { From = linkingSource, To = clicked });
                }
                linkingSource = null;
                Cursor = Cursors.Default;
                canvas?.Invalidate();
            }
        }

        private void CancelKeyboardLinkMode()
        {
            keyboardLinkingMode = false;
            keyboardLinkFirst = null;
            Cursor = Cursors.Default;
            Text = originalTitle;
        }

        private void ExitDeleteMode()
        {
            deleteMode = false;
            Cursor = Cursors.Default;
            if (helpLabelRef != null)
                helpLabelRef.Text = "Shortcuts: Ctrl+N = New node, Ctrl+L = Link nodes (select parent then child), Esc = Cancel linking";
            // ensure keyboard focus returns to the main form/canvas so ProcessCmdKey/KeyPreview still receive shortcuts
            try
            {
                // bring the form to the foreground and clear any active child control
                this.Activate();
                this.ActiveControl = null;
                if (canvas != null)
                {
                    // prefer Select over Focus in some focus scenarios
                    canvas.Select();
                    canvas.Focus();
                }
            }
            catch
            {
                // best-effort; don't throw from UI cleanup
            }
        }

        private void MainForm_KeyDown(object? sender, KeyEventArgs e)
        {
            // Ctrl+N -> create new node
            if (e.Control && e.KeyCode == Keys.N)
            {
                AddNode();
                e.Handled = true;
                return;
            }

            // Ctrl+D -> delete mode
            if (e.Control && e.KeyCode == Keys.D)
            {
                deleteMode = true;
                Cursor = Cursors.No;
                if (helpLabelRef != null)
                    helpLabelRef.Text = "Delete mode: click nodes or links to delete. Esc to cancel.";
                e.Handled = true;
                return;
            }

            // Ctrl+L -> keyboard link mode
            if (e.Control && e.KeyCode == Keys.L)
            {
                keyboardLinkingMode = true;
                keyboardLinkFirst = null;
                Cursor = Cursors.Cross;
                Text = originalTitle + " [Link Mode: select parent then child]";
                e.Handled = true;
                return;
            }

            // Escape cancels linking or delete mode
            if (e.KeyCode == Keys.Escape)
            {
                if (keyboardLinkingMode)
                {
                    keyboardLinkFirst?.SetSelected(false);
                    CancelKeyboardLinkMode();
                    e.Handled = true;
                    return;
                }
                if (deleteMode)
                {
                    deleteMode = false;
                    Cursor = Cursors.Default;
                    if (helpLabelRef != null)
                        helpLabelRef.Text = "Shortcuts: Ctrl+N = New node, Ctrl+L = Link nodes (select parent then child), Esc = Cancel linking";
                    e.Handled = true;
                    return;
                }
            }
        }

        // Handle shortcuts at a low level so they work even if focus is on child controls
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl+N -> create new node
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.N)
            {
                AddNode();
                return true;
            }

            // Ctrl+D -> enter delete mode
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.D)
            {
                deleteMode = true;
                Cursor = Cursors.No;
                if (helpLabelRef != null)
                    helpLabelRef.Text = "Delete mode: click nodes or links to delete. Esc to cancel.";
                return true;
            }

            // Ctrl+L -> keyboard link mode
            if ((keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == Keys.L)
            {
                keyboardLinkingMode = true;
                keyboardLinkFirst = null;
                Cursor = Cursors.Cross;
                Text = originalTitle + " [Link Mode: select parent then child]";
                return true;
            }

            // Escape cancels linking or delete mode
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                if (keyboardLinkingMode)
                {
                    keyboardLinkFirst?.SetSelected(false);
                    CancelKeyboardLinkMode();
                    return true;
                }
                if (deleteMode)
                {
                    ExitDeleteMode();
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddNode()
        {
            string nodeText = Microsoft.VisualBasic.Interaction.InputBox("Enter node text:", "Add Node", "New Node");
            if (string.IsNullOrWhiteSpace(nodeText))
                return;

            var node = new NodeControl(nodeText);
            node.Location = new Point(40 + nodes.Count * 160, 80);
            node.LinkInitiated += Node_LinkInitiated;
            node.NodeClicked += Node_NodeClicked;
            node.PositionChanged += Node_PositionChanged;

            if (canvas != null)
            {
                canvas.Controls.Add(node);
            }
            nodes.Add(node);
            canvas?.Invalidate();
        }
    }
}