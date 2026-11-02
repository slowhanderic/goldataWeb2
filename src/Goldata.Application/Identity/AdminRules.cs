using System.Text.RegularExpressions;
using Goldata.Application.Permissions;
using Goldata.Domain;

namespace Goldata.Application.Identity;

/// <summary>Validation rules for the user / role admin page (REQ-ADM-1…14, 002-spec §5.4).</summary>
public static partial class AdminRules
{
    public const int MinPasswordLength = 10;
    /// <summary>Shortest goldata column a display name is written to is TEXT; keep names short and clean anyway.</summary>
    public const int MaxDisplayNameLength = 32;

    [GeneratedRegex("^[a-z0-9._-]{2,32}$")]   // 2: legacy login `ci`
    private static partial Regex LoginRegex();

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "12345678910", "0123456789", "qwertyuiop", "1q2w3e4r5t", "password12", "password123",
        "goldata123", "goldata2026", "admin12345", "aaaaaaaaaa", "1111111111", "abc1234567", "a123456789",
    };

    public static void ValidateLogin(string login)
    {
        if (!LoginRegex().IsMatch(login ?? ""))
            throw AppError.Validation("登入名只能包含小写字母、数字、点、下划线或连字符，长度 2–32");
    }

    public static string NormalizeDisplayName(string? name)
    {
        var n = (name ?? "").Trim().Normalize(System.Text.NormalizationForm.FormC);
        if (n.Length == 0) throw AppError.Validation("姓名不能为空");
        if (n.Length > MaxDisplayNameLength) throw AppError.Validation($"姓名不能超过 {MaxDisplayNameLength} 个字符");
        if (LegacyText.HasSupplementaryChars(n)) throw AppError.Validation("姓名不支持表情符号或生僻字");
        if (n.Contains('-') || n.Contains('\'') || n.Contains('"') || n.Contains('<') || n.Contains('>') || n.Contains('&'))
            throw AppError.Validation("姓名不能包含 - ' \" < > & 等符号");
        return n;
    }

    public static string NormalizeRoleName(string? name)
    {
        var n = (name ?? "").Trim().Normalize(System.Text.NormalizationForm.FormC);
        if (n.Length is 0 or > 32) throw AppError.Validation("角色名称长度 1–32");
        if (n.Contains('-')) throw AppError.Validation("角色名称不能包含 '-'（修改记录用 '角色-姓名' 格式）");
        if (LegacyText.HasSupplementaryChars(n)) throw AppError.Validation("角色名称不支持表情符号或生僻字");
        return n;
    }

    public static void ValidateNewPassword(string password, string? login = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            throw AppError.Validation($"密码至少 {MinPasswordLength} 位");
        if (password.Length > 72) throw AppError.Validation("密码不能超过 72 位");   // bcrypt limit
        if (CommonPasswords.Contains(password) || (login is not null && password.Contains(login, StringComparison.OrdinalIgnoreCase)))
            throw AppError.Validation("密码太常见或包含登入名，请换一个");
        if (password.Distinct().Count() < 4) throw AppError.Validation("密码太简单，请换一个");
    }

    /// <summary>Unknown codes, and write codes on a read-only role, are rejected (REQ-ADM-11).</summary>
    public static void ValidateGrants(IEnumerable<RoleGrant> grants, bool roleIsReadOnly, IReadOnlySet<string> validRanks)
    {
        foreach (var g in grants)
        {
            if (g.Rank != EffectivePermissions.AllRanks && !validRanks.Contains(g.Rank))
                throw AppError.Validation($"未知职级：{g.Rank}");
            var def = PermissionCatalog.Find(g.Permission) ?? throw AppError.Validation($"未知权限：{g.Permission}");
            if (roleIsReadOnly && def.IsWrite)
                throw AppError.Validation($"只读角色不能拥有写入权限：{def.Label}");
        }
    }

    /// <summary>Generated initial / reset password: 16 chars, no look-alike characters.</summary>
    public static string GeneratePassword()
    {
        const string alphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(16, 0, (span, _) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
        });
    }

    /// <summary>The admin pair that must always remain held by at least one active user.</summary>
    public static bool IsAdmin(IReadOnlySet<string> permissions) =>
        permissions.Contains(PermissionCatalog.UsersAdmin) && permissions.Contains(PermissionCatalog.RolesAdmin);
}
