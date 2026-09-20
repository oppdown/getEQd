using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GetEQd.Updates;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// Every branch of the update check, including the ones that are easy to leave
    /// unhandled: rate limiting, a malformed feed, and a version that cannot be compared.
    /// </summary>
    public class UpdateCheckerTests
    {
        private static readonly Version Running = new Version(0, 2, 0);

        private const string ReleaseJson = @"{
  ""tag_name"": ""v0.3.0"",
  ""html_url"": ""https://github.com/oppdown/getEQd/releases/tag/v0.3.0"",
  ""assets"": [
    { ""name"": ""getEQd.exe"", ""browser_download_url"": ""https://example.invalid/getEQd.exe"" },
    { ""name"": ""getEQd-0.3.0.msi"", ""browser_download_url"": ""https://example.invalid/getEQd-0.3.0.msi"" }
  ]
}";

        [Fact]
        public void ANewerReleaseIsOfferedWithTheInstallerFirst()
        {
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 200, ReleaseJson);

            Assert.Equal(UpdateState.UpdateAvailable, result.State);
            Assert.True(result.HasUpdate);
            Assert.Equal("0.3.0", result.LatestVersion);
            Assert.Equal("https://example.invalid/getEQd-0.3.0.msi", result.DownloadUrl);
            Assert.Equal("https://github.com/oppdown/getEQd/releases/tag/v0.3.0", result.ReleaseUrl);
            Assert.Contains("0.3.0", result.Message);
            Assert.Contains("0.2.0", result.Message);
        }

        [Fact]
        public void TheSameReleaseIsReportedAsCurrent()
        {
            const string same = @"{ ""tag_name"": ""v0.2.0"", ""html_url"": ""https://example.invalid"", ""assets"": [] }";
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 200, same);

            Assert.Equal(UpdateState.UpToDate, result.State);
            Assert.False(result.HasUpdate);
            Assert.Null(result.DownloadUrl);
        }

        [Fact]
        public void AnOlderReleaseIsNotOfferedAsAnUpdate()
        {
            const string older = @"{ ""tag_name"": ""v0.1.3"", ""html_url"": ""https://example.invalid"" }";
            Assert.Equal(UpdateState.UpToDate, UpdateChecker.Evaluate(Running, 200, older).State);
        }

        [Fact]
        public void TheBareExecutableIsUsedWhenThereIsNoInstaller()
        {
            const string exeOnly = @"{
  ""tag_name"": ""v0.3.0"",
  ""assets"": [ { ""name"": ""getEQd.exe"", ""browser_download_url"": ""https://example.invalid/getEQd.exe"" } ]
}";
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 200, exeOnly);

            Assert.Equal(UpdateState.UpdateAvailable, result.State);
            Assert.Equal("https://example.invalid/getEQd.exe", result.DownloadUrl);
        }

        [Theory]
        [InlineData(403)]
        [InlineData(429)]
        public void RateLimitingGetsItsOwnStateRatherThanAGenericFailure(int statusCode)
        {
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, statusCode, "{}");

            Assert.Equal(UpdateState.RateLimited, result.State);
            Assert.Contains("Try again later", result.Message);
        }

        [Fact]
        public void NoPublishedReleaseIsItsOwnState()
        {
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 404, "");
            Assert.Equal(UpdateState.NoReleases, result.State);
        }

        [Fact]
        public void AServerErrorIsReportedWithItsStatusCode()
        {
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 500, "");

            Assert.Equal(UpdateState.Failed, result.State);
            Assert.Contains("500", result.Message);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("[1,2,3]")]
        [InlineData(@"{ ""html_url"": ""https://example.invalid"" }")]
        [InlineData(@"{ ""tag_name"": ""nightly"" }")]
        [InlineData(@"{ ""tag_name"": """" }")]
        public void AMalformedFeedNeverCrashesAndIsNamedAsSuch(string body)
        {
            UpdateCheckResult result = UpdateChecker.Evaluate(Running, 200, body);
            Assert.Equal(UpdateState.Malformed, result.State);
        }

        [Fact]
        public void AnEmptyBodyIsMalformedRatherThanAnException()
        {
            Assert.Equal(UpdateState.Malformed, UpdateChecker.Evaluate(Running, 200, null).State);
            Assert.Equal(UpdateState.Malformed, UpdateChecker.Evaluate(Running, 200, "").State);
        }

        [Theory]
        [InlineData("v0.2.0", 0, 2, 0)]
        [InlineData("0.2.0", 0, 2, 0)]
        [InlineData("V1.4.9", 1, 4, 9)]
        [InlineData("v0.3", 0, 3, 0)]
        [InlineData("v0.3.0.1", 0, 3, 0)]
        public void ReleaseTagsAreParsed(string tag, int major, int minor, int build)
        {
            Assert.True(UpdateChecker.TryParseVersion(tag, out Version version));
            Assert.Equal(new Version(major, minor, build), version);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("nightly")]
        [InlineData("v")]
        [InlineData("vNext")]
        public void UnreadableTagsAreRejected(string tag)
        {
            Assert.False(UpdateChecker.TryParseVersion(tag, out _));
        }

        [Fact]
        public void EveryStateCarriesCopyAndALinkToLookAt()
        {
            foreach (UpdateState state in Enum.GetValues<UpdateState>())
            {
                UpdateCheckResult result = state switch
                {
                    UpdateState.UpToDate => UpdateChecker.Evaluate(Running, 200, @"{""tag_name"":""v0.2.0""}"),
                    UpdateState.UpdateAvailable => UpdateChecker.Evaluate(Running, 200, ReleaseJson),
                    UpdateState.RateLimited => UpdateChecker.Evaluate(Running, 429, ""),
                    UpdateState.NoReleases => UpdateChecker.Evaluate(Running, 404, ""),
                    UpdateState.Malformed => UpdateChecker.Evaluate(Running, 200, "junk"),
                    _ => UpdateChecker.Evaluate(Running, 500, "")
                };

                Assert.False(string.IsNullOrWhiteSpace(result.Message), state + " has no message.");
                Assert.False(string.IsNullOrWhiteSpace(result.ReleaseUrl), state + " has no release link.");
            }
        }

        [Fact]
        public void TheRunningBuildHasAComparableVersion()
        {
            Version current = UpdateChecker.CurrentVersion;
            Assert.True(current >= new Version(0, 2, 0));
            Assert.True(current.Build >= 0);
        }

        [Fact]
        public void ADeadNetworkBecomesTheOfflineStateRatherThanAStuckCheck()
        {
            UpdateCheckResult noRoute = UpdateChecker.DescribeFailure(new HttpRequestException("no route"));
            Assert.Equal(UpdateState.Offline, noRoute.State);
            Assert.Contains("offline", noRoute.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(noRoute.HasUpdate);

            UpdateCheckResult timedOut = UpdateChecker.DescribeFailure(new TaskCanceledException("too slow"));
            Assert.Equal(UpdateState.Offline, timedOut.State);
            Assert.Contains("timed out", timedOut.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(((int)UpdateChecker.RequestTimeout.TotalSeconds).ToString(), timedOut.Message);
        }

        [Fact]
        public void TheRequestIsBoundedByATimeout()
        {
            Assert.True(UpdateChecker.RequestTimeout > TimeSpan.Zero);
            Assert.True(UpdateChecker.RequestTimeout <= TimeSpan.FromSeconds(30));
        }

        [Fact]
        public void AnUnexpectedFailureIsReportedAsFailedRatherThanOffline()
        {
            UpdateCheckResult result = UpdateChecker.DescribeFailure(new InvalidOperationException("something odd"));
            Assert.Equal(UpdateState.Failed, result.State);
            Assert.Contains("something odd", result.Message);
        }
    }
}
