using Goldata.Application.Identity;
using Goldata.Domain;
using Goldata.Infrastructure.Identity;

namespace Goldata.UnitTests;

public class IdentityTests
{
    // Synthetic login.php in the legacy shape. The hashes are made up (never use real ones in tests).
    private const string Hash1 = "$2y$10$abcdefghijklmnopqrstuuABCDEFGHIJKLMNOPQRSTUVWXYZ01234";
    private const string Hash2 = "$2y$10$zyxwvutsrqponmlkjihgfeZYXWVUTSRQPONMLKJIHGFEDCBA98765";

    private static readonly string LoginPhp = $$"""
        <?php
        function check_user($user_name,$password)
        {
            if( $user_name == 'admin' && password_verify($password, '{{Hash1}}'))
            {
                $_SESSION['valid'] = true;
                $_SESSION['timeout'] = time();
                $_SESSION['username'] = '测试管理' ;
                $_SESSION['role'] = '管理' ;
                $_SESSION['rank'] = '总监' ;
                return ;
            }else if( $user_name == 'cg-reader' &&  password_verify($password, '{{Hash2}}'))
            {
                $_SESSION['valid'] = true;
                $_SESSION['username'] = '采购部查阅用户' ;
                $_SESSION['role'] = '采购查阅' ;
                $_SESSION['rank'] = '员工' ;
                return '采购部查阅用户';
            }
            else
            {
                return 'nobody';
            }
        }
        """;

    [Fact]
    public void Parser_reads_every_account_with_its_hash_role_and_rank()
    {
        var accounts = LegacyLoginParser.Parse(LoginPhp);
        accounts.Count.ShouldBe(2);
        accounts[0].ShouldBe(new LegacyAccount("admin", Hash1, "测试管理", "管理", "总监"));
        accounts[1].ShouldBe(new LegacyAccount("cg-reader", Hash2, "采购部查阅用户", "采购查阅", "员工"));
    }

    [Fact]
    public void Parser_rejects_duplicate_logins() =>
        Should.Throw<FormatException>(() => LegacyLoginParser.Parse(LoginPhp + LoginPhp.Replace("'cg-reader'", "'x'")));

    [Fact]
    public void Hashes_are_php_compatible_2y_cost_12_and_verify()
    {
        var hash = PasswordHasher.Hash("Correct-horse-9");
        hash.ShouldStartWith("$2y$12$");
        PasswordHasher.Verify("Correct-horse-9", hash).ShouldBeTrue();
        PasswordHasher.Verify("wrong", hash).ShouldBeFalse();
        PasswordHasher.NeedsRehash(hash).ShouldBeFalse();
    }

    [Fact]
    public void Legacy_cost_10_hashes_verify_and_are_flagged_for_rehash()
    {
        var legacy = BCrypt.Net.BCrypt.HashPassword("old-pass-123", BCrypt.Net.BCrypt.GenerateSalt(10, 'y'));
        legacy.ShouldStartWith("$2y$10$");
        PasswordHasher.Verify("old-pass-123", legacy).ShouldBeTrue();
        PasswordHasher.NeedsRehash(legacy).ShouldBeTrue();
    }

    [Fact]
    public void Unknown_user_verification_is_false() => PasswordHasher.Verify("x", null).ShouldBeFalse();

    [Theory]
    [InlineData("chendx", true)]
    [InlineData("cg-reader", true)]
    [InlineData("ci", true)]          // legacy 2-char login
    [InlineData("a", false)]
    [InlineData("Admin", false)]
    [InlineData("a b", false)]
    public void Login_rule(string login, bool ok)
    {
        if (ok) Should.NotThrow(() => AdminRules.ValidateLogin(login));
        else Should.Throw<AppError>(() => AdminRules.ValidateLogin(login));
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaa")]
    [InlineData("xchendx-2026x")]
    public void Weak_passwords_are_rejected(string pw) =>
        Should.Throw<AppError>(() => AdminRules.ValidateNewPassword(pw, "chendx"));

    [Fact]
    public void Generated_passwords_pass_the_policy()
    {
        for (var i = 0; i < 50; i++) Should.NotThrow(() => AdminRules.ValidateNewPassword(AdminRules.GeneratePassword(), "user"));
    }

    [Theory]
    [InlineData("新角色", true)]
    [InlineData("采购-查阅", false)]
    [InlineData("", false)]
    public void Role_names_cannot_contain_dash(string name, bool ok)
    {
        if (ok) AdminRules.NormalizeRoleName(name).ShouldBe(name);
        else Should.Throw<AppError>(() => AdminRules.NormalizeRoleName(name));
    }

    [Fact]
    public void Display_names_are_trimmed_and_validated()
    {
        AdminRules.NormalizeDisplayName("  陈冬霞 ").ShouldBe("陈冬霞");
        Should.Throw<AppError>(() => AdminRules.NormalizeDisplayName("王-五"));
        Should.Throw<AppError>(() => AdminRules.NormalizeDisplayName("😀"));
    }
}
