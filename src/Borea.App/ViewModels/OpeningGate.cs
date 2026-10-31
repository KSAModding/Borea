using System;
using System.Threading.Tasks;

namespace Borea.App.ViewModels;

/// <summary>Drops a second open of the same row while the page that the first one opens still loads.</summary>
internal sealed class OpeningGate
{
    private bool _isOpening;

    internal async Task RunAsync(Func<Task> open)
    {
        if (_isOpening)
            return;

        _isOpening = true;
        try
        {
            await open();
        }
        finally
        {
            _isOpening = false;
        }
    }
}
