using System;

namespace XamlToCSharpGenerator.Runtime;

internal readonly struct MinimalTextPatch
{
    public MinimalTextPatch(int start, int removedLength, string insertedText)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if (removedLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(removedLength));
        }

        Start = start;
        RemovedLength = removedLength;
        InsertedText = insertedText ?? string.Empty;
    }

    public int Start { get; }

    public int RemovedLength { get; }

    public string InsertedText { get; }

    public int InsertedLength => InsertedText.Length;

    public bool IsNoOp => RemovedLength == 0 && InsertedText.Length == 0;
}

internal static class MinimalTextDiff
{
    public static MinimalTextPatch CreatePatch(string originalText, string updatedText)
    {
        if (originalText is null)
        {
            throw new ArgumentNullException(nameof(originalText));
        }

        if (updatedText is null)
        {
            throw new ArgumentNullException(nameof(updatedText));
        }

        var original = originalText.AsSpan();
        var updated = updatedText.AsSpan();

        var prefixLength = ComputeCommonPrefixLength(original, updated);
        if (prefixLength == original.Length && prefixLength == updated.Length)
        {
            return new MinimalTextPatch(prefixLength, 0, string.Empty);
        }

        var suffixLength = ComputeCommonSuffixLength(original, updated, prefixLength);
        var removedLength = original.Length - prefixLength - suffixLength;
        var insertedLength = updated.Length - prefixLength - suffixLength;
        var insertedText = insertedLength > 0
            ? updatedText.Substring(prefixLength, insertedLength)
            : string.Empty;

        return new MinimalTextPatch(prefixLength, removedLength, insertedText);
    }

    public static string ApplyPatch(string originalText, MinimalTextPatch patch)
    {
        if (originalText is null)
        {
            throw new ArgumentNullException(nameof(originalText));
        }

        if (patch.Start > originalText.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(patch));
        }

        if (patch.Start + patch.RemovedLength > originalText.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(patch));
        }

        if (patch.IsNoOp)
        {
            return originalText;
        }

        var prefixLength = patch.Start;
        var suffixStart = patch.Start + patch.RemovedLength;
        var suffixLength = originalText.Length - suffixStart;
        var resultLength = prefixLength + patch.InsertedLength + suffixLength;

        var buffer = new char[resultLength];
        var destination = buffer.AsSpan();

        if (prefixLength > 0)
        {
            originalText.AsSpan(0, prefixLength).CopyTo(destination);
        }

        if (patch.InsertedLength > 0)
        {
            patch.InsertedText.AsSpan().CopyTo(destination.Slice(prefixLength));
        }

        if (suffixLength > 0)
        {
            var suffixDestinationStart = prefixLength + patch.InsertedLength;
            originalText.AsSpan(suffixStart, suffixLength).CopyTo(destination.Slice(suffixDestinationStart));
        }

        return new string(buffer);
    }

    private static int ComputeCommonPrefixLength(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        var limit = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < limit && left[index] == right[index])
        {
            index++;
        }

        return index;
    }

    private static int ComputeCommonSuffixLength(ReadOnlySpan<char> left, ReadOnlySpan<char> right, int commonPrefixLength)
    {
        var leftIndex = left.Length - 1;
        var rightIndex = right.Length - 1;
        var suffixLength = 0;

        while (leftIndex >= commonPrefixLength &&
               rightIndex >= commonPrefixLength &&
               left[leftIndex] == right[rightIndex])
        {
            suffixLength++;
            leftIndex--;
            rightIndex--;
        }

        return suffixLength;
    }
}
