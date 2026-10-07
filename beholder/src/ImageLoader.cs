using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Beholder
{
    public sealed class LoadedImage
    {
        public string Path;
        public string Name;
        public BitmapSource Bitmap;
        public int Width;
        public int Height;
        public double Aspect { get { return (double)Width / Math.Max(1, Height); } }
    }

    public static class ImageLoader
    {
        public static LoadedImage Load(string path)
        {
            if (!File.Exists(path)) throw new IOException("File no longer exists.");
            if (new FileInfo(path).Length > 200L * 1024 * 1024) throw new IOException("File exceeds the 200 MB safety limit.");
            using (var probe = File.OpenRead(path))
            {
                var signature = new byte[12];
                if (probe.Read(signature, 0, 12) == 12 && System.Text.Encoding.ASCII.GetString(signature, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(signature, 8, 4) == "WEBP") return WebPDecoder.Load(path);
            }
            BitmapSource bitmap;
            int width, height;
            ushort orientation = 1;
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                // Decode from a closed-after-load stream: originals are never locked or modified.
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                width = frame.PixelWidth; height = frame.PixelHeight;
                if ((long)width * height > 100000000) throw new IOException("Image exceeds the 100 megapixel safety limit.");
                var metadata = frame.Metadata as BitmapMetadata;
                if (metadata != null)
                {
                    try
                    {
                        object value = metadata.GetQuery("/app1/ifd/{ushort=274}") ?? metadata.GetQuery("/ifd/{ushort=274}");
                        if (value != null) orientation = Convert.ToUInt16(value);
                    }
                    catch (NotSupportedException) { orientation = 1; }
                }
                // Comparison previews are bounded; dimensions retain the original file resolution.
                stream.Position = 0;
                var preview = new BitmapImage();
                preview.BeginInit();
                preview.CacheOption = BitmapCacheOption.OnLoad;
                preview.StreamSource = stream;
                if (Math.Max(width, height) > 4096)
                {
                    if (width >= height) preview.DecodePixelWidth = 4096;
                    else preview.DecodePixelHeight = 4096;
                }
                preview.EndInit();
                bitmap = preview;
                if (orientation >= 2 && orientation <= 8)
                {
                    Transform transform;
                    switch (orientation)
                    {
                        case 2: transform = new ScaleTransform(-1, 1); break;
                        case 3: transform = new RotateTransform(180); break;
                        case 4: transform = new ScaleTransform(1, -1); break;
                        case 5: transform = new MatrixTransform(0, 1, 1, 0, 0, 0); break;
                        case 6: transform = new RotateTransform(90); break;
                        case 7: transform = new MatrixTransform(0, -1, -1, 0, 0, 0); break;
                        default: transform = new RotateTransform(270); break;
                    }
                    bitmap = new TransformedBitmap(bitmap, transform);
                    if (orientation >= 5) { int old = width; width = height; height = old; }
                }
                bitmap.Freeze();
            }
            return new LoadedImage { Path = System.IO.Path.GetFullPath(path), Name = System.IO.Path.GetFileName(path), Bitmap = bitmap, Width = width, Height = height };
        }

        public static BitmapSource Greyscale(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte grey = (byte)((pixels[i + 2] * 54 + pixels[i + 1] * 183 + pixels[i] * 19 + 128) >> 8);
                pixels[i] = grey; pixels[i + 1] = grey; pixels[i + 2] = grey;
            }
            var output = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            output.Freeze(); return output;
        }

        public static LoadedImage FromClipboardData(IDataObject data, int number)
        {
            if (data == null) return null;
            // Qt/Flameshot puts a valid PNG beside a DeviceIndependentBitmap whose
            // alpha bytes WPF interprets as all zero. Prefer the encoded pixels.
            foreach (string format in new[] { "image/png", "PNG" })
            {
                if (!data.GetDataPresent(format, false)) continue;
                object value = data.GetData(format, false);
                Stream input = value as Stream;
                byte[] bytes = value as byte[];
                if (input == null && bytes == null) continue;
                using (var copy = new MemoryStream())
                {
                    if (bytes != null) copy.Write(bytes, 0, bytes.Length);
                    else
                    {
                        if (input.CanSeek) input.Position = 0;
                        input.CopyTo(copy);
                    }
                    if (copy.Length > 200L * 1024 * 1024) throw new IOException("Clipboard image exceeds the 200 MB safety limit.");
                    copy.Position = 0;
                    var decoder = BitmapDecoder.Create(copy, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var frame = decoder.Frames[0];
                    if ((long)frame.PixelWidth * frame.PixelHeight > 100000000) throw new IOException("Clipboard image exceeds the 100 megapixel safety limit.");
                    return FromClipboard(frame, number);
                }
            }
            var source = data.GetData(DataFormats.Bitmap, true) as BitmapSource;
            return source == null ? null : FromClipboard(source, number);
        }

        public static LoadedImage FromClipboard(BitmapSource source, int number)
        {
            // Copy clipboard-owned pixels so the workspace owns an immutable image.
            var bitmap = new WriteableBitmap(source);
            bitmap.Freeze();
            return new LoadedImage { Path = null, Name = "Clipboard " + number, Bitmap = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight };
        }
    }
}
