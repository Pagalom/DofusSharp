using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;

namespace BestCrush.Services;

public sealed class DofusCrushRowDetectionService
{
    // Couleur du fond des lignes du panneau
    // "Résultat du concassage".
    //
    // Valeur observée autour de :
    // RGB 41, 44, 76
    // OpenCV travaille en BGR.
    //
    // La plage est volontairement assez large
    // pour tolérer l'anti-aliasing et de légères
    // variations de rendu.
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
        MinimumRowColorRatio = 0.25;

    public async Task<CrushRowDetectionResult?>
        DetectLastRowAsync(
            string panelFilePath,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CrushRowDetectionResult> rows =
            await DetectRowsAsync(
                panelFilePath,
                cancellationToken
            );

        return rows
            .OrderByDescending(
                row => row.Y
            )
            .FirstOrDefault();
    }

    public Task<IReadOnlyList<CrushRowDetectionResult>>
        DetectRowsAsync(
            string panelFilePath,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        using Mat source =
            Cv2.ImRead(
                panelFilePath,
                ImreadModes.Color
            );

        if (source.Empty())
        {
            throw new InvalidOperationException(
                "Impossible de charger le panneau de concassage."
            );
        }

        int horizontalMargin =
            Math.Max(
                10,
                (int)Math.Round(
                    source.Width * 0.03
                )
            );

        int scanLeft =
            horizontalMargin;

        int scanWidth =
            source.Width -
            (horizontalMargin * 2);

        int scanTop =
            Math.Max(
                60,
                (int)Math.Round(
                    source.Height * 0.08
                )
            );

        int bottomMargin =
            Math.Max(
                100,
                (int)Math.Round(
                    source.Height * 0.12
                )
            );

        int scanBottom =
            source.Height -
            bottomMargin;

        if (scanWidth <= 0 ||
            scanBottom <= scanTop)
        {
            return Task.FromResult<
                IReadOnlyList<
                    CrushRowDetectionResult
                >
            >([]);
        }

        // On isole directement la couleur du
        // rectangle de fond des lignes.
        //
        // Cette approche est nettement plus fiable
        // que l'ancienne moyenne de luminosité :
        // les lignes Dofus sont bleu sombre et ne
        // sont pas forcément beaucoup plus claires
        // que le fond général du panneau.
        using Mat rowColorMask = new();

        Cv2.InRange(
            source,
            ResultRowLower,
            ResultRowUpper,
            rowColorMask
        );

        List<RowSegment> candidates = [];

        int? segmentStart = null;

        for (
            int y = scanTop;
            y < scanBottom;
            y++)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            using Mat maskRow =
                new(
                    rowColorMask,
                    new CvRect(
                        scanLeft,
                        y,
                        scanWidth,
                        1
                    )
                );

            int matchingPixels =
                Cv2.CountNonZero(
                    maskRow
                );

            double matchingRatio =
                matchingPixels /
                (double)scanWidth;

            bool insideResultRow =
                matchingRatio >=
                MinimumRowColorRatio;

            if (insideResultRow &&
                segmentStart is null)
            {
                segmentStart =
                    y;
            }
            else if (
                !insideResultRow &&
                segmentStart is not null)
            {
                AddCandidate(
                    candidates,
                    segmentStart.Value,
                    y - 1,
                    source.Height
                );

                segmentStart =
                    null;
            }
        }

        if (segmentStart is not null)
        {
            AddCandidate(
                candidates,
                segmentStart.Value,
                scanBottom - 1,
                source.Height
            );
        }

        string directory =
            Path.GetDirectoryName(
                panelFilePath
            )
            ?? Path.GetTempPath();

        string baseName =
            Path.GetFileNameWithoutExtension(
                panelFilePath
            );

        List<CrushRowDetectionResult>
            results = [];

        int rowIndex = 0;

        foreach (
            RowSegment candidate
            in candidates
                .OrderBy(
                    row => row.Top
                ))
        {
            int cropTop =
                Math.Max(
                    0,
                    candidate.Top - 2
                );

            int cropBottom =
                Math.Min(
                    source.Height,
                    candidate.Bottom + 3
                );

            CvRect cropRect =
                new(
                    horizontalMargin,
                    cropTop,
                    source.Width -
                        (horizontalMargin * 2),
                    cropBottom -
                        cropTop
                );

            if (cropRect.Width <= 0 ||
                cropRect.Height <= 0)
            {
                continue;
            }

            using Mat rowImage =
                new(
                    source,
                    cropRect
                );

            string outputPath =
                Path.Combine(
                    directory,
                    $"{baseName}-crush-row-{rowIndex++}.png"
                );

            Cv2.ImWrite(
                outputPath,
                rowImage
            );

            results.Add(
                new CrushRowDetectionResult(
                    cropRect.X,
                    cropRect.Y,
                    cropRect.Width,
                    cropRect.Height,
                    outputPath,
                    0,
                    MinimumRowColorRatio
                )
            );
        }

        return Task.FromResult<
            IReadOnlyList<
                CrushRowDetectionResult
            >
        >(results);
    }

    private static void AddCandidate(
        ICollection<RowSegment> candidates,
        int top,
        int bottom,
        int panelHeight)
    {
        int height =
            bottom -
            top +
            1;

        // Une ligne standard observée fait
        // environ 54 px.
        //
        // Une ligne avec plusieurs rangées de
        // runes peut être beaucoup plus haute.
        //
        // Les fragments inférieurs partiellement
        // masqués par le pied de panneau sont
        // volontairement ignorés.
        int maximumHeight =
            Math.Max(
                180,
                (int)Math.Round(
                    panelHeight * 0.45
                )
            );

        if (height < 30 ||
            height > maximumHeight)
        {
            return;
        }

        candidates.Add(
            new RowSegment(
                top,
                bottom
            )
        );
    }

    private sealed record RowSegment(
        int Top,
        int Bottom
    );
}

public sealed record CrushRowDetectionResult(
    int X,
    int Y,
    int Width,
    int Height,
    string DebugImagePath,
    double BackgroundBrightness,
    double DetectionThreshold
);
