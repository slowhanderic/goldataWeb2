using Goldata.Domain;

namespace Goldata.UnitTests;

public class LegacyTextTests
{
    [Theory]
    [InlineData("A&B", "A&amp;B")]
    [InlineData("say \"hi\"", "say &quot;hi&quot;")]
    [InlineData("<b>", "&lt;b&gt;")]
    [InlineData("it's", "it's")]                  // ENT_COMPAT: single quote untouched (0 &#039; in the data)
    [InlineData("&amp;", "&amp;amp;")]            // double_encode = true, like PHP's default
    [InlineData("合同（一期）", "合同（一期）")]
    public void EncodeCompat_matches_php_htmlspecialchars_default(string input, string expected) =>
        LegacyText.EncodeCompat(input).ShouldBe(expected);

    [Fact]
    public void Decode_round_trips_encoded_text() =>
        LegacyText.Decode(LegacyText.EncodeCompat("A&B \"C\" <D> it's")).ShouldBe("A&B \"C\" <D> it's");

    [Theory]
    [InlineData("普通文字", false)]
    [InlineData("表情😀", true)]
    [InlineData("𠀀", true)]
    public void Supplementary_characters_are_detected(string s, bool expected) =>
        LegacyText.HasSupplementaryChars(s).ShouldBe(expected);

    [Fact]
    public void ContainsPattern_escapes_like_wildcards_and_encodes() =>
        LegacyText.ContainsPattern(" 50%_A&B ").ShouldBe("%50\\%\\_A&amp;B%");
}
