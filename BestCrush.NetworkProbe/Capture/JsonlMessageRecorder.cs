using System.Text;
using System.Text.Json;

using BestCrush.NetworkProbe.Protocol;

namespace BestCrush.NetworkProbe.Capture;

internal sealed class JsonlMessageRecorder : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();

    private long _sequence;
    private bool _failed;

    public string FilePath { get; }

    public JsonlMessageRecorder(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);

        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        _writer = new StreamWriter(
            new FileStream(
                FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read),
            new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }

    public void Write(
        FlowDirection direction,
        AnkamaAny any,
        byte[] frame)
    {
        if (_failed)
            return;

        try
        {
            long sequence = Interlocked.Increment(ref _sequence);
            DateTimeOffset now = DateTimeOffset.Now;

            var record = new
            {
                seq = sequence,
                utc = now.UtcDateTime.ToString("O"),
                local = now.ToString("O"),
                direction = direction == FlowDirection.ServerToClient ? "S2C" : "C2S",
                key = any.Key,
                bodyLength = any.Body.Length,
                bodyHex = Convert.ToHexString(any.Body),
                frameLength = frame.Length,
                frameHex = Convert.ToHexString(frame)
            };

            string json = JsonSerializer.Serialize(record);

            lock (_sync)
            {
                _writer.WriteLine(json);
            }
        }
        catch (Exception ex)
        {
            _failed = true;
            Console.Error.WriteLine(
                $"[record] {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer.Dispose();
        }
    }
}
