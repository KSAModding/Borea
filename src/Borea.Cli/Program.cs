using System.Text;
using Borea.Composition;

namespace Borea.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var output = Console.IsOutputRedirected ? Utf8Writer(Console.OpenStandardOutput()) : null;
        using var error = Console.IsErrorRedirected ? Utf8Writer(Console.OpenStandardError()) : null;

        return await BoreaCli.RunAsync(args, BuildServicesAsync, output ?? Console.Out, error ?? Console.Error).ConfigureAwait(false);
    }

    private static async Task<CliServices> BuildServicesAsync(CancellationToken cancellationToken)
        => CliServices.From(await BoreaServices.BuildAsync(cancellationToken).ConfigureAwait(false));

    private static StreamWriter Utf8Writer(Stream stream)
        => new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
}
