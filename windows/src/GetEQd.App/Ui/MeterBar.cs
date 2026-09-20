using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GetEQd.Ui
{
    /// <summary>
    /// Stereo peak meter with peak-hold and a safety-ceiling gain-reduction lane.
    /// The scale is logarithmic from -60 dBFS to 0 dBFS, so colour corresponds to level.
    /// </summary>
    public sealed class MeterBar : FrameworkElement
    {
        private const double FloorDb = -60;
        private const double ReductionRangeDb = -12;

        private static readonly Brush TrackBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x1E)));
        private static readonly Pen TrackStroke = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x30)), 1));
        private static readonly Brush TickBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x99, 0x3A, 0x42, 0x50)));
        private static readonly Brush HoldBrush = Frozen(new SolidColorBrush(Color.FromArgb(0xE0, 0xE8, 0xEC, 0xF2)));
        private static readonly Brush ClipBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x4F)));
        private static readonly Brush ReductionBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xFF)));

        private static readonly Brush LevelGradient = CreateLevelGradient();

        private static readonly Brush IdleBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x40, 0x6E, 0x76, 0x86)));

        private double _leftDb = FloorDb;
        private double _rightDb = FloorDb;
        private double _holdLeftDb = FloorDb;
        private double _holdRightDb = FloorDb;
        private double _reductionDb;
        private bool _hasSignal;

        private DateTime _lastUpdate = DateTime.UtcNow;

        public MeterBar()
        {
            SnapsToDevicePixels = true;
            ToolTip = "Peak level after the ceiling. GR shows how hard the safety limiter is working.";
        }

        /// <summary>Feeds a new measurement. Values are linear amplitudes.</summary>
        public void SetLevels(double left, double right, double reductionDb, bool active)
        {
            DateTime now = DateTime.UtcNow;
            double elapsed = Math.Max(0.001, (now - _lastUpdate).TotalSeconds);
            _lastUpdate = now;

            _leftDb = AmplitudeToDb(left);
            _rightDb = AmplitudeToDb(right);
            _reductionDb = reductionDb;
            _hasSignal = active;

            // Peak hold falls at 14 dB per second, which keeps a transient readable.
            double decay = 14 * elapsed;
            _holdLeftDb = Math.Max(FloorDb, Math.Max(_holdLeftDb - decay, _leftDb));
            _holdRightDb = Math.Max(FloorDb, Math.Max(_holdRightDb - decay, _rightDb));

            InvalidateVisual();
        }

        public void Clear()
        {
            _leftDb = _rightDb = FloorDb;
            _holdLeftDb = _holdRightDb = FloorDb;
            _reductionDb = 0;
            _hasSignal = false;
            InvalidateVisual();
        }

        private static double AmplitudeToDb(double amplitude)
        {
            double magnitude = Math.Abs(amplitude);
            if (magnitude < 1e-6) return FloorDb;
            double db = 20 * Math.Log10(magnitude);
            return Math.Max(FloorDb, Math.Min(6, db));
        }

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            if (width < 30 || height < 12) return;

            double barHeight = Math.Max(4, (height - 12) / 2.6);
            double gap = 3;
            double reductionHeight = Math.Max(3, height - barHeight * 2 - gap * 2 - 4);

            double y = 0;
            DrawBar(context, width, y, barHeight, _leftDb, _holdLeftDb);
            y += barHeight + gap;
            DrawBar(context, width, y, barHeight, _rightDb, _holdRightDb);
            y += barHeight + gap;
            DrawReduction(context, width, y, reductionHeight);

            DrawTicks(context, width, barHeight * 2 + gap);
        }

        private void DrawBar(DrawingContext context, double width, double top, double height,
            double levelDb, double holdDb)
        {
            Rect track = new Rect(0, top, width, height);
            context.DrawRoundedRectangle(TrackBrush, TrackStroke, track, 2.5, 2.5);

            double fraction = ToFraction(levelDb);
            if (fraction > 0.001)
            {
                double filled = Math.Max(2, width * fraction);
                context.DrawRoundedRectangle(LevelGradient, null, new Rect(0, top, filled, height), 2.5, 2.5);
            }
            else if (!_hasSignal)
            {
                context.DrawRectangle(IdleBrush, null, new Rect(0, top + height / 2 - 0.5, width, 1));
            }

            double holdFraction = ToFraction(holdDb);
            if (holdFraction > 0.002)
            {
                double x = Math.Min(width - 2, width * holdFraction);
                Brush brush = holdDb >= -0.5 ? ClipBrush : HoldBrush;
                context.DrawRectangle(brush, null, new Rect(Math.Max(0, x - 1), top, 2, height));
            }
        }

        private void DrawReduction(DrawingContext context, double width, double top, double height)
        {
            context.DrawRoundedRectangle(TrackBrush, TrackStroke, new Rect(0, top, width, height), 2, 2);

            if (_reductionDb > -0.05) return;

            double fraction = Math.Min(1, -_reductionDb / -ReductionRangeDb);
            double filled = Math.Max(2, width * fraction);
            context.DrawRoundedRectangle(ReductionBrush, null, new Rect(0, top, filled, height), 2, 2);
        }

        private void DrawTicks(DrawingContext context, double width, double barHeight)
        {
            double[] ticks = { -48, -36, -24, -12, -6 };
            foreach (double tick in ticks)
            {
                double x = Math.Round(width * ToFraction(tick)) + 0.5;
                context.DrawRectangle(TickBrush, null, new Rect(x, 0, 1, barHeight));
            }

            double zero = Math.Round(width * ToFraction(0)) - 1;
            context.DrawRectangle(ClipBrush, null, new Rect(Math.Max(0, zero), 0, 2, barHeight));
        }

        /// <summary>Maps dBFS onto 0..1 across the bar.</summary>
        private static double ToFraction(double db)
        {
            if (db <= FloorDb) return 0;
            if (db >= 0) return 1;
            return (db - FloorDb) / (0 - FloorDb);
        }

        private static Brush CreateLevelGradient()
        {
            LinearGradientBrush brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                MappingMode = BrushMappingMode.RelativeToBoundingBox
            };

            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2C, 0x6E, 0x52), ToFraction(-60)));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4F, 0xD3, 0x9A), ToFraction(-30)));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8F, 0xC9, 0x6E), ToFraction(-14)));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xB3, 0x47), ToFraction(-8)));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x8A, 0x5B), ToFraction(-3)));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0x5A, 0x4F), 1.0));

            brush.Freeze();
            return brush;
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        public static string FormatDb(double db)
        {
            if (db <= -59.9) return "−∞";
            return db.ToString("0.0", CultureInfo.InvariantCulture);
        }
    }
}
