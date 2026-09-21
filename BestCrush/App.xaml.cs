using BestCrush.Services;
using Microsoft.Maui.ApplicationModel;

namespace BestCrush;

public partial class App : Application
{
    private readonly OverlayService _overlayService;
    private readonly DofusNetworkCaptureService
        _networkCaptureService;

    public App(
        OverlayService overlayService,
        DofusNetworkCaptureService networkCaptureService)
    {
        InitializeComponent();

        _overlayService = overlayService;
        _networkCaptureService = networkCaptureService;
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
            _networkCaptureService.Start();

            MainThread.BeginInvokeOnMainThread(
                _overlayService.Initialize
            );
        };

        mainWindow.Destroying += (_, _) =>
        {
            _networkCaptureService.Stop();
            _overlayService.Shutdown();
        };

        return mainWindow;
    }
}