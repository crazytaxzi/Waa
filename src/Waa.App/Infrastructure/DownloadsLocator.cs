using System.Runtime.InteropServices;

namespace Waa.App.Infrastructure;

public static class DownloadsLocator
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string GetDownloadsFolder()
    {
        var folderId = DownloadsFolderId;
        var result = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out var pathPointer);
        if (result >= 0 && pathPointer != IntPtr.Zero)
        {
            try
            {
                var resolved = Marshal.PtrToStringUni(pathPointer);
                if (!string.IsNullOrWhiteSpace(resolved))
                {
                    return resolved;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPointer);
            }
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
        {
            throw new DirectoryNotFoundException("Windows did not provide a user profile or Downloads folder.");
        }

        return Path.Combine(profile, "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath(
        ref Guid rfid,
        uint flags,
        IntPtr token,
        out IntPtr path);
}
