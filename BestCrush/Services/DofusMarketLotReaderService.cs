using System.Text;

namespace BestCrush.Services;

public sealed record DofusMarketLot(
    int Quantity,
    long Price
);

public sealed class DofusMarketLotReaderService(
    DofusImageRegionService imageRegionService,
    DofusOcrService ocrService)
{
    private static readonly double[] RowY =
    [
        0.282,
        0.333,
        0.384,
        0.435
    ];

    private static readonly int[] AllowedQuantities =
    [
        1,
        10,
        100,
        1000
    ];

    private static readonly int[] SellQuantities =
    [
        1,
        10,
        100,
        1000
    ];

    private static readonly double[] SellRowY =
    [
        0.642,
        0.691,
        0.739,
        0.788
    ];

    public async Task<IReadOnlyList<DofusMarketLot>>
        ReadMaterialLotsAsync(
            string marketPanelImagePath)
    {
        List<MaterialLotReadRow> rows = [];

        for (
            int index = 0;
            index < RowY.Length;
            index++)
        {
            string quantityRegion =
                await imageRegionService
                    .ExtractRegionAsync(
                        marketPanelImagePath,
                        new RelativeImageRegion(
                            X: 0.18,
                            Y: RowY[index],
                            Width: 0.20,
                            Height: 0.042
                        ),
                        $"hdv-lot-{index + 1}-quantity"
                    );

            string priceRegion =
                await imageRegionService
                    .ExtractRegionAsync(
                        marketPanelImagePath,
                        new RelativeImageRegion(
                            X: 0.50,
                            Y: RowY[index],
                            Width: 0.32,
                            Height: 0.042
                        ),
                        $"hdv-lot-{index + 1}-price"
                    );

            int? quantity =
                await ocrService
                    .RecognizeMarketQuantityAsync(
                        quantityRegion
                    );

            long? price =
                await ocrService
                    .RecognizePriceAsync(
                        priceRegion
                    );

            // On conserve le résultat réellement retourné
            // par l'OCR, même s'il est invalide. Cela permet
            // au fichier de debug d'expliquer exactement
            // pourquoi une ligne a été rejetée.
            rows.Add(
                new MaterialLotReadRow(
                    index + 1,
                    quantity,
                    price is > 0
                        ? price
                        : null
                )
            );
        }

        // Si deux lignes prétendent représenter la même
        // quantité, la lecture est ambiguë. On refuse alors
        // complètement cette quantité au lieu de choisir
        // arbitrairement l'un des deux prix.
        Dictionary<int, int> quantityOccurrences =
            rows
                .Where(row =>
                    row.Quantity is int quantity &&
                    AllowedQuantities.Contains(
                        quantity
                    ) &&
                    row.Price is not null)
                .GroupBy(row =>
                    row.Quantity!.Value)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.Count()
                );

        List<DofusMarketLot> acceptedLots =
            rows
                .Where(row =>
                    row.Quantity is int quantity &&
                    AllowedQuantities.Contains(
                        quantity
                    ) &&
                    row.Price is not null &&
                    quantityOccurrences.TryGetValue(
                        quantity,
                        out int occurrenceCount) &&
                    occurrenceCount == 1)
                .Select(row =>
                    new DofusMarketLot(
                        row.Quantity!.Value,
                        row.Price!.Value
                    )
                )
                .ToList();

        await WriteMaterialLotsDebugAsync(
            marketPanelImagePath,
            rows,
            quantityOccurrences,
            acceptedLots
        );

        return acceptedLots;
    }

    private static async Task
        WriteMaterialLotsDebugAsync(
            string marketPanelImagePath,
            IReadOnlyList<MaterialLotReadRow> rows,
            IReadOnlyDictionary<int, int>
                quantityOccurrences,
            IReadOnlyList<DofusMarketLot>
                acceptedLots)
    {
        try
        {
            string directory =
                Path.GetDirectoryName(
                    marketPanelImagePath
                ) ??
                Path.GetTempPath();

            string debugPath =
                Path.Combine(
                    directory,
                    "hdv-lots-read.txt"
                );

            StringBuilder debug =
                new();

            debug.AppendLine(
                "BESTCRUSH - HDV MATERIAL LOT READ"
            );

            debug.AppendLine(
                $"Source: {Path.GetFileName(marketPanelImagePath)}"
            );

            debug.AppendLine(
                $"UTC: {DateTime.UtcNow:O}"
            );

            debug.AppendLine();

            foreach (
                MaterialLotReadRow row
                in rows)
            {
                string quantityText =
                    row.Quantity?.ToString()
                    ?? "null";

                string priceText =
                    row.Price?.ToString()
                    ?? "null";

                string result;

                if (row.Quantity is null)
                {
                    result =
                        "REJECTED - quantity OCR returned null";
                }
                else if (
                    !AllowedQuantities.Contains(
                        row.Quantity.Value))
                {
                    result =
                        "REJECTED - quantity not allowed";
                }
                else if (row.Price is null)
                {
                    result =
                        "REJECTED - price OCR returned null/invalid";
                }
                else if (
                    quantityOccurrences.TryGetValue(
                        row.Quantity.Value,
                        out int occurrenceCount) &&
                    occurrenceCount > 1)
                {
                    result =
                        $"REJECTED - duplicate quantity ({occurrenceCount} rows)";
                }
                else
                {
                    result =
                        "ACCEPTED";
                }

                debug.AppendLine(
                    $"Row {row.RowNumber}"
                );

                debug.AppendLine(
                    $"  Quantity OCR : {quantityText}"
                );

                debug.AppendLine(
                    $"  Price OCR    : {priceText}"
                );

                debug.AppendLine(
                    $"  Result       : {result}"
                );

                debug.AppendLine(
                    $"  Quantity crop: hdv-lot-{row.RowNumber}-quantity.png"
                );

                debug.AppendLine(
                    $"  Price crop   : hdv-lot-{row.RowNumber}-price.png"
                );

                debug.AppendLine();
            }

            debug.AppendLine(
                $"Accepted lots: {acceptedLots.Count}"
            );

            foreach (
                DofusMarketLot lot
                in acceptedLots
                    .OrderBy(lot =>
                        lot.Quantity))
            {
                debug.AppendLine(
                    $"  x{lot.Quantity} = {lot.Price}"
                );
            }

            await File.WriteAllTextAsync(
                debugPath,
                debug.ToString()
            );
        }
        catch
        {
            // Le debug ne doit jamais empêcher
            // une lecture HDV de fonctionner.
        }
    }

    public async Task<IReadOnlyList<DofusMarketLot>>
        ReadSellMaterialLotsAsync(
            string marketPanelImagePath)
    {
        List<DofusMarketLot> lots = [];

        for (
            int index = 0;
            index < SellRowY.Length;
            index++)
        {
            // Dans l'onglet VENTE, le prix utile se trouve
            // dans le tableau "ACTUELLEMENT EN VENTE" à gauche.
            //
            // Zone calibrée sur le panneau HDV réel :
            // elle conserve intégralement les prix x1/x10/x100/x1000,
            // y compris les valeurs longues comme "1 099 854",
            // sans inclure la colonne "Lot".
            string priceRegion =
                await imageRegionService
                    .ExtractRegionAsync(
                        marketPanelImagePath,
                        new RelativeImageRegion(
                            X: 0.32,
                            Y: SellRowY[index],
                            Width: 0.25,
                            Height: 0.042
                        ),
                        $"hdv-sell-lot-{SellQuantities[index]}-price"
                    );

            long? price =
                await ocrService
                    .RecognizePriceAsync(
                        priceRegion
                    );

            // Une ligne absente ou illisible reste absente.
            // On n'invente jamais de prix de remplacement.
            if (price is null ||
                price <= 0)
            {
                continue;
            }

            lots.Add(
                new DofusMarketLot(
                    SellQuantities[index],
                    price.Value
                )
            );
        }

        return lots;
    }

    private sealed record MaterialLotReadRow(
        int RowNumber,
        int? Quantity,
        long? Price
    );
}
