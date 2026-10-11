using OpenCvSharp;

using Rect = OpenCvSharp.Rect;
using Point = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace BestCrush.Services;

public sealed record DofusItemTooltipCandidate(
    string RecognizedTitle,
    ItemRecognitionResult? Recognition,
    double X,
    double Y,
    int? LotQuantity
);

public sealed record DofusItemTooltipDetectionResult(
    IReadOnlyList<DofusItemTooltipCandidate> Candidates
);

public sealed class DofusItemTooltipDetectionService(
    DofusOcrService ocrService,
    DofusItemRecognitionService itemRecognitionService)
{
    // Couleurs caractéristiques des infobulles Dofus.
    //
    // RGB :
    // couche sombre : 20, 22, 37
    // couche centrale : 27, 29, 50
    //
    // OpenCV travaille en BGR.
    private static readonly Scalar TooltipDarkLower =
        new(34, 19, 17);

    private static readonly Scalar TooltipDarkUpper =
        new(40, 25, 23);

    private static readonly Scalar TooltipBodyLower =
        new(47, 26, 24);

    private static readonly Scalar TooltipBodyUpper =
        new(53, 32, 30);

    // The darker item-tooltip palette in newer Dofus UI builds is
    // different from the legacy header/body palette above (BGR).
    // Keep the existing detector first to preserve known-good captures.
    private static readonly Scalar CurrentTooltipHeaderLower =
        new(45, 25, 23);

    private static readonly Scalar CurrentTooltipHeaderUpper =
        new(65, 41, 42);

    private static readonly Scalar CurrentTooltipBodyLower =
        new(49, 29, 27);

    private static readonly Scalar CurrentTooltipBodyUpper =
        new(59, 40, 41);

    public async Task<DofusItemTooltipDetectionResult>
        DetectAsync(
            string captureFilePath,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using Mat capture =
            Cv2.ImRead(
                captureFilePath,
                ImreadModes.Color
            );

        if (capture.Empty())
        {
            return new DofusItemTooltipDetectionResult(
                []
            );
        }

        IReadOnlyList<CvRect>
            tooltipHeaders =
                DetectTooltipHeaders(
                    capture
                );

        if (tooltipHeaders.Count == 0)
        {
            return new DofusItemTooltipDetectionResult(
                []
            );
        }

        // Plusieurs infobulles :
        // inutile de faire le moindre OCR.
        if (tooltipHeaders.Count > 1)
        {
            IReadOnlyList<DofusItemTooltipCandidate>
                multipleCandidates =
                    tooltipHeaders
                        .Select(
                            rectangle =>
                                new DofusItemTooltipCandidate(
                                    string.Empty,
                                    null,
                                    rectangle.X,
                                    rectangle.Y,
                                    null
                                )
                        )
                        .ToList();

            return new DofusItemTooltipDetectionResult(
                multipleCandidates
            );
        }

        CvRect header =
            tooltipHeaders[0];

        string titleImagePath =
            ExtractTitleRegion(
                capture,
                captureFilePath,
                header
            );

        string recognizedTitleText =
            await ocrService
                .RecognizeUpscaledTextAsync(
                    titleImagePath
                );

        WriteRecognizedTitleDebug(
            captureFilePath,
            recognizedTitleText
        );

        string recognizedTitle =
            recognizedTitleText
                .Split(
                    ['\r', '\n'],
                    StringSplitOptions
                        .RemoveEmptyEntries
                )
                .Select(line =>
                    line.Trim())
                .FirstOrDefault()
            ?? string.Empty;

        string lotRegionPath =
            ExtractLotQuantityRegion(
                capture,
                captureFilePath,
                header
            );

        int? lotQuantity =
            await ocrService
                .RecognizeTooltipLotQuantityAsync(
                    lotRegionPath,
                    cancellationToken
                );

        ItemRecognitionResult? recognition =
            string.IsNullOrWhiteSpace(
                recognizedTitleText
            )
                ? null
                : await itemRecognitionService
                    .RecognizeEquipmentAsync(
                        recognizedTitleText
                    );

        return new DofusItemTooltipDetectionResult(
            [
                new DofusItemTooltipCandidate(
                    recognizedTitle,
                    recognition,
                    header.X,
                    header.Y,
                    lotQuantity
                )
            ]
        );
    }

    // Internal seam so geometry/palette regression tests do not need OCR
    // or any image from an actual player session.
    internal static IReadOnlyList<CvRect>
        DetectTooltipHeaders(
            Mat capture)
    {
        IReadOnlyList<CvRect> legacy =
            DetectTooltipHeadersByPalette(
                capture,
                TooltipDarkLower,
                TooltipDarkUpper,
                TooltipBodyLower,
                TooltipBodyUpper,
                allowSeparatedLongBody: false
            );

        if (legacy.Count > 0)
            return legacy;

        IReadOnlyList<CvRect> current = DetectTooltipHeadersByPalette(
            capture,
            CurrentTooltipHeaderLower,
            CurrentTooltipHeaderUpper,
            CurrentTooltipBodyLower,
            CurrentTooltipBodyUpper,
            allowSeparatedLongBody: true
        );

        // Some modern tooltips blend their title background into adjoining
        // UI surfaces, so the header contour becomes wider than the tooltip
        // and is rejected. Recover the title header from the aligned body
        // palette instead of mistaking a lower STATUS bar for the item title.
        return current.Count > 0
            ? current
            : DetectTooltipHeadersFromModernBody(capture);
    }

    // Final geometry fallback for modern tooltips whose header contour
    // merges with neighboring panels. A dense body-colored rectangle
    // anchors a backwards scan for the header-colored title band.
    private static IReadOnlyList<CvRect>
        DetectTooltipHeadersFromModernBody(Mat capture)
    {
        using Mat headerMask = new();
        using Mat bodyMask = new();

        Cv2.InRange(
            capture,
            CurrentTooltipHeaderLower,
            CurrentTooltipHeaderUpper,
            headerMask);
        Cv2.InRange(
            capture,
            CurrentTooltipBodyLower,
            CurrentTooltipBodyUpper,
            bodyMask);

        List<ContourRectangle> bodies = FindRectangles(
            bodyMask,
            minimumFillRatio: 0.75,
            maximumHeight: 900);

        List<CvRect> candidates = [];

        foreach (ContourRectangle entry in bodies)
        {
            CvRect body = entry.Rectangle;

            // Ignore small isolated bars and require room for a title
            // band above the body. The normal detector handles short
            // conventional tooltips first.
            if (body.Height < 100 || body.Y < 60)
                continue;

            int firstMatchingRow = -1;
            int lastMatchingRow = -1;
            int misses = 0;
            int scanFloor = Math.Max(0, body.Y - 200);

            for (int y = body.Y - 1; y >= scanFloor; y--)
            {
                // Text and item artwork mostly occupy the left half.
                // Sample the right half to find a uniform title panel.
                int samples = 0;
                int matches = 0;
                int right = Math.Min(capture.Width, body.X + body.Width);
                int left = Math.Max(0, body.X + body.Width / 2);

                for (int x = left; x < right; x += 4)
                {
                    samples++;
                    if (headerMask.At<byte>(y, x) != 0)
                        matches++;
                }

                bool titleBackground =
                    samples > 0 && matches * 100 >= samples * 45;

                if (titleBackground)
                {
                    if (lastMatchingRow < 0)
                        lastMatchingRow = y;

                    firstMatchingRow = y;
                    misses = 0;
                }
                else if (lastMatchingRow < 0)
                {
                    if (body.Y - 1 - y > 12)
                        break;
                }
                else if (++misses > 4)
                {
                    break;
                }
            }

            if (firstMatchingRow < 0 || lastMatchingRow < 0)
                continue;

            int height = lastMatchingRow - firstMatchingRow + 1;
            int gap = body.Y - 1 - lastMatchingRow;

            // A broad real header with a short separator is required;
            // arbitrarily dark panels must not create OCR candidates.
            if (height < 60 || height > 180 ||
                gap > 12 || (double)body.Width / height < 1.8)
                continue;

            CvRect header = new(
                body.X,
                firstMatchingRow,
                body.Width,
                height);

            if (!candidates.Any(existing =>
                    Math.Abs(existing.X - header.X) < 30 &&
                    Math.Abs(existing.Y - header.Y) < 30))
            {
                candidates.Add(header);
            }
        }

        return candidates;
    }

    private static IReadOnlyList<CvRect>
        DetectTooltipHeadersByPalette(
            Mat capture,
            Scalar headerLower,
            Scalar headerUpper,
            Scalar bodyLower,
            Scalar bodyUpper,
            bool allowSeparatedLongBody)
    {
        using Mat darkMask = new();
        using Mat bodyMask = new();

        Cv2.InRange(
            capture,
            headerLower,
            headerUpper,
            darkMask
        );

        Cv2.InRange(
            capture,
            bodyLower,
            bodyUpper,
            bodyMask
        );

        List<ContourRectangle>
            darkRectangles =
                FindRectangles(
                    darkMask,
                    minimumFillRatio: 0.25,
                    maximumHeight: 300
                );

        List<ContourRectangle>
            bodyRectangles =
                FindRectangles(
                    bodyMask,
                    minimumFillRatio: 0.75,
                    maximumHeight: 900
                );

        List<Rect> candidates = [];

        foreach (
            ContourRectangle dark
            in darkRectangles)
        {
            CvRect header =
                dark.Rectangle;

            if ((double)header.Width /
                    header.Height <
                1.8)
            {
                continue;
            }

            ContourRectangle? matchingBody =
                bodyRectangles
                    .FirstOrDefault(
                        body =>
                        {
                            CvRect rectangle =
                                body.Rectangle;

                            int verticalGap =
                                rectangle.Y -
                                (
                                    header.Y +
                                    header.Height
                                );

                            return
                                Math.Abs(
                                    rectangle.X -
                                    header.X
                                ) <= 15 &&

                                Math.Abs(
                                    rectangle.Width -
                                    header.Width
                                ) <= 20 &&

                                verticalGap >= -5 &&
                                (
                                    verticalGap <= 15 ||
                                    // In long modern tooltips, the internal
                                    // sections interrupt the body-color mask.
                                    // The remaining uniform lower body can
                                    // start much farther below the header.
                                    // Require a long, aligned body so an
                                    // unrelated panel is not accepted.
                                    (
                                        allowSeparatedLongBody &&
                                        verticalGap <= 220 &&
                                        rectangle.Height >= 300
                                    )
                                ) &&

                                rectangle.Height >= 50;
                        }
                    );

            if (matchingBody is null)
            {
                continue;
            }

            candidates.Add(
                header
            );
        }

        List<Rect> distinct = [];

        foreach (
            CvRect candidate
            in candidates
                .OrderBy(value => value.Y)
                .ThenBy(value => value.X))
        {
            bool alreadyPresent =
                distinct.Any(
                    existing =>
                        Math.Abs(
                            existing.X -
                            candidate.X
                        ) < 30 &&
                        Math.Abs(
                            existing.Y -
                            candidate.Y
                        ) < 30
                );

            if (!alreadyPresent)
            {
                distinct.Add(
                    candidate
                );
            }
        }

        return distinct;
    }

    private static List<ContourRectangle>
        FindRectangles(
            Mat mask,
            double minimumFillRatio,
            int maximumHeight)
    {
        Cv2.FindContours(
            mask,
            out Point[][] contours,
            out _,
            RetrievalModes.External,
            ContourApproximationModes
                .ApproxSimple
        );

        List<ContourRectangle>
            rectangles = [];

        foreach (
            Point[] contour
            in contours)
        {
            CvRect rectangle =
                Cv2.BoundingRect(
                    contour
                );

            if (rectangle.Width < 240 ||
                rectangle.Width > 600 ||
                rectangle.Height < 50 ||
                rectangle.Height > maximumHeight)
            {
                continue;
            }

            double area =
                Cv2.ContourArea(
                    contour
                );

            double rectangleArea =
                rectangle.Width *
                rectangle.Height;

            if (rectangleArea <= 0)
            {
                continue;
            }

            double fillRatio =
                area /
                rectangleArea;

            if (fillRatio <
                minimumFillRatio)
            {
                continue;
            }

            rectangles.Add(
                new ContourRectangle(
                    rectangle,
                    fillRatio
                )
            );
        }

        return rectangles;
    }

    private static string ExtractTitleRegion(
        Mat capture,
        string sourceFilePath,
        CvRect tooltipHeader)
    {
        int x =
            tooltipHeader.X + 15;

        int y =
            tooltipHeader.Y + 10;

        int width =
            tooltipHeader.Width - 105;

        int height =
            Math.Min(
                45,
                tooltipHeader.Height - 10
            );

        CvRect titleRectangle =
            ClampRectangle(
                x,
                y,
                width,
                height,
                capture.Width,
                capture.Height
            );

        using Mat title =
            new(
                capture,
                titleRectangle
            );

        string directory =
            Path.GetDirectoryName(
                sourceFilePath
            )
            ?? Path.GetTempPath();

        string outputPath =
            Path.Combine(
                directory,
                "tooltip-title.png"
            );

        Cv2.ImWrite(
            outputPath,
            title
        );

        return outputPath;
    }

    private static string ExtractLotQuantityRegion(
        Mat capture,
        string sourceFilePath,
        CvRect tooltipHeader)
    {
        // On commence juste sous le bandeau sombre
        // contenant le nom de la rune.
        //
        // La zone couvre notamment :
        // EFFET
        // POIDS 1 - LOT n
        // DENSITÉ ...
        //
        // Elle s'arrête volontairement avant le bas
        // de l'infobulle afin de ne pas confondre
        // "LOT n" avec le prix "- LOT xxx K".
        int x =
            tooltipHeader.X + 8;

        int y =
            tooltipHeader.Y +
            tooltipHeader.Height;

        int width =
            tooltipHeader.Width - 16;

        int height =
            210;

        CvRect lotRectangle =
            ClampRectangle(
                x,
                y,
                width,
                height,
                capture.Width,
                capture.Height
            );

        using Mat lotRegion =
            new(
                capture,
                lotRectangle
            );

        string directory =
            Path.GetDirectoryName(
                sourceFilePath
            )
            ?? Path.GetTempPath();

        string outputPath =
            Path.Combine(
                directory,
                "tooltip-lot-quantity.png"
            );

        Cv2.ImWrite(
            outputPath,
            lotRegion
        );

        return outputPath;
    }

    private static void WriteRecognizedTitleDebug(
        string sourceFilePath,
        string recognizedText)
    {
        try
        {
            string directory =
                Path.GetDirectoryName(
                    sourceFilePath
                )
                ?? Path.GetTempPath();

            File.WriteAllText(
                Path.Combine(
                    directory,
                    "tooltip-title-ocr.txt"
                ),
                recognizedText
            );
        }
        catch
        {
            // Un fichier de debug ne doit jamais
            // faire échouer une reconnaissance.
        }
    }

    private static CvRect ClampRectangle(
        int x,
        int y,
        int width,
        int height,
        int imageWidth,
        int imageHeight)
    {
        int left =
            Math.Clamp(
                x,
                0,
                imageWidth - 1
            );

        int top =
            Math.Clamp(
                y,
                0,
                imageHeight - 1
            );

        int right =
            Math.Clamp(
                x + Math.Max(1, width),
                left + 1,
                imageWidth
            );

        int bottom =
            Math.Clamp(
                y + Math.Max(1, height),
                top + 1,
                imageHeight
            );

        return new CvRect(
            left,
            top,
            right - left,
            bottom - top
        );
    }

    private sealed record ContourRectangle(
        CvRect Rectangle,
        double FillRatio
    );
}
