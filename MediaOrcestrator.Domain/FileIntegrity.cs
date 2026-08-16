using System.Security.Cryptography;

namespace MediaOrcestrator.Domain;

public static class FileIntegrity
{
    private const string Sha256Prefix = "sha256:";

    public static bool EnsureMatches(string filePath, string? expectedDigest)
    {
        var expected = ParseSha256(expectedDigest);

        if (expected == null)
        {
            return false;
        }

        var actual = ComputeSha256(filePath);

        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        throw new InvalidDataException(
            $"Контрольная сумма файла «{Path.GetFileName(filePath)}» не совпала: ожидалась {expected}, получена {actual}");
    }

    public static string ComputeSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();

        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string? ParseSha256(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
        {
            return null;
        }

        var value = digest.Trim();

        if (!value.StartsWith(Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        value = value[Sha256Prefix.Length..];

        return value.Length == 64 ? value : null;
    }
}
