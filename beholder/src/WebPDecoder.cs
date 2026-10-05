using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Beholder
{
    // Official libwebp decoder is embedded, versioned and hash-checked. No shell,
    // optional Windows codec, downloads, or executable code from dropped files.
    public static class WebPDecoder
    {
        private const string CodecHash = "17c1488bf84b7834e9aa908bb40afd0be2e55d57567d210e96336c56bb6ae993";
        private static readonly object CodecLock = new object();
        private sealed class Chunk { public string Kind; public byte[] Bytes; }
        private static int UInt24(byte[] b, int offset) { return b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16); }
        private static List<Chunk> Chunks(byte[] bytes, int offset, int limit)
        {
            var output = new List<Chunk>();
            while (offset + 8 <= limit)
            {
                uint length = BitConverter.ToUInt32(bytes, offset + 4);
                if (length > (long)limit - offset - 8) throw new FileFormatException("Truncated WebP chunk.");
                var content = new byte[(int)length]; Buffer.BlockCopy(bytes, offset + 8, content, 0, content.Length);
                output.Add(new Chunk { Kind = Encoding.ASCII.GetString(bytes, offset, 4), Bytes = content });
                offset += 8 + content.Length + (content.Length & 1);
            }
            return output;
        }

        public static LoadedImage Load(string path)
        {
            byte[] payload = File.ReadAllBytes(path);
            if (payload.Length < 20 || Encoding.ASCII.GetString(payload, 0, 4) != "RIFF" || Encoding.ASCII.GetString(payload, 8, 4) != "WEBP") throw new FileFormatException("Invalid WebP container.");
            uint declared = BitConverter.ToUInt32(payload, 4);
            if ((long)declared + 8 > payload.Length || declared < 4) throw new FileFormatException("Truncated WebP container.");
            var chunks = Chunks(payload, 12, (int)declared + 8);
            int width = 0, height = 0, frameWidth = 0, frameHeight = 0, left = 0, top = 0;
            bool animated = false;
            Color background = Colors.Transparent;
            byte[] framePayload = null;
            foreach (var chunk in chunks)
            {
                byte[] bytes = chunk.Bytes;
                if (chunk.Kind == "VP8X" && bytes.Length >= 10) { width = UInt24(bytes, 4) + 1; height = UInt24(bytes, 7) + 1; animated = (bytes[0] & 2) != 0; }
                else if (chunk.Kind == "VP8L" && bytes.Length >= 5 && width == 0) { uint bits = BitConverter.ToUInt32(bytes, 1); width = (int)(bits & 0x3fff) + 1; height = (int)((bits >> 14) & 0x3fff) + 1; }
                else if (chunk.Kind == "VP8 " && bytes.Length >= 10 && width == 0) { width = bytes[6] | ((bytes[7] & 0x3f) << 8); height = bytes[8] | ((bytes[9] & 0x3f) << 8); }
                else if (chunk.Kind == "ANIM" && bytes.Length >= 6) background = Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
                else if (chunk.Kind == "ANMF" && bytes.Length >= 24 && framePayload == null)
                {
                    animated = true; left = UInt24(bytes, 0) * 2; top = UInt24(bytes, 3) * 2;
                    frameWidth = UInt24(bytes, 6) + 1; frameHeight = UInt24(bytes, 9) + 1;
                    var subchunks = Chunks(bytes, 16, bytes.Length);
                    bool alpha = subchunks.Exists(x => x.Kind == "ALPH" || (x.Kind == "VP8L" && x.Bytes.Length >= 5 && (BitConverter.ToUInt32(x.Bytes, 1) & 0x10000000) != 0));
                    framePayload = StaticFrame(subchunks, frameWidth, frameHeight, alpha);
                }
            }
            if (width <= 0 || height <= 0 || (long)width * height > 100000000) throw new FileFormatException("WebP dimensions invalid or exceed the 100 megapixel safety limit.");
            if (animated && (framePayload == null || left + frameWidth > width || top + frameHeight > height)) throw new FileFormatException("Invalid animated WebP first frame.");
            double scale = Math.Min(1, 4096.0 / Math.Max(width, height));
            int outputWidth = Math.Max(1, (int)Math.Round(width * scale));
            int outputHeight = Math.Max(1, (int)Math.Round(height * scale));
            int decodeW = animated ? Math.Max(1, (int)Math.Round(frameWidth * scale)) : outputWidth;
            int decodeH = animated ? Math.Max(1, (int)Math.Round(frameHeight * scale)) : outputHeight;
            byte[] decoded = Decode(animated ? framePayload : payload, decodeW, decodeH);
            BitmapSource bitmap;
            using (var stream = new MemoryStream(decoded))
            {
                bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                bitmap.Freeze();
            }
            if (animated)
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, outputWidth, outputHeight));
                    dc.DrawImage(bitmap, new Rect(left * scale, top * scale, decodeW, decodeH));
                }
                var composite = new RenderTargetBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Pbgra32);
                composite.Render(visual); composite.Freeze(); bitmap = composite;
            }
            return new LoadedImage { Path = Path.GetFullPath(path), Name = Path.GetFileName(path), Bitmap = bitmap, Width = width, Height = height };
        }

        private static byte[] StaticFrame(List<Chunk> chunks, int width, int height, bool alpha)
        {
            using (var content = new MemoryStream())
            using (var writer = new BinaryWriter(content))
            {
                writer.Write(Encoding.ASCII.GetBytes("WEBPVP8X")); writer.Write(10);
                writer.Write((byte)(alpha ? 16 : 0)); writer.Write(new byte[3]);
                Write24(writer, width - 1); Write24(writer, height - 1);
                foreach (var chunk in chunks)
                {
                    if (chunk.Kind != "ALPH" && chunk.Kind != "VP8 " && chunk.Kind != "VP8L") continue;
                    writer.Write(Encoding.ASCII.GetBytes(chunk.Kind)); writer.Write(chunk.Bytes.Length); writer.Write(chunk.Bytes);
                    if ((chunk.Bytes.Length & 1) != 0) writer.Write((byte)0);
                }
                using (var result = new MemoryStream()) using (var final = new BinaryWriter(result)) { final.Write(Encoding.ASCII.GetBytes("RIFF")); final.Write((int)content.Length); final.Write(content.ToArray()); return result.ToArray(); }
            }
        }
        private static void Write24(BinaryWriter writer, int value) { writer.Write((byte)value); writer.Write((byte)(value >> 8)); writer.Write((byte)(value >> 16)); }

        private static string CodecPath()
        {
            lock (CodecLock)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Beholder", "codecs", "libwebp-1.6.0");
                string path = Path.Combine(folder, "dwebp.exe");
                if (File.Exists(path) && Hash(path) == CodecHash) return path;
                Directory.CreateDirectory(folder);
                string temporary = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
                using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("Beholder.WebP.gz"))
                {
                    if (resource == null) throw new IOException("The bundled WebP decoder is missing; rebuild Beholder with its codec resource.");
                    using (var compressed = new GZipStream(resource, CompressionMode.Decompress)) using (var output = File.Create(temporary)) compressed.CopyTo(output);
                }
                if (Hash(temporary) != CodecHash) { File.Delete(temporary); throw new IOException("WebP decoder integrity check failed."); }
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path); return path;
            }
        }
        private static string Hash(string path)
        {
            using (var sha = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        private static byte[] Decode(byte[] payload, int width, int height)
        {
            var start = new ProcessStartInfo { FileName = CodecPath(), Arguments = "-quiet -resize " + width + " " + height + " -o - -- -", UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            using (var output = new MemoryStream())
            {
                var read = Task.Run(() => process.StandardOutput.BaseStream.CopyTo(output));
                var error = process.StandardError.ReadToEndAsync();
                var write = Task.Run(delegate { process.StandardInput.BaseStream.Write(payload, 0, payload.Length); process.StandardInput.Close(); });
                if (!Task.WaitAll(new Task[] { read, error, write }, 20000)) { process.Kill(); process.WaitForExit(); throw new IOException("WebP decoding timed out."); }
                process.WaitForExit();
                if (process.ExitCode != 0) throw new FileFormatException("WebP could not be decoded: " + error.Result.Trim());
                return output.ToArray();
            }
        }
    }
}
