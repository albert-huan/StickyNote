using System.Windows;
using System.Linq;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace StickyNote
{
    public partial class App : System.Windows.Application
    {
        private Forms.NotifyIcon _tray;
        private HotkeyWindow _hotkeyWin;
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            try
            {
                // 创建主窗口，它会自动加载所有便签作为标签
                var w = new MainWindow();
                w.Show();
                w.Activate();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"启动错误: {ex.Message}\n{ex.StackTrace}");
                return;
            }

            _tray = new Forms.NotifyIcon();
            _tray.Text = "StickyNote";
            try
            {
                var icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (System.IO.File.Exists(icoPath))
                {
                    _tray.Icon = new Drawing.Icon(icoPath);
                }
                else
                {
                    _tray.Icon = Drawing.Icon.ExtractAssociatedIcon(System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!);
                }
            }
            catch
            {
                var bmp = new Drawing.Bitmap(16, 16);
                using (var g = Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(Drawing.Color.Transparent);
                    using var paper = new Drawing.SolidBrush(Drawing.Color.FromArgb(0xE8, 0xD0, 0x96));
                    using var linePen = new Drawing.Pen(Drawing.Color.FromArgb(0xBF, 0xA8, 0x70), 1);
                    g.FillRectangle(paper, 1, 2, 14, 12);
                    g.DrawLine(linePen, 1, 13, 15, 13);
                }
                var hicon = bmp.GetHicon();
                _tray.Icon = Drawing.Icon.FromHandle(hicon);
            }
            var menu = new Forms.ContextMenuStrip();
            var newItem = new Forms.ToolStripMenuItem("新建便签");
            var showItem = new Forms.ToolStripMenuItem("显示全部");
            var hideItem = new Forms.ToolStripMenuItem("隐藏全部");
            var exitItem = new Forms.ToolStripMenuItem("退出");
            newItem.Click += (s, ev) => { 
                this.Dispatcher.Invoke(() => {
                    var mainWin = this.Windows.OfType<MainWindow>().FirstOrDefault();
                    if (mainWin != null)
                    {
                        mainWin.Show();
                        mainWin.Activate();
                        // 触发新建便签
                        mainWin.CreateNewTab();
                    }
                    else
                    {
                        new MainWindow().Show();
                    }
                }); 
            };
            showItem.Click += (s, ev) => { this.Dispatcher.Invoke(ShowAllWindows); };
            hideItem.Click += (s, ev) => { this.Dispatcher.Invoke(HideAllWindows); };
            exitItem.Click += (s, ev) => { this.Dispatcher.Invoke(() => Shutdown()); };
            menu.Items.Add(newItem);
            menu.Items.Add(showItem);
            menu.Items.Add(hideItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);
            var searchItem = new Forms.ToolStripMenuItem("搜索便签");
            var tileItem = new Forms.ToolStripMenuItem("平铺排列");
            var hotkeyItem = new Forms.ToolStripMenuItem("启用全局热键") { Checked = true, CheckOnClick = true };
            var autoStartItem = new Forms.ToolStripMenuItem("开机自启动") { CheckOnClick = true };
            var exportAllItem = new Forms.ToolStripMenuItem("导出全部便签");
            var restoreItem = new Forms.ToolStripMenuItem("恢复最近删除");
            searchItem.Click += (s, ev) => { this.Dispatcher.Invoke(() => NoteSearch.Show()); };
            tileItem.Click += (s, ev) => { this.Dispatcher.Invoke(TileWindows); };
            hotkeyItem.Click += (s, ev) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (hotkeyItem.Checked)
                        _hotkeyWin?.RegisterCtrlAltN();
                    else
                        _hotkeyWin?.UnregisterCtrlAltN();
                });
            };
            autoStartItem.Checked = IsAutoStartEnabled();
            autoStartItem.Click += (s, ev) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
                    if (autoStartItem.Checked)
                        EnableAutoStart(exe);
                    else
                        DisableAutoStart();
                });
            };
            exportAllItem.Click += (s, ev) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "StickyNotes_All.txt");
                    var lines = NoteManager.GetAll().Select(n => $"[{n.Id}]\n{n.Content}\n");
                    System.IO.File.WriteAllText(path, string.Join("\n", lines));
                    Forms.MessageBox.Show($"已导出到: {path}");
                });
            };
            restoreItem.Click += (s, ev) =>
            {
                this.Dispatcher.Invoke(() =>
                {
                    if (NoteManager.RestoreLastDeleted())
                    {
                        var mainWin = this.Windows.OfType<MainWindow>().FirstOrDefault();
                        if (mainWin != null)
                        {
                            mainWin.Show();
                            mainWin.Activate();
                            // 重新加载标签以显示恢复的便签
                            mainWin.RefreshTabs();
                        }
                    }
                });
            };
            menu.Items.Insert(1, searchItem);
            menu.Items.Insert(2, tileItem);
            menu.Items.Insert(3, hotkeyItem);
            menu.Items.Insert(4, autoStartItem);
            menu.Items.Insert(5, exportAllItem);
            menu.Items.Insert(6, restoreItem);
            _tray.ContextMenuStrip = menu;
            _tray.Visible = true;

            _hotkeyWin = new HotkeyWindow(this);
            _hotkeyWin.RegisterCtrlAltN();
            _hotkeyWin.RegisterCtrlAltF();
        }

        private void ShowAllWindows()
        {
            // 显示主窗口
            foreach (MainWindow w in this.Windows.OfType<MainWindow>())
            {
                if (w.Visibility != Visibility.Visible) w.Show();
                w.Activate();
            }
        }

        private void HideAllWindows()
        {
            foreach (Window w in this.Windows)
            {
                w.Hide();
            }
        }

        private void TileWindows()
        {
            var ws = this.Windows.OfType<MainWindow>().ToList();
            if (ws.Count == 0) return;
            var work = SystemParameters.WorkArea;
            int cols = (int)Math.Ceiling(Math.Sqrt(ws.Count));
            int rows = (int)Math.Ceiling((double)ws.Count / cols);
            double cw = work.Width / cols;
            double ch = work.Height / rows;
            for (int i = 0; i < ws.Count; i++)
            {
                int r = i / cols;
                int c = i % cols;
                var win = ws[i];
                win.Left = work.Left + c * cw;
                win.Top = work.Top + r * ch;
                win.Width = Math.Max(200, cw);
                win.Height = Math.Max(150, ch);
            }
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                _hotkeyWin?.Dispose();
            }
            catch { }
        }

        private class HotkeyWindow : Forms.NativeWindow, IDisposable
        {
            private readonly App _app;
            private const int WM_HOTKEY = 0x0312;
            private const uint MOD_ALT = 0x0001;
            private const uint MOD_CONTROL = 0x0002;
            public HotkeyWindow(App app)
            {
                _app = app;
                var cp = new Forms.CreateParams();
                cp.Caption = "StickyNoteHotkey";
                cp.X = 0;
                cp.Y = 0;
                cp.Height = 0;
                cp.Width = 0;
                cp.Style = 0x800000;
                this.CreateHandle(cp);
            }
            [DllImport("user32.dll")]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
            [DllImport("user32.dll")]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
            public void RegisterCtrlAltN()
            {
                RegisterHotKey(this.Handle, 1, MOD_CONTROL | MOD_ALT, (uint)System.Windows.Forms.Keys.N);
            }
            public void UnregisterCtrlAltN()
            {
                UnregisterHotKey(this.Handle, 1);
            }
            public void RegisterCtrlAltF()
            {
                RegisterHotKey(this.Handle, 2, MOD_CONTROL | MOD_ALT, (uint)System.Windows.Forms.Keys.F);
            }
            protected override void WndProc(ref Forms.Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)1)
                    _app.Dispatcher.Invoke(() => {
                        var mainWin = _app.Windows.OfType<MainWindow>().FirstOrDefault();
                        if (mainWin != null)
                        {
                            mainWin.Show();
                            mainWin.Activate();
                            mainWin.CreateNewTab();
                        }
                        else
                        {
                            new MainWindow().Show();
                        }
                    });
                if (m.Msg == WM_HOTKEY && m.WParam == (IntPtr)2)
                    _app.Dispatcher.Invoke(() => NoteSearch.Show());
                base.WndProc(ref m);
            }
            public void Dispose()
            {
                UnregisterHotKey(this.Handle, 1);
                this.DestroyHandle();
            }
        }

        private static readonly string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private static readonly string AutoStartName = "StickyNote";
        private static bool IsAutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (key == null) return false;
            var val = key.GetValue(AutoStartName) as string;
            return !string.IsNullOrEmpty(val);
        }
        private static void EnableAutoStart(string exe)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(AutoStartName, $"\"{exe}\"");
        }
        private static void DisableAutoStart()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AutoStartName, false);
        }
    }
}
