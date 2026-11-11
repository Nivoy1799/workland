using System.Security.Cryptography;
using System.Text;

namespace Worktown.Gateway.Cache;

/// <summary>Stable cache key: SHA-256 (lowercase hex) of a UTF-8, "|"-separated preimage.</summary>
public static class CacheKey
{
    public const string Prefix = "gw1";

    /// <summary>Preimage <c>gw1|{contentType}|{schema}|{prompt}|{model}|{itemHash}</c>.</summary>
    public static string Compute(string contentType, int schemaVersion, int promptVersion,
                                 string modelId, string itemHash)
    {
        var preimage = string.Join('|', Prefix, contentType,
            schemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            promptVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            modelId, itemHash);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(preimage)));
    }
}
