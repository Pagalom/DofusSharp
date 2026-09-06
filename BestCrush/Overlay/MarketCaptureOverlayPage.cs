using BestCrush.Services;
using System.Globalization;
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
        Padding = new Thickness(14);

        Label title = new()
        {
            Text = "Mise à jour marché",
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };

        Button close = new()
        {
            Text = "✕",
            FontSize = 15,
            TextColor = Colors.LightGray,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(8, 2),
            HorizontalOptions = LayoutOptions.End
        };

        close.Clicked += (_, _) =>
            overlayService.Hide();

        Grid header = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        header.Add(title, 0, 0);
        header.Add(close, 1, 0);

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

        VerticalStackLayout content = new()
        {
            Spacing = 9,
            Children =
            {
                header,
                _status,
                _objectName,
                _details,
                _footer
            }
        };

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

    private void SetCopyableDetails(
        params (
            string Text,
            string? ClipboardValue
        )[] segments)
    {
        FormattedString formatted =
            new();

        foreach ((
            string text,
            string? clipboardValue)
            in segments)
        {
            Span span =
                new()
                {
                    Text = text,
                    TextColor = Colors.White
                };

            if (!string.IsNullOrWhiteSpace(
                clipboardValue))
            {
                string copyValue =
                    clipboardValue;

                TapGestureRecognizer tap =
                    new();

                tap.Tapped +=
                    async (_, _) =>
                    {
                        await CopyToClipboardAsync(
                            copyValue
                        );
                    };

                span.GestureRecognizers.Add(
                    tap
                );
            }

            formatted.Spans.Add(
                span
            );
        }

        _details.FormattedText =
            formatted;
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

    private static string FormatClipboardNumber(
        long value)
    {
        return value.ToString(
            CultureInfo.InvariantCulture
        );
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
                    Colors.Transparent
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

    public void ShowCaptureStarted(
        DofusWindowInfo window)
    {
        SetState(
            $"Clic molette — capture {window.Width}×{window.Height}...",
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

    public void ShowMarketPanelDetected(
        DofusMarketPanelDetectionResult panel)
    {
        SetState(
            "✓ Hôtel de vente détecté",
            Colors.LightGreen,
            "Lecture HDV",
            $"Panneau détecté — confiance {panel.Confidence:P0}.",
            "Lecture des lots en cours",
            Colors.LightGreen
        );
    }

    public void ShowMarketEquipmentRead(
        string itemName,
        long? price)
    {
        string name =
            string.IsNullOrWhiteSpace(itemName)
                ? "Objet non reconnu"
                : itemName.Trim();

        string details =
            price is null
                ? "Objet détecté, mais aucun prix fiable n'a été lu."
                : $"Première offre réelle : {price.Value:N0} K.";

        SetState(
            price is null
                ? "⚠ Lecture HDV incomplète"
                : "✓ Hôtel de vente lu",
            price is null
                ? Colors.Orange
                : Colors.LightGreen,
            name,
            details,
            price is null
                ? "Aucune donnée enregistrée"
                : "Prix moyen ignoré",
            price is null
                ? Colors.Orange
                : Colors.LightGreen
        );

        if (price is long copyPrice)
        {
            SetCopyableDetails(
                (
                    "Première offre réelle : ",
                    null
                ),
                (
                    $"{copyPrice:N0} K",
                    FormatClipboardNumber(
                        copyPrice
                    )
                ),
                (
                    ".",
                    null
                )
            );
        }
    }

    public void ShowMarketEquipmentRecorded(
        string itemName,
        double confidence,
        long capturedPrice,
        long effectivePrice,
        bool manualPricePreserved)
    {
        string details =
            $"Reconnaissance : {confidence:P0}\n" +
            $"Prix détecté : {capturedPrice:N0} K\n" +
            (
                manualPricePreserved
                    ? $"Prix utilisé : {effectivePrice:N0} K (manuel conservé)"
                    : $"Prix utilisé : {effectivePrice:N0} K"
            );

        SetState(
            "✓ Prix HDV enregistré",
            Colors.LightGreen,
            itemName,
            details,
            "✓ Observation locale enregistrée",
            Colors.LightGreen
        );

        SetCopyableDetails(
            (
                $"Reconnaissance : {confidence:P0}\nPrix détecté : ",
                null
            ),
            (
                $"{capturedPrice:N0} K",
                FormatClipboardNumber(
                    capturedPrice
                )
            ),
            (
                "\nPrix utilisé : ",
                null
            ),
            (
                $"{effectivePrice:N0} K",
                FormatClipboardNumber(
                    effectivePrice
                )
            ),
            (
                manualPricePreserved
                    ? " (manuel conservé)"
                    : string.Empty,
                null
            )
        );
    }

    public void ShowMarketEquipmentRecognitionFailed(
        string recognizedName,
        long detectedPrice)
    {
        SetState(
            "⚠ Objet HDV non reconnu",
            Colors.Red,
            string.IsNullOrWhiteSpace(recognizedName)
                ? "Objet non reconnu"
                : recognizedName.Trim(),
            $"Prix détecté : {detectedPrice:N0} K\n" +
            "Le nom OCR n'a pas été associé à un objet DofusDB.",
            "Aucune donnée enregistrée",
            Colors.Red
        );

        SetCopyableDetails(
            (
                "Prix détecté : ",
                null
            ),
            (
                $"{detectedPrice:N0} K",
                FormatClipboardNumber(
                    detectedPrice
                )
            ),
            (
                "\nLe nom OCR n'a pas été associé à un objet DofusDB.",
                null
            )
        );
    }

    public void ShowAuxiliaryMarketDataRecorded(
        string objectName,
        string objectKind,
        double confidence,
        IReadOnlyList<MarketCapturePriceLine> prices,
        string? focusedEquipmentName)
    {
        string focusText =
            string.IsNullOrWhiteSpace(
                focusedEquipmentName)
                ? "Aucun équipement actuellement en focus."
                : $"Focus conservé : {focusedEquipmentName}.";

        SetState(
            "✓ Prix HDV enregistrés",
            Colors.LightGreen,
            objectName,
            $"{objectKind} — reconnaissance {confidence:P0}\n" +
            $"{prices.Count} lot(s) enregistré(s).\n" +
            focusText,
            "✓ Prix locaux mis à jour",
            Colors.LightGreen
        );

        List<(
            string Text,
            string? ClipboardValue)>
            segments =
                [];

        segments.Add(
            (
                $"{objectKind} — reconnaissance {confidence:P0}\n",
                null
            )
        );

        foreach (
            MarketCapturePriceLine price
            in prices.OrderBy(
                price =>
                    price.Quantity))
        {
            segments.Add(
                (
                    $"x{price.Quantity} — détecté : ",
                    null
                )
            );

            segments.Add(
                (
                    $"{price.CapturedPrice:N0} K",
                    FormatClipboardNumber(
                        price.CapturedPrice
                    )
                )
            );

            segments.Add(
                (
                    " | utilisé : ",
                    null
                )
            );

            segments.Add(
                (
                    $"{price.EffectivePrice:N0} K",
                    FormatClipboardNumber(
                        price.EffectivePrice
                    )
                )
            );

            if (price.EffectivePriceIsManual)
            {
                segments.Add(
                    (
                        " (manuel)",
                        null
                    )
                );
            }

            segments.Add(
                (
                    "\n",
                    null
                )
            );
        }

        if (string.IsNullOrWhiteSpace(
            focusedEquipmentName))
        {
            segments.Add(
                (
                    "Aucun équipement actuellement en focus.",
                    null
                )
            );
        }
        else
        {
            segments.Add(
                (
                    "Focus conservé : ",
                    null
                )
            );

            segments.Add(
                (
                    focusedEquipmentName,
                    focusedEquipmentName
                )
            );

            segments.Add(
                (
                    ".",
                    null
                )
            );
        }

        SetCopyableDetails(
            segments.ToArray()
        );
    }

    public void ShowAuxiliaryMarketReadFailed(
        string objectName)
    {
        SetState(
            $"⚠ Lecture incomplète : {objectName}",
            Colors.Red,
            objectName,
            "Aucun lot exploitable n'a pu être lu.",
            "Aucune donnée enregistrée",
            Colors.Red
        );
    }

    public void ShowPanelNotDetected()
    {
        SetState(
            "⚠ Aucun panneau reconnu",
            Colors.Orange,
            "Lecture non classée",
            "Ni HDV, ni panneau de concassage exploitable n'a été reconnu.",
            "Aucune donnée enregistrée",
            Colors.Orange
        );
    }

    public void ShowPanelDetected(
        DofusPanelDetectionResult panel)
    {
        _status.Text =
            $"✓ Concassage détecté — {panel.Confidence:P0}";
        _status.TextColor = Colors.LightGreen;
    }

    public void ShowCrushRowNotDetected()
    {
        _status.Text =
            "⚠ Aucune ligne de concassage renseignée";
        _status.TextColor = Colors.Orange;
    }

    public void ShowLastCrushRowDetected(
        CrushRowDetectionResult row)
    {
        _status.Text =
            $"✓ Dernière ligne détectée — Y={row.Y}";
        _status.TextColor = Colors.LightGreen;
    }

    public void ShowCrushFieldsExtracted()
    {
        _status.Text =
            "✓ Nom et coefficient isolés";
        _status.TextColor = Colors.LightGreen;
    }

    public void ShowCrushOcrResult(
        string itemName,
        double? coefficient)
    {
        string coefficientText =
            coefficient is null
                ? "non reconnu"
                : $"{coefficient:0.##} %";

        SetState(
            coefficient is null ||
            string.IsNullOrWhiteSpace(itemName)
                ? "⚠ OCR concassage incomplet"
                : "✓ OCR concassage",
            coefficient is null ||
            string.IsNullOrWhiteSpace(itemName)
                ? Colors.Orange
                : Colors.LightGreen,
            string.IsNullOrWhiteSpace(itemName)
                ? "Objet non reconnu"
                : itemName,
            $"Coefficient détecté : {coefficientText}",
            "Lecture du coefficient",
            coefficient is null
                ? Colors.Orange
                : Colors.LightGreen
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

    public void ShowServerNotSelected()
    {
        SetState(
            "⚠ Serveur non sélectionné",
            Colors.Red,
            "Coefficient non enregistré",
            "Sélectionnez d'abord un serveur BestCrush.",
            "Aucune donnée enregistrée",
            Colors.Red
        );
    }

    public void ShowRecognizedEquipment(
        string itemName,
        double recognitionConfidence,
        double detectedCoefficient,
        double appliedCoefficient,
        bool manualCoefficientPreserved,
        long? equipmentPrice)
    {
        string priceText =
            equipmentPrice is null
                ? "manquant"
                : $"{equipmentPrice.Value:N0} K";

        string coefficientText =
            manualCoefficientPreserved
                ? $"{appliedCoefficient:0.##} % (manuel conservé)\n" +
                  $"Détecté en jeu : {detectedCoefficient:0.##} %"
                : $"{appliedCoefficient:0.##} % (jeu)";

        SetState(
            "✓ Coefficient enregistré",
            Colors.LightGreen,
            itemName,
            $"Reconnaissance : {recognitionConfidence:P0}\n" +
            $"Prix équipement : {priceText}\n" +
            $"Coefficient : {coefficientText}",
            "✓ Données locales mises à jour",
            Colors.LightGreen
        );

        if (equipmentPrice is long copyEquipmentPrice)
        {
            SetCopyableDetails(
                (
                    $"Reconnaissance : {recognitionConfidence:P0}\nPrix équipement : ",
                    null
                ),
                (
                    $"{copyEquipmentPrice:N0} K",
                    FormatClipboardNumber(
                        copyEquipmentPrice
                    )
                ),
                (
                    $"\nCoefficient : {coefficientText}",
                    null
                )
            );
        }
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
