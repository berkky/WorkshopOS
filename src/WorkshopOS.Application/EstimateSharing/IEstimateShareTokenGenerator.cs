namespace WorkshopOS.Application.EstimateSharing;

public interface IEstimateShareTokenGenerator
{
    string GenerateRawToken();

    string EncodeBase64Url(byte[] rawBytes);
}
