using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public enum DataValueKind { Price, CrushingCoefficient }

public sealed class DataSourcePreference
{
    [MaxLength(64)] public string ServerName {get;set;}="";
    public DataValueKind Kind {get;set;}
    public MarketObjectType ObjectType {get;set;}
    // Zero item = whole tab; zero quantity = whole item.
    public long ItemId {get;set;}
    public int Quantity {get;set;}
    public MarketPriceSource Source {get;set;}
}
