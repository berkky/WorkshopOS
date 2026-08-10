namespace WorkshopOS.Application.InspectionMedia;

public static class PhotoUploadPolicy
{
    public const long MaxPhotoBytes = 8 * 1024 * 1024;

    public const int MaxPhotosPerItem = 6;

    public const int MaxPhotosPerInspection = 40;

    public const int MaxCaptionLength = 500;

    public const string JpegContentType = "image/jpeg";

    public const string PngContentType = "image/png";

    public const string WebpContentType = "image/webp";
}
