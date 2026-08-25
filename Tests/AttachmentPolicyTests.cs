using gestao_chamados.Data;

namespace gestao_chamados.Tests;

public class AttachmentPolicyTests
{
    [Theory]
    [InlineData("nota.txt")]
    [InlineData("foto.JPG")] // extensão deve ser case-insensitive
    [InlineData("relatorio.pdf")]
    [InlineData("planilha.xlsx")]
    public void IsAllowed_AcceptsWhitelistedExtensionsWithinSizeLimit(string fileName)
    {
        Assert.True(AttachmentPolicy.IsAllowed(fileName, sizeInBytes: 1024));
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("pagina.html")]
    [InlineData("semextensao")]
    public void IsAllowed_RejectsExtensionsOutsideWhitelist(string fileName)
    {
        Assert.False(AttachmentPolicy.IsAllowed(fileName, sizeInBytes: 1024));
    }

    [Fact]
    public void IsAllowed_RejectsFileLargerThanLimit()
    {
        Assert.False(AttachmentPolicy.IsAllowed("grande.pdf", AttachmentPolicy.MaxSizeInBytes + 1));
    }

    [Fact]
    public void IsAllowed_RejectsEmptyFile()
    {
        Assert.False(AttachmentPolicy.IsAllowed("vazio.pdf", 0));
    }

    [Fact]
    public void IsAllowed_AcceptsFileExactlyAtSizeLimit()
    {
        Assert.True(AttachmentPolicy.IsAllowed("limite.pdf", AttachmentPolicy.MaxSizeInBytes));
    }
}
