using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GetEQd.Audio;
using GetEQd.Systemwide;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// The system-wide mode writes configuration for Equalizer APO instead of filtering
    /// other applications' audio itself. These tests hold two things down: that the emitted
    /// text is the grammar APO actually documents, and that reading it back gives the same
    /// curve the console draws.
    /// </summary>
    public class ApoConfigTests
    {
        private const int SampleRate = 48000;

        /// <summary>The exact block recorded in windows\docs\equalizer-apo-integration.md.</summary>
        private const string DocumentedImpactBody =
            "Preamp: -4.6 dB\n" +
            "Filter 1: ON LSC 10.2 dB Fc 32 Hz Gain +4.0 dB\n" +
            "Filter 2: ON PK Fc 64 Hz Gain +3.0 dB Q 1.00\n" +
            "Filter 3: ON PK Fc 250 Hz Gain -1.5 dB Q 1.10\n" +
            "Filter 4: ON PK Fc 2500 Hz Gain +1.0 dB Q 0.90\n" +
            "Filter 5: ON PK Fc 6400 Hz Gain +1.5 dB Q 1.00\n" +
            "Filter 6: ON HSC 10.2 dB Fc 12000 Hz Gain +0.5 dB\n";

        private static EqSnapshot ImpactSnapshot()
        {
            EqSettings settings = new EqSettings();
            settings.ApplyPreset("impact");
            return settings.CreateSnapshot();
        }

        [Fact]
        public void TheImpactPresetEmitsTheDocumentedConfiguration()
        {
            string config = ApoConfig.Build(ImpactSnapshot(), SampleRate);

            string[] lines = config.Split('\n');
            Assert.Equal(ApoConfig.HeaderLine, lines[0]);
            Assert.StartsWith("# getEQd ", lines[1]);

            int bodyStart = config.IndexOf("Preamp:", StringComparison.Ordinal);
            Assert.Equal(DocumentedImpactBody, config.Substring(bodyStart));
        }

        [Fact]
        public void TheHeaderIsTheFirstLineSoTheFileSaysItIsManaged()
        {
            Assert.StartsWith(ApoConfig.HeaderLine + "\n", ApoConfig.Build(ImpactSnapshot(), SampleRate));
        }

        [Theory]
        [InlineData(0.20, 7.2)]
        [InlineData(0.50, 9.0)]
        [InlineData(0.70, 10.2)]
        [InlineData(0.90, 11.4)]
        [InlineData(1.00, 12.0)]
        [InlineData(4.00, 12.0)]
        public void ShelfSlopeIsTheSameRbjSlopeGetEQdDesignedWith(double q, double expectedDbPerOctave)
        {
            Assert.Equal(expectedDbPerOctave, ApoConfig.ShelfSlopeDbPerOctave(q), 6);
        }

        [Theory]
        [InlineData(BandKind.Peaking, "PK")]
        [InlineData(BandKind.LowShelf, "LSC")]
        [InlineData(BandKind.HighShelf, "HSC")]
        [InlineData(BandKind.LowPass, "LPQ")]
        [InlineData(BandKind.HighPass, "HPQ")]
        [InlineData(BandKind.Notch, "NO")]
        public void EveryKindUsesTheDocumentedTypeToken(BandKind kind, string expectedToken)
        {
            string line = ApoConfig.FilterLine(kind, 1000, 1.0, 3.0, SampleRate);
            Assert.StartsWith(expectedToken + " ", line);
        }

        [Fact]
        public void ShelvesNeverUseThePlainFormThatWouldShiftTheCornerFrequency()
        {
            string low = ApoConfig.FilterLine(BandKind.LowShelf, 300, 0.7, 5.0, SampleRate);
            string high = ApoConfig.FilterLine(BandKind.HighShelf, 300, 0.7, 5.0, SampleRate);

            Assert.DoesNotContain(" LS ", low);
            Assert.DoesNotContain(" HS ", high);
            Assert.StartsWith("LSC ", low);
            Assert.StartsWith("HSC ", high);
            Assert.Contains(" dB Fc ", low);
            Assert.DoesNotContain("Q ", low);
        }

        [Fact]
        public void APassOrNotchBandCarriesNoGainToken()
        {
            Assert.DoesNotContain("Gain", ApoConfig.FilterLine(BandKind.LowPass, 1000, 1.0, 5.0, SampleRate));
            Assert.DoesNotContain("Gain", ApoConfig.FilterLine(BandKind.HighPass, 1000, 1.0, 5.0, SampleRate));
            Assert.DoesNotContain("Gain", ApoConfig.FilterLine(BandKind.Notch, 1000, 1.0, 5.0, SampleRate));
        }

        [Fact]
        public void DisabledBandsAreOmittedAndTheNumberingStaysSequential()
        {
            EqSettings settings = new EqSettings();
            settings.Enabled[2] = false;
            settings.Enabled[4] = false;
            settings.Gains[0] = 4.0;
            settings.Gains[5] = 2.0;

            string config = ApoConfig.Build(settings.CreateSnapshot(), SampleRate);
            string[] filterLines = config
                .Split('\n')
                .Where((line) => line.StartsWith("Filter ", StringComparison.Ordinal))
                .ToArray();

            Assert.Equal(4, filterLines.Length);
            Assert.StartsWith("Filter 1: ", filterLines[0]);
            Assert.StartsWith("Filter 2: ", filterLines[1]);
            Assert.StartsWith("Filter 3: ", filterLines[2]);
            Assert.StartsWith("Filter 4: ", filterLines[3]);
            Assert.DoesNotContain("250 Hz", config);
            Assert.DoesNotContain("6400 Hz", config);
        }

        [Fact]
        public void AFrequencyIsEmittedClampedWhenTheDeviceCannotReachIt()
        {
            EqSettings settings = new EqSettings();
            settings.Frequencies[9] = 16000;
            settings.Enabled[9] = true;
            settings.Gains[9] = 3.0;

            // 0.45 x 32000 = 14400 Hz, which is where getEQd itself would put the band.
            string config = ApoConfig.Build(settings.CreateSnapshot(), 32000);
            Assert.Contains("Fc 14400 Hz", config);
            Assert.DoesNotContain("Fc 16000 Hz", config);
        }

        [Fact]
        public void ThePreampIsTheHeadroomSuggestionTheConsoleAlreadyShows()
        {
            EqSnapshot snapshot = ImpactSnapshot();
            HeadroomAnalysis headroom = Analysis.Evaluate(snapshot, SampleRate);
            string preampLine = ApoConfig.Build(snapshot, SampleRate)
                .Split('\n')
                .First((line) => line.StartsWith("Preamp:", StringComparison.Ordinal));

            Assert.Equal("Preamp: " + headroom.SuggestedPreampDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB", preampLine);
        }

        [Fact]
        public void ReadingTheEmittedConfigBackGivesTheCurveTheConsoleDraws()
        {
            // This is the invariant the whole product rests on, extended to the new output:
            // the file we hand to Equalizer APO must reproduce the drawn response.
            EqSettings settings = new EqSettings();
            settings.ApplyPreset("dialogue");
            settings.Enabled[6] = true;
            settings.Frequencies[6] = 90;
            settings.Kinds[6] = BandKind.LowShelf;
            settings.Qs[6] = 0.7;
            settings.Gains[6] = -3.0;
            settings.Enabled[7] = true;
            settings.Frequencies[7] = 9000;
            settings.Kinds[7] = BandKind.Notch;
            settings.Qs[7] = 4.0;

            EqSnapshot snapshot = settings.CreateSnapshot();
            string config = ApoConfig.Build(snapshot, SampleRate);

            // The file carries the headroom-safe preamp rather than the console's preamp
            // fader, so the comparison is the drawn curve with that one value swapped.
            HeadroomAnalysis headroom = Analysis.Evaluate(snapshot, SampleRate);
            double preampSwap = headroom.SuggestedPreampDb - snapshot.PreampDb;

            List<string> mismatches = new List<string>();
            foreach (double frequency in new[] { 30.0, 90.0, 250.0, 1000.0, 2500.0, 9000.0, 12000.0, 16000.0 })
            {
                double drawn = snapshot.CurveDbAt(SampleRate, frequency) + preampSwap;
                double fromConfig = CurveFromConfig(config, SampleRate, frequency);

                // The shelf slope and the preamp are written to one decimal place, so allow
                // for that rounding.
                if (Math.Abs(drawn - fromConfig) > 0.06)
                {
                    mismatches.Add(frequency + " Hz: drawn " + drawn.ToString("0.00", CultureInfo.InvariantCulture) +
                                   " vs config " + fromConfig.ToString("0.00", CultureInfo.InvariantCulture));
                }
            }

            Assert.True(mismatches.Count == 0, "The emitted config does not reproduce the drawn curve: " +
                                               string.Join("; ", mismatches) + "\n\n" + config);
        }

        [Fact]
        public void AConfigWithNoEnabledBandsIsJustThePreamp()
        {
            EqSettings settings = new EqSettings();
            for (int i = 0; i < Bands.Count; i++) settings.Enabled[i] = false;

            string config = ApoConfig.Build(settings.CreateSnapshot(), SampleRate);
            Assert.Contains("Preamp: -1.0 dB", config);
            Assert.DoesNotContain("Filter ", config);
        }

        [Theory]
        [InlineData("Include: geteqd.txt", true)]
        [InlineData("include: GETEQD.TXT", true)]
        [InlineData("  Include:   geteqd.txt  ", true)]
        [InlineData("Include: example.txt", false)]
        [InlineData("Preamp: -6 dB", false)]
        [InlineData("", false)]
        public void TheIncludeLineIsRecognisedHoweverItIsWritten(string configText, bool expected)
        {
            Assert.Equal(expected, ApoConfig.HasInclude(configText));
        }

        [Fact]
        public void WritingTwiceDoesNotStackASecondIncludeLine()
        {
            string directory = Path.Combine(Path.GetTempPath(), "geteqd-apo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "config.txt"), "Preamp: -6 dB\nInclude: example.txt\n");

                string managed = ApoConfig.Write(directory, ImpactSnapshot(), SampleRate);
                ApoConfig.Write(directory, ImpactSnapshot(), SampleRate);

                Assert.True(File.Exists(managed));
                Assert.Equal(ApoConfig.Build(ImpactSnapshot(), SampleRate), File.ReadAllText(managed));

                string entry = File.ReadAllText(Path.Combine(directory, "config.txt"));
                Assert.Equal(1, entry.Split('\n').Count((line) => ApoConfig.HasInclude(line)));

                // The user's own lines survive.
                Assert.Contains("Preamp: -6 dB", entry);
                Assert.Contains("Include: example.txt", entry);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void RemovingTakesTheManagedFileAndItsIncludeLineBackOut()
        {
            string directory = Path.Combine(Path.GetTempPath(), "geteqd-apo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                File.WriteAllText(Path.Combine(directory, "config.txt"), "Preamp: -6 dB\n");
                ApoConfig.Write(directory, ImpactSnapshot(), SampleRate);

                ApoConfig.Remove(directory);

                Assert.False(File.Exists(Path.Combine(directory, ApoConfig.ManagedFileName)));
                string entry = File.ReadAllText(Path.Combine(directory, "config.txt"));
                Assert.False(ApoConfig.HasInclude(entry));
                Assert.Contains("Preamp: -6 dB", entry);
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void DetectionNeverThrowsAndNeverClaimsAnInstallThatIsNotThere()
        {
            ApoInstallation installation = ApoConfig.Detect();
            Assert.False(string.IsNullOrWhiteSpace(installation.Message));
            if (!installation.IsInstalled)
            {
                Assert.Null(installation.ConfigDirectory);
                Assert.Null(installation.ManagedFilePath);
            }
        }

        // ================================================================ a tiny reader

        /// <summary>
        /// Reads the emitted text the way Equalizer APO would and rebuilds the response. It
        /// deliberately goes through the text rather than the settings, so a formatting or
        /// mapping mistake cannot pass by comparing the source of truth with itself.
        /// </summary>
        private static double CurveFromConfig(string config, int sampleRate, double frequency)
        {
            double total = 0;
            List<Biquad> filters = new List<Biquad>();

            foreach (string raw in config.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                if (line.StartsWith("Preamp:", StringComparison.OrdinalIgnoreCase))
                {
                    total += ParseNumber(line.Substring("Preamp:".Length).Replace("dB", ""));
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon < 0) continue;

                string body = line.Substring(colon + 1).Trim();
                if (!body.StartsWith("ON ", StringComparison.Ordinal)) continue;

                string[] parts = body.Substring(3).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string type = parts[0];
                int index = 1;
                double slope = 0;
                if (type == "LSC" || type == "HSC")
                {
                    slope = ParseNumber(parts[index]);
                    index += 2; // skip the number and its "dB"
                }

                double fc = 0, gain = 0, q = 1;
                for (; index < parts.Length; index++)
                {
                    switch (parts[index])
                    {
                        case "Fc": fc = ParseNumber(parts[index + 1]); break;
                        case "Gain": gain = ParseNumber(parts[index + 1]); break;
                        case "Q": q = ParseNumber(parts[index + 1]); break;
                    }
                }

                filters.Add(type switch
                {
                    "PK" => Biquad.Peaking(sampleRate, fc, q, gain),
                    "LSC" => Biquad.LowShelf(sampleRate, fc, slope / 12.0, gain),
                    "HSC" => Biquad.HighShelf(sampleRate, fc, slope / 12.0, gain),
                    "LPQ" => Biquad.LowPass(sampleRate, fc, q),
                    "HPQ" => Biquad.HighPass(sampleRate, fc, q),
                    "NO" => Biquad.Notch(sampleRate, fc, q),
                    _ => throw new InvalidDataException("Unknown filter type in the emitted config: " + type)
                });
            }

            foreach (Biquad filter in filters)
            {
                total += filter.GainDbAt(sampleRate, frequency);
            }

            return total;
        }

        private static double ParseNumber(string text)
        {
            string cleaned = text.Trim();
            int space = cleaned.IndexOf(' ');
            if (space > 0) cleaned = cleaned.Substring(0, space);
            return double.Parse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
