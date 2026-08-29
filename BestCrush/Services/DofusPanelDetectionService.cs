using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;

namespace BestCrush.Services;

public sealed class DofusPanelDetectionService
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

        panelRect ??=
            DetectFromGeometry(
                source
            );

        if (panelRect is null ||
            panelRect.Value.Width <= 0 ||
            panelRect.Value.Height <= 0)
        {
            return null;
        }

        CvRect finalRect =
            panelRect.Value;

        using Mat panel =
            new(
                source,
                finalRect
            );

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
            finalRect.X,
            finalRect.Y,
            finalRect.Width,
            finalRect.Height,
            outputPath
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

            return ClampRectangle(
                panelX,
                panelY,
                720,
                790,
                source.Width,
                source.Height
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
            // Les lignes sont analysées par
            // DofusCrushRowDetectionService avec
            // 3 % de marge de chaque côté.
            //
            // On retrouve ici la largeur réelle
            // du panneau à partir de la largeur
            // observée du rectangle de résultat.
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
            // Secours du secours : on utilise
            // le grand corps sombre.
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

        CvRect panel =
            ClampRectangle(
                panelX,
                panelY,
                panelWidth,
                panelHeight,
                source.Width,
                source.Height
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

        // Plusieurs lignes du même panneau ont
        // pratiquement le même X et la même largeur.
        //
        // On choisit le groupe le plus représenté,
        // ce qui évite de prendre un autre élément
        // bleu de l'interface Dofus.
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
        // Recherche exacte autour de la largeur
        // attendue afin de respecter le même
        // Math.Round() que le détecteur de lignes.
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
