using System.Security.Cryptography;
using System.Text;

namespace WorkshopOS.Application.EstimateSharing;

public static class EstimateShareTokenHasher
{
    public static string HashToHex(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool FixedTimeEqualsHex(string presentedHashHex, string storedHashHex)
    {
        if (presentedHashHex.Length != storedHashHex.Length)
        {
            return false;
        }

        try
        {
            var presented = Convert.FromHexString(presentedHashHex);
            var stored = Convert.FromHexString(storedHashHex);
            return presented.Length == stored.Length
                   && CryptographicOperations.FixedTimeEquals(presented, stored);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
