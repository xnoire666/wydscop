using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Wydscop.Search;

/// <summary>
/// Gets the icon Windows itself shows for any shell item (apps, Store apps, files, folders)
/// through IShellItemImageFactory, keeping the alpha channel.
/// </summary>
internal static class ShellIcons
{
    private const int SIIGBF_BIGGERSIZEOK = 0x1;
    private const int SIIGBF_ICONONLY = 0x4;

    /// <returns>A frozen image (safe to use from any thread), or null.</returns>
    public static ImageSource? Get(string parsingName, int sizePx)
    {
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            Guid iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out object obj);
            var factory = (IShellItemImageFactory)obj;
            int hr = factory.GetImage(new SIZE { cx = sizePx, cy = sizePx }, SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK, out hBitmap);
            Marshal.ReleaseComObject(obj);
            if (hr != 0 || hBitmap == IntPtr.Zero) return null;

            var image = FromHBitmap(hBitmap);
            image?.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// Imaging.CreateBitmapSourceFromHBitmap drops the alpha channel (black corners), so copy the
    /// 32-bit premultiplied pixels ourselves.
    /// </summary>
    private static BitmapSource? FromHBitmap(IntPtr hBitmap)
    {
        if (GetObject(hBitmap, Marshal.SizeOf<DIBSECTION>(), out DIBSECTION ds) == 0)
            return null;

        var bm = ds.dsBm;
        if (bm.bmBitsPixel != 32 || bm.bmBits == IntPtr.Zero)
        {
            return Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }

        int width = bm.bmWidth, height = bm.bmHeight, stride = bm.bmWidthBytes;
        var pixels = new byte[stride * height];
        bool bottomUp = ds.dsBmih.biHeight > 0;
        for (int row = 0; row < height; row++)
        {
            int srcRow = bottomUp ? height - 1 - row : row;
            Marshal.Copy(bm.bmBits + srcRow * stride, pixels, row * stride, stride);
        }

        // Old-style icons sometimes have no alpha at all: make them opaque instead of invisible.
        bool anyAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
            if (pixels[i] != 0) { anyAlpha = true; break; }
        if (!anyAlpha)
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0;
        public uint dsBitfields1;
        public uint dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out DIBSECTION lpvObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
