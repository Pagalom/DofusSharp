using System.ComponentModel.DataAnnotations;

namespace BestCrush.Domain.Models;

public sealed class PetFeedingObservation
{
    [Key,MaxLength(64)] public string Id {get;set;}="";
    public long FoodItemId {get;set;}
    public long PetItemId {get;set;}
    public int Quantity {get;set;}
    public long ExperienceBefore {get;set;}
    public long ExperienceAfter {get;set;}
    public DateTime ObservedAtUtc {get;set;}
    [MaxLength(64)] public string ClientBuild {get;set;}="";
}
