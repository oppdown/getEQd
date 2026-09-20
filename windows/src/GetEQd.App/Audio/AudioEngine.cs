using System;
using System.Collections.Generic;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GetEQd.Audio
{
    public enum PlaybackState
    {
        Idle,
        Playing,
        Paused,
        Ended,
        Faulted
    }

    public enum ProbeSignal
    {
        PinkNoise,
        Tone1k,
        SineSweep
    }

    /// <summary>One render endpoint the console can send audio to.</summary>
    public sealed class OutputDevice
    {
        public OutputDevice(string id, string name, int channels, int sampleRate)
        {
            Id = id;
            Name = name;
            Channels = channels;
            SampleRate = sampleRate;
        }

        public string Id { get; }
        public string Name { get; }
        public int Channels { get; }
        public int SampleRate { get; }

        public string ChannelLabel => Channels switch
        {
            1 => "mono",
            2 => "stereo",
            6 => "5.1",
            8 => "7.1",
            _ => Channels + " ch"
        };

        public override string ToString() => $"{Name} · {ChannelLabel} · {SampleRate / 1000.0:0.#} kHz";
    }

    /// <summary>
    /// Owns the WASAPI stream and the user's place in it. Everything here runs on the
    /// UI thread or a worker; the realtime path lives in <see cref="EqProcessor"/>.
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly EqProcessor _processor = new EqProcessor();
        private readonly object _gate = new object();

        private WasapiOut? _output;
        private AudioFileReader? _fileReader;
        private ISampleProvider? _chain;
        private ProbeSignalProvider? _probe;

        private string? _deviceId;
        private string _sourceName = "No source loaded";
        private bool _loop;
        private bool _stopping;
        private bool _disposed;

        private int _mixRate = 48000;
        private int _mixChannels = 2;
        private PlaybackState _state = PlaybackState.Idle;
        private string _statusDetail = "Idle. Open a file or start a probe signal.";

        public EqProcessor Processor => _processor;

        public PlaybackState State => _state;
        public string StatusDetail => _statusDetail;
        public string SourceName => _sourceName;
        public int MixRate => _mixRate;
        public int MixChannels => _mixChannels;
        public bool IsFileLoaded => _fileReader != null;

        public bool Loop
        {
            get => _loop;
            set => _loop = value;
        }

        public string SelectedDeviceId => _deviceId ?? string.Empty;

        // ------------------------------------------------------------------ devices

        public IReadOnlyList<OutputDevice> EnumerateDevices()
        {
            List<OutputDevice> devices = new List<OutputDevice>();

            try
            {
                MMDeviceCollection collection = _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (MMDevice device in collection)
                {
                    try
                    {
                        WaveFormat mix = device.AudioClient.MixFormat;
                        devices.Add(new OutputDevice(device.ID, device.FriendlyName, mix.Channels, mix.SampleRate));
                    }
                    catch
                    {
                        devices.Add(new OutputDevice(device.ID, device.FriendlyName, 2, 48000));
                    }
                    finally
                    {
                        device.Dispose();
                    }
                }
            }
            catch
            {
                // A machine with no render endpoints is a valid state; the console says so.
            }

            return devices;
        }

        public OutputDevice? DefaultDevice()
        {
            foreach (OutputDevice device in EnumerateDevices())
            {
                try
                {
                    using MMDevice? fallback = _enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                        : null;

                    if (fallback != null && fallback.ID == device.Id) return device;
                }
                catch
                {
                    // Ignore and fall through to the first endpoint.
                }
            }

            IReadOnlyList<OutputDevice> all = EnumerateDevices();
            return all.Count > 0 ? all[0] : null;
        }

        public void SelectDevice(string deviceId)
        {
            if (string.Equals(_deviceId, deviceId, StringComparison.Ordinal)) return;

            bool wasPlaying = _state == PlaybackState.Playing;
            Stop();
            _deviceId = deviceId;
            RefreshMixFormat();
            _statusDetail = "Output set to " + DescribeDevice(deviceId) + ".";

            if (wasPlaying) Play();
        }

        private string DescribeDevice(string deviceId)
        {
            foreach (OutputDevice device in EnumerateDevices())
            {
                if (device.Id == deviceId) return device.Name;
            }
            return "the selected endpoint";
        }

        private void RefreshMixFormat()
        {
            try
            {
                if (!string.IsNullOrEmpty(_deviceId))
                {
                    using MMDevice device = _enumerator.GetDevice(_deviceId);
                    WaveFormat mix = device.AudioClient.MixFormat;
                    _mixRate = mix.SampleRate;
                    _mixChannels = Math.Max(1, Math.Min(8, mix.Channels));
                    return;
                }
            }
            catch
            {
                // Fall back to the friendly defaults below.
            }

            _mixRate = 48000;
            _mixChannels = 2;
        }

        // ------------------------------------------------------------------ sources

        public void LoadFile(string path)
        {
            Stop();

            lock (_gate)
            {
                _fileReader?.Dispose();
                _fileReader = new AudioFileReader(path);
                _probe = null;
                _sourceName = System.IO.Path.GetFileName(path);
            }

            _statusDetail = $"Loaded {_sourceName}. Press play to hear the current curve.";
            _state = PlaybackState.Idle;
        }

        public void UseProbe(ProbeSignal signal)
        {
            Stop();

            lock (_gate)
            {
                _fileReader?.Dispose();
                _fileReader = null;
                _probe = new ProbeSignalProvider(signal, _mixRate);
                _sourceName = signal switch
                {
                    ProbeSignal.PinkNoise => "Probe · pink noise",
                    ProbeSignal.Tone1k => "Probe · 1 kHz tone",
                    _ => "Probe · sine sweep 20 Hz–20 kHz"
                };
            }

            _statusDetail = "Probe signal armed. Press play, then move a band.";
            _state = PlaybackState.Idle;
        }

        // ------------------------------------------------------------------ transport

        public void Play()
        {
            if (_disposed) return;

            lock (_gate)
            {
                if (_chain == null || _output == null)
                {
                    if (!BuildChain()) return;
                }
            }

            try
            {
                _output!.Play();
                _state = PlaybackState.Playing;
                _statusDetail = "Playing · " + _sourceName;
            }
            catch (Exception error)
            {
                _state = PlaybackState.Faulted;
                _statusDetail = "Could not start playback: " + error.Message;
            }
        }

        public void Pause()
        {
            if (_output == null) return;
            try
            {
                _output.Pause();
                _state = PlaybackState.Paused;
                _statusDetail = "Paused.";
            }
            catch (Exception error)
            {
                _state = PlaybackState.Faulted;
                _statusDetail = "Could not pause: " + error.Message;
            }
        }

        public void Stop()
        {
            _stopping = true;

            try
            {
                _output?.Stop();
            }
            catch
            {
                // A device that vanished mid-stream is not worth reporting twice.
            }

            TeardownOutput();

            _stopping = false;
            _state = PlaybackState.Idle;
        }

        public void Toggle()
        {
            switch (_state)
            {
                case PlaybackState.Playing:
                    Pause();
                    break;
                case PlaybackState.Paused:
                    Play();
                    break;
                default:
                    Play();
                    break;
            }
        }

        public void Seek(TimeSpan position)
        {
            lock (_gate)
            {
                if (_fileReader == null) return;
                TimeSpan clamped = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                if (clamped > _fileReader.TotalTime) clamped = _fileReader.TotalTime;
                _fileReader.CurrentTime = clamped;
                _processor.Reset();
            }
        }

        public TimeSpan Position
        {
            get
            {
                lock (_gate)
                {
                    return _fileReader?.CurrentTime ?? TimeSpan.Zero;
                }
            }
        }

        public TimeSpan Duration
        {
            get
            {
                lock (_gate)
                {
                    return _fileReader?.TotalTime ?? TimeSpan.Zero;
                }
            }
        }

        // ------------------------------------------------------------------ chain

        private bool BuildChain()
        {
            if (_fileReader == null && _probe == null)
            {
                _statusDetail = "Nothing to play yet. Open a file or start a probe signal.";
                _state = PlaybackState.Idle;
                return false;
            }

            RefreshMixFormat();

            try
            {
                ISampleProvider source;
                if (_fileReader != null)
                {
                    ISampleProvider reader = _fileReader;
                    source = reader.WaveFormat.SampleRate == _mixRate
                        ? reader
                        : new WdlResamplingSampleProvider(reader, _mixRate);
                }
                else
                {
                    _probe!.Configure(_mixRate);
                    source = _probe;
                }

                _processor.Configure(_mixRate, _mixChannels);
                _chain = new EngineSampleProvider(source, _processor, _mixChannels);

                MMDevice device = string.IsNullOrEmpty(_deviceId)
                    ? _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                    : _enumerator.GetDevice(_deviceId);

                _output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60);
                _output.PlaybackStopped += OnPlaybackStopped;
                _output.Init(_chain.ToWaveProvider());
                return true;
            }
            catch (Exception error)
            {
                TeardownOutput();
                _state = PlaybackState.Faulted;
                _statusDetail = "Audio device refused the stream: " + error.Message;
                return false;
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
        {
            if (_stopping) return;

            Exception? failure = args.Exception;

            if (failure != null)
            {
                _state = PlaybackState.Faulted;
                _statusDetail = "Stream stopped: " + failure.Message;
                TeardownOutput();
                return;
            }

            if (_loop && _fileReader != null)
            {
                // Restart off the callback so the device has finished tearing down.
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(30);
                    if (_disposed || !_loop) return;
                    Seek(TimeSpan.Zero);
                    Play();
                });
                return;
            }

            _state = _fileReader != null ? PlaybackState.Ended : PlaybackState.Idle;
            _statusDetail = _fileReader != null ? "Reached the end of " + _sourceName + "." : "Probe stopped.";
        }

        private void TeardownOutput()
        {
            if (_output != null)
            {
                _output.PlaybackStopped -= OnPlaybackStopped;
                try
                {
                    _output.Dispose();
                }
                catch
                {
                    // Ignore teardown races with a removed device.
                }
                _output = null;
            }

            _chain = null;
        }

        // ------------------------------------------------------------------ metering

        public void ReadMeters(out double left, out double right, out double momentary, out double reductionDb)
        {
            _processor.ReadPeaks(out left, out right, out momentary, out reductionDb);
        }

        public void Dispose()
        {
            _disposed = true;
            _stopping = true;
            TeardownOutput();
            _fileReader?.Dispose();
            _fileReader = null;
            _enumerator.Dispose();
        }
    }

    /// <summary>
    /// Adapts a stereo sample source to the endpoint channel count and runs every frame
    /// through the processor. Pre-allocates its scratch buffer so the audio thread never
    /// allocates.
    /// </summary>
    internal sealed class EngineSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EqProcessor _processor;
        private float[] _scratch = new float[8192];

        public EngineSampleProvider(ISampleProvider source, EqProcessor processor, int outputChannels)
        {
            _source = source;
            _processor = processor;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outputChannels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int outputChannels = WaveFormat.Channels;
            int frames = count / outputChannels;
            if (frames <= 0) return 0;

            int sourceChannels = _source.WaveFormat.Channels;
            int needed = frames * sourceChannels;
            if (_scratch.Length < needed) _scratch = new float[needed];

            int sourceSamples = _source.Read(_scratch, 0, needed);
            int sourceFrames = sourceChannels > 0 ? sourceSamples / sourceChannels : 0;

            for (int frame = 0; frame < sourceFrames; frame++)
            {
                int destination = offset + frame * outputChannels;
                int origin = frame * sourceChannels;

                for (int channel = 0; channel < outputChannels; channel++)
                {
                    // Routing rewrites every derived channel later; seed the front pair so
                    // extra channels always start from defined values.
                    buffer[destination + channel] = channel < sourceChannels
                        ? _scratch[origin + channel]
                        : 0f;
                }
            }

            int produced = sourceFrames * outputChannels;
            if (produced > 0) _processor.Process(buffer, offset, produced);
            return produced;
        }
    }

    /// <summary>
    /// Generates a probe signal so the curve can be auditioned with no file loaded.
    /// Pink noise uses Paul Kellet's filter; the sweep is a logarithmic 20 Hz–20 kHz pass.
    /// </summary>
    internal sealed class ProbeSignalProvider : ISampleProvider
    {
        private readonly ProbeSignal _signal;
        private readonly Random _random = new Random(20260910);

        private readonly double[] _pinkLeft = new double[7];
        private readonly double[] _pinkRight = new double[7];

        private double _phase;
        private double _sweepPosition;
        private int _sampleRate;

        private const double SweepSeconds = 6.0;

        public ProbeSignalProvider(ProbeSignal signal, int sampleRate)
        {
            _signal = signal;
            Configure(sampleRate);
        }

        public void Configure(int sampleRate)
        {
            _sampleRate = Math.Max(8000, sampleRate);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);
            _phase = 0;
            _sweepPosition = 0;
        }

        public WaveFormat WaveFormat { get; private set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            double leftValue = 0;
            double rightValue = 0;

            for (int frame = 0; frame < frames; frame++)
            {
                switch (_signal)
                {
                    case ProbeSignal.PinkNoise:
                        leftValue = Pink(_pinkLeft);
                        rightValue = Pink(_pinkRight);
                        break;

                    case ProbeSignal.Tone1k:
                        leftValue = rightValue = 0.28 * Math.Sin(_phase);
                        _phase = Advance(_phase, 1000.0);
                        break;

                    default:
                        // Logarithmic sweep, then a short rest before repeating.
                        double progress = _sweepPosition / (_sampleRate * SweepSeconds);
                        if (progress >= 1.0)
                        {
                            _sweepPosition = 0;
                            progress = 0;
                        }

                        double frequency = 20 * Math.Pow(1000.0, progress);
                        double envelope = Math.Sin(Math.PI * progress);

                        leftValue = rightValue = 0.30 * envelope * Math.Sin(_phase);
                        _phase = Advance(_phase, frequency);
                        _sweepPosition++;
                        break;
                }

                buffer[offset + frame * 2] = (float)leftValue;
                buffer[offset + frame * 2 + 1] = (float)rightValue;
            }

            return frames * 2;
        }

        private double Advance(double phase, double frequency)
        {
            double next = phase + 2 * Math.PI * frequency / _sampleRate;
            return next > 2 * Math.PI * 1000 ? next - 2 * Math.PI * 1000 : next;
        }

        /// <summary>Kellet's refined pink noise filter, at roughly -3 dB per octave.</summary>
        private double Pink(double[] state)
        {
            double white = _random.NextDouble() * 2 - 1;

            state[0] = 0.99886 * state[0] + white * 0.0555179;
            state[1] = 0.99332 * state[1] + white * 0.0750759;
            state[2] = 0.96900 * state[2] + white * 0.1538520;
            state[3] = 0.86650 * state[3] + white * 0.3104856;
            state[4] = 0.55000 * state[4] + white * 0.5329522;
            state[5] = -0.7616 * state[5] - white * 0.0168980;

            double pink = state[0] + state[1] + state[2] + state[3] + state[4] + state[5] + state[6] + white * 0.5362;
            state[6] = white * 0.115926;

            return pink * 0.11;
        }
    }
}
