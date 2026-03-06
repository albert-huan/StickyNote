using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StickyNote
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // 高 DPI 感知
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 单实例检测
            bool created;
            var mutex = new System.Threading.Mutex(true, "StickyNote_SingleInstance", out created);
            if (!created)
            {
                MessageBox.Show("StickyNote 已在运行中！", "StickyNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var app = new StickyApp();
            app.Run();
        }
    }

    /// <summary>
    /// 应用程序主控：管理托盘图标、全局热键、主窗口生命周期
    /// </summary>
    internal class StickyApp : IDisposable
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint min, nint max);

        private const uint MOD_ALT  = 0x0001;
        private const uint MOD_CTRL = 0x0002;
        private const int  HOTKEY_NEW    = 1;
        private const int  HOTKEY_SEARCH = 2;

        private NotifyIcon _tray = null!;
        private StickyForm _mainForm = null!;
        private HotkeyForm _hotkeyForm = null!;
        private System.Windows.Forms.Timer _memTimer = null!;

        public void Run()
        {
            _mainForm   = new StickyForm();
            _hotkeyForm = new HotkeyForm();
            _hotkeyForm.HotkeyPressed += OnGlobalHotkey;

            // 加载窗口位置
            var notes = NoteManager.GetAll();
            if (notes.Count > 0)
            {
                _mainForm.Left = (int)notes[0].Left;
                _mainForm.Top  = (int)notes[0].Top;
            }
            else
            {
                // 居中
                var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
                _mainForm.Left = screen.Left + (screen.Width  - _mainForm.Width)  / 2;
                _mainForm.Top  = screen.Top  + (screen.Height - _mainForm.Height) / 2;
            }

            BuildTray();

            // 注册热键
            _hotkeyForm.Register(HOTKEY_NEW,    MOD_CTRL | MOD_ALT, (uint)Keys.N);
            _hotkeyForm.Register(HOTKEY_SEARCH, MOD_CTRL | MOD_ALT, (uint)Keys.F);

            // 内存定期释放
            _memTimer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _memTimer.Tick += (s, e) => TrimMemory();
            _memTimer.Start();

            _mainForm.Show();
            Application.Run(_mainForm);
        }

        private void OnGlobalHotkey(int id)
        {
            if (id == HOTKEY_NEW)
            {
                _mainForm.Invoke(() =>
                {
                    _mainForm.Show();
                    _mainForm.WindowState = FormWindowState.Normal;
                    _mainForm.Activate();
                    _mainForm.CreateNewTab();
                });
            }
            else if (id == HOTKEY_SEARCH)
            {
                _mainForm.Invoke(() =>
                {
                    NoteSearchForm.Show(NoteManager.GetAll(), id =>
                    {
                        _mainForm.Show();
                        _mainForm.WindowState = FormWindowState.Normal;
                        _mainForm.FocusNote(id);
                    });
                });
            }
        }

        // ── 系统托盘 ────────────────────────────────────────────────
        private void BuildTray()
        {
            _tray = new NotifyIcon { Text = "StickyNote", Visible = true };

            // 图标
            try
            {
                using var stream = typeof(Program).Assembly.GetManifestResourceStream("StickyNote.app.ico");
                if (stream != null) _tray.Icon = new Icon(stream);
                else _tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            }
            catch { _tray.Icon = SystemIcons.Application; }

            // 菜单
            var menu = new ContextMenuStrip();
            MenuItem(menu, "新建便签 (Ctrl+Alt+N)",  () => { ShowMain(); _mainForm.CreateNewTab(); });
            MenuItem(menu, "搜索便签 (Ctrl+Alt+F)",  () =>
            {
                NoteSearchForm.Show(NoteManager.GetAll(), id =>
                {
                    ShowMain();
                    _mainForm.FocusNote(id);
                });
            });
            menu.Items.Add(new ToolStripSeparator());
            MenuItem(menu, "显示主窗口",     () => ShowMain());
            MenuItem(menu, "隐藏主窗口",     () => { _mainForm.Hide(); TrimMemory(); });
            menu.Items.Add(new ToolStripSeparator());

            // 开机自启
            var autoStart = new ToolStripMenuItem("开机自启动")
            {
                CheckOnClick = true,
                Checked = IsAutoStartEnabled()
            };
            autoStart.Click += (s, e) =>
            {
                if (autoStart.Checked) EnableAutoStart(Application.ExecutablePath);
                else DisableAutoStart();
            };
            menu.Items.Add(autoStart);

            // 导出全部
            MenuItem(menu, "导出全部便签", () =>
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "StickyNotes_All.txt");
                var lines = NoteManager.GetAll().Select(n => $"[{n.Title}]\n{TabPanel.StripRtf(n.Content)}\n");
                File.WriteAllText(path, string.Join("\n", lines));
                MessageBox.Show($"已导出到桌面：{Path.GetFileName(path)}");
            });

            // 恢复删除
            MenuItem(menu, "恢复最近删除", () =>
            {
                if (NoteManager.RestoreLastDeleted())
                {
                    ShowMain();
                    _mainForm.Invoke(() => _mainForm.RefreshNotes());
                }
            });

            menu.Items.Add(new ToolStripSeparator());
            MenuItem(menu, "退出", () => Application.Exit());

            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => ShowMain();
        }

        private void ShowMain()
        {
            _mainForm.Show();
            _mainForm.WindowState = FormWindowState.Normal;
            _mainForm.Activate();
        }

        private static void MenuItem(ContextMenuStrip menu, string text, Action action)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (s, e) => action();
            menu.Items.Add(mi);
        }

        private static void TrimMemory()
        {
            GC.Collect(2, GCCollectionMode.Optimized, false);
            GC.WaitForPendingFinalizers();
            SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
        }

        // ── 自启动注册表 ────────────────────────────────────────────
        private const string RunKey  = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        private const string AppName = "StickyNote";
        private static bool IsAutoStartEnabled()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
            return key?.GetValue(AppName) is string v && !string.IsNullOrEmpty(v);
        }
        private static void EnableAutoStart(string exe)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(AppName, $"\"{exe}\"");
        }
        private static void DisableAutoStart()
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(AppName, false);
        }

        public void Dispose()
        {
            _memTimer?.Dispose();
            _tray?.Dispose();
            _hotkeyForm?.Dispose();
        }
    }

    /// <summary>
    /// 隐藏窗口用于接收全局热键消息
    /// </summary>
    internal class HotkeyForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        private const int WM_HOTKEY = 0x0312;

        public event Action<int>? HotkeyPressed;
        private readonly System.Collections.Generic.List<int> _ids = new();

        public HotkeyForm()
        {
            this.ShowInTaskbar = false;
            this.WindowState   = FormWindowState.Minimized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.Size = new Size(0, 0);
            this.CreateHandle(); // B4: 确保句柄在后台创建成功以接收全局热键
        }

        public void Register(int id, uint mod, uint vk)
        {
            if (RegisterHotKey(this.Handle, id, mod, vk)) _ids.Add(id);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                HotkeyPressed?.Invoke(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            foreach (var id in _ids) UnregisterHotKey(this.Handle, id);
            base.Dispose(disposing);
        }

        protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);
    }
}
