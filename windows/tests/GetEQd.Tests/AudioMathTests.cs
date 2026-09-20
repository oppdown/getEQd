using System;
using GetEQd.Audio;
using Xunit;

namespace GetEQd.Tests
{
    /// <summary>
    /// The filter shapes are Cookbook formulas, so they have known answers at known
    /// frequencies. These tests pin those answers down: the ear should not be the first
    /// thing to notice that a shape stopped behaving.
    /// </summary>
    public class AudioMathTests
    {
        private const int SampleRate = 48000;

        [Theory]
        [InlineData(-9.0)]
        [InlineData(-3.0)]
        [InlineData(3.0)]
        [InlineData(6.0)]
        [InlineData(11.0)]
        public void PeakingFilter_ReachesItsGainAtTheCentreFrequency(double gainDb)
        {
            Biquad filter = Biquad.Peaking(SampleRate, 1000, 1.0, gainDb);
            Assert.Equal(gainDb, filter.GainDbAt(SampleRate, 1000), 3);
        }

        [Fact]
        public void PeakingFilter_IsFlatWhenTheGainIsZero()
        {
            Biquad filter = Biquad.Peaking(SampleRate, 1000, 1.0, 0.0);
            foreach (double frequency in new[] { 20.0, 200.0, 1000.0, 5000.0, 20000.0 })
            {
                Assert.Equal(0.0, filter.GainDbAt(SampleRate, frequency), 6);
            }
        }

        [Fact]
        public void PeakingFilter_IsSymmetricalAboutTheCentre()
        {
            Biquad filter = Biquad.Peaking(SampleRate, 1000, 2.0, 6.0);
            double below = filter.GainDbAt(SampleRate, 500);
            double above = filter.GainDbAt(SampleRate, 2000);
            // Bilinear warping leaves a hundredth of a dB of asymmetry; the intent of the
            // test is that the shape is centred, not that it is perfectly symmetrical.
            Assert.Equal(below, above, 2);
        }

        [Fact]
        public void LowPassAndHighPass_AreThreeDbDownAtTheCornerWithAButterworthQ()
        {
            double butterworth = 1.0 / Math.Sqrt(2.0);
            Biquad lowPass = Biquad.LowPass(SampleRate, 1000, butterworth);
            Biquad highPass = Biquad.HighPass(SampleRate, 1000, butterworth);

            Assert.Equal(-3.0103, lowPass.GainDbAt(SampleRate, 1000), 2);
            Assert.Equal(-3.0103, highPass.GainDbAt(SampleRate, 1000), 2);

            // And they get out of the way on the side they are meant to pass.
            Assert.True(lowPass.GainDbAt(SampleRate, 100) > -0.1);
            Assert.True(highPass.GainDbAt(SampleRate, 10000) > -0.1);
        }

        [Fact]
        public void NotchFilter_IsDeepAtItsCentreAndAlmostUntouchedElsewhere()
        {
            Biquad notch = Biquad.Notch(SampleRate, 1000, 4.0);
            Assert.True(notch.GainDbAt(SampleRate, 1000) < -40.0);
            Assert.Equal(0.0, notch.GainDbAt(SampleRate, 100), 2);
            Assert.Equal(0.0, notch.GainDbAt(SampleRate, 10000), 2);
        }

        [Fact]
        public void LowShelf_LiftsTheBottomAndLeavesTheTopAlone()
        {
            Biquad shelf = Biquad.LowShelf(SampleRate, 200, 0.7, 6.0);
            Assert.True(shelf.GainDbAt(SampleRate, 20) > 5.9);
            Assert.Equal(0.0, shelf.GainDbAt(SampleRate, 10000), 2);
        }

        [Fact]
        public void HighShelf_LiftsTheTopAndLeavesTheBottomAlone()
        {
            Biquad shelf = Biquad.HighShelf(SampleRate, 8000, 0.7, 6.0);
            Assert.True(shelf.GainDbAt(SampleRate, 20000) > 5.9);
            Assert.Equal(0.0, shelf.GainDbAt(SampleRate, 50), 2);
        }

        [Fact]
        public void ForBand_ClampsOutOfRangeRequestsInsteadOfProducingNonsense()
        {
            Biquad requested = Biquad.ForBand(BandKind.Peaking, SampleRate, 1, 0.01, 4.0);
            Biquad clamped = Biquad.Peaking(SampleRate, 20, 0.2, 4.0);

            foreach (double frequency in new[] { 20.0, 100.0, 1000.0, 10000.0 })
            {
                Assert.Equal(
                    clamped.GainDbAt(SampleRate, frequency),
                    requested.GainDbAt(SampleRate, frequency),
                    9);
            }
        }

        [Fact]
        public void ProcessingASineThroughTheFilterMatchesTheAnalyticResponse()
        {
            // The analytic magnitude and the difference equation are two different code
            // paths. A sine through the section is the check that they agree.
            foreach (BandKind kind in new[] { BandKind.Peaking, BandKind.LowShelf, BandKind.HighShelf, BandKind.LowPass, BandKind.HighPass })
            {
                Biquad filter = Biquad.ForBand(kind, SampleRate, 1000, 1.0, 6.0);
                double measured = MeasureGainDb(new[] { filter }, 0.0, 1000, SampleRate);
                Assert.Equal(filter.GainDbAt(SampleRate, 1000), measured, 1);
            }
        }

        internal static double MeasureGainDb(Biquad[] filters, double preampDb, double frequency, int sampleRate)
        {
            int total = sampleRate;
            int settle = sampleRate / 2;
            double preampGain = Math.Pow(10, preampDb / 20.0);

            BiquadState[] states = new BiquadState[filters.Length];
            double sum = 0;
            for (int n = 0; n < total; n++)
            {
                double sample = Math.Sin(2 * Math.PI * frequency * n / sampleRate);
                for (int i = 0; i < filters.Length; i++)
                {
                    sample = states[i].Process(sample, in filters[i]);
                }

                // The preamp sits after the filters in the chain, so it is applied here.
                if (n >= settle)
                {
                    double output = sample * preampGain;
                    sum += output * output;
                }
            }

            double rms = Math.Sqrt(sum / (total - settle));
            double reference = 1.0 / Math.Sqrt(2.0);
            return 20 * Math.Log10(rms / reference);
        }
    }
}
