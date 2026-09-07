using BestCrush.Services;
using System.Globalization;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Layouts;

namespace BestCrush.Overlay;

public sealed class CrushSessionOverlayPage
    : ContentPage
{
    private readonly Label _status;
    private readonly Label _scannedCells;
    private readonly VerticalStackLayout _runes;
    private readonly Label _total;
    private readonly Label _copyFeedback;

    private double? _lastTotalValue;
    private int _copyFeedbackVersion;
    private int _formulaTapVersion;
    private DateTime _lastFormulaDoubleTapUtc =
        DateTime.MinValue;

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

        Padding = 0;

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

        Label dragHint =
            new()
            {
                Text = "⋮⋮",
                FontSize = 16,
                TextColor = Colors.Gray,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End
            };

        Grid header =
            new()
            {
                BackgroundColor =
                    Color.FromArgb(
                        "#1B1E22"
                    ),

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
            dragHint,
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

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        _sessionService
                            .EndDrag();
                        break;
                }
            };

        header.GestureRecognizers.Add(
            dragGesture
        );

        PointerGestureRecognizer dragPointer =
            new();

        dragPointer.PointerEntered +=
            (_, _) =>
            {
                header.BackgroundColor =
                    Color.FromArgb(
                        "#22262A"
                    );
            };

        dragPointer.PointerExited +=
            (_, _) =>
            {
                header.BackgroundColor =
                    Color.FromArgb(
                        "#1B1E22"
                    );
            };

        header.GestureRecognizers.Add(
            dragPointer
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
                    FontAttributes.Bold,

                TextDecorations =
                    TextDecorations.Underline
            };

        _copyFeedback =
            new Label
            {
                Text = string.Empty,
                TextColor = Colors.LightGreen,
                FontSize = 12
            };

        MakeCopyable(
            _total,
            () =>
                _lastTotalValue is double total
                    ? FormatClipboardNumber(
                        total
                    )
                    : null
        );

        VerticalStackLayout content =
            new()
            {
                Spacing = 10,

                Margin =
                    new Thickness(14),

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

                    _total,

                    _copyFeedback
                }
            };

        Grid resizeContainer =
            CreateResizeContainer(
                content
            );

        Content =
            resizeContainer;
    }

    private Grid CreateResizeContainer(
        View content)
    {
        Grid container =
            new()
            {
                RowDefinitions =
                {
                    new RowDefinition(10),
                    new RowDefinition(
                        GridLength.Star
                    ),
                    new RowDefinition(10)
                },

                ColumnDefinitions =
                {
                    new ColumnDefinition(10),
                    new ColumnDefinition(
                        GridLength.Star
                    ),
                    new ColumnDefinition(10)
                }
            };

        Grid.SetRow(
            content,
            0
        );

        Grid.SetColumn(
            content,
            0
        );

        Grid.SetRowSpan(
            content,
            3
        );

        Grid.SetColumnSpan(
            content,
            3
        );

        container.Children.Add(
            content
        );

        AddResizeZone(
            container,
            0,
            1,
            OverlayResizeEdge.Top
        );

        AddResizeZone(
            container,
            2,
            1,
            OverlayResizeEdge.Bottom
        );

        AddResizeZone(
            container,
            1,
            0,
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            1,
            2,
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            container,
            0,
            0,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            0,
            2,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            container,
            2,
            0,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            2,
            2,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Right
        );

        return container;
    }

    private void AddResizeZone(
        Grid container,
        int row,
        int column,
        OverlayResizeEdge edge)
    {
        BoxView resizeZone =
            new()
            {
                BackgroundColor =
                    Color.FromArgb(
                        "#1D2024"
                    )
            };

        PanGestureRecognizer gesture =
            new();

        gesture.PanUpdated +=
            (_, e) =>
            {
                switch (
                    e.StatusType)
                {
                    case GestureStatus.Started:
                        _sessionService
                            .BeginResize(
                                edge
                            );
                        break;

                    case GestureStatus.Running:
                        _sessionService
                            .Resize(
                                e.TotalX,
                                e.TotalY
                            );
                        break;

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        _sessionService
                            .EndResize();
                        break;
                }
            };

        resizeZone
            .GestureRecognizers
            .Add(
                gesture
            );


        PointerGestureRecognizer pointer =
            new();

        pointer.PointerEntered +=
            (_, _) =>
            {
                resizeZone.BackgroundColor =
                    Color.FromArgb(
                        "#2A2E33"
                    );
            };

        pointer.PointerExited +=
            (_, _) =>
            {
                resizeZone.BackgroundColor =
                    Color.FromArgb(
                        "#1D2024"
                    );
            };

        resizeZone.GestureRecognizers.Add(
            pointer
        );

        Grid.SetRow(
            resizeZone,
            row
        );

        Grid.SetColumn(
            resizeZone,
            column
        );

        container.Children.Add(
            resizeZone
        );
    }

    public void Update(
        CrushSessionSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(
            snapshot.ErrorMessage))
        {
            _status.Text =
                snapshot.ErrorMessage;

            _status.TextColor =
                Colors.Red;
        }
        else
        {
            _status.Text =
                snapshot.IsRunning
                    ? "● Acquisition active — survolez les runes"
                    : "○ Acquisition arrêtée";

            _status.TextColor =
                snapshot.IsRunning
                    ? Colors.LightGreen
                    : Colors.Orange;
        }

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
            Grid row =
                new()
                {
                    RowDefinitions =
                    {
                        new RowDefinition(
                            GridLength.Auto
                        ),
                        new RowDefinition(
                            GridLength.Auto
                        )
                    },

                    ColumnDefinitions =
                    {
                        new ColumnDefinition(
                            GridLength.Star
                        ),
                        new ColumnDefinition(
                            GridLength.Auto
                        )
                    },

                    RowSpacing = 2,
                    ColumnSpacing = 10
                };

            HorizontalStackLayout identity =
                new()
                {
                    Spacing = 0
                };

            Label nameLabel =
                new()
                {
                    Text = rune.Name,
                    TextColor = Colors.White,
                    FontSize = 13
                };

            Label quantityPrefix =
                new()
                {
                    Text = " x",
                    TextColor = Colors.White,
                    FontSize = 13
                };

            Label quantityLabel =
                new()
                {
                    Text =
                        rune.Quantity.ToString(
                            CultureInfo.InvariantCulture
                        ),
                    TextColor = Colors.White,
                    FontSize = 13,
                    TextDecorations =
                        TextDecorations.Underline
                };

            string runeName =
                rune.Name;

            MakeCopyable(
                nameLabel,
                () => runeName
            );

            MakeCopyable(
                quantityLabel,
                () =>
                    rune.Quantity.ToString(
                        CultureInfo.InvariantCulture
                    )
            );

            identity.Children.Add(
                nameLabel
            );

            identity.Children.Add(
                quantityPrefix
            );

            identity.Children.Add(
                quantityLabel
            );

            Label valueLabel =
                new()
                {
                    Text =
                        rune.Value is double runeValue
                            ? $"{runeValue:N0} K"
                            : "prix manquant",

                    TextColor =
                        rune.Value is null
                            ? Colors.Red
                            : Colors.White,

                    FontSize = 13,

                    FontAttributes =
                        FontAttributes.Bold,

                    HorizontalTextAlignment =
                        TextAlignment.End
                };

            if (rune.Value is double copyRuneValue)
            {
                MakeCopyable(
                    valueLabel,
                    () =>
                        FormatClipboardNumber(
                            copyRuneValue
                        )
                );
            }

            row.Add(
                identity,
                0,
                0
            );

            row.Add(
                valueLabel,
                1,
                0
            );

            if (rune.Lots.Count > 0)
            {
                FlexLayout formulaLayout =
                    new()
                    {
                        Direction =
                            FlexDirection.Row,

                        Wrap =
                            FlexWrap.Wrap,

                        AlignItems =
                            FlexAlignItems.Center,

                        Margin =
                            new Thickness(
                                0,
                                1,
                                0,
                                1
                            )
                    };

                string fullFormula =
                    BuildExcelFormula(
                        rune.Lots
                    );

                formulaLayout.Children.Add(
                    new Label
                    {
                        Text = "(",
                        TextColor =
                            Colors.LightGray,
                        FontSize = 11
                    }
                );

                for (
                    int index = 0;
                    index < rune.Lots.Count;
                    index++)
                {
                    CrushSessionRuneLotLine lot =
                        rune.Lots[index];

                    if (index > 0)
                    {
                        formulaLayout.Children.Add(
                            new Label
                            {
                                Text = " + ",
                                TextColor =
                                    Colors.LightGray,
                                FontSize = 11
                            }
                        );
                    }

                    Label term =
                        new()
                        {
                            FormattedText =
                                BuildLotTermFormattedString(
                                    lot
                                ),

                            FontSize = 11,

                            TextDecorations =
                                TextDecorations.Underline
                        };

                    string termFormula =
                        "=" +
                        BuildExcelTerm(
                            lot
                        );

                    MakeFormulaTermCopyable(
                        term,
                        termFormula,
                        fullFormula
                    );

                    formulaLayout.Children.Add(
                        term
                    );
                }

                formulaLayout.Children.Add(
                    new Label
                    {
                        Text = ")",
                        TextColor =
                            Colors.LightGray,
                        FontSize = 11
                    }
                );

                MakeDoubleCopyable(
                    formulaLayout,
                    fullFormula
                );

                Grid.SetColumnSpan(
                    formulaLayout,
                    2
                );

                row.Add(
                    formulaLayout,
                    0,
                    1
                );
            }

            _runes.Children.Add(
                row
            );
        }

        _lastTotalValue =
            snapshot.TotalValue;

        _copyFeedbackVersion++;

        _copyFeedback.Text =
            string.Empty;

        RefreshTotalLabel();
    }

    private static string FormatCompactPrice(
        long value)
    {
        CultureInfo french =
            CultureInfo.GetCultureInfo(
                "fr-FR"
            );

        long absolute =
            Math.Abs(
                value
            );

        if (absolute >=
            1_000_000_000)
        {
            return
                (value / 1_000_000_000d)
                    .ToString(
                        "0.#",
                        french
                    ) +
                "Md";
        }

        if (absolute >=
            1_000_000)
        {
            return
                (value / 1_000_000d)
                    .ToString(
                        "0.#",
                        french
                    ) +
                "M";
        }

        if (absolute >=
            1_000)
        {
            return
                (value / 1_000d)
                    .ToString(
                        "0.#",
                        french
                    ) +
                "k";
        }

        return value.ToString(
            "N0",
            french
        );
    }

    private static FormattedString
        BuildLotTermFormattedString(
            CrushSessionRuneLotLine lot)
    {
        Color color =
            lot.IsEstimated
                ? Colors.Orange
                : Colors.LightGray;

        FormattedString formatted =
            new();

        formatted.Spans.Add(
            new Span
            {
                Text =
                    lot.IsEstimated
                        ? $"~{lot.Count}x"
                        : $"{lot.Count}x",

                TextColor = color
            }
        );

        formatted.Spans.Add(
            new Span
            {
                Text =
                    FormatCompactPrice(
                        lot.LotPrice
                    ),

                TextColor = color,

                FontAttributes =
                    FontAttributes.Bold
            }
        );

        if (lot.IsEstimated &&
            lot.LotQuantity > 1)
        {
            formatted.Spans.Add(
                new Span
                {
                    Text =
                        $"/{lot.LotQuantity}",

                    TextColor = color
                }
            );
        }

        return formatted;
    }

    private static string BuildExcelTerm(
        CrushSessionRuneLotLine lot)
    {
        if (lot.IsEstimated &&
            lot.LotQuantity > 1)
        {
            return
                $"{lot.Count}*" +
                $"{lot.LotPrice}/" +
                $"{lot.LotQuantity}";
        }

        return
            $"{lot.Count}*" +
            $"{lot.LotPrice}";
    }

    private static string BuildExcelFormula(
        IReadOnlyList<
            CrushSessionRuneLotLine> lots)
    {
        return
            "=" +
            string.Join(
                "+",
                lots.Select(
                    BuildExcelTerm
                )
            );
    }

    private void MakeFormulaTermCopyable(
        View target,
        string termFormula,
        string fullFormula)
    {
        TapGestureRecognizer singleTap =
            new()
            {
                NumberOfTapsRequired = 1
            };

        singleTap.Tapped +=
            async (_, _) =>
            {
                int version =
                    ++_formulaTapVersion;

                await Task.Delay(
                    280
                );

                if (version !=
                    _formulaTapVersion)
                {
                    return;
                }

                if (DateTime.UtcNow -
                        _lastFormulaDoubleTapUtc <
                    TimeSpan.FromMilliseconds(
                        400
                    ))
                {
                    return;
                }

                await CopyToClipboardAsync(
                    termFormula
                );
            };

        TapGestureRecognizer doubleTap =
            new()
            {
                NumberOfTapsRequired = 2
            };

        doubleTap.Tapped +=
            async (_, _) =>
            {
                _lastFormulaDoubleTapUtc =
                    DateTime.UtcNow;

                _formulaTapVersion++;

                await CopyToClipboardAsync(
                    fullFormula
                );
            };

        target.GestureRecognizers.Add(
            singleTap
        );

        target.GestureRecognizers.Add(
            doubleTap
        );
    }

    private void MakeDoubleCopyable(
        View target,
        string fullFormula)
    {
        TapGestureRecognizer doubleTap =
            new()
            {
                NumberOfTapsRequired = 2
            };

        doubleTap.Tapped +=
            async (_, _) =>
            {
                _lastFormulaDoubleTapUtc =
                    DateTime.UtcNow;

                _formulaTapVersion++;

                await CopyToClipboardAsync(
                    fullFormula
                );
            };

        target.GestureRecognizers.Add(
            doubleTap
        );
    }

    private void MakeCopyable(
        View target,
        Func<string?> getText)
    {
        TapGestureRecognizer tap =
            new();

        tap.Tapped +=
            async (_, _) =>
            {
                await CopyToClipboardAsync(
                    getText()
                );
            };

        target.GestureRecognizers.Add(
            tap
        );
    }

    private static string FormatClipboardNumber(
        double value)
    {
        return Math.Round(
                value
            )
            .ToString(
                "0",
                CultureInfo.InvariantCulture
            );
    }

    private async Task CopyToClipboardAsync(
        string? text)
    {
        if (string.IsNullOrWhiteSpace(
            text))
        {
            return;
        }

        string value =
            text.Trim();

        await Clipboard.Default
            .SetTextAsync(
                value
            );

        int feedbackVersion =
            ++_copyFeedbackVersion;

        string feedback =
            $"✓ {value} copié";

        _copyFeedback.Text =
            feedback;

        await Task.Delay(
            900
        );

        if (feedbackVersion !=
                _copyFeedbackVersion ||
            _copyFeedback.Text !=
                feedback)
        {
            return;
        }

        _copyFeedback.Text =
            string.Empty;
    }

    private void RefreshTotalLabel()
    {
        _total.Text =
            _lastTotalValue
                is double total
                ? $"Valeur réelle : {total:N0} K"
                : "Valeur réelle : —";

        _total.TextColor =
            _lastTotalValue is null
                ? Colors.White
                : Colors.LightBlue;
    }
}