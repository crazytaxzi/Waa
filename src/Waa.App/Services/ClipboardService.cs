using System.Windows;

namespace Waa.App.Services;

public interface IClipboardService
{
    void SetText(string text);
}

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Clipboard.SetText(text);
    }
}
