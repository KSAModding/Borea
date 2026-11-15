using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Borea.App.Tests.ViewModels;
using Borea.App.Views;

namespace Borea.App.Tests.Views;

/// <summary>
/// The destructive item of a menu comes last behind its own separator and wears the danger class, and
/// a disabled one does not look clickable.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class MenuConventionTests
{
    [Fact]
    public async Task InstanceMenu_EndsWithASeparatorAndTheDangerDelete()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        var (lastIsDanger, separatorBefore) = await HeadlessApp.RunAsync(harness, () =>
        {
            var menu = new InstanceMenu();
            var items = ((MenuFlyout)((Button)menu.Content!).Flyout!).Items.ToList();
            var last = Assert.IsType<MenuItem>(items[^1]);
            return Task.FromResult((last.Classes.Contains("danger"), items[^2] is Separator));
        });

        Assert.True(lastIsDanger);
        Assert.True(separatorBefore);
    }

    [Fact]
    public async Task DisabledDangerItem_IsDimmed()
    {
        using var harness = await ViewModelHarness.CreateAsync();

        var opacity = await HeadlessApp.RunAsync(harness, () =>
        {
            var item = new MenuItem { Header = "Remove", IsEnabled = false };
            item.Classes.Add("danger");
            var window = new Window { Width = 300, Height = 200, Content = new Menu { Items = { item } } };
            window.Show();
            window.UpdateLayout();
            var header = item.GetVisualDescendants().OfType<ContentPresenter>().Single(presenter => presenter.Name == "PART_HeaderPresenter");
            var value = header.Opacity;
            window.Close();
            return Task.FromResult(value);
        });

        Assert.True(opacity < 1);
    }
}
