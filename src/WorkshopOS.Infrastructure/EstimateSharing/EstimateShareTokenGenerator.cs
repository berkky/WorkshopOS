using System.Security.Cryptography;
using WorkshopOS.Application.EstimateSharing;

namespace WorkshopOS.Infrastructure.EstimateSharing;

public sealed class EstimateShareTokenGenerator : IEstimateShareTokenGenerator
{
    private const int TokenByteLength = 32;

    public string GenerateRawToken()
    {
        var bytes = new byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return EncodeBase64Url(bytes);
    }

    public string EncodeBase64Url(byte[] rawBytes) =>
        Convert.ToBase64String(rawBytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
