namespace FullTime.App;

using FullTime.App.Services;

public partial class App : Application
{
    private readonly MauiAppLifecycle _lifecycle;

    public App(MauiAppLifecycle lifecycle, MauiInstallReporter installReporter)
    {
        _lifecycle = lifecycle;
        InitializeComponent();
        _ = installReporter.ReportIfFirstLaunchAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "FullTime.App" };
        window.Resumed += (_, _) => _lifecycle.RaiseResumed();
        return window;
    }
}
