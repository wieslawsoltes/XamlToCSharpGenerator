using System;

namespace XamlToCSharpGenerator.MiniLanguageParsing.Text;

/// <summary>
/// Represents a minimal single-range replacement patch.
/// </summary>
public readonly struct MinimalTextPatch
{
    /// <summary>
    /// Initializes a new patch.
    /// </summary>
    /// <param name="start">Replacement start offset in the original text.</param>
    /// <param name="removedLength">Number of characters removed from the original text.</param>
    /// <param name="insertedText">Replacement text to insert.</param>
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

    /// <summary>
    /// Gets the replacement start offset in the original text.
    /// </summary>
    public int Start { get; }

    /// <summary>
    /// Gets the number of removed characters in the original text.
    /// </summary>
    public int RemovedLength { get; }

    /// <summary>
    /// Gets the replacement text inserted at <see cref="Start"/>.
    /// </summary>
    public string InsertedText { get; }

    /// <summary>
    /// Gets the number of inserted characters.
    /// </summary>
    public int InsertedLength => InsertedText.Length;

    /// <summary>
    /// Gets a value indicating whether the patch is a no-op.
    /// </summary>
    public bool IsNoOp => RemovedLength == 0 && InsertedText.Length == 0;
}

/// <summary>
/// Computes and applies minimal single-range text replacement patches.
/// </summary>
public static class MinimalTextDiff
{
    /// <summary>
    /// Computes a minimal single-range replacement patch from <paramref name="originalText"/> to <paramref name="updatedText"/>.
    /// </summary>
    /// <param name="originalText">Original text.</param>
    /// <param name="updatedText">Updated text.</param>
    /// <returns>A minimal replacement patch.</returns>
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

    /// <summary>
    /// Applies <paramref name="patch"/> to <paramref name="originalText"/>.
    /// </summary>
    /// <param name="originalText">Original text.</param>
    /// <param name="patch">Patch to apply.</param>
    /// <returns>Patched text.</returns>
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
            originalText
                .AsSpan(suffixStart, suffixLength)
                .CopyTo(destination.Slice(suffixDestinationStart));
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

    private static int ComputeCommonSuffixLength(
        ReadOnlySpan<char> left,
        ReadOnlySpan<char> right,
        int commonPrefixLength)
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
