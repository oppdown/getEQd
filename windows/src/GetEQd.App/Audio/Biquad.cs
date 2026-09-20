using System;

namespace GetEQd.Audio
{
    /// <summary>Filter shapes available to the quick and advanced EQ bands.</summary>
    public enum BandKind
    {
        LowShelf,
        Peaking,
        HighShelf,
        LowPass,
        HighPass,
        Notch
    }

    /// <summary>
    /// A single second-order IIR section, designed with the Audio EQ Cookbook
    /// (Robert Bristow-Johnson) formulas. Coefficients are normalised by a0 so the
    /// difference equation is y = b0*x + b1*x1 + b2*x2 - a1*y1 - a2*y2.
    /// </summary>
    public readonly struct Biquad
    {
        public readonly double B0;
        public readonly double B1;
        public readonly double B2;
        public readonly double A1;
        public readonly double A2;

        public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            B0 = b0 / a0;
            B1 = b1 / a0;
            B2 = b2 / a0;
            A1 = a1 / a0;
            A2 = a2 / a0;
        }

        /// <summary>A pass-through section. Used for zero-gain bands so the chain shape stays fixed.</summary>
        public static Biquad Identity => new Biquad(1, 0, 0, 1, 0, 0);

        private const double MinGainDb = 1e-4;

        public static Biquad Peaking(double sampleRate, double frequency, double q, double gainDb)
        {
            if (Math.Abs(gainDb) < MinGainDb) return Identity;
            double a = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * Math.Max(1e-6, q));

            return new Biquad(
                1 + alpha * a,
                -2 * cosW0,
                1 - alpha * a,
                1 + alpha / a,
                -2 * cosW0,
                1 - alpha / a);
        }

        public static Biquad LowShelf(double sampleRate, double frequency, double slope, double gainDb)
        {
            if (Math.Abs(gainDb) < MinGainDb) return Identity;
            double a = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double s = Math.Max(1e-6, slope);
            double alpha = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / s - 1) + 2);
            double beta = 2 * Math.Sqrt(a) * alpha;

            return new Biquad(
                a * ((a + 1) - (a - 1) * cosW0 + beta),
                2 * a * ((a - 1) - (a + 1) * cosW0),
                a * ((a + 1) - (a - 1) * cosW0 - beta),
                (a + 1) + (a - 1) * cosW0 + beta,
                -2 * ((a - 1) + (a + 1) * cosW0),
                (a + 1) + (a - 1) * cosW0 - beta);
        }

        public static Biquad HighShelf(double sampleRate, double frequency, double slope, double gainDb)
        {
            if (Math.Abs(gainDb) < MinGainDb) return Identity;
            double a = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double s = Math.Max(1e-6, slope);
            double alpha = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / s - 1) + 2);
            double beta = 2 * Math.Sqrt(a) * alpha;

            return new Biquad(
                a * ((a + 1) + (a - 1) * cosW0 + beta),
                -2 * a * ((a - 1) + (a + 1) * cosW0),
                a * ((a + 1) + (a - 1) * cosW0 - beta),
                (a + 1) - (a - 1) * cosW0 + beta,
                2 * ((a - 1) - (a + 1) * cosW0),
                (a + 1) - (a - 1) * cosW0 - beta);
        }

        public static Biquad LowPass(double sampleRate, double frequency, double q)
        {
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * Math.Max(1e-6, q));
            return new Biquad(
                (1 - cosW0) / 2,
                1 - cosW0,
                (1 - cosW0) / 2,
                1 + alpha,
                -2 * cosW0,
                1 - alpha);
        }

        public static Biquad HighPass(double sampleRate, double frequency, double q)
        {
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * Math.Max(1e-6, q));
            return new Biquad(
                (1 + cosW0) / 2,
                -(1 + cosW0),
                (1 + cosW0) / 2,
                1 + alpha,
                -2 * cosW0,
                1 - alpha);
        }

        public static Biquad Notch(double sampleRate, double frequency, double q)
        {
            double w0 = 2 * Math.PI * frequency / sampleRate;
            double cosW0 = Math.Cos(w0);
            double alpha = Math.Sin(w0) / (2 * Math.Max(1e-6, q));
            return new Biquad(
                1,
                -2 * cosW0,
                1,
                1 + alpha,
                -2 * cosW0,
                1 - alpha);
        }

        /// <summary>Builds the section that realises one control band.</summary>
        public static Biquad ForBand(BandKind kind, double sampleRate, double frequency, double q, double gainDb)
        {
            frequency = Math.Max(20.0, Math.Min(sampleRate * 0.45, frequency));
            q = Math.Max(0.2, Math.Min(8.0, q));

            // A shelf needs a slope rather than a Q; map the band Q onto a sensible slope
            // so one "width" control keeps meaning across every filter shape.
            switch (kind)
            {
                case BandKind.LowShelf:
                    return LowShelf(sampleRate, frequency, ShelfSlopeFromQ(q), gainDb);
                case BandKind.HighShelf:
                    return HighShelf(sampleRate, frequency, ShelfSlopeFromQ(q), gainDb);
                case BandKind.LowPass:
                    return LowPass(sampleRate, frequency, q);
                case BandKind.HighPass:
                    return HighPass(sampleRate, frequency, q);
                case BandKind.Notch:
                    return Notch(sampleRate, frequency, q);
                default:
                    return Peaking(sampleRate, frequency, q, gainDb);
            }
        }

        private static double ShelfSlopeFromQ(double q) => Math.Max(0.1, Math.Min(1.0, 0.5 + q * 0.5));

        /// <summary>
        /// Analytic magnitude response |H(e^jw)| in linear terms at one frequency.
        /// This is what makes the on-screen curve the true response of the running DSP.
        /// </summary>
        public double MagnitudeAt(double sampleRate, double frequency)
        {
            double w = 2 * Math.PI * frequency / sampleRate;
            double cosW = Math.Cos(w);
            double sinW = Math.Sin(w);
            double cos2W = Math.Cos(2 * w);
            double sin2W = Math.Sin(2 * w);

            double numReal = B0 + B1 * cosW + B2 * cos2W;
            double numImag = -(B1 * sinW + B2 * sin2W);
            double denReal = 1 + A1 * cosW + A2 * cos2W;
            double denImag = -(A1 * sinW + A2 * sin2W);

            double num = Math.Sqrt(numReal * numReal + numImag * numImag);
            double den = Math.Sqrt(denReal * denReal + denImag * denImag);
            return den <= 1e-20 ? 1.0 : num / den;
        }

        public double GainDbAt(double sampleRate, double frequency)
        {
            double magnitude = MagnitudeAt(sampleRate, frequency);
            return 20 * Math.Log10(Math.Max(1e-9, magnitude));
        }
    }

    /// <summary>
    /// Direct-form-I state for one channel of one section. Held as a struct in a
    /// pre-allocated array so the audio thread never allocates.
    /// </summary>
    public struct BiquadState
    {
        private double _x1, _x2, _y1, _y2;

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0;
        }

        public double Process(double input, in Biquad c)
        {
            double output = c.B0 * input + c.B1 * _x1 + c.B2 * _x2 - c.A1 * _y1 - c.A2 * _y2;

            _x2 = _x1;
            _x1 = input;
            _y2 = _y1;

            // Flush denormals: a decaying tail in a silent room otherwise costs
            // real CPU on x86 without hardware flush-to-zero.
            if (output > -1e-30 && output < 1e-30) output = 0;

            _y1 = output;
            return output;
        }
    }

    /// <summary>One-pole low pass. Used for crossfeed and the sub lane.</summary>
    public struct OnePole
    {
        private double _z;
        private double _coefficient;

        public void Configure(double sampleRate, double frequency)
        {
            _coefficient = 1 - Math.Exp(-2 * Math.PI * frequency / sampleRate);
            _z = 0;
        }

        public double Process(double input)
        {
            _z += _coefficient * (input - _z);
            if (_z > -1e-30 && _z < 1e-30) _z = 0;
            return _z;
        }

        public void Reset() => _z = 0;
    }
}
