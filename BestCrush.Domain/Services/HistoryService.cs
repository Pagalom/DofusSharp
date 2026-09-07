using BestCrush.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace BestCrush.Domain.Services;

public sealed record CrushHistoryRuneLotWriteModel(
    long Count,
    int LotQuantity,
    long LotPrice,
    bool IsEstimated,
    MarketPriceSource? PriceSource,
    DateTime? PriceObservedAtUtc
);

public sealed record CrushHistoryRuneWriteModel(
    long DofusDbId,
    string RuneName,
    int Quantity,
    double? Value,
    IReadOnlyList<CrushHistoryRuneLotWriteModel> Lots
);

public sealed record CrushHistoryEquipmentWriteModel(
    long DofusDbId,
    string EquipmentName,
    double CoefficientPercent,
    CoefficientSource CoefficientSource,
    int RowY
);

public sealed record CrushHistorySessionWriteModel(
    string ServerName,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    double? TotalValue,
    IReadOnlyList<CrushHistoryEquipmentWriteModel> Equipments,
    IReadOnlyList<CrushHistoryRuneWriteModel> Runes
);

public sealed class HistoryService(
    BestCrushDbContext context)
{
    public async Task<Guid> SaveCrushSessionAsync(
        CrushHistorySessionWriteModel sessionData,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                sessionData.ServerName))
        {
            throw new ArgumentException(
                "Le serveur de l'historique ne peut pas être vide.",
                nameof(sessionData)
            );
        }

        if (sessionData.Runes.Count == 0)
        {
            throw new ArgumentException(
                "Une session de concassage historique doit contenir au moins une rune.",
                nameof(sessionData)
            );
        }

        long[] equipmentIds =
            sessionData
                .Equipments
                .Select(equipment =>
                    equipment.DofusDbId)
                .Distinct()
                .ToArray();

        long[] runeIds =
            sessionData
                .Runes
                .Select(rune =>
                    rune.DofusDbId)
                .Distinct()
                .ToArray();

        Dictionary<long, long?>
            equipmentIconIds =
                await context
                    .Equipments
                    .AsNoTracking()
                    .Where(equipment =>
                        equipmentIds.Contains(
                            equipment.DofusDbId))
                    .ToDictionaryAsync(
                        equipment =>
                            equipment.DofusDbId,
                        equipment =>
                            equipment.DofusDbIconId,
                        cancellationToken
                    );

        Dictionary<long, long?>
            runeIconIds =
                await context
                    .Runes
                    .AsNoTracking()
                    .Where(rune =>
                        runeIds.Contains(
                            rune.DofusDbId))
                    .ToDictionaryAsync(
                        rune =>
                            rune.DofusDbId,
                        rune =>
                            rune.DofusDbIconId,
                        cancellationToken
                    );

        CrushHistorySession session =
            new(
                sessionData.ServerName,
                sessionData.StartedAtUtc,
                sessionData.CompletedAtUtc,
                sessionData.TotalValue
            );

        foreach (
            CrushHistoryEquipmentWriteModel equipment
            in sessionData.Equipments)
        {
            session.Equipments.Add(
                new CrushHistoryEquipment(
                    session,
                    equipment.DofusDbId,
                    equipmentIconIds
                        .GetValueOrDefault(
                            equipment.DofusDbId),
                    equipment.EquipmentName,
                    equipment.CoefficientPercent,
                    equipment.CoefficientSource,
                    equipment.RowY
                )
            );
        }

        foreach (
            CrushHistoryRuneWriteModel runeData
            in sessionData.Runes)
        {
            CrushHistoryRune rune =
                new(
                    session,
                    runeData.DofusDbId,
                    runeIconIds
                        .GetValueOrDefault(
                            runeData.DofusDbId),
                    runeData.RuneName,
                    runeData.Quantity,
                    runeData.Value
                );

            foreach (
                CrushHistoryRuneLotWriteModel lot
                in runeData.Lots)
            {
                rune.Lots.Add(
                    new CrushHistoryRuneLot(
                        rune,
                        lot.Count,
                        lot.LotQuantity,
                        lot.LotPrice,
                        lot.IsEstimated,
                        lot.PriceSource,
                        lot.PriceObservedAtUtc
                    )
                );
            }

            session.Runes.Add(
                rune
            );
        }

        context.CrushHistorySessions.Add(
            session
        );

        await context.SaveChangesAsync(
            cancellationToken
        );

        return session.Id;
    }
}
