using BestCrush.Services;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace BestCrush.Overlay;

public sealed class MarketCaptureOverlayPage : ContentPage
{
    private readonly Label _status;
    private readonly Label _objectName;
    private readonly Label _details;
    private readonly Label _footer;

    public MarketCaptureOverlayPage(
        MarketCaptureOverlayService overlayService)
    {
        BackgroundColor = Color.FromArgb("#17191C");
        Padding = 0;

        Label title = new()
        {
            Text = "Mise à jour marché",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };

        Label dragHint = new()
        {
            Text = "⋮⋮",
            FontSize = 16,
            TextColor = Colors.Gray,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };

        Grid header = new()
        {
            BackgroundColor =
                Color.FromArgb(
                    "#1B1E22"
                ),

            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        header.Add(title, 0, 0);
        header.Add(dragHint, 1, 0);

        PanGestureRecognizer dragGesture = new();
        dragGesture.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    overlayService.BeginDrag();
                    break;

                case GestureStatus.Running:
                    overlayService.Drag(
                        e.TotalX,
                        e.TotalY
                    );
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    overlayService.EndDrag();
                    break;
            }
        };
        header.GestureRecognizers.Add(dragGesture);

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

        _status = new Label
        {
            Text = "Clic molette — prêt à lire",
            TextColor = Colors.Gray,
            FontSize = 12
        };

        _objectName = new Label
        {
            Text = "Aucune capture récente",
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        };

        _details = new Label
        {
            Text =
                "Les captures de runes, ressources et prix HDV " +
                "apparaîtront ici.",
            TextColor = Colors.White,
            FontSize = 13
        };

        _footer = new Label
        {
            Text = "En attente",
            TextColor = Colors.Gray,
            FontSize = 12
        };

        MakeCopyable(
            _objectName,
            () =>
                string.IsNullOrWhiteSpace(
                    _objectName.Text
                )
                    ? null
                    : _objectName.Text
        );

        ScrollView detailsScroll =
            new()
            {
                Content =
                    _details,

                VerticalScrollBarVisibility =
                    ScrollBarVisibility.Default,

                VerticalOptions =
                    LayoutOptions.Fill,

                HorizontalOptions =
                    LayoutOptions.Fill
            };

        Grid content =
            new()
            {
                RowDefinitions =
                {
                    new RowDefinition(
                        GridLength.Auto
                    ),
                    new RowDefinition(
                        GridLength.Auto
                    ),
                    new RowDefinition(
                        GridLength.Auto
                    ),
                    new RowDefinition(
                        GridLength.Star
                    ),
                    new RowDefinition(
                        GridLength.Auto
                    )
                },

                RowSpacing = 9,
                Margin = new Thickness(14),

                VerticalOptions =
                    LayoutOptions.Fill,

                HorizontalOptions =
                    LayoutOptions.Fill
            };

        content.Add(
            header,
            0,
            0
        );

        content.Add(
            _status,
            0,
            1
        );

        content.Add(
            _objectName,
            0,
            2
        );

        content.Add(
            detailsScroll,
            0,
            3
        );

        content.Add(
            _footer,
            0,
            4
        );

        Grid resizeContainer =
            CreateResizeContainer(
                content,
                overlayService
            );

        Content =
            resizeContainer;
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

        string previousText =
            _footer.Text;

        Color previousColor =
            _footer.TextColor;

        string feedback =
            $"✓ {value} copié";

        _footer.Text =
            feedback;

        _footer.TextColor =
            Colors.LightGreen;

        await Task.Delay(
            1200
        );

        if (_footer.Text !=
            feedback)
        {
            return;
        }

        _footer.Text =
            previousText;

        _footer.TextColor =
            previousColor;
    }

    private static Grid CreateResizeContainer(
        View content,
        MarketCaptureOverlayService
            overlayService)
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
            overlayService,
            0,
            1,
            OverlayResizeEdge.Top
        );

        AddResizeZone(
            container,
            overlayService,
            2,
            1,
            OverlayResizeEdge.Bottom
        );

        AddResizeZone(
            container,
            overlayService,
            1,
            0,
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            overlayService,
            1,
            2,
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            container,
            overlayService,
            0,
            0,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            overlayService,
            0,
            2,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            container,
            overlayService,
            2,
            0,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            container,
            overlayService,
            2,
            2,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Right
        );

        return container;
    }

    private static void AddResizeZone(
        Grid container,
        MarketCaptureOverlayService
            overlayService,
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
                        overlayService
                            .BeginResize(
                                edge
                            );
                        break;

                    case GestureStatus.Running:
                        overlayService
                            .Resize(
                                e.TotalX,
                                e.TotalY
                            );
                        break;

                    case GestureStatus.Completed:
                    case GestureStatus.Canceled:
                        overlayService
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

    public void ShowServerSelectionRequired()
    {
        SetState(
            "⚠ Sélectionnez d'abord un serveur",
            Colors.Red,
            "Serveur non sélectionné",
            "La lecture clic molette est désactivée tant qu'un " +
            "serveur BestCrush n'a pas été sélectionné.",
            "Aucune capture effectuée",
            Colors.Red
        );
    }

    public void ShowReadCancelled()
    {
        SetState(
            "⚠ Dofus non détecté",
            Colors.Orange,
            "Lecture annulée",
            "BestCrush n'a pas trouvé de fenêtre Dofus active.",
            "Aucune donnée enregistrée",
            Colors.Orange
        );
    }

    public void ShowTooltipNotDetected()
    {
        SetState(
            "⚠ Infobulle non détectée",
            Colors.Orange,
            "Aucun équipement en focus",
            "La capture Dofus a réussi, mais aucune infobulle " +
            "d'équipement exploitable n'a été repérée. " +
            "Survole un seul équipement et réessaie avec F8.",
            "Aucun focus modifié",
            Colors.Orange
        );
    }

    public void ShowCaptureStarted(
        DofusWindowInfo window)
    {
        SetState(
            $"F8 — capture {window.Width}×{window.Height}...",
            Colors.LightBlue,
            "Lecture en cours",
            "Capture de la fenêtre Dofus.",
            "Analyse en arrière-plan",
            Colors.Gray
        );
    }

    public void ShowCaptureSuccess(
        DofusCaptureResult capture)
    {
        _status.Text =
            $"✓ Capture réussie — {capture.Width}×{capture.Height}";
        _status.TextColor = Colors.LightGreen;
    }

    public void ShowCaptureFailed(
        string message)
    {
        SetState(
            "⚠ Lecture impossible",
            Colors.Red,
            "Capture non exploitable",
            message,
            "Aucune donnée enregistrée",
            Colors.Red
        );
    }

    public void ShowMultipleTooltipsDetected(
        int count)
    {
        SetState(
            $"⚠ {count} infobulles détectées",
            Colors.Orange,
            "Lecture ambiguë",
            "Plusieurs infobulles sont visibles simultanément.",
            "Aucune donnée enregistrée",
            Colors.Orange
        );
    }

    public void ShowTooltipEquipmentFocused(
        string itemName,
        double confidence)
    {
        SetState(
            "✓ Équipement en focus",
            Colors.LightGreen,
            itemName,
            $"Reconnaissance : {confidence:P0}\n" +
            "Le focus Rentabilité a été mis à jour.",
            "✓ Focus conservé",
            Colors.LightGreen
        );
    }

    public void ShowEquipmentRecognitionFailed(
        string recognizedText)
    {
        SetState(
            "⚠ Équipement non reconnu",
            Colors.Red,
            string.IsNullOrWhiteSpace(recognizedText)
                ? "Équipement non reconnu"
                : recognizedText,
            "La lecture OCR n'a pas pu être associée " +
            "avec suffisamment de certitude à un équipement DofusDB.",
            "Aucune donnée enregistrée",
            Colors.Red
        );
    }

    private void SetState(
        string status,
        Color statusColor,
        string objectName,
        string details,
        string footer,
        Color footerColor)
    {
        _status.Text = status;
        _status.TextColor = statusColor;
        _objectName.Text = objectName;
        _details.FormattedText = null;
        _details.Text = details;
        _footer.Text = footer;
        _footer.TextColor = footerColor;
    }
}
