using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace BetterGlorious
{
    /// <summary>
    /// Tray and notification text. Each language is src/Locales/&lt;code&gt;.json, embedded in the exe
    /// by build.bat. English is the source; a missing key falls back to it. The panel's own text
    /// lives in web/src/locales and uses the same language codes.
    /// </summary>
    public static class T
    {
        /// <summary>Language codes the app ships, in the same spelling as the web UI.</summary>
        public static readonly string[] Codes = { "en", "de", "es", "fr", "it", "pl", "pt-BR", "ru", "tr", "ja", "ko", "zh-CN" };

        static readonly Dictionary<string, string> En = Load("en");
        static Dictionary<string, string> current = En;

        /// <summary>The active language code, e.g. "en", "tr", "pt-BR".</summary>
        public static string Code { get; private set; }

        static T() { Code = "en"; }

        /// <summary>"auto" follows Windows' display language.</summary>
        public static void Init(string language)
        {
            string code = Resolve(language == null || language == "auto" ? CultureInfo.CurrentUICulture.Name : language);
            Code = code;
            current = code == "en" ? En : (Load(code) ?? En);
        }

        static string Resolve(string want)
        {
            foreach (var c in Codes)
                if (string.Equals(c, want, StringComparison.OrdinalIgnoreCase)) return c;
            string two = want.Split('-')[0].ToLowerInvariant();
            if (two == "pt") return "pt-BR";
            if (two == "zh") return "zh-CN";
            foreach (var c in Codes)
                if (c == two) return c;
            return "en";
        }

        static Dictionary<string, string> Load(string code)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("BetterGlorious.Locales." + code + ".json"))
                {
                    if (s == null) return null;
                    using (var r = new StreamReader(s))
                    {
                        var parsed = Json.Parse(r.ReadToEnd()) as Dictionary<string, object>;
                        var d = new Dictionary<string, string>();
                        if (parsed != null)
                            foreach (var kv in parsed)
                                if (kv.Value is string && ((string)kv.Value).Length > 0) d[kv.Key] = (string)kv.Value;
                        return d;
                    }
                }
            }
            catch (Exception ex) { Log.Error("locale " + code, ex); return null; }
        }

        public static string Get(string key)
        {
            string s;
            if (current != null && current.TryGetValue(key, out s)) return s;
            return En != null && En.TryGetValue(key, out s) ? s : key;
        }

        /// <summary>"84%", "%84" or "84 %", as the language writes it.</summary>
        public static string Percent(int p)
        {
            try { return (p / 100.0).ToString("P0", CultureInfo.GetCultureInfo(Code)); }
            catch (CultureNotFoundException) { return p + "%"; }
        }
    }
}
