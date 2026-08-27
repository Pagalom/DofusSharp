using OpenCvSharp;

using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;

namespace BestCrush.Services;

public sealed record DofusMarketPanelDetectionResult(
    string DebugImagePath,
    double Confidence,
    int X,
    int Y,
    int Width,
    int Height
);

public sealed class DofusMarketPanelDetectionService
{
    private const double DetectionThreshold = 0.85;

    public async Task<DofusMarketPanelDetectionResult?>
        DetectMarketPanelAsync(
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
            return null;
        }

        using Stream templateStream =
            await FileSystem.OpenAppPackageFileAsync(
                "hdv-offers-header.png"
            );

        using MemoryStream memory = new();

        await templateStream.CopyToAsync(
            memory,
            cancellationToken
        );

        byte[] templateBytes =
            memory.ToArray();

        using Mat template =
            Cv2.ImDecode(
                templateBytes,
                ImreadModes.Color
            );

        if (template.Empty())
        {
            return null;
        }

        if (capture.Width < template.Width ||
            capture.Height < template.Height)
        {
            return null;
        }

        using Mat captureGray = new();
        using Mat templateGray = new();

        Cv2.CvtColor(
            capture,
            captureGray,
            ColorConversionCodes.BGR2GRAY
        );

        Cv2.CvtColor(
            template,
            templateGray,
            ColorConversionCodes.BGR2GRAY
        );

        int resultWidth =
            captureGray.Width -
            templateGray.Width + 1;

        int resultHeight =
            captureGray.Height -
            templateGray.Height + 1;

        using Mat result =
            new(
                resultHeight,
                resultWidth,
                MatType.CV_32FC1
            );

        Cv2.MatchTemplate(
            captureGray,
            templateGray,
            result,
            TemplateMatchModes.CCoeffNormed
        );

        Cv2.MinMaxLoc(
            result,
            out _,
            out double maxValue,
            out _,
            out CvPoint maxLocation
        );

        if (maxValue < DetectionThreshold)
        {
            return null;
        }

        // Le template est placé sur la ligne
        // "Lot / Prix".
        //
        // Ces offsets récupèrent tout le panneau
        // de détail situé autour.
        int panelX =
            maxLocation.X - 20;

        int panelY =
            maxLocation.Y - 176;

        int panelWidth = 400;
        int panelHeight = 815;

        panelX =
            Math.Max(
                0,
                panelX
            );

        panelY =
            Math.Max(
                0,
                panelY
            );

        panelWidth =
            Math.Min(
                panelWidth,
                capture.Width - panelX
            );

        panelHeight =
            Math.Min(
                panelHeight,
                capture.Height - panelY
            );

        if (panelWidth <= 0 ||
            panelHeight <= 0)
        {
            return null;
        }

        CvRect panelRect =
            new(
                panelX,
                panelY,
                panelWidth,
                panelHeight
            );

        using Mat panel =
            new(
                capture,
                panelRect
            );

        string directory =
            Path.GetDirectoryName(
                captureFilePath
            ) ??
            Path.GetTempPath();

        string debugImagePath =
            Path.Combine(
                directory,
                $"hdv-panel-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png"
            );

        Cv2.ImWrite(
            debugImagePath,
            panel
        );

        return new DofusMarketPanelDetectionResult(
            debugImagePath,
            maxValue,
            panelX,
            panelY,
            panelWidth,
            panelHeight
        );
    }
}