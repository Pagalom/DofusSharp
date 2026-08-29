using BestCrush.Services;

namespace BestCrush.Overlay;

public sealed class CrushSessionOverlayPage
    : ContentPage
{
    private readonly Label _status;
    private readonly Label _scannedCells;
    private readonly VerticalStackLayout _runes;
    private readonly Label _total;

    private readonly CrushSessionService
        _sessionService;

    public CrushSessionOverlayPage(
        CrushSessionService sessionService)
    {
        _sessionService =
            sessionService;

        BackgroundColor =
            Color.FromArgb(
                "#17191C"
            );

        Padding =
            new Thickness(14);

        Label title =
            new()
            {
                Text =
                    "Résultat concassage",

                FontSize = 18,

                FontAttributes =
                    FontAttributes.Bold,

                TextColor =
                    Colors.White,

                VerticalOptions =
                    LayoutOptions.Center
            };

        Button close =
            new()
            {
                Text = "✕",

                FontSize = 16,

                TextColor =
                    Colors.LightGray,

                BackgroundColor =
                    Colors.Transparent,

                Padding =
                    new Thickness(
                        8,
                        2
                    ),

                HorizontalOptions =
                    LayoutOptions.End
            };

        close.Clicked +=
            (_, _) =>
            {
                _sessionService
                    .CloseAndReset();
            };

        Grid header =
            new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(
                        GridLength.Star
                    ),

                    new ColumnDefinition(
                        GridLength.Auto
                    )
                }
            };

        header.Add(
            title,
            0,
            0
        );

        header.Add(
            close,
            1,
            0
        );

        PanGestureRecognizer dragGesture =
            new();

        dragGesture.PanUpdated +=
            (_, e) =>
            {
                switch (e.StatusType)
                {
                    case GestureStatus.Started:
                        _sessionService
                            .BeginDrag();
                        break;

                    case GestureStatus.Running:
                        _sessionService
                            .Drag(
                                e.TotalX,
                                e.TotalY
                            );
                        break;
                }
            };

        header.GestureRecognizers.Add(
            dragGesture
        );

        _status =
            new Label
            {
                TextColor =
                    Colors.LightGreen,

                FontSize = 13
            };

        _scannedCells =
            new Label
            {
                TextColor =
                    Colors.White,

                FontSize = 13
            };

        _runes =
            new VerticalStackLayout
            {
                Spacing = 5
            };

        _total =
            new Label
            {
                Text =
                    "Valeur réelle : —",

                TextColor =
                    Colors.White,

                FontSize = 15,

                FontAttributes =
                    FontAttributes.Bold
            };

        VerticalStackLayout content =
            new()
            {
                Spacing = 10,

                Children =
                {
                    header,

                    _status,

                    _scannedCells,

                    new BoxView
                    {
                        HeightRequest = 1,

                        BackgroundColor =
                            Color.FromArgb(
                                "#555A60"
                            )
                    },

                    _runes,

                    new BoxView
                    {
                        HeightRequest = 1,

                        BackgroundColor =
                            Color.FromArgb(
                                "#555A60"
                            )
                    },

                    _total
                }
            };

        Content =
            content;
    }

    public void Update(
        CrushSessionSnapshot snapshot)
    {
        _status.Text =
            snapshot.IsRunning
                ? "● Acquisition active — survolez les runes"
                : "○ Acquisition arrêtée";

        _status.TextColor =
            snapshot.IsRunning
                ? Colors.LightGreen
                : Colors.Orange;

        string cursorText =
            snapshot.LastCursorX is int x &&
            snapshot.LastCursorY is int y
                ? $"\nDernière capture : X={x}, Y={y}"
                : "";

        _scannedCells.Text =
            $"Cases scannées : " +
            $"{snapshot.ScannedCells}\n" +

            $"Captures sur arrêt souris : " +
            $"{snapshot.IdleCaptures}" +

            cursorText;

        _runes.Children.Clear();

        foreach (
            CrushSessionRuneLine rune
            in snapshot.Runes)
        {
            string value =
                rune.Value is double runeValue
                    ? $"{runeValue:N0} K"
                    : "prix manquant";

            _runes.Children.Add(
                new Label
                {
                    Text =
                        $"{rune.Name} x{rune.Quantity}    {value}",

                    TextColor =
                        Colors.White,

                    FontSize = 13
                }
            );
        }

        _total.Text =
            snapshot.TotalValue
                is double total
                ? $"Valeur réelle : {total:N0} K"
                : "Valeur réelle : —";
    }
}