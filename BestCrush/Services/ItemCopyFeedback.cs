namespace BestCrush.Services;

// Owned by one screen. Only the notification component subscribes, so copying
// does not rerender the analysis or recalculate any results.
public sealed class ItemCopyFeedback
{
    public event Action<string>? Copied;

    public async Task CopyAsync(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string text = value.Trim();
        await Microsoft.Maui.ApplicationModel.DataTransfer.Clipboard.Default.SetTextAsync(text);
        Copied?.Invoke(text);
    }
}
