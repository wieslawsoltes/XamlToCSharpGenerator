using System;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Text;

public ref struct MiniLanguageCharacterReader
{
    private readonly ReadOnlySpan<char> _source;
    private int _index;

    public MiniLanguageCharacterReader(ReadOnlySpan<char> source)
    {
        _source = source;
        _index = 0;
    }

    public int Position => _index;

    public bool End => _index >= _source.Length;

    public char Peek => End ? '\0' : _source[_index];

    public ReadOnlySpan<char> Remaining => End ? ReadOnlySpan<char>.Empty : _source[_index..];

    public bool TryTake(char token)
    {
        if (!End && _source[_index] == token)
        {
            _index++;
            return true;
        }

        return false;
    }

    public bool TryTakeOrdinalIgnoreCase(string token)
    {
        if (token is null)
        {
            return false;
        }

        if (_index + token.Length > _source.Length)
        {
            return false;
        }

        for (var i = 0; i < token.Length; i++)
        {
            var left = _source[_index + i];
            var right = token[i];
            if (char.ToUpperInvariant(left) != char.ToUpperInvariant(right))
            {
                return false;
            }
        }

        _index += token.Length;
        return true;
    }

    public int SkipWhitespace()
    {
        var start = _index;
        while (!End && char.IsWhiteSpace(_source[_index]))
        {
            _index++;
        }

        return _index - start;
    }

    public void Advance(int count)
    {
        if (count < 0 || _index + count > _source.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        _index += count;
    }
}
