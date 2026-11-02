using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Borea.App.ViewModels;

/// <summary>
/// The Discover rows that want a drawn still while they are near the window. It draws one vehicle at a time, because
/// one draw holds the meshes and textures of a whole vehicle in memory, and it takes the newest row in view first, then
/// the newest row near the view. A row that left is dropped, and its draw stops. Every call comes on the UI thread.
/// </summary>
internal sealed class PreviewStillQueue(Func<DiscoverItem, CancellationToken, Task> draw)
{
    private readonly Dictionary<DiscoverItem, bool> _near = [];
    private readonly List<DiscoverItem> _waiting = [];
    private DiscoverItem? _running;
    private CancellationTokenSource? _runningStop;
    private Task? _pump;

    /// <summary>Completes when the queue draws nothing and nothing waits, so a test can wait for it.</summary>
    internal Task Idle => _pump ?? Task.CompletedTask;

    /// <summary>Says that the row is near the window, and whether it is in the window. Only a change moves it to the front.</summary>
    public void Show(DiscoverItem row, bool inView)
    {
        if (_near.TryGetValue(row, out var wasInView) && wasInView == inView)
            return;

        _near[row] = inView;
        _waiting.Remove(row);
        _waiting.Add(row);
        Start();
    }

    /// <summary>Drops a row that left the window, and stops its draw when it runs now.</summary>
    public void Hide(DiscoverItem row)
    {
        _near.Remove(row);
        _waiting.Remove(row);
        if (ReferenceEquals(row, _running))
            _runningStop?.Cancel();
    }

    /// <summary>Lets every row near the window ask again, after something changed that can give it another still.</summary>
    public void Refresh()
    {
        foreach (var row in _near.Keys)
        {
            if (!_waiting.Contains(row))
                _waiting.Add(row);
        }

        Start();
    }

    /// <summary>
    /// Stops the draw that runs and forgets what waits, so no draw starts by itself, and gives the task that completes
    /// when the draw closed its files. The rows ask again with the next <see cref="Refresh"/> or when they scroll.
    /// </summary>
    public Task Stop()
    {
        _waiting.Clear();
        if (_running is null)
            return Task.CompletedTask;

        _runningStop?.Cancel();
        return Idle;
    }

    private void Start()
    {
        if (_pump is null && _waiting.Count > 0)
            _pump = PumpAsync();
    }

    private async Task PumpAsync()
    {
        // The rows of one layout pass all ask first, so the choice below sees them all and the layout pass reads no file.
        await Task.Yield();
        try
        {
            while (Next() is { } row)
            {
                using var stop = new CancellationTokenSource();
                _running = row;
                _runningStop = stop;
                try
                {
                    await draw(row, stop.Token);
                }
                finally
                {
                    _running = null;
                    _runningStop = null;
                }
            }
        }
        finally
        {
            _pump = null;
        }
    }

    private DiscoverItem? Next()
    {
        if (_waiting.Count == 0)
            return null;

        var index = _waiting.FindLastIndex(row => _near.GetValueOrDefault(row));
        var next = _waiting[index >= 0 ? index : _waiting.Count - 1];
        _waiting.Remove(next);
        return next;
    }
}
