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

        string recognizedTitle =
            await ocrService
                .RecognizeUpscaledTextAsync(
                    titleImagePath
                );

        recognizedTitle =
            recognizedTitle
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
                recognizedTitle
            )
                ? null
                : await itemRecognitionService
                    .RecognizeEquipmentAsync(
                        recognizedTitle
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

    private static IReadOnlyList<CvRect>
        DetectTooltipHeaders(
            Mat capture)
    {
        using Mat darkMask = new();
        using Mat bodyMask = new();

        Cv2.InRange(
            capture,
            TooltipDarkLower,
            TooltipDarkUpper,
            darkMask
        );

        Cv2.InRange(
            capture,
            TooltipBodyLower,
            TooltipBodyUpper,
            bodyMask
        );

        List<ContourRectangle>
            darkRectangles =
                FindRectangles(
                    darkMask,
                    minimumFillRatio: 0.25
                );

        List<ContourRectangle>
            bodyRectangles =
                FindRectangles(
                    bodyMask,
                    minimumFillRatio: 0.75
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
                                verticalGap <= 15 &&

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
            double minimumFillRatio)
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
                rectangle.Height > 260)
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
