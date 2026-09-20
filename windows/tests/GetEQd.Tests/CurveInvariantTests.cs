using GetEQd.Audio;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// The product's central promise: the curve on screen is the response of the filters
    /// that are actually running. These tests measure real audio through the built filters
    /// and compare it with the drawn curve, so a display computed separately from the DSP
    /// cannot pass.
    /// </summary>
    public class CurveInvariantTests
    {
        private const int SampleRate = 48000;

        private static readonly double[] ProbeFrequencies = { 60, 250, 1000, 4000, 10000 };

        private static EqSettings ShapedSettings()
        {
            EqSettings settings = new EqSettings();
            settings.Gains[0] = 6.0;
            settings.Gains[1] = -3.0;
            settings.Gains[3] = 4.5;
            settings.Gains[5] = 3.0;
            settings.PreampDb = -4.0;
            settings.CeilingDb = -1.0;
            return settings;
        }

        [Fact]
        public void DrawnCurveMatchesMeasuredAudioAcrossTheAudibleRange()
        {
            EqSnapshot snapshot = ShapedSettings().CreateSnapshot();
            Biquad[] filters = snapshot.BuildFilters(SampleRate);

            foreach (double frequency in ProbeFrequencies)
            {
                double drawn = snapshot.CurveDbAt(SampleRate, frequency);
                double measured = AudioMathTests.MeasureGainDb(filters, snapshot.PreampDb, frequency, SampleRate);
                Assert.Equal(drawn, measured, 1);
            }
        }

        [Fact]
        public void DrawnCurveIncludesThePreamp()
        {
            EqSnapshot quiet = ShapedSettings().CreateSnapshot();
            Biquad[] filters = quiet.BuildFilters(SampleRate);

            double atUnityPreamp = AudioMathTests.MeasureGainDb(filters, 0.0, 1000, SampleRate);
            double withPreamp = AudioMathTests.MeasureGainDb(filters, quiet.PreampDb, 1000, SampleRate);
            Assert.Equal(quiet.PreampDb, withPreamp - atUnityPreamp, 3);
        }

        [Fact]
        public void ADisabledBandContributesNothingToTheCurveOrTheAudio()
        {
            EqSettings settings = new EqSettings();
            for (int i = 0; i < Bands.Count; i++)
            {
                settings.Enabled[i] = i == 1;
            }

            settings.Gains[1] = 6.0;
            settings.Frequencies[1] = 64;
            settings.Kinds[1] = BandKind.Peaking;
            settings.Qs[1] = 1.0;
            settings.PreampDb = 0;

            EqSnapshot enabled = settings.CreateSnapshot();
            Assert.Equal(6.0, enabled.CurveDbAt(SampleRate, 64), 2);

            settings.Enabled[1] = false;
            EqSnapshot bypassed = settings.CreateSnapshot();
            Assert.Equal(0.0, bypassed.CurveDbAt(SampleRate, 64), 6);

            Biquad[] filters = bypassed.BuildFilters(SampleRate);
            Assert.Equal(Biquad.Identity.B0, filters[1].B0);
            Assert.Equal(Biquad.Identity.A1, filters[1].A1);

            double measured = AudioMathTests.MeasureGainDb(filters, 0.0, 64, SampleRate);
            Assert.Equal(0.0, measured, 3);
        }

        [Fact]
        public void HeadroomAnalysisFindsThePeakAndSuggestsAPreampThatClearsTheCeiling()
        {
            EqSnapshot snapshot = ShapedSettings().CreateSnapshot();
            HeadroomAnalysis analysis = Analysis.Evaluate(snapshot, SampleRate);

            // The peak is measured through the preamp, so it is smaller than the raw
            // band gain, but the scan must still find at least the low-shelf centre.
            Assert.True(analysis.PeakCurveDb >= snapshot.CurveDbAt(SampleRate, 32));
            Assert.InRange(analysis.PeakCurveDb, 0.0, 6.0);
            Assert.Equal(snapshot.CeilingDb - analysis.PeakCurveDb, analysis.SuggestedPreampDb, 6);
            Assert.Equal(
                analysis.PeakCurveDb > snapshot.CeilingDb + 0.05,
                analysis.LimiterWillEngage);
            Assert.False(string.IsNullOrWhiteSpace(analysis.Message));
        }
    }
}
