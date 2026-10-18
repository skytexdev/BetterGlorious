using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace BetterGlorious
{
    /// <summary>
    /// Looks at the latest GitHub release a few seconds after start and then twice a day.
    /// Only reads the public releases API; nothing is downloaded or installed.
    /// </summary>
    public static class UpdateChecker
    {
        public static string LatestVersion { get; private set; }
        public static string ReleaseUrl { get; private set; }
        public static bool Available { get { return LatestVersion != null && Newer(LatestVersion, AppInfo.Version); } }

        /// <summary>Raised on a worker thread when a newer release is found.</summary>
        public static event Action Found;

        static Timer timer;

        public static void Start()
        {
            timer = new Timer(_ => Check(), null, TimeSpan.FromSeconds(10), TimeSpan.FromHours(12));
        }

        static void Check()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                var api = AppInfo.RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest";
                var req = (HttpWebRequest)WebRequest.Create(api);
                req.UserAgent = AppInfo.Name + "/" + AppInfo.Version;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                string body;
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream()))
                    body = r.ReadToEnd();
                var json = Json.Parse(body) as Dictionary<string, object>;
                if (json == null) return;
                var tag = (json.ContainsKey("tag_name") ? json["tag_name"] as string : null) ?? "";
                tag = tag.TrimStart('v', 'V');
                if (tag.Length == 0) return;
                bool wasAvailable = Available;
                LatestVersion = tag;
                ReleaseUrl = json.ContainsKey("html_url") ? json["html_url"] as string : AppInfo.RepoUrl + "/releases";
                if (Available && !wasAvailable && Found != null) Found();
            }
            catch (WebException) { } // offline, or no release published yet (404)
            catch (Exception ex) { Log.Error("update check", ex); }
        }

        /// <summary>True when a is a higher dotted version than b (non-numeric parts count as 0).</summary>
        public static bool Newer(string a, string b)
        {
            var x = a.Split('.', '-');
            var y = b.Split('.', '-');
            for (int i = 0; i < 3; i++)
            {
                int p, q;
                int.TryParse(i < x.Length ? x[i] : "0", out p);
                int.TryParse(i < y.Length ? y[i] : "0", out q);
                if (p != q) return p > q;
            }
            return false;
        }
    }
}
