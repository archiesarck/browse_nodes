using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace browse_nodes
{
    /// <summary>
    /// A self-contained document control hosting a canvas with nodes and links.
    /// Each instance represents one opened file/tab.
    /// </summary>
    public class GraphDocumentControl : UserControl
    {
        private Panel canvas;
        private readonly List<NodeControl> nodes = new List<NodeControl>();
        private readonly List<Link> links = new List<Link>();
        private NodeControl? linkingSource;

        // keyboard linking state
        private bool keyboardLinkingMode = false;
        private NodeControl? keyboardLinkFirst = null;
        // delete mode state
        private bool deleteMode = false;

    public string? CurrentFilePath { get; private set; }
    // Optional display name for untitled docs, e.g., "Untitled 1". If null, we use file name or "Untitled".
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? DisplayName { get; set; }

        public GraphDocumentControl()
        {
            this.BackColor = Color.FromArgb(25, 25, 28);
            this.Dock = DockStyle.Fill;

            canvas = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(25, 25, 28)
            };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseClick += Canvas_MouseClick;
            canvas.ControlRemoved += Canvas_ControlRemoved;
            Controls.Add(canvas);

            // Double-buffering to reduce flicker
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            this.UpdateStyles();
        }

        public string GetSuggestedTabTitle()
        {
            if (!string.IsNullOrEmpty(DisplayName)) return DisplayName!;
            return string.IsNullOrEmpty(CurrentFilePath) ? "Untitled" : Path.GetFileName(CurrentFilePath);
        }

        public void NewBlank()
        {
            ClearDocument();
            CurrentFilePath = null;
        }

        public void LoadFromFile(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            string json = File.ReadAllText(path);
            var data = JsonSerializer.Deserialize<GraphData>(json);
            if (data == null) return;

            ClearDocument();

            // Create nodes
            foreach (var nd in data.Nodes)
            {
                var nc = CreateNode(nd.Text, new Rectangle(nd.X, nd.Y, nd.Width, nd.Height));
                canvas.Controls.Add(nc);
                nodes.Add(nc);
            }

            // Create links
            foreach (var ld in data.Links)
            {
                if (ld.FromNodeIndex >= 0 && ld.FromNodeIndex < nodes.Count &&
                    ld.ToNodeIndex >= 0 && ld.ToNodeIndex < nodes.Count)
                {
                    links.Add(new Link { From = nodes[ld.FromNodeIndex], To = nodes[ld.ToNodeIndex] });
                }
            }

            CurrentFilePath = path;
            DisplayName = null; // use file name from now on
            canvas.Invalidate();
        }

        public void Save()
        {
            if (string.IsNullOrEmpty(CurrentFilePath))
            {
                SaveAs();
            }
            else
            {
                SaveToFile(CurrentFilePath!);
            }
        }

        public void SaveAs()
        {
            using var saveDialog = new SaveFileDialog
            {
                Filter = "Node Graph (*.nodes)|*.nodes|JSON (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = "nodes",
                AddExtension = true,
                FileName = string.IsNullOrEmpty(CurrentFilePath) ? "Untitled.nodes" : Path.GetFileName(CurrentFilePath)
            };
            if (saveDialog.ShowDialog(this.FindForm()) == DialogResult.OK)
            {
                SaveToFile(saveDialog.FileName);
            }
        }

        public void SaveToFile(string path)
        {
            var data = new GraphData();
            var nodeIndex = new Dictionary<NodeControl, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var nc = nodes[i];
                nodeIndex[nc] = i;
                data.Nodes.Add(new NodeData
                {
                    Text = nc.NodeText,
                    X = nc.Left,
                    Y = nc.Top,
                    Width = nc.Width,
                    Height = nc.Height
                });
            }
            foreach (var l in links)
            {
                if (l.From != null && l.To != null && nodeIndex.ContainsKey(l.From) && nodeIndex.ContainsKey(l.To))
                {
                    data.Links.Add(new LinkData
                    {
                        FromNodeIndex = nodeIndex[l.From],
                        ToNodeIndex = nodeIndex[l.To]
                    });
                }
            }

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            CurrentFilePath = path;
            DisplayName = null; // after save, show file name
        }

        public void AddNodeInteractive()
        {
            var mousePos = canvas.PointToClient(Control.MousePosition);
            var center = new Point(
                Math.Max(0, Math.Min(canvas.ClientSize.Width - 140, mousePos.X - 70)),
                Math.Max(0, Math.Min(canvas.ClientSize.Height - 90, mousePos.Y - 45))
            );
            var nc = CreateNode("Node", new Rectangle(center, new Size(140, 90)));
            canvas.Controls.Add(nc);
            nodes.Add(nc);
            canvas.Invalidate();
        }

        public void ToggleDeleteMode()
        {
            deleteMode = !deleteMode;
            CancelKeyboardLinkMode();
            if (!deleteMode) ClearSelection();
        }

        public void ToggleKeyboardLinkMode()
        {
            keyboardLinkingMode = !keyboardLinkingMode;
            keyboardLinkFirst = null;
            linkingSource = null;
            ClearSelection();
            canvas.Invalidate();
        }

        public void CancelModes()
        {
            CancelKeyboardLinkMode();
            if (deleteMode)
            {
                deleteMode = false;
                ClearSelection();
            }
        }

        private void CancelKeyboardLinkMode()
        {
            if (keyboardLinkingMode)
            {
                keyboardLinkingMode = false;
                keyboardLinkFirst = null;
                ClearSelection();
                canvas.Invalidate();
            }
        }

        private void Canvas_ControlRemoved(object? sender, ControlEventArgs e)
        {
            if (e.Control is NodeControl removed)
            {
                links.RemoveAll(l => l.From == removed || l.To == removed);
                nodes.Remove(removed);
                canvas.Invalidate();
            }
        }

        private NodeControl CreateNode(string text, Rectangle bounds)
        {
            var nc = new NodeControl(text)
            {
                Bounds = bounds
            };
            nc.PositionChanged += Node_PositionChanged;
            nc.LinkInitiated += Node_LinkInitiated;
            nc.NodeClicked += Node_NodeClicked;
            return nc;
        }

        private void Node_PositionChanged(object? sender, EventArgs e)
        {
            canvas.Invalidate();
        }

        private void Node_LinkInitiated(object? sender, EventArgs e)
        {
            if (sender is NodeControl src)
            {
                linkingSource = src;
                keyboardLinkingMode = false;
                keyboardLinkFirst = null;
                canvas.Invalidate();
            }
        }

        private void Node_NodeClicked(object? sender, EventArgs e)
        {
            var clicked = sender as NodeControl;
            if (clicked == null) return;

            if (deleteMode)
            {
                links.RemoveAll(l => l.From == clicked || l.To == clicked);
                canvas.Controls.Remove(clicked);
                nodes.Remove(clicked);
                canvas.Invalidate();
                return;
            }

            if (keyboardLinkingMode)
            {
                if (keyboardLinkFirst == null)
                {
                    keyboardLinkFirst = clicked;
                    clicked.SetSelected(true);
                }
                else
                {
                    if (clicked != keyboardLinkFirst)
                    {
                        links.Add(new Link { From = keyboardLinkFirst, To = clicked });
                    }
                    keyboardLinkFirst.SetSelected(false);
                    keyboardLinkFirst = null;
                    keyboardLinkingMode = false;
                    canvas.Invalidate();
                }
                return;
            }

            if (linkingSource != null)
            {
                if (clicked != linkingSource)
                {
                    links.Add(new Link { From = linkingSource, To = clicked });
                    linkingSource = null;
                    canvas.Invalidate();
                }
            }
        }

        private void ClearSelection()
        {
            foreach (var n in nodes) n.SetSelected(false);
        }

        private void Canvas_MouseClick(object? sender, MouseEventArgs e)
        {
            if (!deleteMode || e.Button != MouseButtons.Left) return;

            var pt = e.Location;
            // check for node under cursor first
            NodeControl? hitNode = null;
            foreach (Control c in canvas.Controls)
            {
                if (c is NodeControl nc && nc.Bounds.Contains(pt))
                {
                    hitNode = nc;
                    break;
                }
            }

            if (hitNode != null)
            {
                links.RemoveAll(l => l.From == hitNode || l.To == hitNode);
                canvas.Controls.Remove(hitNode);
                nodes.Remove(hitNode);
                canvas.Invalidate();
                return;
            }

            // otherwise check for link near click
            const float thresh = 6f;
            for (int i = links.Count - 1; i >= 0; i--)
            {
                var link = links[i];
                if (link.From == null || link.To == null) continue;
                var p1 = link.From.GetCenter();
                var p2 = link.To.GetCenter();
                if (DistancePointToSegment(pt, p1, p2) <= thresh)
                {
                    links.RemoveAt(i);
                    canvas.Invalidate();
                    return;
                }
            }
        }

        private void Canvas_MouseDown(object? sender, MouseEventArgs e)
        {
            // clicking empty canvas cancels linking mode
            if (e.Button == MouseButtons.Left && linkingSource != null)
            {
                linkingSource = null;
                Cursor = Cursors.Default;
                canvas.Invalidate();
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
                    {
                        var p1 = link.From.GetCenter();
                        var p2 = link.To.GetCenter();
                        DrawArrow(e.Graphics, pen, p1, p2);
                    }
                }
            }

            // hint line when linking from context-menu initiated link
            if (linkingSource != null)
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
            // base of arrow head
            var bx = p2.X + ux * headSize;
            var by = p2.Y + uy * headSize;
            // perpendicular
            var perpX = -uy;
            var perpY = ux;
            var pLeft = new PointF(bx + perpX * (headSize / 2), by + perpY * (headSize / 2));
            var pRight = new PointF(bx - perpX * (headSize / 2), by - perpY * (headSize / 2));
            using (var brush = new SolidBrush(pen.Color))
            {
                g.FillPolygon(brush, new[] { p2, pLeft, pRight });
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

        private void ClearDocument()
        {
            links.Clear();
            nodes.Clear();
            canvas.Controls.Clear();
            linkingSource = null;
            keyboardLinkFirst = null;
            keyboardLinkingMode = false;
            deleteMode = false;
            canvas.Invalidate();
        }
    }
}
