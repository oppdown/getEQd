using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GetEQd.Updates
{
    /// <summary>What the update check concluded. Every state has its own copy.</summary>
    public enum UpdateState
    {
        UpToDate,
        UpdateAvailable,
        Offline,
        RateLimited,
        NoReleases,
        Malformed,
        Failed
    }

    /// <summary>The outcome of one check, ready to show to a listener.</summary>
    public sealed class UpdateCheckResult
    {
        public UpdateCheckResult(
            UpdateState state,
            string message,
            string? latestVersion = null,
            string? releaseUrl = null,
            string? downloadUrl = null)
        {
            State = state;
            Message = message;
            LatestVersion = latestVersion;
            ReleaseUrl = releaseUrl;
            DownloadUrl = downloadUrl;
        }

        public UpdateState State { get; }
        public string Message { get; }
        public string? LatestVersion { get; }
        public string? ReleaseUrl { get; }
        public string? DownloadUrl { get; }

        public bool HasUpdate => State == UpdateState.UpdateAvailable;
    }

    /// <summary>
    /// Checks GitHub Releases for a newer build.
    ///
    /// The split matters: <see cref="Evaluate"/> is pure and carries every decision, so the
    /// state handling is unit tested without a network. <see cref="CheckAsync"/> only does
    /// the request and hands the response over.
    ///
    /// This deliberately does not copy the updater it was modelled on. That one has no
    /// request timeout, no distinct rate-limit state, and a status channel that can leave a
    /// spinner on screen forever. Here every path returns a result, including the ones that
    /// fail.
    /// </summary>
    public static class UpdateChecker
    {
        public const string ReleasesApiUrl = "https://api.github.com/repos/oppdown/getEQd/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/oppdown/getEQd/releases/latest";

        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);

        private static readonly HttpClient Client = CreateClient();

        /// <summary>The running build's version, three components wide.</summary>
        public static Version CurrentVersion
        {
            get
            {
                // The app's own version, not the caller's: a test host has its own.
                Version? version = typeof(UpdateChecker).Assembly.GetName().Version;
                return Normalize(version ?? new Version(0, 0, 0));
            }
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient { Timeout = RequestTimeout };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("getEQd/" + CurrentVersion);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return client;
        }

        public static async Task<UpdateCheckResult> CheckAsync(
            Version current,
            CancellationToken cancellationToken = default,
            string? endpoint = null)
        {
            string url = string.IsNullOrWhiteSpace(endpoint) ? ReleasesApiUrl : endpoint!;

            try
            {
                using HttpResponseMessage response = await Client
                    .GetAsync(url, cancellationToken)
                    .ConfigureAwait(false);

                string body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                return Evaluate(current, (int)response.StatusCode, body);
            }
            catch (Exception error)
            {
                return DescribeFailure(error);
            }
        }

        /// <summary>
        /// Turns a thrown request failure into a state. Kept separate from the request so
        /// the offline and timeout paths are covered by tests rather than by hoping the
        /// network breaks at the right moment.
        /// </summary>
        public static UpdateCheckResult DescribeFailure(Exception error)
        {
            if (error is OperationCanceledException)
            {
                return new UpdateCheckResult(
                    UpdateState.Offline,
                    "The update check timed out after " + (int)RequestTimeout.TotalSeconds +
                    " seconds. Check the connection and try again.",
                    releaseUrl: ReleasesPageUrl);
            }

            if (error is HttpRequestException)
            {
                return new UpdateCheckResult(
                    UpdateState.Offline,
                    "No connection to GitHub. The console still works offline.",
                    releaseUrl: ReleasesPageUrl);
            }

            return new UpdateCheckResult(
                UpdateState.Failed,
                "The update check failed: " + error.Message,
                releaseUrl: ReleasesPageUrl);
        }

        /// <summary>Every decision the check makes, given a response. Pure.</summary>
        public static UpdateCheckResult Evaluate(Version current, int statusCode, string? body)
        {
            if (statusCode == 403 || statusCode == 429)
            {
                return new UpdateCheckResult(
                    UpdateState.RateLimited,
                    "GitHub is limiting how often this can be checked. Try again later; nothing is wrong with the app.",
                    releaseUrl: ReleasesPageUrl);
            }

            if (statusCode == 404)
            {
                return new UpdateCheckResult(
                    UpdateState.NoReleases,
                    "No published release was found for this build yet.",
                    releaseUrl: ReleasesPageUrl);
            }

            if (statusCode != 200)
            {
                return new UpdateCheckResult(
                    UpdateState.Failed,
                    "The update check failed (HTTP " + statusCode + ").",
                    releaseUrl: ReleasesPageUrl);
            }

            string tag;
            string? page;
            string? download;
            try
            {
                using JsonDocument document = JsonDocument.Parse(body ?? string.Empty);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new JsonException("The release response was not an object.");
                }

                tag = ReadString(root, "tag_name");
                page = ReadString(root, "html_url");
                download = FindDownloadUrl(root);
            }
            catch (JsonException)
            {
                return new UpdateCheckResult(
                    UpdateState.Malformed,
                    "GitHub returned a release this build could not read.",
                    releaseUrl: ReleasesPageUrl);
            }

            if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out Version latest))
            {
                return new UpdateCheckResult(
                    UpdateState.Malformed,
                    "The latest release does not carry a version this build can compare against.",
                    releaseUrl: ReleasesPageUrl);
            }

            string running = Normalize(current).ToString(3);
            string latestLabel = latest.ToString(3);
            string releaseUrl = string.IsNullOrWhiteSpace(page) ? ReleasesPageUrl : page!;

            if (latest > Normalize(current))
            {
                return new UpdateCheckResult(
                    UpdateState.UpdateAvailable,
                    "getEQd " + latestLabel + " is available. This build is " + running + ".",
                    latestLabel,
                    releaseUrl,
                    download);
            }

            return new UpdateCheckResult(
                UpdateState.UpToDate,
                "This build (" + running + ") is the latest release.",
                latestLabel,
                releaseUrl);
        }

        /// <summary>Accepts "v0.2.0" or "0.2.0" and ignores anything else.</summary>
        public static bool TryParseVersion(string tag, out Version version)
        {
            version = new Version(0, 0, 0);
            if (string.IsNullOrWhiteSpace(tag)) return false;

            string trimmed = tag.Trim();
            if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(1);
            }

            if (!Version.TryParse(trimmed, out Version? parsed) || parsed == null) return false;
            if (parsed.Major < 0 || parsed.Minor < 0) return false;

            version = Normalize(parsed);
            return true;
        }

        /// <summary>
        /// The headless form, used to verify the check without a person at the keyboard.
        ///
        ///   getEQd.exe --check-updates
        ///   getEQd.exe --check-updates --as-version 0.1.3
        ///   getEQd.exe --check-updates --as-endpoint http://127.0.0.1:8080/403
        ///
        /// --as-version only changes what the answer is compared against, which is how the
        /// "an update is available" branch gets exercised on a machine that is already
        /// current. --as-endpoint points the check at a different release feed, which is how
        /// the offline, rate-limited, and malformed branches get exercised without waiting
        /// for GitHub to misbehave. Neither flag can change what is reported as the latest
        /// release at the default endpoint.
        /// </summary>
        public static int Run(string[] args, TextWriter output)
        {
            Version current = CurrentVersion;
            string endpoint = ReleasesApiUrl;
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--as-version", StringComparison.OrdinalIgnoreCase) &&
                    TryParseVersion(args[i + 1], out Version simulated))
                {
                    current = simulated;
                }

                if (string.Equals(args[i], "--as-endpoint", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    endpoint = args[i + 1];
                }
            }

            UpdateCheckResult result = CheckAsync(current, default, endpoint).GetAwaiter().GetResult();

            output.WriteLine("getEQd update check");
            output.WriteLine("  this build      : " + Normalize(current).ToString(3) +
                             (current == CurrentVersion ? "" : " (simulated)"));
            output.WriteLine("  endpoint        : " + endpoint);
            output.WriteLine("  state           : " + result.State);
            output.WriteLine("  message         : " + result.Message);
            if (!string.IsNullOrWhiteSpace(result.LatestVersion)) output.WriteLine("  latest release  : " + result.LatestVersion);
            if (!string.IsNullOrWhiteSpace(result.ReleaseUrl)) output.WriteLine("  release page    : " + result.ReleaseUrl);
            if (!string.IsNullOrWhiteSpace(result.DownloadUrl)) output.WriteLine("  download        : " + result.DownloadUrl);

            return result.State switch
            {
                UpdateState.UpToDate => 0,
                UpdateState.UpdateAvailable => 0,
                _ => 1
            };
        }

        private static Version Normalize(Version version) =>
            new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build);

        private static string ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement element)) return string.Empty;
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
        }

        /// <summary>Prefers the installer, then the bare executable.</summary>
        private static string? FindDownloadUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out JsonElement assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? executable = null;
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = ReadString(asset, "name");
                string url = ReadString(asset, "browser_download_url");
                if (url.Length == 0) continue;

                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) return url;
                if (executable == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) executable = url;
            }

            return executable;
        }
    }
}
