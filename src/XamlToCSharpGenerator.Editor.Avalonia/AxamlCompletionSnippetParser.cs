using System.Text;

namespace XamlToCSharpGenerator.Editor.Avalonia;

internal static class AxamlCompletionSnippetParser
{
    public static AxamlCompletionSnippetExpansion Expand(string? snippet)
    {
        if (string.IsNullOrEmpty(snippet))
        {
            return new AxamlCompletionSnippetExpansion(string.Empty, 0);
        }

        var builder = new StringBuilder(snippet.Length);
        var primaryCaretOffset = -1;
        var fallbackCaretOffset = -1;

        for (var index = 0; index < snippet.Length; index++)
        {
            var current = snippet[index];

            if (current == '\\' && index + 1 < snippet.Length)
            {
                var escaped = snippet[index + 1];
                if (escaped is '\\' or '$' or '}')
                {
                    builder.Append(escaped);
                    index++;
                    continue;
                }
            }

            if (current != '$' || index + 1 >= snippet.Length)
            {
                builder.Append(current);
                continue;
            }

            var next = snippet[index + 1];
            if (char.IsDigit(next))
            {
                var barePlaceholderIndex = 0;
                index++;
                while (index < snippet.Length && char.IsDigit(snippet[index]))
                {
                    barePlaceholderIndex = (barePlaceholderIndex * 10) + (snippet[index] - '0');
                    if (index + 1 >= snippet.Length || !char.IsDigit(snippet[index + 1]))
                    {
                        break;
                    }

                    index++;
                }

                RegisterCaret(barePlaceholderIndex, builder.Length, ref primaryCaretOffset, ref fallbackCaretOffset);
                continue;
            }

            if (next != '{')
            {
                builder.Append(current);
                continue;
            }

            var cursor = index + 2;
            if (!TryParsePlaceholderIndex(snippet, ref cursor, out var placeholderIndex))
            {
                builder.Append(current);
                continue;
            }

            if (cursor < snippet.Length && snippet[cursor] == '}')
            {
                RegisterCaret(placeholderIndex, builder.Length, ref primaryCaretOffset, ref fallbackCaretOffset);
                index = cursor;
                continue;
            }

            if (cursor < snippet.Length && snippet[cursor] == ':')
            {
                cursor++;
                var placeholderText = ParsePlaceholderText(snippet, ref cursor);
                RegisterCaret(placeholderIndex, builder.Length, ref primaryCaretOffset, ref fallbackCaretOffset);
                builder.Append(placeholderText);
                index = cursor;
                continue;
            }

            builder.Append(current);
        }

        var caretOffset = primaryCaretOffset >= 0
            ? primaryCaretOffset
            : fallbackCaretOffset >= 0
                ? fallbackCaretOffset
                : builder.Length;

        return new AxamlCompletionSnippetExpansion(builder.ToString(), caretOffset);
    }

    private static bool TryParsePlaceholderIndex(string snippet, ref int cursor, out int placeholderIndex)
    {
        placeholderIndex = 0;
        if (cursor >= snippet.Length || !char.IsDigit(snippet[cursor]))
        {
            return false;
        }

        while (cursor < snippet.Length && char.IsDigit(snippet[cursor]))
        {
            placeholderIndex = (placeholderIndex * 10) + (snippet[cursor] - '0');
            cursor++;
        }

        return true;
    }

    private static string ParsePlaceholderText(string snippet, ref int cursor)
    {
        var builder = new StringBuilder();

        while (cursor < snippet.Length)
        {
            var current = snippet[cursor];
            if (current == '\\' && cursor + 1 < snippet.Length)
            {
                var escaped = snippet[cursor + 1];
                if (escaped is '\\' or '$' or '}')
                {
                    builder.Append(escaped);
                    cursor += 2;
                    continue;
                }
            }

            if (current == '}')
            {
                return builder.ToString();
            }

            builder.Append(current);
            cursor++;
        }

        return builder.ToString();
    }

    private static void RegisterCaret(
        int placeholderIndex,
        int outputOffset,
        ref int primaryCaretOffset,
        ref int fallbackCaretOffset)
    {
        if (placeholderIndex == 0)
        {
            if (primaryCaretOffset < 0)
            {
                primaryCaretOffset = outputOffset;
            }

            return;
        }

        if (fallbackCaretOffset < 0)
        {
            fallbackCaretOffset = outputOffset;
        }
    }
}

internal readonly record struct AxamlCompletionSnippetExpansion(string Text, int CaretOffset);
