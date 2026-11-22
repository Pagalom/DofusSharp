using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BestCrush.Services;

/// <summary>Rune IDs only, scoped to a server; independent of incoming market prices.</summary>
public sealed class RuneRankingPreferences
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly ILogger<RuneRankingPreferences>? _logger;
    private readonly Dictionary<string,HashSet<long>> _hidden;
    public RuneRankingPreferences(ILogger<RuneRankingPreferences> logger) : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BestCrush","Settings","rune-ranking.json"),logger) { }
    internal RuneRankingPreferences(string path,ILogger<RuneRankingPreferences>? logger=null)
    {
        _path=path;_logger=logger;
        try
        {
            var stored=File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string,long[]>>(File.ReadAllText(path)) : null;
            _hidden=new(StringComparer.OrdinalIgnoreCase);
            if(stored is not null)foreach(var pair in stored)_hidden[pair.Key]=pair.Value?.Where(id=>id>0).ToHashSet() ?? [];
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException)
        {_hidden=new(StringComparer.OrdinalIgnoreCase);_logger?.LogWarning(ex,"Could not read hidden rune choices.");}
    }
    public IReadOnlySet<long> Hidden(string? server)
    {lock(_sync)return server is not null && _hidden.TryGetValue(server,out var ids)?ids.ToHashSet():new HashSet<long>();}
    public void Hide(string server,long id)=>Set(server,id,true);
    public void Restore(string server,long id)=>Set(server,id,false);
    private void Set(string server,long id,bool hidden)
    {
        if(string.IsNullOrWhiteSpace(server) || id<=0)return;
        lock(_sync)
        {
            if(!_hidden.TryGetValue(server,out var ids))_hidden[server]=ids=[];
            if(hidden?ids.Add(id):ids.Remove(id))Save();
        }
    }
    public void Reset(string server){lock(_sync){if(_hidden.Remove(server))Save();}}
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path+".tmp",JsonSerializer.Serialize(_hidden));
            File.Move(_path+".tmp",_path,true);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {_logger?.LogWarning(ex,"Could not save hidden rune choices.");}
    }
}
