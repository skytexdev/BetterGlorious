using System;
using System.IO;

namespace BetterGlorious
{
    public static class AppInfo
    {
        public const string Name = "BetterGlorious";
        public const string Version = "0.1.0";
        public const string RepoUrl = "https://github.com/skytexdev/BetterGlorious";

        public static string DataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }

    public static class Log
    {
        static readonly object sync = new object();

        static string FilePath { get { return Path.Combine(AppInfo.DataDir, "log.txt"); } }

        public static void Info(string msg) { Write("INFO", msg); }

        public static void Error(string where, Exception ex) { Write("ERROR", where + ": " + ex.Message); }

        static void Write(string level, string msg)
        {
            try
            {
                lock (sync)
                {
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
