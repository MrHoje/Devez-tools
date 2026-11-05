using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DevezTools.Infrastructure;

/// <summary>
/// 클립보드·파일의 이미지를 스레드와 상관없이 쓸 수 있는(Frozen) 비트맵으로 읽는다(talkRemind WPF 도구 상자에서 이식).
/// Clipboard.GetImage() 는 32bpp DIB 의 줄 간격(stride)·알파를 잘못 읽어 그림이 밀리거나 투명해지므로 PNG·DIB 를 직접 푼다.
/// </summary>
internal static class ClipboardImage
{
    public static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp"];

    public static bool IsImageFile(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>복사한 이미지 파일이 있으면 그 파일을, 아니면 클립보드 그림을 읽는다. 없으면 null.</summary>
    public static BitmapSource? FromClipboard()
    {
        IDataObject? data = Clipboard.GetDataObject();
        if (data is null) return null;
        if (data.GetData(DataFormats.FileDrop) is string[] files && files.FirstOrDefault(IsImageFile) is { } path) return FromFile(path);

        BitmapSource? png = null, dib = null;
        try
        {
            if (data.GetData("PNG") is Stream stream)
                using (stream) png = ToFrozenCopy(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0]);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException) { }
        try
        {
            if (data.GetData(DataFormats.Dib) is Stream stream)
                using (stream) dib = DecodeDib(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { }

        // 어떤 프로그램은 PNG 에 축소본, DIB 에 원본을 넣는다(또는 반대) — 픽셀이 많은 쪽을 고른다
        if (png != null && dib != null) return (long)dib.PixelWidth * dib.PixelHeight > (long)png.PixelWidth * png.PixelHeight ? dib : png;
        if (png != null || dib != null) return png ?? dib;
        return Clipboard.ContainsImage() && Clipboard.GetImage() is { } image ? ToFrozenCopy(image) : null;
    }

    public static BitmapSource FromFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad; // 파일을 바로 놓아 준다
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static byte[] ToPng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // CF_DIB: BITMAPINFOHEADER + (BI_BITFIELDS 면 마스크 12바이트) + 4바이트 정렬 줄들. 24·32bpp 만 직접 풀고 나머지는 null.
    private static BitmapSource? DecodeDib(Stream source)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        byte[] dib = buffer.ToArray();
        if (dib.Length < 40) return null;

        int headerSize = BitConverter.ToInt32(dib, 0);
        int width = BitConverter.ToInt32(dib, 4);
        int rawHeight = BitConverter.ToInt32(dib, 8);
        int bits = BitConverter.ToUInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        if (width <= 0 || rawHeight == 0 || bits is not (24 or 32)) return null;

        bool bottomUp = rawHeight > 0;
        int height = Math.Abs(rawHeight);
        int offset = headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
        int sourceStride = (width * bits + 31) / 32 * 4;
        if (offset + (long)sourceStride * height > dib.Length) return null;

        int bytesPerPixel = bits / 8;
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int from = offset + (bottomUp ? height - 1 - y : y) * sourceStride;
            int to = y * stride;
            for (int x = 0; x < width; x++)
            {
                pixels[to + x * 4] = dib[from + x * bytesPerPixel];
                pixels[to + x * 4 + 1] = dib[from + x * bytesPerPixel + 1];
                pixels[to + x * 4 + 2] = dib[from + x * bytesPerPixel + 2];
            }
        }
        BitmapSource result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, stride); // 알파는 버린다
        result.Freeze();
        return result;
    }

    private static BitmapSource ToFrozenCopy(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        BitmapSource copy = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }
}
