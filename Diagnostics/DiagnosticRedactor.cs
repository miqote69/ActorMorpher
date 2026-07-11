using System.Security.Cryptography;
using System.Text;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticRedactor(bool includeActorNames, bool includeRawAddresses, string sessionSalt)
{
    public string ActorKey(LogicalActorKey key, string? actorName = null)
    {
        var kind = key.ObjectKind.ToString();
        if (includeActorNames && !string.IsNullOrWhiteSpace(actorName))
            return $"{kind}:{NormalizeText(actorName, 100)}#{Hash(key)}";
        return $"{kind}#{Hash(key)}";
    }

    public string? Address(nint address)
        => includeRawAddresses ? $"0x{address:X}" : null;

    public string RedactPath(string path)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile)
            ? path
            : path.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeText(string text, int maximumLength)
    {
        var normalized = new string(text.Where(character => !char.IsControl(character) || character == ' ').ToArray())
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private string Hash(LogicalActorKey key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionSalt}:{key}"));
        return Convert.ToHexString(bytes.AsSpan(0, 3));
    }
}
