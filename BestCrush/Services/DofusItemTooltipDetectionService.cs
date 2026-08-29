using OpenCvSharp;

using Rect = OpenCvSharp.Rect;
using Point = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace BestCrush.Services;

public sealed record DofusItemTooltipCandidate(
    string RecognizedTitle,
    ItemRecognitionResult? Recognition,
    double X,
    double Y
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
                                    rectangle.Y
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
                    header.Y
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

            // Une vraie partie supérieure
            // d'infobulle est beaucoup plus
            // large que haute.
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

        // Sécurité contre une éventuelle
        // double détection du même panneau.
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

            // Plage volontairement large
            // pour supporter différents
            // facteurs d'échelle de l'UI.
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
        // Le nom se trouve dans le coin
        // supérieur gauche.
        //
        // On laisse volontairement la partie
        // droite de côté pour ne pas capturer
        // l'image de l'objet.
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

        x =
            Math.Clamp(
                x,
                0,
                capture.Width - 1
            );

        y =
            Math.Clamp(
                y,
                0,
                capture.Height - 1
            );

        width =
            Math.Min(
                width,
                capture.Width - x
            );

        height =
            Math.Min(
                height,
                capture.Height - y
            );

        CvRect titleRectangle =
            new(
                x,
                y,
                width,
                height
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

    private sealed record ContourRectangle(
        CvRect Rectangle,
        double FillRatio
    );
}