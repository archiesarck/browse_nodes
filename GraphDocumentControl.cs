using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.IO.Compression;
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

        // pan/zoom state (world -> screen: screen = world * zoom + pan)
        private float zoom = 1f;
        private const float MinZoom = 0.2f;
        private const float MaxZoom = 4f;
        private PointF pan = new PointF(0, 0);
        private bool isPanning = false;
        private Point lastPanMouse;
        private bool suppressNodeSync = false;
        private readonly Dictionary<NodeControl, RectangleF> logicalBounds = new Dictionary<NodeControl, RectangleF>();

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
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseLeave += Canvas_MouseUp;
            canvas.MouseClick += Canvas_MouseClick;
            canvas.MouseWheel += Canvas_MouseWheel;
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

            GraphData? data = null;
            using (var fileStream = File.OpenRead(path))
            using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
            {
                data = JsonSerializer.Deserialize<GraphData>(gzip);
            }

            if (data == null) return;

            ClearDocument();

            // Create nodes (logical coordinates)
            foreach (var nd in data.Nodes)
            {
                var logical = new Rectangle(nd.X, nd.Y, nd.Width, nd.Height);
                var nc = CreateNode(nd.Text, logical);
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
            ApplyTransformToAllNodes();
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
                Filter = "Node Graph (*.nodes)|*.nodes|All files (*.*)|*.*",
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
                var logical = GetLogicalBounds(nc);
                data.Nodes.Add(new NodeData
                {
                    Text = nc.NodeText,
                    X = (int)Math.Round(logical.X),
                    Y = (int)Math.Round(logical.Y),
                    Width = (int)Math.Round(logical.Width),
                    Height = (int)Math.Round(logical.Height)
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

            // Save compressed binary format directly to the chosen path
            using (var fileStream = File.Create(path))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, data);
            }

            CurrentFilePath = path;
            DisplayName = null; // after save, show file name
        }

        public void AddNodeInteractive()
        {
            var mousePosScreen = canvas.PointToClient(Control.MousePosition);
            var mouseWorld = ScreenToWorld(mousePosScreen);
            var size = new SizeF(140, 90);
            var topLeft = new PointF(mouseWorld.X - size.Width / 2f, mouseWorld.Y - size.Height / 2f);
            var logical = new RectangleF(topLeft, size);

            var nc = CreateNode("Node", Rectangle.Round(logical));
            canvas.Controls.Add(nc);
            nodes.Add(nc);
            ApplyTransformToAllNodes();
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
                logicalBounds.Remove(removed);
                canvas.Invalidate();
            }
        }

        private NodeControl CreateNode(string text, Rectangle logicalBoundsRect)
        {
            var nc = new NodeControl(text);
            logicalBounds[nc] = logicalBoundsRect;
            nc.Bounds = TransformRect(logicalBoundsRect);
            nc.PositionChanged += Node_PositionChanged;
            nc.LinkInitiated += Node_LinkInitiated;
            nc.NodeClicked += Node_NodeClicked;
            return nc;
        }

        private void Node_PositionChanged(object? sender, EventArgs e)
        {
            if (suppressNodeSync || sender is not NodeControl nc) { canvas.Invalidate(); return; }
            var logical = InverseTransformRect(nc.Bounds);
            logicalBounds[nc] = logical;
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
                logicalBounds.Remove(clicked);
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
                logicalBounds.Remove(hitNode);
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
            if (e.Button == MouseButtons.Left && linkingSource != null)
            {
                linkingSource = null;
                Cursor = Cursors.Default;
                canvas.Invalidate();
            }

            if (e.Button == MouseButtons.Left)
            {
                isPanning = true;
                lastPanMouse = e.Location;
                Cursor = Cursors.SizeAll;
            }
        }

        private void Canvas_MouseMove(object? sender, MouseEventArgs e)
        {
            if (isPanning)
            {
                var dx = e.Location.X - lastPanMouse.X;
                var dy = e.Location.Y - lastPanMouse.Y;
                pan = new PointF(pan.X + dx, pan.Y + dy);
                lastPanMouse = e.Location;
                ApplyTransformToAllNodes();
            }
        }

        private void Canvas_MouseUp(object? sender, EventArgs e)
        {
            if (isPanning)
            {
                isPanning = false;
                Cursor = Cursors.Default;
            }
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
            {
                // Scroll up = zoom in
                ZoomAt(e.Location, zoom * 1.1f);
            }
            else if (e.Delta < 0)
            {
                // Scroll down = zoom out
                ZoomAt(e.Location, zoom / 1.1f);
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
            logicalBounds.Clear();
            canvas.Controls.Clear();
            linkingSource = null;
            keyboardLinkFirst = null;
            keyboardLinkingMode = false;
            deleteMode = false;
            zoom = 1f;
            pan = new PointF(0, 0);
            canvas.Invalidate();
        }

        // Helpers: transforms
        private PointF ScreenToWorld(PointF s) => new PointF((s.X - pan.X) / zoom, (s.Y - pan.Y) / zoom);
        private PointF WorldToScreen(PointF w) => new PointF(w.X * zoom + pan.X, w.Y * zoom + pan.Y);
        private Rectangle TransformRect(RectangleF wr)
        {
            var x = wr.X * zoom + pan.X;
            var y = wr.Y * zoom + pan.Y;
            var w = wr.Width * zoom;
            var h = wr.Height * zoom;
            return Rectangle.Round(new RectangleF(x, y, w, h));
        }
        private RectangleF InverseTransformRect(Rectangle r)
        {
            return new RectangleF((r.X - pan.X) / zoom, (r.Y - pan.Y) / zoom, r.Width / zoom, r.Height / zoom);
        }
        private RectangleF GetLogicalBounds(NodeControl nc)
        {
            if (logicalBounds.TryGetValue(nc, out var rect)) return rect;
            // fallback from current screen bounds
            return InverseTransformRect(nc.Bounds);
        }
        private void ApplyTransformToAllNodes()
        {
            suppressNodeSync = true;
            foreach (var nc in nodes)
            {
                if (logicalBounds.TryGetValue(nc, out var wr))
                {
                    nc.Bounds = TransformRect(wr);
                }
            }
            suppressNodeSync = false;
            canvas.Invalidate();
        }

        // Public: zoom API for host form shortcuts
        public void ZoomIn() => SetZoom(zoom * 1.1f);
        public void ZoomOut() => SetZoom(zoom / 1.1f);
        private void SetZoom(float newZoom)
        {
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            var centerScreen = new PointF(canvas.ClientSize.Width / 2f, canvas.ClientSize.Height / 2f);
            var centerWorld = ScreenToWorld(centerScreen);
            zoom = newZoom;
            pan = new PointF(centerScreen.X - centerWorld.X * zoom, centerScreen.Y - centerWorld.Y * zoom);
            ApplyTransformToAllNodes();
        }

        private void ZoomAt(Point screenPoint, float newZoom)
        {
            newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
            var worldPoint = ScreenToWorld(screenPoint);
            zoom = newZoom;
            pan = new PointF(screenPoint.X - worldPoint.X * zoom, screenPoint.Y - worldPoint.Y * zoom);
            ApplyTransformToAllNodes();
        }
    }
}
