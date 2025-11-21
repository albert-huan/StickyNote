using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Documents;
using System.Windows.Media.Animation;

namespace StickyNote
{
    public partial class MainWindow : Window
    {
        // 当前便签的数据实体
        private NoteData _noteData;
        private bool _isInitializing = true;
        private DispatcherTimer _saveTimer;
        private bool _isSnapping = false;
        private DispatcherTimer _toastTimer;
        public static readonly RoutedUICommand NewCmd = new RoutedUICommand("New", "New", typeof(MainWindow));
        public static readonly RoutedUICommand DeleteCmd = new RoutedUICommand("Delete", "Delete", typeof(MainWindow));
        public static readonly RoutedUICommand ToggleTopmostCmd = new RoutedUICommand("Topmost", "Topmost", typeof(MainWindow));
        public static readonly RoutedUICommand CloseCmd = new RoutedUICommand("Close", "Close", typeof(MainWindow));
        public static readonly RoutedUICommand ExportCmd = new RoutedUICommand("Export", "Export", typeof(MainWindow));
        public static readonly RoutedUICommand InsertSeparatorCmd = new RoutedUICommand("Separator", "Separator", typeof(MainWindow));
        public string NoteId => _noteData.Id;

        // 构造函数：支持传入已有的数据
        public MainWindow(NoteData? data = null)
        {
            InitializeComponent();

            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    using var s = System.IO.File.OpenRead(icoPath);
                    var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(s, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    this.Icon = decoder.Frames[0];
                }
                else
                {
                    var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
                    if (exeIcon != null)
                    {
                        var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(exeIcon.Handle, System.Windows.Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        this.Icon = src;
                    }
                }
            }
            catch { }

            if (data == null)
            {
                _noteData = new NoteData
                {
                    Id = Guid.NewGuid().ToString(),
                    Content = "",
                    Width = 300,
                    Height = 300,
                    Left = SystemParameters.WorkArea.Width / 2 - 150,
                    Top = SystemParameters.WorkArea.Height / 2 - 150,
                    ColorHex = "#E8D096",
                    Opacity = 1.0,
                    IsTopmost = false,
                    FontSize = 16,
                    IsBold = false,
                    IsItalic = false,
                    IsUnderline = false,
                    IsStrikethrough = false,
                    Alignment = TextAlignment.Left.ToString()
                };
                NoteManager.AddNote(_noteData);
            }
            else
            {
                // 加载已有便签
                _noteData = data;
            }

            // 应用数据到界面
            ApplyDataToUI();
            _isInitializing = false;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); NoteManager.SaveNotes(); };
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); StatusText.Opacity = 0; };

            this.CommandBindings.Add(new CommandBinding(NewCmd, (s, e) => NewNote_Click(s, e)));
            this.CommandBindings.Add(new CommandBinding(DeleteCmd, (s, e) => DeleteNote_Click(s, e)));
            this.CommandBindings.Add(new CommandBinding(ToggleTopmostCmd, (s, e) => { btnTopmost.IsChecked = !(btnTopmost.IsChecked == true); PinButton_Click(s, e); }));
            this.CommandBindings.Add(new CommandBinding(CloseCmd, (s, e) => CloseButton_Click(s, e)));
            this.CommandBindings.Add(new CommandBinding(ExportCmd, (s, e) => Export_Click(s, e)));
            this.CommandBindings.Add(new CommandBinding(InsertSeparatorCmd, (s, e) => InsertSeparator()));

            this.SizeChanged += (s, e) => SaveState();
            this.LocationChanged += (s, e) => { SnapToEdges(); SaveState(); };
        }

        // 将数据渲染到 UI
        private void ApplyDataToUI()
        {
            this.Width = _noteData.Width;
            this.Height = _noteData.Height;
            this.Left = _noteData.Left;
            this.Top = _noteData.Top;
            this.Topmost = _noteData.IsTopmost;
            this.Opacity = _noteData.Opacity;
            SetDocumentFromContent(_noteData.Content);
            btnTopmost.IsChecked = _noteData.IsTopmost;

            try
            {
                MainBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_noteData.ColorHex));
            }
            catch { }

            EnsureOnScreen(this.Width, this.Height);
            UpdateShadowForDpi();
            UpdateForegroundForBackground(_noteData.ColorHex);
            UpdatePaperBrush(_noteData.ColorHex);
            if (_noteData.FontSize > 0) rtbContent.FontSize = _noteData.FontSize;
            rtbContent.FontWeight = _noteData.IsBold ? FontWeights.Bold : FontWeights.Normal;
            rtbContent.FontStyle = _noteData.IsItalic ? FontStyles.Italic : FontStyles.Normal;
            if (!string.IsNullOrEmpty(_noteData.Alignment))
            {
                if (Enum.TryParse<TextAlignment>(_noteData.Alignment, out var a))
                    rtbContent.Document.TextAlignment = a;
            }
        }

        // 保存当前状态
        private void SaveState()
        {
            if (_isInitializing) return;

            _noteData.Left = this.Left;
            _noteData.Top = this.Top;
            _noteData.Width = this.Width;
            _noteData.Height = this.Height;
            _noteData.Content = GetDocumentXaml();
            _noteData.IsTopmost = this.Topmost;
            _noteData.FontSize = rtbContent.FontSize;
            _noteData.IsBold = rtbContent.FontWeight == FontWeights.Bold;
            _noteData.IsItalic = rtbContent.FontStyle == FontStyles.Italic;
            _noteData.Alignment = rtbContent.Document.TextAlignment.ToString();

            ScheduleSave();
        }

        private void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // --- 交互事件 ---

        private void NewNote_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow();
            var pos = GetNextPosition(newWindow.Width, newWindow.Height);
            newWindow.Left = pos.X;
            newWindow.Top = pos.Y;
            newWindow.Show();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized; // 最小化窗口而不是关闭
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            this.Topmost = btnTopmost.IsChecked == true;
            SaveState();
            ShowStatus(this.Topmost ? "已置顶" : "取消置顶");
            AdjustTopmostVisuals();
        }

        private void RtbContent_TextChanged(object sender, TextChangedEventArgs e)
        {
            SaveState();
        }

        // 右键菜单：改颜色
        private void ChangeColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string colorHex)
            {
                MainBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
                _noteData.ColorHex = colorHex;
                SaveState();
                UpdateForegroundForBackground(colorHex);
                UpdatePaperBrush(colorHex);
            }
        }

        // 右键菜单：改透明度
        private void ChangeOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && double.TryParse(item.Tag.ToString(), out double opacity))
            {
                this.Opacity = opacity;
                _noteData.Opacity = opacity;
                SaveState();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            rtbContent.Document.Blocks.Clear();
            rtbContent.Document.Blocks.Add(new Paragraph());
        }

        // 删除便签 (永久删除)
        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("确定要删除此便签吗？", "删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                NoteManager.RemoveNote(_noteData);
                _isInitializing = true; // 防止Closing再保存
                this.Close();
            }
        }

        // 快捷键
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);
            // Ctrl + N 新建
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N)
                NewNote_Click(this, new RoutedEventArgs());
            // Ctrl + D 删除
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.D)
                DeleteNote_Click(this, new RoutedEventArgs());
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.T)
            {
                btnTopmost.IsChecked = !(btnTopmost.IsChecked == true);
                PinButton_Click(this, new RoutedEventArgs());
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W)
            {
                CloseButton_Click(this, new RoutedEventArgs());
            }
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.E)
            {
                Export_Click(this, new RoutedEventArgs());
            }
            if (e.Key == Key.Escape)
                Keyboard.ClearFocus();
        }

        private void SnapToEdges()
        {
            if (_isSnapping) return;
            _isSnapping = true;
            var work = SystemParameters.WorkArea;
            double t = 10;
            double nl = this.Left;
            double nt = this.Top;
            if (Math.Abs(this.Left - work.Left) <= t) nl = work.Left;
            if (Math.Abs(this.Top - work.Top) <= t) nt = work.Top;
            if (Math.Abs((work.Right - (this.Left + this.Width))) <= t) nl = work.Right - this.Width;
            if (Math.Abs((work.Bottom - (this.Top + this.Height))) <= t) nt = work.Bottom - this.Height;
            this.Left = nl;
            this.Top = nt;
            _isSnapping = false;
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var range = new TextRange(rtbContent.Selection.IsEmpty ? rtbContent.Document.ContentStart : rtbContent.Selection.Start,
                rtbContent.Selection.IsEmpty ? rtbContent.Document.ContentEnd : rtbContent.Selection.End);
            System.Windows.Clipboard.SetText(range.Text ?? string.Empty);
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            var text = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
            var pos = rtbContent.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            pos.InsertTextInRun(text);
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            rtbContent.SelectAll();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var path = Path.Combine(dir, ($"StickyNote_{_noteData.Id}.txt"));
                var range2 = new TextRange(rtbContent.Document.ContentStart, rtbContent.Document.ContentEnd);
                File.WriteAllText(path, range2.Text ?? string.Empty);
                System.Windows.MessageBox.Show($"已导出到: {path}");
            }
            catch { }
        }

        private void InsertSeparator()
        {
            var sep = "\n————————————\n";
            var p = rtbContent.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            p.InsertTextInRun(sep);
            SaveState();
        }

        private void ShowStatus(string text)
        {
            StatusText.Text = text;
            StatusText.Opacity = 1;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ToggleLock_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                rtbContent.IsReadOnly = item.IsChecked;
            }
        }

        private void ToggleStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool strike = v == null || !v.Any(d => d.Location == TextDecorationLocation.Strikethrough);
            var col = new TextDecorationCollection();
            if (strike) foreach (var d in TextDecorations.Strikethrough) col.Add(d);
            if (v != null && v.Any(d => d.Location == TextDecorationLocation.Underline)) foreach (var d in TextDecorations.Underline) col.Add(d);
            ApplyToSelectionOrLine(Inline.TextDecorationsProperty, col.Count > 0 ? col : null);
            SaveState();
        }

        private void FontInc_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            double cur = v is double d ? d : rtbContent.FontSize;
            ApplyToSelectionOrLine(TextElement.FontSizeProperty, Math.Min(72, cur + 1));
            SaveState();
        }
        private void FontDec_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            double cur = v is double d ? d : rtbContent.FontSize;
            ApplyToSelectionOrLine(TextElement.FontSizeProperty, Math.Max(10, cur - 1));
            SaveState();
        }
        private void ToggleBold_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(TextElement.FontWeightProperty);
            var toBold = !(v is FontWeight fw && fw == FontWeights.Bold);
            ApplyToSelectionOrLine(TextElement.FontWeightProperty, toBold ? FontWeights.Bold : FontWeights.Normal);
            SaveState();
        }
        private void ToggleItalic_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(TextElement.FontStyleProperty);
            var toItalic = !(v is System.Windows.FontStyle fs && fs == System.Windows.FontStyles.Italic);
            ApplyToSelectionOrLine(TextElement.FontStyleProperty, toItalic ? System.Windows.FontStyles.Italic : System.Windows.FontStyles.Normal);
            SaveState();
        }
        private void ToggleUnderline_Click(object sender, RoutedEventArgs e)
        {
            var v = rtbContent.Selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool underline = v == null || !v.Any(d => d.Location == TextDecorationLocation.Underline);
            var col = new TextDecorationCollection();
            if (underline) foreach (var d in TextDecorations.Underline) col.Add(d);
            if (v != null && v.Any(d => d.Location == TextDecorationLocation.Strikethrough)) foreach (var d in TextDecorations.Strikethrough) col.Add(d);
            ApplyToSelectionOrLine(Inline.TextDecorationsProperty, col.Count > 0 ? col : null);
            SaveState();
        }
        private void AlignLeft_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSelectionOrLine(Paragraph.TextAlignmentProperty, TextAlignment.Left);
            SaveState();
        }
        private void AlignCenter_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSelectionOrLine(Paragraph.TextAlignmentProperty, TextAlignment.Center);
            SaveState();
        }
        private void AlignRight_Click(object sender, RoutedEventArgs e)
        {
            ApplyToSelectionOrLine(Paragraph.TextAlignmentProperty, TextAlignment.Right);
            SaveState();
        }
        private void PaletteButton_Click(object sender, RoutedEventArgs e)
        {
            var cm = new ContextMenu();
            void add(string header, string hex)
            {
                var mi = new MenuItem { Header = header, Tag = hex };
                mi.Click += ChangeColor_Click;
                cm.Items.Add(mi);
            }
            add("经典牛皮纸", "#E3C887");
            add("樱花粉", "#FFCDD2");
            add("天空蓝", "#B3E5FC");
            add("护眼绿", "#DCEDC8");
            add("极简白", "#FFF9C4");
            cm.Items.Add(new Separator());
            add("深灰", "#424242");
            add("木炭黑", "#263238");
            add("橄榄绿", "#3D5B3D");
            cm.PlacementTarget = PaletteBtn;
            cm.IsOpen = true;
        }


        private void UpdateLineSystem() { }

        private void ApplyToSelectionOrLine(DependencyProperty prop, object value)
        {
            TextRange range;
            if (!rtbContent.Selection.IsEmpty)
            {
                range = new TextRange(rtbContent.Selection.Start, rtbContent.Selection.End);
            }
            else
            {
                var p = rtbContent.CaretPosition.Paragraph;
                if (p == null)
                {
                    p = new Paragraph();
                    rtbContent.Document.Blocks.Add(p);
                }
                range = new TextRange(p.ContentStart, p.ContentEnd);
            }
            range.ApplyPropertyValue(prop, value);
        }

        private void UpdatePaperBrush(string colorHex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                var line = System.Windows.Media.Color.FromRgb((byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8));
                var dg = new DrawingGroup();
                var rect = new GeometryDrawing(new SolidColorBrush(c), null, new RectangleGeometry(new Rect(0, 0, 100, 30)));
                dg.Children.Add(rect);
                var pen = new System.Windows.Media.Pen(new SolidColorBrush(line), 1);
                var lineGeo = new GeometryDrawing(null, pen, new LineGeometry(new System.Windows.Point(0, 29), new System.Windows.Point(100, 29)));
                dg.Children.Add(lineGeo);
                var brush = new DrawingBrush(dg)
                {
                    Viewport = new Rect(0, 0, 1, 30),
                    ViewportUnits = BrushMappingMode.Absolute,
                    TileMode = TileMode.Tile
                };
                LinesGrid.Background = brush;
            }
            catch { }
        }

        private void UpdateShadowForDpi()
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            double scale = dpi.DpiScaleX;
            if (MainBorder.Effect is System.Windows.Media.Effects.DropShadowEffect eff)
            {
                eff.BlurRadius = 10 * scale;
                eff.ShadowDepth = 3 * scale;
                eff.Opacity = this.Topmost ? 0.2 : 0.3;
            }
        }

        private System.Windows.Point GetNextPosition(double w, double h)
        {
            var work = SystemParameters.WorkArea;
            int cols = Math.Max(1, (int)(work.Width / (w + 40)));
            int count = System.Windows.Application.Current.Windows.OfType<MainWindow>().Count();
            int r = count / cols;
            int c = count % cols;
            double left = work.Left + c * (w + 40);
            double top = work.Top + r * (h + 40);
            if (left + w > work.Right) left = work.Right - w;
            if (top + h > work.Bottom) top = work.Bottom - h;
            return new System.Windows.Point(left, top);
        }

        private void EnsureOnScreen(double w, double h)
        {
            var work = SystemParameters.WorkArea;
            bool off = (this.Left + w < work.Left + 20) || (this.Left > work.Right - 20) || (this.Top + h < work.Top + 20) || (this.Top > work.Bottom - 20);
            if (off)
            {
                this.Left = work.Left + Math.Max(0, (work.Width - w) / 2);
                this.Top = work.Top + Math.Max(0, (work.Height - h) / 2);
            }
            if (this.Left < work.Left) this.Left = work.Left;
            if (this.Top < work.Top) this.Top = work.Top;
            if (this.Left + w > work.Right) this.Left = work.Right - w;
            if (this.Top + h > work.Bottom) this.Top = work.Bottom - h;
        }

        private void AdjustTopmostVisuals()
        {
            if (MainBorder.Effect is System.Windows.Media.Effects.DropShadowEffect eff)
            {
                eff.Opacity = this.Topmost ? 0.2 : 0.3;
            }
        }

        private void UpdateForegroundForBackground(string colorHex)
        {
            try
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                rtbContent.Foreground = lum < 128 ? new SolidColorBrush(Colors.White) : new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E2723"));
            }
            catch { }
        }

        private void SetDocumentFromContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                rtbContent.Document = new FlowDocument(new Paragraph(new Run("")));
                return;
            }
            if (content.TrimStart().StartsWith("<FlowDocument"))
            {
                try
                {
                    var doc = System.Windows.Markup.XamlReader.Parse(content) as FlowDocument;
                    rtbContent.Document = doc ?? new FlowDocument(new Paragraph(new Run("")));
                }
                catch
                {
                    rtbContent.Document = new FlowDocument(new Paragraph(new Run(content)));
                }
            }
            else
            {
                rtbContent.Document = new FlowDocument(new Paragraph(new Run(content)));
            }
        }
        private string GetDocumentXaml()
        {
            try
            {
                return System.Windows.Markup.XamlWriter.Save(rtbContent.Document);
            }
            catch { return new TextRange(rtbContent.Document.ContentStart, rtbContent.Document.ContentEnd).Text; }
        }

        private void Search_Click(object sender, RoutedEventArgs e)
        {
            NoteSearch.Show();
        }

    }

    // --- 数据管理类 ---

    public class NoteData
    {
        public required string Id { get; set; }
        public required string Content { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsTopmost { get; set; }
        public required string ColorHex { get; set; }
        public double Opacity { get; set; }
        public double FontSize { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikethrough { get; set; }
        public required string Alignment { get; set; }
    }

    public static class NoteManager
    {
        private static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyNotes_Data.json");
        private static List<NoteData> _notes = new List<NoteData>();
        private static NoteData? _lastDeleted;

        public static void AddNote(NoteData note)
        {
            _notes.Add(note);
            SaveNotes();
        }

        public static void RemoveNote(NoteData note)
        {
            var target = _notes.FirstOrDefault(n => n.Id == note.Id);
            if (target != null)
            {
                _lastDeleted = target;
                _notes.Remove(target);
            }
            SaveNotes();
        }

        public static void SaveNotes()
        {
            try
            {
                string json = JsonSerializer.Serialize(_notes);
                var temp = FilePath + ".tmp";
                var bak = FilePath + ".bak";
                File.WriteAllText(temp, json);
                if (File.Exists(FilePath))
                    File.Replace(temp, FilePath, bak);
                else
                    File.Move(temp, FilePath);
            }
            catch { }
        }

        public static List<NoteData> LoadNotes()
        {
            if (!File.Exists(FilePath)) return new List<NoteData>();
            try
            {
                string json = File.ReadAllText(FilePath);
                _notes = JsonSerializer.Deserialize<List<NoteData>>(json) ?? new List<NoteData>();
                _notes = _notes
                    .GroupBy(n => n.Id)
                    .Select(g => g.Last())
                    .ToList();
                return _notes;
            }
            catch { return new List<NoteData>(); }
        }

        public static IReadOnlyList<NoteData> GetAll()
        {
            return _notes;
        }

        public static bool RestoreLastDeleted()
        {
            if (_lastDeleted == null) return false;
            _notes.Add(_lastDeleted);
            SaveNotes();
            return true;
        }
    }

        public static class NoteSearch
        {
            public static void Show()
            {
            var win = new Window
            {
                Title = "搜索便签",
                Width = 420,
                Height = 520,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var tb = new System.Windows.Controls.TextBox { Margin = new Thickness(10), FontSize = 14 };
            var lb = new System.Windows.Controls.ListBox { Margin = new Thickness(10) };
            grid.Children.Add(tb);
            Grid.SetRow(lb, 1);
            grid.Children.Add(lb);
            win.Content = grid;
            void Refresh()
            {
                var q = tb.Text?.Trim() ?? string.Empty;
                var items = NoteManager.GetAll()
                    .Select(n => new { Data = n, Preview = BuildPreview(n.Content) })
                    .Where(x => string.IsNullOrEmpty(q) || (x.Preview ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                lb.ItemsSource = items;
                lb.DisplayMemberPath = "Preview";
            }
            tb.TextChanged += (s, e) => Refresh();
            lb.MouseDoubleClick += (s, e) => ActivateSelected();
            win.KeyDown += (s, e) => { if (e.Key == Key.Enter) ActivateSelected(); };
            void ActivateSelected()
            {
                if (lb.SelectedItem == null) return;
                dynamic obj = lb.SelectedItem;
                NoteData data = obj.Data;
                var target = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.NoteId == data.Id);
                if (target == null)
                {
                    target = new MainWindow(data);
                    target.Show();
                }
                else
                {
                    if (target.Visibility != Visibility.Visible) target.Show();
                }
                target.Topmost = true;
                target.Activate();
                win.Close();
            }
            string BuildPreview(string text)
            {
                text = text ?? string.Empty;
                if (text.TrimStart().StartsWith("<FlowDocument"))
                {
                    try
                    {
                        var doc = System.Windows.Markup.XamlReader.Parse(text) as FlowDocument;
                        if (doc != null)
                        {
                            var rng = new TextRange(doc.ContentStart, doc.ContentEnd);
                            text = rng.Text;
                        }
                    }
                    catch { }
                }
                var firstLine = text.Split(new[] { '\r', '\n' }).FirstOrDefault() ?? string.Empty;
                if (firstLine.Length > 40) firstLine = firstLine.Substring(0, 40) + "...";
                return firstLine;
            }
            Refresh();
            win.ShowDialog();
        }
    }
}
