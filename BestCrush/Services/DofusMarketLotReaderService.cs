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
        0.326,
        0.386,
        0.446,
        0.506
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
        int[] allowedQuantities =
        [
            1,
            10,
            100,
            1000
        ];

        List<(int? Quantity, long? Price)> rows = [];

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

            rows.Add(
                (
                    quantity,
                    price is > 0
                        ? price
                        : null
                )
            );
        }

        // Une quantité peut être inférée uniquement lorsqu'il
        // n'existe qu'une seule possibilité entre les lignes
        // voisines déjà reconnues.
        for (
            int index = 0;
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
}
