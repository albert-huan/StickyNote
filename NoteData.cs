using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickyNote
{
    public class NoteData
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";   // RTF 格式
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsTopmost { get; set; }
        public string ColorHex { get; set; } = "#DCEDC8";
        public double Opacity { get; set; } = 1.0;
        public float FontSize { get; set; } = 13f;
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikethrough { get; set; }
        public string Alignment { get; set; } = "Left";
        public bool IsCustomTitle { get; set; } = false;

        // 组织与管理：多标签
        public List<string> Tags { get; set; } = new();

        // P1: 纯文本预览缓存（不序列化，运行时生成）
        [JsonIgnore]
        public string PlainPreview { get; set; } = "";
    }

    /// <summary>
    /// 应用级设置（窗口位置、分隔条宽度等）— 保存在独立 JSON 中
    /// </summary>
    public class AppSettings
    {
        public int WindowLeft   { get; set; } = -1;
        public int WindowTop    { get; set; } = -1;
        public int WindowWidth  { get; set; } = 520;
        public int WindowHeight { get; set; } = 340;
        public int SplitterWidth { get; set; } = 110;

        // 编辑区显示设置
        public bool EditorWordWrap { get; set; } = false;
        public bool ShowHorizontalScrollBar { get; set; } = true;
        public bool ShowVerticalScrollBar { get; set; } = true;

        // 底部工具栏是否随窗口宽度自动换行
        public bool ToolbarAutoWrap { get; set; } = true;

        // UI 缩放与主题
        public int UiScalePercent { get; set; } = 100;          // 90/100/125/150
        public string ThemeMode { get; set; } = "System";       // Light / Dark / System

        // 自动保存间隔（秒）：0 代表手动保存
        public int AutoSaveSeconds { get; set; } = 10;

        // 启动后最小化到托盘
        public bool StartMinimizedToTray { get; set; } = false;

        // 全局置顶可覆盖单便签
        public bool GlobalTopmost { get; set; } = false;

        // 自动备份策略
        public int BackupKeepDays { get; set; } = 7;
        public int BackupKeepVersions { get; set; } = 20;

        // 拖拽吸附阈值（px），0=关闭吸附
        public int SnapThresholdPx { get; set; } = 8;

        // 颜色菜单：最近使用颜色（最多 10 个）
        public List<string> RecentColorHexes { get; set; } = new();

        // 首次引导：是否已展示（展示后不重复打扰）
        public bool OnboardingDismissed { get; set; } = false;
    }

    public static class NoteManager
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StickyNote");

        private static readonly string FilePath     = Path.Combine(DataDir, "StickyNotes_Data.json");
        private static readonly string SettingsPath = Path.Combine(DataDir, "AppSettings.json");

        private static readonly string BackupDir = Path.Combine(DataDir, "backups");

        private static List<NoteData> _notes = new();
        private static NoteData? _lastDeleted;

        private static DateTime _lastBackupUtc = DateTime.MinValue;
        private static string _lastBackupFingerprint = string.Empty;

        private static AppSettings _settings = new();
        public static AppSettings Settings => _settings;

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
                Directory.CreateDirectory(DataDir);
                string json  = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = false });
                string temp  = FilePath + ".tmp";
                string bak   = FilePath + ".bak";
                File.WriteAllText(temp, json);
                if (File.Exists(FilePath)) File.Replace(temp, FilePath, bak);
                else File.Move(temp, FilePath);

                TryCreateBackupSnapshot(json);
            }
            catch { }
        }

        public static List<NoteData> LoadNotes()
        {
            if (!File.Exists(FilePath)) { _notes = new(); return _notes; }
            try
            {
                string json = File.ReadAllText(FilePath);
                _notes = JsonSerializer.Deserialize<List<NoteData>>(json) ?? new();
                _notes = _notes.GroupBy(n => n.Id).Select(g => g.Last()).ToList();
                return _notes;
            }
            catch { _notes = new(); return _notes; }
        }

        public static IReadOnlyList<NoteData> GetAll() => _notes;

        public static bool RestoreLastDeleted()
        {
            if (_lastDeleted == null) return false;
            _notes.Add(_lastDeleted);
            _lastDeleted = null;
            SaveNotes();
            return true;
        }

        // ── 应用设置 ────────────────────────────────────────────────
        public static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new();
            }
            catch { _settings = new(); }

            NormalizeSettings();
        }

        public static void SaveSettings()
        {
            try
            {
                NormalizeSettings();
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { }
        }

        private static void NormalizeSettings()
        {
            int[] validScale = { 90, 100, 125, 150 };
            if (!validScale.Contains(_settings.UiScalePercent))
                _settings.UiScalePercent = 100;

            if (!string.Equals(_settings.ThemeMode, "Light", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_settings.ThemeMode, "Dark", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(_settings.ThemeMode, "System", StringComparison.OrdinalIgnoreCase))
            {
                _settings.ThemeMode = "System";
            }

            _settings.AutoSaveSeconds = _settings.AutoSaveSeconds switch
            {
                0 => 0,
                5 => 5,
                10 => 10,
                30 => 30,
                _ => 10
            };

            _settings.BackupKeepDays = Math.Clamp(_settings.BackupKeepDays, 1, 365);
            _settings.BackupKeepVersions = Math.Clamp(_settings.BackupKeepVersions, 1, 500);
            _settings.SnapThresholdPx = Math.Clamp(_settings.SnapThresholdPx, 0, 32);

            _settings.RecentColorHexes ??= new List<string>();
            _settings.RecentColorHexes = _settings.RecentColorHexes
                .Where(IsValidHexColor)
                .Select(NormalizeHexColor)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .ToList();
        }

        private static bool IsValidHexColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            string v = value.Trim();
            if (v.Length != 7 || v[0] != '#') return false;
            for (int i = 1; i < v.Length; i++)
            {
                char c = v[i];
                bool hex = (c >= '0' && c <= '9')
                    || (c >= 'a' && c <= 'f')
                    || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        private static string NormalizeHexColor(string value)
            => value.Trim().ToUpperInvariant();

        private static void TryCreateBackupSnapshot(string json)
        {
            try
            {
                Directory.CreateDirectory(BackupDir);

                string fingerprint = $"{json.Length}:{ComputeSha256(json)}";
                var nowUtc = DateTime.UtcNow;
                bool changed = !string.Equals(fingerprint, _lastBackupFingerprint, StringComparison.Ordinal);
                bool intervalElapsed = (nowUtc - _lastBackupUtc) >= TimeSpan.FromSeconds(30);
                if (!changed || !intervalElapsed) return;

                _lastBackupFingerprint = fingerprint;
                _lastBackupUtc = nowUtc;

                string fileName = $"StickyNotes_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                File.WriteAllText(Path.Combine(BackupDir, fileName), json);

                CleanupBackups();
            }
            catch { }
        }

        private static void CleanupBackups()
        {
            try
            {
                if (!Directory.Exists(BackupDir)) return;

                var files = Directory.GetFiles(BackupDir, "StickyNotes_*.json")
                    .Select(path =>
                    {
                        var fi = new FileInfo(path);
                        var ts = TryParseBackupTimestamp(Path.GetFileNameWithoutExtension(path)) ?? fi.CreationTime;
                        return new { fi.FullName, fi.Name, Timestamp = ts };
                    })
                    .OrderByDescending(x => x.Timestamp)
                    .ToList();

                if (files.Count == 0) return;

                int keepDays = Math.Max(1, _settings.BackupKeepDays);
                int keepVersions = Math.Max(1, _settings.BackupKeepVersions);
                DateTime threshold = DateTime.Now.AddDays(-keepDays);

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in files.Take(keepVersions))
                    keep.Add(item.FullName);

                foreach (var item in files)
                    if (item.Timestamp >= threshold)
                        keep.Add(item.FullName);

                foreach (var item in files)
                    if (!keep.Contains(item.FullName) && File.Exists(item.FullName))
                        File.Delete(item.FullName);
            }
            catch { }
        }

        private static DateTime? TryParseBackupTimestamp(string fileNameWithoutExt)
        {
            // StickyNotes_yyyyMMdd_HHmmss
            const string prefix = "StickyNotes_";
            if (!fileNameWithoutExt.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

            string ts = fileNameWithoutExt.Substring(prefix.Length);
            if (DateTime.TryParseExact(ts, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var result))
            {
                return result;
            }

            return null;
        }

        private static string ComputeSha256(string text)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(text);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }
    }
}
