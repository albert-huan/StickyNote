using System;
using System.IO;

namespace StickyNote
{
    public static class Logger
    {
        private static string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");

        public static void Log(string message)
        {
            // Logging disabled
            /*
            try
            {
                File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
            }
            catch { }
            */
        }
    }
}
