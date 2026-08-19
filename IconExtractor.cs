using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Winfred;

/// <summary>
/// Pulls the Windows shell icon for a file, folder, shortcut or Store app — the
/// same glyphs Alfred shows for apps and files — and caches them for the session.
/// </summary>
public static class IconExtractor
{
    private const int IconPx = 48;
    private const int SiigbfIconOnly = 0x04;
    private const int SiigbfBiggerSizeOk = 0x01;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Guid ShellItemImageFactoryIid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static ImageSource? ForFile(string path, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return Cache.GetOrAdd(CacheKey(path, isDirectory), _ => Extract(path));
    }

    public static ImageSource? ForApp(ApplicationIndex.Entry entry)
    {
        if (entry.IsAppId)
            return Cache.GetOrAdd("appid:" + entry.Target, _ => Extract(@"shell:AppsFolder\" + entry.Target));
        return ForFile(entry.Target);
    }

    private static string CacheKey(string path, bool isDirectory)
    {
        if (isDirectory) return "*folder*";
        string extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".msc", StringComparison.OrdinalIgnoreCase))
            return path;
        return "*" + extension.ToLowerInvariant();
    }

    private static ImageSource? Extract(string path)
    {
        try
        {
            var iid = ShellItemImageFactoryIid;
            SHCreateItemFromParsingName(path, 0, ref iid, out var factory);
            if (factory == null) return null;

            var size = new SIZE { cx = IconPx, cy = IconPx };
            if (factory.GetImage(size, SiigbfIconOnly | SiigbfBiggerSizeOk, out nint hbitmap) != 0 || hbitmap == 0)
                return null;

            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(
                    hbitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hbitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string pszPath, nint pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);
}
