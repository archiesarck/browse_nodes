using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace browse_nodes
{
    public class MainForm : Form
    {
    private readonly TabControl tabControl;
    private readonly Label helpLabelRef;
    private readonly FlowLayoutPanel topTabsPanel;
    private int untitledCounter = 0;

    private static readonly Color BgDark = Color.FromArgb(25, 25, 28);
    private static readonly Color BgPanel = Color.FromArgb(30, 30, 32);
    private static readonly Color BgTabActive = Color.FromArgb(38, 38, 44);
    private static readonly Color BgTabInactive = Color.FromArgb(28, 28, 30);

        public MainForm()
        {
            Text = "browse_nodes";
            KeyPreview = true;
            Size = new Size(1200, 800);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            BackColor = BgDark;

            helpLabelRef = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Shortcuts: Ctrl+N Node | Ctrl+T New Tab | Ctrl+O Open | Ctrl+L Link | Ctrl+D Delete | Ctrl+S Save | Esc Cancel | Active: -"
            };
            Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                BackColor = BgPanel,
                Controls = { helpLabelRef }
            });

            topTabsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = BgPanel,
                AutoScroll = true,
                WrapContents = false
            };
            Controls.Add(topTabsPanel);

            tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Alignment = TabAlignment.Bottom,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                HotTrack = true,
                BackColor = BgDark,
                ItemSize = new Size(0, 24),
                ContextMenuStrip = new ContextMenuStrip
                {
                    Items =
                    {
                        new ToolStripMenuItem("New Untitled Tab (Ctrl+T)", null, (s, e) => CreateNewDocumentTab()),
                        new ToolStripMenuItem("Open… (Ctrl+O)", null, (s, e) => OpenGraphIntoNewTab()),
                        new ToolStripMenuItem("Show Open Tabs…", null, (s, e) => ShowOpenTabsDialog()),
                        new ToolStripSeparator(),
                        new ToolStripMenuItem("Close Tab", null, (s, e) => CloseActiveTab())
                    }
                }
            };
            tabControl.DrawItem += TabControl_DrawItem;
            tabControl.Paint += TabControl_Paint;
            tabControl.SelectedIndexChanged += (s, e) => RefreshUI();
            tabControl.ControlAdded += (s, e) => RefreshUI();
            tabControl.ControlRemoved += (s, e) => RefreshUI();
            Controls.Add(tabControl);

            Shown += MainForm_Shown;
            HandleCreated += (s, e) => TryApplyDarkTitleBar();
        }

        private void MainForm_Shown(object? sender, EventArgs e)
        {
            CreateNewDocumentTab();
        }

        private GraphDocumentControl? ActiveDoc => tabControl.SelectedTab?.Controls.OfType<GraphDocumentControl>().FirstOrDefault();

        private void CreateNewDocumentTab()
        {
            var doc = new GraphDocumentControl();
            doc.NewBlank();
            doc.DisplayName = $"Untitled {++untitledCounter}";
            var page = new TabPage(doc.GetSuggestedTabTitle()) { BackColor = BgDark };
            page.Controls.Add(doc);
            tabControl.TabPages.Add(page);
            tabControl.SelectedTab = page;
        }

        private void OpenGraphIntoNewTab()
        {
            using var openDialog = new OpenFileDialog
            {
                Filter = "Node Graph (*.nodes)|*.nodes|All files (*.*)|*.*",
                Multiselect = true
            };
            if (openDialog.ShowDialog(this) == DialogResult.OK)
            {
                foreach (var file in openDialog.FileNames)
                {
                    try
                    {
                        var doc = new GraphDocumentControl();
                        doc.LoadFromFile(file);
                        var page = new TabPage(Path.GetFileName(file)) { BackColor = BgDark };
                        page.Controls.Add(doc);
                        tabControl.TabPages.Add(page);
                        tabControl.SelectedTab = page;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, $"Failed to open {file}: {ex.Message}", "Open Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (IsCtrlKey(keyData, Keys.T)) { CreateNewDocumentTab(); return true; }
            if (IsCtrlKey(keyData, Keys.O)) { OpenGraphIntoNewTab(); return true; }

            var doc = ActiveDoc;
            if (doc == null) return base.ProcessCmdKey(ref msg, keyData);

            if (IsCtrlKey(keyData, Keys.N)) { doc.AddNodeInteractive(); return true; }
            if (IsCtrlKey(keyData, Keys.D)) { doc.ToggleDeleteMode(); UpdateHelpLabel("Delete mode toggled"); return true; }
            if (IsCtrlKey(keyData, Keys.L)) { doc.ToggleKeyboardLinkMode(); UpdateHelpLabel("Link mode toggled"); return true; }
            if (IsCtrlKey(keyData, Keys.S)) { doc.Save(); UpdateTabTitle(doc); return true; }

            // Ctrl+ and Ctrl- zoom shortcuts (support OEM and numpad)
            if ((keyData & Keys.Control) == Keys.Control)
            {
                var key = keyData & Keys.KeyCode;
                if (key == Keys.Oemplus || key == Keys.Add)
                {
                    doc.ZoomIn();
                    UpdateHelpLabel("Zoom in");
                    return true;
                }
                if (key == Keys.OemMinus || key == Keys.Subtract)
                {
                    doc.ZoomOut();
                    UpdateHelpLabel("Zoom out");
                    return true;
                }
            }

            if (keyData == Keys.Escape) { doc.CancelModes(); UpdateHelpLabel("Modes cleared"); return true; }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static bool IsCtrlKey(Keys keyData, Keys key) =>
            (keyData & Keys.Control) == Keys.Control && (keyData & Keys.KeyCode) == key;

        private void UpdateHelpLabel(string status) =>
            helpLabelRef.Text = $"Shortcuts: Ctrl+N Node | Ctrl+T New Tab | Ctrl+O Open | Ctrl+L Link | Ctrl+D Delete | Ctrl+S Save | Esc Cancel | Active: {tabControl.SelectedTab?.Text ?? "-"}    [{status}]";

        private void UpdateTabTitle(GraphDocumentControl doc)
        {
            var page = tabControl.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Controls.Contains(doc));
            if (page != null)
            {
                page.Text = doc.GetSuggestedTabTitle();
                RefreshUI();
            }
        }

        private void HandleOpenChoice()
        {
            var result = MessageBox.Show(this, "Open: Yes = New blank file, No = Open existing file(s)", "Open",
                MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (result == DialogResult.Yes) CreateNewDocumentTab();
            else if (result == DialogResult.No) OpenGraphIntoNewTab();
        }

        private void RefreshUI()
        {
            helpLabelRef.Text = $"Shortcuts: Ctrl+N Node | Ctrl+T New Tab | Ctrl+O Open | Ctrl+L Link | Ctrl+D Delete | Ctrl+S Save | Esc Cancel | Active: {tabControl.SelectedTab?.Text ?? "-"}    [Tabs: {tabControl.TabPages.Count}]";
            RefreshTopTabsPanel();
        }

        private void CloseActiveTab()
        {
            if (tabControl.SelectedTab != null)
            {
                tabControl.TabPages.Remove(tabControl.SelectedTab);
                if (tabControl.TabPages.Count == 0) CreateNewDocumentTab();
            }
        }

        private void ShowOpenTabsDialog()
        {
            using var dlg = new Form
            {
                Text = "Open Tabs",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(400, 300),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = BgDark,
                ForeColor = Color.White
            };
            var list = new ListBox { Dock = DockStyle.Fill, BackColor = BgPanel, ForeColor = Color.White };
            for (int i = 0; i < tabControl.TabPages.Count; i++)
            {
                var page = tabControl.TabPages[i];
                list.Items.Add((i + 1).ToString().PadLeft(2) + ") " + page.Text);
            }
            list.DoubleClick += (s, e) => { if (list.SelectedIndex >= 0) dlg.DialogResult = DialogResult.OK; };
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { dlg.DialogResult = DialogResult.OK; e.Handled = true; } };
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = BgPanel };
            var ok = new Button { Text = "Activate", DialogResult = DialogResult.OK, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Width = 90, Left = 400 - 190, Top = 10 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right | AnchorStyles.Bottom, Width = 90, Left = 400 - 95, Top = 10 };
            panel.Controls.Add(ok);
            panel.Controls.Add(cancel);
            dlg.Controls.Add(list);
            dlg.Controls.Add(panel);
            dlg.AcceptButton = ok;
            if (dlg.ShowDialog(this) == DialogResult.OK && list.SelectedIndex >= 0)
            {
                tabControl.SelectedIndex = list.SelectedIndex;
            }
        }

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            try
            {
                var tab = tabControl;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = tab.GetTabRect(e.Index);
                bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

                Color bg = selected ? BgTabActive : BgDark;
                Color fg = Color.White;

                using (var b = new SolidBrush(bg)) g.FillRectangle(b, rect);

                // Text centered
                string text = tab.TabPages[e.Index].Text;
                TextRenderer.DrawText(g, text, tab.Font, rect, fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Optional bottom highlight for selected (since tabs are at bottom)
                if (selected)
                {
                    using var pen = new Pen(Color.FromArgb(104, 104, 117), 2);
                    if (tab.Alignment == TabAlignment.Bottom)
                        g.DrawLine(pen, rect.Left + 6, rect.Top + 1, rect.Right - 6, rect.Top + 1);
                    else
                        g.DrawLine(pen, rect.Left + 6, rect.Bottom - 2, rect.Right - 6, rect.Bottom - 2);
                }
            }
            catch { /* ignore drawing errors */ }
        }

        private void TabControl_Paint(object? sender, PaintEventArgs e)
        {
            try
            {
                // Overpaint the page border area with background color to hide white border
                var rect = tabControl.DisplayRectangle;
                using var pen = new Pen(BgDark, 3);
                var r = new Rectangle(rect.X - 2, rect.Y - 2, rect.Width + 4, rect.Height + 4);
                e.Graphics.DrawRectangle(pen, r);
            }
            catch { /* ignore */ }
        }

        private void RefreshTopTabsPanel()
        {
            topTabsPanel.SuspendLayout();
            topTabsPanel.Controls.Clear();
            for (int i = 0; i < tabControl.TabPages.Count; i++)
            {
                var page = tabControl.TabPages[i];
                bool isActive = tabControl.SelectedTab == page;

                var item = new Panel
                {
                    AutoSize = false,
                    Height = 22,
                    BackColor = isActive ? BgTabActive : BgTabInactive,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    Tag = page,
                    Cursor = Cursors.Hand
                };

                var title = new Label
                {
                    AutoSize = true,
                    Text = page.Text,
                    ForeColor = Color.White,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    Dock = DockStyle.Left,
                    Cursor = Cursors.Hand,
                    Tag = page
                };
                EventHandler activateTab = (s, e) => tabControl.SelectedTab = page;
                MouseEventHandler closeTab = (s, e) => { if (e.Button == MouseButtons.Middle) tabControl.TabPages.Remove(page); };
                title.Click += activateTab;
                item.Click += activateTab;
                title.MouseUp += closeTab;
                item.MouseUp += closeTab;

                var close = new Button
                {
                    Text = "×",
                    Font = new Font(Font.FontFamily, 9f, FontStyle.Bold),
                    Width = 18,
                    Height = 18,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = isActive ? Color.FromArgb(45,45,50) : Color.FromArgb(34,34,36),
                    ForeColor = Color.White,
                    Margin = new Padding(0),
                    Padding = new Padding(0),
                    Dock = DockStyle.Right,
                    TabStop = false,
                    Tag = page
                };
                close.FlatAppearance.BorderSize = 0;
                close.FlatAppearance.MouseOverBackColor = Color.FromArgb(60, 60, 66);
                close.FlatAppearance.MouseDownBackColor = Color.FromArgb(76, 76, 84);
                close.Click += (s, e) =>
                {
                    tabControl.TabPages.Remove(page);
                    if (tabControl.TabPages.Count == 0) CreateNewDocumentTab();
                };

                item.Controls.AddRange(new Control[] { close, title });
                item.Width = title.PreferredWidth + close.Width + 4;
                
                topTabsPanel.Controls.Add(item);
            }
            topTabsPanel.ResumeLayout();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private void TryApplyDarkTitleBar()
        {
            try
            {
                int useDark = 1;
                int result = DwmSetWindowAttribute(this.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
                if (result != 0)
                {
                    int attr = DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1;
                    DwmSetWindowAttribute(this.Handle, attr, ref useDark, sizeof(int));
                }
            }
            catch
            {
                // Not supported; ignore.
            }
        }
    }
}