using BestCrush.Components.Pure;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using BestCrush.Models;
using DofusSharp.Dofocus.ApiClients.Models.Items;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BestCrush.Components.Pages;

public partial class Server
{
    private sealed class ServerPageState
    {
        public SearchItemsModel Model { get; set; } =
            new();

        public Equipment[]? AllItems { get; set; }

        public List<ItemCrushResult>? RankedItems { get; set; }

        public List<Equipment>? UnrankedItems { get; set; }

        public Dictionary<long, HashSet<string>>
            MissingDataByItem { get; set; } =
                new();

        public IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            RunePrices { get; set; } =
                new Dictionary<
                    (long DofusDbId, int Quantity),
                    MarketPriceObservation>();

        public IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            EquipmentPrices { get; set; } =
                new Dictionary<
                    (long DofusDbId, int Quantity),
                    MarketPriceObservation>();

        public IReadOnlyDictionary<
            (long DofusDbId, int Quantity),
            MarketPriceObservation>
            ResourcePrices { get; set; } =
                new Dictionary<
                    (long DofusDbId, int Quantity),
                    MarketPriceObservation>();

        public Dictionary<long, CraftCostResult>
            CraftCostsByItem { get; set; } =
                new();

        public Dictionary<long, CoefficientObservation>
            Coefficients { get; set; } =
                new();

        public SortOrder SortOrder { get; set; }
    }
}
