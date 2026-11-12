namespace DofusSharp.DofusDb.ApiClients.Models.Items;

/// <summary>Raw public effect parameters; distinct from rendered characteristic ranges.</summary>
public sealed class DofusDbItemRawEffect
{
    public long? EffectId { get; init; }
    public long? ActionId { get; init; }
    public long? BaseEffectId { get; init; }
    public int? Value { get; init; }
    public int? DiceNum { get; init; }
    public int? DiceSide { get; init; }
}
