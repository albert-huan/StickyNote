using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace StickyNote
{
    /// <summary>
    /// 左侧标签面板：GDI+ 全自绘，支持悬停高亮、选中状态
    /// </summary>
    public class TabPanel : Panel
    {
        // ── 事件 ──────────────────────────────────────────────────
        public event EventHandler<NoteData>? TabSelected;
        public event EventHandler<NoteData>? TabDeleteRequested;
        public event EventHandler<NoteData>? TabRenameRequested;
        public event EventHandler? NewTabRequested;

        // ── 数据 ──────────────────────────────────────────────────
        private readonly List<NoteData> _notes = new();
        private NoteData? _selected;
        private int _hoverIndex = -1;
        private int _scrollOffset = 0;
        private const int ItemH = 52;
        private const int FooterH = 36;
        private Color _baseColor = ColorTranslator.FromHtml("#E8D096");

        // ── 动画 ──────────────────────────────────────────────────
        private System.Windows.Forms.Timer _animTimer;
        private float _animAlpha = 0f;

        public TabPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            this.MouseMove += OnMouseMove;
            this.MouseLeave += (s, e) => { _hoverIndex = -1; Invalidate(); };
            this.MouseDown += OnMouseDown;
            this.MouseWheel += (s, e) => { _scrollOffset = Math.Max(0, _scrollOffset - e.Delta / 3); Invalidate(); };

            _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _animTimer.Tick += (s, e) => { _animAlpha = Math.Min(1f, _animAlpha + 0.12f); if (_animAlpha >= 1f) _animTimer.Stop(); Invalidate(); };
        }

        public Color BaseColor
        {
            get => _baseColor;
            set { _baseColor = value; Invalidate(); }
        }

        public void SetNotes(List<NoteData> notes, NoteData? selected)
        {
            _notes.Clear();
            _notes.AddRange(notes);
            _selected = selected;
            _animAlpha = 0f;
            _animTimer.Start();
            // P1: 确保所有便签预览缓存已生成
            foreach (var n in _notes)
                if (string.IsNullOrEmpty(n.PlainPreview) && !string.IsNullOrEmpty(n.Content))
                    n.PlainPreview = BuildPreview(n.Content);
            Invalidate();
        }

        public void SelectNote(NoteData? note)
        {
            _selected = note;
            Invalidate();
        }

        public void UpdateNote(NoteData note)
        {
            var idx = _notes.FindIndex(n => n.Id == note.Id);
            if (idx >= 0) { _notes[idx] = note; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 背景：比主区域稍深的纸色
            var bgColor = Darken(_baseColor, 0.08f);
            g.Clear(bgColor);

            int y = -_scrollOffset;
            int w = Width;

            for (int i = 0; i < _notes.Count; i++)
            {
                var note = _notes[i];
                bool isSelected = note.Id == _selected?.Id;
                bool isHover = i == _hoverIndex;

                // 选中背景
                if (isSelected)
                {
                    var selBrush = new SolidBrush(Color.FromArgb(70, 93, 64, 55));
                    g.FillRectangle(selBrush, 0, y, w, ItemH);
                    selBrush.Dispose();
                    // 左侧选中条
                    using var accentBrush = new SolidBrush(Darken(_baseColor, 0.4f));
                    g.FillRectangle(accentBrush, 0, y + 4, 3, ItemH - 8);
                }
                else if (isHover)
                {
                    var hBrush = new SolidBrush(Color.FromArgb(35, 93, 64, 55));
                    g.FillRectangle(hBrush, 0, y, w, ItemH);
                    hBrush.Dispose();
                }

                // 标题
                string title = string.IsNullOrEmpty(note.Title) ? "便签" : note.Title;
                if (title.Length > 12) title = title[..12] + "…";
                using var titleFont = new Font("Microsoft YaHei", 9f, FontStyle.Bold);
                using var titleBrush = new SolidBrush(Color.FromArgb(isSelected ? 255 : 200, 62, 39, 35));
                g.DrawString(title, titleFont, titleBrush, new RectangleF(8, y + 6, w - 12, 20));

                // 预览 (P1: 直接读取缓存)
                string preview = note.PlainPreview ?? "";
                if (!string.IsNullOrEmpty(preview))
                {
                    using var previewFont = new Font("Microsoft YaHei", 7.5f);
                    using var previewBrush = new SolidBrush(Color.FromArgb(140, 93, 64, 55));
                    g.DrawString(preview, previewFont, previewBrush, new RectangleF(8, y + 26, w - 12, 20));
                }

                // 分隔线
                using var sepPen = new Pen(Color.FromArgb(30, 93, 64, 55), 1);
                g.DrawLine(sepPen, 4, y + ItemH - 1, w - 4, y + ItemH - 1);

                // U2: 右侧便签颜色圆点
                try
                {
                    var dotColor = ColorTranslator.FromHtml(note.ColorHex);
                    using var dotBrush = new SolidBrush(dotColor);
                    using var dotPen = new Pen(Darken(dotColor, 0.25f), 1f);
                    g.FillEllipse(dotBrush, w - 14, y + (ItemH / 2) - 5, 10, 10);
                    g.DrawEllipse(dotPen,  w - 14, y + (ItemH / 2) - 5, 10, 10);
                }
                catch { }

                y += ItemH;
            }

            // 底部新建按钮区域
            int footerY = Height - FooterH;
            using var footerBrush = new SolidBrush(Color.FromArgb(40, 93, 64, 55));
            g.FillRectangle(footerBrush, 0, footerY, w, FooterH);

            bool footerHover = _hoverIndex == _notes.Count;
            using var plusFont = new Font("Microsoft YaHei", 18f, FontStyle.Regular);
            using var plusBrush = new SolidBrush(footerHover
                ? Color.FromArgb(200, 62, 39, 35)
                : Color.FromArgb(130, 93, 64, 55));
            var plusRect = new RectangleF(0, footerY, w, FooterH);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("+", plusFont, plusBrush, plusRect, sf);
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            int footerY = Height - FooterH;
            if (e.Y >= footerY)
            {
                int newHover = _notes.Count;
                if (newHover != _hoverIndex) { _hoverIndex = newHover; Invalidate(); }
                return;
            }
            int idx = (e.Y + _scrollOffset) / ItemH;
            if (idx < 0 || idx >= _notes.Count) idx = -1;
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            int footerY = Height - FooterH;
            if (e.Y >= footerY)
            {
                NewTabRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            int idx = (e.Y + _scrollOffset) / ItemH;
            if (idx < 0 || idx >= _notes.Count) return;
            var note = _notes[idx];

            if (e.Button == MouseButtons.Left)
            {
                // U3: 双击重命名
                if (e.Clicks == 2)
                {
                    TabRenameRequested?.Invoke(this, note);
                    return;
                }
                _selected = note;
                Invalidate();
                TabSelected?.Invoke(this, note);
            }
            else if (e.Button == MouseButtons.Right)
            {
                _selected = note;
                Invalidate();
                ShowContextMenu(note, e.Location);
            }
        }

        private void ShowContextMenu(NoteData note, Point pt)
        {
            var menu = new ContextMenuStrip();
            var rename = new ToolStripMenuItem("重命名");
            var delete = new ToolStripMenuItem("删除") { ForeColor = Color.Crimson };
            rename.Click += (s, e) => TabRenameRequested?.Invoke(this, note);
            delete.Click += (s, e) => TabDeleteRequested?.Invoke(this, note);
            menu.Items.Add(rename);
            menu.Items.Add(delete);
            menu.Show(this, pt);
        }

        public static string BuildPreview(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            string text = content.StartsWith("{\\rtf") ? StripRtf(content) : content;
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string preview = lines.Length > 1 ? lines[1] : (lines.Length == 1 ? lines[0] : "");
            if (preview.Length > 22) preview = preview[..22] + "…";
            return preview;
        }

        internal static string StripRtf(string rtf)
        {
            // B1: 正则剥离，不创建 RichTextBox 实例
            if (string.IsNullOrEmpty(rtf)) return "";
            try
            {
                // 1. 去除 RTF 控制块（花括号嵌套）和控制字
                string s = Regex.Replace(rtf, @"\\([a-z]+)(-?\d+)? ?", " ");   // \cmd123
                s = Regex.Replace(s, @"\{[^{}]*\}", " ");                       // 嵌套块
                s = Regex.Replace(s, @"\\[\\{}*]", "");                          // \\, \{, \}
                s = Regex.Replace(s, @"[\{\}]", "");                             // 剩余括号
                // 2. 合并空白
                s = Regex.Replace(s, @"[ \t]+", " ").Trim();
                return s;
            }
            catch { return ""; }
        }

        private static Color Darken(Color c, float amount)
        {
            return Color.FromArgb(c.A,
                (int)(c.R * (1 - amount)),
                (int)(c.G * (1 - amount)),
                (int)(c.B * (1 - amount)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _animTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
