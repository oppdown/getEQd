using System.Text.Json;
using GetEQd.Audio;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// A saved listening profile has to come back exactly as it went out. These tests walk
    /// the full path a profile takes: settings to stored shape, back into settings, and
    /// through JSON on the way.
    /// </summary>
    public class SettingsRoundTripTests
    {
        private static EqSettings ShapedSettings()
        {
            EqSettings settings = new EqSettings();
            settings.Gains[0] = 5.5;
            settings.Gains[9] = -4.0;
            settings.Qs[9] = 3.5;
            settings.Frequencies[9] = 7500;
            settings.Kinds[9] = BandKind.Notch;
            settings.Enabled[9] = true;

            settings.PreampDb = -3.5;
            settings.CeilingDb = -0.5;
            settings.Route = Route.Headphones;
            settings.Target = HeadphoneTarget.ClosedBackPunch;
            settings.Handoff = SoftwareHandoff.WindowsSpatialSound;
            settings.CrossfeedPercent = 22.0;
            settings.StageWidthPercent = 115.0;
            settings.TargetTrimEnabled = false;
            settings.BassToSub = false;
            settings.SubLaneDb = 3.0;
            settings.CenterLiftDb = 2.5;
            settings.Bypassed = true;
            settings.ActivePresetId = "impact";
            settings.AdvancedMode = true;
            return settings;
        }

        [Fact]
        public void SettingsSurviveTheTripThroughTheStoredShape()
        {
            EqSettings original = ShapedSettings();
            ListeningSettings mapped = ProfileMapping.ToSettings(original);

            EqSettings restored = new EqSettings();
            ProfileMapping.Apply(mapped, restored);

            Assert.Equal(original.Gains, restored.Gains);
            Assert.Equal(original.Qs, restored.Qs);
            Assert.Equal(original.Frequencies, restored.Frequencies);
            Assert.Equal(original.Kinds, restored.Kinds);
            Assert.Equal(original.Enabled, restored.Enabled);
            Assert.Equal(original.PreampDb, restored.PreampDb);
            Assert.Equal(original.CeilingDb, restored.CeilingDb);
            Assert.Equal(original.Route, restored.Route);
            Assert.Equal(original.Target, restored.Target);
            Assert.Equal(original.Handoff, restored.Handoff);
            Assert.Equal(original.CrossfeedPercent, restored.CrossfeedPercent);
            Assert.Equal(original.StageWidthPercent, restored.StageWidthPercent);
            Assert.Equal(original.TargetTrimEnabled, restored.TargetTrimEnabled);
            Assert.Equal(original.BassToSub, restored.BassToSub);
            Assert.Equal(original.SubLaneDb, restored.SubLaneDb);
            Assert.Equal(original.CenterLiftDb, restored.CenterLiftDb);
            Assert.Equal(original.Bypassed, restored.Bypassed);
            Assert.Equal(original.ActivePresetId, restored.ActivePresetId);
            Assert.Equal(original.AdvancedMode, restored.AdvancedMode);
        }

        [Fact]
        public void ASavedProfileSurvivesJson()
        {
            ListeningSettings mapped = ProfileMapping.ToSettings(ShapedSettings());
            ListeningProfile saved = new ListeningProfile
            {
                Name = "Late night",
                SavedAt = "2026-09-20T00:00:00.0000000Z",
                Settings = mapped
            };

            string json = JsonSerializer.Serialize(saved);
            ListeningProfile loaded = Assert.IsType<ListeningProfile>(
                JsonSerializer.Deserialize<ListeningProfile>(json));

            Assert.Equal("Late night", loaded.Name);
            Assert.Equal(saved.SavedAt, loaded.SavedAt);
            Assert.Equal(mapped.Bands, loaded.Settings.Bands);
            Assert.Equal(mapped.Frequencies, loaded.Settings.Frequencies);
            Assert.Equal(mapped.Types, loaded.Settings.Types);
            Assert.Equal(mapped.Enabled, loaded.Settings.Enabled);
            Assert.Equal(mapped.Preamp, loaded.Settings.Preamp);
            Assert.Equal(mapped.Route, loaded.Settings.Route);
            Assert.Equal(mapped.HeadphoneTarget, loaded.Settings.HeadphoneTarget);
            Assert.Equal(mapped.AdvancedMode, loaded.Settings.AdvancedMode);
        }

        [Fact]
        public void ApplyingAProfileClampsRatherThanTrustingTheStoredNumbers()
        {
            ListeningSettings hostile = new ListeningSettings
            {
                Bands = new double[] { 99, -99, 0, 0, 0, 0, 0, 0, 0, 0 },
                Widths = new double[] { 50, 0.001, 1, 1, 1, 1, 1, 1, 1, 1 },
                Frequencies = new double[] { 1, 90000, 1000, 1000, 1000, 1000, 1000, 1000, 1000, 1000 },
                Types = new string[] { "nonsense", "Peaking", "", "", "", "", "", "", "", "" },
                Enabled = new bool[10],
                Preamp = 99,
                Ceiling = -99,
                Crossfeed = 99,
                StageWidth = 1,
                SubLane = 99,
                CenterLift = -99
            };

            EqSettings settings = new EqSettings();
            ProfileMapping.Apply(hostile, settings);

            Assert.Equal(Bands.MaxGainDb, settings.Gains[0]);
            Assert.Equal(Bands.MinGainDb, settings.Gains[1]);
            Assert.Equal(8.0, settings.Qs[0]);
            Assert.Equal(1.0, settings.Qs[1]);
            Assert.Equal(20.0, settings.Frequencies[0]);
            Assert.Equal(20000.0, settings.Frequencies[1]);
            Assert.Equal(BandKind.Peaking, settings.Kinds[1]);
            Assert.Equal(11.0, settings.PreampDb);
            Assert.Equal(-6.0, settings.CeilingDb);
            Assert.Equal(35.0, settings.CrossfeedPercent);
            Assert.Equal(70.0, settings.StageWidthPercent);
            Assert.Equal(6.0, settings.SubLaneDb);
            Assert.Equal(-3.0, settings.CenterLiftDb);
        }
    }
}
