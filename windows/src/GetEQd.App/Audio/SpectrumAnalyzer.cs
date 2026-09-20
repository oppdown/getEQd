using System;
using NAudio.Dsp;

namespace GetEQd.Audio
{
    /// <summary>
    /// Turns the post-EQ signal into a log-magnitude spectrum for the curve backdrop.
    /// Uses a Hann window and NAudio's FFT; sized once, reused every UI tick.
    /// </summary>
    public sealed class SpectrumAnalyzer
    {
        private readonly int _fftSize;
        private readonly int _order;
        private readonly Complex[] _buffer;
        private readonly float[] _window;

        private readonly float[] _windowBuffer;

        public SpectrumAnalyzer(int fftSize = 2048)
        {
            _fftSize = fftSize;
            _order = (int)Math.Round(Math.Log(fftSize, 2));
            _buffer = new Complex[_fftSize];
            _windowBuffer = new float[_fftSize];
            _window = new float[_fftSize];

            for (int i = 0; i < _fftSize; i++)
            {
                _window[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (_fftSize - 1)));
            }

            MagnitudesDb = new float[_fftSize / 2];
        }

        /// <summary>Bins from DC upward, in dB relative to full scale, clamped to -100.</summary>
        public float[] MagnitudesDb { get; }

        public int BinCount => MagnitudesDb.Length;

        /// <summary>Frequency at the centre of a bin for a given sample rate.</summary>
        public double BinFrequency(int index, int sampleRate) => index * (double)sampleRate / _fftSize;

        public void Compute(float[] samples, int sampleRate)
        {
            int length = Math.Min(_fftSize, samples.Length);

            // Newest samples last, so pad the front when handed a short window.
            int padding = _fftSize - length;
            for (int i = 0; i < padding; i++) _windowBuffer[i] = 0;

            for (int i = 0; i < length; i++)
            {
                _windowBuffer[padding + i] = samples[i] * _window[padding + i];
            }

            for (int i = 0; i < _fftSize; i++)
            {
                _buffer[i].X = _windowBuffer[i];
                _buffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, _order, _buffer);

            double normalisation = 2.0 / _fftSize;

            for (int i = 0; i < MagnitudesDb.Length; i++)
            {
                double real = _buffer[i].X;
                double imaginary = _buffer[i].Y;
                double magnitude = Math.Sqrt(real * real + imaginary * imaginary) * normalisation;
                double db = 20 * Math.Log10(Math.Max(1e-6, magnitude));
                MagnitudesDb[i] = (float)Math.Max(-100.0, Math.Min(0.0, db));
            }
        }
    }
}
