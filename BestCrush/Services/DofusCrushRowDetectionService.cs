 using OpenCvSharp;

using CvRect = OpenCvSharp.Rect;

namespace BestCrush.Services;

public sealed class DofusCrushRowDetectionService
{
    public Task<CrushRowDetectionResult?> DetectLastRowAsync(
        string panelFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        using Mat gray = new();

        Cv2.CvtColor(
            source,
            gray,
            ColorConversionCodes.BGR2GRAY
        );

        // On ignore les bords du panneau.
        int horizontalMargin =
            Math.Max(
                10,
                (int)Math.Round(source.Width * 0.03)
            );

        int scanLeft = horizontalMargin;

        int scanWidth =
            source.Width - (horizontalMargin * 2);

        // On ignore le titre / en-têtes en haut
        // ainsi que les valeurs et boutons du bas.
        int scanTop =
            Math.Max(
                60,
                (int)Math.Round(source.Height * 0.08)
            );

        int bottomMargin =
            Math.Max(
                100,
                (int)Math.Round(source.Height * 0.12)
            );

        int scanBottom =
            source.Height - bottomMargin;

        if (scanWidth <= 0 ||
            scanBottom <= scanTop)
        {
            return Task.FromResult<CrushRowDetectionResult?>(
                null
            );
        }

        List<RowBrightness> brightness = [];

        for (int y = scanTop; y < scanBottom; y++)
        {
            using Mat row =
                new(
                    gray,
                    new CvRect(
                        scanLeft,
                        y,
                        scanWidth,
                        1
                    )
                );

            double mean =
                Cv2.Mean(row).Val0;

            brightness.Add(
                new RowBrightness(
                    y,
                    mean
                )
            );
        }

        if (brightness.Count == 0)
        {
            return Task.FromResult<CrushRowDetectionResult?>(
                null
            );
        }

        // Le fond vide constitue la majorité du panneau :
        // sa médiane nous sert donc de référence adaptative.
        double[] sortedMeans =
            brightness
                .Select(row => row.Mean)
                .OrderBy(value => value)
                .ToArray();

        double backgroundMean =
            sortedMeans[sortedMeans.Length / 2];

        // Les rectangles contenant les résultats sont
        // sensiblement plus clairs que le fond du panneau.
        double threshold =
            backgroundMean + 8.0;

        List<RowSegment> candidates = [];

        int? segmentStart = null;

        foreach (RowBrightness row in brightness)
        {
            bool insideResultRow =
                row.Mean >= threshold;

            if (insideResultRow &&
                segmentStart is null)
            {
                segmentStart = row.Y;
            }
            else if (!insideResultRow &&
                     segmentStart is not null)
            {
                AddCandidate(
                    candidates,
                    segmentStart.Value,
                    row.Y - 1
                );

                segmentStart = null;
            }
        }

        if (segmentStart is not null)
        {
            AddCandidate(
                candidates,
                segmentStart.Value,
                scanBottom - 1
            );
        }

        RowSegment? lastRow =
            candidates
                .OrderByDescending(row => row.Top)
                .FirstOrDefault();

        if (lastRow is null)
        {
            return Task.FromResult<CrushRowDetectionResult?>(
                null
            );
        }

        // Quelques pixels supplémentaires pour ne pas couper
        // les bords du rectangle.
        int cropTop =
            Math.Max(
                0,
                lastRow.Top - 2
            );

        int cropBottom =
            Math.Min(
                source.Height,
                lastRow.Bottom + 3
            );

        CvRect cropRect =
            new(
                horizontalMargin,
                cropTop,
                source.Width - (horizontalMargin * 2),
                cropBottom - cropTop
            );

        using Mat lastRowImage =
            new(
                source,
                cropRect
            );

        string directory =
            Path.GetDirectoryName(
                panelFilePath
            )!;

        string outputPath =
            Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(panelFilePath)}-last-crush-row.png"
            );

        Cv2.ImWrite(
            outputPath,
            lastRowImage
        );

        CrushRowDetectionResult result =
            new(
                cropRect.X,
                cropRect.Y,
                cropRect.Width,
                cropRect.Height,
                outputPath,
                backgroundMean,
                threshold
            );

        return Task.FromResult<CrushRowDetectionResult?>(
            result
        );
    }

    private static void AddCandidate(
        ICollection<RowSegment> candidates,
        int top,
        int bottom)
    {
        int height =
            bottom - top + 1;

        // Les lignes de résultats observées font environ
        // 50-55 px. On conserve volontairement une marge.
        if (height < 30 ||
            height > 80)
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

    private sealed record RowBrightness(
        int Y,
        double Mean
    );

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