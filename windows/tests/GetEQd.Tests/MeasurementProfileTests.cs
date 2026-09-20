using System;
using System.IO;
using System.Text.Json;
using GetEQd.Audio;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// The importer is the boundary between other people's measurement files and the
    /// console. It has to accept good data, refuse bad data with a reason a listener can
    /// act on, and never invent a curve of its own.
    /// </summary>
    public class MeasurementProfileTests
    {
        private const string ValidRawProfile = @"{
  ""schema"": ""getEQd-profile/v1"",
  ""model"": ""Test Headphone"",
  ""source"": ""Example measurement set"",
  ""measuredAt"": ""2026-09-01"",
  ""rig"": ""Example rig"",
  ""target"": ""Example target"",
  ""responseType"": ""raw"",
  ""notes"": ""Synthetic data used only by the test suite."",
  ""frequenciesHz"": [20, 100, 1000, 10000],
  ""gainDb"": [2, 1, -3, 4],
  ""targetDb"": [0, 0, 0, 0]
}";

        [Fact]
        public void AValidRawProfileRoundTripsThroughTheImporter()
        {
            MeasurementProfile profile = MeasurementProfileIO.Parse(ValidRawProfile);

            Assert.Equal("getEQd-profile/v1", profile.Schema);
            Assert.Equal("Test Headphone", profile.Model);
            Assert.Equal("Example measurement set", profile.Source);
            Assert.Equal("Example rig", profile.Rig);
            Assert.True(profile.IsRaw);
            Assert.Equal("Raw measurement", profile.ResponseTypeLabel);
            Assert.Equal(4, profile.PointCount);
        }

        [Fact]
        public void ARawProfileCorrectionIsTargetMinusMeasurement()
        {
            MeasurementProfile profile = MeasurementProfileIO.Parse(ValidRawProfile);

            // At 1000 Hz the measurement is -3 dB against a flat target, so the
            // correction is +3 dB.
            Assert.Equal(-3.0, MeasurementProfileIO.RawDbAt(profile, 1000), 6);
            Assert.Equal(3.0, MeasurementProfileIO.CorrectionDbAt(profile, 1000), 6);

            double[] curve = MeasurementProfileIO.ToCorrectionCurve(profile);
            Assert.Equal(new[] { -2.0, -1.0, 3.0, -4.0 }, curve);
        }

        [Fact]
        public void TheImporterSnapsCurveLandingsToTheFaderStepAndKeepsThemInRange()
        {
            double[] landing = MeasurementProfileIO.ToBandGains(MeasurementProfileIO.Parse(ValidRawProfile));

            Assert.Equal(Bands.Count, landing.Length);
            foreach (double gain in landing)
            {
                Assert.InRange(gain, Bands.MinGainDb, Bands.MaxGainDb);
                Assert.Equal(gain, Math.Round(gain * 2, MidpointRounding.AwayFromZero) / 2, 9);
            }
        }

        [Fact]
        public void ThePublishedTemplateParses()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "getEQd-profile-template.json");
            Assert.True(File.Exists(path), "The published template was not copied next to the tests." + path);

            MeasurementProfile profile = MeasurementProfileIO.Parse(File.ReadAllText(path));
            Assert.Equal("getEQd-profile/v1", profile.Schema);
            Assert.True(profile.PointCount >= 2);
        }

        [Theory]
        [InlineData("{}", "Schema must be")]
        [InlineData(@"{""schema"":""getEQd-profile/v1""}", "Add a model name")]
        [InlineData(@"{""schema"":""getEQd-profile/v1"",""model"":""x"",""frequenciesHz"":[20,100],""gainDb"":[1]}", "matching arrays")]
        [InlineData(@"{""schema"":""getEQd-profile/v1"",""model"":""x"",""frequenciesHz"":[0,100],""gainDb"":[1,2]}", "above zero")]
        [InlineData(@"{""schema"":""getEQd-profile/v1"",""model"":""x"",""frequenciesHz"":[20,100],""gainDb"":[1,2],""responseType"":""guess""}", "raw or correction")]
        [InlineData(@"{""schema"":""getEQd-profile/v1"",""model"":""x"",""frequenciesHz"":[20,100],""gainDb"":[1,2],""targetDb"":[0]}", "matching frequenciesHz")]
        [InlineData(@"[1,2,3]", "must be a JSON object")]
        [InlineData(@"this is not json", "not valid JSON")]
        public void BadImportsAreRefusedWithAReadableReason(string json, string expectedFragment)
        {
            InvalidDataException error = Assert.Throws<InvalidDataException>(
                () => MeasurementProfileIO.Parse(json));
            Assert.Contains(expectedFragment, error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AProfileWithoutAResponseTypeIsTreatedAsLegacyCorrectionData()
        {
            const string legacy = @"{
  ""schema"": ""getEQd-profile/v1"",
  ""model"": ""Legacy Profile"",
  ""frequenciesHz"": [20, 1000],
  ""gainDb"": [-4, 2]
}";

            MeasurementProfile profile = MeasurementProfileIO.Parse(legacy);
            Assert.False(profile.IsRaw);
            Assert.Equal(-4.0, MeasurementProfileIO.CorrectionDbAt(profile, 20), 6);
        }

        [Fact]
        public void TheAuditExportCarriesItsOwnProvenanceAndTheNumbersItCameFrom()
        {
            MeasurementProfile profile = MeasurementProfileIO.Parse(ValidRawProfile);
            string audit = MeasurementProfileIO.AuditJson(profile);

            using JsonDocument document = JsonDocument.Parse(audit);
            JsonElement root = document.RootElement;
            Assert.Equal("getEQd-calibration-audit/v1", root.GetProperty("schema").GetString());

            JsonElement measurement = root.GetProperty("measurement");
            Assert.Equal("Test Headphone", measurement.GetProperty("Model").GetString());
            Assert.Equal("Example measurement set", measurement.GetProperty("Source").GetString());
            Assert.Equal("Example rig", measurement.GetProperty("Rig").GetString());
            Assert.Equal("raw", measurement.GetProperty("responseType").GetString());
            Assert.Equal(4, measurement.GetProperty("frequenciesHz").GetArrayLength());
            Assert.Equal(4, measurement.GetProperty("rawDb").GetArrayLength());
            Assert.Equal(4, measurement.GetProperty("targetDb").GetArrayLength());
            Assert.Equal(4, measurement.GetProperty("correctionDb").GetArrayLength());

            Assert.Equal(Bands.AnalysisFrequencies.Length, root.GetProperty("quickBandCorrectionDb").GetArrayLength());

            // Re-importing the export must not be possible by accident: the audit is a
            // record, not a profile.
            Assert.Throws<InvalidDataException>(() => MeasurementProfileIO.Parse(audit));
        }
    }
}
