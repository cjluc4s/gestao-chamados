using gestao_chamados.Data;

namespace gestao_chamados.Tests;

public class CsvFormatterTests
{
    [Fact]
    public void Escape_KeepsPlainTextAsIs()
    {
        Assert.Equal("Impressora sem toner", CsvFormatter.Escape("Impressora sem toner"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Escape_ReturnsEmptyForNullOrEmpty(string? value)
    {
        Assert.Equal(string.Empty, CsvFormatter.Escape(value));
    }

    [Theory]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("diz \"oi\"", "\"diz \"\"oi\"\"\"")]
    [InlineData("linha1\nlinha2", "\"linha1\nlinha2\"")]
    [InlineData("linha1\r\nlinha2", "\"linha1\r\nlinha2\"")]
    public void Escape_QuotesSpecialCharacters(string value, string expected)
    {
        Assert.Equal(expected, CsvFormatter.Escape(value));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+1+1")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void Escape_NeutralizesFormulaInjection(string value)
    {
        var escaped = CsvFormatter.Escape(value).TrimStart('"');
        Assert.StartsWith("'", escaped);
    }
}
