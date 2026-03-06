using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StickyNote
{
    public class NoteData
    {
        public required string Id { get; set; }
        public string Title { get; set; } = "";
        public required string Content { get; set; }   // RTF 格式
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsTopmost { get; set; }
        public required string ColorHex { get; set; }
        public double Opacity { get; set; } = 1.0;
        public float FontSize { get; set; } = 13f;
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public bool IsUnderline { get; set; }
        public bool IsStrikethrough { get; set; }
        public string Alignment { get; set; } = "Left";
        public bool IsCustomTitle { get; set; } = false;

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
    }

    public static class NoteManager
    {
        private static readonly string DataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StickyNote");

        private static readonly string FilePath     = Path.Combine(DataDir, "StickyNotes_Data.json");
        private static readonly string SettingsPath = Path.Combine(DataDir, "AppSettings.json");

        private static List<NoteData> _notes = new();
        private static NoteData? _lastDeleted;

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
        }

        public static void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch { }
        }
    }
}
