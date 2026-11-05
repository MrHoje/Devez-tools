using System.IO;
using DevezTools.Services;

namespace DevezTools.Tests;

public class UpdateServiceTests
{
    private static readonly string Sha = new('a', 64);

    [Fact]
    public void Parses_manifest_written_by_deploy_script()
    {
        UpdateService.Release? release = UpdateService.Parse($$"""{"version":"1.2.3","sha256":"{{Sha.ToUpperInvariant()}}","notes":"첫 줄\n둘째 줄"}""");
        Assert.NotNull(release);
        Assert.Equal(new Version(1, 2, 3), release.Version);
        Assert.Equal(Sha, release.Sha256);
        Assert.Equal("첫 줄\n둘째 줄", release.Notes);
    }

    [Fact]
    public void Four_part_version_equals_three_part()
    {
        UpdateService.Release? release = UpdateService.Parse($$"""{"version":"1.2.3.0","sha256":"{{Sha}}"}""");
        Assert.Equal(new Version(1, 2, 3), release!.Version);
    }

    [Theory]
    [InlineData("""{"version":"1.2.3"}""")]                        // 해시 없음
    [InlineData("""{"version":"1.2.3","sha256":"abc"}""")]          // 해시 길이 틀림
    [InlineData("""{"version":"x","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    [InlineData("""{"version":1,"sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""")]
    [InlineData("""[]""")]
    [InlineData("<html>")]
    public void Rejects_broken_manifest(string json) => Assert.Null(UpdateService.Parse(json));

    [Fact]
    public void Hash_matches_deploy_script_format()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, "abc");
        // PowerShell (Get-FileHash -Algorithm SHA256).Hash.ToLower() 와 같은 소문자 hex
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", UpdateService.Sha256Of(file));
        File.Delete(file);
    }
}
