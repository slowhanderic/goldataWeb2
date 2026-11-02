using System.Text.RegularExpressions;

namespace Goldata.Application.Identity;

public sealed record LegacyAccount(string Login, string PasswordHash, string DisplayName, string Role, string Rank);

/// <summary>
/// Reads the hard-coded accounts out of the legacy <c>login.php</c> <c>check_user()</c> (one-time import, runbook step 7).
/// Each branch looks like:
/// <code>if( $user_name == 'x' &amp;&amp; password_verify($password, '$2y$10$…')) { … $_SESSION['username'] = '…'; $_SESSION['role'] = '…'; $_SESSION['rank'] = '…'; …}</code>
/// </summary>
public static partial class LegacyLoginParser
{
    [GeneratedRegex(@"\$user_name\s*==\s*'(?<login>[^']+)'\s*&&\s*password_verify\(\s*\$password\s*,\s*'(?<hash>\$2[aby]\$\d{2}\$[./A-Za-z0-9]{53})'\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BranchRegex();

    [GeneratedRegex(@"\$_SESSION\['(?<key>username|role|rank)'\]\s*=\s*'(?<value>[^']*)'", RegexOptions.CultureInvariant)]
    private static partial Regex SessionRegex();

    public static IReadOnlyList<LegacyAccount> Parse(string php)
    {
        var branches = BranchRegex().Matches(php);
        var result = new List<LegacyAccount>();
        for (var i = 0; i < branches.Count; i++)
        {
            var b = branches[i];
            var end = i + 1 < branches.Count ? branches[i + 1].Index : php.Length;
            var body = php[(b.Index + b.Length)..end];
            var values = new Dictionary<string, string>();
            foreach (Match s in SessionRegex().Matches(body))
                values.TryAdd(s.Groups["key"].Value, s.Groups["value"].Value.Trim());

            if (!values.TryGetValue("username", out var name) || !values.TryGetValue("role", out var role))
                throw new FormatException($"login.php: account '{b.Groups["login"].Value}' has no username/role.");

            result.Add(new LegacyAccount(
                b.Groups["login"].Value.Trim(), b.Groups["hash"].Value, name, role,
                values.GetValueOrDefault("rank", "员工")));
        }
        var dup = result.GroupBy(a => a.Login).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new FormatException($"login.php: duplicate login '{dup.Key}'.");
        return result;
    }
}
