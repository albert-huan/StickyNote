using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace StickyNote
{
    /// <summary>
    /// 主窗口：WinForms + GDI+ 全自绘，无边框 + DWM 阴影
    /// 界面布局与原 WPF 版一致，视觉更精致
    /// </summary>
    public class StickyForm : Form
    {
        // ══════════════════════════════════════════════════════════════
        //  Win32 P/Invoke
        // ══════════════════════════════════════════════════════════════
        [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hWnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("kernel32.dll")] private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint min, nint max);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int Left, Right, Top, Bottom; }

        private const int WM_NCHITTEST   = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCLIENT       = 1;
        private const int HTCAPTION      = 2;
        private const int HTLEFT         = 10;
        private const int HTRIGHT        = 11;
        private const int HTTOP          = 12;
        private const int HTTOPLEFT      = 13;
        private const int HTTOPRIGHT     = 14;
        private const int HTBOTTOM       = 15;
        private const int HTBOTTOMLEFT   = 16;
        private const int HTBOTTOMRIGHT  = 17;
        private const int RESIZE_BORDER  = 6;

        // ══════════════════════════════════════════════════════════════
        //  UI 控件
        // ══════════════════════════════════════════════════════════════
        private TabPanel _tabPanel = null!;
        private RichTextBox _rtb = null!;
        private Panel _toolbar = null!;    // 底部格式工具栏
        private Panel _titleBar = null!;   // 顶部标题栏（自绘）
        private Panel _contentArea = null!; // 右侧内容区
        private readonly List<ToolIconButton> _toolbarButtons = new();

        private const int ToolbarButtonWidth = 26;
        private const int ToolbarButtonHeight = 28;
        private const int ToolbarButtonGap = 2;
        private const int ToolbarPaddingX = 4;
        private const int ToolbarPaddingY = 2;

        // ══════════════════════════════════════════════════════════════
        //  状态
        // ══════════════════════════════════════════════════════════════
        private List<NoteData> _notes = new();
        private NoteData? _current;
        private bool _loading = false;
        private bool _isTopmost = false;
        private Color _baseColor = ColorTranslator.FromHtml("#DCEDC8");

        // 色彩方案
        private static readonly (string Name, string Hex)[] ColorSchemes =
        {
            ("经典牛皮纸", "#E3C887"), ("樱花粉",   "#FFCDD2"),
            ("天空蓝",   "#B3E5FC"), ("护眼绿",   "#DCEDC8"),
            ("极简淡黄", "#FFF9C4"),
            // 暗色
            ("深灰",  "#424242"), ("木炭黑", "#263238"), ("橄榄绿", "#3D5B3D"),
        };

        // 保存定时器
        private System.Windows.Forms.Timer _saveTimer = null!;
        // 状态栏消息定时器
        private System.Windows.Forms.Timer _statusTimer = null!;
        // 每分钟刷新内存
        private System.Windows.Forms.Timer _memTimer = null!;
        // 状态文字
        private string _statusMsg = "";

        // 分隔条宽度
        private const int SplitterW = 2;
        private const int TabPanelDefaultW = 110;
        private int _tabPanelW = TabPanelDefaultW;
        private bool _splitterDragging = false;
        private int _splitterDragStart;

        // ══════════════════════════════════════════════════════════════
        //  构造
        // ══════════════════════════════════════════════════════════════
        public StickyForm()
        {
            // P1 & B3 & U5: 启动时加载设置
            NoteManager.LoadSettings();

            this.FormBorderStyle = FormBorderStyle.None;
            this.BackColor       = ColorTranslator.FromHtml("#DCEDC8");
            this.Padding         = new Padding(4);

            var s = NoteManager.Settings;
            if (s.WindowWidth > 100 && s.WindowHeight > 100)
                this.Size = new Size(s.WindowWidth, s.WindowHeight);
            else
                this.Size = new Size(520, 340);

            if (s.WindowLeft != -1)
                this.Location = new Point(s.WindowLeft, s.WindowTop);
            else
                this.StartPosition = FormStartPosition.CenterScreen;

            _tabPanelW = Math.Clamp(s.SplitterWidth, 60, 280);

            this.MinimumSize     = new Size(240, 180);
            this.Font            = new Font("Microsoft YaHei", 9f);
            this.DoubleBuffered  = true;

            BuildLayout();
            ApplyEditorDisplaySettings();
            ReflowToolbar();
            LoadNotes();
            SetupTimers();
            ApplyDwmShadow();

            // 存位置
            this.LocationChanged += (sender, args) => SaveWindowBounds();
            this.SizeChanged += (sender, args) => SaveWindowBounds();
        }

        private void SaveWindowBounds()
        {
            if (this.WindowState == FormWindowState.Normal)
            {
                var s = NoteManager.Settings;
                s.WindowLeft = this.Left;
                s.WindowTop = this.Top;
                s.WindowWidth = this.Width;
                s.WindowHeight = this.Height;
                s.SplitterWidth = _tabPanelW;
                NoteManager.SaveSettings();
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  布局构建
        // ══════════════════════════════════════════════════════════════
        private void BuildLayout()
        {
            // ── 标题栏 ──────────────────────────────────────────────
            _titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = Color.Transparent
            };
            _titleBar.Paint += TitleBar_Paint;
            _titleBar.MouseDown += TitleBar_MouseDown;
            _titleBar.MouseDoubleClick += TitleBar_MouseDoubleClick;

            // 标题栏 - 右侧控制按钮（自定义绘制的 Panel）
            var ctrlPanel = new ControlButtonPanel(this);
            ctrlPanel.Dock = DockStyle.Right;
            ctrlPanel.Width = 150; // 5 buttons × 30px
            ctrlPanel.TintColor = _baseColor;  // 初始化时同步颜色，避免黑色背景
            ctrlPanel.NewClicked += (s, e) => CreateNewTab();
            ctrlPanel.PaletteClicked += (s, e) => ShowColorMenu(ctrlPanel);
            ctrlPanel.PinClicked += (s, e) => ToggleTopmost();
            ctrlPanel.MinimizeClicked += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            ctrlPanel.CloseClicked += (s, e) => { this.WindowState = FormWindowState.Minimized; };
            _titleBar.Controls.Add(ctrlPanel);

            // ── 主体容器 ────────────────────────────────────────────
            var body = new Panel { Dock = DockStyle.Fill };

            // ── 标签栏 ──────────────────────────────────────────────
            _tabPanel = new TabPanel
            {
                Width = _tabPanelW,
                Dock = DockStyle.Left
            };
            _tabPanel.TabSelected      += OnTabSelected;
            _tabPanel.TabDeleteRequested += OnTabDeleteRequested;
            _tabPanel.TabRenameRequested += OnTabRenameRequested;
            _tabPanel.TabMoveUpRequested += OnTabMoveUpRequested;
            _tabPanel.TabMoveDownRequested += OnTabMoveDownRequested;
            _tabPanel.NewTabRequested  += (s, e) => CreateNewTab();

            // ── 分割条 ──────────────────────────────────────────────
            var splitter = new Panel
            {
                Dock = DockStyle.Left,
                Width = SplitterW,
                BackColor = Color.FromArgb(60, 93, 64, 55),
                Cursor = Cursors.VSplit
            };
            splitter.MouseDown  += (s, e) => { _splitterDragging = true; _splitterDragStart = Cursor.Position.X - _tabPanelW; splitter.Capture = true; };
            splitter.MouseMove  += (s, e) => { if (_splitterDragging) { _tabPanelW = Math.Clamp(Cursor.Position.X - _splitterDragStart, 60, 280); _tabPanel.Width = _tabPanelW; body.PerformLayout(); } };
            splitter.MouseUp    += (s, e) => { _splitterDragging = false; splitter.Capture = false; SaveWindowBounds(); };

            // ── 内容区 ──────────────────────────────────────────────
            _contentArea = new Panel { Dock = DockStyle.Fill, BackColor = ColorTranslator.FromHtml("#DCEDC8") };
            _contentArea.Paint += ContentArea_Paint;
            _contentArea.Resize += (s, e) => ReflowToolbar();

            // ── RichTextBox ─────────────────────────────────────────
            _rtb = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor   = ColorTranslator.FromHtml("#DCEDC8"),  // WinForms RTB 不支持透明
                Font        = new Font("Microsoft YaHei", 13f),
                ForeColor   = ColorTranslator.FromHtml("#3E2723"),
                ScrollBars  = RichTextBoxScrollBars.Both,
                WordWrap    = true,
                AcceptsTab  = true,
                DetectUrls  = false,
                Multiline   = true,
            };
            _rtb.TextChanged += RtbTextChanged;
            _rtb.KeyDown     += RtbKeyDown;
            _rtb.MouseUp     += RtbMouseUp;
            _rtb.ContextMenuStrip = BuildRtbContextMenu();

            // ── 底部工具栏 ──────────────────────────────────────────
            _toolbar = BuildToolbar();

            // 空状态标签
            var emptyLabel = new Label
            {
                Text      = "点击左下角 ＋ 新建便签",
                AutoSize  = false,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(100, 93, 64, 55),
                Font      = new Font("Microsoft YaHei", 12f),
                Name      = "EmptyLabel"
            };

            _contentArea.Controls.Add(_rtb);
            _contentArea.Controls.Add(emptyLabel);
            _contentArea.Controls.Add(_toolbar);

            body.Controls.Add(_contentArea);
            body.Controls.Add(splitter);
            body.Controls.Add(_tabPanel);

            this.Controls.Add(body);
            this.Controls.Add(_titleBar);
        }

        // ══════════════════════════════════════════════════════════════
        //  标题栏绘制
        // ══════════════════════════════════════════════════════════════
        private void TitleBar_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // U1: 左侧显示当前标题
            if (_current != null)
            {
                using var font = new Font("Microsoft YaHei", 9.5f, FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(180, 62, 39, 35));
                g.DrawString(_current.Title, font, brush, new PointF(12, 6));
            }

            // 状态消息
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                using var fb = new SolidBrush(Color.FromArgb(160, 93, 64, 55));
                using var statusFont = new Font("Microsoft YaHei", 8.5f);
                g.DrawString(_statusMsg, statusFont, fb, _titleBar.ClientRectangle, sf);
            }
        }

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        private void TitleBar_MouseDoubleClick(object? sender, MouseEventArgs e)
        {
            if (this.WindowState == FormWindowState.Normal)
                this.WindowState = FormWindowState.Maximized;
            else
                this.WindowState = FormWindowState.Normal;
        }

        // ══════════════════════════════════════════════════════════════
        //  内容区背景（牛皮纸 + 横线）
        // ══════════════════════════════════════════════════════════════
        private void ContentArea_Paint(object? sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var rc = _contentArea.ClientRectangle;
            // 背景填充
            g.Clear(_baseColor);

            // 横线：从标题栏底部开始，步长 28px
            using var linePen = new Pen(Color.FromArgb(50, Darken(_baseColor, 0.25f)), 1f);
            int lineH = 28;
            int startY = 32 - (_toolbar.Height); // 对齐行高
            for (int y = lineH; y < rc.Height - _toolbar.Height; y += lineH)
                g.DrawLine(linePen, 0, y, rc.Width, y);
        }

        // ══════════════════════════════════════════════════════════════
        //  底部工具栏
        // ══════════════════════════════════════════════════════════════
        private Panel BuildToolbar()
        {
            var bar = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 32,
                // 使用实色，不用半透明（WinForms 不支持 alpha Panel 背景）
                // 颜色在 ApplyColorInternal 时动态更新
                BackColor = Darken(ColorTranslator.FromHtml("#DCEDC8"), 0.10f)
            };
            _toolbar = bar; // 保存引用以便后续换色
            _toolbarButtons.Clear();

            // 工具按钮列表：(ToolTip, Action, DrawFunc)
            var btns = new (string tip, Action act, Action<Graphics, Rectangle> draw)[]
            {
                ("加粗 (Ctrl+B)",      () => ToggleBold(),            DrawBold),
                ("斜体 (Ctrl+I)",      () => ToggleItalic(),          DrawItalic),
                ("下划线 (Ctrl+U)",    () => ToggleUnderline(),       DrawUnderline),
                ("删除线",             () => ToggleStrike(),           DrawStrike),
                ("增大字号",           () => FontSize(+1),            DrawFontBig),
                ("减小字号",           () => FontSize(-1),            DrawFontSmall),
                ("左对齐",             () => SetAlign(HorizontalAlignment.Left),   DrawAlignLeft),
                ("居中",               () => SetAlign(HorizontalAlignment.Center), DrawAlignCenter),
                ("右对齐",             () => SetAlign(HorizontalAlignment.Right),  DrawAlignRight),
                ("☐ 待办",            () => InsertTodo(),            DrawTodo),
                ("——— 分隔线",          () => InsertSeparator(),       DrawSep),
            };

            foreach (var (tip, act, draw) in btns)
            {
                var btn = new ToolIconButton(draw)
                {
                    Width = ToolbarButtonWidth,
                    Height = ToolbarButtonHeight
                };
                btn.ToolTipText = tip;
                btn.Clicked += (s, e) => act();
                bar.Controls.Add(btn);
                _toolbarButtons.Add(btn);
            }

            bar.Resize += (s, e) => ReflowToolbar();
            ReflowToolbar();
            return bar;
        }

        private void ReflowToolbar()
        {
            if (_toolbar == null || _toolbarButtons.Count == 0)
                return;

            int availableWidth = Math.Max(1, _toolbar.ClientSize.Width - ToolbarPaddingX * 2);
            int stepX = ToolbarButtonWidth + ToolbarButtonGap;
            bool autoWrap = NoteManager.Settings.ToolbarAutoWrap;

            int buttonsPerRow = autoWrap
                ? Math.Max(1, availableWidth / stepX)
                : Math.Max(1, _toolbarButtons.Count);

            for (int i = 0; i < _toolbarButtons.Count; i++)
            {
                int row = i / buttonsPerRow;
                int col = i % buttonsPerRow;

                _toolbarButtons[i].Left = ToolbarPaddingX + col * stepX;
                _toolbarButtons[i].Top = ToolbarPaddingY + row * (ToolbarButtonHeight + ToolbarPaddingY);
            }

            int rows = (int)Math.Ceiling(_toolbarButtons.Count / (double)buttonsPerRow);
            int desiredHeight = ToolbarPaddingY + rows * (ToolbarButtonHeight + ToolbarPaddingY);
            if (_toolbar.Height != desiredHeight)
            {
                _toolbar.Height = desiredHeight;
                _contentArea?.Invalidate();
            }
        }

        private static RichTextBoxScrollBars GetEditorScrollBars(bool showHorizontal, bool showVertical, bool wordWrap)
        {
            // WinForms RichTextBox 在启用自动换行时不会显示水平滚动
            if (wordWrap)
                return showVertical ? RichTextBoxScrollBars.Vertical : RichTextBoxScrollBars.None;

            if (showHorizontal && showVertical) return RichTextBoxScrollBars.Both;
            if (showHorizontal) return RichTextBoxScrollBars.Horizontal;
            if (showVertical) return RichTextBoxScrollBars.Vertical;
            return RichTextBoxScrollBars.None;
        }

        private void ApplyEditorDisplaySettings()
        {
            if (_rtb == null) return;

            var s = NoteManager.Settings;
            _rtb.WordWrap = s.EditorWordWrap;
            _rtb.ScrollBars = GetEditorScrollBars(s.ShowHorizontalScrollBar, s.ShowVerticalScrollBar, s.EditorWordWrap);
        }

        // ══════════════════════════════════════════════════════════════
        //  右键菜单
        // ══════════════════════════════════════════════════════════════
        private ContextMenuStrip BuildRtbContextMenu()
        {
            var menu = new ContextMenuStrip();
            void Add(string t, Action a) { var mi = new ToolStripMenuItem(t); mi.Click += (s, e) => a(); menu.Items.Add(mi); }
            Add("新建便签 (Ctrl+N)",   () => CreateNewTab());
            menu.Items.Add(new ToolStripSeparator());
            // 颜色子菜单
            var colorMenu = new ToolStripMenuItem("纸张颜色");
            foreach (var (name, hex) in ColorSchemes)
            {
                var mi = new ToolStripMenuItem(name) { Tag = hex };
                mi.Click += (s, e) => ApplyColor(hex);
                colorMenu.DropDownItems.Add(mi);
            }
            menu.Items.Add(colorMenu);
            // 透明度
            var opMenu = new ToolStripMenuItem("透明度");
            foreach (var (label, val) in new[] { ("100%", 1.0), ("80%", 0.8), ("50%", 0.5) })
            {
                var mi = new ToolStripMenuItem(label) { Tag = val };
                mi.Click += (s, e) => ApplyOpacity(val);
                opMenu.DropDownItems.Add(mi);
            }
            menu.Items.Add(opMenu);
            menu.Items.Add(new ToolStripSeparator());
            Add("搜索便签",    () => NoteSearchForm.Show(_notes));
            Add("导出为文本",  () => Export());
            Add("锁定内容",    () => { _rtb.ReadOnly = !_rtb.ReadOnly; ShowStatus(_rtb.ReadOnly ? "已锁定" : "已解锁"); });
            menu.Items.Add(new ToolStripSeparator());
            Add("设置...",     () => ShowSettingsDialog());
            Add("关于本软件",  () => ShowAboutDialog());
            menu.Items.Add(new ToolStripSeparator());
            Add("清空内容",    () => { if (Confirm("确定清空？")) _rtb.Clear(); });
            var del = new ToolStripMenuItem("删除此便签 (Ctrl+D)") { ForeColor = Color.Crimson };
            del.Click += (s, e) => DeleteCurrentNote();
            menu.Items.Add(del);
            return menu;
        }

        // ══════════════════════════════════════════════════════════════
        //  数据操作
        // ══════════════════════════════════════════════════════════════
        private void LoadNotes()
        {
            _notes = NoteManager.LoadNotes();
            if (_notes.Count == 0)
            {
                _current = null;
                _tabPanel.SetNotes(_notes, null);
                UpdateEmptyState();
                return;
            }
            _tabPanel.SetNotes(_notes, _notes[0]);
            SwitchTo(_notes[0]);
        }

        // B2: 提供公开刷新方法供外部（如托盘恢复删除）调用
        public void RefreshNotes()
        {
            var oldId = _current?.Id;
            _notes = NoteManager.LoadNotes();
            var target = _notes.Find(n => n.Id == oldId) ?? (_notes.Count > 0 ? _notes[0] : null);
            _tabPanel.SetNotes(_notes, target);
            if (target != null) SwitchTo(target);
            else UpdateEmptyState();
        }

        public void CreateNewTab()
        {
            var note = new NoteData
            {
                Id       = Guid.NewGuid().ToString(),
                Title    = "便签",
                Content  = "",
                Width    = this.Width,
                Height   = this.Height,
                Left     = this.Left,
                Top      = this.Top,
                ColorHex = "#DCEDC8",
                Opacity  = 1.0,
                FontSize = 13f,
                Alignment = "Left"
            };
            // 注意：_notes 与 NoteManager._notes 是同一个 List 引用，
            // 由 NoteManager.AddNote 统一添加，不能在此重复 Add
            NoteManager.AddNote(note);
            _tabPanel.SetNotes(_notes, note);
            SwitchTo(note);
        }

        private void SwitchTo(NoteData note)
        {
            _loading = true;
            try
            {
                _current = note;
                _tabPanel.SelectNote(note);

                // 应用颜色
                ApplyColorInternal(note.ColorHex);

                // 应用透明度
                this.Opacity = note.Opacity;

                // 加载内容
                if (!string.IsNullOrEmpty(note.Content) && note.Content.StartsWith("{\\rtf"))
                {
                    try { _rtb.Rtf = note.Content; }
                    catch { _rtb.Text = note.Content; }
                }
                else
                {
                    _rtb.Text = note.Content;
                }

                // 字体
                _rtb.Font = new Font("Microsoft YaHei", Math.Max(8f, note.FontSize > 0 ? note.FontSize : 13f));
                // 置顶
                _isTopmost = note.IsTopmost;
                this.TopMost = _isTopmost;

                UpdateEmptyState();
                _contentArea.Invalidate();
            }
            finally { _loading = false; }
        }

        private void SaveCurrentNote()
        {
            if (_loading || _current == null) return;
            try
            {
                _current.Content = _rtb.Rtf ?? "";
                _current.FontSize = _rtb.Font.Size;
                // P1: 更新预览缓存
                _current.PlainPreview = TabPanel.BuildPreview(_current.Content);
                // 标题取第一行 (如果不是自定义标题)
                if (!_current.IsCustomTitle)
                {
                    string firstLine = _rtb.Lines.Length > 0 ? _rtb.Lines[0] : "便签";
                    if (firstLine.Length > 15) firstLine = firstLine[..15] + "…";
                    _current.Title = string.IsNullOrWhiteSpace(firstLine) ? "便签" : firstLine;
                }
                _tabPanel.UpdateNote(_current);
                _titleBar.Invalidate(); // U1: 更新标题
                ScheduleSave();
            }
            catch { }
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // ══════════════════════════════════════════════════════════════
        //  事件处理
        // ══════════════════════════════════════════════════════════════
        private void OnTabSelected(object? s, NoteData note) => SwitchTo(note);

        private void OnTabDeleteRequested(object? s, NoteData note)
        {
            if (!Confirm($"确定删除便签 '{note.Title}' 吗？")) return;
            NoteManager.RemoveNote(note);
            _notes.Remove(note);
            if (_current?.Id == note.Id) _current = null;
            _tabPanel.SetNotes(_notes, _notes.Count > 0 ? _notes[0] : null);
            if (_notes.Count > 0) SwitchTo(_notes[0]);
            else UpdateEmptyState();
        }

        private void OnTabRenameRequested(object? s, NoteData note)
        {
            string? newName = InputDialog("重命名", "标签名称:", note.Title);
            if (newName == null) return;
            note.Title = newName.Trim();
            note.IsCustomTitle = true;
            NoteManager.SaveNotes();
            _tabPanel.SetNotes(_notes, _current);
        }

        private void OnTabMoveUpRequested(object? s, NoteData note) => MoveNote(note, -1);
        private void OnTabMoveDownRequested(object? s, NoteData note) => MoveNote(note, +1);

        private void MoveNote(NoteData note, int delta)
        {
            int idx = _notes.FindIndex(n => n.Id == note.Id);
            if (idx < 0) return;

            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= _notes.Count) return;

            _notes.RemoveAt(idx);
            _notes.Insert(newIdx, note);

            NoteManager.SaveNotes();
            _tabPanel.SetNotes(_notes, _current ?? note);
        }

        private void RtbTextChanged(object? s, EventArgs e)
        {
            if (!_loading) SaveCurrentNote();
        }

        private void RtbKeyDown(object? s, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.N) { CreateNewTab(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.D) { DeleteCurrentNote(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.T) { ToggleTopmost(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.E) { Export(); e.Handled = true; }
            if (e.Control && e.KeyCode == Keys.W) { this.WindowState = FormWindowState.Minimized; e.Handled = true; }

            // 已完成待办行（☑）按回车时，重置新行输入样式，避免继续灰色删除线
            if (e.KeyCode == Keys.Enter && !e.Control && !e.Alt && !_rtb.ReadOnly && IsCaretOnCompletedTodoLine())
            {
                e.SuppressKeyPress = true;
                e.Handled = true;

                var align = _rtb.SelectionAlignment;
                _rtb.SelectedText = Environment.NewLine;
                _rtb.SelectionColor = GetNormalTextColor();
                _rtb.SelectionFont = new Font(_rtb.Font, FontStyle.Regular);
                _rtb.SelectionAlignment = align;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Control && e.KeyCode == Keys.N) { CreateNewTab(); e.Handled = true; }

            // U4: Ctrl+Tab 和 Ctrl+Shift+Tab 切换
            if (e.Control && e.KeyCode == Keys.Tab && _notes.Count > 1)
            {
                if (_current == null) return;
                int idx = _notes.FindIndex(n => n.Id == _current.Id);
                if (e.Shift) idx = (idx - 1 + _notes.Count) % _notes.Count;
                else         idx = (idx + 1) % _notes.Count;
                SwitchTo(_notes[idx]);
                e.Handled = true;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _contentArea?.Invalidate();
            if (this.WindowState == FormWindowState.Minimized)
            {
                // 最小化时释放内存
                GC.Collect();
                GC.WaitForPendingFinalizers();
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  颜色 & 样式
        // ══════════════════════════════════════════════════════════════
        private void ApplyColor(string hex)
        {
            if (_current == null) return;
            _current.ColorHex = hex;
            ApplyColorInternal(hex);
            SaveCurrentNote();
            NoteManager.SaveNotes();
        }

        private void ApplyColorInternal(string hex)
        {
            try
            {
                _baseColor = ColorTranslator.FromHtml(hex);
                this.BackColor = _baseColor;
                _tabPanel.BaseColor = _baseColor;
                _contentArea.BackColor = _baseColor;
                _rtb.BackColor = _baseColor;
                // 工具栏背景：用比纸色略深 10% 的实色
                if (_toolbar != null)
                    _toolbar.BackColor = Darken(_baseColor, 0.10f);
                // ControlButtonPanel 标题栏按钮区同步颜色
                foreach (Control c in _titleBar.Controls)
                    if (c is ControlButtonPanel cbp) { cbp.TintColor = _baseColor; cbp.Invalidate(); }

                // 根据亮度决定前景色
                double lum = 0.299 * _baseColor.R + 0.587 * _baseColor.G + 0.114 * _baseColor.B;
                var fg = lum < 128
                    ? Color.FromArgb(220, 240, 240, 240)
                    : ColorTranslator.FromHtml("#3E2723");
                _rtb.ForeColor = fg;

                _contentArea?.Invalidate();
                _titleBar?.Invalidate();
            }
            catch { }
        }

        private void ApplyOpacity(double val)
        {
            this.Opacity = val;
            if (_current != null) { _current.Opacity = val; NoteManager.SaveNotes(); }
        }

        private void ShowColorMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            foreach (var (name, hex) in ColorSchemes)
            {
                var mi = new ToolStripMenuItem(name) { Tag = hex };
                // 颜色图标
                var bmp = new Bitmap(16, 12);
                using (var g = Graphics.FromImage(bmp))
                using (var b = new SolidBrush(ColorTranslator.FromHtml(hex)))
                    g.FillRectangle(b, 0, 0, 16, 12);
                mi.Image = bmp;
                mi.Click += (s, e) => ApplyColor(hex);
                menu.Items.Add(mi);
            }
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        // ══════════════════════════════════════════════════════════════
        //  格式操作
        // ══════════════════════════════════════════════════════════════
        private void ToggleBold()
        {
            _rtb.SelectionFont = _rtb.SelectionFont?.Style.HasFlag(FontStyle.Bold) == true
                ? new Font(_rtb.SelectionFont, _rtb.SelectionFont.Style & ~FontStyle.Bold)
                : new Font(_rtb.SelectionFont ?? _rtb.Font, (_rtb.SelectionFont?.Style ?? FontStyle.Regular) | FontStyle.Bold);
        }
        private void ToggleItalic()
        {
            _rtb.SelectionFont = _rtb.SelectionFont?.Style.HasFlag(FontStyle.Italic) == true
                ? new Font(_rtb.SelectionFont, _rtb.SelectionFont.Style & ~FontStyle.Italic)
                : new Font(_rtb.SelectionFont ?? _rtb.Font, (_rtb.SelectionFont?.Style ?? FontStyle.Regular) | FontStyle.Italic);
        }
        private void ToggleUnderline()
        {
            _rtb.SelectionFont = _rtb.SelectionFont?.Style.HasFlag(FontStyle.Underline) == true
                ? new Font(_rtb.SelectionFont, _rtb.SelectionFont.Style & ~FontStyle.Underline)
                : new Font(_rtb.SelectionFont ?? _rtb.Font, (_rtb.SelectionFont?.Style ?? FontStyle.Regular) | FontStyle.Underline);
        }
        private void ToggleStrike()
        {
            _rtb.SelectionFont = _rtb.SelectionFont?.Style.HasFlag(FontStyle.Strikeout) == true
                ? new Font(_rtb.SelectionFont, _rtb.SelectionFont.Style & ~FontStyle.Strikeout)
                : new Font(_rtb.SelectionFont ?? _rtb.Font, (_rtb.SelectionFont?.Style ?? FontStyle.Regular) | FontStyle.Strikeout);
        }
        private void FontSize(int delta)
        {
            var f = _rtb.SelectionFont ?? _rtb.Font;
            float sz = Math.Clamp(f.Size + delta, 8f, 72f);
            _rtb.SelectionFont = new Font(f.FontFamily, sz, f.Style);
        }
        private void SetAlign(HorizontalAlignment align)
        {
            _rtb.SelectionAlignment = align;
        }

        private Color GetNormalTextColor() => _rtb.ForeColor;

        private int GetLineContentLength(int lineStart, int lineLen)
        {
            int len = Math.Max(0, lineLen);
            while (len > 0)
            {
                char ch = _rtb.Text[lineStart + len - 1];
                if (ch == '\r' || ch == '\n') len--;
                else break;
            }
            return len;
        }

        private bool IsCaretOnCompletedTodoLine()
        {
            if (_rtb.SelectionLength > 0) return false;

            int lineIdx = _rtb.GetLineFromCharIndex(_rtb.SelectionStart);
            if (lineIdx < 0 || lineIdx >= _rtb.Lines.Length) return false;

            string lineText = _rtb.Lines[lineIdx] ?? string.Empty;
            return lineText.StartsWith("☑ ");
        }

        private void InsertTodo()
        {
            int oldStart = _rtb.SelectionStart;
            int oldLen = _rtb.SelectionLength;

            int startLine = _rtb.GetLineFromCharIndex(oldStart);
            int endChar = oldLen > 0 ? Math.Max(oldStart, oldStart + oldLen - 1) : oldStart;
            int endLine = _rtb.GetLineFromCharIndex(endChar);

            _rtb.SuspendLayout();
            try
            {
                int insertedBeforeCaret = 0;
                for (int i = startLine; i <= endLine; i++)
                {
                    int lineStart = _rtb.GetFirstCharIndexFromLine(i);
                    if (lineStart < 0) continue;

                    string lineText = i < _rtb.Lines.Length ? _rtb.Lines[i] : "";
                    if (lineText.StartsWith("☐ ") || lineText.StartsWith("☑ ")) continue;

                    _rtb.SelectionStart = lineStart;
                    _rtb.SelectionLength = 0;
                    _rtb.SelectionColor = GetNormalTextColor();
                    _rtb.SelectionFont = new Font(_rtb.Font, FontStyle.Regular);
                    _rtb.SelectedText = "☐ ";

                    if (lineStart <= oldStart) insertedBeforeCaret += 2;
                }

                _rtb.SelectionStart = Math.Min(_rtb.TextLength, oldStart + insertedBeforeCaret);
                _rtb.SelectionLength = 0;
            }
            finally
            {
                _rtb.ResumeLayout();
            }
        }

        private void RtbMouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            
            // 获取点击的字元序号（点在字符右半边会返回下一个字符的索引）
            int charIndex = _rtb.GetCharIndexFromPosition(e.Location);
            if (charIndex < 0 || charIndex >= _rtb.TextLength) return;

            int targetIndex = -1;
            char targetChar = '\0';

            // 检查当前字符及前后字符，看是否点到了复选框附近
            for (int i = Math.Max(0, charIndex - 1); i <= Math.Min(_rtb.TextLength - 1, charIndex + 1); i++)
            {
                char c = _rtb.Text[i];
                if (c == '☐' || c == '☑')
                {
                    Point pt = _rtb.GetPositionFromCharIndex(i);
                    // 放宽点击范围：左边距、右边距、上下距离
                    if (e.X >= pt.X - 10 && e.X <= pt.X + 35 && 
                        e.Y >= pt.Y - 10 && e.Y <= pt.Y + 35)
                    {
                        targetIndex = i;
                        targetChar = c;
                        break;
                    }
                }
            }

            if (targetIndex != -1)
            {
                _rtb.SuspendLayout();
                int oldStart = _rtb.SelectionStart;
                int oldLen = _rtb.SelectionLength;

                // 找到此行的范围
                int lineIdx = _rtb.GetLineFromCharIndex(targetIndex);
                int lineStart = _rtb.GetFirstCharIndexFromLine(lineIdx);
                int nextLineStart = lineIdx < _rtb.Lines.Length - 1 ? _rtb.GetFirstCharIndexFromLine(lineIdx + 1) : _rtb.TextLength;
                int lineLen = nextLineStart - lineStart;

                if (targetChar == '☐')
                {
                    // 改为已完成 ☑
                    _rtb.SelectionStart = targetIndex;
                    _rtb.SelectionLength = 1;
                    _rtb.SelectionColor = GetNormalTextColor();
                    _rtb.SelectedText = "☑";
                    
                    // 整行变灰并加删除线（跳过框和空格，且不包含换行符）
                    int lineContentLen = GetLineContentLength(lineStart, lineLen);
                    if (lineContentLen > 2)
                    {
                        _rtb.SelectionStart = lineStart + 2;
                        _rtb.SelectionLength = lineContentLen - 2;
                        _rtb.SelectionColor = Color.Gray;
                        _rtb.SelectionFont = new Font(_rtb.Font, FontStyle.Strikeout);
                    }
                }
                else
                {
                    // 改为未完成 ☐
                    _rtb.SelectionStart = targetIndex;
                    _rtb.SelectionLength = 1;
                    _rtb.SelectionColor = GetNormalTextColor();
                    _rtb.SelectedText = "☐";

                    // 整行恢复正常（不包含换行符）
                    int lineContentLen = GetLineContentLength(lineStart, lineLen);
                    if (lineContentLen > 2)
                    {
                        _rtb.SelectionStart = lineStart + 2;
                        _rtb.SelectionLength = lineContentLen - 2;
                        _rtb.SelectionColor = GetNormalTextColor();
                        _rtb.SelectionFont = new Font(_rtb.Font, FontStyle.Regular);
                    }
                }

                _rtb.SelectionStart = oldStart;
                _rtb.SelectionLength = oldLen;
                _rtb.ResumeLayout();
                ScheduleSave();
            }
        }
        private void InsertSeparator()
        {
            _rtb.SelectedText = "\n────────────────\n";
        }

        // ══════════════════════════════════════════════════════════════
        //  其他操作
        // ══════════════════════════════════════════════════════════════
        private void ToggleTopmost()
        {
            _isTopmost = !_isTopmost;
            this.TopMost = _isTopmost;
            if (_current != null) { _current.IsTopmost = _isTopmost; NoteManager.SaveNotes(); }
            ShowStatus(_isTopmost ? "📌 已置顶" : "取消置顶");
        }

        private void DeleteCurrentNote()
        {
            if (_current == null) return;
            if (!Confirm($"确定要删除便签 '{_current.Title}' 吗？")) return;
            NoteManager.RemoveNote(_current);
            _notes.Remove(_current);
            _current = null;
            _tabPanel.SetNotes(_notes, _notes.Count > 0 ? _notes[0] : null);
            if (_notes.Count > 0) SwitchTo(_notes[0]);
            else UpdateEmptyState();
        }

        private void Export()
        {
            if (_current == null) return;
            string path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"StickyNote_{_current.Title}.txt");
            System.IO.File.WriteAllText(path, _rtb.Text);
            ShowStatus($"已导出: {System.IO.Path.GetFileName(path)}");
        }

        private void UpdateEmptyState()
        {
            bool empty = _current == null || _notes.Count == 0;
            _rtb.Visible = !empty;
            _toolbar.Visible = !empty;
            var lbl = _contentArea.Controls["EmptyLabel"];
            if (lbl != null) lbl.Visible = empty;
        }

        private void ShowStatus(string msg)
        {
            _statusMsg = msg;
            _titleBar.Invalidate();
            _statusTimer.Stop();
            _statusTimer.Start();
        }

        private bool Confirm(string msg) =>
            MessageBox.Show(msg, "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        private string? InputDialog(string title, string prompt, string defaultValue)
        {
            using var dlg = new Form
            {
                Text = title, Width = 300, Height = 140,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false
            };
            var lbl = new Label { Left = 10, Top = 12, Width = 260, Text = prompt };
            var tb = new TextBox { Left = 10, Top = 32, Width = 260, Text = defaultValue };
            var ok = new Button { Left = 120, Top = 64, Width = 70, Text = "确定", DialogResult = DialogResult.OK };
            var cancel = new Button { Left = 200, Top = 64, Width = 70, Text = "取消", DialogResult = DialogResult.Cancel };
            dlg.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
            dlg.AcceptButton = ok; dlg.CancelButton = cancel;
            tb.SelectAll();
            return dlg.ShowDialog(this) == DialogResult.OK ? tb.Text : null;
        }

        public void ShowSettings() => ShowSettingsDialog();
        public void ShowAbout() => ShowAboutDialog();

        private void ShowSettingsDialog()
        {
            var s = NoteManager.Settings;

            using var dlg = new Form
            {
                Text = "设置",
                Width = 520,
                Height = 280,
                MinimumSize = new Size(420, 220),
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Font = this.Font
            };

            var tips = new Label
            {
                Dock = DockStyle.Top,
                Height = 42,
                Padding = new Padding(10, 10, 10, 0),
                ForeColor = Color.FromArgb(120, 62, 39, 35),
                Text = "提示：当启用“自动换行”时，水平滚动条会被自动忽略。"
            };

            var optionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 8, 10, 8),
                AutoScroll = true,
                WrapContents = true,
                FlowDirection = FlowDirection.LeftToRight
            };

            var chkWrap = new CheckBox { AutoSize = true, Margin = new Padding(6), Text = "编辑区自动换行", Checked = s.EditorWordWrap };
            var chkHScroll = new CheckBox { AutoSize = true, Margin = new Padding(6), Text = "显示水平滚动条", Checked = s.ShowHorizontalScrollBar };
            var chkVScroll = new CheckBox { AutoSize = true, Margin = new Padding(6), Text = "显示垂直滚动条", Checked = s.ShowVerticalScrollBar };
            var chkToolbarWrap = new CheckBox { AutoSize = true, Margin = new Padding(6), Text = "底部工具按钮随窗口宽度自动换行", Checked = s.ToolbarAutoWrap };

            void SyncOptionState()
            {
                chkHScroll.Enabled = !chkWrap.Checked;
            }

            chkWrap.CheckedChanged += (sender, args) => SyncOptionState();
            SyncOptionState();

            optionsPanel.Controls.Add(chkWrap);
            optionsPanel.Controls.Add(chkHScroll);
            optionsPanel.Controls.Add(chkVScroll);
            optionsPanel.Controls.Add(chkToolbarWrap);

            var actionPanel = new Panel { Dock = DockStyle.Bottom, Height = 48 };
            var ok = new Button { Text = "确定", Width = 80, Height = 30, Left = dlg.ClientSize.Width - 180, Top = 9, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "取消", Width = 80, Height = 30, Left = dlg.ClientSize.Width - 92, Top = 9, Anchor = AnchorStyles.Right | AnchorStyles.Top, DialogResult = DialogResult.Cancel };
            actionPanel.Controls.Add(ok);
            actionPanel.Controls.Add(cancel);

            dlg.Controls.Add(optionsPanel);
            dlg.Controls.Add(actionPanel);
            dlg.Controls.Add(tips);
            dlg.AcceptButton = ok;
            dlg.CancelButton = cancel;

            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            s.EditorWordWrap = chkWrap.Checked;
            s.ShowHorizontalScrollBar = chkHScroll.Checked;
            s.ShowVerticalScrollBar = chkVScroll.Checked;
            s.ToolbarAutoWrap = chkToolbarWrap.Checked;

            NoteManager.SaveSettings();
            ApplyEditorDisplaySettings();
            ReflowToolbar();
            ShowStatus("设置已应用");
        }

        private void ShowAboutDialog()
        {
            var version = Application.ProductVersion;
            MessageBox.Show(
                $"StickyNote 便签\n版本: v{version}\n\n主要能力：\n- 富文本编辑与标签管理\n- 托盘常驻与全局快捷键\n- 自动保存与轻量内存占用",
                "关于本软件",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        // ══════════════════════════════════════════════════════════════
        //  定时器
        // ══════════════════════════════════════════════════════════════
        private void SetupTimers()
        {
            _saveTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); NoteManager.SaveNotes(); };

            _statusTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _statusTimer.Tick += (s, e) => { _statusTimer.Stop(); _statusMsg = ""; _titleBar.Invalidate(); };

            _memTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _memTimer.Tick += (s, e) =>
            {
                GC.Collect(2, GCCollectionMode.Optimized, false);
                SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
            };
            _memTimer.Start();
        }

        // ══════════════════════════════════════════════════════════════
        //  无边框窗口 - WM_NCHITTEST（支持拖动 + 8方向缩放）
        // ══════════════════════════════════════════════════════════════
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && this.WindowState == FormWindowState.Normal)
            {
                var pt = PointToClient(Cursor.Position);
                int r = RESIZE_BORDER;
                int w = Width, h = Height;

                if      (pt.X <= r && pt.Y <= r)            m.Result = (IntPtr)HTTOPLEFT;
                else if (pt.X >= w - r && pt.Y <= r)         m.Result = (IntPtr)HTTOPRIGHT;
                else if (pt.X <= r && pt.Y >= h - r)         m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (pt.X >= w - r && pt.Y >= h - r)     m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (pt.X <= r)                          m.Result = (IntPtr)HTLEFT;
                else if (pt.X >= w - r)                      m.Result = (IntPtr)HTRIGHT;
                else if (pt.Y <= r)                          m.Result = (IntPtr)HTTOP;
                else if (pt.Y >= h - r)                      m.Result = (IntPtr)HTBOTTOM;
                else                                          base.WndProc(ref m);
                return;
            }
            base.WndProc(ref m);
        }

        // ══════════════════════════════════════════════════════════════
        //  DWM 系统阴影
        // ══════════════════════════════════════════════════════════════
        private void ApplyDwmShadow()
        {
            try
            {
                int v = 2;
                DwmSetWindowAttribute(this.Handle, 2, ref v, 4); // DWMWA_NCRENDERING_POLICY
                var m = new MARGINS { Left = 1, Right = 1, Top = 1, Bottom = 1 };
                DwmExtendFrameIntoClientArea(this.Handle, ref m);
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyDwmShadow();
        }

        // ══════════════════════════════════════════════════════════════
        //  工具栏按钮图标绘制方法
        // ══════════════════════════════════════════════════════════════
        private Color FgColor => ColorTranslator.FromHtml("#5D4037");

        private void DrawBold(Graphics g, Rectangle r)
        {
            using var f = new Font("Georgia", 11f, FontStyle.Bold);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("B", f, b, r, sf);
        }
        private void DrawItalic(Graphics g, Rectangle r)
        {
            using var f = new Font("Georgia", 11f, FontStyle.Italic);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("I", f, b, r, sf);
        }
        private void DrawUnderline(Graphics g, Rectangle r)
        {
            using var f = new Font("Georgia", 11f, FontStyle.Underline);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("U", f, b, r, sf);
        }
        private void DrawStrike(Graphics g, Rectangle r)
        {
            using var f = new Font("Georgia", 10f, FontStyle.Strikeout);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("S", f, b, r, sf);
        }
        private void DrawFontBig(Graphics g, Rectangle r)
        {
            using var f = new Font("Arial", 13f, FontStyle.Bold);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("A", f, b, r, sf);
        }
        private void DrawFontSmall(Graphics g, Rectangle r)
        {
            using var f = new Font("Arial", 9f);
            using var b = new SolidBrush(FgColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("A", f, b, r, sf);
        }
        private void DrawAlignLeft(Graphics g, Rectangle r)
        {
            using var p = new Pen(FgColor, 1.5f);
            int cx = r.Left + 4, w = r.Width - 8, y = r.Top + 5;
            g.DrawLine(p, cx, y, cx + w, y);     y += 5;
            g.DrawLine(p, cx, y, cx + w - 5, y); y += 5;
            g.DrawLine(p, cx, y, cx + w, y);     y += 5;
            g.DrawLine(p, cx, y, cx + w - 5, y);
        }
        private void DrawAlignCenter(Graphics g, Rectangle r)
        {
            using var p = new Pen(FgColor, 1.5f);
            int cx = r.Left + 3, w = r.Width - 6, y = r.Top + 5;
            g.DrawLine(p, cx, y, cx + w, y);       y += 5;
            g.DrawLine(p, cx + 3, y, cx + w - 3, y); y += 5;
            g.DrawLine(p, cx, y, cx + w, y);       y += 5;
            g.DrawLine(p, cx + 3, y, cx + w - 3, y);
        }
        private void DrawAlignRight(Graphics g, Rectangle r)
        {
            using var p = new Pen(FgColor, 1.5f);
            int cx = r.Left + 4, rr = r.Right - 4, w = rr - cx, y = r.Top + 5;
            g.DrawLine(p, cx, y, rr, y);       y += 5;
            g.DrawLine(p, cx + 5, y, rr, y);   y += 5;
            g.DrawLine(p, cx, y, rr, y);       y += 5;
            g.DrawLine(p, cx + 5, y, rr, y);
        }
        private void DrawTodo(Graphics g, Rectangle r)
        {
            using var p = new Pen(FgColor, 1.5f);
            int s = 13, ox = r.Left + (r.Width - s) / 2, oy = r.Top + (r.Height - s) / 2;
            g.DrawRectangle(p, ox, oy, s, s);
        }
        private void DrawSep(Graphics g, Rectangle r)
        {
            using var p = new Pen(FgColor, 1.5f);
            int cy = r.Top + r.Height / 2;
            g.DrawLine(p, r.Left + 3, cy, r.Right - 3, cy);
        }

        // ══════════════════════════════════════════════════════════════
        //  工具方法
        // ══════════════════════════════════════════════════════════════
        public static Color Darken(Color c, float f) =>
            Color.FromArgb(c.A, (int)(c.R * (1 - f)), (int)(c.G * (1 - f)), (int)(c.B * (1 - f)));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _saveTimer?.Dispose();
                _statusTimer?.Dispose();
                _memTimer?.Dispose();
            }
            base.Dispose(disposing);
        }

        // 公开访问当前便签 ID（供 NoteSearch 使用）
        public string? CurrentNoteId => _current?.Id;
        public void FocusNote(string id)
        {
            var n = _notes.Find(x => x.Id == id);
            if (n != null) SwitchTo(n);
            this.Activate();
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  控制按钮面板（标题栏右侧）
    // ══════════════════════════════════════════════════════════════════
    internal class ControlButtonPanel : Panel
    {
        public event EventHandler? NewClicked;
        public event EventHandler? PaletteClicked;
        public event EventHandler? PinClicked;
        public event EventHandler? MinimizeClicked;
        public event EventHandler? CloseClicked;

        private readonly StickyForm _owner;
        private int _hoverBtn = -1;
        private bool _isPinned = false;
        private Color _tintColor = ColorTranslator.FromHtml("#E8D096");

        public Color TintColor
        {
            get => _tintColor;
            set => _tintColor = value;
        }

        // 按钮区：New | Palette | Pin | Min | Close
        // Width=150, 5 buttons × 30px
        public ControlButtonPanel(StickyForm owner)
        {
            _owner = owner;
            this.Width = 150;
            DoubleBuffered = true;
            // 支持透明背景
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            this.BackColor = Color.Transparent;
            this.MouseMove  += (s, e) => { int b = e.X / 30; if (b != _hoverBtn) { _hoverBtn = b; Invalidate(); } };
            this.MouseLeave += (s, e) => { _hoverBtn = -1; Invalidate(); };
            this.MouseDown  += OnMouseDown;
        }

        public bool IsPinned { get => _isPinned; set { _isPinned = value; Invalidate(); } }

        private void OnMouseDown(object? s, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int btn = e.X / 30;
            switch (btn)
            {
                case 0: NewClicked?.Invoke(this, EventArgs.Empty); break;
                case 1: PaletteClicked?.Invoke(this, EventArgs.Empty); break;
                case 2: _isPinned = !_isPinned; PinClicked?.Invoke(this, EventArgs.Empty); Invalidate(); break;
                case 3: MinimizeClicked?.Invoke(this, EventArgs.Empty); break;
                case 4: CloseClicked?.Invoke(this, EventArgs.Empty); break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 用父控件相同的颜色作背景，不用透明（避免黑色）
            g.Clear(_tintColor);
            var fg = StickyForm.Darken(_tintColor, 0.45f);

            for (int i = 0; i < 5; i++)
            {
                var rc = new Rectangle(i * 30, 0, 30, Height);
                if (_hoverBtn == i)
                {
                    bool isClose = i == 4;
                    using var hb = new SolidBrush(isClose
                        ? Color.FromArgb(50, 200, 50, 50)
                        : Color.FromArgb(40, 93, 64, 55));
                    g.FillRectangle(hb, rc);
                }

                using var b = new SolidBrush(fg);
                using var p = new Pen(fg, 1.5f);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                int cx = rc.Left + 15, cy = rc.Top + rc.Height / 2;

                switch (i)
                {
                    case 0: // 新建 (+)
                        using (var fnt = new Font("Arial", 16f)) g.DrawString("+", fnt, b, rc, sf);
                        break;
                    case 1: // 调色板
                        using (var fnt = new Font("Segoe UI Emoji", 10.5f)) g.DrawString("🎨", fnt, b, rc, sf);
                        break;
                    case 2: // 图钉
                        DrawPin(g, cx, cy, _isPinned, fg);
                        break;
                    case 3: // 最小化
                        g.DrawLine(p, cx - 5, cy + 2, cx + 5, cy + 2);
                        break;
                    case 4: // 关闭
                        g.DrawLine(p, cx - 5, cy - 5, cx + 5, cy + 5);
                        g.DrawLine(p, cx + 5, cy - 5, cx - 5, cy + 5);
                        break;
                }
            }
        }

        private static void DrawPin(Graphics g, int cx, int cy, bool pinned, Color fg)
        {
            using var p = new Pen(fg, 1.5f);
            using var brush = new SolidBrush(fg);
            if (pinned)
            {
                // 垂直图钉（竖直）缩小针的比例，增大钉帽
                g.DrawLine(p, cx, cy - 4, cx, cy + 5);
                g.DrawEllipse(p, cx - 3, cy - 8, 6, 6);
                g.FillEllipse(brush, cx - 3, cy - 8, 6, 6);
                g.DrawLine(p, cx - 2, cy + 5, cx + 2, cy + 5);
            }
            else
            {
                // 倾斜图钉
                g.TranslateTransform(cx, cy);
                g.RotateTransform(45);
                g.DrawLine(p, 0, -4, 0, 5);
                g.DrawEllipse(p, -3, -8, 6, 6);
                g.DrawLine(p, -2, 5, 2, 5);
                g.ResetTransform();
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  工具栏小图标按钮
    // ══════════════════════════════════════════════════════════════════
    internal class ToolIconButton : Control
    {
        public event EventHandler? Clicked;
        public string ToolTipText { get; set; } = "";

        private readonly Action<Graphics, Rectangle> _drawFunc;
        private bool _hover = false;
        private static readonly ToolTip _tip = new();

        public ToolIconButton(Action<Graphics, Rectangle> drawFunc)
        {
            _drawFunc = drawFunc;
            DoubleBuffered = true;
            // 不加 SupportsTransparentBackColor，直接用父色绘制背景，更稳定
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.Cursor = Cursors.Hand;
            this.MouseEnter += (s, e) => { _hover = true; Invalidate(); _tip.SetToolTip(this, ToolTipText); };
            this.MouseLeave += (s, e) => { _hover = false; Invalidate(); };
            this.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Clicked?.Invoke(this, EventArgs.Empty); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 用父容器背景色填充，避免出现黑色
            var bg = Parent?.BackColor ?? SystemColors.Control;
            g.Clear(bg);

            if (_hover)
            {
                // 悬停：在背景色上叠加半透明圆角高亮
                using var hb = new SolidBrush(Color.FromArgb(50, 62, 39, 35));
                g.FillRoundedRectangle(hb, new Rectangle(1, 1, Width - 2, Height - 2), 3);
            }
            _drawFunc(g, this.ClientRectangle);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    //  GDI+ 扩展：圆角矩形
    // ══════════════════════════════════════════════════════════════════
    internal static class GraphicsEx
    {
        public static void FillRoundedRectangle(this Graphics g, Brush b, Rectangle r, int radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
            path.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
            path.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(b, path);
        }
    }
}
