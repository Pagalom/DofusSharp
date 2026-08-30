using BestCrush.Services;

namespace BestCrush.Overlay;

public sealed class OverlayControlBarPage : ContentPage
{
    private readonly OverlayControlBarService _service;
    private readonly Button _profitability;
    private readonly Button _market;
    private readonly Button _crush;

    public OverlayControlBarPage(
        OverlayControlBarService service)
    {
        _service = service;

        BackgroundColor = Color.FromArgb("#17191C");
        Padding = new Thickness(6);

        Label grip = new()
        {
            Text = "⋮",
            TextColor = Colors.Gray,
            FontSize = 18,
            WidthRequest = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        PanGestureRecognizer drag = new();
        drag.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _service.BeginDrag();
                    break;

                case GestureStatus.Running:
                    _service.Drag(
                        e.TotalX,
                        e.TotalY
                    );
                    break;
            }
        };
        grip.GestureRecognizers.Add(drag);

        _profitability = CreateButton(
            "◈",
            "Rentabilité"
        );
        _profitability.Clicked +=
            (_, _) =>
                _service.ToggleProfitability();

        _market = CreateButton(
            "↻",
            "Mise à jour marché"
        );
        _market.Clicked +=
            (_, _) =>
                _service.ToggleMarket();

        _crush = CreateButton(
            "⚒",
            "Résultat concassage"
        );
        _crush.Clicked +=
            (_, _) =>
                _service.ToggleCrush();

        Button settings = CreateButton(
            "⚙",
            "Paramètres"
        );
        settings.Clicked +=
            (_, _) =>
                _service.OpenSettings();

        HorizontalStackLayout row = new()
        {
            Spacing = 4,
            Children =
            {
                grip,
                _profitability,
                _market,
                _crush,
                settings
            }
        };

        Content = row;

        Loaded += (_, _) =>
        {
            RefreshState();

            Dispatcher.StartTimer(
                TimeSpan.FromMilliseconds(250),
                () =>
                {
                    RefreshState();
                    return true;
                }
            );
        };
    }

    public void RefreshState()
    {
        SetButtonState(
            _profitability,
            _service.IsProfitabilityVisible
        );

        SetButtonState(
            _market,
            _service.IsMarketVisible
        );

        SetButtonState(
            _crush,
            _service.IsCrushVisible
        );
    }

    private static Button CreateButton(
        string icon,
        string semanticDescription)
    {
        Button button = new()
        {
            Text = icon,
            FontSize = 18,
            TextColor = Colors.White,
            WidthRequest = 38,
            HeightRequest = 36,
            Padding = 0,
            CornerRadius = 7,
            BackgroundColor = Color.FromArgb("#2A2D31")
        };

        SemanticProperties.SetDescription(
            button,
            semanticDescription
        );

        return button;
    }

    private static void SetButtonState(
        Button button,
        bool active)
    {
        button.BackgroundColor =
            active
                ? Color.FromArgb("#2D6A4F")
                : Color.FromArgb("#2A2D31");

        button.Opacity =
            active
                ? 1.0
                : 0.68;
    }
}
