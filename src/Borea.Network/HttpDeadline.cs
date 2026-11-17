namespace Borea.Network;

internal static class HttpDeadline
{
    public static async Task<T> RunAsync<T>(TimeSpan timeout, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            return await operation(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException($"The HTTP request did not complete within {timeout}.", exception);
        }
    }
}
