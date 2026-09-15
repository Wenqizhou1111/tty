using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace MyIme.Shared
{
    /// <summary>
    /// 极简 JSON 读写。对象 = Dictionary&lt;string,object&gt;，数组 = List&lt;object&gt;，
    /// 数字 = double，布尔 = bool，空 = null，字符串 = string。
    /// 手写实现（不用 JavaScriptSerializer / DataContractJsonSerializer），
    /// 这样 AI 接口返回什么奇怪结构都能兜住，也不依赖任何额外程序集。
    /// </summary>
    public static class Json
    {
        // ==================== 解析 ====================

        public static object Parse(string text)
        {
            if (text == null) return null;
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length) return null;
            object v = ParseValue(text, ref i);
            return v;
        }

        /// <summary>解析失败返回 null，不抛异常（AI 返回可能被截断）。</summary>
        public static object TryParse(string text)
        {
            try { return Parse(text); }
            catch { return null; }
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 字面量错误 @" + i);
            i += word.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            Dictionary<string, object> map = new Dictionary<string, object>();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("对象未闭合");
                if (s[i] != '"') throw new FormatException("对象键必须是字符串 @" + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("缺少冒号 @" + i);
                i++;
                object val = ParseValue(s, ref i);
                map[key] = val;
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("对象语法错误 @" + i);
            }
            return map;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            List<object> list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                object val = ParseValue(s, ref i);
                list.Add(val);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("数组语法错误 @" + i);
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串未闭合");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("转义未结束");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 转义不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') { i++; continue; }
                break;
            }
            if (i == start) throw new FormatException("数字格式错误 @" + start);
            string span = s.Substring(start, i - start);
            double d;
            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("数字格式错误: " + span);
        }

        // ==================== 序列化 ====================

        public static string Stringify(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static string StringifyPretty(object value)
        {
            StringBuilder sb = new StringBuilder();
            WriteValueIndented(sb, value, 0);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object v)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is bool) { sb.Append(((bool)v) ? "true" : "false"); return; }
            if (v is IDictionary)
            {
                sb.Append('{');
                bool first = true;
                foreach (DictionaryEntry de in (IDictionary)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteString(sb, Convert.ToString(de.Key, CultureInfo.InvariantCulture));
                    sb.Append(':');
                    WriteValue(sb, de.Value);
                }
                sb.Append('}');
                return;
            }
            if (v is IEnumerable)
            {
                sb.Append('[');
                bool first = true;
                foreach (object item in (IEnumerable)v)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    WriteValue(sb, item);
                }
                sb.Append(']');
                return;
            }
            if (v is double) { sb.Append(Num(((double)v).ToString("R", CultureInfo.InvariantCulture))); return; }
            if (v is float) { sb.Append(Num(((float)v).ToString("R", CultureInfo.InvariantCulture))); return; }
            if (v is decimal) { sb.Append(Num(((decimal)v).ToString(CultureInfo.InvariantCulture))); return; }
            if (v is int || v is long || v is short || v is byte || v is uint || v is ulong || v is sbyte || v is ushort)
            {
                sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
                return;
            }
            if (v is Enum) { WriteString(sb, v.ToString()); return; }
            if (v is DateTime) { WriteString(sb, ((DateTime)v).ToString("o", CultureInfo.InvariantCulture)); return; }
            // 兜底：反射成对象
            WriteReflected(sb, v);
        }

        static string Num(string s)
        {
            // JSON 不接受 NaN / Infinity
            if (s.IndexOf('N') >= 0 || s.IndexOf('I') >= 0) return "0";
            return s;
        }

        static void WriteValueIndented(StringBuilder sb, object v, int depth)
        {
            if (v is IDictionary)
            {
                IDictionary d = (IDictionary)v;
                if (d.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\n");
                bool first = true;
                foreach (DictionaryEntry de in d)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    Indent(sb, depth + 1);
                    WriteString(sb, Convert.ToString(de.Key, CultureInfo.InvariantCulture));
                    sb.Append(": ");
                    WriteValueIndented(sb, de.Value, depth + 1);
                }
                sb.Append('\n'); Indent(sb, depth); sb.Append('}');
                return;
            }
            if (v is IEnumerable && !(v is string))
            {
                List<object> items = new List<object>();
                foreach (object o in (IEnumerable)v) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\n");
                for (int k = 0; k < items.Count; k++)
                {
                    if (k > 0) sb.Append(",\n");
                    Indent(sb, depth + 1);
                    WriteValueIndented(sb, items[k], depth + 1);
                }
                sb.Append('\n'); Indent(sb, depth); sb.Append(']');
                return;
            }
            WriteValue(sb, v);
        }

        static void Indent(StringBuilder sb, int depth) { sb.Append(' ', depth * 2); }

        static void WriteReflected(StringBuilder sb, object v)
        {
            Type t = v.GetType();
            sb.Append('{');
            bool first = true;
            foreach (MemberInfo mi in MappableMembers(t))
            {
                object val = ReadMember(mi, v);
                if (!first) sb.Append(',');
                first = false;
                WriteString(sb, mi.Name);
                sb.Append(':');
                WriteValue(sb, val);
            }
            sb.Append('}');
        }

        static void WriteString(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ==================== 取值助手 ====================

        public static Dictionary<string, object> AsObject(object v)
        {
            return v as Dictionary<string, object>;
        }

        public static List<object> AsArray(object v)
        {
            return v as List<object>;
        }

        public static string GetString(object o, string key, string def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            if (v is string) return (string)v;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public static int GetInt(object o, string key, int def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            if (v is double) return (int)Math.Round((double)v);
            if (v is bool) return ((bool)v) ? 1 : 0;
            int r;
            if (v is string && int.TryParse((string)v, out r)) return r;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static double GetDouble(object o, string key, double def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            if (v is double) return (double)v;
            double r;
            if (v is string && double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return r;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return def; }
        }

        public static bool GetBool(object o, string key, bool def)
        {
            object v = Get(o, key);
            if (v == null) return def;
            if (v is bool) return (bool)v;
            if (v is double) return ((double)v) != 0;
            string s = v as string;
            if (s != null)
            {
                if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
                if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return def;
        }

        public static object Get(object o, string key)
        {
            Dictionary<string, object> map = o as Dictionary<string, object>;
            if (map == null) return null;
            object v;
            if (map.TryGetValue(key, out v)) return v;
            return null;
        }

        public static bool Has(object o, string key)
        {
            Dictionary<string, object> map = o as Dictionary<string, object>;
            if (map == null) return false;
            return map.ContainsKey(key);
        }

        /// <summary>
        /// 按路径取值，路径形如 "choices[0].message.content"。
        /// 接各种 AI 接口时，用路径配置就能适配不同的返回结构。
        /// </summary>
        public static object Select(object root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            object cur = root;
            int i = 0;
            while (i < path.Length)
            {
                if (path[i] == '.') { i++; continue; }
                if (path[i] == '[')
                {
                    int close = path.IndexOf(']', i);
                    if (close < 0) return null;
                    int idx;
                    if (!int.TryParse(path.Substring(i + 1, close - i - 1), out idx)) return null;
                    List<object> arr = cur as List<object>;
                    if (arr == null || idx < 0 || idx >= arr.Count) return null;
                    cur = arr[idx];
                    i = close + 1;
                    continue;
                }
                int end = i;
                while (end < path.Length && path[end] != '.' && path[end] != '[') end++;
                string key = path.Substring(i, end - i);
                if (key.Length > 0)
                {
                    Dictionary<string, object> map = cur as Dictionary<string, object>;
                    if (map == null) return null;
                    object nxt;
                    if (!map.TryGetValue(key, out nxt)) return null;
                    cur = nxt;
                }
                i = end;
            }
            return cur;
        }

        public static bool TrySelectString(object root, string path, out string value)
        {
            value = null;
            object v = Select(root, path);
            if (v == null) return false;
            if (v is string) { value = (string)v; return true; }
            return false;
        }

        // ==================== 反射映射（设置对象 <-> JSON） ====================

        static readonly Dictionary<Type, MemberInfo[]> MemberCache = new Dictionary<Type, MemberInfo[]>();

        internal static MemberInfo[] MappableMembers(Type t)
        {
            MemberInfo[] cached;
            lock (MemberCache)
            {
                if (MemberCache.TryGetValue(t, out cached)) return cached;
            }
            List<MemberInfo> list = new List<MemberInfo>();
            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.CanRead || !p.CanWrite) continue;
                if (p.GetIndexParameters().Length > 0) continue;
                if (!IsMappable(p.PropertyType)) continue;
                list.Add(p);
            }
            foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsMappable(f.FieldType)) continue;
                list.Add(f);
            }
            cached = list.ToArray();
            lock (MemberCache) { MemberCache[t] = cached; }
            return cached;
        }

        static bool IsMappable(Type t)
        {
            if (t.IsEnum) return true;
            if (t == typeof(string) || t == typeof(bool)) return true;
            if (t.IsPrimitive) return true;
            if (t == typeof(decimal)) return true;
            if (t == typeof(List<string>)) return true;
            if (t == typeof(Dictionary<string, string>)) return true;
            if (t.IsClass && !t.IsAbstract && t != typeof(object)) return true;
            return false;
        }

        internal static object ReadMember(MemberInfo mi, object instance)
        {
            PropertyInfo p = mi as PropertyInfo;
            if (p != null) return p.GetValue(instance, null);
            return ((FieldInfo)mi).GetValue(instance);
        }

        internal static void WriteMember(MemberInfo mi, object instance, object value)
        {
            PropertyInfo p = mi as PropertyInfo;
            if (p != null) { p.SetValue(instance, value, null); return; }
            ((FieldInfo)mi).SetValue(instance, value);
        }

        /// <summary>把 JSON 对象里的字段灌进一个已存在的对象（反射，含嵌套对象）。</summary>
        public static void Apply(object target, object json)
        {
            if (target == null) return;
            Dictionary<string, object> map = json as Dictionary<string, object>;
            if (map == null) return;
            foreach (MemberInfo mi in MappableMembers(target.GetType()))
            {
                object raw;
                if (!map.TryGetValue(mi.Name, out raw)) continue;
                Type mt = (mi is PropertyInfo) ? ((PropertyInfo)mi).PropertyType : ((FieldInfo)mi).FieldType;

                // 嵌套配置对象要灌进"已有实例"，否则 JSON 里没写的字段会丢掉默认值
                if (IsNestedObjectType(mt))
                {
                    object current = ReadMember(mi, target);
                    if (current != null) { Apply(current, raw); continue; }
                }
                object converted;
                if (Coerce(raw, mt, out converted)) WriteMember(mi, target, converted);
            }
        }

        static bool IsNestedObjectType(Type t)
        {
            if (t == typeof(string) || t.IsEnum) return false;
            if (t == typeof(List<string>) || t == typeof(Dictionary<string, string>)) return false;
            if (!t.IsClass || t == typeof(object) || t.IsAbstract) return false;
            return true;
        }

        public static bool Coerce(object raw, Type target, out object result)
        {
            result = null;
            if (target == typeof(string)) { result = raw == null ? null : (raw is string ? (string)raw : Stringify(raw)); return true; }
            if (target == typeof(bool)) { result = ToBool(raw); return true; }
            if (target == typeof(int)) { result = (int)ToDouble(raw); return true; }
            if (target == typeof(long)) { result = (long)ToDouble(raw); return true; }
            if (target == typeof(double)) { result = ToDouble(raw); return true; }
            if (target == typeof(float)) { result = (float)ToDouble(raw); return true; }
            if (target == typeof(decimal)) { result = (decimal)ToDouble(raw); return true; }
            if (target.IsEnum)
            {
                if (raw is string)
                {
                    try { result = Enum.Parse(target, (string)raw, true); return true; }
                    catch { return false; }
                }
                result = Enum.ToObject(target, (int)ToDouble(raw));
                return true;
            }
            if (target == typeof(List<string>))
            {
                List<string> list = new List<string>();
                List<object> arr = raw as List<object>;
                if (arr != null) foreach (object o in arr) list.Add(o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture));
                else if (raw is string) list.Add((string)raw);
                result = list;
                return true;
            }
            if (target == typeof(Dictionary<string, string>))
            {
                Dictionary<string, string> d = new Dictionary<string, string>();
                Dictionary<string, object> src = raw as Dictionary<string, object>;
                if (src != null)
                {
                    foreach (KeyValuePair<string, object> kv in src)
                        d[kv.Key] = kv.Value == null ? null : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                }
                result = d;
                return true;
            }
            if (raw is Dictionary<string, object>)
            {
                object instance = Activator.CreateInstance(target);
                Apply(instance, raw);
                result = instance;
                return true;
            }
            return false;
        }

        static bool ToBool(object raw)
        {
            if (raw is bool) return (bool)raw;
            if (raw is double) return ((double)raw) != 0;
            if (raw is string)
            {
                string s = (string)raw;
                return s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1";
            }
            return false;
        }

        static double ToDouble(object raw)
        {
            if (raw == null) return 0;
            if (raw is double) return (double)raw;
            if (raw is bool) return ((bool)raw) ? 1 : 0;
            if (raw is string)
            {
                double d;
                if (double.TryParse((string)raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                return 0;
            }
            try { return Convert.ToDouble(raw, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        /// <summary>深拷贝（走一遍 JSON，简单可靠）。</summary>
        public static T Clone<T>(T source) where T : class, new()
        {
            T copy = new T();
            Apply(copy, Parse(Stringify(source)));
            return copy;
        }
    }
}
