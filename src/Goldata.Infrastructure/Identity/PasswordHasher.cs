namespace Goldata.Infrastructure.Identity;

/// <summary>
/// bcrypt, kept PHP-compatible: <c>$2y$</c>, cost 12, so the legacy <c>password_verify()</c> can read the same
/// <c>app_users.password_hash</c> (002-spec §5.1, §5.5).
/// </summary>
public static class PasswordHasher
{
    public const int WorkFactor = 12;

    // A valid hash of a random value: verifying against it keeps the timing of unknown logins the same.
    private static readonly string Dummy = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N"), BCrypt.Net.BCrypt.GenerateSalt(WorkFactor, 'y'));

    public static string Hash(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password, BCrypt.Net.BCrypt.GenerateSalt(WorkFactor, 'y'));

    /// <summary>Verifies <c>$2y$/$2a$/$2b$</c> hashes. Pass <c>null</c> for an unknown user to spend the same time.</summary>
    public static bool Verify(string password, string? hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password ?? "", hash ?? Dummy) && hash is not null; }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    /// <summary>True for legacy hashes with a lower cost (e.g. <c>$2y$10$</c>): re-hashed on the next good login.</summary>
    public static bool NeedsRehash(string hash) =>
        hash.Length < 7 || !int.TryParse(hash.AsSpan(4, 2), out var cost) || cost < WorkFactor || !hash.StartsWith("$2y$", StringComparison.Ordinal);
}
