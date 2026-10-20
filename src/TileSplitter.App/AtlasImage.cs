using System.IO;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace TileSplitter.App;

public sealed record AtlasImage(string Path, BitmapSource Bitmap, string Fingerprint, bool HasOrientation)
{
    public static AtlasImage Load(string path)
    {
        using var stream = File.OpenRead(path);
        string fingerprint = Convert.ToHexString(SHA256.HashData(stream));
        stream.Position = 0;

        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        bool hasOrientation = false;
        if (frame.Metadata is BitmapMetadata metadata)
        {
            try
            {
                hasOrientation = metadata.ContainsQuery("/app1/ifd/{ushort=274}") || metadata.ContainsQuery("/ifd/{ushort=274}");
            }
            catch (NotSupportedException)
            {
                // Some decoders expose metadata without supporting EXIF queries.
            }
        }

        frame.Freeze();
        return new(System.IO.Path.GetFullPath(path), frame, fingerprint, hasOrientation);
    }
}
