using System.Text;
using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class CompanyArchiveTests
{
    [Fact]
    public void DestinationToken_RoundTripsChineseNestedPath()
    {
        const string relative = @"公司甲\项目一\合同";

        var token = DestinationToken.Encode(relative);

        Assert.Equal(relative, DestinationToken.Decode(token));
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void DestinationToken_RejectsParentTraversal()
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(@"..\外部"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Throws<ArgumentException>(() => DestinationToken.Decode(token));
    }

    [Fact]
    public void ResolveDestination_StaysInsideRoot()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.DirectoryAt("archive");

        var result = CompanyArchive.ResolveDestination(root, @"公司甲\项目一");

        Assert.Equal(Path.Combine(root, "公司甲", "项目一"), result);
        Assert.True(CompanyArchive.IsWithinRoot(result, root));
    }
}
