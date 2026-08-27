using OpenCvSharp;
using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;

namespace BestCrush.Services;

public sealed class DofusPanelDetectionService
{
    private const double CrushHeaderMinimumConfidence = 0.85;

    public async Task<DofusPanelDetectionResult?>
        DetectCrushResultPanelAsync(
            string sourceFilePath,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

        byte[] templateBytes;

        await using (Stream templateStream =
            await FileSystem.OpenAppPackageFileAsync(
                "crush-result-header.png"
            ))
        {
            using MemoryStream memoryStream = new();

            await templateStream.CopyToAsync(
                memoryStream,
                cancellationToken
            );

            templateBytes =
                memoryStream.ToArray();
        }

        using Mat template =
            Cv2.ImDecode(
                templateBytes,
                ImreadModes.Color
            );

        if (template.Empty())
        {
            throw new InvalidOperationException(
                "Impossible de charger le modèle du panneau de concassage."
            );
        }

        if (source.Width < template.Width ||
            source.Height < template.Height)
        {
            return null;
        }

        using Mat result = new();

        Cv2.MatchTemplate(
            source,
            template,
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

        if (maxValue < CrushHeaderMinimumConfidence)
        {
            return null;
        }

        // Le modèle correspond au centre supérieur
        // du panneau "Résultat du concassage".
        //
        // Ces offsets sont relatifs AU BANDEAU détecté,
        // et non à la fenêtre Dofus.
        int panelX =
            maxLocation.X - 200;

        int panelY =
            maxLocation.Y + 2;

        int panelWidth = 720;
        int panelHeight = 790;

        CvRect panelRect =
            ClampRectangle(
                panelX,
                panelY,
                panelWidth,
                panelHeight,
                source.Width,
                source.Height
            );

        if (panelRect.Width <= 0 ||
            panelRect.Height <= 0)
        {
            return null;
        }

        using Mat panel =
            new(
                source,
                panelRect
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
            maxValue,
            panelRect.X,
            panelRect.Y,
            panelRect.Width,
            panelRect.Height,
            outputPath
        );
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
            Math.Max(0, right - left),
            Math.Max(0, bottom - top)
        );
    }
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