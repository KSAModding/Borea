using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Borea.Core.Instances;

namespace Borea.App.ViewModels;

/// <summary>The vehicles and saves Borea installed in an instance (RFC 0085).</summary>
public partial class MainViewModel
{
    /// <summary>The records of an instance. A record file Borea cannot read gives none, so the instance still opens.</summary>
    private async Task<IReadOnlyList<InstalledContentRecord>> InstalledContentOfAsync(Guid instanceId)
    {
        if (_services is null)
            return [];

        try
        {
            return await _services.InstalledContent.ListAsync(instanceId);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
