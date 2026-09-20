using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using GetEQd.Audio;

namespace GetEQd.Ui
{
    /// <summary>
    /// A read-only measurement view. It deliberately keeps the raw response, target,
    /// and required correction visually separate so a calibration can be audited before
    /// it is placed on the live EQ.
    /// </summary>
    public sealed class ResponseLabView : FrameworkElement
    {
        private const double MinimumHz = 20;
        private const double MaximumHz = 20000;
        private const double TopDb = 15;
        private const double BottomDb = -15;
        private const double LeftPad = 34;
        private const double RightPad = 8;
        private const double TopPad = 20;
        private const double BottomPad = 22;
        private const int Resolution = 260;

        private static readonly Brush Backdrop = Frozen(new SolidColorBrush(Color.FromRgb(0x0B, 0x0D, 0x11)));
        private static readonly Pen GridPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x27, 0x31)), 1));
        private static readonly Pen ZeroPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x46, 0x4E, 0x5C)), 1));
        private static readonly Pen RawPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x74, 0xB9, 0xFF)), 1.8));
        private static readonly Pen TargetPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xD4, 0xDA, 0xE5)), 1.3)
        {
            DashStyle = new DashStyle(new double[] { 5, 3 }, 0)
        });
        private static readonly Pen CorrectionPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)), 2.0));
        private static readonly Brush LabelBrush = Frozen(new SolidColorBrush(Color.FromRgb(0x82, 0x8C, 0x9C)));
        private static readonly Brush StrongBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xD4, 0xDA, 0xE5)));
        private static readonly Typeface LabelTypeface = new Typeface(
            new FontFamily("Segoe UI, Tahoma"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface NumericTypeface = new Typeface(
            new FontFamily("Cascadia Mono, Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        private MeasurementProfile? _profile;

        public ResponseLabView()
        {
            ClipToBounds = true;
            SnapsToDevicePixels = true;
            ToolTip = "Raw measurement, target, and required correction. This view does not edit the imported source.";
        }

        public MeasurementProfile? Profile
        {
            get => _profile;
            set
            {
                _profile = value;
                InvalidateVisual();
            }
        }

        private double PlotWidth => Math.Max(1, ActualWidth - LeftPad - RightPad);
        private double PlotHeight => Math.Max(1, ActualHeight - TopPad - BottomPad);
        private static readonly double LogMin = Math.Log10(MinimumHz);
        private static readonly double LogMax = Math.Log10(MaximumHz);

        private double XForFrequency(double frequency)
        {
            double normalised = (Math.Log10(Math.Max(MinimumHz, Math.Min(MaximumHz, frequency))) - LogMin) / (LogMax - LogMin);
            return LeftPad + normalised * PlotWidth;
        }

        private double YForDecibels(double db)
        {
            double clamped = Math.Max(BottomDb, Math.Min(TopDb, db));
            return TopPad + (TopDb - clamped) / (TopDb - BottomDb) * PlotHeight;
        }

        protected override void OnRender(DrawingContext context)
        {
            if (ActualWidth < 40 || ActualHeight < 40) return;

            context.DrawRectangle(Backdrop, null, new Rect(0, 0, ActualWidth, ActualHeight));
            DrawGrid(context);

            if (_profile == null)
            {
                DrawText(context, "SELECT A MEASUREMENT BELOW", new Point(LeftPad, TopPad + PlotHeight / 2 - 7),
                    10, LabelBrush, LabelTypeface);
                return;
            }

            DrawCurve(context, RawPen, frequency => MeasurementProfileIO.RawDbAt(_profile, frequency));
            DrawCurve(context, TargetPen, frequency => MeasurementProfileIO.TargetDbAt(_profile, frequency));
            DrawCurve(context, CorrectionPen, frequency => MeasurementProfileIO.CorrectionDbAt(_profile, frequency));

            DrawText(context, "RAW", new Point(LeftPad, 4), 9.5, RawPen.Brush, LabelTypeface);
            DrawText(context, "TARGET", new Point(LeftPad + 42, 4), 9.5, TargetPen.Brush, LabelTypeface);
            DrawText(context, "CORRECTION", new Point(LeftPad + 98, 4), 9.5, CorrectionPen.Brush, LabelTypeface);
            DrawAxisLabels(context);
        }

        private void DrawGrid(DrawingContext context)
        {
            double bottom = TopPad + PlotHeight;
            double[] decades = { 20, 100, 1000, 10000, 20000 };
            foreach (double frequency in decades)
            {
                double x = Math.Round(XForFrequency(frequency)) + 0.5;
                context.DrawLine(GridPen, new Point(x, TopPad), new Point(x, bottom));
            }

            for (double db = BottomDb; db <= TopDb; db += 5)
            {
                double y = Math.Round(YForDecibels(db)) + 0.5;
                context.DrawLine(Math.Abs(db) < 0.01 ? ZeroPen : GridPen,
                    new Point(LeftPad, y), new Point(LeftPad + PlotWidth, y));
            }
        }

        private void DrawCurve(DrawingContext context, Pen pen, Func<double, double> valueAt)
        {
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext path = geometry.Open())
            {
                for (int point = 0; point <= Resolution; point++)
                {
                    double frequency = MinimumHz * Math.Pow(MaximumHz / MinimumHz, point / (double)Resolution);
                    Point position = new Point(XForFrequency(frequency), YForDecibels(valueAt(frequency)));
                    if (point == 0) path.BeginFigure(position, false, false);
                    else path.LineTo(position, true, false);
                }
            }
            geometry.Freeze();
            context.DrawGeometry(null, pen, geometry);
        }

        private void DrawAxisLabels(DrawingContext context)
        {
            double axisY = TopPad + PlotHeight + 5;
            (double Frequency, string Label)[] ticks = { (20, "20"), (1000, "1k"), (20000, "20k") };
            foreach ((double frequency, string label) in ticks)
            {
                TextAlignment alignment = frequency == 20 ? TextAlignment.Left : frequency == 20000 ? TextAlignment.Right : TextAlignment.Center;
                double x = XForFrequency(frequency);
                if (alignment == TextAlignment.Center) x -= 8;
                if (alignment == TextAlignment.Right) x -= 22;
                DrawText(context, label, new Point(x, axisY), 9.5, LabelBrush, NumericTypeface);
            }
        }

        private static void DrawText(DrawingContext context, string value, Point origin, double size, Brush brush, Typeface typeface)
        {
            context.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                typeface, size, brush, 1.0), origin);
        }

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
