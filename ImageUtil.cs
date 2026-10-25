using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIBoundary.Core;

/// <summary>ছবি পড়া, সোজা করা (মোবাইলের ঘোরানো ছবি), ছোট করা ও JPEG বানানো।</summary>
public static class ImageUtil
{
    /// <summary>ফাইল থেকে ছবি পড়ে সোজা করে, বড় হলে ছোট করে JPEG বাইট দেয়। না পারলে null।</summary>
    public static byte[]? LoadNormalized(string path, int maxSide)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource src = frame;
            src = ApplyOrientation(src, GetOrientation(frame));
            src = Downscale(src, maxSide);
            return Encode(src, 85);
        }
        catch { return null; }
    }

    public static byte[] EncodeBgr(byte[] bgr, int width, int height, int quality)
    {
        var bs = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgr, width * 3);
        return Encode(bs, quality);
    }

    public static byte[]? MakeThumb(byte[] jpeg, int side)
    {
        try
        {
            using var ms = new MemoryStream(jpeg);
            var frame = BitmapFrame.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return Encode(Downscale(frame, side), 80);
        }
        catch { return null; }
    }

    public static BitmapSource? FromBytes(byte[]? data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(data);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static int GetOrientation(BitmapFrame frame)
    {
        try
        {
            if (frame.Metadata is BitmapMetadata md && md.ContainsQuery("System.Photo.Orientation") &&
                md.GetQuery("System.Photo.Orientation") is ushort u)
                return u;
        }
        catch { }
        return 1;
    }

    private static BitmapSource ApplyOrientation(BitmapSource src, int orientation) => orientation switch
    {
        3 => new TransformedBitmap(src, new RotateTransform(180)),
        6 => new TransformedBitmap(src, new RotateTransform(90)),
        8 => new TransformedBitmap(src, new RotateTransform(270)),
        _ => src
    };

    private static BitmapSource Downscale(BitmapSource src, int maxSide)
    {
        int longest = Math.Max(src.PixelWidth, src.PixelHeight);
        if (longest <= maxSide) return src;
        double scale = (double)maxSide / longest;
        return new TransformedBitmap(src, new ScaleTransform(scale, scale));
    }

    private static byte[] Encode(BitmapSource src, int quality)
    {
        // JPEG-এর জন্য সবসময় Bgr24 (PNG-র স্বচ্ছতা থাকলে সমস্যা এড়াতে)
        BitmapSource conv = src.Format == PixelFormats.Bgr24
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgr24, null, 0);

        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(conv));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}
