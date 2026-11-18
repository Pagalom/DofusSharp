using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
#if WINDOWS
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
#endif

namespace BestCrush.Services;

internal sealed class MarketClipboardQueue :
    IDisposable
{
    internal sealed record CopyResult(int Count, bool IsQueue);

#if WINDOWS
    private readonly DofusWindowService _windows;
    private readonly ILogger<MarketClipboardQueue> _logger;
    private readonly object _sync = new();
    private CancellationTokenSource? _monitor;
    private DofusClientWindowKey _window;
    private IntPtr _clipboardOwner;
    private string[] _names = [];
    private int _index;
    private uint _clipboardSequence;
    private bool _wasVDown;
    private long? _pasteStarted;

    public MarketClipboardQueue(DofusWindowService windows, ILogger<MarketClipboardQueue> logger)
    {
        _windows = windows;
        _logger = logger;
    }
#endif

    internal Task<CopyResult> CopyAsync(IEnumerable<string> names, DofusClientWindowKey? window)
    {
        var terms = names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (terms.Length == 0) return new CopyResult(0, false);
#if WINDOWS
            lock (_sync)
            {
                Stop();
                if (terms.Length > 1 && TryStart(terms, window)) return new CopyResult(terms.Length, true);
            }
#endif
            await Clipboard.Default.SetTextAsync(string.Join(Environment.NewLine, terms));
            return new CopyResult(terms.Length, false);
        });
    }

#if WINDOWS
    private bool TryStart(string[] terms, DofusClientWindowKey? window)
    {
        if (window is null)
        {
            var games = _windows.ListVisibleDofusWindows().Where(game => game.ProcessName.Equals("Dofus", StringComparison.OrdinalIgnoreCase))
                .Take(2).ToArray();
            if (games is [var game])
                window = new(game.Handle.ToInt64(), game.ProcessId);
        }
        if (window is not { } key || !DofusWindowService.MatchesWindow(key) ||
            Microsoft.Maui.Controls.Application.Current is not { } application ||
            application.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window appWindow ||
            GetClipboardSequenceNumber() == 0)
            return false;

        _window = key;
        _clipboardOwner = WinRT.Interop.WindowNative.GetWindowHandle(appWindow);
        if (_clipboardOwner == IntPtr.Zero || !TryWrite(terms[0], null))
            return false;
        _names = terms;
        _index = 0;
        _wasVDown = true;
        _monitor = new CancellationTokenSource();
        var token = _monitor.Token;
        _logger.LogDebug("File HDV active pour la fenêtre {Window}, processus {Process}, {Count} noms.",
            key.Handle, key.ProcessId, terms.Length);
        _ = Task.Run(() => MonitorAsync(token));
        return true;
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        // A dispatcher timer can be throttled while the MAUI window is in the background.
        // Clipboard ownership remains with our window, while input sampling runs independently.
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(15, token).ConfigureAwait(false);
                lock (_sync)
                {
                    if (token.IsCancellationRequested) return;
                    Poll();
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            lock (_sync)
            {
                if (token.IsCancellationRequested) return;
                _logger.LogWarning(ex, "Surveillance de la file HDV interrompue.");
                Stop();
            }
        }
    }

    private void Poll()
    {
        if (GetClipboardSequenceNumber() != _clipboardSequence || !DofusWindowService.MatchesWindow(_window))
        {
            _logger.LogDebug("File HDV arrêtée. Presse-papier attendu {Expected}, actuel {Current}, fenêtre valide {Valid}.",
                _clipboardSequence, GetClipboardSequenceNumber(), DofusWindowService.MatchesWindow(_window));
            Stop();
            return;
        }

        bool isForeground = DofusWindowService.IsForegroundWindow(_window);
        bool vDown = isForeground && GetAsyncKeyState(0x56) < 0;
        if (vDown && !_wasVDown && _pasteStarted is null && GetAsyncKeyState(0x11) < 0)
            _pasteStarted = Stopwatch.GetTimestamp();
        _wasVDown = !isForeground || vDown;

        // Let the normal paste reach the game before replacing its clipboard text.
        if (_pasteStarted is not { } paste || Stopwatch.GetElapsedTime(paste) < TimeSpan.FromMilliseconds(300))
            return;

        if (_index + 1 == _names.Length)
        {
            Stop();
            return;
        }
        try
        {
            if (TryWrite(_names[_index + 1], _clipboardSequence))
            {
                _index++;
                _pasteStarted = null;
                _logger.LogDebug("File HDV passée au nom {Index} sur {Count}.", _index + 1, _names.Length);
            }
            else if (Stopwatch.GetElapsedTime(paste) >= TimeSpan.FromSeconds(2))
            {
                Stop();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "La file du presse-papier HDV a été interrompue.");
            Stop();
        }
    }

    private bool TryWrite(string text, uint? expectedSequence)
    {
        byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
        IntPtr buffer = GlobalAlloc(0x0002, (nuint)bytes.Length); // GMEM_MOVEABLE is required by SetClipboardData.
        if (buffer == IntPtr.Zero) throw new Win32Exception();
        try
        {
            IntPtr address = GlobalLock(buffer);
            if (address == IntPtr.Zero) throw new Win32Exception();
            try { Marshal.Copy(bytes, 0, address, bytes.Length); }
            finally { GlobalUnlock(buffer); }

            if (!OpenClipboard(_clipboardOwner)) return false;
            bool written = false;
            try
            {
                if (expectedSequence.HasValue && GetClipboardSequenceNumber() != expectedSequence.Value)
                    return false;
                if (!EmptyClipboard() || SetClipboardData(13, buffer) == IntPtr.Zero) // CF_UNICODETEXT
                    throw new Win32Exception();
                buffer = IntPtr.Zero; // The clipboard owns the handle after a successful write.
                written = true;
                return true;
            }
            finally
            {
                CloseClipboard();
                // Windows finishes publishing the clipboard formats at CloseClipboard.
                // The sequence sampled before closing is already outdated when polling starts.
                if (written) _clipboardSequence = GetClipboardSequenceNumber();
            }
        }
        finally
        {
            if (buffer != IntPtr.Zero) GlobalFree(buffer);
        }
    }

    private void Stop()
    {
        _monitor?.Cancel();
        _monitor?.Dispose();
        _monitor = null;
        _pasteStarted = null;
        _names = [];
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
#endif

    public void Dispose()
    {
#if WINDOWS
        lock (_sync) Stop();
#endif
    }
}
