using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BetterGlorious
{
    /// <summary>Minimal JSON helpers: enough to talk to the UI page without a dependency.</summary>
    public static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '<' || c == '>') sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Bool(bool b) { return b ? "true" : "false"; }

        /// <summary>Parses objects, arrays, strings, numbers (as double), true/false/null.</summary>
        public static object Parse(string json)
        {
            int i = 0;
            var v = Value(json, ref i);
            return v;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var o = new Dictionary<string, object>();
                i++; Skip(s, ref i);
                if (s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    string k = String(s, ref i);
                    Skip(s, ref i); i++; // ':'
                    o[k] = Value(s, ref i);
                    Skip(s, ref i);
                    if (s[i++] == '}') return o;
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++; Skip(s, ref i);
                if (s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (s[i++] == ']') return a;
                }
            }
            if (c == '"') return String(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i).StartsWith("false")) { i += 5; return false; }
            if (s.Substring(i).StartsWith("null")) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static string String(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            i++;
            return sb.ToString();
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }
}
