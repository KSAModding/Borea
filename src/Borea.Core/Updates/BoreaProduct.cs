namespace Borea.Core.Updates;

/// <summary>Which of the two programs a release archive holds.</summary>
public enum BoreaProduct
{
    /// <summary>The desktop App, whose program runs the commands as well.</summary>
    App = 0,

    /// <summary>The command line alone.</summary>
    Cli = 1,
}
