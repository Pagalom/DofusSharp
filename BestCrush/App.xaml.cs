using BestCrush.Services;
using Microsoft.Maui.ApplicationModel;

namespace BestCrush;

public partial class App : Application
{
    private readonly OverlayService _overlayService;

    public App(OverlayService overlayService)
    {
        InitializeComponent();

        _overlayService = overlayService;
    }

    protected override Window CreateWindow(
        IActivationState? activationState)
    {
        Window mainWindow = new(new MainPage())
        {
            Title =
                $"Best Crush v{CurrentVersion.Version.WithoutMetadata()}"
        };

        mainWindow.Created += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(
                _overlayService.Initialize
            );
        };

        mainWindow.Destroying += (_, _) =>
        {
            _overlayService.Shutdown();
        };

        return mainWindow;
    }
}