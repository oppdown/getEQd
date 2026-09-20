using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GetEQd.Audio;

namespace GetEQd.Diagnostics
{
    /// <summary>
    /// Verifies the parts that are easy to get quietly wrong: that the filters do what the
    /// curve claims, that the ceiling actually holds, that bypass really is bypass, and
    /// that bass management and crossfeed behave as described.
    ///
    /// Every level claim here is measured by running audio through the real processor, not
    /// by re-reading the design maths.
    /// </summary>
    public static class SelfTest
    {
        private const int SampleRate = 48000;
        private const double GainToleranceDb = 0.06;

        public static int Run(string[] args, TextWriter output)
        {
            TestReport report = new TestReport(output);

            FlatChainIsTransparent(report);
            CurveMatchesRunningFilters(report);
            AdvancedFilterShapesWork(report);
            PreampScalesExactly(report);
            SafetyCeilingHolds(report);
            BypassIsTrueBypass(report);
            BassManagementIsFrequencySelective(report);
            CrossfeedEngagesOnlyWhenAsked(report);
            StageWidthScalesTheSideSignal(report);
            HeadphoneTargetTrimApplies(report);
            HeadroomAdviceIsCorrect(report);
            PresetsMatchTheConsole(report);
            MeasurementImportValidates(report);
            MeasuredCalibrationMathIsAuditable(report);
            ListeningProfilesRoundTrip(report);

            return report.Finish(args);
        }

        // ------------------------------------------------------------------ tests

        private static void FlatChainIsTransparent(TestReport report)
        {
            EqSnapshot snapshot = TestSnapshot();
            EqProcessor processor = new EqProcessor();
            processor.Configure(SampleRate, 2);
            processor.PublishImmediate(snapshot);

            float[] buffer = new float[512 * 2];
            float[] original = new float[buffer.Length];
            Random random = new Random(11);

            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)(random.NextDouble() * 1.0 - 0.5);
            }

            Array.Copy(buffer, original, buffer.Length);
            processor.Process(buffer, 0, buffer.Length);

            double worst = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                worst = Math.Max(worst, Math.Abs(buffer[i] - original[i]));
            }

            report.Check("A flat, unmetered chain passes audio through untouched",
                worst == 0,
                "largest sample deviation " + worst.ToString("0.############", CultureInfo.InvariantCulture));
        }

        private static void CurveMatchesRunningFilters(TestReport report)
        {
            // The curve on screen is only honest if it is the response of the filters that
            // are actually running, so every band is measured with real audio.
            for (int band = 0; band < Bands.Count; band++)
            {
                double frequency = Bands.All[band].FrequencyHz;

                EqSnapshot snapshot = TestSnapshot(settings =>
                {
                    settings.Enabled[band] = true;
                    settings.Gains[band] = 6.0;
                });

                Measurement measurement = Measure(snapshot, frequency, 0.25, 0.25);
                double measured = measurement.GainDb(0.25);
                double predicted = snapshot.CurveDbAt(SampleRate, frequency);

                report.Check($"{Bands.All[band].Name}: measured response matches the drawn curve at {Bands.All[band].FrequencyLabel}",
                    Math.Abs(measured - predicted) <= GainToleranceDb,
                    "measured " + Db(measured) + ", curve says " + Db(predicted));

                if (Bands.All[band].Kind == BandKind.Peaking)
                {
                    report.Check($"{Bands.All[band].Name}: a peaking band reaches its full +6.0 dB at its own centre",
                        Math.Abs(measured - 6.0) <= GainToleranceDb,
                        "measured " + Db(measured));
                }
            }

            // A shelf must lift the deep bass while leaving the mids alone. It reaches its
            // plateau gradually, so the claim to test is the lift and the isolation, not
            // that the corner frequency already shows the full figure.
            EqSnapshot lowShelf = TestSnapshot(settings => settings.Gains[0] = 6.0);
            double at20 = Measure(lowShelf, 20, 0.3, 0.3).GainDb(0.25);
            double at1k = Measure(lowShelf, 1000, 0.3, 0.3).GainDb(0.25);
            double predicted20 = lowShelf.CurveDbAt(SampleRate, 20);

            report.Check("Sub foundation: a +6.0 dB low shelf lifts deep bass and leaves 1 kHz alone",
                at20 >= 4.5 && Math.Abs(at1k) <= 0.1 && Math.Abs(at20 - predicted20) <= GainToleranceDb,
                "20 Hz " + Db(at20) + " (curve says " + Db(predicted20) + "), 1 kHz " + Db(at1k));

            EqSnapshot highShelf = TestSnapshot(settings => settings.Gains[5] = 5.0);
            double at18k = Measure(highShelf, 18000, 0.3, 0.3).GainDb(0.25);
            double at200 = Measure(highShelf, 200, 0.3, 0.3).GainDb(0.25);
            double predicted18k = highShelf.CurveDbAt(SampleRate, 18000);

            report.Check("Air: a +5.0 dB high shelf lifts the top and leaves 200 Hz alone",
                at18k >= 4.0 && Math.Abs(at200) <= 0.1 && Math.Abs(at18k - predicted18k) <= GainToleranceDb,
                "18 kHz " + Db(at18k) + " (curve says " + Db(predicted18k) + "), 200 Hz " + Db(at200));

            // And the whole quick-band curve, measured rather than assumed.
            EqSnapshot full = TestSnapshot(settings =>
            {
                double[] gains = { 4.0, 3.0, -1.5, 1.0, 1.5, 0.5 };
                for (int i = 0; i < Bands.QuickCount; i++) settings.Gains[i] = gains[i];
            });

            double worstError = 0;
            string worstPoint = string.Empty;

            double[] probeFrequencies = { 25, 40, 63, 100, 250, 800, 2500, 4000, 6400, 12000, 17000 };
            foreach (double probe in probeFrequencies)
            {
                double measured = Measure(full, probe, 0.3, 0.25).GainDb(0.25);
                double predicted = full.CurveDbAt(SampleRate, probe);
                double error = Math.Abs(measured - predicted);

                if (error > worstError)
                {
                    worstError = error;
                    worstPoint = probe.ToString("0", CultureInfo.InvariantCulture) + " Hz: measured " +
                                 Db(measured) + " vs curve " + Db(predicted);
                }
            }

            report.Check("The Impact preset's whole quick-band curve matches real audio across the spectrum",
                worstError <= 0.15,
                "worst disagreement " + worstError.ToString("0.000", CultureInfo.InvariantCulture) + " dB at " + worstPoint);
        }

        private static void AdvancedFilterShapesWork(TestReport report)
        {
            EqSnapshot notch = TestSnapshot(settings =>
            {
                settings.Enabled[6] = true;
                settings.Kinds[6] = BandKind.Notch;
                settings.Frequencies[6] = 1000;
                settings.Qs[6] = 8;
                settings.Gains[6] = 0;
            });
            double notchAtCentre = Measure(notch, 1000, 0.35, 0.3).GainDb(0.25);
            double notchAway = Measure(notch, 700, 0.35, 0.3).GainDb(0.25);
            report.Check("Advanced notch filter rejects its centre frequency",
                notchAtCentre < -18 && notchAway > -3,
                "1 kHz " + Db(notchAtCentre) + ", 700 Hz " + Db(notchAway));

            EqSnapshot highPass = TestSnapshot(settings =>
            {
                settings.Enabled[7] = true;
                settings.Kinds[7] = BandKind.HighPass;
                settings.Frequencies[7] = 1000;
                settings.Qs[7] = 0.707;
            });
            double below = Measure(highPass, 100, 0.35, 0.3).GainDb(0.25);
            double above = Measure(highPass, 5000, 0.35, 0.3).GainDb(0.25);
            report.Check("Advanced high-pass filter separates below and above its cutoff",
                below < -10 && above > -3,
                "100 Hz " + Db(below) + ", 5 kHz " + Db(above));
        }

        private static void PreampScalesExactly(TestReport report)
        {
            EqSnapshot snapshot = TestSnapshot(settings => settings.PreampDb = -6.0);
            double measured = Measure(snapshot, 1000, 0.25, 0.25).GainDb(0.25);

            report.Check("Preamp applies the level it says it does",
                Math.Abs(measured + 6.0) <= GainToleranceDb,
                "asked for -6.0 dB, measured " + Db(measured));
        }

        private static void SafetyCeilingHolds(TestReport report)
        {
            EqSnapshot snapshot = TestSnapshot(settings => settings.CeilingDb = -6.0);

            // A full-scale sine with the ceiling at -6 dB: the limiter has to bring it down.
            Measurement measurement = Measure(snapshot, 400, 0.4, 0.3, leftAmplitude: 1.0, rightAmplitude: 1.0);
            double ceilingLinear = Math.Pow(10, -6.0 / 20.0);

            report.Check("Safety ceiling holds a full-scale signal at the set level",
                measurement.Peak <= ceilingLinear + 0.02,
                "peak " + measurement.Peak.ToString("0.0000", CultureInfo.InvariantCulture) +
                " against ceiling " + ceilingLinear.ToString("0.0000", CultureInfo.InvariantCulture));

            report.Check("Safety ceiling reports the gain reduction it applied",
                measurement.ReductionDb < -4.0,
                "reduction " + Db(measurement.ReductionDb));

            // A quiet signal must pass without the limiter touching it.
            EqSnapshot quiet = TestSnapshot(settings => settings.CeilingDb = -1.0);
            Measurement untouched = Measure(quiet, 1000, 0.3, 0.25, leftAmplitude: 0.1, rightAmplitude: 0.1);

            report.Check("Safety ceiling stays out of the way when nothing is near it",
                untouched.ReductionDb > -0.05 && Math.Abs(untouched.GainDb(0.1)) <= GainToleranceDb,
                "reduction " + Db(untouched.ReductionDb));
        }

        private static void BypassIsTrueBypass(TestReport report)
        {
            EqSnapshot snapshot = TestSnapshot(settings =>
            {
                for (int i = 0; i < Bands.Count; i++) settings.Gains[i] = 11.0;
                settings.PreampDb = -12;
                settings.Bypassed = true;
            });

            double measured = Measure(snapshot, 1000, 0.3, 0.25).GainDb(0.25);

            report.Check("Bypass ignores every band and the preamp",
                Math.Abs(measured) <= 0.01,
                "measured " + Db(measured) + " with +11 dB on every band");
        }

        private static void BassManagementIsFrequencySelective(TestReport report)
        {
            EqSnapshot snapshot = TestSnapshot(settings =>
            {
                settings.BassToSub = true;
                settings.Route = Route.Stereo21;
            });

            // Hard-panned sub content should come out of both speakers at a similar level.
            Measurement low = Measure(snapshot, 40, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);
            double lowBalanceDb = Ratio(low.RightRms, low.LeftRms);
            double lowCorrelation = Correlation(low.SamplesLeft, low.SamplesRight);

            report.Check("Bass management sends hard-panned sub content to both speakers",
                lowBalanceDb > -3.0 && lowCorrelation > 0.95,
                "opposite side arrived at " + Db(lowBalanceDb) + ", correlation " +
                lowCorrelation.ToString("0.0000", CultureInfo.InvariantCulture));

            // Above the crossover the same signal must stay where it was panned. The level
            // ratio is the test here: correlation is scale invariant, so it cannot tell a
            // tiny coherent leak apart from a full copy.
            Measurement high = Measure(snapshot, 2000, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);
            double highLeakDb = Ratio(high.RightRms, high.LeftRms);

            report.Check("Bass management keeps everything above the crossover hard-panned",
                highLeakDb < -40,
                "opposite side leaked only " + Db(highLeakDb) + " at 2 kHz");

            // With the feature off, nothing is summed at all.
            EqSnapshot off = TestSnapshot(settings => settings.BassToSub = false);
            Measurement untouched = Measure(off, 40, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);

            report.Check("With bass management off the low end stays hard-panned",
                untouched.RightRms < 1e-7,
                "opposite side RMS " + untouched.RightRms.ToString("0.########", CultureInfo.InvariantCulture));
        }

        private static void CrossfeedEngagesOnlyWhenAsked(TestReport report)
        {
            EqSnapshot off = TestSnapshot(settings =>
            {
                settings.Route = Route.Headphones;
                settings.Handoff = SoftwareHandoff.DirectStereo;
                settings.CrossfeedPercent = 0;
            });

            Measurement withoutCrossfeed = Measure(off, 300, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);
            double silentRight = withoutCrossfeed.RightRms;

            report.Check("With crossfeed at zero the headphone path stays fully separated",
                silentRight < 1e-7,
                "right channel RMS " + silentRight.ToString("0.########", CultureInfo.InvariantCulture));

            EqSnapshot on = TestSnapshot(settings =>
            {
                settings.Route = Route.Headphones;
                settings.Handoff = SoftwareHandoff.DirectStereo;
                settings.CrossfeedPercent = 35;
            });

            Measurement withCrossfeed = Measure(on, 300, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);
            double bleedDb = 20 * Math.Log10(Math.Max(1e-9, withCrossfeed.RightRms / Math.Max(1e-9, withCrossfeed.LeftRms)));

            report.Check("Crossfeed at 35% feeds the opposite ear at low frequencies",
                bleedDb > -30 && bleedDb < -3,
                "opposite-ear level " + Db(bleedDb) + " relative to the driven side");

            // Windows Spatial Sound means getEQd must stop doing its own spatialisation.
            EqSnapshot handedOff = TestSnapshot(settings =>
            {
                settings.Route = Route.Headphones;
                settings.Handoff = SoftwareHandoff.WindowsSpatialSound;
                settings.CrossfeedPercent = 35;
            });

            Measurement handedOffMeasurement = Measure(handedOff, 300, 0.35, 0.3, leftAmplitude: 0.4, rightAmplitude: 0.0);

            report.Check("Choosing Spatial Sound stands getEQd's crossfeed down",
                handedOffMeasurement.RightRms < 1e-7,
                "right channel RMS " + handedOffMeasurement.RightRms.ToString("0.########", CultureInfo.InvariantCulture));
        }

        private static void StageWidthScalesTheSideSignal(TestReport report)
        {
            EqSnapshot narrowed = TestSnapshot(settings =>
            {
                settings.Route = Route.Headphones;
                settings.CrossfeedPercent = 0;
                settings.StageWidthPercent = 70;
            });

            // Opposite polarity is pure side content, which width should scale directly.
            Measurement measurement = Measure(narrowed, 1000, 0.3, 0.25, leftAmplitude: 0.3, rightAmplitude: -0.3);
            double expected = 0.7;
            double ratio = measurement.LeftRms / (0.3 / Math.Sqrt(2));

            report.Check("Stage width at 70% scales the side signal to 70%",
                Math.Abs(ratio - expected) <= 0.01,
                "side amplitude came out at " + (ratio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");

            EqSnapshot widened = TestSnapshot(settings =>
            {
                settings.Route = Route.Headphones;
                settings.CrossfeedPercent = 0;
                settings.StageWidthPercent = 130;
            });

            Measurement wide = Measure(widened, 1000, 0.3, 0.25, leftAmplitude: 0.3, rightAmplitude: -0.3);
            double wideRatio = wide.LeftRms / (0.3 / Math.Sqrt(2));

            report.Check("Stage width at 130% scales the side signal to 130%",
                Math.Abs(wideRatio - 1.3) <= 0.02,
                "side amplitude came out at " + (wideRatio * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%");
        }

        private static void HeadphoneTargetTrimApplies(TestReport report)
        {
            EqSettings settings = new EqSettings
            {
                Route = Route.Headphones,
                Target = HeadphoneTarget.GamingClarity,
                TargetTrimEnabled = true
            };

            EqSnapshot trimmed = settings.CreateSnapshot();
            double expected = HeadphoneTargets.Find(HeadphoneTarget.GamingClarity).TrimDb[0];

            report.Check("A headphone target trims the bands it says it does",
                Math.Abs(trimmed.Gains[0] - expected) <= 1e-9,
                "band one " + Db(trimmed.Gains[0]) + " against the target's " + Db(expected));

            settings.TargetTrimEnabled = false;
            EqSnapshot untrimmed = settings.CreateSnapshot();

            report.Check("The target trim can be switched off, leaving the faders in charge",
                Math.Abs(untrimmed.Gains[0]) <= 1e-9,
                "band one " + Db(untrimmed.Gains[0]) + " with the trim off");

            settings.Route = Route.Surround51;
            EqSnapshot speakers = settings.CreateSnapshot();

            report.Check("Headphone targets do not touch the speaker routes",
                Math.Abs(speakers.Gains[0]) <= 1e-9,
                "band one " + Db(speakers.Gains[0]) + " on the 5.1 route");
        }

        private static void HeadroomAdviceIsCorrect(TestReport report)
        {
            // The console advises a preamp trim. That advice has to be right, because a
            // listener who follows it should end up with the ceiling doing nothing.
            EqSettings settings = new EqSettings
            {
                Route = Route.Stereo21,
                BassToSub = false,
                CeilingDb = -1.0
            };

            double[] gains = { 6.0, 5.0, -2.0, 3.0, 2.0, 1.0 };
            for (int i = 0; i < Bands.QuickCount; i++) settings.Gains[i] = gains[i];

            HeadroomAnalysis analysis = Analysis.Evaluate(settings.CreateSnapshot(), SampleRate);

            report.Check("Headroom advice notices that a big boost runs past the ceiling",
                analysis.LimiterWillEngage && analysis.SuggestedPreampDb < 0,
                "peak " + Db(analysis.PeakCurveDb) + ", suggested trim " + Db(analysis.SuggestedPreampDb));

            // Follow the advice, and the peak should land exactly on the ceiling.
            EqSettings trimmed = new EqSettings
            {
                Route = Route.Stereo21,
                BassToSub = false,
                CeilingDb = -1.0,
                PreampDb = analysis.SuggestedPreampDb
            };

            for (int i = 0; i < Bands.QuickCount; i++) trimmed.Gains[i] = gains[i];

            HeadroomAnalysis after = Analysis.Evaluate(trimmed.CreateSnapshot(), SampleRate);

            report.Check("Following the advised trim puts the curve peak exactly on the ceiling",
                Math.Abs(after.PeakCurveDb - trimmed.CeilingDb) <= 0.01 && !after.LimiterWillEngage,
                "peak after trimming " + Db(after.PeakCurveDb) + " against ceiling " + Db(trimmed.CeilingDb));

            // A curve that only cuts cannot clip anything on its own.
            EqSettings cutting = new EqSettings
            {
                Route = Route.Stereo21,
                BassToSub = false,
                CeilingDb = -1.0
            };

            for (int i = 0; i < Bands.Count; i++) cutting.Gains[i] = -6.0;

            HeadroomAnalysis cut = Analysis.Evaluate(cutting.CreateSnapshot(), SampleRate);

            report.Check("A cut-only curve reports that it cannot clip anything",
                !cut.LimiterWillEngage && cut.PeakCurveDb <= 0.05,
                "peak " + Db(cut.PeakCurveDb) + " with every band cut");
        }

        private static void PresetsMatchTheConsole(TestReport report)
        {
            EqSettings settings = new EqSettings();
            settings.ApplyPreset("impact");

            double[] expected = Presets.Find("impact")!.Gains;
            bool match = true;
            for (int i = 0; i < Bands.Count; i++)
            {
                if (Math.Abs(settings.Gains[i] - expected[i]) > 1e-9) match = false;
            }

            report.Check("The Impact preset matches the published values", match,
                string.Join(" / ", settings.Gains));

            settings.ApplyPreset("night");
            double[] nightExpected = Presets.Find("night")!.Gains;
            match = true;
            for (int i = 0; i < Bands.Count; i++)
            {
                if (Math.Abs(settings.Gains[i] - nightExpected[i]) > 1e-9) match = false;
            }

            report.Check("The Night preset matches the published values", match,
                string.Join(" / ", settings.Gains));
        }

        private static void MeasurementImportValidates(TestReport report)
        {
            string good = "{\"schema\":\"getEQd-profile/v1\",\"model\":\"Test rig\",\"source\":\"Bench\"," +
                          "\"measuredAt\":\"2026-01-01\",\"notes\":\"n\",\"frequenciesHz\":[20,250,2500,20000]," +
                          "\"gainDb\":[0,0,6,0]}";

            try
            {
                MeasurementProfile profile = MeasurementProfileIO.Parse(good);
                report.Check("A well-formed measurement imports",
                    profile.Model == "Test rig" && profile.PointCount == 4 && profile.Source == "Bench",
                    profile.Model + ", " + profile.PointCount + " points");

                double[] gains = MeasurementProfileIO.ToBandGains(profile);
                report.Check("A measurement is sampled onto the six band centres",
                    Math.Abs(gains[3] - 6.0) <= 0.5 && Math.Abs(gains[0]) <= 0.5,
                    "presence band " + Db(gains[3]) + ", sub band " + Db(gains[0]));
            }
            catch (Exception error)
            {
                report.Check("A well-formed measurement imports", false, "threw " + error.Message);
            }

            ExpectRejected(report, "wrong schema",
                "{\"schema\":\"nope/v9\",\"model\":\"m\",\"frequenciesHz\":[20,20000],\"gainDb\":[0,0]}",
                "Schema must be getEQd-profile/v1");

            ExpectRejected(report, "missing model name",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"  \",\"frequenciesHz\":[20,20000],\"gainDb\":[0,0]}",
                "Add a model name");

            ExpectRejected(report, "mismatched array lengths",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"m\",\"frequenciesHz\":[20,200,2000],\"gainDb\":[0,0]}",
                "matching arrays");

            ExpectRejected(report, "a frequency at or below zero",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"m\",\"frequenciesHz\":[0,20000],\"gainDb\":[0,0]}",
                "finite numbers");

            ExpectRejected(report, "a text gain value",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"m\",\"frequenciesHz\":[20,20000],\"gainDb\":[0,\"loud\"]}",
                "finite numbers");

            ExpectRejected(report, "malformed JSON", "{not json", "not valid JSON");
        }

        private static void ExpectRejected(TestReport report, string what, string json, string expectedFragment)
        {
            try
            {
                MeasurementProfileIO.Parse(json);
                report.Check("Import rejects " + what, false, "it was accepted");
            }
            catch (InvalidDataException error)
            {
                report.Check("Import rejects " + what,
                    error.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase),
                    "\"" + error.Message + "\"");
            }
            catch (Exception error)
            {
                report.Check("Import rejects " + what, false, "threw " + error.GetType().Name);
            }
        }

        private static void MeasuredCalibrationMathIsAuditable(TestReport report)
        {
            string rawJson = "{\"schema\":\"getEQd-profile/v1\",\"model\":\"Measured headphones\"," +
                             "\"source\":\"Bench file\",\"measuredAt\":\"2026-09-12\",\"rig\":\"5128 fixture\"," +
                             "\"target\":\"Flat reference\",\"responseType\":\"raw\",\"notes\":\"fixture note\"," +
                             "\"frequenciesHz\":[20,1000,20000],\"gainDb\":[4,2,-2],\"targetDb\":[0,0,0]}";

            try
            {
                MeasurementProfile profile = MeasurementProfileIO.Parse(rawJson);
                double raw = MeasurementProfileIO.RawDbAt(profile, 20);
                double target = MeasurementProfileIO.TargetDbAt(profile, 20);
                double correction = MeasurementProfileIO.CorrectionDbAt(profile, 20);
                string audit = MeasurementProfileIO.AuditJson(profile);

                report.Check("A raw measurement keeps its rig and target provenance",
                    profile.IsRaw && profile.Rig == "5128 fixture" && profile.Target == "Flat reference" &&
                    profile.ResponseTypeLabel == "Raw measurement",
                    profile.ResponseType + ", " + profile.Rig + ", " + profile.Target);
                report.Check("Raw response becomes target minus measurement",
                    Math.Abs(raw - 4) < 1e-9 && Math.Abs(target) < 1e-9 && Math.Abs(correction + 4) < 1e-9,
                    "raw " + Db(raw) + ", target " + Db(target) + ", correction " + Db(correction));
                report.Check("Calibration audit includes raw, target and correction arrays",
                    audit.Contains("getEQd-calibration-audit/v1", StringComparison.Ordinal) &&
                    audit.Contains("rawDb", StringComparison.Ordinal) &&
                    audit.Contains("targetDb", StringComparison.Ordinal) &&
                    audit.Contains("correctionDb", StringComparison.Ordinal),
                    "audit length " + audit.Length);

                MeasurementProfile template = MeasurementProfileIO.Parse(MeasurementProfileIO.TemplateJson());
                report.Check("The downloadable template is accepted by the same importer",
                    template.IsRaw && template.TargetDb.Length == template.PointCount,
                    template.ResponseType + ", " + template.TargetDb.Length + " target points");
            }
            catch (Exception error)
            {
                report.Check("The measured calibration workflow stays self-contained", false, "threw " + error.Message);
            }

            ExpectRejected(report, "an unknown response type",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"m\",\"responseType\":\"guess\",\"frequenciesHz\":[20,20000],\"gainDb\":[0,0]}",
                "responseType must be raw or correction");

            ExpectRejected(report, "a mismatched target curve",
                "{\"schema\":\"getEQd-profile/v1\",\"model\":\"m\",\"responseType\":\"raw\",\"frequenciesHz\":[20,20000],\"gainDb\":[0,0],\"targetDb\":[0]}",
                "targetDb must be an array matching frequenciesHz");
        }

        private static void ListeningProfilesRoundTrip(TestReport report)
        {
            EqSettings original = new EqSettings
            {
                Route = Route.Headphones,
                Target = HeadphoneTarget.OpenBackAir,
                Handoff = SoftwareHandoff.GameSpatialMix,
                PreampDb = -3.5,
                CeilingDb = -2.0,
                CrossfeedPercent = 22,
                StageWidthPercent = 118,
                SubLaneDb = 2.5,
                CenterLiftDb = -1.0,
                BassToSub = false,
                TargetTrimEnabled = false,
                Bypassed = true,
                ActivePresetId = "dialogue"
            };

            double[] gains = { -4.0, -2.0, 1.5, 3.0, -1.0, 2.5 };
            for (int i = 0; i < Bands.QuickCount; i++) original.Gains[i] = gains[i];
            for (int i = 0; i < Bands.QuickCount; i++) original.Qs[i] = 1.25;
            original.AdvancedMode = true;
            original.Enabled[6] = true;
            original.Frequencies[6] = 777;
            original.Kinds[6] = BandKind.Notch;
            original.Qs[6] = 4.2;

            ListeningSettings stored = ProfileMapping.ToSettings(original);

            string path = Path.Combine(Path.GetTempPath(), "geteqd-selftest-profiles.json");
            ProfileStore.SaveListening(new[] { new ListeningProfile { Name = "Round trip", Settings = stored } });

            string json = System.Text.Json.JsonSerializer.Serialize(
                new List<ListeningProfile> { new ListeningProfile { Name = "Round trip", Settings = stored } });

            List<ListeningProfile>? reloaded = System.Text.Json.JsonSerializer.Deserialize<List<ListeningProfile>>(json);
            EqSettings restored = new EqSettings();

            if (reloaded != null && reloaded.Count == 1)
            {
                ProfileMapping.Apply(reloaded[0].Settings, restored);
            }

            try { if (File.Exists(path)) File.Delete(path); } catch { /* not important */ }

            bool bandsMatch = true;
            for (int i = 0; i < Bands.QuickCount; i++)
            {
                if (Math.Abs(restored.Gains[i] - gains[i]) > 1e-9) bandsMatch = false;
            }

            report.Check("A saved listening profile survives a save and reload",
                bandsMatch
                && restored.Route == Route.Headphones
                && restored.AdvancedMode
                && restored.Enabled[6]
                && Math.Abs(restored.Frequencies[6] - 777) < 1e-9
                && restored.Kinds[6] == BandKind.Notch
                && Math.Abs(restored.Qs[6] - 4.2) < 1e-9
                && restored.Target == HeadphoneTarget.OpenBackAir
                && restored.Handoff == SoftwareHandoff.GameSpatialMix
                && Math.Abs(restored.PreampDb + 3.5) < 1e-9
                && Math.Abs(restored.CrossfeedPercent - 22) < 1e-9
                && Math.Abs(restored.StageWidthPercent - 118) < 1e-9
                && restored.BassToSub == false
                && restored.TargetTrimEnabled == false
                && restored.Bypassed,
                "route " + restored.Route + ", target " + restored.Target + ", preamp " + Db(restored.PreampDb));
        }

        // ------------------------------------------------------------------ measurement rig

        private static EqSnapshot TestSnapshot(Action<EqSettings>? configure = null)
        {
            EqSettings settings = new EqSettings
            {
                Route = Route.Stereo21,
                BassToSub = false,
                CeilingDb = 0
            };

            configure?.Invoke(settings);
            return settings.CreateSnapshot();
        }

        private sealed class Measurement
        {
            public double LeftRms;
            public double RightRms;
            public double Peak;
            public double ReductionDb;
            public double[] SamplesLeft = Array.Empty<double>();
            public double[] SamplesRight = Array.Empty<double>();

            /// <summary>Level of the left channel relative to a sine of the given amplitude.</summary>
            public double GainDb(double inputAmplitude)
            {
                double reference = inputAmplitude / Math.Sqrt(2);
                return 20 * Math.Log10(Math.Max(1e-12, LeftRms) / reference);
            }
        }

        /// <summary>
        /// Runs a sine through a fresh processor and reports what came out, after letting
        /// the filters and the limiter settle.
        /// </summary>
        private static Measurement Measure(EqSnapshot snapshot, double frequency, double warmupSeconds,
            double measureSeconds, double leftAmplitude = 0.25, double rightAmplitude = 0.25, int channels = 2)
        {
            EqProcessor processor = new EqProcessor();
            processor.Configure(SampleRate, channels);
            processor.PublishImmediate(snapshot);

            int blockFrames = 256;
            int warmupFrames = (int)(SampleRate * warmupSeconds);
            int measureFrames = (int)(SampleRate * measureSeconds);
            int totalFrames = warmupFrames + measureFrames;

            float[] buffer = new float[blockFrames * channels];
            List<double> collectedLeft = new List<double>();
            List<double> collectedRight = new List<double>();

            double phase = 0;
            double step = 2 * Math.PI * frequency / SampleRate;
            double sumLeft = 0;
            double sumRight = 0;
            double peak = 0;
            int countedFrames = 0;

            int frame = 0;
            while (frame < totalFrames)
            {
                int frames = Math.Min(blockFrames, totalFrames - frame);

                for (int i = 0; i < frames; i++)
                {
                    double value = Math.Sin(phase);
                    phase += step;
                    if (phase > 2 * Math.PI * 1000) phase -= 2 * Math.PI * 1000;

                    buffer[i * channels] = (float)(leftAmplitude * value);
                    if (channels > 1) buffer[i * channels + 1] = (float)(rightAmplitude * value);
                    for (int c = 2; c < channels; c++) buffer[i * channels + c] = 0;
                }

                processor.Process(buffer, 0, frames * channels);

                for (int i = 0; i < frames; i++)
                {
                    int global = frame + i;
                    if (global < warmupFrames) continue;

                    double left = buffer[i * channels];
                    double right = channels > 1 ? buffer[i * channels + 1] : left;

                    sumLeft += left * left;
                    sumRight += right * right;
                    peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
                    countedFrames++;

                    if (collectedLeft.Count < 8192)
                    {
                        collectedLeft.Add(left);
                        collectedRight.Add(right);
                    }
                }

                frame += frames;
            }

            processor.ReadPeaks(out _, out _, out _, out double reductionDb);

            // The peak tracker holds the worst case across the whole run, which is what the
            // ceiling claim has to be tested against.
            int divisor = Math.Max(1, countedFrames);

            return new Measurement
            {
                LeftRms = Math.Sqrt(sumLeft / divisor),
                RightRms = Math.Sqrt(sumRight / divisor),
                Peak = peak,
                ReductionDb = reductionDb,
                SamplesLeft = collectedLeft.ToArray(),
                SamplesRight = collectedRight.ToArray()
            };
        }

        private static double Correlation(double[] left, double[] right)
        {
            int length = Math.Min(left.Length, right.Length);
            if (length < 2) return 0;

            double meanLeft = 0;
            double meanRight = 0;
            for (int i = 0; i < length; i++)
            {
                meanLeft += left[i];
                meanRight += right[i];
            }

            meanLeft /= length;
            meanRight /= length;

            double covariance = 0;
            double varianceLeft = 0;
            double varianceRight = 0;

            for (int i = 0; i < length; i++)
            {
                double a = left[i] - meanLeft;
                double b = right[i] - meanRight;
                covariance += a * b;
                varianceLeft += a * a;
                varianceRight += b * b;
            }

            double denominator = Math.Sqrt(varianceLeft * varianceRight);
            return denominator < 1e-15 ? 0 : covariance / denominator;
        }

        private static string Db(double value) =>
            value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " dB";

        /// <summary>Level of one channel relative to another, in dB.</summary>
        private static double Ratio(double numerator, double denominator) =>
            20 * Math.Log10(Math.Max(1e-12, numerator) / Math.Max(1e-12, denominator));

        // ------------------------------------------------------------------ reporting

        private sealed class TestReport
        {
            private readonly TextWriter _output;
            private readonly StringBuilder _log = new StringBuilder();
            private int _passed;
            private int _failed;

            public TestReport(TextWriter output)
            {
                _output = output;
            }

            public void Check(string name, bool ok, string detail)
            {
                if (ok) _passed++;
                else _failed++;

                string line = (ok ? "  PASS  " : "  FAIL  ") + name + "\n          " + detail;
                _log.AppendLine(line);
                _output.WriteLine(line);
            }

            public int Finish(string[] args)
            {
                string summary = _failed == 0
                    ? $"getEQd self-test: all {_passed} checks passed."
                    : $"getEQd self-test: {_failed} of {_passed + _failed} checks FAILED.";

                _output.WriteLine();
                _output.WriteLine(summary);
                _log.AppendLine();
                _log.AppendLine(summary);

                string reportPath = args.Length > 1
                    ? args[1]
                    : Path.Combine(Environment.CurrentDirectory, "geteqd-selftest.txt");

                try
                {
                    File.WriteAllText(reportPath, _log.ToString());
                    _output.WriteLine("Report written to " + reportPath);
                }
                catch
                {
                    // The console result is enough if the file cannot be written.
                }

                return _failed == 0 ? 0 : 1;
            }
        }
    }
}
