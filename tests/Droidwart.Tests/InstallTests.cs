using Droidwart.Worker.Installs;
using Xunit;

namespace Droidwart.Tests;

public class InstallTests
{
    [Theory]
    [InlineData("sha256:ABCdef0123", "abcdef0123")]
    [InlineData("SHA256:ff", "ff")]
    [InlineData("sha512:ff", null)]
    [InlineData("sha256:", null)]
    [InlineData(null, null)]
    public void ParseSha256_ReadsGitHubDigest(string? digest, string? expected)
    {
        Assert.Equal(expected, ApkDownloader.ParseSha256(digest));
    }

    [Theory]
    [InlineData("com.termux/.app.TermuxActivity", true)]
    [InlineData("com.topjohnwu.magisk/.ui.MainActivity", true)]
    [InlineData("com.termux/.App; reboot", false)]
    [InlineData("com.termux", false)]
    [InlineData(null, false)]
    public void LaunchComponent_IsValidatedBeforeUseInShell(string? component, bool valid)
    {
        Assert.Equal(valid, InstallService.IsValidLaunchComponent(component));
    }

    [Fact]
    public void Queue_RejectsDuplicateUntilCompleted()
    {
        var queue = new InstallQueue();
        var request = new InstallRequest("android-ip-cam-garden", "termux", "test");

        Assert.True(queue.TryEnqueue(request));
        Assert.False(queue.TryEnqueue(request with { Source = "again" }));
        Assert.True(queue.TryEnqueue(request with { AppId = "obtainium" }));

        queue.Complete(request);
        Assert.True(queue.TryEnqueue(request));
    }

    [Fact]
    public void FailureMessage_GetsHintForSignatureMismatch()
    {
        var message = InstallService.DescribeInstallFailure("Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]");

        Assert.Contains("different key", message);
    }

    [Fact]
    public async Task ProgressReporter_ThrottlesAndKeepsOrder()
    {
        var published = new List<int>();
        var reporter = new ProgressReporter(async p =>
        {
            await Task.Yield();
            lock (published)
            {
                published.Add(p);
            }
        });

        foreach (var value in new[] { 0, 1, 2, 5, 6, 12, 50, 51, 100 })
        {
            reporter.Report(value);
        }

        await reporter.FlushAsync();

        Assert.Equal(new[] { 0, 5, 12, 50, 100 }, published);
    }
}
