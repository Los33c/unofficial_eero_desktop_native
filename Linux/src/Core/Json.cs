using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;

namespace EeroDesktop
{
    // JSON helpers with the same object model and output as the Windows build
    // (System.Web.Script.Serialization.JavaScriptSerializer): objects become
    // Dictionary<string, object> in document order, arrays become object[],
    // whole numbers become int/long (or decimal when larger), other numbers decimal.
    public static class J
    {
        private const int MaxLength = 16 * 1024 * 1024, MaxDepth = 100;
        public static Dictionary<string, object> Obj(params object[] pairs)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }
        public static object Get(object o, params string[] keys)
        {
            foreach (string k in keys)
            {
                var d = o as IDictionary<string, object>;
                if (d == null || !d.TryGetValue(k, out o)) return null;
            }
            return o;
        }
        public static string S(object o, params string[] keys)
        {
            object v = keys.Length == 0 ? o : Get(o, keys);
            return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture);
        }
        public static bool B(object o, params string[] keys) { return S(o, keys).Equals("true", StringComparison.OrdinalIgnoreCase); }
        public static List<object> List(object o)
        {
            var d = o as IDictionary<string, object>;
            if (d != null) return d.ContainsKey("data") ? List(d["data"]) : new List<object>();
            var a = o as IEnumerable;
            if (a == null || o is string) return new List<object>();
            return a.Cast<object>().ToList();
        }
        public static string First(object o, params string[] keys)
        {
            foreach (string k in keys) { string s = S(o, k); if (s.Length > 0) return s; }
            return "";
        }
        public static object Parse(string s)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (s.Length > MaxLength) throw new ArgumentException("The JSON input is too large.");
            var reader = new Reader(s);
            reader.Space();
            object value = reader.Value(0);
            reader.Space();
            if (!reader.End) throw new ArgumentException("Invalid JSON primitive after the document.");
            return value;
        }
        public static string Stringify(object o)
        {
            var b = new StringBuilder();
            Write(b, o, 0);
            if (b.Length > MaxLength) throw new InvalidOperationException("The JSON output is too large.");
            return b.ToString();
        }

        private sealed class Reader
        {
            private readonly string s; private int i;
            public Reader(string text) { s = text; }
            public bool End { get { return i >= s.Length; } }
            public void Space() { while (i < s.Length && Char.IsWhiteSpace(s[i])) i++; }
            private ArgumentException Bad(string what) { return new ArgumentException("Invalid JSON: " + what + " at position " + i + "."); }
            public object Value(int depth)
            {
                if (depth > MaxDepth) throw Bad("nesting too deep");
                if (End) throw Bad("unexpected end");
                char c = s[i];
                if (c == '{') return Object(depth);
                if (c == '[') return Array(depth);
                if (c == '"' || c == '\'') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                throw Bad("unexpected character '" + c + "'");
            }
            private bool Word(string w)
            {
                if (String.CompareOrdinal(s, i, w, 0, w.Length) != 0) return false;
                int end = i + w.Length;
                if (end < s.Length && (Char.IsLetterOrDigit(s[end]) || s[end] == '_')) return false;
                i = end; return true;
            }
            private Dictionary<string, object> Object(int depth)
            {
                var d = new Dictionary<string, object>(); i++; Space();
                if (!End && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Space();
                    if (End || (s[i] != '"' && s[i] != '\'')) throw Bad("expected a property name");
                    string key = ReadString(); Space();
                    if (End || s[i] != ':') throw Bad("expected ':'");
                    i++; Space();
                    d[key] = Value(depth + 1); Space();
                    if (End) throw Bad("unterminated object");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Bad("expected ',' or '}'");
                }
            }
            private object[] Array(int depth)
            {
                var list = new List<object>(); i++; Space();
                if (!End && s[i] == ']') { i++; return list.ToArray(); }
                while (true)
                {
                    Space(); list.Add(Value(depth + 1)); Space();
                    if (End) throw Bad("unterminated array");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list.ToArray(); }
                    throw Bad("expected ',' or ']'");
                }
            }
            private string ReadString()
            {
                char quote = s[i++]; var b = new StringBuilder();
                while (true)
                {
                    if (End) throw Bad("unterminated string");
                    char c = s[i++];
                    if (c == quote) return b.ToString();
                    if (c != '\\') { b.Append(c); continue; }
                    if (End) throw Bad("unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': b.Append('"'); break;
                        case '\'': b.Append('\''); break;
                        case '\\': b.Append('\\'); break;
                        case '/': b.Append('/'); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            int code;
                            if (i + 4 > s.Length || !Int32.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw Bad("invalid unicode escape");
                            b.Append((char)code); i += 4; break;
                        default: throw Bad("invalid escape");
                    }
                }
            }
            private object Number()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (Char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                string text = s.Substring(start, i - start);
                int n; long l; decimal m; double d;
                if (Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
                if (Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                if (Decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out m)) return m;
                if (Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                throw Bad("invalid number");
            }
        }

        private static void Write(StringBuilder b, object o, int depth)
        {
            if (depth > MaxDepth) throw new InvalidOperationException("The object graph is nested too deeply to serialize.");
            if (o == null || o is DBNull) { b.Append("null"); return; }
            var s = o as string;
            if (s != null) { Quote(b, s); return; }
            if (o is char) { Quote(b, o.ToString()); return; }
            if (o is bool) { b.Append((bool)o ? "true" : "false"); return; }
            if (o is double) { double d = (double)o; b.Append(Double.IsNaN(d) || Double.IsInfinity(d) ? "\"" + d.ToString(CultureInfo.InvariantCulture) + "\"" : d.ToString("r", CultureInfo.InvariantCulture)); return; }
            if (o is float) { b.Append(((float)o).ToString("r", CultureInfo.InvariantCulture)); return; }
            if (o is Enum) { b.Append(Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (o is int || o is long || o is short || o is byte || o is sbyte || o is uint || o is ulong || o is ushort || o is decimal) { b.Append(Convert.ToString(o, CultureInfo.InvariantCulture)); return; }
            if (o is DateTime) { var t = ((DateTime)o).ToUniversalTime(); b.Append("\"\\/Date(" + (long)(t - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds + ")\\/\""); return; }
            if (o is Guid || o is Uri) { Quote(b, o.ToString()); return; }
            var map = o as IDictionary<string, object>;
            if (map != null) { WriteMap(b, map.Select(p => new KeyValuePair<string, object>(p.Key, p.Value)), depth); return; }
            var legacy = o as IDictionary;
            if (legacy != null) { WriteMap(b, legacy.Keys.Cast<object>().Select(k => new KeyValuePair<string, object>(Convert.ToString(k, CultureInfo.InvariantCulture), legacy[k])), depth); return; }
            var list = o as IEnumerable;
            if (list != null)
            {
                b.Append('['); bool first = true;
                foreach (object item in list) { if (!first) b.Append(','); first = false; Write(b, item, depth + 1); }
                b.Append(']'); return;
            }
            // Plain objects serialize their public properties and fields, like JavaScriptSerializer.
            var members = new List<KeyValuePair<string, object>>();
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)) members.Add(new KeyValuePair<string, object>(f.Name, f.GetValue(o)));
            foreach (PropertyInfo p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) if (p.CanRead && p.GetIndexParameters().Length == 0) members.Add(new KeyValuePair<string, object>(p.Name, p.GetValue(o, null)));
            WriteMap(b, members, depth);
        }
        private static void WriteMap(StringBuilder b, IEnumerable<KeyValuePair<string, object>> pairs, int depth)
        {
            b.Append('{'); bool first = true;
            foreach (var p in pairs) { if (!first) b.Append(','); first = false; Quote(b, p.Key); b.Append(':'); Write(b, p.Value, depth + 1); }
            b.Append('}');
        }
        // Same escaping as JavaScriptSerializer: short escapes for common controls and
        // \u00XX for other controls and the HTML-sensitive characters < > ' &.
        private static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\b': b.Append("\\b"); break;
                    case '\f': b.Append("\\f"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '<' || c == '>' || c == '\'' || c == '&') b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
