using System;
using System.Collections.Generic;

namespace GetEQd.Audio
{
    /// <summary>Where the processed signal is being sent.</summary>
    public enum Route
    {
        Surround51,
        Stereo21,
        Headphones
    }

    /// <summary>
    /// Headphone starting curves. These are broad category shapes, deliberately not
    /// presented as measured model targets.
    /// </summary>
    public enum HeadphoneTarget
    {
        NeutralReference,
        ClosedBackPunch,
        OpenBackAir,
        GamingClarity
    }

    /// <summary>Which downstream stage owns spatial presentation.</summary>
    public enum SoftwareHandoff
    {
        DirectStereo,
        WindowsSpatialSound,
        GameSpatialMix
    }

    /// <summary>Static description of one quick-mode band or advanced slot.</summary>
    public sealed class BandDefinition
    {
        public BandDefinition(string name, string role, double frequencyHz, BandKind kind, double q, string frequencyLabel)
        {
            Name = name;
            Role = role;
            FrequencyHz = frequencyHz;
            Kind = kind;
            DefaultQ = q;
            FrequencyLabel = frequencyLabel;
        }

        public string Name { get; }
        public string Role { get; }
        public double FrequencyHz { get; }
        public BandKind Kind { get; }
        public double DefaultQ { get; }
        public string FrequencyLabel { get; }
    }

    /// <summary>Quick bands plus four disabled advanced slots.</summary>
    public static class Bands
    {
        public const int QuickCount = 6;
        public const int Count = 10;

        public const double MinGainDb = -12.0;
        public const double MaxGainDb = 11.0;
        public const double GainStepDb = 0.5;

        public static readonly BandDefinition[] All =
        {
            new BandDefinition("Sub foundation", "Room / weight", 32, BandKind.LowShelf, 0.70, "32 Hz"),
            new BandDefinition("Kick body", "Punch / impact", 64, BandKind.Peaking, 1.00, "64 Hz"),
            new BandDefinition("Low-mid", "Warmth / mud", 250, BandKind.Peaking, 1.10, "250 Hz"),
            new BandDefinition("Presence", "Voice / attack", 2500, BandKind.Peaking, 0.90, "2.5 kHz"),
            new BandDefinition("Detail", "Clarity / bite", 6400, BandKind.Peaking, 1.00, "6.4 kHz"),
            new BandDefinition("Air", "Space / shimmer", 12000, BandKind.HighShelf, 0.70, "12 kHz"),
            new BandDefinition("Advanced 07", "Optional parametric slot", 125, BandKind.Peaking, 1.00, "125 Hz"),
            new BandDefinition("Advanced 08", "Optional parametric slot", 1000, BandKind.Peaking, 1.00, "1 kHz"),
            new BandDefinition("Advanced 09", "Optional parametric slot", 4000, BandKind.Peaking, 1.00, "4 kHz"),
            new BandDefinition("Advanced 10", "Optional parametric slot", 16000, BandKind.Peaking, 1.00, "16 kHz")
        };

        /// <summary>Frequency used for the "centre" of each band when analysing headroom.</summary>
        public static readonly double[] AnalysisFrequencies = { 32, 64, 250, 2500, 6400, 12000, 125, 1000, 4000, 16000 };
    }

    /// <summary>Factory presets, carried over one-for-one from the web console.</summary>
    public static class Presets
    {
        public sealed class Preset
        {
            public Preset(string id, string label, string blurb, double[] gains)
            {
                Id = id;
                Label = label;
                Blurb = blurb;
                Gains = gains;
            }

            public string Id { get; }
            public string Label { get; }
            public string Blurb { get; }
            public double[] Gains { get; }
        }

        public static readonly Preset[] All =
        {
            new Preset("reference", "Reference", "Flat. The honest starting point.", new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }),
            new Preset("impact", "Impact", "Sub and kick forward, low-mid pulled back.", new[] { 4.0, 3.0, -1.5, 1.0, 1.5, 0.5, 0.0, 0.0, 0.0, 0.0 }),
            new Preset("dialogue", "Dialogue", "Bass trimmed, presence lifted for speech.", new[] { -2.0, -1.0, -2.0, 2.5, 1.5, 0.0, 0.0, 0.0, 0.0, 0.0 }),
            new Preset("night", "Night", "Quiet-hours contour: less bottom, less bite.", new[] { -3.0, -2.0, 1.0, 1.0, -1.0, -2.0, 0.0, 0.0, 0.0, 0.0 })
        };

        public static Preset? Find(string id)
        {
            foreach (Preset preset in All)
            {
                if (string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)) return preset;
            }
            return null;
        }
    }

    /// <summary>Category trims applied on top of the faders in the headphone path.</summary>
    public static class HeadphoneTargets
    {
        public sealed class Target
        {
            public Target(HeadphoneTarget id, string label, string note, double[] trimDb)
            {
                Id = id;
                Label = label;
                Note = note;
                TrimDb = trimDb;
            }

            public HeadphoneTarget Id { get; }
            public string Label { get; }
            public string Note { get; }
            public double[] TrimDb { get; }
        }

        public static readonly Target[] All =
        {
            new Target(HeadphoneTarget.NeutralReference, "Neutral reference",
                "No trim. The faders are the whole story.", new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 }),

            new Target(HeadphoneTarget.ClosedBackPunch, "Closed-back punch",
                "Eases the sealed-cup low bump and adds a little bite.",
                new[] { -1.5, -1.0, 0.5, 0.5, 1.0, 0.0 }),

            new Target(HeadphoneTarget.OpenBackAir, "Open-back air",
                "Gentle top-end lift to fill in an open cup.",
                new[] { 0.5, 0.5, 0.0, 0.0, 0.5, 1.5 }),

            new Target(HeadphoneTarget.GamingClarity, "Gaming headset clarity",
                "Mud out of the way, footsteps and cues forward.",
                new[] { -3.0, -2.0, -1.5, 2.5, 2.0, 0.5 })
        };

        public static Target Find(HeadphoneTarget id)
        {
            foreach (Target target in All)
            {
                if (target.Id == id) return target;
            }
            return All[0];
        }
    }

    /// <summary>
    /// Mutable editing state. Only the UI thread mutates this; the audio thread only
    /// ever sees the immutable <see cref="EqSnapshot"/> produced from it.
    /// </summary>
    public sealed class EqSettings
    {
        public double[] Gains { get; } = new double[Bands.Count];
        public double[] Qs { get; } = new double[Bands.Count];
        public double[] Frequencies { get; } = new double[Bands.Count];
        public BandKind[] Kinds { get; } = new BandKind[Bands.Count];
        public bool[] Enabled { get; } = new bool[Bands.Count];

        public double PreampDb { get; set; }
        public double CeilingDb { get; set; } = -1.0;

        public Route Route { get; set; } = Route.Surround51;
        public HeadphoneTarget Target { get; set; } = HeadphoneTarget.NeutralReference;
        public SoftwareHandoff Handoff { get; set; } = SoftwareHandoff.DirectStereo;
        public double CrossfeedPercent { get; set; } = 10.0;
        public double StageWidthPercent { get; set; } = 100.0;

        public bool TargetTrimEnabled { get; set; } = true;
        public bool BassToSub { get; set; } = true;
        public double SubLaneDb { get; set; }
        public double CenterLiftDb { get; set; } = 1.5;
        public bool Bypassed { get; set; }

        public string ActivePresetId { get; set; } = "reference";
        public bool AdvancedMode { get; set; }

        public EqSettings()
        {
            for (int i = 0; i < Bands.Count; i++)
            {
                Qs[i] = Bands.All[i].DefaultQ;
                Frequencies[i] = Bands.All[i].FrequencyHz;
                Kinds[i] = Bands.All[i].Kind;
                Enabled[i] = i < Bands.QuickCount;
            }
        }

        public void ApplyPreset(string presetId)
        {
            Presets.Preset? preset = Presets.Find(presetId);
            if (preset == null) return;

            for (int i = 0; i < Bands.Count; i++)
            {
                Gains[i] = preset.Gains[i];
                if (i >= Bands.QuickCount) Enabled[i] = false;
            }
            ActivePresetId = preset.Id;
        }

        public void Reset()
        {
            for (int i = 0; i < Bands.Count; i++)
            {
                Gains[i] = 0;
                Qs[i] = Bands.All[i].DefaultQ;
                Frequencies[i] = Bands.All[i].FrequencyHz;
                Kinds[i] = Bands.All[i].Kind;
                Enabled[i] = i < Bands.QuickCount;
            }
            PreampDb = 0;
            CeilingDb = -1.0;
            CrossfeedPercent = 10;
            StageWidthPercent = 100;
            SubLaneDb = 0;
            CenterLiftDb = 1.5;
            BassToSub = true;
            TargetTrimEnabled = true;
            Bypassed = false;
            ActivePresetId = "reference";
            AdvancedMode = false;
        }

        /// <summary>True when no fader, preamp or contour deviates from flat.</summary>
        public bool IsFlat()
        {
            for (int i = 0; i < Bands.Count; i++)
            {
                if (Math.Abs(Gains[i]) > 1e-9) return false;
            }
            return Math.Abs(PreampDb) < 1e-9;
        }

        /// <summary>
        /// The headphone target's offset for one band, or zero when no trim applies.
        /// The faders carry the listener's own values; the trim rides on top of them.
        /// </summary>
        public double TrimFor(int band)
        {
            if (band < 0 || band >= Bands.QuickCount) return 0;
            if (Route != Route.Headphones || !TargetTrimEnabled) return 0;
            return HeadphoneTargets.Find(Target).TrimDb[band];
        }

        /// <summary>
        /// What one band is actually contributing to the response, trim included. The
        /// interface draws and edits in these terms so a handle always sits on the curve.
        /// </summary>
        public double EffectiveGain(int band)
        {
            if (band < 0 || band >= Bands.Count) return 0;
            double value = Gains[band] + TrimFor(band);
            return Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, value));
        }

        /// <summary>Stores an effective gain, backing out any target trim first.</summary>
        public void SetEffectiveGain(int band, double effectiveGain)
        {
            if (band < 0 || band >= Bands.Count) return;
            Gains[band] = Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, effectiveGain - TrimFor(band)));
        }

        public double[] EffectiveGains()
        {
            double[] effective = new double[Bands.Count];
            for (int i = 0; i < Bands.Count; i++) effective[i] = EffectiveGain(i);
            return effective;
        }

        public EqSnapshot CreateSnapshot() => EqSnapshot.From(this);
    }

    /// <summary>
    /// Immutable view of the whole signal path. One instance is published to the audio
    /// thread at a time, so every parameter change is applied atomically.
    /// </summary>
    public sealed class EqSnapshot
    {
        private EqSnapshot(
            double[] gains, double[] frequencies, double[] qs, BandKind[] kinds, bool[] enabled,
            double preampDb, double ceilingDb,
            double crossfeed, double width, bool crossfeedActive, double centerLiftDb,
            bool subMono, double subLaneDb, bool bypassed, Route route, string summary)
        {
            Gains = gains;
            Frequencies = frequencies;
            Qs = qs;
            Kinds = kinds;
            Enabled = enabled;
            PreampDb = preampDb;
            CeilingDb = ceilingDb;
            Crossfeed = crossfeed;
            Width = width;
            CrossfeedActive = crossfeedActive;
            CenterLiftDb = centerLiftDb;
            SubMono = subMono;
            SubLaneDb = subLaneDb;
            Bypassed = bypassed;
            Route = route;
            Summary = summary;
        }

        public double[] Gains { get; }
        public double[] Frequencies { get; }
        public double[] Qs { get; }
        public BandKind[] Kinds { get; }
        public bool[] Enabled { get; }
        public double PreampDb { get; }
        public double CeilingDb { get; }
        public double Crossfeed { get; }
        public double Width { get; }
        public bool CrossfeedActive { get; }
        public double CenterLiftDb { get; }
        public bool SubMono { get; }
        public double SubLaneDb { get; }
        public bool Bypassed { get; }
        public Route Route { get; }
        public string Summary { get; }

        public static EqSnapshot From(EqSettings settings)
        {
            double[] gains = settings.EffectiveGains();
            double[] frequencies = new double[Bands.Count];
            double[] qs = new double[Bands.Count];
            BandKind[] kinds = new BandKind[Bands.Count];
            bool[] enabled = new bool[Bands.Count];

            for (int i = 0; i < Bands.Count; i++)
            {
                frequencies[i] = settings.Frequencies[i];
                qs[i] = settings.Qs[i];
                kinds[i] = settings.Kinds[i];
                enabled[i] = settings.Enabled[i];
            }

            // Spatial presentation belongs to Windows or the game engine when either of
            // those handoffs is selected, so getEQd's own crossfeed has to stand down.
            bool crossfeedActive = settings.Route == Route.Headphones
                && settings.Handoff == SoftwareHandoff.DirectStereo;

            return new EqSnapshot(
                gains, frequencies, qs, kinds, enabled,
                settings.PreampDb, settings.CeilingDb,
                settings.CrossfeedPercent / 100.0, settings.StageWidthPercent / 100.0,
                crossfeedActive, settings.CenterLiftDb,
                settings.Route != Route.Headphones && settings.BassToSub, settings.SubLaneDb,
                settings.Bypassed, settings.Route,
                Describe(settings));
        }

        private static string Describe(EqSettings settings)
        {
            switch (settings.Route)
            {
                case Route.Headphones:
                    return "Headphones · " + HeadphoneTargets.Find(settings.Target).Label;
                case Route.Stereo21:
                    return settings.BassToSub ? "2.1 Stereo · bass to sub" : "2.1 Stereo";
                default:
                    return settings.BassToSub ? "5.1 Surround · bass to sub" : "5.1 Surround";
            }
        }

        /// <summary>Builds the running sections for a given device sample rate.</summary>
        public Biquad[] BuildFilters(int sampleRate)
        {
            Biquad[] filters = new Biquad[Bands.Count];
            for (int i = 0; i < Bands.Count; i++)
            {
                filters[i] = Enabled[i]
                    ? Biquad.ForBand(Kinds[i], sampleRate, Frequencies[i], Qs[i], Gains[i])
                    : Biquad.Identity;
            }
            return filters;
        }

        /// <summary>Combined analytic response of every band plus the preamp, in dB.</summary>
        public double CurveDbAt(int sampleRate, double frequency)
        {
            double total = PreampDb;
            for (int i = 0; i < Bands.Count; i++)
            {
                if (Enabled[i])
                {
                    total += Biquad
                        .ForBand(Kinds[i], sampleRate, Frequencies[i], Qs[i], Gains[i])
                        .GainDbAt(sampleRate, frequency);
                }
            }
            return total;
        }
    }

    /// <summary>Headroom bookkeeping shown to the listener.</summary>
    public readonly struct HeadroomAnalysis
    {
        public HeadroomAnalysis(double peakCurveDb, double averageCurveDb, double suggestedPreampDb, bool limiterWillEngage, string message)
        {
            PeakCurveDb = peakCurveDb;
            AverageCurveDb = averageCurveDb;
            SuggestedPreampDb = suggestedPreampDb;
            LimiterWillEngage = limiterWillEngage;
            Message = message;
        }

        /// <summary>Largest boost anywhere in the audible curve, in dB of gain.</summary>
        public double PeakCurveDb { get; }

        /// <summary>Average response across the band centres, in dB of gain.</summary>
        public double AverageCurveDb { get; }

        /// <summary>
        /// The preamp that puts material already peaking at full scale exactly on the
        /// ceiling. Applying it is the gain-staging move that keeps the limiter idle.
        /// </summary>
        public double SuggestedPreampDb { get; }

        public bool LimiterWillEngage { get; }
        public string Message { get; }
    }

    public static class Analysis
    {
        /// <summary>
        /// Works out how much headroom the current curve is asking for.
        ///
        /// The curve is a set of gains, so whether the ceiling engages depends on the
        /// programme level as well. The useful assumption is material that already peaks
        /// at full scale: that is the case where a boost runs out of room, and it is what
        /// the suggested preamp is measured against.
        /// </summary>
        public static HeadroomAnalysis Evaluate(EqSnapshot snapshot, int sampleRate)
        {
            double peak = double.NegativeInfinity;
            double sum = 0;

            // A fixed list of quick-band centres is not enough once an advanced slot can
            // move anywhere on the log-frequency axis. Scan the whole audible range and
            // also sample every exact slot centre so narrow Q values cannot hide a peak.
            const int curveSamples = 240;
            int sampleCount = curveSamples + 1;
            for (int i = 0; i <= curveSamples; i++)
            {
                double frequency = 20 * Math.Pow(1000, i / (double)curveSamples);
                double db = snapshot.CurveDbAt(sampleRate, frequency);
                if (db > peak) peak = db;
                sum += db;
            }

            for (int i = 0; i < snapshot.Frequencies.Length; i++)
            {
                double db = snapshot.CurveDbAt(sampleRate, snapshot.Frequencies[i]);
                if (db > peak) peak = db;
                sum += db;
                sampleCount++;
            }

            double average = sum / sampleCount;
            double suggested = snapshot.CeilingDb - peak;
            bool limiterEngages = peak > snapshot.CeilingDb + 0.05;

            string message;
            if (snapshot.Bypassed)
            {
                message = "Bypassed. Audio is passing through untouched.";
            }
            else if (peak <= 0.05)
            {
                message = "No boost above unity, so the curve cannot clip anything on its own.";
            }
            else if (limiterEngages)
            {
                message = $"Peak boost is +{peak:0.0} dB, which would push full-scale material " +
                          $"{(-suggested):0.0} dB past the ceiling. Trim the preamp and the ceiling stays idle.";
            }
            else
            {
                message = $"Peak boost is +{peak:0.0} dB and the ceiling is at {snapshot.CeilingDb:0.0} dB, " +
                          "so full-scale material still fits underneath it.";
            }

            return new HeadroomAnalysis(peak, average, suggested, limiterEngages, message);
        }
    }
}
