using System.Security.Cryptography;

namespace TG.Control.Server;

public static class PasswordHasher
{
    private const string Prefix = "$pbkdf2-sha256$";
    private const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        if (iterations < 100_000) throw new ArgumentOutOfRangeException(nameof(iterations));
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Prefix}{iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(encoded) ||
            !encoded.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = encoded[Prefix.Length..].Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations) || iterations < 100_000) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            if (salt.Length < SaltSize || expected.Length < HashSize) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
    }
}
