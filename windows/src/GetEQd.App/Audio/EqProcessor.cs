using System;

namespace GetEQd.Audio
{
    /// <summary>
    /// The live signal chain. Runs on the WASAPI playback thread, so it never allocates,
    /// never locks and never touches WPF state.
    ///
    /// Order of operations per block:
    ///   1. per-channel six-band EQ
    ///   2. routing: bass management, LFE lane and surround upmix
    ///   3. spatial: stage width and crossfeed (headphone path only)
    ///   4. preamp, safety ceiling and metering
    ///
    /// Parameter changes are published as an immutable snapshot and then ramped, so
    /// dragging a fader never clicks.
    /// </summary>
    public sealed class EqProcessor
    {
        private const int MaxChannels = 8;
        private const int DelayLineLength = 512;
        private const double CrossfeedLowPassHz = 700.0;
        private const double CrossfeedDelaySeconds = 0.00025;

        /// <summary>Crossover between the mains and the sub lane.</summary>
        private const double CrossoverHz = 80.0;

        /// <summary>Butterworth section Q used twice per side to build a Linkwitz-Riley 4th order.</summary>
        private const double ButterworthQ = 0.7071067811865476;

        private readonly BiquadState[,] _bandState = new BiquadState[MaxChannels, Bands.Count];

        // Two cascaded sections per path, per channel: a Linkwitz-Riley 4th order crossover.
        private readonly BiquadState[,] _subLowState = new BiquadState[MaxChannels, 2];
        private readonly BiquadState[,] _mainHighState = new BiquadState[MaxChannels, 2];

        private readonly OnePole[] _crossfeedLow = new OnePole[2];
        private readonly double[] _delayLineLeft = new double[DelayLineLength];
        private readonly double[] _delayLineRight = new double[DelayLineLength];
        private readonly double[] _frame = new double[MaxChannels];

        private int _sampleRate = 48000;
        private int _channels = 2;
        private int _delayWrite;
        private int _delaySamples = 12;

        private Biquad[] _filters = new Biquad[Bands.Count];
        private Biquad _subLowSection = Biquad.Identity;
        private Biquad _mainHighSection = Biquad.Identity;
        private double _subLaneGain = 1.0;
        private double _ceilingLinear = 0.891;
        private double _centerGain = 1.0;
        private double _width = 1.0;
        private double _crossfeedAmount;
        private bool _crossfeedActive;
        private bool _subMono;
        private bool _bypassed;

        private readonly double[] _rampedGains = new double[Bands.Count];
        private readonly double[] _rampedFrequencies = new double[Bands.Count];
        private readonly double[] _rampedQs = new double[Bands.Count];
        private double _rampedPreamp;
        private double _rampedCeilingDb = -1.0;
        private double _rampedWidth = 1.0;
        private double _rampedCrossfeed;
        private double _rampedCenterLift;
        private bool _primed;

        private double _limiterGain = 1.0;
        private double _limiterAttack = 1.0;
        private double _limiterRelease = 1.0;
        private double _gainReduction = 1.0;

        private double _peakLeft;
        private double _peakRight;
        private double _peakMomentary;

        private volatile EqSnapshot? _target;
        private EqSnapshot? _applied;

        /// <summary>Most recent post-processing sample window, for the spectrum display.</summary>
        public float[] SpectrumTap { get; } = new float[4096];

        private int _spectrumWrite;

        public int SampleRate => _sampleRate;
        public int Channels => _channels;

        /// <summary>Longest delay the crossfeed line can hold, so callers do not over-read.</summary>
        public void Configure(int sampleRate, int channels)
        {
            _sampleRate = Math.Max(8000, sampleRate);
            _channels = Math.Max(1, Math.Min(MaxChannels, channels));
            _delaySamples = Math.Max(1, Math.Min(DelayLineLength - 1,
                (int)Math.Round(CrossfeedDelaySeconds * _sampleRate)));

            _limiterAttack = TimeCoefficient(0.0004);
            _limiterRelease = TimeCoefficient(0.150);

            // A Linkwitz-Riley 4th order is two cascaded Butterworth 2nd order sections.
            // Built this way the low and high outputs sum back to a flat magnitude, so
            // splitting the bass off does not change the tonal balance.
            _subLowSection = Biquad.LowPass(_sampleRate, CrossoverHz, ButterworthQ);
            _mainHighSection = Biquad.HighPass(_sampleRate, CrossoverHz, ButterworthQ);

            // Crossfeed shapes the low end only; 700 Hz keeps voices untouched.
            _crossfeedLow[0].Configure(_sampleRate, CrossfeedLowPassHz);
            _crossfeedLow[1].Configure(_sampleRate, CrossfeedLowPassHz);

            Reset();
        }

        private double TimeCoefficient(double seconds)
        {
            double samples = Math.Max(1.0, seconds * _sampleRate);
            return 1 - Math.Exp(-1.0 / samples);
        }

        public void Reset()
        {
            for (int c = 0; c < MaxChannels; c++)
            {
                for (int b = 0; b < Bands.Count; b++) _bandState[c, b].Reset();
                for (int section = 0; section < 2; section++)
                {
                    _subLowState[c, section].Reset();
                    _mainHighState[c, section].Reset();
                }
            }
            Array.Clear(_delayLineLeft, 0, _delayLineLeft.Length);
            Array.Clear(_delayLineRight, 0, _delayLineRight.Length);
            Array.Clear(SpectrumTap, 0, SpectrumTap.Length);
            _delayWrite = 0;
            _limiterGain = 1.0;
            _gainReduction = 1.0;
            _primed = false;
        }

        /// <summary>
        /// Publishes a new parameter set. Safe to call from the UI thread at any time;
        /// the values are ramped in on the audio thread.
        /// </summary>
        public void Publish(EqSnapshot snapshot)
        {
            _target = snapshot;
        }

        /// <summary>Applies a snapshot immediately, with no ramp. Used when starting playback.</summary>
        public void PublishImmediate(EqSnapshot snapshot)
        {
            _target = snapshot;
            _applied = null;
            _primed = false;
        }

        // ---------------------------------------------------------------- processing

        public void Process(float[] buffer, int offset, int count)
        {
            EqSnapshot? snapshot = _target;
            if (snapshot == null || count <= 0) return;

            int frames = count / _channels;
            if (frames <= 0) return;

            double ramp = 1 - Math.Exp(-(double)frames / (0.025 * _sampleRate));
            if (ramp > 1) ramp = 1;
            if (ramp < 0) ramp = 0;

            SyncParameters(snapshot, ramp);

            for (int frame = 0; frame < frames; frame++)
            {
                int origin = offset + frame * _channels;

                // ---- 1. per-channel six-band EQ
                for (int channel = 0; channel < _channels; channel++)
                {
                    double sample = buffer[origin + channel];
                    if (!_bypassed)
                    {
                        for (int band = 0; band < Bands.Count; band++)
                        {
                            sample = _bandState[channel, band].Process(sample, in _filters[band]);
                        }
                    }
                    _frame[channel] = sample;
                }

                double left = _frame[0];
                double right = _channels > 1 ? _frame[1] : left;

                // ---- 2. routing
                if (!_bypassed)
                {
                    RouteFrame(left, right);
                    left = _frame[0];
                    right = _channels > 1 ? _frame[1] : left;
                }

                // ---- 3. spatial
                if (!_bypassed && _channels >= 2)
                {
                    double mid = (left + right) * 0.5;
                    double side = (left - right) * 0.5 * _width;
                    left = mid + side;
                    right = mid - side;

                    if (_crossfeedActive && _crossfeedAmount > 0)
                    {
                        double lowLeft = _crossfeedLow[0].Process(left);
                        double lowRight = _crossfeedLow[1].Process(right);

                        int readIndex = _delayWrite - _delaySamples;
                        if (readIndex < 0) readIndex += DelayLineLength;

                        double delayedLeft = _delayLineLeft[readIndex];
                        double delayedRight = _delayLineRight[readIndex];

                        _delayLineLeft[_delayWrite] = lowLeft;
                        _delayLineRight[_delayWrite] = lowRight;
                        _delayWrite++;
                        if (_delayWrite >= DelayLineLength) _delayWrite = 0;

                        left += _crossfeedAmount * (delayedRight - lowLeft);
                        right += _crossfeedAmount * (delayedLeft - lowRight);
                    }

                    _frame[0] = left;
                    _frame[1] = right;
                }

                // ---- 4. preamp, ceiling, metering
                double preamp = _bypassed ? 1.0 : _rampedPreamp;
                double framePeak = 0;

                for (int channel = 0; channel < _channels; channel++)
                {
                    double sample = _frame[channel] * preamp;
                    double magnitude = sample < 0 ? -sample : sample;
                    if (magnitude > framePeak) framePeak = magnitude;
                }

                double limiterGain = 1.0;
                if (!_bypassed)
                {
                    double desired = framePeak > _ceilingLinear ? _ceilingLinear / framePeak : 1.0;
                    double coefficient = desired < _limiterGain ? _limiterAttack : _limiterRelease;
                    _limiterGain += (desired - _limiterGain) * coefficient;
                    if (_limiterGain > 1.0) _limiterGain = 1.0;
                    limiterGain = _limiterGain;
                }
                else
                {
                    // Let the ceiling recover while bypassed, so switching back in does not
                    // reopen with a stale gain reduction.
                    _limiterGain = 1.0;
                }

                if (limiterGain < _gainReduction) _gainReduction = limiterGain;

                double framePeakOut = 0;

                for (int channel = 0; channel < _channels; channel++)
                {
                    double sample = _frame[channel] * preamp * limiterGain;

                    // Final guard: the ceiling is a limiter, not a brick wall, so clamp
                    // hard at full scale to protect the device from a transient overshoot.
                    if (sample > 1.0) sample = 1.0;
                    else if (sample < -1.0) sample = -1.0;

                    buffer[origin + channel] = (float)sample;

                    double magnitude = sample < 0 ? -sample : sample;
                    if (magnitude > framePeakOut) framePeakOut = magnitude;
                }

                if (framePeakOut > _peakMomentary) _peakMomentary = framePeakOut;

                if (_channels >= 1)
                {
                    double leftOut = buffer[origin];
                    if (leftOut > _peakLeft) _peakLeft = leftOut;
                    else if (-leftOut > _peakLeft) _peakLeft = -leftOut;
                }
                if (_channels >= 2)
                {
                    double rightOut = buffer[origin + 1];
                    if (rightOut > _peakRight) _peakRight = rightOut;
                    else if (-rightOut > _peakRight) _peakRight = -rightOut;
                }

                SpectrumTap[_spectrumWrite] = (float)((left + right) * 0.5);
                _spectrumWrite++;
                if (_spectrumWrite >= SpectrumTap.Length) _spectrumWrite = 0;
            }
        }

        /// <summary>
        /// Bass management and upmix. A Linkwitz-Riley 4th order split at 80 Hz sends the
        /// low band to one lane and the mains keep the rest, so the sub is fed a single
        /// coherent signal instead of two fighting copies.
        /// </summary>
        private void RouteFrame(double left, double right)
        {
            if (_channels < 2)
            {
                _frame[0] = left;
                return;
            }

            if (!_subMono)
            {
                // Nothing to derive: keep the front pair and leave the rest silent.
                if (_channels > 2)
                {
                    for (int channel = 2; channel < _channels; channel++) _frame[channel] = 0;
                }
                return;
            }

            double lowLeft = LowBand(0, left);
            double lowRight = LowBand(1, right);
            double monoBass = (lowLeft + lowRight) * 0.5 * _subLaneGain;

            double mainLeft = HighBand(0, left);
            double mainRight = HighBand(1, right);

            if (_channels >= 6)
            {
                // True bass management: the mains hand their bottom octave to the LFE lane,
                // and the derived centre and surrounds stay out of the bass as well.
                _frame[0] = mainLeft;
                _frame[1] = mainRight;
                _frame[3] = monoBass;

                double mid = (mainLeft + mainRight) * 0.5;
                double side = (mainLeft - mainRight) * 0.5;

                _frame[2] = mid * _centerGain;
                _frame[4] = side * 0.75;
                _frame[5] = -side * 0.75;

                for (int channel = 6; channel < _channels; channel++) _frame[channel] = 0;
            }
            else
            {
                // A stereo pair carrying the summed sub lane, which is what a 2.1 rig does.
                // Both channels receive the identical low band, so the bass is mono.
                _frame[0] = mainLeft + monoBass;
                _frame[1] = mainRight + monoBass;
                for (int channel = 2; channel < _channels; channel++) _frame[channel] = 0;
            }
        }

        /// <summary>Linkwitz-Riley 4th order low band: two cascaded Butterworth sections.</summary>
        private double LowBand(int channel, double input)
        {
            double value = _subLowState[channel, 0].Process(input, in _subLowSection);
            return _subLowState[channel, 1].Process(value, in _subLowSection);
        }

        /// <summary>Linkwitz-Riley 4th order high band, the complement of <see cref="LowBand"/>.</summary>
        private double HighBand(int channel, double input)
        {
            double value = _mainHighState[channel, 0].Process(input, in _mainHighSection);
            return _mainHighState[channel, 1].Process(value, in _mainHighSection);
        }

        private void SyncParameters(EqSnapshot snapshot, double ramp)
        {
            bool fresh = !_primed || _applied == null;
            _applied = snapshot;

            // Ramp toward the published values. On the first block after a start or a
            // bypass change we jump straight there so nothing fades in.
            double step = fresh ? 1.0 : ramp;

            for (int band = 0; band < Bands.Count; band++)
            {
                _rampedGains[band] = Approach(_rampedGains[band], snapshot.Gains[band], step);
                _rampedFrequencies[band] = Approach(_rampedFrequencies[band], snapshot.Frequencies[band], step);
                _rampedQs[band] = Approach(_rampedQs[band], snapshot.Qs[band], step);
            }

            _rampedPreamp = Approach(_rampedPreamp, DecibelsToLinear(snapshot.PreampDb), step);
            _rampedCeilingDb = Approach(_rampedCeilingDb, snapshot.CeilingDb, step);
            _rampedWidth = Approach(_rampedWidth, snapshot.Width, step);
            _rampedCrossfeed = Approach(_rampedCrossfeed, snapshot.Crossfeed, step);
            _rampedCenterLift = Approach(_rampedCenterLift, snapshot.CenterLiftDb, step);

            _ceilingLinear = DecibelsToLinear(_rampedCeilingDb);
            _centerGain = DecibelsToLinear(_rampedCenterLift);
            _width = _rampedWidth;
            _crossfeedAmount = _rampedCrossfeed;
            _crossfeedActive = snapshot.CrossfeedActive;
            _subMono = snapshot.SubMono;
            _subLaneGain = DecibelsToLinear(snapshot.SubLaneDb);
            _bypassed = snapshot.Bypassed;

            // Rebuild the small, fixed coefficient bank every audio block. This keeps
            // frequency, Q and filter-type edits in lockstep with the ramped gain values;
            // ten biquads are inexpensive compared with the audio callback itself.
            for (int band = 0; band < Bands.Count; band++)
            {
                _filters[band] = snapshot.Enabled[band]
                    ? Biquad.ForBand(snapshot.Kinds[band], _sampleRate, _rampedFrequencies[band],
                        _rampedQs[band], _rampedGains[band])
                    : Biquad.Identity;
            }

            _primed = true;
        }

        private static double Approach(double current, double target, double step)
        {
            double difference = target - current;
            if (Math.Abs(difference) < 1e-5) return target;
            return current + difference * step;
        }

        private static double DecibelsToLinear(double db) => Math.Pow(10, db / 20.0);

        // ---------------------------------------------------------------- metering

        /// <summary>Returns the peaks seen since the previous call and restarts the window.</summary>
        public void ReadPeaks(out double left, out double right, out double momentary, out double reductionDb)
        {
            left = _peakLeft;
            right = _peakRight;
            momentary = _peakMomentary;
            reductionDb = 20 * Math.Log10(Math.Max(1e-6, _gainReduction));

            _peakLeft = 0;
            _peakRight = 0;
            _peakMomentary = 0;
            _gainReduction = 1.0;
        }

        /// <summary>Copies the most recent samples in chronological order for spectrum analysis.</summary>
        public void CopySpectrumWindow(float[] destination)
        {
            int length = Math.Min(destination.Length, SpectrumTap.Length);
            int start = _spectrumWrite - length;
            if (start < 0) start += SpectrumTap.Length;

            for (int i = 0; i < length; i++)
            {
                int index = start + i;
                if (index >= SpectrumTap.Length) index -= SpectrumTap.Length;
                destination[i] = SpectrumTap[index];
            }
        }
    }
}
