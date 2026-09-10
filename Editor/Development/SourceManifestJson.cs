using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Deucarian.PackageInstaller.Editor.Development
{
    // Deliberately package-manifest scoped. Offsets preserve all unrelated JSON text.
    // A complete parse rejects duplicates and malformed input before any write.
    internal sealed class SourceManifestJson
    {
        internal sealed class Member
        {
            internal string Name;
            internal string Value;
            internal int Start, ValueStart, End, Comma = -1;
            internal List<Member> Members;
        }

        private readonly string _text;
        private int _position;
        private int _depth;

        internal SourceManifestJson(string text) { _text = text; }

        internal Member Parse()
        {
            SkipSpace();
            Member root = ReadValue();
            SkipSpace();
            if (_position != _text.Length || root.Members == null)
                throw Invalid();
            return root;
        }

        private Member ReadValue()
        {
            SkipSpace();
            if (_position >= _text.Length || ++_depth > 64) throw Invalid();
            Member value = new Member { Start = _position, ValueStart = _position };
            char current = _text[_position];
            if (current == '{') value.Members = ReadObject();
            else if (current == '[') ReadArray();
            else if (current == '"') value.Value = ReadString();
            else ReadPrimitive();
            value.End = _position;
            --_depth;
            return value;
        }

        private List<Member> ReadObject()
        {
            ++_position;
            List<Member> members = new List<Member>();
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            SkipSpace();
            if (Take('}')) return members;
            while (true)
            {
                SkipSpace();
                int start = _position;
                string name = ReadString();
                if (!names.Add(name)) throw Invalid();
                SkipSpace();
                if (!Take(':')) throw Invalid();
                Member member = ReadValue();
                member.Name = name;
                member.Start = start;
                members.Add(member);
                SkipSpace();
                if (Take('}')) return members;
                member.Comma = _position;
                if (!Take(',')) throw Invalid();
            }
        }

        private void ReadArray()
        {
            ++_position;
            SkipSpace();
            if (Take(']')) return;
            while (true)
            {
                ReadValue();
                SkipSpace();
                if (Take(']')) return;
                if (!Take(',')) throw Invalid();
            }
        }

        private string ReadString()
        {
            if (!Take('"')) throw Invalid();
            StringBuilder value = new StringBuilder();
            while (_position < _text.Length)
            {
                char current = _text[_position++];
                if (current == '"') return value.ToString();
                if (current < ' ') throw Invalid();
                if (current != '\\') { value.Append(current); continue; }
                if (_position >= _text.Length) throw Invalid();
                char escaped = _text[_position++];
                switch (escaped)
                {
                    case '"': case '\\': case '/': value.Append(escaped); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length || !ushort.TryParse(
                                _text.Substring(_position, 4), NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out ushort code)) throw Invalid();
                        value.Append((char)code);
                        _position += 4;
                        break;
                    default: throw Invalid();
                }
            }
            throw Invalid();
        }

        private void ReadPrimitive()
        {
            int start = _position;
            while (_position < _text.Length && !char.IsWhiteSpace(_text[_position]) &&
                   _text[_position] != ',' && _text[_position] != ']' && _text[_position] != '}')
                ++_position;
            string value = _text.Substring(start, _position - start);
            if (value == "true" || value == "false" || value == "null") return;
            int index = 0;
            if (index < value.Length && value[index] == '-') ++index;
            if (index == value.Length) throw Invalid();
            if (value[index] == '0') ++index;
            else
            {
                if (value[index] < '1' || value[index] > '9') throw Invalid();
                while (index < value.Length && IsDigit(value[index])) ++index;
            }
            if (index < value.Length && value[index] == '.')
            {
                int digits = ++index;
                while (index < value.Length && IsDigit(value[index])) ++index;
                if (digits == index) throw Invalid();
            }
            if (index < value.Length && (value[index] == 'e' || value[index] == 'E'))
            {
                ++index;
                if (index < value.Length && (value[index] == '-' || value[index] == '+')) ++index;
                int digits = index;
                while (index < value.Length && IsDigit(value[index])) ++index;
                if (digits == index) throw Invalid();
            }
            if (index != value.Length) throw Invalid();
        }

        private bool Take(char value)
        {
            if (_position >= _text.Length || _text[_position] != value) return false;
            ++_position;
            return true;
        }

        private void SkipSpace()
        {
            while (_position < _text.Length && (_text[_position] == ' ' ||
                _text[_position] == '\t' || _text[_position] == '\r' || _text[_position] == '\n'))
                ++_position;
        }

        private static bool IsDigit(char value) => value >= '0' && value <= '9';
        private static InvalidOperationException Invalid() =>
            new InvalidOperationException("The package manifest is malformed or has duplicate properties.");

        internal static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\"");
            foreach (char current in value)
            {
                if (current == '"' || current == '\\') result.Append('\\').Append(current);
                else if (current < ' ') result.Append("\\u").Append(((int)current).ToString("x4"));
                else result.Append(current);
            }
            return result.Append('"').ToString();
        }
    }
}
