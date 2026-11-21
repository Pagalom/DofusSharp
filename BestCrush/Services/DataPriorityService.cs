using BestCrush.Domain.Models;
using BestCrush.Domain.Services;
using Microsoft.Maui.Storage;

namespace BestCrush.Services;

public sealed class DataPriorityService : IDataPriorityProvider
{
    private readonly IPreferences _preferences;
    public DataPriorityService(IPreferences? preferences=null)=>_preferences=preferences ?? Preferences.Default;
    private const string PreferenceKey =
        "BestCrush.DataPriority";
    public DataPriority CustomDefault=>Enum.TryParse<DataPriority>(_preferences.Get(PreferenceKey+".Default",DataPriority.Manual.ToString()),out var saved)
        && saved!=DataPriority.Custom ? saved : DataPriority.Manual;

    public DataPriority Priority
    {
        get
        {
            string saved =
                _preferences.Get(
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
            var previous=Priority;
            if(value!=DataPriority.Custom)_preferences.Set(PreferenceKey+".Default",value.ToString());
            else if(previous!=DataPriority.Custom)_preferences.Set(PreferenceKey+".Default",previous.ToString());
            _preferences.Set(
                PreferenceKey,
                value.ToString()
            );
        }
    }
}
