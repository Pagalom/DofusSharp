using System.Text.Json;
using BestCrush.Domain.Models;
using BestCrush.Domain.Services;

namespace BestCrush.Services;

/// <summary>User-managed planning only; no inferred game identity, quest completion, stock or market writes.</summary>
public sealed class WeeklyRoutineStore(string path)
{
    private readonly object _sync = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public WeeklyRoutine Read()
    {
        lock (_sync)
        {
            if (!File.Exists(path)) return WeeklyRoutinePlanner.CreateDefault();
            try
            {
                var routine = JsonSerializer.Deserialize<WeeklyRoutine>(File.ReadAllText(path), Json) ?? throw new JsonException();
                // Old frequency fields are ignored; dated completions are never expanded or removed.
                if (routine.Version == 1) routine.Version = 2;
                WeeklyRoutinePlanner.Validate(routine);
                WeeklyRoutinePlanner.AssignDefaultKeys(routine);
                WeeklyRoutinePlanner.AddMissingSoulActivity(routine);
                return routine;
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NullReferenceException)
            { throw new InvalidDataException("Le fichier de routine est illisible. Il est conservé ; aucune réinitialisation n’a été effectuée.", error); }
        }
    }
    public static WeeklyRoutine Copy(WeeklyRoutine routine) => JsonSerializer.Deserialize<WeeklyRoutine>(JsonSerializer.Serialize(routine, Json), Json)!;
    public void Save(WeeklyRoutine routine)
    {
        WeeklyRoutinePlanner.Validate(routine);
        lock (_sync)
        {
            // Do not replace an existing damaged file with a fresh default.
            if (File.Exists(path)) _ = Read();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temporary, JsonSerializer.Serialize(routine, Json)); File.Move(temporary, path, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
