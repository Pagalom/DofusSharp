using BestCrush.Services;

using BestCrush.Domain.Models;

using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace BestCrush.Overlay;

public sealed class OverlayPage : ContentPage
{
    private readonly Label _readStatus;
    private readonly Border _hoverTooltip;
    private bool _pointerOverTooltip;
    private bool _pointerOverTooltipTarget;
    private int _tooltipHideVersion;
    private readonly VerticalStackLayout _hoverTooltipContent;

    private EquipmentProfitabilityResult? _currentProfitability;
    private EquipmentProfitabilityScenario? _currentScenario;
    private readonly Label _item;
    private readonly Label _details;

    private readonly VerticalStackLayout _profitabilityDetails;
    private readonly Label _runeValueLine;
    private readonly Label _coefficientLine;
    private readonly Label _purchaseLine;
    private readonly Label _purchaseResultLine;
    private readonly Label _craftLine;
    private readonly Label _craftResultLine;
    private readonly Label _partialLine;

    private readonly Label _footer;
    public OverlayPage(OverlayService overlayService,DofusWindowService dofusWindowService)
    {
        BackgroundColor = Color.FromArgb("#17191C");
        Padding = 0;

        Label title = new()
        {
            Text = "BestCrush",
            FontSize = 19,
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

        Grid dragZone = new()
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        dragZone.Add(title, 0, 0);
        dragZone.Add(dragHint, 1, 0);

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
            }
        };

        dragZone.GestureRecognizers.Add(dragGesture);
        
        Label status = new()
        {
            Text = "Recherche de Dofus...",
            TextColor = Colors.Gray,
            FontSize = 12
        };

        _readStatus = new Label
        {
            Text = "F8 — prêt à lire",
            TextColor = Colors.Gray,
            FontSize = 12
        };

        _item = new Label
        {
            Text = "Aucun équipement en focus",
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 17
        };

        _details = new Label
        {
            Text =
                "F8 sur un équipement en HDV ou sur un concassage " +
                "pour sélectionner l'équipement à analyser.",
            TextColor = Colors.White,
            FontSize = 14,
            VerticalOptions = LayoutOptions.Start
        };

        _coefficientLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _runeValueLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _purchaseLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _purchaseResultLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _craftLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _craftResultLine = new Label
        {
            TextColor = Colors.White,
            FontSize = 14
        };

        _partialLine = new Label
        {
            TextColor = Colors.Orange,
            FontSize = 13
        };

        _profitabilityDetails = new VerticalStackLayout
        {
            Spacing = 5,
            IsVisible = false,
            Children =
            {
                _coefficientLine,
                _runeValueLine,

                new BoxView
                {
                    HeightRequest = 4,
                    Opacity = 0
                },

                _purchaseLine,
                _purchaseResultLine,

                new BoxView
                {
                    HeightRequest = 4,
                    Opacity = 0
                },

                _craftLine,
                _craftResultLine,

                new BoxView
                {
                    HeightRequest = 4,
                    Opacity = 0
                },

                _partialLine
            }
        };

        VerticalStackLayout detailsContainer = new()
        {
            Spacing = 0,
            Children =
            {
                _details,
                _profitabilityDetails
            }
        };

        ScrollView detailsScroll = new()
        {
            Content = detailsContainer,
            VerticalScrollBarVisibility =
                ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Start
        };

        _footer = new Label
        {
            Text = "En attente d'un équipement",
            TextColor = Colors.Gray,
            FontSize = 12
        };

        Grid root = new()
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 8,

            VerticalOptions = LayoutOptions.Start,

            Margin = new Thickness(14)
        };

        root.Add(dragZone, 0, 0);
        root.Add(status, 0, 1);
        root.Add(_readStatus, 0, 2);
        root.Add(_item, 0, 3);
        root.Add(detailsScroll, 0, 4);
        root.Add(_footer, 0, 5);

        void UpdateDofusStatus()
        {
            DofusWindowInfo? dofusWindow =
                dofusWindowService.GetActiveDofusWindow();

            if (dofusWindow is null)
            {
                status.Text =
                    "○ Dofus non détecté";

                status.TextColor =
                    Colors.Orange;

                return;
            }

            status.Text =
                $"● Dofus détecté — {dofusWindow.Width}×{dofusWindow.Height}";

            status.TextColor =
                Colors.LightGreen;
        }

        Loaded += (_, _) =>
        {
            UpdateDofusStatus();

            Dispatcher.StartTimer(
                TimeSpan.FromSeconds(1),
                () =>
                {
                    UpdateDofusStatus();
                    return true;
                }
            );
        };

        Grid resizeContainer = new()
        {
            RowDefinitions =
            {
                new RowDefinition(10),
                new RowDefinition(GridLength.Star),
                new RowDefinition(10)
            },

            ColumnDefinitions =
            {
                new ColumnDefinition(10),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(10)
            }
        };

        // Le contenu normal occupe toute la fenêtre.
        Grid.SetRow(root, 0);
        Grid.SetColumn(root, 0);

        Grid.SetRowSpan(root, 3);
        Grid.SetColumnSpan(root, 3);

        resizeContainer.Children.Add(root);

        _hoverTooltipContent = new VerticalStackLayout
        {
            Spacing = 4
        };

        _hoverTooltip = new Border
        {
            BackgroundColor = Color.FromArgb("#111315"),
            Stroke = Color.FromArgb("#555A60"),
            StrokeThickness = 1,
            Padding = new Thickness(9),
            IsVisible = false,
            InputTransparent = false,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(18, 145, 18, 0),
            MaximumWidthRequest = 305,
            ZIndex = 1000,
            Content = _hoverTooltipContent
        };

        Grid.SetRow(_hoverTooltip, 0);
        Grid.SetColumn(_hoverTooltip, 0);
        Grid.SetRowSpan(_hoverTooltip, 3);
        Grid.SetColumnSpan(_hoverTooltip, 3);

        PointerGestureRecognizer tooltipPointer =
            new();

        tooltipPointer.PointerEntered +=
            (_, _) =>
            {
                _pointerOverTooltip = true;
                CancelTooltipHide();
            };

        tooltipPointer.PointerExited +=
            (_, _) =>
            {
                _pointerOverTooltip = false;
                ScheduleTooltipHide();
            };

        _hoverTooltip.GestureRecognizers.Add(
            tooltipPointer
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            0,
            1,
            OverlayResizeEdge.Top
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            2,
            1,
            OverlayResizeEdge.Bottom
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            1,
            0,
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            1,
            2,
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            0,
            0,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            0,
            2,
            OverlayResizeEdge.Top |
            OverlayResizeEdge.Right
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            2,
            0,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Left
        );

        AddResizeZone(
            resizeContainer,
            overlayService,
            2,
            2,
            OverlayResizeEdge.Bottom |
            OverlayResizeEdge.Right
        );

        resizeContainer.Children.Add(
            _hoverTooltip
        );

        AttachHoverTooltip(
            _runeValueLine,
            ShowRuneComparisonTooltip,
            145
        );

        AttachHoverTooltip(
            _craftLine,
            ShowCraftDetailsTooltip,
            215
        );

        AttachHoverTooltip(
            _partialLine,
            ShowMissingDataTooltip,
            290
        );

        MakeCopyable(
            _item,
            () =>
            {
                string? focusedName =
                    _currentProfitability?
                        .Equipment
                        .Name;

                return string.Equals(
                    _item.Text,
                    focusedName,
                    StringComparison.Ordinal
                )
                    ? focusedName
                    : null;
            }
        );

        Content = resizeContainer;
    }

    private void AttachHoverTooltip(
        View target,
        Action showTooltip,
        double topMargin)
    {
        PointerGestureRecognizer pointer =
            new();

        pointer.PointerEntered +=
            (_, _) =>
            {
                _pointerOverTooltipTarget = true;

                CancelTooltipHide();

                _hoverTooltip.Margin =
                    new Thickness(
                        18,
                        topMargin,
                        18,
                        0
                    );

                showTooltip();
            };

        pointer.PointerExited +=
            (_, _) =>
            {
                _pointerOverTooltipTarget = false;

                ScheduleTooltipHide();
            };

        target.GestureRecognizers.Add(
            pointer
        );
    }

    private void CancelTooltipHide()
    {
        _tooltipHideVersion++;
    }

    private async void ScheduleTooltipHide()
    {
        int version =
            ++_tooltipHideVersion;

        await Task.Delay(150);

        if (version != _tooltipHideVersion)
        {
            return;
        }

        if (!_pointerOverTooltipTarget &&
            !_pointerOverTooltip)
        {
            _hoverTooltip.IsVisible =
                false;
        }
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

        await Task.Delay(1200);

        // On ne restaure l'ancien footer que si
        // rien d'autre ne l'a modifié entre-temps.
        if (_footer.Text == feedback)
        {
            _footer.Text =
                previousText;

            _footer.TextColor =
                previousColor;
        }
    }

    public void ShowMarketPanelDetected(
        DofusMarketPanelDetectionResult panel)
    {
        _readStatus.Text =
            "✓ Hôtel de vente détecté";

        _readStatus.TextColor =
            Colors.LightGreen;

        _item.Text =
            "Lecture HDV";

        _details.Text =
            $"Panneau des offres détecté\n" +
            $"Confiance : {panel.Confidence:P0}\n\n" +
            "Lecture des prix à venir.";

        _footer.Text =
            "✓ Panneau Lot / Prix reconnu";

        _footer.TextColor =
            Colors.LightGreen;
    }

    private static void AddResizeZone(
        Grid container,
        OverlayService overlayService,
        int row,
        int column,
        OverlayResizeEdge edge)
    {
        BoxView resizeZone = new()
        {
            BackgroundColor =
                Colors.Transparent
        };

        PanGestureRecognizer gesture = new();

        gesture.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    overlayService.BeginResize(
                        edge
                    );
                    break;

                case GestureStatus.Running:
                    overlayService.Resize(
                        e.TotalX,
                        e.TotalY
                    );
                    break;
            }
        };

        resizeZone.GestureRecognizers.Add(
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

    public void ShowMarketEquipmentRead(
        string itemName,
        long? price)
    {
        ShowNormalDetails();

        _readStatus.Text =
            "✓ Hôtel de vente lu";

        _readStatus.TextColor =
            Colors.LightGreen;

        _item.Text =
            string.IsNullOrWhiteSpace(itemName)
                ? "Objet non reconnu"
                : itemName.Trim();

        if (price is not null)
        {
            _details.Text =
                $"Première offre réelle : " +
                $"{price.Value:N0} kamas\n\n" +
                "Prix moyen ignoré.";
        }
        else
        {
            _details.Text =
                "Objet reconnu, mais le prix " +
                "de la première offre n'a pas pu être lu.";
        }

        _footer.Text =
            "Test HDV — aucune donnée enregistrée";

        _footer.TextColor =
            price is not null
                ? Colors.LightGreen
                : Colors.Orange;
    }

    public void ShowMarketEquipmentRecorded(
        string itemName,
        double confidence,
        long capturedPrice,
        long effectivePrice,
        bool manualPricePreserved)
    {
        ShowNormalDetails();

        _readStatus.Text =
            "✓ Prix HDV enregistré";

        _readStatus.TextColor =
            Colors.LightGreen;

        _item.Text =
            itemName;

        string details =
            $"Reconnaissance : {confidence:P0}\n\n" +
            $"Prix détecté : {capturedPrice:N0} kamas\n";

        if (manualPricePreserved)
        {
            details +=
                $"Prix utilisé : {effectivePrice:N0} kamas (manuel)\n" +
                $"Détecté en jeu : {capturedPrice:N0} kamas — non appliqué";
        }
        else
        {
            details +=
                $"Prix utilisé : {effectivePrice:N0} kamas";
        }

        _details.Text = details;

        _footer.Text =
            "✓ Observation locale enregistrée";

        _footer.TextColor =
            Colors.LightGreen;
    }

    public void ShowMarketEquipmentRecognitionFailed(
        string recognizedName,
        long detectedPrice)
    {
        ShowNormalDetails();

        _readStatus.Text =
            "⚠ Objet HDV non reconnu";

        _readStatus.TextColor =
            Colors.Orange;

        _item.Text =
            string.IsNullOrWhiteSpace(recognizedName)
                ? "Objet non reconnu"
                : recognizedName.Trim();

        _details.Text =
            $"Prix détecté : {detectedPrice:N0} kamas\n\n" +
            "Le nom OCR n'a pas pu être associé " +
            "avec suffisamment de certitude à un équipement DofusDB.";

        _footer.Text =
            "Aucune donnée enregistrée";

        _footer.TextColor =
            Colors.Orange;
    }
    public void ShowRecognizedEquipment(
        string itemName,
        double recognitionConfidence,
        double detectedCoefficient,
        double appliedCoefficient,
        bool manualCoefficientPreserved,
        long? equipmentPrice)
    {
        ShowNormalDetails();

        _item.Text = itemName;

        string priceText =
            equipmentPrice is null
                ? "manquant"
                : $"{equipmentPrice.Value:N0} K";

        string coefficientText;

        if (manualCoefficientPreserved)
        {
            coefficientText =
                $"{appliedCoefficient:0.##} % (manuel)\n" +
                $"Détecté en jeu : {detectedCoefficient:0.##} % — non appliqué";
        }
        else
        {
            coefficientText =
                $"{appliedCoefficient:0.##} % (jeu)";
        }

        _details.Text =
            $"Reconnaissance : {recognitionConfidence:P0}\n\n" +
            $"Prix équipement : {priceText}\n" +
            $"Coefficient : {coefficientText}\n\n" +
            "Valeur des runes : à calculer\n" +
            "Coût des ressources : à calculer";

        if (equipmentPrice is null)
        {
            _footer.Text =
                "⚠ Prix local de l'équipement manquant";

            _footer.TextColor =
                Colors.Orange;
        }
        else
        {
            _footer.Text =
                "✓ Équipement reconnu — données complémentaires à charger";

            _footer.TextColor =
                Colors.LightGreen;
        }
    }

    private void ShowNormalDetails()
    {
        _details.IsVisible = true;
        _profitabilityDetails.IsVisible = false;
    }

    private void ShowProfitabilityDetails()
    {
        _details.IsVisible = false;
        _profitabilityDetails.IsVisible = true;
    }

    public void ShowProfitability(
        EquipmentProfitabilityResult result)
    {
        ShowProfitabilityDetails();

        _item.Text =
            result.Equipment.Name;

        EquipmentProfitabilityScenario? scenario =
            result.BestByBenefit;
        
        _currentProfitability =
            result;

        _currentScenario =
            scenario;
        
        int missingDataCount =
            GetMissingDataCount(
                result,
                scenario
            );

        string equipmentPrice =
            result.EquipmentCost is null
                ? "indisponible"
                : $"{result.EquipmentCost.Price:N0} K";

        string craftPrice =
            result.CraftCost.TotalCost is long craftCost
                ? $"{craftCost:N0} K"
                : result.CraftCost.KnownCost > 0
                    ? $"{result.CraftCost.KnownCost:N0} K connus"
                    : "incomplet";
        
        if (result.Coefficient is null)
        {
            _coefficientLine.Text =
                "Coefficient : À scanner";

            _coefficientLine.TextColor =
                Colors.White;
        }
        else
        {
            SetFreshnessLine(
                _coefficientLine,
                $"Coefficient : " +
                $"{result.Coefficient.CoefficientPercent:0.##} %",
                DataFreshnessEvaluator.Evaluate(
                    result.Coefficient.ObservedAtUtc
                )
            );
        }

        if (scenario is null)
        {
            _runeValueLine.Text =
                "Valeur runes : indisponible";

            _purchaseLine.Text =
                $"Achat équipement : {equipmentPrice}";

            _purchaseResultLine.Text = "";

            _craftLine.Text =
                $"Craft : {craftPrice}";

            _craftResultLine.Text = "";

            _partialLine.Text =
                missingDataCount > 0
                    ? $"⚠ {missingDataCount} donnée(s) manquante(s)"
                    : "";

            return;
        }

        string focusLabel =
            GetFocusLabel(scenario);

        SetFreshnessLine(
            _runeValueLine,
            $"Valeur runes ({focusLabel}) : " +
            $"{scenario.EstimatedRuneValue:N0} K",
            GetRuneFreshness(
                result
            )
        );

        SetFreshnessLine(
            _purchaseLine,
            $"Achat équipement : {equipmentPrice}",
            result.EquipmentCost is null
                ? null
                : DataFreshnessEvaluator.Evaluate(
                    result.EquipmentCost.ObservedAtUtc
                )
        );

        _purchaseResultLine.Text =
            scenario.PurchaseBenefit is double purchaseBenefit &&
            scenario.PurchaseYield is double purchaseYield
                ? $"→ {FormatProfitability(
                    purchaseBenefit,
                    purchaseYield)}"
                : "→ indisponible";
        
        _purchaseResultLine.TextColor =
            scenario.PurchaseBenefit is double purchaseBenefitColor
                ? purchaseBenefitColor > 0
                    ? Colors.LightGreen
                    : Colors.Red
                : Colors.Gray;

        SetFreshnessLine(
            _craftLine,
            $"Craft : {craftPrice}",
            GetCraftFreshness(
                result.CraftCost
            )
        );

        _craftResultLine.Text =
            scenario.CraftBenefit is double craftBenefit &&
            scenario.CraftYield is double craftYield
                ? $"→ {FormatProfitability(
                    craftBenefit,
                    craftYield)}"
                : "→ incomplet";
        
        _craftResultLine.TextColor =
            scenario.CraftBenefit is double craftBenefitColor
                ? craftBenefitColor > 0
                    ? Colors.LightGreen
                    : Colors.Red
                : Colors.Gray;

        if (result.IsPartial)
        {
            _partialLine.Text =
                $"⚠ Résultat partiel — " +
                $"{missingDataCount} donnée(s) manquante(s)";
        }
        else
        {
            _partialLine.Text = "";
        }
    }

    private void ShowRuneComparisonTooltip()
    {
        EquipmentProfitabilityResult? result =
            _currentProfitability;

        EquipmentProfitabilityScenario? best =
            _currentScenario;

        if (result is null ||
            best is null)
        {
            return;
        }

        _hoverTooltipContent.Children.Clear();

        HashSet<string> displayedRunes =
        new(
            StringComparer.OrdinalIgnoreCase
        );

        IEnumerable<EquipmentProfitabilityScenario>
            focusScenarios =
                result.Scenarios
                    .Where(scenario =>
                        scenario.FocusedCharacteristic
                            is not null);

        foreach (
            EquipmentProfitabilityScenario scenario
            in focusScenarios)
        {
            Rune? focusedRune =
                GetFocusedRune(
                    scenario
                );

            if (focusedRune is null)
            {
                continue;
            }

            displayedRunes.Add(
                focusedRune.Name
            );

            Grid row =
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
                    },

                    ColumnSpacing = 10
                };

            Label right =
                new()
                {
                    FontSize = 12,
                    HorizontalTextAlignment =
                        TextAlignment.End
                };

            scenario.Runes.TryGetValue(
                focusedRune,
                out double runeQuantity
            );

            bool hasRuneValue =
                scenario.RuneValues.TryGetValue(
                    focusedRune,
                    out MarketValueResult runeValue
                );

            DataFreshness? runeFreshness =
                hasRuneValue
                    ? runeValue.Freshness
                    : null;

            string unitPriceText;

            if (hasRuneValue &&
                runeQuantity > 0)
            {
                double unitPrice =
                    runeValue.Value /
                    runeQuantity;

                unitPriceText =
                    $" ({unitPrice:N0} K/u)";
            }
            else
            {
                unitPriceText =
                    " (À scanner)";
            }

            HorizontalStackLayout left =
                new()
                {
                    Spacing = 0
                };

            Label runeNameLabel =
                new()
                {
                    Text =
                        focusedRune.Name,

                    FontSize = 12,

                    TextColor =
                        runeFreshness is
                            DataFreshness freshness
                            ? GetFreshnessColor(
                                freshness
                            )
                            : Colors.White
                };

            Label unitPriceLabel =
                new()
                {
                    Text =
                        unitPriceText,

                    FontSize = 12,
                    TextColor = Colors.White
                };

            string runeName =
                focusedRune.Name;

            MakeCopyable(
                runeNameLabel,
                () => runeName
            );

            left.Children.Add(
                runeNameLabel
            );

            left.Children.Add(
                unitPriceLabel
            );

            if (hasRuneValue)
            {
                right.Text =
                    $"{scenario.EstimatedRuneValue:N0} K";

                right.TextColor =
                    SameScenario(
                        scenario,
                        best
                    )
                        ? Colors.Gold
                        : Colors.White;
            }
            else
            {
                right.Text =
                    "À scanner";

                right.TextColor =
                    Colors.White;
            }

            row.Add(
                left,
                0,
                0
            );

            row.Add(
                right,
                1,
                0
            );

            _hoverTooltipContent
                .Children
                .Add(row);
        }

        IEnumerable<string> missingRunes =
            result.MissingData
                .Where(value =>
                    value.StartsWith(
                        "Prix de rune manquant :",
                        StringComparison.Ordinal
                    ))
                .Select(value =>
                    value[
                        "Prix de rune manquant :"
                            .Length..
                    ])
                .Distinct(
                    StringComparer.OrdinalIgnoreCase
                )
                .Where(runeName =>
                    !displayedRunes.Contains(
                        runeName
                    ))
                .OrderBy(runeName =>
                    runeName);

        foreach (string runeName in missingRunes)
        {
            Grid row =
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
                    },

                    ColumnSpacing = 10
                };

            HorizontalStackLayout left =
                new()
                {
                    Spacing = 0
                };

            Label runeNameLabel =
                new()
                {
                    Text = runeName,
                    TextColor = Colors.White,
                    FontSize = 12
                };

            Label missingLabel =
                new()
                {
                    Text = " (À scanner)",
                    TextColor = Colors.White,
                    FontSize = 12
                };

            string copyRuneName =
                runeName;

            MakeCopyable(
                runeNameLabel,
                () => copyRuneName
            );

            left.Children.Add(
                runeNameLabel
            );

            left.Children.Add(
                missingLabel
            );

            Label right =
                new()
                {
                    Text = "À scanner",
                    TextColor = Colors.White,
                    FontSize = 12,
                    HorizontalTextAlignment =
                        TextAlignment.End
                };

            row.Add(
                left,
                0,
                0
            );

            row.Add(
                right,
                1,
                0
            );

            _hoverTooltipContent
                .Children
                .Add(row);
        }

        _hoverTooltipContent.Children.Add(
            new BoxView
            {
                HeightRequest = 1,
                BackgroundColor =
                    Color.FromArgb("#555A60"),
                Margin =
                    new Thickness(
                        0,
                        3
                    )
            }
        );

        EquipmentProfitabilityScenario?
            noFocus =
                result.Scenarios
                    .FirstOrDefault(
                        scenario =>
                            scenario
                                .FocusedCharacteristic
                            is null
                    );

        Grid totalRow =
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
                },

                ColumnSpacing = 10
            };

        Label totalName =
            new()
            {
                Text =
                    "TOTAL (sans focus)",
                TextColor =
                    Colors.White,
                FontSize = 12,
                FontAttributes =
                    FontAttributes.Bold
            };

        Label totalValue =
            new()
            {
                FontSize = 12,
                FontAttributes =
                    FontAttributes.Bold
            };

        if (noFocus is null)
        {
            totalValue.Text =
                "À scanner";

            totalValue.TextColor =
                Colors.White;
        }
        else
        {
            totalValue.Text =
                $"{noFocus.EstimatedRuneValue:N0} K";

            totalValue.TextColor =
                SameScenario(
                    noFocus,
                    best
                )
                    ? Colors.Gold
                    : Colors.White;
        }

        totalRow.Add(
            totalName,
            0,
            0
        );

        totalRow.Add(
            totalValue,
            1,
            0
        );

        _hoverTooltipContent
            .Children
            .Add(totalRow);

        _hoverTooltip.IsVisible =
            true;
    }

    private static bool IsCopyableMissingItem(
        EquipmentProfitabilityResult result,
        string item)
    {
        bool isResource =
            result.CraftCost.Resources
                .Any(resource =>
                    resource.Purchase is null &&
                    string.Equals(
                        resource.ResourceName,
                        item,
                        StringComparison.OrdinalIgnoreCase
                    ));

        if (isResource)
        {
            return true;
        }

        const string runePrefix =
            "Prix de rune manquant :";

        return result.MissingData
            .Where(value =>
                value.StartsWith(
                    runePrefix,
                    StringComparison.Ordinal
                ))
            .Select(value =>
                value[runePrefix.Length..]
                    .Trim())
            .Any(runeName =>
                string.Equals(
                    runeName,
                    item,
                    StringComparison.OrdinalIgnoreCase
                ));
    }

    private static Rune? GetFocusedRune(
        EquipmentProfitabilityScenario scenario)
    {
        if (scenario.FocusedCharacteristic
            is null)
        {
            return null;
        }

        return scenario.Runes.Keys
            .FirstOrDefault(
                rune =>
                    rune.Characteristic ==
                    scenario.FocusedCharacteristic
            );
    }

    private static IReadOnlyList<string>
        GetMissingDataItems(
            EquipmentProfitabilityResult result)
    {
        List<string> items = [];

        if (result.EquipmentCost is null)
        {
            items.Add(
                "Prix de l'équipement"
            );
        }

        if (result.Coefficient is null)
        {
            items.Add(
                "Coefficient de brisage"
            );
        }

        items.AddRange(
            result.CraftCost.Resources
                .Where(resource =>
                    resource.Purchase is null)
                .Select(resource =>
                    resource.ResourceName)
        );

        items.AddRange(
            result.MissingData
                .Where(value =>
                    value.StartsWith(
                        "Prix de rune manquant :",
                        StringComparison.Ordinal
                    ))
                .Select(value =>
                    value[
                        "Prix de rune manquant :"
                            .Length..
                    ])
        );

        return items
            .Distinct(
                StringComparer.OrdinalIgnoreCase
            )
            .ToList();
    }

    private void ShowCraftDetailsTooltip()
    {
        EquipmentProfitabilityResult? result =
            _currentProfitability;

        if (result is null)
        {
            return;
        }

        _hoverTooltipContent.Children.Clear();

        foreach (CraftResourceCostLine resource
                in result.CraftCost.Resources
                    .OrderBy(resource =>
                        resource.ResourceName))
        {
            Grid row =
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
                    },

                    ColumnSpacing = 10
                };

                int? requiredQuantity =
                    resource.Purchase?
                        .RequiredQuantity;

                Label name =
                    new()
                    {
                        Text =
                            requiredQuantity is int quantity
                                ? $"{resource.ResourceName} x{quantity}"
                                : resource.ResourceName,

                        FontSize = 12,

                        TextColor =
                            resource.Purchase?.Freshness
                                is DataFreshness freshness
                                ? GetFreshnessColor(
                                    freshness
                                )
                                : Colors.White
                    };
            
            string resourceName =
                resource.ResourceName;

            MakeCopyable(
                name,
                () => resourceName
            );

            Label price =
                new()
                {
                    FontSize = 12,
                    TextColor = Colors.White,
                    HorizontalTextAlignment =
                        TextAlignment.End,

                    Text =
                        resource.Purchase is null
                            ? "À scanner"
                            : $"{resource.Purchase.TotalCost:N0} K"
                };

            row.Add(
                name,
                0,
                0
            );

            row.Add(
                price,
                1,
                0
            );

            _hoverTooltipContent
                .Children
                .Add(row);
        }

        _hoverTooltip.IsVisible =
            true;
    }

    private static bool SameScenario(
        EquipmentProfitabilityScenario left,
        EquipmentProfitabilityScenario right)
    {
        return left.FocusedCharacteristic ==
            right.FocusedCharacteristic;
    }

    private static void SetFreshnessLine(
        Label label,
        string text,
        DataFreshness? freshness)
    {
        FormattedString formatted =
            new();

        if (freshness is DataFreshness value)
        {
            formatted.Spans.Add(
                new Span
                {
                    Text = "● ",
                    TextColor =
                        GetFreshnessColor(
                            value
                        )
                }
            );
        }

        formatted.Spans.Add(
            new Span
            {
                Text = text,
                TextColor = Colors.White
            }
        );

        label.FormattedText =
            formatted;
    }

    private static Color GetFreshnessColor(
        DataFreshness freshness)
    {
        return freshness switch
        {
            DataFreshness.Fresh =>
                Colors.LightGreen,

            DataFreshness.Aging =>
                Colors.Orange,

            DataFreshness.Stale =>
                Colors.Red,

            _ =>
                Colors.White
        };
    }

    public void ShowTooltipEquipmentFocused(
        string itemName,
        double confidence)
    {
        _readStatus.Text =
            $"✓ Focus : {itemName}";

        _readStatus.TextColor =
            Colors.LightGreen;

        _footer.Text =
            $"✓ Infobulle reconnue ({confidence:P0}) — " +
            "aucune donnée modifiée";

        _footer.TextColor =
            Colors.LightGreen;
    }

    public void ShowMultipleTooltipsDetected(
        int count)
    {
        _readStatus.Text =
            $"⚠ {count} infobulles détectées";

        _readStatus.TextColor =
            Colors.Orange;

        _footer.Text =
            "Plusieurs infobulles épinglées — focus impossible";

        _footer.TextColor =
            Colors.Orange;
    }

    public void ShowTooltipEquipmentNotRecognized(
        string recognizedTitle)
    {
        _readStatus.Text =
            $"⚠ Infobulle non reconnue : {recognizedTitle}";

        _readStatus.TextColor =
            Colors.Orange;

        _footer.Text =
            "Focus inchangé";

        _footer.TextColor =
            Colors.Orange;
    }

    private static DataFreshness? GetRuneFreshness(
        EquipmentProfitabilityResult result)
    {
        bool hasMissingRune =
            result.MissingData.Any(
                value =>
                    value.StartsWith(
                        "Prix de rune manquant :",
                        StringComparison.Ordinal
                    )
            );

        if (hasMissingRune)
        {
            return null;
        }

        DataFreshness[] freshness =
            result.Scenarios
                .SelectMany(
                    scenario =>
                        scenario.RuneValues.Values
                )
                .Select(
                    value =>
                        value.Freshness
                )
                .Where(
                    value =>
                        value is not null
                )
                .Select(
                    value =>
                        value!.Value
                )
                .ToArray();

        return freshness.Length == 0
            ? null
            : freshness.Max();
    }

    private static DataFreshness? GetCraftFreshness(
        CraftCostResult craftCost)
    {
        DataFreshness[] freshness =
            craftCost.Resources
                .Select(resource =>
                    resource.Purchase?.Freshness)
                .Where(value =>
                    value is not null)
                .Select(value =>
                    value!.Value)
                .ToArray();

        return freshness.Length == 0
            ? null
            : freshness.Max();
    }

    private static int GetMissingDataCount(
        EquipmentProfitabilityResult result,
        EquipmentProfitabilityScenario? scenario)
    {
        return GetMissingDataItems(
            result
        ).Count;
    }

    private void ShowMissingDataTooltip()
    {
        EquipmentProfitabilityResult? result =
            _currentProfitability;

        if (result is null)
        {
            return;
        }

        IReadOnlyList<string> missing =
            GetMissingDataItems(
                result
            );

        if (missing.Count == 0)
        {
            return;
        }

        _hoverTooltipContent.Children.Clear();

        foreach (string item in missing)
        {
            Label label =
                new()
                {
                    Text = item,
                    TextColor = Colors.White,
                    FontSize = 12
                };

            if (IsCopyableMissingItem(
                result,
                item))
            {
                string copyName =
                    item;

                MakeCopyable(
                    label,
                    () => copyName
                );
            }

            _hoverTooltipContent.Children.Add(
                label
            );
        }

        _hoverTooltip.IsVisible =
            true;
    }

    private static string GetFocusLabel(
        EquipmentProfitabilityScenario scenario)
    {
        if (scenario.FocusedCharacteristic is null)
        {
            return "sans focus";
        }

        Rune? focusedRune =
            scenario.Runes.Keys
                .FirstOrDefault(rune =>
                    rune.Characteristic ==
                    scenario.FocusedCharacteristic);

        return focusedRune?.Name
            ?? "focus";
    }

    private static string FormatProfitability(
        double benefit,
        double yield)
    {
        string benefitSign =
            benefit >= 0
                ? "+"
                : "";

        string yieldSign =
            yield >= 0
                ? "+"
                : "";

        return
            $"{benefitSign}{benefit:N0} K " +
            $"({yieldSign}{yield:P0})";
    }

    public void ShowServerSelectionRequired()
    {
        _readStatus.Text =
            "⚠ Sélectionnez d'abord un serveur dans BestCrush";

        _readStatus.TextColor =
            Colors.Orange;

        _item.Text =
            "Serveur non sélectionné";

        _details.Text =
            "Les captures F8 sont désactivées tant qu'un serveur " +
            "n'a pas été sélectionné dans BestCrush.";

        _footer.Text =
            "Aucune capture effectuée";

        _footer.TextColor =
            Colors.Orange;
    }

    public void ShowEquipmentRecognitionFailed(
        string recognizedText)
    {

        ShowNormalDetails();

        _item.Text =
            string.IsNullOrWhiteSpace(recognizedText)
                ? "Équipement non reconnu"
                : recognizedText;

        _details.Text =
            "BestCrush n'a pas pu associer cette lecture OCR " +
            "avec suffisamment de certitude à un équipement DofusDB.";

        _footer.Text =
            "⚠ Aucune donnée enregistrée";

        _footer.TextColor =
            Colors.Orange;
    }

    public void ShowServerNotSelected()
    {
        _footer.Text =
            "⚠ Serveur BestCrush non défini — coefficient non enregistré";

        _footer.TextColor =
            Colors.Orange;
    }
    public void ShowCaptureStarted(
        DofusWindowInfo window)
    {
        _readStatus.Text =
            $"F8 — capture de Dofus ({window.Width}×{window.Height})...";

        _readStatus.TextColor =
            Colors.LightBlue;
    }

    public void ShowCaptureSuccess(
        DofusCaptureResult capture)
    {
        _readStatus.Text =
            $"✓ Capture réussie — {capture.Width}×{capture.Height}";

        _readStatus.TextColor =
            Colors.LightGreen;
    }

    public void ShowPanelDetected(
        DofusPanelDetectionResult panel)
    {
        _readStatus.Text =
            $"✓ Concassage détecté — confiance {panel.Confidence:P0}";

        _readStatus.TextColor =
            Colors.LightGreen;
    }

    public void ShowPanelNotDetected()
    {
        _readStatus.Text =
            "F8 — aucun panneau reconnu";

        _readStatus.TextColor =
            Colors.Orange;
    }

    public void ShowCaptureFailed(
        string message)
    {
        ShowNormalDetails();

        _readStatus.Text =
            $"⚠ Lecture impossible — {message}";

        _readStatus.TextColor =
            Colors.Orange;

        _item.Text =
            "Lecture interrompue";

        _details.Text =
            "La lecture n'a pas produit de donnée fiable.";

        _footer.Text =
            "Aucune donnée enregistrée";

        _footer.TextColor =
            Colors.Orange;
    }

    public void ShowAuxiliaryMarketDataRecorded(
        string objectName,
        int lotCount,
        string? focusedEquipmentName)
    {
        _readStatus.Text =
            $"✓ {objectName} mis à jour";

        _readStatus.TextColor =
            Colors.LightGreen;

        _footer.Text =
            $"✓ {lotCount} lot(s) enregistré(s)";

        _footer.TextColor =
            Colors.LightGreen;

        // Très important :
        // si un équipement est déjà en focus,
        // on ne touche PAS à _item ni _details.
        if (!string.IsNullOrWhiteSpace(
            focusedEquipmentName))
        {
            ShowNormalDetails();
            return;
        }

        _item.Text =
            "Aucun équipement en focus";

        _details.Text =
            $"Les prix de {objectName} ont été enregistrés, " +
            "mais aucun équipement à concasser n'est actuellement sélectionné.";
    }

    public void ShowAuxiliaryMarketReadFailed(
        string objectName)
    {
        _readStatus.Text =
            $"⚠ Lecture incomplète : {objectName}";

        _readStatus.TextColor =
            Colors.Orange;

        _footer.Text =
            "Aucune donnée enregistrée";

        _footer.TextColor =
            Colors.Orange;
    }

    public void ShowReadCancelled()
    {
        _readStatus.Text =
            "F8 — Dofus non détecté, lecture annulée";

        _readStatus.TextColor =
            Colors.Orange;
    }

    public void ShowLastCrushRowDetected(
        CrushRowDetectionResult row)
    {
        _readStatus.Text =
            $"✓ Dernière ligne de concassage détectée — Y={row.Y}";

        _readStatus.TextColor =
            Colors.LightGreen;
    }

    public void ShowCrushRowNotDetected()
    {
        _readStatus.Text =
            "⚠ Concassage détecté, mais aucune ligne renseignée trouvée";

        _readStatus.TextColor =
            Colors.Orange;
    }

    public void ShowCrushFieldsExtracted()
    {
        _readStatus.Text =
            "✓ Nom et coefficient isolés — prêts pour lecture";

        _readStatus.TextColor =
            Colors.LightGreen;
    }
    public void ShowCrushOcrResult(
        string itemName,
        double? coefficient)
    {
        string coefficientText =
            coefficient is null
                ? "non reconnu"
                : $"{coefficient:0.##} %";

        _readStatus.Text =
            $"✓ OCR : {itemName} — {coefficientText}";

        _readStatus.TextColor =
            coefficient is null ||
            string.IsNullOrWhiteSpace(itemName)
                ? Colors.Orange
                : Colors.LightGreen;
    }
}