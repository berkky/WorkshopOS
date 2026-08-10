namespace WorkshopOS.Application.InspectionMedia;

public enum DetectedPhotoFormat
{
    None = 0,
    Jpeg = 1,
    Png = 2,
    Webp = 3,
}

public static class PhotoSignatureValidator
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static DetectedPhotoFormat DetectFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return DetectedPhotoFormat.Jpeg;
        }

        if (header.Length >= PngSignature.Length && header[..PngSignature.Length].SequenceEqual(PngSignature))
        {
            return DetectedPhotoFormat.Png;
        }

        if (header.Length >= 12
            && header[0] == (byte)'R'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'F'
            && header[8] == (byte)'W'
            && header[9] == (byte)'E'
            && header[10] == (byte)'B'
            && header[11] == (byte)'P')
        {
            return DetectedPhotoFormat.Webp;
        }

        return DetectedPhotoFormat.None;
    }

    public static string ToContentType(DetectedPhotoFormat format) =>
        format switch
        {
            DetectedPhotoFormat.Jpeg => PhotoUploadPolicy.JpegContentType,
            DetectedPhotoFormat.Png => PhotoUploadPolicy.PngContentType,
            DetectedPhotoFormat.Webp => PhotoUploadPolicy.WebpContentType,
            _ => string.Empty,
        };

    public static string ToFileExtension(DetectedPhotoFormat format) =>
        format switch
        {
            DetectedPhotoFormat.Jpeg => ".jpg",
            DetectedPhotoFormat.Png => ".png",
            DetectedPhotoFormat.Webp => ".webp",
            _ => string.Empty,
        };

    public static bool IsSvgContentType(string? contentType) =>
        string.Equals(contentType, "image/svg+xml", StringComparison.OrdinalIgnoreCase);

    public static bool IsSvgFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName)
        && fileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
}
