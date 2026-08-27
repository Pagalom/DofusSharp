using BestCrush.Domain.Models;

namespace BestCrush.Domain.Services;

public interface IDataPriorityProvider
{
    DataPriority Priority { get; set; }
}