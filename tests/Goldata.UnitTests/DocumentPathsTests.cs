using Goldata.Application.Documents;
using Goldata.Domain;

namespace Goldata.UnitTests;

public class DocumentPathsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "goldata-docroot");

    [Fact]
    public void Relative_path_uses_legacy_format_and_china_month()
    {
        // 2025-12-31 17:30 UTC = 2026-01-01 01:30 in Asia/Shanghai → folder 202601
        var path = DocumentPaths.BuildRelativePath("contracts", "合同.pdf", new DateTimeOffset(2025, 12, 31, 17, 30, 0, TimeSpan.Zero));
        path.ShouldBe("pdf_folder/contracts/202601/合同.pdf");
    }

    [Theory]
    [InlineData("pdf_folder/contracts/202301/a.pdf", true)]
    [InlineData("../pdf_folder/contracts/202301/a.pdf", true)]           // tolerated legacy prefix
    [InlineData("pdf_folder/../../etc/passwd", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("other/a.pdf", false)]
    [InlineData("", false)]
    public void Resolve_stays_under_the_root(string stored, bool ok)
    {
        var full = DocumentPaths.ResolveUnderRoot(Root, stored);
        if (ok) full.ShouldStartWith(Path.GetFullPath(Root));
        else full.ShouldBeNull();
    }

    [Theory]
    [InlineData("报价单.PDF")]
    [InlineData("图纸 v2.dwg")]
    public void Valid_names_pass(string name) => DocumentPaths.NormalizeFileName(name).ShouldBe(name);

    [Theory]
    [InlineData("a.exe")]
    [InlineData("..pdf")]
    [InlineData("a#b.pdf")]
    [InlineData("a;b.pdf")]
    [InlineData(".hidden.pdf")]
    [InlineData("表情😀.pdf")]
    public void Invalid_names_fail(string name) => Should.Throw<AppError>(() => DocumentPaths.NormalizeFileName(name));

    [Fact]
    public void Directory_parts_are_stripped_from_uploaded_names() =>
        DocumentPaths.NormalizeFileName("C:\\Users\\x\\合同.pdf").ShouldBe("合同.pdf");

    [Fact]
    public void Names_are_normalised_to_nfc()
    {
        var nfd = "e\u0301.pdf";                       // é as e + combining accent (macOS SMB)
        DocumentPaths.NormalizeFileName(nfd).ShouldBe("\u00e9.pdf");
    }

    [Fact]
    public void Suggested_names_keep_the_extension() => DocumentPaths.Suggest("合同.pdf", 2).ShouldBe("合同 (2).pdf");

    [Fact]
    public void Magic_bytes_are_checked()
    {
        DocumentPaths.LooksLike("a.pdf", "%PDF-1.7"u8).ShouldBeTrue();
        DocumentPaths.LooksLike("a.pdf", "MZ\0\0"u8).ShouldBeFalse();
        DocumentPaths.LooksLike("a.docx", "PK\u0003\u0004"u8).ShouldBeTrue();
    }
}
