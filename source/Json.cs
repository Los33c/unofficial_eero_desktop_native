using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace EeroDesktop
{
    // Replaces System.Web.Script.Serialization.JavaScriptSerializer, which does not exist on .NET 10.
    // The object shapes match it because callers depend on them: objects are
    // Dictionary<string, object>, arrays are object[], and numbers are int, long, decimal or double.
    internal static class JsonCodec
    {
        private const int MaxLength = 16 * 1024 * 1024;
        private const int MaxDepth = 100;

        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            if (text.Length > MaxLength) throw new InvalidOperationException("JSON input is too long.");
            var reader = new Reader(text);
            reader.SkipSpace();
            if (reader.AtEnd) return null;
            object value = reader.Value(0);
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Invalid();
            return value;
        }

        public static string Stringify(object value)
        {
            var output = new StringBuilder();
            Write(output, value, 0);
            if (output.Length > MaxLength) throw new InvalidOperationException("JSON output is too long.");
            return output.ToString();
        }

        private sealed class Reader
        {
            private readonly string s;
            private int i;
            public Reader(string text) { s = text; }
            public bool AtEnd { get { return i >= s.Length; } }
            public ArgumentException Invalid() { return new ArgumentException("Invalid JSON at position " + i + "."); }
            public void SkipSpace() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            public object Value(int depth)
            {
                if (depth > MaxDepth) throw new ArgumentException("JSON is nested too deeply.");
                SkipSpace();
                if (AtEnd) throw Invalid();
                char c = s[i];
                if (c == '{') return Map(depth);
                if (c == '[') return Items(depth);
                if (c == '"') return Text();
                if (Word("true")) return true;
                if (Word("false")) return false;
                if (Word("null")) return null;
                return Number();
            }

            private bool Word(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            private Dictionary<string, object> Map(int depth)
            {
                var map = new Dictionary<string, object>();
                i++;
                SkipSpace();
                if (!AtEnd && s[i] == '}') { i++; return map; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || s[i] != '"') throw Invalid();
                    string key = Text();
                    SkipSpace();
                    if (AtEnd || s[i] != ':') throw Invalid();
                    i++;
                    map[key] = Value(depth + 1);
                    SkipSpace();
                    if (AtEnd) throw Invalid();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return map; }
                    throw Invalid();
                }
            }

            private object[] Items(int depth)
            {
                var items = new List<object>();
                i++;
                SkipSpace();
                if (!AtEnd && s[i] == ']') { i++; return items.ToArray(); }
                while (true)
                {
                    items.Add(Value(depth + 1));
                    SkipSpace();
                    if (AtEnd) throw Invalid();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return items.ToArray(); }
                    throw Invalid();
                }
            }

            private string Text()
            {
                var b = new StringBuilder();
                i++;
                while (true)
                {
                    if (AtEnd) throw Invalid();
                    char c = s[i++];
                    if (c == '"') return b.ToString();
                    if (c != '\\') { b.Append(c); continue; }
                    if (AtEnd) throw Invalid();
                    c = s[i++];
                    switch (c)
                    {
                        case '"': case '\\': case '/': b.Append(c); break;
                        case 'b': b.Append('\b'); break;
                        case 'f': b.Append('\f'); break;
                        case 'n': b.Append('\n'); break;
                        case 'r': b.Append('\r'); break;
                        case 't': b.Append('\t'); break;
                        case 'u':
                            int code;
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code)) throw Invalid();
                            b.Append((char)code);
                            i += 4;
                            break;
                        default: throw Invalid();
                    }
                }
            }

            private object Number()
            {
                int start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
                string token = s.Substring(start, i - start);
                int small; long large; decimal exact; double approximate;
                if (int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out small)) return small;
                if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out large)) return large;
                bool exponent = token.IndexOf('e') >= 0 || token.IndexOf('E') >= 0;
                if (!exponent && decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out exact)) return exact;
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out approximate) && !double.IsInfinity(approximate)) return approximate;
                i = start;
                throw Invalid();
            }
        }

        private static void Write(StringBuilder o, object value, int depth)
        {
            if (depth > MaxDepth) throw new ArgumentException("JSON is nested too deeply.");
            if (value == null || value is DBNull) { o.Append("null"); return; }
            if (value is string) { Quote(o, (string)value); return; }
            if (value is char) { Quote(o, value.ToString()); return; }
            if (value is bool) { o.Append((bool)value ? "true" : "false"); return; }
            if (value is DateTime)
            {
                long ms = (((DateTime)value).ToUniversalTime().Ticks - 621355968000000000L) / 10000L;
                o.Append("\"\\/Date(").Append(ms.ToString(CultureInfo.InvariantCulture)).Append(")\\/\"");
                return;
            }
            if (value is Guid) { Quote(o, value.ToString()); return; }
            if (value is Uri) { o.Append('"').Append(((Uri)value).GetComponents(UriComponents.SerializationInfoString, UriFormat.UriEscaped)).Append('"'); return; }
            if (value is Enum) { o.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is double) { o.Append(((double)value).ToString("r", CultureInfo.InvariantCulture)); return; }
            if (value is float) { o.Append(((float)value).ToString("r", CultureInfo.InvariantCulture)); return; }
            if (value is sbyte || value is byte || value is short || value is ushort || value is int || value is uint || value is long || value is ulong || value is decimal)
            {
                o.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            var map = value as IDictionary;
            if (map != null)
            {
                o.Append('{');
                bool first = true;
                foreach (DictionaryEntry entry in map)
                {
                    if (!first) o.Append(',');
                    first = false;
                    Quote(o, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    o.Append(':');
                    Write(o, entry.Value, depth + 1);
                }
                o.Append('}');
                return;
            }
            var items = value as IEnumerable;
            if (items != null)
            {
                o.Append('[');
                bool first = true;
                foreach (object item in items)
                {
                    if (!first) o.Append(',');
                    first = false;
                    Write(o, item, depth + 1);
                }
                o.Append(']');
                return;
            }
            WriteMembers(o, value, depth);
        }

        private static void WriteMembers(StringBuilder o, object value, int depth)
        {
            o.Append('{');
            bool first = true;
            Type type = value.GetType();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!first) o.Append(',');
                first = false;
                Quote(o, field.Name);
                o.Append(':');
                Write(o, field.GetValue(value), depth + 1);
            }
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || property.GetGetMethod() == null) continue;
                if (!first) o.Append(',');
                first = false;
                Quote(o, property.Name);
                o.Append(':');
                Write(o, property.GetValue(value, null), depth + 1);
            }
            o.Append('}');
        }

        // Same escaping as HttpUtility.JavaScriptStringEncode, which JavaScriptSerializer used.
        private static void Quote(StringBuilder o, string text)
        {
            o.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': o.Append("\\\""); break;
                    case '\\': o.Append("\\\\"); break;
                    case '\b': o.Append("\\b"); break;
                    case '\f': o.Append("\\f"); break;
                    case '\n': o.Append("\\n"); break;
                    case '\r': o.Append("\\r"); break;
                    case '\t': o.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\'' || c == '<' || c == '>' || c == '&' || c == 0x85 || c == 0x2028 || c == 0x2029)
                            o.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else o.Append(c);
                        break;
                }
            }
            o.Append('"');
        }
    }
}
