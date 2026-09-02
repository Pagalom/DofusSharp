using System.Globalization;
using System.Text;

using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;

namespace BestCrush.Services;

public sealed class DofusPanelDetectionService(
    DofusOcrService ocrService)
{
    private const double
        CrushHeaderMinimumConfidence = 0.85;

    // Corps sombre du panneau.
    // RGB ≈ 22,24,41 -> BGR avec OpenCV.
    private static readonly Scalar
        CrushBodyLower =
            new(
                34,
                18,
                16
            );

    private static readonly Scalar
        CrushBodyUpper =
            new(
                52,
                34,
                32
            );

    // Fond des lignes de résultat.
    // RGB ≈ 41,44,76 -> BGR avec OpenCV.
    private static readonly Scalar
        ResultRowLower =
            new(
                60,
                32,
                30
            );

    private static readonly Scalar
        ResultRowUpper =
            new(
                95,
                65,
                63
            );

    private const double
        ReferencePanelWidth = 720.0;

    private const double
        ReferencePanelHeight = 790.0;

    private const double
        ReferenceBodyTopOffset = 58.0;

    private const double
        ReferenceHorizontalMarginRatio = 0.03;

    public async Task<DofusPanelDetectionResult?>
        DetectCrushResultPanelAsync(
            string sourceFilePath,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        using Mat source =
            Cv2.ImRead(
                sourceFilePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            throw new InvalidOperationException(
                "Impossible de charger la capture Dofus."
            );
        }

        CvRect? panelRect =
            await TryDetectWithHeaderTemplateAsync(
                source,
                cancellationToken
            );

        bool usedGeometricFallback =
            panelRect is null;

        if (panelRect is null)
        {
            panelRect =
                DetectFromGeometry(
                    source
                );

            if (panelRect is null)
            {
                return null;
            }

            // Une géométrie compatible ne suffit pas :
            // l'HDV utilise les mêmes couleurs et peut
            // avoir des rectangles de dimensions proches.
            //
            // Le fallback doit donc être confirmé par
            // du texte propre au résultat de concassage.
            bool semanticallyConfirmed =
                await ValidateGeometricCrushPanelAsync(
                    source,
                    panelRect.Value,
                    sourceFilePath,
                    cancellationToken
                );

            if (!semanticallyConfirmed)
            {
                return null;
            }
        }

        if (panelRect.Value.Width <= 0 ||
            panelRect.Value.Height <= 0)
        {
            return null;
        }

        // Conserver le rectangle LOGIQUE du panneau.
        //
        // Contrairement à un crop classique, ce rectangle peut
        // partiellement sortir de la capture. Ses dimensions et
        // son origine ne doivent jamais être modifiées par le
        // clipping, sinon tous les calculs relatifs F9 changent
        // quand le panneau est déplacé près d'un bord.
        CvRect logicalPanelRect =
            panelRect.Value;

        using Mat panel =
            CreateCanonicalPanel(
                source,
                logicalPanelRect.X,
                logicalPanelRect.Y,
                logicalPanelRect.Width,
                logicalPanelRect.Height
            );

        if (panel.Empty())
        {
            return null;
        }

        string directory =
            Path.GetDirectoryName(
                sourceFilePath
            )!;

        string outputPath =
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(sourceFilePath)}-crush-panel.png"
            );

        Cv2.ImWrite(
            outputPath,
            panel
        );

        return new DofusPanelDetectionResult(
            DofusPanelType.CrushResult,
            usedGeometricFallback
                ? 0.75
                : 1.0,
            logicalPanelRect.X,
            logicalPanelRect.Y,
            logicalPanelRect.Width,
            logicalPanelRect.Height,
            outputPath
        );
    }

    private async Task<bool>
        ValidateGeometricCrushPanelAsync(
            Mat source,
            CvRect panelRect,
            string sourceFilePath,
            CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        // Le titre et les en-têtes se trouvent dans
        // les ~20 % supérieurs du panneau.
        int validationHeight =
            Math.Min(
                panelRect.Height,
                Math.Max(
                    100,
                    (int)Math.Round(
                        panelRect.Height * 0.20
                    )
                )
            );

        CvRect validationRect =
            ClampRectangle(
                panelRect.X,
                panelRect.Y,
                panelRect.Width,
                validationHeight,
                source.Width,
                source.Height
            );

        if (validationRect.Width <= 0 ||
            validationRect.Height <= 0)
        {
            return false;
        }

        using Mat validationImage =
            new(
                source,
                validationRect
            );

        string directory =
            Path.GetDirectoryName(
                sourceFilePath
            )
            ?? Path.GetTempPath();

        string validationPath =
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(sourceFilePath)}-crush-validation-header.png"
            );

        Cv2.ImWrite(
            validationPath,
            validationImage
        );

        string recognizedText =
            await ocrService
                .RecognizeUpscaledTextAsync(
                    validationPath
                );

        string normalized =
            NormalizeForSemanticCheck(
                recognizedText
            );

        bool hasCrushTitle =
            normalized.Contains(
                "concas",
                StringComparison.Ordinal
            );

        bool hasCoefficient =
            normalized.Contains(
                "coefficient",
                StringComparison.Ordinal
            ) ||
            normalized.Contains(
                "coeff",
                StringComparison.Ordinal
            );

        bool hasRunes =
            normalized.Contains(
                "rune",
                StringComparison.Ordinal
            );

        // Soit le titre du panneau est reconnu,
        // soit les deux en-têtes les plus spécifiques
        // confirment indépendamment le contexte.
        return hasCrushTitle ||
            (hasCoefficient && hasRunes);
    }

    private static string
        NormalizeForSemanticCheck(
            string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decomposed =
            value
                .Trim()
                .ToLowerInvariant()
                .Normalize(
                    NormalizationForm.FormD
                );

        StringBuilder builder =
            new();

        bool previousWasSpace =
            false;

        foreach (char character in decomposed)
        {
            UnicodeCategory category =
                CharUnicodeInfo
                    .GetUnicodeCategory(
                        character
                    );

            if (category ==
                UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            previousWasSpace = false;
            builder.Append(character);
        }

        return builder
            .ToString()
            .Normalize(
                NormalizationForm.FormC
            );
    }

    private static async Task<CvRect?>
        TryDetectWithHeaderTemplateAsync(
            Mat source,
            CancellationToken cancellationToken)
    {
        try
        {
            byte[] templateBytes;

            await using (
                Stream templateStream =
                    await FileSystem
                        .OpenAppPackageFileAsync(
                            "crush-result-header.png"
                        ))
            {
                using MemoryStream memoryStream =
                    new();

                await templateStream
                    .CopyToAsync(
                        memoryStream,
                        cancellationToken
                    );

                templateBytes =
                    memoryStream
                        .ToArray();
            }

            using Mat template =
                Cv2.ImDecode(
                    templateBytes,
                    ImreadModes.Color
                );

            if (template.Empty() ||
                source.Width <
                    template.Width ||
                source.Height <
                    template.Height)
            {
                return null;
            }

            using Mat result =
                new();

            Cv2.MatchTemplate(
                source,
                template,
                result,
                TemplateMatchModes
                    .CCoeffNormed
            );

            Cv2.MinMaxLoc(
                result,
                out _,
                out double maxValue,
                out _,
                out CvPoint maxLocation
            );

            if (maxValue <
                CrushHeaderMinimumConfidence)
            {
                return null;
            }

            int panelX =
                maxLocation.X -
                200;

            int panelY =
                maxLocation.Y +
                2;

            // Ne PAS clamper ici.
            //
            // panelX/panelY représentent l'origine logique du
            // panneau dans la capture, même si elle est négative.
            // Le canvas canonique créé plus tard conservera ce
            // repère 720x790 et remplira uniquement les zones
            // réellement visibles.
            return new CvRect(
                panelX,
                panelY,
                720,
                790
            );
        }
        catch (
            OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static CvRect?
        DetectFromGeometry(
            Mat source)
    {
        CvRect? body =
            DetectPanelBody(
                source
            );

        if (body is null)
        {
            return null;
        }

        RowGeometry? rowGeometry =
            DetectResultRowGeometry(
                source
            );

        int panelWidth;
        int panelX;

        if (rowGeometry is not null)
        {
            panelWidth =
                SolvePanelWidthFromContentWidth(
                    rowGeometry.Width
                );

            int horizontalMargin =
                Math.Max(
                    10,
                    (int)Math.Round(
                        panelWidth *
                        ReferenceHorizontalMarginRatio
                    )
                );

            panelX =
                rowGeometry.X -
                horizontalMargin;
        }
        else
        {
            panelWidth =
                (int)Math.Round(
                    ReferencePanelWidth
                );

            panelX =
                body.Value.X;
        }

        double scale =
            panelWidth /
            ReferencePanelWidth;

        int panelY =
            body.Value.Y -
            (int)Math.Round(
                ReferenceBodyTopOffset *
                scale
            );

        int panelHeight =
            (int)Math.Round(
                ReferencePanelHeight *
                scale
            );

        // Même règle que pour la détection par template :
        // ce rectangle décrit le panneau LOGIQUE complet.
        // Le fait qu'une partie soit hors capture ne doit jamais
        // modifier sa largeur, sa hauteur ni son origine.
        CvRect panel =
            new(
                panelX,
                panelY,
                panelWidth,
                panelHeight
            );

        return panel.Width > 0 &&
            panel.Height > 0
                ? panel
                : null;
    }

    private static CvRect?
        DetectPanelBody(
            Mat source)
    {
        using Mat mask =
            new();

        Cv2.InRange(
            source,
            CrushBodyLower,
            CrushBodyUpper,
            mask
        );

        using Mat kernel =
            Cv2.GetStructuringElement(
                MorphShapes.Rect,
                new OpenCvSharp.Size(
                    9,
                    9
                )
            );

        Cv2.MorphologyEx(
            mask,
            mask,
            MorphTypes.Close,
            kernel,
            iterations: 2
        );

        Cv2.FindContours(
            mask,
            out OpenCvSharp.Point[][]
                contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes
                .ApproxSimple
        );

        return contours
            .Select(
                Cv2.BoundingRect
            )
            .Where(
                rectangle =>
                    rectangle.Width >= 550 &&
                    rectangle.Width <= 950 &&
                    rectangle.Height >= 350 &&
                    rectangle.Height <= 1000
            )
            .Where(
                rectangle =>
                {
                    double ratio =
                        rectangle.Width /
                        (double)
                            rectangle.Height;

                    return ratio >= 0.65 &&
                        ratio <= 1.8;
                }
            )
            .OrderByDescending(
                rectangle =>
                    rectangle.Width *
                    rectangle.Height
            )
            .Cast<CvRect?>()
            .FirstOrDefault();
    }

    private static RowGeometry?
        DetectResultRowGeometry(
            Mat source)
    {
        using Mat mask =
            new();

        Cv2.InRange(
            source,
            ResultRowLower,
            ResultRowUpper,
            mask
        );

        Cv2.FindContours(
            mask,
            out OpenCvSharp.Point[][]
                contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes
                .ApproxSimple
        );

        List<CvRect> rowRectangles =
            contours
                .Select(
                    Cv2.BoundingRect
                )
                .Where(
                    rectangle =>
                        rectangle.Width >= 450 &&
                        rectangle.Width <= 850 &&
                        rectangle.Height >= 20 &&
                        rectangle.Height <= 220
                )
                .Where(
                    rectangle =>
                        rectangle.Width /
                        (double)
                            rectangle.Height >=
                        3.0
                )
                .ToList();

        if (rowRectangles.Count == 0)
        {
            return null;
        }

        var bestGroup =
            rowRectangles
                .GroupBy(
                    rectangle =>
                        (
                            X:
                                (int)Math.Round(
                                    rectangle.X /
                                    5.0
                                ),
                            Width:
                                (int)Math.Round(
                                    rectangle.Width /
                                    5.0
                                )
                        )
                )
                .OrderByDescending(
                    group =>
                        group.Count()
                )
                .First();

        List<CvRect> rows =
            bestGroup
                .ToList();

        if (rows.Count < 2)
        {
            return null;
        }

        int x =
            (int)Math.Round(
                rows.Average(
                    rectangle =>
                        rectangle.X
                )
            );

        int width =
            (int)Math.Round(
                rows.Average(
                    rectangle =>
                        rectangle.Width
                )
            );

        return new RowGeometry(
            x,
            width
        );
    }

    private static int
        SolvePanelWidthFromContentWidth(
            int contentWidth)
    {
        for (
            int panelWidth =
                contentWidth;
            panelWidth <=
                contentWidth + 120;
            panelWidth++)
        {
            int margin =
                Math.Max(
                    10,
                    (int)Math.Round(
                        panelWidth *
                        ReferenceHorizontalMarginRatio
                    )
                );

            if (panelWidth -
                (2 * margin) ==
                contentWidth)
            {
                return panelWidth;
            }
        }

        return (int)Math.Round(
            contentWidth /
            (
                1.0 -
                2.0 *
                ReferenceHorizontalMarginRatio
            )
        );
    }

    private static Mat CreateCanonicalPanel(
        Mat source,
        int desiredX,
        int desiredY,
        int desiredWidth,
        int desiredHeight)
    {
        if (desiredWidth <= 0 ||
            desiredHeight <= 0)
        {
            return new Mat();
        }

        int sourceLeft =
            Math.Max(
                0,
                desiredX
            );

        int sourceTop =
            Math.Max(
                0,
                desiredY
            );

        int sourceRight =
            Math.Min(
                source.Width,
                desiredX +
                desiredWidth
            );

        int sourceBottom =
            Math.Min(
                source.Height,
                desiredY +
                desiredHeight
            );

        int copyWidth =
            sourceRight -
            sourceLeft;

        int copyHeight =
            sourceBottom -
            sourceTop;

        if (copyWidth <= 0 ||
            copyHeight <= 0)
        {
            return new Mat();
        }

        // Le canvas conserve TOUJOURS les dimensions logiques
        // du panneau détecté. Les zones hors capture restent
        // noires au lieu de réduire/décaler le crop.
        Mat canonical =
            new(
                desiredHeight,
                desiredWidth,
                source.Type(),
                Scalar.All(0)
            );

        int destinationX =
            sourceLeft -
            desiredX;

        int destinationY =
            sourceTop -
            desiredY;

        CvRect sourceRect =
            new(
                sourceLeft,
                sourceTop,
                copyWidth,
                copyHeight
            );

        CvRect destinationRect =
            new(
                destinationX,
                destinationY,
                copyWidth,
                copyHeight
            );

        using Mat sourceRegion =
            new(
                source,
                sourceRect
            );

        using Mat destinationRegion =
            new(
                canonical,
                destinationRect
            );

        sourceRegion.CopyTo(
            destinationRegion
        );

        return canonical;
    }

    private static CvRect
        ClampRectangle(
            int x,
            int y,
            int width,
            int height,
            int imageWidth,
            int imageHeight)
    {
        int left =
            Math.Max(
                0,
                x
            );

        int top =
            Math.Max(
                0,
                y
            );

        int right =
            Math.Min(
                imageWidth,
                x + width
            );

        int bottom =
            Math.Min(
                imageHeight,
                y + height
            );

        return new CvRect(
            left,
            top,
            Math.Max(
                0,
                right - left
            ),
            Math.Max(
                0,
                bottom - top
            )
        );
    }

    private sealed record RowGeometry(
        int X,
        int Width
    );
}

public enum DofusPanelType
{
    CrushResult
}

public sealed record DofusPanelDetectionResult(
    DofusPanelType Type,
    double Confidence,
    int X,
    int Y,
    int Width,
    int Height,
    string DebugImagePath
);
