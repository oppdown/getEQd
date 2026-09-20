using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using GetEQd.Audio;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// The measured-model lab had never been run against a real third-party measurement.
    /// These tests put every sourced profile through the importer the app actually uses,
    /// so the data and the code that consumes it are checked together.
    /// </summary>
    public class SourcedMeasurementTests
    {
        private static string ProfilesDirectory => Path.Combine(AppContext.BaseDirectory, "profiles");

        private static string[] ProfileFiles() =>
            Directory.Exists(ProfilesDirectory)
                ? Directory.GetFiles(ProfilesDirectory, "*.json")
                : Array.Empty<string>();

        [Fact]
        public void TheSourcedProfilesArePresent()
        {
            Assert.True(
                ProfileFiles().Length >= 5,
                "Expected at least five sourced measurement profiles in windows\\profiles.");
        }

        [Fact]
        public void EverySourcedProfileIsAcceptedByTheRealImporterAndCarriesItsProvenance()
        {
            foreach (string file in ProfileFiles())
            {
                string name = Path.GetFileName(file);
                MeasurementProfile profile = MeasurementProfileIO.Parse(File.ReadAllText(file));

                Assert.True(profile.IsRaw, name + " should be raw measurement data.");
                Assert.True(profile.PointCount >= 100, name + " has too few points to be a real curve.");
                Assert.Equal(profile.PointCount, profile.TargetDb.Length);

                Assert.False(string.IsNullOrWhiteSpace(profile.Model), name + " has no model name.");
                Assert.False(string.IsNullOrWhiteSpace(profile.Source), name + " has no source.");
                Assert.False(string.IsNullOrWhiteSpace(profile.Rig), name + " has no measurement rig.");
                Assert.False(string.IsNullOrWhiteSpace(profile.MeasuredAt), name + " has no measurement date.");
                Assert.False(string.IsNullOrWhiteSpace(profile.Target), name + " has no target curve named.");
            }
        }

        [Fact]
        public void EverySourcedProfileProducesASaneCorrectionCurve()
        {
            foreach (string file in ProfileFiles())
            {
                string name = Path.GetFileName(file);
                MeasurementProfile profile = MeasurementProfileIO.Parse(File.ReadAllText(file));

                // A real headphone curve is not flat and not a straight line of zeros.
                double low = MeasurementProfileIO.RawDbAt(profile, 20);
                double mid = MeasurementProfileIO.RawDbAt(profile, 1000);
                double high = MeasurementProfileIO.RawDbAt(profile, 10000);
                Assert.True(
                    Math.Abs(low - mid) > 0.5 || Math.Abs(high - mid) > 0.5,
                    name + " looks flat, which would mean the curve did not import.");

                double correctionAtOneK = MeasurementProfileIO.CorrectionDbAt(profile, 1000);
                Assert.InRange(correctionAtOneK, -20.0, 20.0);

                double[] landing = MeasurementProfileIO.ToBandGains(profile);
                Assert.Equal(Bands.Count, landing.Length);
                foreach (double gain in landing)
                {
                    Assert.InRange(gain, Bands.MinGainDb, Bands.MaxGainDb);
                    Assert.Equal(gain, Math.Round(gain * 2, MidpointRounding.AwayFromZero) / 2, 9);
                }

                // The audit export has to round-trip the provenance it came from.
                using JsonDocument audit = JsonDocument.Parse(MeasurementProfileIO.AuditJson(profile));
                Assert.Equal(
                    "getEQd-calibration-audit/v1",
                    audit.RootElement.GetProperty("schema").GetString());
                Assert.Equal(
                    profile.Model,
                    audit.RootElement.GetProperty("measurement").GetProperty("Model").GetString());
            }
        }

        [Fact]
        public void TheSourcedProfilesShareOneTargetGrid()
        {
            var grids = ProfileFiles()
                .Select(file => MeasurementProfileIO.Parse(File.ReadAllText(file)))
                .Select(profile => profile.FrequenciesHz.Length + ":" + profile.FrequenciesHz[0] + ":" + profile.FrequenciesHz[^1])
                .Distinct()
                .ToArray();

            Assert.Single(grids);
        }
    }
}
