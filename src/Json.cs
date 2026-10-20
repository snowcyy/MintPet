using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MintPet
{
    /// <summary>
    /// Minimal JSON reader (object / array / string / number / bool / null).
    /// Only used for the DeepSeek balance response, so it stays small and has
    /// no external assemblies behind it.
    /// </summary>
    internal static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("空的 JSON。");

            int position = 0;
            object value = ParseValue(text, ref position);
            SkipWhitespace(text, ref position);
            if (position != text.Length) throw new FormatException("JSON 结尾有多余字符。");
            return value;
        }

        private static object ParseValue(string s, ref int p)
        {
            SkipWhitespace(s, ref p);
            if (p >= s.Length) throw new FormatException("JSON 意外结束。");

            char c = s[p];
            if (c == '{') return ParseObject(s, ref p);
            if (c == '[') return ParseArray(s, ref p);
            if (c == '"') return ParseString(s, ref p);
            if (c == 't') { Expect(s, ref p, "true"); return true; }
            if (c == 'f') { Expect(s, ref p, "false"); return false; }
            if (c == 'n') { Expect(s, ref p, "null"); return null; }
            return ParseNumber(s, ref p);
        }

        private static Dictionary<string, object> ParseObject(string s, ref int p)
        {
            p++; // {
            Dictionary<string, object> result = new Dictionary<string, object>();

            SkipWhitespace(s, ref p);
            if (p < s.Length && s[p] == '}')
            {
                p++;
                return result;
            }

            while (true)
            {
                SkipWhitespace(s, ref p);
                if (p >= s.Length || s[p] != '"') throw new FormatException("JSON 对象的键必须是字符串。");

                string key = ParseString(s, ref p);
                SkipWhitespace(s, ref p);
                if (p >= s.Length || s[p] != ':') throw new FormatException("JSON 对象缺少冒号。");
                p++;

                result[key] = ParseValue(s, ref p);

                SkipWhitespace(s, ref p);
                if (p >= s.Length) throw new FormatException("JSON 意外结束。");
                if (s[p] == ',') { p++; continue; }
                if (s[p] == '}') { p++; return result; }
                throw new FormatException("JSON 对象格式错误。");
            }
        }

        private static List<object> ParseArray(string s, ref int p)
        {
            p++; // [
            List<object> result = new List<object>();

            SkipWhitespace(s, ref p);
            if (p < s.Length && s[p] == ']')
            {
                p++;
                return result;
            }

            while (true)
            {
                result.Add(ParseValue(s, ref p));

                SkipWhitespace(s, ref p);
                if (p >= s.Length) throw new FormatException("JSON 意外结束。");
                if (s[p] == ',') { p++; continue; }
                if (s[p] == ']') { p++; return result; }
                throw new FormatException("JSON 数组格式错误。");
            }
        }

        private static string ParseString(string s, ref int p)
        {
            p++; // opening quote
            StringBuilder text = new StringBuilder();

            while (true)
            {
                if (p >= s.Length) throw new FormatException("JSON 字符串意外结束。");

                char c = s[p++];
                if (c == '"') return text.ToString();

                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }

                if (p >= s.Length) throw new FormatException("JSON 转义意外结束。");
                char escape = s[p++];
                switch (escape)
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (p + 4 > s.Length) throw new FormatException("JSON Unicode 转义不完整。");
                        string hex = s.Substring(p, 4);
                        p += 4;
                        int code;
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            throw new FormatException("JSON Unicode 转义无效。");
                        text.Append((char)code);
                        break;
                    default:
                        throw new FormatException("JSON 转义字符无法识别。");
                }
            }
        }

        private static object ParseNumber(string s, ref int p)
        {
            int start = p;
            while (p < s.Length)
            {
                char c = s[p];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') p++;
                else break;
            }

            if (start == p) throw new FormatException("JSON 数值无效。");

            string raw = s.Substring(start, p - start);
            double number;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                throw new FormatException("JSON 数值无效：" + raw);
            return number;
        }

        private static void Expect(string s, ref int p, string literal)
        {
            if (p + literal.Length > s.Length || string.CompareOrdinal(s, p, literal, 0, literal.Length) != 0)
                throw new FormatException("JSON 字面量无效。");
            p += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int p)
        {
            while (p < s.Length)
            {
                char c = s[p];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') p++;
                else break;
            }
        }
    }
}
