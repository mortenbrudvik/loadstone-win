using System.Windows;
using System.Windows.Threading;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

public class SmokeTests
{
    [WpfFact]
    public void WpfFact_RunsOnStaThread_WithDispatcher()
    {
        Thread.CurrentThread.GetApartmentState().ShouldBe(ApartmentState.STA);
        Dispatcher.CurrentDispatcher.ShouldNotBeNull();
        var window = new Window { Width = 100, Height = 100, ShowActivated = false, ShowInTaskbar = false };
        window.Show();
        window.IsVisible.ShouldBeTrue();
        window.Close();
    }
}
