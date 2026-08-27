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

    public async Task<IReadOnlyList<DofusMarketLot>>
        ReadMaterialLotsAsync(
            string marketPanelImagePath)
    {
        int[] allowedQuantities =
        [
            1,
            10,
            100,
            1000
        ];

        List<(int? Quantity, long? Price)> rows = [];

        for (int index = 0;
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

            rows.Add(
                (
                    quantity,
                    price is > 0
                        ? price
                        : null
                )
            );
        }

        // Si une ligne possède un prix mais que sa quantité
        // n'a pas été reconnue, on tente de l'inférer.
        //
        // On ne l'accepte que s'il n'existe qu'UNE SEULE
        // quantité possible compte tenu des lignes voisines.
        for (int index = 0;
            index < rows.Count;
            index++)
        {
            if (rows[index].Price is null ||
                rows[index].Quantity is not null)
            {
                continue;
            }

            int? previousQuantity =
                rows
                    .Take(index)
                    .Where(row =>
                        row.Quantity is not null)
                    .Select(row =>
                        row.Quantity)
                    .LastOrDefault();

            int? nextQuantity =
                rows
                    .Skip(index + 1)
                    .Where(row =>
                        row.Quantity is not null)
                    .Select(row =>
                        row.Quantity)
                    .FirstOrDefault();

            int[] candidates =
                allowedQuantities
                    .Where(quantity =>
                        (
                            previousQuantity is null ||
                            quantity >
                                previousQuantity.Value
                        ) &&
                        (
                            nextQuantity is null ||
                            quantity <
                                nextQuantity.Value
                        )
                    )
                    .ToArray();

            if (candidates.Length == 1)
            {
                rows[index] =
                    (
                        candidates[0],
                        rows[index].Price
                    );
            }
        }

        return rows
            .Where(row =>
                row.Quantity is not null &&
                row.Price is not null)
            .Select(row =>
                new DofusMarketLot(
                    row.Quantity!.Value,
                    row.Price!.Value
                )
            )
            .ToList();
    }
}