using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GetEQd.Audio
{
    /// <summary>
    /// A measured frequency response, in the same getEQd-profile/v1 shape the web console
    /// accepts. Kept exactly as imported; getEQd never edits the source measurement.
    /// </summary>
    public sealed class MeasurementProfile
    {
        [JsonPropertyName("schema")]
        public string Schema { get; set; } = "getEQd-profile/v1";

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("measuredAt")]
        public string MeasuredAt { get; set; } = string.Empty;

        [JsonPropertyName("rig")]
        public string Rig { get; set; } = string.Empty;

        [JsonPropertyName("target")]
        public string Target { get; set; } = string.Empty;

        [JsonPropertyName("responseType")]
        public string ResponseType { get; set; } = "correction";

        [JsonPropertyName("notes")]
        public string Notes { get; set; } = string.Empty;

        [JsonPropertyName("frequenciesHz")]
        public double[] FrequenciesHz { get; set; } = Array.Empty<double>();

        [JsonPropertyName("gainDb")]
        public double[] GainDb { get; set; } = Array.Empty<double>();

        [JsonPropertyName("targetDb")]
        public double[] TargetDb { get; set; } = Array.Empty<double>();

        [JsonIgnore]
        public int PointCount => Math.Min(FrequenciesHz.Length, GainDb.Length);

        [JsonIgnore]
        public bool IsRaw => string.Equals(ResponseType, "raw", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string ResponseTypeLabel => IsRaw ? "Raw measurement" : "Correction data";

        [JsonIgnore]
        public string TargetLabel => string.IsNullOrWhiteSpace(Target) ? "Flat target" : Target;

        [JsonIgnore]
        public string RigLabel => string.IsNullOrWhiteSpace(Rig) ? "Rig not supplied" : Rig;

        [JsonIgnore]
        public string MeasuredAtLabel
        {
            get
            {
                if (string.IsNullOrWhiteSpace(MeasuredAt)) return "Date not supplied";
                return DateTime.TryParse(MeasuredAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed)
                    ? parsed.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
                    : MeasuredAt;
            }
        }
    }

    /// <summary>Serialisable form of every control on the console.</summary>
    public sealed class ListeningSettings
    {
        public double[] Bands { get; set; } = new double[GetEQd.Audio.Bands.Count];
        public double[] Widths { get; set; } = new double[GetEQd.Audio.Bands.Count];
        // Optional so profiles written before Advanced EQ was introduced retain their
        // original six-band behavior when they are followed.
        public double[] Frequencies { get; set; } = Array.Empty<double>();
        public string[] Types { get; set; } = Array.Empty<string>();
        public bool[] Enabled { get; set; } = Array.Empty<bool>();
        public double Preamp { get; set; }
        public double Ceiling { get; set; } = -1;

        [JsonPropertyName("route")]
        public string Route { get; set; } = "Surround51";

        [JsonPropertyName("headphoneTarget")]
        public string HeadphoneTarget { get; set; } = "NeutralReference";

        [JsonPropertyName("handoff")]
        public string Handoff { get; set; } = "DirectStereo";

        public double Crossfeed { get; set; } = 10;
        public double StageWidth { get; set; } = 100;
        public bool TargetTrim { get; set; } = true;
        public bool BassToSub { get; set; } = true;
        public double SubLane { get; set; }
        public double CenterLift { get; set; } = 1.5;
        public bool Bypassed { get; set; }
        public string Preset { get; set; } = "reference";
        public bool AdvancedMode { get; set; }
    }

    public sealed class ListeningProfile
    {
        public string Name { get; set; } = string.Empty;
        public string SavedAt { get; set; } = string.Empty;
        public ListeningSettings Settings { get; set; } = new ListeningSettings();

        [JsonIgnore]
        public string SavedAtLabel
        {
            get
            {
                return DateTime.TryParse(SavedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
                    ? parsed.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)
                    : "Unsaved date";
            }
        }

        [JsonIgnore]
        public string BandSummary
        {
            get
            {
                List<string> parts = new List<string>();
                foreach (double gain in Settings.Bands)
                {
                    parts.Add((gain > 0 ? "+" : string.Empty) + gain.ToString("0.0", CultureInfo.InvariantCulture) + " dB");
                }
                return string.Join(" / ", parts);
            }
        }
    }

    public static class ProfileMapping
    {
        public static ListeningSettings ToSettings(EqSettings settings)
        {
            ListeningSettings mapped = new ListeningSettings
            {
                Bands = new double[GetEQd.Audio.Bands.Count],
                Widths = new double[GetEQd.Audio.Bands.Count],
                Frequencies = new double[GetEQd.Audio.Bands.Count],
                Types = new string[GetEQd.Audio.Bands.Count],
                Enabled = new bool[GetEQd.Audio.Bands.Count],
                Preamp = settings.PreampDb,
                Ceiling = settings.CeilingDb,
                Route = settings.Route.ToString(),
                HeadphoneTarget = settings.Target.ToString(),
                Handoff = settings.Handoff.ToString(),
                Crossfeed = settings.CrossfeedPercent,
                StageWidth = settings.StageWidthPercent,
                TargetTrim = settings.TargetTrimEnabled,
                BassToSub = settings.BassToSub,
                SubLane = settings.SubLaneDb,
                CenterLift = settings.CenterLiftDb,
                Bypassed = settings.Bypassed,
                Preset = settings.ActivePresetId,
                AdvancedMode = settings.AdvancedMode
            };

            for (int i = 0; i < Bands.Count; i++)
            {
                mapped.Bands[i] = settings.Gains[i];
                mapped.Widths[i] = settings.Qs[i];
                mapped.Frequencies[i] = settings.Frequencies[i];
                mapped.Types[i] = settings.Kinds[i].ToString();
                mapped.Enabled[i] = settings.Enabled[i];
            }

            return mapped;
        }

        public static void Apply(ListeningSettings mapped, EqSettings settings)
        {
            for (int i = 0; i < Bands.Count; i++)
            {
                if (mapped.Bands != null && i < mapped.Bands.Length)
                {
                    settings.Gains[i] = Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, mapped.Bands[i]));
                }

                if (mapped.Widths != null && i < mapped.Widths.Length && mapped.Widths[i] > 0.05)
                {
                    settings.Qs[i] = Math.Max(0.2, Math.Min(8.0, mapped.Widths[i]));
                }

                if (mapped.Frequencies != null && i < mapped.Frequencies.Length && mapped.Frequencies[i] > 0)
                {
                    settings.Frequencies[i] = Math.Max(20, Math.Min(20000, mapped.Frequencies[i]));
                }

                if (mapped.Types != null && i < mapped.Types.Length &&
                    Enum.TryParse(mapped.Types[i], true, out BandKind kind))
                {
                    settings.Kinds[i] = kind;
                }

                if (mapped.Enabled != null && i < mapped.Enabled.Length)
                {
                    settings.Enabled[i] = mapped.Enabled[i];
                }
            }

            settings.PreampDb = Clamp(mapped.Preamp, -12, 11);
            settings.CeilingDb = Clamp(mapped.Ceiling, -6, 0);
            settings.CrossfeedPercent = Clamp(mapped.Crossfeed, 0, 35);
            settings.StageWidthPercent = Clamp(mapped.StageWidth, 70, 130);
            settings.SubLaneDb = Clamp(mapped.SubLane, -6, 6);
            settings.CenterLiftDb = Clamp(mapped.CenterLift, -3, 6);

            settings.Route = ParseEnum(mapped.Route, Route.Surround51);
            settings.Target = ParseEnum(mapped.HeadphoneTarget, HeadphoneTarget.NeutralReference);
            settings.Handoff = ParseEnum(mapped.Handoff, SoftwareHandoff.DirectStereo);

            settings.TargetTrimEnabled = mapped.TargetTrim;
            settings.BassToSub = mapped.BassToSub;
            settings.Bypassed = mapped.Bypassed;
            settings.ActivePresetId = string.IsNullOrWhiteSpace(mapped.Preset) ? "reference" : mapped.Preset;
            settings.AdvancedMode = mapped.AdvancedMode;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (double.IsNaN(value)) return min;
            return value < min ? min : value > max ? max : value;
        }

        private static T ParseEnum<T>(string? value, T fallback) where T : struct
        {
            return Enum.TryParse(value, true, out T parsed) ? parsed : fallback;
        }
    }

    public static class MeasurementProfileIO
    {
        private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        /// <summary>
        /// Validates an imported measurement with the same rules the web console used.
        /// Throws <see cref="InvalidDataException"/> with a listener-readable reason.
        /// </summary>
        public static MeasurementProfile Parse(string json)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("That file is not valid JSON (" + error.Message + ").");
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("The profile must be a JSON object.");
                }

                string schema = ReadString(root, "schema");
                if (!string.Equals(schema, "getEQd-profile/v1", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Schema must be getEQd-profile/v1.");
                }

                string model = ReadString(root, "model").Trim();
                if (model.Length == 0) throw new InvalidDataException("Add a model name.");

                if (!root.TryGetProperty("frequenciesHz", out JsonElement frequencies) || frequencies.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("frequenciesHz must be an array.");
                }

                if (!root.TryGetProperty("gainDb", out JsonElement gains) || gains.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("gainDb must be an array.");
                }

                if (frequencies.GetArrayLength() != gains.GetArrayLength() || frequencies.GetArrayLength() < 2)
                {
                    throw new InvalidDataException("Frequencies and gainDb must be matching arrays with at least two points.");
                }

                int count = frequencies.GetArrayLength();
                double[] frequencyValues = new double[count];
                double[] gainValues = new double[count];

                int index = 0;
                foreach (JsonElement element in frequencies.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value) || !IsFinite(value) || value <= 0)
                    {
                        throw new InvalidDataException("Frequency and gain values must be finite numbers, and frequencies must be above zero.");
                    }
                    frequencyValues[index++] = value;
                }

                index = 0;
                foreach (JsonElement element in gains.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value) || !IsFinite(value))
                    {
                        throw new InvalidDataException("Frequency and gain values must be finite numbers.");
                    }
                    gainValues[index++] = value;
                }

                string source = ReadString(root, "source").Trim();
                string rig = ReadString(root, "rig").Trim();
                string target = ReadString(root, "target").Trim();
                string responseType = ReadString(root, "responseType").Trim().ToLowerInvariant();
                if (responseType.Length == 0) responseType = "correction";
                if (responseType != "raw" && responseType != "correction")
                {
                    throw new InvalidDataException("responseType must be raw or correction.");
                }

                double[] targetValues = Array.Empty<double>();
                if (root.TryGetProperty("targetDb", out JsonElement targetGains))
                {
                    if (targetGains.ValueKind != JsonValueKind.Array || targetGains.GetArrayLength() != count)
                    {
                        throw new InvalidDataException("targetDb must be an array matching frequenciesHz.");
                    }

                    targetValues = new double[count];
                    index = 0;
                    foreach (JsonElement element in targetGains.EnumerateArray())
                    {
                        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out double value) || !IsFinite(value))
                        {
                            throw new InvalidDataException("targetDb values must be finite numbers.");
                        }
                        targetValues[index++] = value;
                    }
                }

                string notes = ReadString(root, "notes").Trim();

                return new MeasurementProfile
                {
                    Schema = "getEQd-profile/v1",
                    Model = model,
                    Source = source.Length > 0 ? source : "Local import",
                    MeasuredAt = ReadString(root, "measuredAt").Trim(),
                    Rig = rig,
                    Target = target,
                    ResponseType = responseType,
                    Notes = notes,
                    FrequenciesHz = frequencyValues,
                    GainDb = gainValues,
                    TargetDb = targetValues
                };
            }
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static string ReadString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement element)) return string.Empty;
            return element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;
        }

        /// <summary>
        /// Samples the measurement onto the default band centres, using the same logarithmic
        /// interpolation as the web console, then snaps to the 0.5 dB fader step.
        /// </summary>
        public static double[] ToBandGains(MeasurementProfile profile)
        {
            double[] result = new double[Bands.Count];
            for (int band = 0; band < Bands.Count; band++)
            {
                double gain = band < Bands.QuickCount
                    ? CorrectionDbAt(profile, Bands.AnalysisFrequencies[band])
                    : 0;
                double snapped = Math.Round(gain * 2, MidpointRounding.AwayFromZero) / 2;
                result[band] = Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, snapped));
            }

            return result;
        }

        /// <summary>Returns the imported measurement at a frequency in dB.</summary>
        public static double RawDbAt(MeasurementProfile profile, double frequency) =>
            Interpolate(profile.FrequenciesHz, profile.GainDb, profile.PointCount, frequency);

        /// <summary>Returns the imported target at a frequency, or a flat target when omitted.</summary>
        public static double TargetDbAt(MeasurementProfile profile, double frequency)
        {
            if (profile.TargetDb == null || profile.TargetDb.Length == 0) return 0;
            int count = Math.Min(profile.FrequenciesHz.Length, profile.TargetDb.Length);
            return Interpolate(profile.FrequenciesHz, profile.TargetDb, count, frequency);
        }

        /// <summary>
        /// Returns the EQ correction represented by the profile. New raw profiles use
        /// target minus measurement; legacy correction profiles keep their gainDb values.
        /// </summary>
        public static double CorrectionDbAt(MeasurementProfile profile, double frequency)
        {
            double rawOrCorrection = RawDbAt(profile, frequency);
            return profile.IsRaw ? TargetDbAt(profile, frequency) - rawOrCorrection : rawOrCorrection;
        }

        public static double[] ToCorrectionCurve(MeasurementProfile profile)
        {
            double[] result = new double[profile.PointCount];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = profile.IsRaw ? profile.TargetDbAtIndex(i) - profile.GainDb[i] : profile.GainDb[i];
            }
            return result;
        }

        private static double Interpolate(double[] frequencies, double[] gains, int count, double frequency)
        {
            List<(double Frequency, double Gain)> points = new List<(double, double)>();
            for (int i = 0; i < count; i++) points.Add((frequencies[i], gains[i]));
            points.Sort((a, b) => a.Frequency.CompareTo(b.Frequency));

            if (points.Count == 0) return 0;
            if (frequency <= points[0].Frequency) return points[0].Gain;
            if (frequency >= points[points.Count - 1].Frequency) return points[points.Count - 1].Gain;

            for (int i = 1; i < points.Count; i++)
            {
                if (points[i].Frequency >= frequency)
                {
                    (double lowFrequency, double lowGain) = points[i - 1];
                    (double highFrequency, double highGain) = points[i];
                    if (highFrequency <= lowFrequency) return lowGain;

                    double ratio = (Math.Log(frequency) - Math.Log(lowFrequency))
                                 / (Math.Log(highFrequency) - Math.Log(lowFrequency));
                    return lowGain + (highGain - lowGain) * ratio;
                }
            }

            return points[points.Count - 1].Gain;
        }

        public static string TemplateJson()
        {
            MeasurementProfile template = new MeasurementProfile
            {
                Model = "Your headphone or speaker model",
                Source = "Measurement source or rig",
                MeasuredAt = "2026-01-01",
                Rig = "Measurement rig and fixture",
                Target = "Target curve name or flat",
                ResponseType = "raw",
                Notes = "Add how this measurement was made.",
                FrequenciesHz = new double[] { 20, 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 12000, 16000, 20000 },
                GainDb = new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                TargetDb = new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
            };

            return JsonSerializer.Serialize(template, WriteOptions);
        }

        public static void Write(MeasurementProfile profile, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile, WriteOptions));
        }

        public static string AuditJson(MeasurementProfile profile)
        {
            double[] correction = ToCorrectionCurve(profile);
            var audit = new
            {
                schema = "getEQd-calibration-audit/v1",
                generatedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                measurement = new
                {
                    profile.Schema,
                    profile.Model,
                    profile.Source,
                    measuredAt = profile.MeasuredAt,
                    profile.Rig,
                    profile.Target,
                    responseType = profile.ResponseType,
                    profile.Notes,
                    frequenciesHz = profile.FrequenciesHz,
                    rawDb = profile.GainDb,
                    targetDb = profile.TargetDb == null || profile.TargetDb.Length == 0
                        ? new double[profile.PointCount]
                        : profile.TargetDb,
                    correctionDb = correction
                },
                quickBandFrequenciesHz = Bands.AnalysisFrequencies,
                quickBandCorrectionDb = ToBandGains(profile)
            };

            return JsonSerializer.Serialize(audit, WriteOptions);
        }

        public static void WriteAudit(MeasurementProfile profile, string path)
        {
            File.WriteAllText(path, AuditJson(profile));
        }

        private static double TargetDbAtIndex(this MeasurementProfile profile, int index)
        {
            return profile.TargetDb != null && index < profile.TargetDb.Length ? profile.TargetDb[index] : 0;
        }
    }

    /// <summary>File-backed storage under %LOCALAPPDATA%\getEQd.</summary>
    public static class ProfileStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static string Folder
        {
            get
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "getEQd");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public static string MeasurementsPath => Path.Combine(Folder, "measurements.json");
        public static string ListeningPath => Path.Combine(Folder, "listening-profiles.json");

        public static List<MeasurementProfile> LoadMeasurements() => LoadList<MeasurementProfile>(MeasurementsPath);
        public static List<ListeningProfile> LoadListening() => LoadList<ListeningProfile>(ListeningPath);

        public static void SaveMeasurements(IEnumerable<MeasurementProfile> profiles) =>
            SaveList(MeasurementsPath, profiles);

        public static void SaveListening(IEnumerable<ListeningProfile> profiles) =>
            SaveList(ListeningPath, profiles);

        private static List<T> LoadList<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<T>();
                string json = File.ReadAllText(path);
                List<T>? loaded = JsonSerializer.Deserialize<List<T>>(json, Options);
                return loaded ?? new List<T>();
            }
            catch
            {
                // A corrupt store should never stop the console from opening.
                return new List<T>();
            }
        }

        private static void SaveList<T>(string path, IEnumerable<T> items)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new List<T>(items), Options));
        }
    }
}
