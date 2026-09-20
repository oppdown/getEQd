using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GetEQd.Audio;

namespace GetEQd.Ui
{
    /// <summary>
    /// The live response analyzer. Draws the true analytic response of the running
    /// filters, the spectrum of what is actually leaving the console, and the band
    /// handles you can drag.
    ///
    /// Drag a handle vertically to set gain, and horizontally in Advanced mode to set
    /// frequency. Roll the wheel over it to set width, double-click to flatten, and
    /// right-click to restore the default width.
    /// </summary>
    public sealed class CurveView : FrameworkElement
    {
        // ---- plot geometry
        private const double MinimumHz = 20;
        private const double MaximumHz = 20000;
        private const double TopDb = 15;
        private const double BottomDb = -15;
        private const double LeftPad = 42;
        private const double RightPad = 12;
        private const double TopPad = 14;
        private const double BottomPad = 26;

        private const double HandleRadius = 6.5;

        // ---- palette
        private static readonly Brush Backdrop = Frozen(new LinearGradientBrush(
            Color.FromRgb(0x0B, 0x0D, 0x11), Color.FromRgb(0x07, 0x08, 0x0A), 90));

        private static readonly Brush SpectrumFill = Frozen(new LinearGradientBrush(
            Color.FromArgb(0x3C, 0x5A, 0x7A, 0x9E), Color.FromArgb(0x00, 0x5A, 0x7A, 0x9E), 90));

        private static readonly Brush CurveFill = Frozen(new LinearGradientBrush(
            Color.FromArgb(0x46, 0xFF, 0xB3, 0x47), Color.FromArgb(0x04, 0xFF, 0xB3, 0x47), 90));

        private static readonly Pen GridMinor = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x18, 0x1C, 0x23)), 1));
        private static readonly Pen GridMajor = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x25, 0x2A, 0x34)), 1));
        private static readonly Pen ZeroLine = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x42, 0x50)), 1));
        private static readonly Pen ElevenLine = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xB3, 0x47)), 1)
        {
            DashStyle = new DashStyle(new double[] { 3, 4 }, 0)
        });

        private static readonly Pen PreampLine = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0x8F, 0xB8, 0xFF)), 1)
        {
            DashStyle = new DashStyle(new double[] { 2, 3 }, 0)
        });

        private static readonly Pen SpectrumStroke = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x88, 0x6A, 0x8C, 0xB4)), 1));
        private static readonly Pen CurveStroke = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)), 2.2)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });

        private static readonly Pen NetStroke = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x6F, 0xA8, 0xFF)), 1.6)
        {
            DashStyle = new DashStyle(new double[] { 5, 3 }, 0),
            LineJoin = PenLineJoin.Round
        });

        private static readonly Brush HandleFill = Frozen(new SolidColorBrush(Color.FromRgb(0x11, 0x14, 0x19)));
        private static readonly Pen HandleStroke = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)), 1.8));
        private static readonly Pen HandleStrokeHover = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x77)), 2.4));
        private static readonly Brush HandleGlow = Frozen(new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xB3, 0x47)));
        private static readonly Brush AccentBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)));

        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6E, 0x76, 0x86)));
        private static readonly Brush LabelStrongBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xC8, 0xCF, 0xDA)));
        private static readonly Brush PanelBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xF0, 0x14, 0x18, 0x1E)));
        private static readonly Pen PanelStroke = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x3A, 0x46)), 1));

        private static readonly Typeface LabelTypeface =
            new Typeface(new FontFamily("Segoe UI, Tahoma"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private static readonly Typeface StrongTypeface =
            new Typeface(new FontFamily("Segoe UI, Tahoma"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        private static readonly Typeface NumericTypeface =
            new Typeface(new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // ---- state
        private readonly StreamGeometry _curveGeometry = new StreamGeometry();
        private readonly StreamGeometry _curveFillGeometry = new StreamGeometry();
        private readonly StreamGeometry _netGeometry = new StreamGeometry();
        private readonly StreamGeometry _spectrumGeometry = new StreamGeometry();

        private EqSettings? _settings;
        private float[] _spectrum = Array.Empty<float>();
        private int _spectrumSampleRate = 48000;
        private bool _showSpectrum;

        private int _hoverBand = -1;
        private int _dragBand = -1;
        private int _selectedBand = -1;
        private double _dragGain;
        private double _wheelAccumulator;

        private const int CurveResolution = 420;

        public CurveView()
        {
            Focusable = true;
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            Cursor = Cursors.Cross;
            ToolTip = "Drag a handle to set gain · Advanced mode also moves frequency · wheel sets width";
        }

        /// <summary>Raised whenever the curve itself changes a band value.</summary>
        public event EventHandler? SettingsEdited;

        /// <summary>Raised when the listener selects a band, so the rack can mirror it.</summary>
        public event EventHandler<int>? BandSelected;

        public int SampleRate { get; set; } = 48000;

        public int SelectedBand => _selectedBand;

        /// <summary>True while a spectrum is being drawn behind the curve.</summary>
        public bool HasSpectrum => _showSpectrum && _spectrum.Length > 0;

        /// <summary>Selects a band from outside the control, so the rack can drive the curve.</summary>
        public void SetSelectedBand(int band)
        {
            if (band < 0 || band >= Bands.Count) return;

            _selectedBand = band;
            BandSelected?.Invoke(this, band);
            InvalidateVisual();
        }

        public void Attach(EqSettings settings)
        {
            _settings = settings;
            InvalidateVisual();
        }

        public void SetSpectrum(float[] magnitudes, int sampleRate, bool active)
        {
            _spectrum = magnitudes;
            _spectrumSampleRate = sampleRate;
            _showSpectrum = active;
        }

        // ------------------------------------------------------------------ mapping

        private static readonly double LogMin = Math.Log10(MinimumHz);
        private static readonly double LogMax = Math.Log10(MaximumHz);

        private double PlotWidth => Math.Max(1, ActualWidth - LeftPad - RightPad);
        private double PlotHeight => Math.Max(1, ActualHeight - TopPad - BottomPad);

        private double XForFrequency(double frequency)
        {
            double normalised = (Math.Log10(Math.Max(MinimumHz, frequency)) - LogMin) / (LogMax - LogMin);
            return LeftPad + normalised * PlotWidth;
        }

        private double FrequencyForX(double x)
        {
            double normalised = (x - LeftPad) / PlotWidth;
            return Math.Pow(10, LogMin + normalised * (LogMax - LogMin));
        }

        private double YForDecibels(double db)
        {
            double normalised = (TopDb - db) / (TopDb - BottomDb);
            return TopPad + normalised * PlotHeight;
        }

        private double DecibelsForY(double y)
        {
            double normalised = (y - TopPad) / PlotHeight;
            return TopDb - normalised * (TopDb - BottomDb);
        }

        // ------------------------------------------------------------------ interaction

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();

            Point position = e.GetPosition(this);
            int band = HitTestBand(position);

            if (e.ClickCount == 2)
            {
                if (band >= 0 && _settings != null)
                {
                    _settings.SetEffectiveGain(band, 0);
                    _selectedBand = band;
                    SettingsEdited?.Invoke(this, EventArgs.Empty);
                    BandSelected?.Invoke(this, band);
                    InvalidateVisual();
                }
                return;
            }

            if (band >= 0)
            {
                _dragBand = band;
                _selectedBand = band;
                _dragGain = _settings?.EffectiveGain(band) ?? 0;
                CaptureMouse();
                BandSelected?.Invoke(this, band);
                InvalidateVisual();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point position = e.GetPosition(this);

            if (_dragBand >= 0 && _settings != null)
            {
                double db = DecibelsForY(position.Y);
                double snapped = Math.Round(db / Bands.GainStepDb, MidpointRounding.AwayFromZero) * Bands.GainStepDb;
                snapped = Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, snapped));

                double oldFrequency = _settings.Frequencies[_dragBand];
                double frequency = oldFrequency;
                if (_settings.AdvancedMode)
                {
                    frequency = Math.Max(20, Math.Min(20000, FrequencyForX(position.X)));
                    frequency = Math.Round(frequency < 1000 ? frequency : frequency / 10) * (frequency < 1000 ? 1 : 10);
                    _settings.Frequencies[_dragBand] = frequency;
                }

                if (Math.Abs(snapped - _settings.EffectiveGain(_dragBand)) > 1e-9 ||
                    Math.Abs(frequency - oldFrequency) > 1e-9)
                {
                    _settings.SetEffectiveGain(_dragBand, snapped);
                    _dragGain = snapped;
                    SettingsEdited?.Invoke(this, EventArgs.Empty);
                }

                InvalidateVisual();
                return;
            }

            int hover = HitTestBand(position);
            if (hover != _hoverBand)
            {
                _hoverBand = hover;
                Cursor = hover >= 0
                    ? (_settings?.AdvancedMode == true ? Cursors.SizeAll : Cursors.SizeNS)
                    : Cursors.Cross;
                InvalidateVisual();
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (_dragBand >= 0)
            {
                _dragBand = -1;
                ReleaseMouseCapture();
                InvalidateVisual();
            }
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);

            int band = HitTestBand(e.GetPosition(this));
            if (band >= 0 && _settings != null)
            {
                _settings.Qs[band] = Bands.All[band].DefaultQ;
                _selectedBand = band;
                SettingsEdited?.Invoke(this, EventArgs.Empty);
                BandSelected?.Invoke(this, band);
                InvalidateVisual();
            }
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_settings == null) return;

            Point position = e.GetPosition(this);
            int band = HitTestBand(position);
            if (band < 0) band = _selectedBand;
            if (band < 0) return;

            _wheelAccumulator += e.Delta / 120.0;
            double steps = Math.Truncate(_wheelAccumulator);
            if (Math.Abs(steps) < 0.5) return;
            _wheelAccumulator -= steps;

            double q = _settings.Qs[band] + steps * 0.05;
            _settings.Qs[band] = Math.Max(0.3, Math.Min(6.0, Math.Round(q, 2)));
            _selectedBand = band;

            SettingsEdited?.Invoke(this, EventArgs.Empty);
            BandSelected?.Invoke(this, band);
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBand >= 0)
            {
                _hoverBand = -1;
                InvalidateVisual();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_settings == null) return;

            int band = _selectedBand >= 0 ? _selectedBand : 0;
            bool changed = false;

            switch (e.Key)
            {
                case Key.Up:
                case Key.OemPlus:
                    _settings.SetEffectiveGain(band, _settings.EffectiveGain(band) + Bands.GainStepDb);
                    changed = true;
                    break;

                case Key.Down:
                case Key.OemMinus:
                    _settings.SetEffectiveGain(band, _settings.EffectiveGain(band) - Bands.GainStepDb);
                    changed = true;
                    break;

                case Key.Left:
                    _selectedBand = Math.Max(0, band - 1);
                    BandSelected?.Invoke(this, _selectedBand);
                    InvalidateVisual();
                    e.Handled = true;
                    return;

                case Key.Right:
                    _selectedBand = Math.Min(Bands.Count - 1, band + 1);
                    BandSelected?.Invoke(this, _selectedBand);
                    InvalidateVisual();
                    e.Handled = true;
                    return;

                case Key.D0:
                case Key.NumPad0:
                    _settings.SetEffectiveGain(band, 0);
                    changed = true;
                    break;
            }

            if (changed)
            {
                _selectedBand = band;
                SettingsEdited?.Invoke(this, EventArgs.Empty);
                BandSelected?.Invoke(this, band);
                InvalidateVisual();
                e.Handled = true;
            }
        }

        private int HitTestBand(Point position)
        {
            if (_settings == null) return -1;

            int best = -1;
            double bestDistance = 16;

            for (int band = 0; band < Bands.Count; band++)
            {
                if (!_settings.Enabled[band]) continue;
                double x = XForFrequency(_settings.Frequencies[band]);
                double y = YForDecibels(_settings.EffectiveGain(band));
                double distance = Math.Sqrt((x - position.X) * (x - position.X) + (y - position.Y) * (y - position.Y));

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = band;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ rendering

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 40 || height < 40) return;

            context.DrawRectangle(Backdrop, null, new Rect(0, 0, width, height));

            DrawGrid(context);
            if (_showSpectrum) DrawSpectrum(context);
            DrawPreampLine(context);

            if (_settings != null)
            {
                DrawCurve(context);
                DrawHandles(context);
            }

            DrawAxisLabels(context);
            DrawReadouts(context);
            DrawLegend(context);
        }

        private void DrawGrid(DrawingContext context)
        {
            double top = TopPad;
            double bottom = TopPad + PlotHeight;

            double[] decades = { 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 };
            foreach (double frequency in decades)
            {
                double x = Math.Round(XForFrequency(frequency)) + 0.5;
                bool major = frequency == 100 || frequency == 1000 || frequency == 10000;
                context.DrawLine(major ? GridMajor : GridMinor, new Point(x, top), new Point(x, bottom));
            }

            for (double db = BottomDb; db <= TopDb; db += 3)
            {
                double y = Math.Round(YForDecibels(db)) + 0.5;
                bool isZero = Math.Abs(db) < 0.001;
                context.DrawLine(isZero ? ZeroLine : GridMinor, new Point(LeftPad, y), new Point(LeftPad + PlotWidth, y));
            }

            // The top of the fader, called out because the product promises a visible path to it.
            double elevenY = Math.Round(YForDecibels(Bands.MaxGainDb)) + 0.5;
            context.DrawLine(ElevenLine, new Point(LeftPad, elevenY), new Point(LeftPad + PlotWidth, elevenY));
        }

        private void DrawSpectrum(DrawingContext context)
        {
            if (_spectrum.Length < 8) return;

            int columns = 280;
            using (StreamGeometryContext geometry = _spectrumGeometry.Open())
            {
                bool started = false;
                double bottom = TopPad + PlotHeight;

                for (int column = 0; column <= columns; column++)
                {
                    double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, column / (double)columns);
                    double db = SpectrumDecibelsAt(frequency);
                    double x = XForFrequency(frequency);
                    double y = YForDecibels(Math.Max(BottomDb, Math.Min(TopDb, db + TopDb)));

                    if (!started)
                    {
                        geometry.BeginFigure(new Point(x, bottom), true, true);
                        geometry.LineTo(new Point(x, y), true, false);
                        started = true;
                    }
                    else
                    {
                        geometry.LineTo(new Point(x, y), true, false);
                    }
                }

                if (started)
                {
                    geometry.LineTo(new Point(XForFrequency(MaximumHz), bottom), true, false);
                }
            }

            context.DrawGeometry(SpectrumFill, SpectrumStroke, _spectrumGeometry);
        }

        /// <summary>Peak-holds the bins inside one display column so narrow tones stay visible.</summary>
        private double SpectrumDecibelsAt(double frequency)
        {
            if (_spectrum.Length == 0 || _spectrumSampleRate <= 0) return -100;

            double binWidth = (double)_spectrumSampleRate / (_spectrum.Length * 2);
            int firstBin = (int)(frequency / binWidth);
            int lastBin = (int)(frequency * 1.06 / binWidth);

            firstBin = Math.Max(1, Math.Min(_spectrum.Length - 1, firstBin));
            lastBin = Math.Max(firstBin, Math.Min(_spectrum.Length - 1, lastBin));

            double peak = -100;
            for (int bin = firstBin; bin <= lastBin; bin++)
            {
                if (_spectrum[bin] > peak) peak = _spectrum[bin];
            }

            return peak;
        }

        private void DrawPreampLine(DrawingContext context)
        {
            if (_settings == null || Math.Abs(_settings.PreampDb) < 0.05) return;

            double y = Math.Round(YForDecibels(_settings.PreampDb)) + 0.5;
            context.DrawLine(PreampLine, new Point(LeftPad, y), new Point(LeftPad + PlotWidth, y));

            DrawText(context, $"PREAMP {_settings.PreampDb:+0.0;-0.0;0.0} dB",
                new Point(LeftPad + PlotWidth - 4, y - 15), 10, LabelBrush, NumericTypeface, TextAlignment.Right, PlotWidth);
        }

        private void DrawCurve(DrawingContext context)
        {
            EqSettings settings = _settings!;
            double sampleRate = SampleRate <= 0 ? 48000 : SampleRate;

            Biquad[] filters = new Biquad[Bands.Count];
            for (int band = 0; band < Bands.Count; band++)
            {
                filters[band] = settings.Enabled[band]
                    ? Biquad.ForBand(settings.Kinds[band], sampleRate, settings.Frequencies[band],
                        settings.Qs[band], settings.EffectiveGain(band))
                    : Biquad.Identity;
            }

            double[] curve = new double[CurveResolution + 1];
            double[] net = new double[CurveResolution + 1];
            double baseline = YForDecibels(0);

            using (StreamGeometryContext geometry = _curveGeometry.Open())
            {
                for (int point = 0; point <= CurveResolution; point++)
                {
                    double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, point / (double)CurveResolution);
                    double total = 0;
                    for (int band = 0; band < Bands.Count; band++)
                    {
                        total += filters[band].GainDbAt(sampleRate, frequency);
                    }

                    curve[point] = total;
                    net[point] = total + settings.PreampDb;

                    Point position = new Point(XForFrequency(frequency), YForDecibels(total));
                    if (point == 0) geometry.BeginFigure(position, false, false);
                    else geometry.LineTo(position, true, false);
                }
            }

            using (StreamGeometryContext fill = _curveFillGeometry.Open())
            {
                fill.BeginFigure(new Point(XForFrequency(MinimumHz), baseline), true, true);
                for (int point = 0; point <= CurveResolution; point++)
                {
                    double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, point / (double)CurveResolution);
                    fill.LineTo(new Point(XForFrequency(frequency), YForDecibels(curve[point])), true, false);
                }
                fill.LineTo(new Point(XForFrequency(MaximumHz), baseline), true, false);
            }

            context.DrawGeometry(CurveFill, null, _curveFillGeometry);
            context.DrawGeometry(null, CurveStroke, _curveGeometry);

            if (Math.Abs(settings.PreampDb) > 0.05)
            {
                using (StreamGeometryContext geometry = _netGeometry.Open())
                {
                    for (int point = 0; point <= CurveResolution; point++)
                    {
                        double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, point / (double)CurveResolution);
                        Point position = new Point(XForFrequency(frequency), YForDecibels(net[point]));
                        if (point == 0) geometry.BeginFigure(position, false, false);
                        else geometry.LineTo(position, true, false);
                    }
                }

                context.DrawGeometry(null, NetStroke, _netGeometry);
            }
        }

        private void DrawHandles(DrawingContext context)
        {
            EqSettings settings = _settings!;

            for (int band = 0; band < Bands.Count; band++)
            {
                if (!settings.Enabled[band]) continue;
                double x = XForFrequency(settings.Frequencies[band]);

                // Handles sit on the effective gain, so a handle is always on the curve
                // even when a headphone target trim is riding on top of the faders.
                double y = YForDecibels(settings.EffectiveGain(band));

                bool active = band == _dragBand || band == _hoverBand || band == _selectedBand;

                if (band == _selectedBand || band == _dragBand)
                {
                    context.DrawEllipse(HandleGlow, null, new Point(x, y), HandleRadius * 3.1, HandleRadius * 3.1);
                }

                context.DrawEllipse(HandleFill, active ? HandleStrokeHover : HandleStroke,
                    new Point(x, y), HandleRadius, HandleRadius);

                // A hairline down to the zero axis makes small moves readable.
                if (active)
                {
                    context.DrawLine(HandleStroke, new Point(x, y + HandleRadius),
                        new Point(x, YForDecibels(0)));
                }
            }
        }

        private void DrawAxisLabels(DrawingContext context)
        {
            double axisY = TopPad + PlotHeight + 7;

            (double Frequency, string Label)[] ticks =
            {
                (20, "20"), (100, "100"), (1000, "1k"), (10000, "10k"), (20000, "20k")
            };

            foreach ((double frequency, string label) in ticks)
            {
                double x = XForFrequency(frequency);
                TextAlignment alignment = frequency <= 20 ? TextAlignment.Left
                    : frequency >= 20000 ? TextAlignment.Right
                    : TextAlignment.Center;
                DrawText(context, label, new Point(x, axisY), 10.5, LabelBrush, LabelTypeface, alignment, PlotWidth);
            }

            DrawText(context, "Hz", new Point(LeftPad, axisY), 10.5, LabelBrush, LabelTypeface, TextAlignment.Left);

            double[] decibelTicks = { 12, 6, 0, -6, -12 };
            foreach (double db in decibelTicks)
            {
                double y = YForDecibels(db) - 7;
                DrawText(context, db > 0 ? "+" + db : db.ToString("0", CultureInfo.InvariantCulture),
                    new Point(LeftPad - 8, y), 10.5, LabelBrush, NumericTypeface, TextAlignment.Right, LeftPad - 8);
            }
        }

        private void DrawReadouts(DrawingContext context)
        {
            if (_settings == null) return;

            int band = _dragBand >= 0 ? _dragBand : _hoverBand >= 0 ? _hoverBand : _selectedBand;
            if (band < 0) return;

            BandDefinition definition = Bands.All[band];
            string gain = _settings.EffectiveGain(band).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
            string q = _settings.Qs[band].ToString("0.00", CultureInfo.InvariantCulture);

            double x = XForFrequency(_settings.Frequencies[band]);
            double y = YForDecibels(_settings.EffectiveGain(band));

            string headline = $"{definition.Name}  {gain} dB";
            string detail = $"{FormatFrequency(_settings.Frequencies[band])} · { _settings.Kinds[band] } · width Q {q}" +
                            (band == _selectedBand ? " · ↑↓ nudge · wheel width" : string.Empty);

            FormattedText headlineText = Text(headline, 12, LabelStrongBrush, StrongTypeface);
            FormattedText detailText = Text(detail, 10.5, LabelBrush, LabelTypeface);

            double boxWidth = Math.Max(headlineText.Width, detailText.Width) + 20;
            double boxHeight = headlineText.Height + detailText.Height + 14;

            double boxX = Math.Max(LeftPad, Math.Min(LeftPad + PlotWidth - boxWidth, x - boxWidth / 2));
            double boxY = y - boxHeight - 18;
            if (boxY < TopPad) boxY = y + 20;
            if (boxY + boxHeight > TopPad + PlotHeight) boxY = TopPad + PlotHeight - boxHeight;

            Rect box = new Rect(boxX, boxY, boxWidth, boxHeight);
            context.DrawRoundedRectangle(PanelBrush, PanelStroke, box, 5, 5);

            context.DrawText(headlineText, new Point(box.X + 10, box.Y + 6));
            context.DrawText(detailText, new Point(box.X + 10, box.Y + 7 + headlineText.Height));
        }

        private static string FormatFrequency(double frequency)
        {
            if (frequency >= 1000) return (frequency / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + " kHz";
            return frequency.ToString("0", CultureInfo.InvariantCulture) + " Hz";
        }

        private void DrawLegend(DrawingContext context)
        {
            if (_settings == null) return;

            double x = LeftPad + 4;
            double y = TopPad + 2;

            context.DrawRectangle(AccentBrush, null, new Rect(x, y + 4, 10, 2));
            DrawText(context, "BANDS", new Point(x + 15, y), 10, LabelBrush, LabelTypeface, TextAlignment.Left);

            if (Math.Abs(_settings.PreampDb) > 0.05)
            {
                double secondX = x + 68;
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xCC, 0x6F, 0xA8, 0xFF)), null,
                    new Rect(secondX, y + 4, 10, 2));
                DrawText(context, "NET (WITH PREAMP)", new Point(secondX + 15, y), 10, LabelBrush, LabelTypeface, TextAlignment.Left);
            }
        }

        private static void DrawText(DrawingContext context, string value, Point origin, double size,
            Brush brush, Typeface typeface, TextAlignment alignment, double availableWidth = 0)
        {
            FormattedText text = Text(value, size, brush, typeface);

            if (alignment == TextAlignment.Center && availableWidth > 0)
            {
                origin = new Point(origin.X - text.Width / 2, origin.Y);
            }
            else if (alignment == TextAlignment.Right && availableWidth > 0)
            {
                origin = new Point(origin.X - text.Width, origin.Y);
            }

            context.DrawText(text, origin);
        }

        private static FormattedText Text(string value, double size, Brush brush, Typeface typeface)
        {
            return new FormattedText(
                value,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                size,
                brush,
                1.0);
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
