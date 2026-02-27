using System;

namespace XamlToCSharpGenerator.Runtime;

public sealed class SourceGenHotReloadHandshakeResult
{
    public SourceGenHotReloadHandshakeResult(
        bool isSuccess,
        bool isPending,
        string message,
        Exception? exception = null)
    {
        if (isPending && !isSuccess)
        {
            throw new ArgumentException("A pending handshake result must be successful.", nameof(isPending));
        }

        IsSuccess = isSuccess;
        IsPending = isPending;
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Exception = exception;
    }

    public bool IsSuccess { get; }

    public bool IsPending { get; }

    public string Message { get; }

    public Exception? Exception { get; }

    public static SourceGenHotReloadHandshakeResult Success(string message, bool isPending = false)
    {
        return new SourceGenHotReloadHandshakeResult(isSuccess: true, isPending: isPending, message);
    }

    public static SourceGenHotReloadHandshakeResult Failure(string message, Exception? exception = null)
    {
        return new SourceGenHotReloadHandshakeResult(isSuccess: false, isPending: false, message, exception);
    }
}
