using System;
using System.Collections.Generic;

namespace Borea.App.SingleInstance;

internal sealed record StartArguments(IReadOnlyList<string> Arguments, bool Forwarded);

/// <summary>Holds the starts that arrive before the main window opens, and then hands each on in order.</summary>
internal sealed class StartArgumentsInbox
{
    /// <summary>More starts than this before the window opens are rejected, because nobody clicks that fast.</summary>
    public const int MaxPending = 16;

    private readonly object _gate = new();
    private readonly Queue<StartArguments> _pending = new();
    private Action<StartArguments>? _receiver;

    /// <returns>False when the start was rejected because too many wait.</returns>
    public bool Post(StartArguments start)
    {
        ArgumentNullException.ThrowIfNull(start);
        lock (_gate)
        {
            if (_receiver is not null)
                _receiver(start);
            else if (_pending.Count < MaxPending)
                _pending.Enqueue(start);
            else
                return false;

            return true;
        }
    }

    /// <param name="receiver">Runs under the inbox lock on the posting thread, so it only queues the work.</param>
    public void Open(Action<StartArguments> receiver)
    {
        ArgumentNullException.ThrowIfNull(receiver);
        lock (_gate)
        {
            if (_receiver is not null)
                throw new InvalidOperationException("The inbox is already open.");

            _receiver = receiver;
            while (_pending.TryDequeue(out var start))
                receiver(start);
        }
    }
}
