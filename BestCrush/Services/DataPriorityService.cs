using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Maui.Storage;

namespace BestCrush.Services;

public sealed class DataPriorityService : IDataPriorityProvider
{
    private const string PreferenceKey =
        "BestCrush.DataPriority";

    public DataPriority Priority
    {
        get
        {
            string saved =
                Preferences.Default.Get(
                    PreferenceKey,
                    DataPriority.Manual.ToString()
                );

            return Enum.TryParse(
                saved,
                out DataPriority priority
            )
                ? priority
                : DataPriority.Manual;
        }

        set
        {
            Preferences.Default.Set(
                PreferenceKey,
                value.ToString()
            );
        }
    }
}
