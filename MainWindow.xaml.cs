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
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace StickyNote
{
    // 标签项数据模型
    public class TabItem : INotifyPropertyChanged
    {
        private string _title = "便签";
        private string _preview = string.Empty;

        public string Id { get; set; } = string.Empty;
        
        public string Title 
        { 
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }
        
        public string Preview 
        { 
            get => _preview;
            set
            {
                if (_preview != value)
                {
                    _preview = value;
                    OnPropertyChanged(nameof(Preview));
                }
            }
        }
        
        public NoteData NoteData { get; set; } = null!;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class MainWindow : Window
    {
        // 标签管理
        private ObservableCollection<TabItem> _tabs = new ObservableCollection<TabItem>();
        private TabItem? _currentTab;
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
        public string NoteId => _currentTab?.NoteData?.Id ?? string.Empty;

        // 构造函数：支持传入已有的数据
        public MainWindow(NoteData? data = null)
        {
            try
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

                // 初始化标签列表
                TabList.ItemsSource = _tabs;

                // 加载所有便签作为标签
                LoadAllNotesAsTabs();

                // 如果没有标签，显示空状态
                if (_tabs.Count == 0)
                {
                    _currentTab = null;
                    UpdateEmptyState();
                }
                else
                {
                    // 选择第一个标签
                    TabList.SelectedIndex = 0;
                }

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
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"初始化错误: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // 加载所有便签作为标签
        private void LoadAllNotesAsTabs()
        {
            var notes = NoteManager.LoadNotes();
            foreach (var note in notes)
            {
                if (string.IsNullOrEmpty(note.Title))
                {
                    note.Title = GetNoteTitle(note.Content);
                }

                var tab = new TabItem
                {
                    Id = note.Id,
                    Title = note.Title,
                    Preview = GetNotePreview(note.Content),
                    NoteData = note
                };
                _tabs.Add(tab);
            }
        }

        // 创建新标签
        public void CreateNewTab()
        {
            var noteData = new NoteData
            {
                Id = Guid.NewGuid().ToString(),
                Title = "便签",
                Content = "",
                Width = this.Width,
                Height = this.Height,
                Left = this.Left,
                Top = this.Top,
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

            var tab = new TabItem
            {
                Id = noteData.Id,
                Title = "便签",
                Preview = "",
                NoteData = noteData
            };

            _tabs.Add(tab);
            NoteManager.AddNote(noteData);
            TabList.SelectedItem = tab;
        }

        // 刷新标签列表
        public void RefreshTabs()
        {
            _tabs.Clear();
            LoadAllNotesAsTabs();
            if (_tabs.Count > 0)
            {
                TabList.SelectedIndex = _tabs.Count - 1; // 选择最后一个（刚恢复的）
            }
            else
            {
                _currentTab = null;
                UpdateEmptyState();
            }
        }

        // 获取便签标题
        private string GetNoteTitle(string content)
        {
            if (string.IsNullOrEmpty(content)) return "便签";
            
            string text = content;
            if (content.TrimStart().StartsWith("<FlowDocument"))
            {
                try
                {
                    var doc = System.Windows.Markup.XamlReader.Parse(content) as FlowDocument;
                    if (doc != null)
                    {
                        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                        text = range.Text;
                    }
                }
                catch { }
            }

            var firstLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "便签";
            if (firstLine.Length > 15) firstLine = firstLine.Substring(0, 15) + "...";
            return firstLine;
        }

        // 获取便签预览
        private string GetNotePreview(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            
            string text = content;
            if (content.TrimStart().StartsWith("<FlowDocument"))
            {
                try
                {
                    var doc = System.Windows.Markup.XamlReader.Parse(content) as FlowDocument;
                    if (doc != null)
                    {
                        var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                        text = range.Text;
                    }
                }
                catch { }
            }

            // 移除第一行作为标题，剩下的作为预览
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1) return "";
            
            var preview = string.Join(" ", lines.Skip(1));
            if (preview.Length > 50) preview = preview.Substring(0, 50) + "...";
            return preview;
        }

        private bool _isLoading = false;

        private void UpdateEmptyState()
        {
            bool isEmpty = _tabs.Count == 0;
            if (EmptyStateText != null)
            {
                EmptyStateText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
            }
            if (rtbContent != null)
            {
                rtbContent.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
                rtbContent.IsEnabled = !isEmpty;
            }
            
            // 禁用/启用一些按钮
            if (PaletteBtn != null) PaletteBtn.IsEnabled = !isEmpty;
            if (btnTopmost != null) btnTopmost.IsEnabled = !isEmpty;
        }

        // 将数据渲染到 UI
        private void ApplyDataToUI()
        {
            UpdateEmptyState();
            if (_currentTab?.NoteData == null) return;

            _isLoading = true;
            try
            {
                var noteData = _currentTab.NoteData;
                this.Topmost = noteData.IsTopmost;
                this.Opacity = noteData.Opacity;
                SetDocumentFromContent(noteData.Content);
                btnTopmost.IsChecked = noteData.IsTopmost;

                try
                {
                    MainBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(noteData.ColorHex));
                }
                catch { }

                UpdateShadowForDpi();
                UpdateForegroundForBackground(noteData.ColorHex);
                UpdatePaperBrush(noteData.ColorHex);
                if (noteData.FontSize > 0) rtbContent.FontSize = noteData.FontSize;
                rtbContent.FontWeight = noteData.IsBold ? FontWeights.Bold : FontWeights.Normal;
                rtbContent.FontStyle = noteData.IsItalic ? FontStyles.Italic : FontStyles.Normal;
                if (!string.IsNullOrEmpty(noteData.Alignment))
                {
                    if (Enum.TryParse<TextAlignment>(noteData.Alignment, out var a))
                        rtbContent.Document.TextAlignment = a;
                }
            }
            finally
            {
                _isLoading = false;
            }
        }

        // 保存当前状态
        private void SaveState()
        {
            if (_isInitializing || _isLoading || _currentTab?.NoteData == null) 
            {
                return;
            }

            var noteData = _currentTab.NoteData;
            noteData.Left = this.Left;
            noteData.Top = this.Top;
            noteData.Width = this.Width;
            noteData.Height = this.Height;
            noteData.Content = GetDocumentXaml();
            noteData.IsTopmost = this.Topmost;
            noteData.FontSize = rtbContent.FontSize;
            noteData.IsBold = rtbContent.FontWeight == FontWeights.Bold;
            noteData.IsItalic = rtbContent.FontStyle == FontStyles.Italic;
            noteData.Alignment = rtbContent.Document.TextAlignment.ToString();

            // 更新标签的标题和预览
            noteData.Title = _currentTab.Title;
            _currentTab.Preview = GetNotePreview(noteData.Content);

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
            CreateNewTab();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
            }
            else
            {
                this.WindowState = WindowState.Maximized;
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (pathMaximize != null)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    // Restore icon (Two overlapping squares)
                    pathMaximize.Data = Geometry.Parse("M4,4 L4,12 L12,12 L12,4 Z M6,4 L6,2 L14,2 L14,10 L12,10");
                }
                else
                {
                    // Maximize icon (Single square)
                    pathMaximize.Data = Geometry.Parse("M2,2 L12,2 L12,12 L2,12 Z");
                }
            }
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
            UpdateCurrentTabPreview();
        }

        // 更新当前标签的预览
        private void UpdateCurrentTabPreview()
        {
            if (_currentTab != null && !_isInitializing)
            {
                var content = GetDocumentXaml();
                _currentTab.Preview = GetNotePreview(content);
            }
        }

        private void AddTodo_Click(object sender, RoutedEventArgs e)
        {
            // 在当前光标位置插入未完成的代办事项符号
            InsertTodoAtCurrentPosition(false);
        }

        private void InsertTodoAtCurrentPosition(bool isCompleted)
        {
            // 获取当前文档
            var doc = rtbContent.Document;
            if (doc == null) return;

            // 获取当前光标位置
            TextPointer caretPos = rtbContent.CaretPosition;
            if (caretPos == null) return;

            // 创建一个新的Run，包含代办事项符号
            string todoSymbol = isCompleted ? "☑ " : "☐ ";
            Run todoRun = new Run(todoSymbol);

            // 获取当前段落
            Paragraph currentParagraph = caretPos.Paragraph;
            if (currentParagraph == null)
            {
                // 如果没有段落，创建一个新段落
                currentParagraph = new Paragraph(todoRun) { Margin = new Thickness(0) };
                doc.Blocks.Add(currentParagraph);
                // 将光标移动到代办事项符号后面
                rtbContent.CaretPosition = todoRun.ContentEnd;
                return;
            }

            // 检查当前段落是否已经有代办事项符号
            bool hasTodo = false;
            TextPointer start = currentParagraph.ContentStart;
            while (start != null && start.CompareTo(currentParagraph.ContentEnd) < 0)
            {
                if (start.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string text = start.GetTextInRun(LogicalDirection.Forward);
                    if (text.Contains("☐") || text.Contains("☑"))
                    {
                        hasTodo = true;
                        break;
                    }
                    start = start.GetPositionAtOffset(text.Length, LogicalDirection.Forward);
                }
                else
                {
                    start = start.GetNextContextPosition(LogicalDirection.Forward);
                }
            }

            if (!hasTodo)
            {
                // 如果当前段落没有代办事项符号，在段落开头插入
                currentParagraph.Inlines.InsertBefore(currentParagraph.Inlines.FirstInline, todoRun);
            }
            else
            {
                // 如果当前段落已有代办事项符号，在当前光标位置创建新段落并插入
                Paragraph newParagraph = new Paragraph(todoRun) { Margin = new Thickness(0) };
                doc.Blocks.InsertAfter(currentParagraph, newParagraph);
            }

            // 将光标移动到代办事项符号后面
            rtbContent.CaretPosition = todoRun.ContentEnd;
        }

        // 处理RichTextBox的点击事件，用于切换代办事项状态
        private void RtbContent_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // 获取点击位置
            System.Windows.Point clickPoint = e.GetPosition(rtbContent);

            // 将点击位置转换为TextPointer
            TextPointer textPosition = rtbContent.GetPositionFromPoint(clickPoint, true);
            if (textPosition == null) return;

            // 检查点击位置是否在代办事项符号上
            TextPointer start = textPosition.GetInsertionPosition(LogicalDirection.Backward);
            TextPointer end = textPosition.GetInsertionPosition(LogicalDirection.Forward);

            // 扩大搜索范围，确保能找到代办事项符号
            start = start.GetNextInsertionPosition(LogicalDirection.Backward) ?? start;
            end = end.GetNextInsertionPosition(LogicalDirection.Forward) ?? end;

            // 获取点击位置周围的文本
            TextRange range = new TextRange(start, end);
            string text = range.Text;

            // 检查是否包含代办事项符号
            int uncheckedIndex = text.IndexOf("☐");
            int checkedIndex = text.IndexOf("☑");

            if (uncheckedIndex >= 0)
            {
                // 将未完成的代办事项切换为已完成
                ReplaceTodoSymbol(start, "☐", "☑");
            }
            else if (checkedIndex >= 0)
            {
                // 将已完成的代办事项切换为未完成
                ReplaceTodoSymbol(start, "☑", "☐");
            }
        }

        private void ReplaceTodoSymbol(TextPointer start, string oldSymbol, string newSymbol)
        {
            // 在文档中查找并替换代办事项符号
            TextPointer current = start;
            while (current != null && current.CompareTo(rtbContent.Document.ContentEnd) < 0)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = current.GetTextInRun(LogicalDirection.Forward);
                    int index = textRun.IndexOf(oldSymbol);
                    if (index >= 0)
                    {
                        // 找到符号，替换它
                        TextPointer symbolStart = current.GetPositionAtOffset(index);
                        TextPointer symbolEnd = symbolStart.GetPositionAtOffset(oldSymbol.Length);
                        
                        rtbContent.BeginChange();
                        try
                        {
                            TextRange symbolRange = new TextRange(symbolStart, symbolEnd);
                            symbolRange.Text = newSymbol;

                            // 如果是标记为完成（☑），则将该行移动到末尾
                            if (newSymbol == "☑")
                            {
                                Paragraph para = symbolStart.Paragraph;
                                if (para != null && para.Parent is FlowDocument doc && doc == rtbContent.Document)
                                {
                                    // 检查是否已经是最后一段
                                    if (doc.Blocks.LastBlock != para)
                                    {
                                        // 记录原来位置的下一个段落，用于保持视觉位置
                                        Block nextBlock = para.NextBlock;

                                        doc.Blocks.Remove(para);
                                        doc.Blocks.Add(para);
                                        
                                        // 如果有下一个段落，将光标移过去，防止视角跳到文档末尾
                                        if (nextBlock != null)
                                        {
                                            rtbContent.CaretPosition = nextBlock.ContentStart;
                                            nextBlock.BringIntoView();
                                        }
                                        else
                                        {
                                            rtbContent.ScrollToEnd();
                                        }
                                    }
                                }
                            }
                        }
                        finally
                        {
                            rtbContent.EndChange();
                        }
                        break;
                    }
                }
                current = current.GetNextContextPosition(LogicalDirection.Forward);
            }
        }

        // --- 批量操作与编号 ---

        private List<Paragraph> GetSelectedParagraphs()
        {
            var list = new List<Paragraph>();
            var start = rtbContent.Selection.Start;
            var end = rtbContent.Selection.End;

            // 确保 start < end
            if (start.CompareTo(end) > 0) (start, end) = (end, start);

            Block current = rtbContent.Document.Blocks.FirstBlock;
            while (current != null)
            {
                // 检查交叉：Block Start <= Selection End AND Block End >= Selection Start
                if (current.ContentStart.CompareTo(end) <= 0 && current.ContentEnd.CompareTo(start) >= 0)
                {
                    if (current is Paragraph p) list.Add(p);
                }

                if (current.ContentStart.CompareTo(end) > 0) break;

                current = current.NextBlock;
            }
            return list;
        }

        private void BatchSetTodo_Click(object sender, RoutedEventArgs e)
        {
            var paras = GetSelectedParagraphs();
            if (paras.Count == 0) return;

            rtbContent.BeginChange();
            try
            {
                foreach (var p in paras)
                {
                    string text = new TextRange(p.ContentStart, p.ContentEnd).Text;
                    if (!text.StartsWith("☐ ") && !text.StartsWith("☑ "))
                    {
                        // 确保 Paragraph 有 Inline，如果没有（空行），添加一个 Run
                        if (p.Inlines.Count == 0)
                        {
                            p.Inlines.Add(new Run("☐ "));
                        }
                        else
                        {
                            p.Inlines.InsertBefore(p.Inlines.FirstInline, new Run("☐ "));
                        }
                    }
                }
            }
            finally
            {
                rtbContent.EndChange();
                SaveState();
            }
        }

        private void BatchUnsetTodo_Click(object sender, RoutedEventArgs e)
        {
            var paras = GetSelectedParagraphs();
            if (paras.Count == 0) return;

            rtbContent.BeginChange();
            try
            {
                foreach (var p in paras)
                {
                    TextPointer start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                    string text = start.GetTextInRun(LogicalDirection.Forward);

                    if (text.StartsWith("☐ ") || text.StartsWith("☑ "))
                    {
                        TextPointer delEnd = start.GetPositionAtOffset(2);
                        new TextRange(start, delEnd).Text = "";
                    }
                    else if (text.StartsWith("☐") || text.StartsWith("☑"))
                    {
                         TextPointer delEnd = start.GetPositionAtOffset(1);
                         new TextRange(start, delEnd).Text = "";
                    }
                }
            }
            finally
            {
                rtbContent.EndChange();
                SaveState();
            }
        }

        private void BatchCheckTodo_Click(object sender, RoutedEventArgs e)
        {
            var paras = GetSelectedParagraphs();
            if (paras.Count == 0) return;

            rtbContent.BeginChange();
            try
            {
                foreach (var p in paras)
                {
                    TextPointer start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                    string text = start.GetTextInRun(LogicalDirection.Forward);

                    if (text.StartsWith("☐"))
                    {
                        TextPointer symbolEnd = start.GetPositionAtOffset(1);
                        new TextRange(start, symbolEnd).Text = "☑";
                    }
                }
            }
            finally
            {
                rtbContent.EndChange();
                SaveState();
            }
        }

        private void BatchUncheckTodo_Click(object sender, RoutedEventArgs e)
        {
            var paras = GetSelectedParagraphs();
            if (paras.Count == 0) return;

            rtbContent.BeginChange();
            try
            {
                foreach (var p in paras)
                {
                    TextPointer start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                    string text = start.GetTextInRun(LogicalDirection.Forward);

                    if (text.StartsWith("☑"))
                    {
                        TextPointer symbolEnd = start.GetPositionAtOffset(1);
                        new TextRange(start, symbolEnd).Text = "☐";
                    }
                }
            }
            finally
            {
                rtbContent.EndChange();
                SaveState();
            }
        }

        private void BatchNumbering_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string style)
            {
                var paras = GetSelectedParagraphs();
                if (paras.Count == 0) return;

                rtbContent.BeginChange();
                try
                {
                    // 先尝试移除现有的编号，避免重复添加
                    RemoveNumbering(paras);

                    int index = 1;
                    foreach (var p in paras)
                    {
                        string text = new TextRange(p.ContentStart, p.ContentEnd).Text;
                        if (string.IsNullOrWhiteSpace(text)) continue;

                        string prefix = "";
                        switch (style)
                        {
                            case "1.": prefix = $"{index}. "; break;
                            case "1)": prefix = $"{index}) "; break;
                            case "①": prefix = $"{GetCircleNumber(index)} "; break;
                            case "A.": prefix = $"{GetAlphaNumber(index)}. "; break;
                        }

                        if (p.Inlines.Count == 0)
                            p.Inlines.Add(new Run(prefix));
                        else
                            p.Inlines.InsertBefore(p.Inlines.FirstInline, new Run(prefix));
                        
                        index++;
                    }
                }
                finally
                {
                    rtbContent.EndChange();
                    SaveState();
                }
            }
        }

        private void BatchRemoveNumbering_Click(object sender, RoutedEventArgs e)
        {
            var paras = GetSelectedParagraphs();
            if (paras.Count == 0) return;

            rtbContent.BeginChange();
            try
            {
                RemoveNumbering(paras);
            }
            finally
            {
                rtbContent.EndChange();
                SaveState();
            }
        }

        private void RemoveNumbering(List<Paragraph> paras)
        {
            foreach (var p in paras)
            {
                TextPointer start = p.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                string text = start.GetTextInRun(LogicalDirection.Forward);
                
                // 匹配常见的编号格式
                // 1. 1) ① A. (1) 等
                // 这里使用简单的逻辑判断，移除开头的特定模式
                
                int removeLen = 0;
                
                // 检查 A. B. ...
                if (text.Length >= 3 && char.IsLetter(text[0]) && text[1] == '.' && text[2] == ' ')
                    removeLen = 3;
                // 检查数字开头的 1. 10. 1) 10) ...
                else if (char.IsDigit(text[0]))
                {
                    int i = 0;
                    while (i < text.Length && char.IsDigit(text[i])) i++;
                    if (i < text.Length && (text[i] == '.' || text[i] == ')') && i + 1 < text.Length && text[i+1] == ' ')
                    {
                        removeLen = i + 2;
                    }
                }
                // 检查 ① ... ⑳
                else if (text.Length >= 2 && text[0] >= '①' && text[0] <= '⑳' && text[1] == ' ')
                {
                    removeLen = 2;
                }
                // 检查 (1) ...
                else if (text.StartsWith("(") && text.Contains(")") && text.IndexOf(")") < 6)
                {
                    int idx = text.IndexOf(")");
                    if (idx + 1 < text.Length && text[idx+1] == ' ')
                    {
                        // 确保括号里是数字
                        bool isNum = true;
                        for(int k=1; k<idx; k++) if(!char.IsDigit(text[k])) { isNum = false; break; }
                        if (isNum) removeLen = idx + 2;
                    }
                }

                if (removeLen > 0)
                {
                    TextPointer delEnd = start.GetPositionAtOffset(removeLen);
                    new TextRange(start, delEnd).Text = "";
                }
            }
        }

        private string GetCircleNumber(int i)
        {
            if (i >= 1 && i <= 20) return ((char)('①' + i - 1)).ToString();
            return $"({i})";
        }

        private string GetAlphaNumber(int i)
        {
            string res = "";
            while (i > 0)
            {
                i--;
                res = (char)('A' + (i % 26)) + res;
                i /= 26;
            }
            return res;
        }

        // 右键菜单：改颜色
        // 标签选择变化事件
        private void TabList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                // 切换前保存当前便签的内容
                // 使用 _currentTab 来保存离开的那个便签的状态
                if (_currentTab != null && _currentTab.NoteData != null)
                {
                    // 只有当 _currentTab 确实是之前选中的那个便签时才保存
                    // 防止事件多次触发导致的混乱
                    try
                    {
                        SaveState();
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"SaveState failed during switch: {ex.Message}");
                    }
                }

                // 获取新选中的标签
                StickyNote.TabItem? selectedTab = null;
                if (e.AddedItems.Count > 0)
                {
                    selectedTab = e.AddedItems[0] as StickyNote.TabItem;
                }
                
                if (selectedTab == null)
                {
                    selectedTab = TabList.SelectedItem as StickyNote.TabItem;
                }

                if (selectedTab != null)
                {
                    _currentTab = selectedTab;
                    ApplyDataToUI();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in TabList_SelectionChanged: {ex.Message}");
            }
        }

        private void TabList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // 获取点击的 ListBoxItem
                var item = ItemsControl.ContainerFromElement(TabList, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item != null && item.Content is StickyNote.TabItem clickedTab)
                {
                    // 如果点击的是当前已经选中的标签，强制刷新 UI
                    // 这解决了 ListBox 选中状态与实际内容不一致的问题
                    if (clickedTab == _currentTab)
                    {
                        Logger.Log($"Forcing reload for clicked tab: {clickedTab.Title}");
                        ApplyDataToUI();
                    }
                    else if (TabList.SelectedItem == clickedTab && _currentTab != clickedTab)
                    {
                        // 如果 ListBox 认为它被选中了，但 _currentTab 不是它，说明状态不同步
                        // 手动触发切换逻辑
                        Logger.Log($"Fixing sync issue for tab: {clickedTab.Title}");
                        _currentTab = clickedTab;
                        ApplyDataToUI();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error in TabList_PreviewMouseLeftButtonUp: {ex.Message}");
            }
        }

        // 双击标签重命名
        private void TabList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TabList.SelectedItem is TabItem tab)
            {
                RenameTabDialog(tab);
            }
        }

        // 添加标签按钮点击事件
        private void AddTab_Click(object sender, RoutedEventArgs e)
        {
            CreateNewTab();
        }

        // 重命名标签
        private void RenameTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is TabItem tab)
            {
                RenameTabDialog(tab);
            }
        }

        // 重命名标签对话框
        private void RenameTabDialog(TabItem tab)
        {
            var dialog = new Window
            {
                Title = "重命名标签",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.Label { Content = "标签名称:", Margin = new Thickness(10) };
            var textBox = new System.Windows.Controls.TextBox { Text = tab.Title, Margin = new Thickness(10), FontSize = 14 };
            var buttonPanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(10) };
            
            var okButton = new System.Windows.Controls.Button { Content = "确定", Width = 60, Height = 25, Margin = new Thickness(5, 0, 0, 0) };
            var cancelButton = new System.Windows.Controls.Button { Content = "取消", Width = 60, Height = 25, Margin = new Thickness(5, 0, 0, 0) };

            okButton.Click += (s, args) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    tab.Title = textBox.Text.Trim();
                    dialog.DialogResult = true;
                }
            };

            cancelButton.Click += (s, args) => dialog.DialogResult = false;
            textBox.KeyDown += (s, args) =>
            {
                if (args.Key == Key.Enter) okButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (args.Key == Key.Escape) cancelButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(label, 0);
            Grid.SetRow(textBox, 1);
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            dialog.Content = grid;
            textBox.Focus();
            textBox.SelectAll();

            if (dialog.ShowDialog() == true)
            {
                if (tab.NoteData != null)
                {
                    tab.NoteData.Title = tab.Title;
                    NoteManager.SaveNotes();
                }
            }
        }

        // 删除标签
        private void DeleteTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is TabItem tab)
            {
                DeleteTabWithConfirmation(tab);
            }
        }

        // 关闭标签按钮
        private void CloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.DataContext is TabItem tab)
            {
                DeleteTabWithConfirmation(tab);
            }
        }

        // 删除标签并确认
        private void DeleteTabWithConfirmation(TabItem tab)
        {
            if (System.Windows.MessageBox.Show($"确定要删除标签 '{tab.Title}' 吗？", "删除标签", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                NoteManager.RemoveNote(tab.NoteData);
                _tabs.Remove(tab);
                
                if (_tabs.Count == 0)
                {
                    _currentTab = null;
                    UpdateEmptyState();
                }
                else if (_currentTab == tab)
                {
                    TabList.SelectedIndex = 0;
                }
            }
        }

        private void ChangeColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string colorHex && _currentTab?.NoteData != null)
            {
                MainBorder.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex));
                _currentTab.NoteData.ColorHex = colorHex;
                SaveState();
                UpdateForegroundForBackground(colorHex);
                UpdatePaperBrush(colorHex);
            }
        }

        // 右键菜单：改透明度
        private void ChangeOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && double.TryParse(item.Tag.ToString(), out double opacity) && _currentTab?.NoteData != null)
            {
                this.Opacity = opacity;
                _currentTab.NoteData.Opacity = opacity;
                SaveState();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            rtbContent.Document.Blocks.Clear();
            rtbContent.Document.Blocks.Add(new Paragraph() { Margin = new Thickness(0) });
        }

        // 删除便签 (永久删除)
        private void DeleteNote_Click(object sender, RoutedEventArgs e)
        {
            if (_currentTab == null) return;

            if (System.Windows.MessageBox.Show("确定要删除此便签吗？", "删除", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                var tabToDelete = _currentTab;
                NoteManager.RemoveNote(tabToDelete.NoteData);
                _tabs.Remove(tabToDelete);
                
                if (_tabs.Count == 0)
                {
                    _currentTab = null;
                    UpdateEmptyState();
                }
                else
                {
                    TabList.SelectedIndex = 0;
                }
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
            if (_currentTab?.NoteData == null) return;

            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fileName = $"StickyNote_{_currentTab.Title}_{_currentTab.NoteData.Id}.txt";
                var path = Path.Combine(dir, fileName);
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
                    p = new Paragraph() { Margin = new Thickness(0) };
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
            FlowDocument doc;
            if (string.IsNullOrEmpty(content))
            {
                doc = new FlowDocument(new Paragraph(new Run("")) { Margin = new Thickness(0) });
            }
            else if (content.TrimStart().StartsWith("<FlowDocument"))
            {
                try
                {
                    doc = System.Windows.Markup.XamlReader.Parse(content) as FlowDocument ?? new FlowDocument(new Paragraph(new Run("")) { Margin = new Thickness(0) });
                }
                catch
                {
                    doc = new FlowDocument(new Paragraph(new Run(content)) { Margin = new Thickness(0) });
                }
            }
            else
            {
                doc = new FlowDocument(new Paragraph(new Run(content)) { Margin = new Thickness(0) });
            }

            // 设置足够大的宽度以防止自动换行 (20000px)
            doc.PageWidth = 20000;
            rtbContent.Document = doc;
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

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            // 格式化为 v1.4.0 (去掉末尾的 .0 如果是 0)
            string verStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.4.0";
            System.Windows.MessageBox.Show($"StickyNote 便签\n版本: v{verStr}\n\n一个简单好用的桌面便签工具。", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
        }

    }

    // --- 数据管理类 ---

    public class NoteData
    {
        public required string Id { get; set; }
        public string Title { get; set; } = "";
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
        private static string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickyNote", "StickyNotes_Data.json");
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
                // 确保目录存在
                string directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
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
