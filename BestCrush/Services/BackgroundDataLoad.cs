namespace BestCrush.Services;

/// <summary>Runs complete reads off the renderer; obsolete completions cannot publish data.</summary>
public sealed class BackgroundDataLoad<T> : IDisposable
{
    private CancellationTokenSource? _stop;
    private int _version;
    private bool _disposed;
    public bool IsLoading { get; private set; }
    public T? Value { get; private set; }
    public Exception? Error { get; private set; }

    public async Task<bool> RunAsync(Func<CancellationToken, Task<T>> read)
    {
        if (_disposed) return false;
        Cancel();
        int version = _version;
        using var stop = new CancellationTokenSource();
        _stop = stop;
        IsLoading = true;
        Error = null;
        try
        {
            T result = await Task.Run(() => read(stop.Token), stop.Token);
            if (_disposed || version != _version || stop.IsCancellationRequested) return false;
            Value = result;
            return true;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { return false; }
        catch (Exception error)
        {
            if (!_disposed && version == _version) Error = error;
            return false;
        }
        finally
        {
            if (version == _version) { IsLoading = false; _stop = null; }
        }
    }
    public void Cancel()
    {
        _version++;
        var stop = _stop;
        _stop = null;
        IsLoading = false;
        stop?.Cancel();
    }
    public void Dispose() { _disposed = true; Cancel(); }
}
