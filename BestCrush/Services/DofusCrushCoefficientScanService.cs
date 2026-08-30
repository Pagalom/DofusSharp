using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BestCrush.Services;

public sealed record CrushCoefficientScanResult(
    long DofusDbId,
    string EquipmentName,
    double CoefficientPercent,
    int RowY
);

public sealed class DofusCrushCoefficientScanService(
    DofusCrushRowDetectionService rowDetectionService,
    DofusImageRegionService imageRegionService,
    DofusOcrService ocrService,
    IServiceScopeFactory serviceScopeFactory)
{
    public async Task<IReadOnlyList<CrushCoefficientScanResult>>
        ScanAndStoreAsync(
            string panelFilePath,
            string serverName,
            CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CrushRowDetectionResult> rows =
            await rowDetectionService.DetectRowsAsync(
                panelFilePath,
                cancellationToken
            );

        if (rows.Count == 0)
        {
            return [];
        }

        using IServiceScope scope =
            serviceScopeFactory.CreateScope();

        DofusItemRecognitionService itemRecognitionService =
            scope.ServiceProvider.GetRequiredService<
                DofusItemRecognitionService>();

        CoefficientService coefficientService =
            scope.ServiceProvider.GetRequiredService<
                CoefficientService>();

        Dictionary<long, CrushCoefficientScanResult>
            latestByEquipment = [];

        int rowIndex = 0;

        // DetectRowsAsync retourne les lignes du haut vers le bas.
        // Une reconnaissance plus basse remplace donc la précédente
        // pour le même équipement.
        foreach (CrushRowDetectionResult row in
            rows.OrderBy(value => value.Y))
        {
            cancellationToken.ThrowIfCancellationRequested();

            double topAreaHeight =
                Math.Min(
                    0.90,
                    70.0 / Math.Max(1, row.Height)
                );

            string itemNameRegion =
                await imageRegionService.ExtractRegionAsync(
                    row.DebugImagePath,
                    new RelativeImageRegion(
                        X: 0.07,
                        Y: 0.04,
                        Width: 0.40,
                        Height: topAreaHeight
                    ),
                    $"f9-coefficient-item-{rowIndex}"
                );

            string coefficientRegion =
                await imageRegionService.ExtractRegionAsync(
                    row.DebugImagePath,
                    new RelativeImageRegion(
                        X: 0.44,
                        Y: 0.04,
                        Width: 0.18,
                        Height: topAreaHeight
                    ),
                    $"f9-coefficient-value-{rowIndex}"
                );

            rowIndex++;

            string recognizedItemName =
                await ocrService.RecognizeUpscaledTextAsync(
                    itemNameRegion
                );

            recognizedItemName =
                recognizedItemName
                    .Split(
                        ['\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries
                    )
                    .Select(line => line.Trim())
                    .FirstOrDefault()
                ?? string.Empty;

            if (string.IsNullOrWhiteSpace(recognizedItemName))
            {
                continue;
            }

            double? coefficient =
                await ocrService.RecognizeCoefficientAsync(
                    coefficientRegion,
                    cancellationToken
                );

            if (coefficient is null)
            {
                continue;
            }

            ItemRecognitionResult? recognition =
                await itemRecognitionService.RecognizeEquipmentAsync(
                    recognizedItemName
                );

            if (recognition is null)
            {
                continue;
            }

            Equipment equipment =
                recognition.Equipment;

            latestByEquipment[equipment.DofusDbId] =
                new CrushCoefficientScanResult(
                    equipment.DofusDbId,
                    equipment.Name,
                    coefficient.Value,
                    row.Y
                );
        }

        List<CrushCoefficientScanResult> stored = [];

        foreach (CrushCoefficientScanResult result in
            latestByEquipment.Values.OrderBy(value => value.RowY))
        {
            await coefficientService.AddObservationAsync(
                result.DofusDbId,
                serverName,
                result.CoefficientPercent,
                CoefficientSource.InGameAutomatic,
                cancellationToken
            );

            stored.Add(result);
        }

        return stored;
    }
}
