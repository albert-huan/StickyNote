using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace StickyNote
{
    /// <summary>
    /// 搜索便签对话框（WinForms 版本）
    /// </summary>
    public static class NoteSearchForm
    {
        public static void Show(IReadOnlyList<NoteData> notes, Action<string>? onActivate = null)
        {
            var dlg = new Form
            {
                Text             = "搜索便签",
                Width            = 440,
                Height           = 520,
                StartPosition    = FormStartPosition.CenterScreen,
                TopMost          = true,
                Font             = new Font("Microsoft YaHei", 9f),
                BackColor        = Color.FromArgb(0xFD, 0xF6, 0xE3),
                FormBorderStyle  = FormBorderStyle.FixedSingle,
                MaximizeBox      = false
            };

            var searchBox = new TextBox
            {
                Dock        = DockStyle.None,
                Left        = 10, Top = 10, Width = 400,
                Font        = new Font("Microsoft YaHei", 11f),
                PlaceholderText = "输入关键词搜索…",
                BorderStyle = BorderStyle.FixedSingle
            };

            var listBox = new ListBox
            {
                Left        = 10, Top = 44,
                Width       = 400, Height = 400,
                Font        = new Font("Microsoft YaHei", 10f),
                BorderStyle = BorderStyle.None,
                ItemHeight  = 42,
                DrawMode    = DrawMode.OwnerDrawFixed,
                BackColor   = Color.FromArgb(0xFD, 0xF6, 0xE3)
            };

            // 搜索结果数据
            var results = new List<NoteData>();

            void Refresh()
            {
                string q = searchBox.Text.Trim().ToLower();
                results.Clear();
                foreach (var n in notes)
                {
                    string plain = GetPlain(n.Content);
                    string tags = BuildTagsText(n);
                    if (string.IsNullOrEmpty(q)
                        || n.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || plain.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || tags.Contains(q, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(n);
                    }
                }
                listBox.BeginUpdate();
                listBox.Items.Clear();
                foreach (var r in results)
                    listBox.Items.Add(r.Title.Length > 0 ? r.Title : "便签");
                listBox.EndUpdate();
            }

            // 自绘列表项
            listBox.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= results.Count) return;
                e.DrawBackground();
                var note = results[e.Index];
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                bool sel = (e.State & DrawItemState.Selected) != 0;
                var bgColor = sel ? Color.FromArgb(180, 227, 200, 135) : Color.FromArgb(0xFD, 0xF6, 0xE3);
                g.FillRectangle(new SolidBrush(bgColor), e.Bounds);

                // 左色条
                var noteColor = TryParse(note.ColorHex, Color.FromArgb(0xE3, 0xC8, 0x87));
                g.FillRectangle(new SolidBrush(noteColor), e.Bounds.Left, e.Bounds.Top + 4, 4, e.Bounds.Height - 8);

                // 标题
                string title = string.IsNullOrEmpty(note.Title) ? "便签" : note.Title;
                using (var tf = new Font("Microsoft YaHei", 9.5f, FontStyle.Bold))
                using (var tb = new SolidBrush(Color.FromArgb(62, 39, 35)))
                    g.DrawString(title, tf, tb, e.Bounds.Left + 12, e.Bounds.Top + 5);

                // 预览
                string preview = GetPreviewLine(GetPlain(note.Content));
                string tagsText = BuildTagsText(note);
                string summary = string.IsNullOrEmpty(tagsText) ? preview : $"{preview}   #{tagsText}";
                using (var pf = new Font("Microsoft YaHei", 8f))
                using (var pb = new SolidBrush(Color.FromArgb(130, 93, 64, 55)))
                    g.DrawString(summary, pf, pb, e.Bounds.Left + 12, e.Bounds.Top + 23);

                // 分隔线
                g.DrawLine(new Pen(Color.FromArgb(30, 93, 64, 55)), e.Bounds.Left + 8, e.Bounds.Bottom - 1, e.Bounds.Right - 8, e.Bounds.Bottom - 1);
            };

            searchBox.TextChanged += (s, e) => Refresh();

            void Activate()
            {
                if (listBox.SelectedIndex < 0 || listBox.SelectedIndex >= results.Count) return;
                onActivate?.Invoke(results[listBox.SelectedIndex].Id);
                dlg.Close();
            }

            listBox.MouseDoubleClick += (s, e) => Activate();
            listBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) Activate(); };
            searchBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Down && listBox.Items.Count > 0) { listBox.SelectedIndex = 0; listBox.Focus(); }
                if (e.KeyCode == Keys.Enter) Activate();
            };

            dlg.Controls.Add(searchBox);
            dlg.Controls.Add(listBox);

            Refresh();
            dlg.ShowDialog();
        }

        private static string GetPlain(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            if (content.StartsWith("{\\rtf")) return TabPanel.StripRtf(content);
            return content;
        }

        private static string GetPreviewLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            string preview = lines.Length > 1 ? lines[1] : "";
            if (preview.Length > 50) preview = preview[..50] + "…";
            return preview;
        }

        private static string BuildTagsText(NoteData note)
        {
            if (note.Tags == null || note.Tags.Count == 0) return "";
            return string.Join(" ", note.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()));
        }

        private static Color TryParse(string hex, Color fallback)
        {
            try { return ColorTranslator.FromHtml(hex); } catch { return fallback; }
        }
    }
}
