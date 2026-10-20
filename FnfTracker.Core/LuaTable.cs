using System.Globalization;
using System.Text;

namespace FnfTracker.Core;

/// <summary>
/// A Lua table as written by WoW's SavedVariables serializer: string keys go in
/// <see cref="Fields"/>, integer keys (1, 2, 3…) in <see cref="Items"/> in order.
/// Values are string, double, bool, null or another LuaTable.
/// </summary>
public sealed class LuaTable
{
    public Dictionary<string, object?> Fields { get; } = new();
    public SortedDictionary<long, object?> Indexed { get; } = new();

    public IEnumerable<object?> Items => Indexed.Values;

    public object? this[string key] => Fields.TryGetValue(key, out var v) ? v : null;

    public string? Str(string key) => this[key] as string;
    public double? Num(string key) => this[key] is double d ? d : null;
    public long? Long(string key) => this[key] is double d ? (long)Math.Round(d) : null;
    public int? Int(string key) => this[key] is double d ? (int)Math.Round(d) : null;
    public bool? Bool(string key) => this[key] as bool?;
    public LuaTable? Table(string key) => this[key] as LuaTable;
    public IEnumerable<LuaTable> Tables(string key) => Table(key)?.Items.OfType<LuaTable>() ?? Enumerable.Empty<LuaTable>();
}

/// <summary>
/// Reads WoW SavedVariables files: a list of <c>Name = value</c> assignments
/// where values are Lua literals. Not a general Lua parser — it only handles the
/// subset WoW writes (strings, numbers, booleans, nil, nested tables, comments).
/// </summary>
public sealed class LuaSavedVariablesReader
{
    private readonly string _s;
    private int _i;

    private LuaSavedVariablesReader(string text) => _s = text;

    public static Dictionary<string, object?> Parse(string text)
    {
        var r = new LuaSavedVariablesReader(text);
        var globals = new Dictionary<string, object?>();
        while (true)
        {
            r.SkipTrivia();
            if (r.AtEnd) break;
            var name = r.ReadIdentifier();
            r.SkipTrivia();
            r.Expect('=');
            globals[name] = r.ReadValue();
        }
        return globals;
    }

    private bool AtEnd => _i >= _s.Length;
    private char Peek => _i < _s.Length ? _s[_i] : '\0';

    private Exception Error(string what)
    {
        int line = 1;
        for (int k = 0; k < Math.Min(_i, _s.Length); k++) if (_s[k] == '\n') line++;
        return new FormatException($"SavedVariables parse error on line {line}: {what}");
    }

    private void Expect(char c)
    {
        if (Peek != c) throw Error($"expected '{c}' but found '{Peek}'");
        _i++;
    }

    private void SkipTrivia()
    {
        while (!AtEnd)
        {
            char c = Peek;
            if (char.IsWhiteSpace(c) || c == ';') { _i++; continue; }
            if (c == '-' && _i + 1 < _s.Length && _s[_i + 1] == '-')
            {
                _i += 2;
                if (_s.AsSpan(_i).StartsWith("[["))
                {
                    var end = _s.IndexOf("]]", _i + 2, StringComparison.Ordinal);
                    _i = end < 0 ? _s.Length : end + 2;
                }
                else
                {
                    while (!AtEnd && Peek != '\n') _i++;
                }
                continue;
            }
            break;
        }
    }

    private string ReadIdentifier()
    {
        int start = _i;
        while (!AtEnd && (char.IsLetterOrDigit(Peek) || Peek == '_')) _i++;
        if (_i == start) throw Error($"expected a name but found '{Peek}'");
        return _s[start.._i];
    }

    private object? ReadValue()
    {
        SkipTrivia();
        char c = Peek;
        if (c == '{') return ReadTable();
        if (c == '"' || c == '\'') return ReadString();
        if (c == '-' || c == '.' || char.IsDigit(c)) return ReadNumber();
        if (char.IsLetter(c))
        {
            var word = ReadIdentifier();
            return word switch
            {
                "true" => true,
                "false" => false,
                "nil" => null,
                "inf" or "math.huge" => double.PositiveInfinity,
                "nan" => double.NaN,
                _ => throw Error($"unexpected '{word}'"),
            };
        }
        throw Error($"unexpected '{c}'");
    }

    private LuaTable ReadTable()
    {
        Expect('{');
        var t = new LuaTable();
        long nextIndex = 1;
        while (true)
        {
            SkipTrivia();
            if (Peek == '}') { _i++; return t; }

            if (Peek == '[')
            {
                _i++;
                var key = ReadValue();
                SkipTrivia();
                Expect(']');
                SkipTrivia();
                Expect('=');
                var value = ReadValue();
                switch (key)
                {
                    case string sk: t.Fields[sk] = value; break;
                    case double dk when dk == Math.Floor(dk): t.Indexed[(long)dk] = value; break;
                    case bool bk: t.Fields[bk ? "true" : "false"] = value; break;
                    default: t.Fields[Convert.ToString(key, CultureInfo.InvariantCulture) ?? ""] = value; break;
                }
            }
            else if (char.IsLetter(Peek) || Peek == '_')
            {
                // Either "name = value" or a bare word value (true/false/nil).
                int save = _i;
                var word = ReadIdentifier();
                SkipTrivia();
                if (Peek == '=' && (_i + 1 >= _s.Length || _s[_i + 1] != '='))
                {
                    _i++;
                    t.Fields[word] = ReadValue();
                }
                else
                {
                    _i = save;
                    t.Indexed[nextIndex++] = ReadValue();
                }
            }
            else
            {
                t.Indexed[nextIndex++] = ReadValue();
            }

            SkipTrivia();
            if (Peek == ',') _i++;
            else if (Peek != '}') throw Error($"expected ',' or '}}' but found '{Peek}'");
        }
    }

    private string ReadString()
    {
        char quote = Peek;
        _i++;
        // Collect UTF-8 bytes so \ddd byte escapes of multi-byte characters decode correctly.
        var bytes = new List<byte>();
        var one = new char[1];
        var buf = new byte[4];
        void Add(char ch)
        {
            one[0] = ch;
            int n = Encoding.UTF8.GetBytes(one, 0, 1, buf, 0);
            for (int k = 0; k < n; k++) bytes.Add(buf[k]);
        }

        while (true)
        {
            if (AtEnd) throw Error("unterminated string");
            char c = _s[_i++];
            if (c == quote) return Encoding.UTF8.GetString(bytes.ToArray());
            if (c != '\\')
            {
                if (char.IsHighSurrogate(c) && !AtEnd)
                {
                    var pair = new string(new[] { c, _s[_i++] });
                    bytes.AddRange(Encoding.UTF8.GetBytes(pair));
                }
                else Add(c);
                continue;
            }

            if (AtEnd) throw Error("unterminated string");
            char e = _s[_i++];
            switch (e)
            {
                case 'n': Add('\n'); break;
                case 'r': Add('\r'); break;
                case 't': Add('\t'); break;
                case '\n': Add('\n'); break;
                case 'a': Add('\a'); break;
                case 'b': Add('\b'); break;
                case 'f': Add('\f'); break;
                case 'v': Add('\v'); break;
                default:
                    if (char.IsDigit(e))
                    {
                        int code = e - '0';
                        for (int k = 0; k < 2 && !AtEnd && char.IsDigit(Peek); k++) code = code * 10 + (_s[_i++] - '0');
                        bytes.Add((byte)code);
                    }
                    else Add(e); // \\ \" \' and anything else map to themselves
                    break;
            }
        }
    }

    private double ReadNumber()
    {
        int start = _i;
        if (Peek == '-') _i++;
        if (_s.AsSpan(_i).StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            _i += 2;
            int hs = _i;
            while (!AtEnd && Uri.IsHexDigit(Peek)) _i++;
            var hex = long.Parse(_s[hs.._i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return _s[start] == '-' ? -hex : hex;
        }
        while (!AtEnd && (char.IsDigit(Peek) || Peek is '.' or 'e' or 'E'
                          || ((Peek is '+' or '-') && (_s[_i - 1] is 'e' or 'E')))) _i++;
        var text = _s[start.._i];
        if (text == "-" && (_s.AsSpan(_i).StartsWith("inf") || _s.AsSpan(_i).StartsWith("nan")))
        {
            bool inf = _s[_i] == 'i';
            _i += 3;
            return inf ? double.NegativeInfinity : double.NaN;
        }
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            throw Error($"bad number '{text}'");
        return d;
    }
}
