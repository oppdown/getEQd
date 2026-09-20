using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GetEQd.Audio;
using Microsoft.Win32;

namespace GetEQd.Ui
{
    /// <summary>
    /// The console. Owns the engine, mirrors the settings into the UI, and keeps the
    /// control surface honest: every readout is derived from the same numbers the
    /// audio thread is using.
    /// </summary>
    public partial class MainView : UserControl
    {
        private readonly AudioEngine _engine = new AudioEngine();
        private readonly EqSettings _settings = new EqSettings();
        private readonly SpectrumAnalyzer _analyzer = new SpectrumAnalyzer(2048);
        private readonly float[] _spectrumScratch = new float[2048];

        private readonly Slider[] _bandSliders = new Slider[Bands.QuickCount];
        private readonly TextBlock[] _bandValueTexts = new TextBlock[Bands.QuickCount];
        private readonly Border[] _bandCards = new Border[Bands.QuickCount];
        private readonly List<AdvancedBandRow> _advancedRows = new List<AdvancedBandRow>();
        private readonly List<ToggleButton> _presetButtons = new List<ToggleButton>();
        private readonly List<ToggleButton> _routeButtons = new List<ToggleButton>();
        private readonly List<ToggleButton> _targetButtons = new List<ToggleButton>();
        private readonly List<ToggleButton> _handoffButtons = new List<ToggleButton>();

        private readonly DispatcherTimer _timer;

        private List<MeasurementProfile> _measurements = new List<MeasurementProfile>();
        private List<ListeningProfile> _listening = new List<ListeningProfile>();
        private MeasurementProfile? _selectedMeasurement;
        private int _deviceCount;
        private int _fitAttempts;

        private bool _updating;
        private bool _userSeeking;
        private bool _shutdown;
        private double _lastPositionSeconds;

        public MainView()
        {
            InitializeComponent();

            _settings.ApplyPreset("reference");

            BuildPresetPanel();
            BuildBandRack();
            BuildAdvancedPanel();
            BuildRoutePanel();
            BuildTargetPanel();
            BuildHandoffPanel();
            WireEvents();

            Curve.Attach(_settings);
            Curve.SampleRate = _engine.MixRate;

            LoadStoredProfiles();
            LoadOutputDevices();

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += OnTick;
            _timer.Start();

            Unloaded += (_, _) => Shutdown();

            // Row heights are only real once layout has run, so re-fit after loading.
            Loaded += (_, _) => FitEndpointList();

            RefreshAll();
        }

        public AudioEngine Engine => _engine;
        public EqSettings Settings => _settings;
        public CurveView CurveControl => Curve;

        private sealed class AdvancedBandRow
        {
            public required ToggleButton EnabledButton { get; init; }
            public required ComboBox TypeBox { get; init; }
            public required TextBox FrequencyBox { get; init; }
            public required Slider GainSlider { get; init; }
            public required Slider QSlider { get; init; }
            public required TextBlock GainValue { get; init; }
            public required TextBlock QValue { get; init; }
        }

        // ================================================================ construction

        private void BuildPresetPanel()
        {
            foreach (Presets.Preset preset in Presets.All)
            {
                ToggleButton button = new ToggleButton
                {
                    Content = preset.Label,
                    Style = FindStyle("Segment"),
                    Margin = new Thickness(0, 0, 6, 0),
                    Tag = preset.Id,
                    ToolTip = preset.Blurb
                };

                string id = preset.Id;
                button.Click += (_, _) => ApplyPreset(id);
                _presetButtons.Add(button);
                PresetPanel.Children.Add(button);
            }
        }

        private void BuildBandRack()
        {
            for (int index = 0; index < Bands.QuickCount; index++)
            {
                BandDefinition definition = Bands.All[index];
                int band = index;

                TextBlock indexText = TextBlockFor("0" + (index + 1).ToString(CultureInfo.InvariantCulture), "NumericSmall");
                TextBlock valueText = TextBlockFor("0.0", "NumericSmall");
                valueText.HorizontalAlignment = HorizontalAlignment.Right;

                Grid top = new Grid();
                top.Children.Add(indexText);
                top.Children.Add(valueText);

                TextBlock name = TextBlockFor(definition.Name, "CardTitle");
                name.FontSize = 12.5;
                name.Margin = new Thickness(0, 8, 0, 0);
                name.TextWrapping = TextWrapping.Wrap;

                TextBlock role = TextBlockFor(definition.Role, null);
                role.FontSize = 11;
                role.Foreground = FindBrush("TextMuted");
                role.Margin = new Thickness(0, 3, 0, 0);

                Slider slider = new Slider
                {
                    Style = FindStyle("StudioSlider"),
                    Minimum = Bands.MinGainDb,
                    Maximum = Bands.MaxGainDb,
                    SmallChange = Bands.GainStepDb,
                    LargeChange = 1,
                    TickFrequency = Bands.GainStepDb,
                    Value = 0,
                    Margin = new Thickness(0, 9, 0, 0),
                    ToolTip = definition.Name + " · " + definition.FrequencyLabel +
                              "\nDrag, or use ↑ ↓ with the band selected on the curve."
                };

                slider.ValueChanged += (_, args) => OnBandSliderChanged(band, args.NewValue);

                TextBlock frequency = TextBlockFor(definition.FrequencyLabel, "NumericSmall");
                frequency.Margin = new Thickness(0, 7, 0, 0);

                StackPanel content = new StackPanel();
                content.Children.Add(top);
                content.Children.Add(name);
                content.Children.Add(role);
                content.Children.Add(slider);
                content.Children.Add(frequency);

                Border card = new Border
                {
                    Style = FindStyle("Card"),
                    Margin = new Thickness(index == 0 ? 0 : 3, 0, index == Bands.QuickCount - 1 ? 0 : 3, 0),
                    Padding = new Thickness(10, 9, 10, 9),
                    Background = FindBrush("SurfaceSunken"),
                    Cursor = Cursors.Hand
                };
                card.Child = content;

                int captured = index;
                card.MouseLeftButtonDown += (_, _) => CurveControl.SetSelectedBand(captured);

                _bandSliders[index] = slider;
                _bandValueTexts[index] = valueText;
                _bandCards[index] = card;
                BandRack.Children.Add(card);
            }
        }

        private void BuildAdvancedPanel()
        {
            for (int index = 0; index < Bands.Count; index++)
            {
                int band = index;
                BandDefinition definition = Bands.All[index];

                ToggleButton enabledButton = new ToggleButton
                {
                    Style = FindStyle("Segment"),
                    Width = 48,
                    Padding = new Thickness(4, 3, 4, 3),
                    FontSize = 10,
                    ToolTip = "Enable or bypass this filter slot."
                };

                TextBlock name = TextBlockFor("0" + (index + 1).ToString(CultureInfo.InvariantCulture) + "  " + definition.Name,
                    "CardTitle", 11.5);
                name.VerticalAlignment = VerticalAlignment.Center;

                ComboBox typeBox = new ComboBox
                {
                    Style = FindStyle("StudioComboBox"),
                    Width = 104,
                    ItemsSource = new[] { BandKind.LowShelf, BandKind.Peaking, BandKind.HighShelf,
                        BandKind.LowPass, BandKind.HighPass, BandKind.Notch },
                    ToolTip = "Filter shape for this slot."
                };

                TextBox frequencyBox = new TextBox
                {
                    Style = FindStyle("StudioTextBox"),
                    Width = 64,
                    Padding = new Thickness(6, 4, 6, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Right,
                    ToolTip = "Centre or cutoff frequency in Hz. Press Enter or leave the field to apply."
                };

                TextBlock gainValue = TextBlockFor("0.0 dB", "NumericSmall");
                gainValue.Width = 58;
                gainValue.HorizontalAlignment = HorizontalAlignment.Right;
                Slider gainSlider = new Slider
                {
                    Style = FindStyle("StudioSlider"),
                    Minimum = Bands.MinGainDb,
                    Maximum = Bands.MaxGainDb,
                    SmallChange = Bands.GainStepDb,
                    LargeChange = 1,
                    Margin = new Thickness(7, 0, 0, 0),
                    ToolTip = "Gain for this filter."
                };

                TextBlock qValue = TextBlockFor("Q 1.00", "NumericSmall");
                qValue.Width = 54;
                qValue.HorizontalAlignment = HorizontalAlignment.Right;
                Slider qSlider = new Slider
                {
                    Style = FindStyle("StudioSlider"),
                    Minimum = 0.2,
                    Maximum = 8,
                    SmallChange = 0.05,
                    LargeChange = 0.5,
                    Margin = new Thickness(7, 0, 0, 0),
                    ToolTip = "Filter width. Lower Q is broader; higher Q is narrower."
                };

                Grid top = new Grid();
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(enabledButton, 0);
                Grid.SetColumn(name, 1);
                Grid.SetColumn(typeBox, 2);
                Grid.SetColumn(frequencyBox, 3);
                top.Children.Add(enabledButton);
                top.Children.Add(name);
                top.Children.Add(typeBox);
                top.Children.Add(frequencyBox);

                Grid bottom = new Grid { Margin = new Thickness(0, 7, 0, 0) };
                bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(gainSlider, 1);
                Grid.SetColumn(gainValue, 0);
                Grid.SetColumn(qValue, 2);
                Grid.SetColumn(qSlider, 3);
                bottom.Children.Add(gainValue);
                bottom.Children.Add(gainSlider);
                bottom.Children.Add(qValue);
                bottom.Children.Add(qSlider);

                StackPanel content = new StackPanel();
                content.Children.Add(top);
                content.Children.Add(bottom);

                Border card = new Border
                {
                    Style = FindStyle("Card"),
                    Background = FindBrush("SurfaceSunken"),
                    Padding = new Thickness(8, 8, 8, 8),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = content
                };
                AdvancedFilterList.Children.Add(card);

                AdvancedBandRow row = new AdvancedBandRow
                {
                    EnabledButton = enabledButton,
                    TypeBox = typeBox,
                    FrequencyBox = frequencyBox,
                    GainSlider = gainSlider,
                    QSlider = qSlider,
                    GainValue = gainValue,
                    QValue = qValue
                };
                _advancedRows.Add(row);

                enabledButton.Click += (_, _) =>
                {
                    if (_updating) return;
                    _settings.Enabled[band] = enabledButton.IsChecked == true;
                    _settings.ActivePresetId = "custom";
                    RefreshAll();
                };
                typeBox.SelectionChanged += (_, _) =>
                {
                    if (_updating || typeBox.SelectedItem is not BandKind kind) return;
                    _settings.Kinds[band] = kind;
                    _settings.ActivePresetId = "custom";
                    RefreshAll();
                };
                gainSlider.ValueChanged += (_, args) =>
                {
                    if (_updating) return;
                    _settings.SetEffectiveGain(band, Math.Round(args.NewValue / Bands.GainStepDb,
                        MidpointRounding.AwayFromZero) * Bands.GainStepDb);
                    _settings.ActivePresetId = "custom";
                    RefreshAll();
                };
                qSlider.ValueChanged += (_, args) =>
                {
                    if (_updating) return;
                    _settings.Qs[band] = Math.Max(0.2, Math.Min(8, Math.Round(args.NewValue, 2)));
                    _settings.ActivePresetId = "custom";
                    RefreshAll();
                };
                frequencyBox.LostFocus += (_, _) => ApplyAdvancedFrequency(band, frequencyBox);
                frequencyBox.KeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter)
                    {
                        ApplyAdvancedFrequency(band, frequencyBox);
                        args.Handled = true;
                    }
                };
            }
        }

        private void ApplyAdvancedFrequency(int band, TextBox box)
        {
            if (_updating || !double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                RefreshAdvancedRows();
                return;
            }

            _settings.Frequencies[band] = Math.Max(20, Math.Min(20000, Math.Round(value)));
            _settings.ActivePresetId = "custom";
            RefreshAll();
        }

        private void RefreshAdvancedRows()
        {
            AdvancedFilterList.Visibility = _settings.AdvancedMode ? Visibility.Visible : Visibility.Collapsed;
            AdvancedToggleButton.IsChecked = _settings.AdvancedMode;
            AdvancedToggleButton.Content = _settings.AdvancedMode ? "Close editor" : "Open editor";

            int enabled = 0;
            for (int index = 0; index < _advancedRows.Count; index++)
            {
                AdvancedBandRow row = _advancedRows[index];
                bool active = _settings.Enabled[index];
                if (active) enabled++;
                row.EnabledButton.IsChecked = active;
                row.EnabledButton.Content = active ? "ON" : "OFF";
                row.TypeBox.SelectedItem = _settings.Kinds[index];
                row.FrequencyBox.Text = _settings.Frequencies[index].ToString("0", CultureInfo.InvariantCulture);
                row.GainSlider.Value = _settings.EffectiveGain(index);
                row.QSlider.Value = _settings.Qs[index];
                row.GainValue.Text = FormatDb(_settings.EffectiveGain(index));
                row.QValue.Text = "Q " + _settings.Qs[index].ToString("0.00", CultureInfo.InvariantCulture);
                row.GainSlider.IsEnabled = active;
                row.QSlider.IsEnabled = active;
                row.TypeBox.IsEnabled = active;
                row.FrequencyBox.IsEnabled = active;
            }

            AdvancedStatusText.Text = enabled + " of " + Bands.Count +
                " filter slots active. Drag handles horizontally on the graph to move frequency.";
        }

        private void BuildRoutePanel()
        {
            (Route Route, string Label, string Tip)[] routes =
            {
                (Route.Surround51, "5.1 Surround", "Surround rig. Bass management can hand the bottom octave to the LFE lane."),
                (Route.Stereo21, "2.1 Stereo", "Stereo pair with a summed sub lane."),
                (Route.Headphones, "Headphones", "Adds crossfeed and stage width. Targets apply here.")
            };

            foreach ((Route route, string label, string tip) in routes)
            {
                ToggleButton button = new ToggleButton
                {
                    Content = label,
                    Style = FindStyle("Segment"),
                    Margin = new Thickness(0, 0, 6, 0),
                    ToolTip = tip
                };

                Route captured = route;
                button.Click += (_, _) => SetRoute(captured);
                _routeButtons.Add(button);
                RoutePanel.Children.Add(button);
            }
        }

        private void BuildTargetPanel()
        {
            foreach (HeadphoneTargets.Target target in HeadphoneTargets.All)
            {
                ToggleButton button = new ToggleButton
                {
                    Content = target.Label,
                    Style = FindStyle("Segment"),
                    Margin = new Thickness(0, 0, 6, 6),
                    FontSize = 11.5,
                    ToolTip = target.Note
                };

                HeadphoneTarget captured = target.Id;
                button.Click += (_, _) =>
                {
                    _settings.Target = captured;
                    RefreshAll();
                };

                _targetButtons.Add(button);
                TargetPanel.Children.Add(button);
            }
        }

        private void BuildHandoffPanel()
        {
            (SoftwareHandoff Handoff, string Label)[] handoffs =
            {
                (SoftwareHandoff.DirectStereo, "Direct stereo"),
                (SoftwareHandoff.WindowsSpatialSound, "Spatial Sound"),
                (SoftwareHandoff.GameSpatialMix, "Game mix")
            };

            foreach ((SoftwareHandoff handoff, string label) in handoffs)
            {
                ToggleButton button = new ToggleButton
                {
                    Content = label,
                    Style = FindStyle("Segment"),
                    Margin = new Thickness(0, 0, 6, 0),
                    FontSize = 11
                };

                SoftwareHandoff captured = handoff;
                button.Click += (_, _) =>
                {
                    _settings.Handoff = captured;
                    RefreshAll();
                };

                _handoffButtons.Add(button);
                HandoffPanel.Children.Add(button);
            }
        }

        private void WireEvents()
        {
            BypassToggle.Click += (_, _) =>
            {
                _settings.Bypassed = BypassToggle.IsChecked == true;
                RefreshAll();
            };

            Curve.SettingsEdited += (_, _) =>
            {
                _settings.ActivePresetId = "custom";
                RefreshAll();
            };

            Curve.BandSelected += (_, band) => HighlightBand(band);

            AdvancedToggleButton.Click += (_, _) =>
            {
                _settings.AdvancedMode = AdvancedToggleButton.IsChecked == true;
                RefreshAll();
            };

            PreampSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.PreampDb = args.NewValue;
                RefreshAll();
            };

            CeilingSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.CeilingDb = args.NewValue;
                RefreshAll();
            };

            SubLaneSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.SubLaneDb = args.NewValue;
                RefreshAll();
            };

            CenterLiftSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.CenterLiftDb = args.NewValue;
                RefreshAll();
            };

            CrossfeedSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.CrossfeedPercent = args.NewValue;
                RefreshAll();
            };

            StageWidthSlider.ValueChanged += (_, args) =>
            {
                if (_updating) return;
                _settings.StageWidthPercent = args.NewValue;
                RefreshAll();
            };

            BassToSubCheck.Click += (_, _) =>
            {
                _settings.BassToSub = BassToSubCheck.IsChecked == true;
                RefreshAll();
            };

            TargetTrimCheck.Click += (_, _) =>
            {
                _settings.TargetTrimEnabled = TargetTrimCheck.IsChecked == true;
                RefreshAll();
            };

            TrimButton.Click += (_, _) =>
            {
                HeadroomAnalysis analysis = Analysis.Evaluate(_settings.CreateSnapshot(), GraphSampleRate);
                _settings.PreampDb = Math.Max(-12, Math.Min(11, Math.Round(analysis.SuggestedPreampDb * 2) / 2));
                RefreshAll();
            };

            OpenFileButton.Click += (_, _) => OpenAudioFile();
            PlayButton.Click += (_, _) => _engine.Toggle();
            StopButton.Click += (_, _) => _engine.Stop();

            LoopCheck.Click += (_, _) => _engine.Loop = LoopCheck.IsChecked == true;

            ProbePinkToggle.Click += (_, _) => UseProbe(ProbeSignal.PinkNoise);
            ProbeToneToggle.Click += (_, _) => UseProbe(ProbeSignal.Tone1k);
            ProbeSweepToggle.Click += (_, _) => UseProbe(ProbeSignal.SineSweep);

            PositionSlider.PreviewMouseLeftButtonDown += (_, _) => _userSeeking = true;
            PositionSlider.PreviewMouseLeftButtonUp += (_, _) =>
            {
                _userSeeking = false;
                SeekToSlider();
            };
            PositionSlider.ValueChanged += (_, _) =>
            {
                if (_userSeeking) SeekToSlider();
            };

            EndpointList.SelectionChanged += (_, _) =>
            {
                if (_updating) return;

                if (EndpointList.SelectedItem is ListBoxItem item && item.Tag is string deviceId)
                {
                    _engine.SelectDevice(deviceId);
                    RefreshAll();
                }
            };

            RefreshDevicesButton.Click += (_, _) => LoadOutputDevices();

            SaveProfileButton.Click += (_, _) => SaveListeningProfile();
            ProfileNameBox.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter) SaveListeningProfile();
            };
            ProfileNameBox.TextChanged += (_, _) => UpdateNamePlaceholder();

            ImportButton.Click += (_, _) => ImportMeasurement();
            TemplateButton.Click += (_, _) => ExportTemplate();
            ApplyCorrectionButton.Click += (_, _) => ApplyMeasurementCalibration();
            ExportAuditButton.Click += (_, _) => ExportMeasurementAudit();
            OpenFolderButton.Click += (_, _) => OpenStorageFolder();
        }

        /// <summary>Stops the console. Safe to call more than once.</summary>
        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;

            _timer.Stop();
            _engine.Dispose();
        }

        // ================================================================ state flow

        /// <summary>Pushes the current settings to the audio thread and repaints every readout.</summary>
        public void RefreshAll()
        {
            EqSnapshot snapshot = _settings.CreateSnapshot();
            _engine.Processor.Publish(snapshot);

            HeadroomAnalysis analysis = Analysis.Evaluate(snapshot, GraphSampleRate);

            _updating = true;
            try
            {
                BypassToggle.IsChecked = _settings.Bypassed;
                PreampSlider.Value = _settings.PreampDb;
                CeilingSlider.Value = _settings.CeilingDb;
                SubLaneSlider.Value = _settings.SubLaneDb;
                CenterLiftSlider.Value = _settings.CenterLiftDb;
                CrossfeedSlider.Value = _settings.CrossfeedPercent;
                StageWidthSlider.Value = _settings.StageWidthPercent;
                BassToSubCheck.IsChecked = _settings.BassToSub;
                TargetTrimCheck.IsChecked = _settings.TargetTrimEnabled;

                for (int index = 0; index < Bands.QuickCount; index++)
                {
                    _bandSliders[index].Value = _settings.EffectiveGain(index);
                }

                RefreshAdvancedRows();

                SelectExclusive(_presetButtons, _settings.ActivePresetId);
                SelectExclusive(_routeButtons, _settings.Route.ToString());
                SelectExclusive(_targetButtons, _settings.Target.ToString());
                SelectExclusive(_handoffButtons, _settings.Handoff.ToString());
            }
            finally
            {
                _updating = false;
            }

            PreampValue.Text = FormatDb(_settings.PreampDb);
            CeilingValue.Text = FormatDb(_settings.CeilingDb);
            SubLaneValue.Text = FormatDb(_settings.SubLaneDb);
            CenterLiftValue.Text = FormatDb(_settings.CenterLiftDb);
            CrossfeedValue.Text = _settings.CrossfeedPercent.ToString("0", CultureInfo.InvariantCulture) + "%";
            StageWidthValue.Text = _settings.StageWidthPercent.ToString("0", CultureInfo.InvariantCulture) + "%";

            PeakBoostText.Text = FormatDb(analysis.PeakCurveDb);
            PeakBoostText.Foreground = analysis.PeakCurveDb > 0.05 ? FindBrush("AccentText") : FindBrush("TextPrimary");
            AverageText.Text = FormatDb(analysis.AverageCurveDb);
            HeadroomText.Text = analysis.Message;
            HeadroomText.Foreground = analysis.LimiterWillEngage ? FindBrush("WarnFill") : FindBrush("TextMuted");

            // The preamp that would put full-scale material exactly on the ceiling. When it
            // is negative the boost has run past the ceiling and the limiter will work.
            double suggestedTrim = analysis.SuggestedPreampDb;
            HeadroomValue.Text = FormatDb(suggestedTrim);
            HeadroomValue.Foreground = suggestedTrim < 0 ? FindBrush("WarnFill") : FindBrush("TextPrimary");

            bool headphones = _settings.Route == Route.Headphones;
            SpeakerDeck.Visibility = headphones ? Visibility.Collapsed : Visibility.Visible;
            HeadphoneDeck.Visibility = headphones ? Visibility.Visible : Visibility.Collapsed;
            SpeakerNote.Text = _settings.Route == Route.Surround51
                ? "On a 5.1 endpoint the mains hand everything under 80 Hz to the LFE channel, the centre is derived from the front pair, and the surrounds take the difference signal."
                : "Bass below 80 Hz is summed to one lane and shared by both speakers, which is what a 2.1 rig wants.";

            HeadphoneTargets.Target target = HeadphoneTargets.Find(_settings.Target);
            TargetNote.Text = target.Note + " Category curve, not a measured model target.";
            HandoffNote.Text = _settings.Handoff == SoftwareHandoff.DirectStereo
                ? "getEQd owns the stage: crossfeed and width below are active."
                : "Windows or the game engine owns spatialisation, so getEQd's crossfeed stands down and the EQ narrows to tone only.";
            CrossfeedSlider.IsEnabled = _settings.Handoff == SoftwareHandoff.DirectStereo;
            StageWidthSlider.IsEnabled = _settings.Handoff == SoftwareHandoff.DirectStereo;
            CrossfeedValue.Opacity = CrossfeedSlider.IsEnabled ? 1 : 0.45;
            StageWidthValue.Opacity = StageWidthSlider.IsEnabled ? 1 : 0.45;

            for (int index = 0; index < Bands.QuickCount; index++)
            {
                double gain = _settings.EffectiveGain(index);
                _bandValueTexts[index].Text = gain.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
                _bandValueTexts[index].Foreground = gain > 0.001 ? FindBrush("AccentText")
                    : gain < -0.001 ? FindBrush("CoolFill")
                    : FindBrush("TextMuted");
            }

            BandSummaryText.Text = _settings.Bypassed
                ? "BYPASSED"
                : _settings.CreateSnapshot().Summary.ToUpperInvariant();

            GraphRateText.Text = (GraphSampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";
            SourceText.Text = _engine.SourceName.ToUpperInvariant();

            StoragePathText.Text = ProfileStore.Folder;
            SaveStatusText.Text = "Curve is live. Save it to follow it later.";

            Curve.SampleRate = GraphSampleRate;
            Curve.InvalidateVisual();
        }

        private int GraphSampleRate => _engine.MixRate > 0 ? _engine.MixRate : 48000;

        private void ApplyPreset(string presetId)
        {
            _settings.ApplyPreset(presetId);
            RefreshAll();
        }

        private void SetRoute(Route route)
        {
            _settings.Route = route;
            RefreshAll();
        }

        private void OnBandSliderChanged(int band, double value)
        {
            if (_updating) return;

            // The slider can report values off the 0.5 dB grid when dragged by pixel.
            double snapped = Math.Round(value / Bands.GainStepDb, MidpointRounding.AwayFromZero) * Bands.GainStepDb;
            _settings.SetEffectiveGain(band, Math.Max(Bands.MinGainDb, Math.Min(Bands.MaxGainDb, snapped)));
            _settings.ActivePresetId = "custom";

            RefreshAll();
            HighlightBand(band);
        }

        private void HighlightBand(int band)
        {
            for (int index = 0; index < Bands.QuickCount; index++)
            {
                _bandCards[index].BorderBrush = index == band ? FindBrush("AccentEdge") : FindBrush("StrokeSoft");
            }

            if (band >= Bands.QuickCount && band < _advancedRows.Count)
            {
                _advancedRows[band].FrequencyBox.Focus();
            }
        }

        private void UseProbe(ProbeSignal signal)
        {
            bool alreadyActive = _engine.SourceName.Contains("Probe", StringComparison.OrdinalIgnoreCase);

            _engine.UseProbe(signal);
            _engine.Processor.PublishImmediate(_settings.CreateSnapshot());

            if (alreadyActive || !_engine.IsFileLoaded) _engine.Play();

            RefreshAll();
            UpdateProbeToggles();
        }

        private void OpenAudioFile()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Open an audio file",
                Filter = "Audio files|*.wav;*.mp3;*.aiff;*.aif;*.wma;*.m4a;*.flac;*.ogg|All files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _engine.LoadFile(dialog.FileName);
                _engine.Processor.PublishImmediate(_settings.CreateSnapshot());
                _engine.Play();
                UpdateProbeToggles();
                RefreshAll();
            }
            catch (Exception error)
            {
                SaveStatusText.Text = "That file could not be opened: " + error.Message;
            }
        }

        private void SeekToSlider()
        {
            TimeSpan duration = _engine.Duration;
            if (duration <= TimeSpan.Zero) return;

            _engine.Seek(TimeSpan.FromSeconds(PositionSlider.Value * duration.TotalSeconds));
            _lastPositionSeconds = _engine.Position.TotalSeconds;
        }

        // ================================================================ devices

        /// <summary>
        /// Sizes the endpoint list to a whole number of rows so a device name is never
        /// sliced through the middle by the scroll viewport.
        /// </summary>
        private void FitEndpointList()
        {
            try
            {
                if (_deviceCount <= 0) return;

                EndpointList.UpdateLayout();

                if (EndpointList.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem first
                    || first.ActualHeight <= 1)
                {
                    // Row heights are only real after a layout pass, which has not happened
                    // yet during construction. Try again once the queue reaches Loaded.
                    if (_fitAttempts++ < 4)
                    {
                        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(FitEndpointList));
                    }
                    return;
                }

                int rows = Math.Max(1, Math.Min(_deviceCount, 4));
                EndpointList.MaxHeight = Math.Ceiling(rows * first.ActualHeight) + 2;
            }
            catch
            {
                // Falls back to the height set in XAML.
            }
        }

        private void LoadOutputDevices()
        {
            IReadOnlyList<OutputDevice> devices = _engine.EnumerateDevices();
            OutputDevice? current = null;
            _deviceCount = devices.Count;

            foreach (OutputDevice device in devices)
            {
                if (device.Id == _engine.SelectedDeviceId) current = device;
            }

            current ??= _engine.DefaultDevice();

            _updating = true;
            try
            {
                EndpointList.Items.Clear();

                if (devices.Count == 0)
                {
                    EndpointList.Items.Add(new ListBoxItem
                    {
                        Content = TextBlockFor("No active playback devices found.", null),
                        IsEnabled = false
                    });
                    EndpointMeta.Text = "Plug in an output device, then rescan.";
                    return;
                }

                ListBoxItem? toSelect = null;

                foreach (OutputDevice device in devices)
                {
                    StackPanel content = new StackPanel();
                    content.Children.Add(TextBlockFor(device.Name, "CardTitle", 12.5));

                    TextBlock detail = TextBlockFor(device.ChannelLabel + " · " +
                        (device.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz", "NumericSmall");
                    detail.Margin = new Thickness(0, 3, 0, 0);
                    content.Children.Add(detail);

                    ListBoxItem item = new ListBoxItem
                    {
                        Content = content,
                        Tag = device.Id
                    };

                    EndpointList.Items.Add(item);

                    if (current != null && device.Id == current.Id) toSelect = item;
                }

                if (toSelect != null)
                {
                    EndpointList.SelectedItem = toSelect;
                    toSelect.BringIntoView();
                }
            }
            finally
            {
                _updating = false;
            }

            FitEndpointList();

            if (current != null)
            {
                _engine.SelectDevice(current.Id);
                EndpointMeta.Text = "Streaming " + _engine.MixChannels + " ch at " +
                    (_engine.MixRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) +
                    " kHz · shared mode · 60 ms buffer";
                Curve.SampleRate = GraphSampleRate;
            }
        }

        // ================================================================ profiles

        private void LoadStoredProfiles()
        {
            _measurements = ProfileStore.LoadMeasurements();
            _listening = ProfileStore.LoadListening();
            _selectedMeasurement = _measurements.Count > 0 ? _measurements[0] : null;
            BuildSavedProfileList();
            BuildMeasurementList();
            UpdateMeasuredLab();
        }

        private void SaveListeningProfile()
        {
            string name = ProfileNameBox.Text.Trim();

            if (name.Length == 0)
            {
                SaveStatusText.Text = "Give this sound a name first.";
                ProfileNameBox.Focus();
                return;
            }

            _listening.RemoveAll(profile => string.Equals(profile.Name, name, StringComparison.OrdinalIgnoreCase));
            _listening.Insert(0, new ListeningProfile
            {
                Name = name,
                SavedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Settings = ProfileMapping.ToSettings(_settings)
            });

            try
            {
                ProfileStore.SaveListening(_listening);
                ProfileNameBox.Text = string.Empty;
                SaveStatusText.Text = "Saved " + name + ". Follow it again from Your profiles.";
            }
            catch (Exception error)
            {
                SaveStatusText.Text = "Could not write the profile: " + error.Message;
            }

            BuildSavedProfileList();
        }

        private void FollowListeningProfile(ListeningProfile profile)
        {
            ProfileMapping.Apply(profile.Settings, _settings);
            RefreshAll();
            SaveStatusText.Text = "Following " + profile.Name + ". Adjust it, then save again to keep the change.";
        }

        private void DeleteListeningProfile(ListeningProfile profile)
        {
            _listening.Remove(profile);
            ProfileStore.SaveListening(_listening);
            BuildSavedProfileList();
            SaveStatusText.Text = "Removed " + profile.Name + " from this machine.";
        }

        private void ImportMeasurement()
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Import a measured response",
                Filter = "getEQd profile (*.json)|*.json|All files|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                MeasurementProfile profile = MeasurementProfileIO.Parse(json);

                _measurements.RemoveAll(item => string.Equals(item.Model, profile.Model, StringComparison.OrdinalIgnoreCase));
                _measurements.Insert(0, profile);
                _selectedMeasurement = profile;
                ProfileStore.SaveMeasurements(_measurements);
                BuildMeasurementList();
                UpdateMeasuredLab();

                ImportStatusText.Text = "Imported " + profile.Model + " with " + profile.PointCount +
                    " points. The source file was not modified.";
                ImportStatusText.Foreground = FindBrush("TextMuted");
            }
            catch (Exception error)
            {
                ImportStatusText.Text = "Import not accepted: " + error.Message;
                ImportStatusText.Foreground = FindBrush("BadFill");
            }
        }

        private void ExportTemplate()
        {
            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Save the measurement template",
                FileName = "getEQd-profile-template.json",
                Filter = "JSON (*.json)|*.json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, MeasurementProfileIO.TemplateJson());
                ImportStatusText.Text = "Template written to " + Path.GetFileName(dialog.FileName) +
                    ". Fill in your measurement and import it back.";
                ImportStatusText.Foreground = FindBrush("TextMuted");
            }
            catch (Exception error)
            {
                ImportStatusText.Text = "Could not write the template: " + error.Message;
                ImportStatusText.Foreground = FindBrush("BadFill");
            }
        }

        private void AuditionMeasurement(MeasurementProfile profile)
        {
            _selectedMeasurement = profile;
            double[] gains = MeasurementProfileIO.ToBandGains(profile);

            for (int index = 0; index < Bands.QuickCount; index++) _settings.SetEffectiveGain(index, gains[index]);

            _settings.ActivePresetId = "custom";
            RefreshAll();
            UpdateMeasuredLab();

            ImportStatusText.Text = profile.Model + " correction auditioned on the six quick bands. The stored measurement is unchanged.";
            ImportStatusText.Foreground = FindBrush("TextMuted");
        }

        private void OpenMeasurementLab(MeasurementProfile profile)
        {
            _selectedMeasurement = profile;
            UpdateMeasuredLab();
            LabStatusText.Text = profile.Model + " is open in the lab. Review the raw, target and correction lines before applying it.";
            LabStatusText.Foreground = FindBrush("TextMuted");
        }

        private void ApplyMeasurementCalibration()
        {
            if (_selectedMeasurement == null) return;

            double[] corrections = MeasurementProfileIO.ToBandGains(_selectedMeasurement);
            for (int index = 0; index < Bands.QuickCount; index++)
            {
                _settings.SetEffectiveGain(index, corrections[index]);
            }

            // A measured model correction replaces the category trim. Leaving both on
            // would double-correct the same headphone response.
            if (_settings.Route == Route.Headphones) _settings.TargetTrimEnabled = false;
            _settings.ActivePresetId = "custom";

            HeadroomAnalysis analysis = Analysis.Evaluate(_settings.CreateSnapshot(), GraphSampleRate);
            double safePreamp = Math.Min(_settings.PreampDb, analysis.SuggestedPreampDb);
            safePreamp = Math.Max(-12, Math.Min(11, Math.Round(safePreamp * 2, MidpointRounding.AwayFromZero) / 2));
            _settings.PreampDb = safePreamp;

            RefreshAll();
            UpdateMeasuredLab();
            LabStatusText.Text = _selectedMeasurement.Model + " correction applied to the six quick bands. " +
                "The source stays unchanged; preamp is " + FormatDb(_settings.PreampDb) + " for safe headroom.";
            LabStatusText.Foreground = FindBrush("TextMuted");
        }

        private void ExportMeasurementAudit()
        {
            if (_selectedMeasurement == null) return;

            SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export calibration audit",
                FileName = _selectedMeasurement.Model.Replace(' ', '-') + "-geteqd-audit.json",
                Filter = "JSON (*.json)|*.json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                MeasurementProfileIO.WriteAudit(_selectedMeasurement, dialog.FileName);
                LabStatusText.Text = "Audit written to " + Path.GetFileName(dialog.FileName) + ". Raw data, target, correction and metadata are included.";
                LabStatusText.Foreground = FindBrush("TextMuted");
            }
            catch (Exception error)
            {
                LabStatusText.Text = "Could not write the audit: " + error.Message;
                LabStatusText.Foreground = FindBrush("BadFill");
            }
        }

        private void UpdateMeasuredLab()
        {
            ResponseLab.Profile = _selectedMeasurement;
            bool hasProfile = _selectedMeasurement != null;
            ApplyCorrectionButton.IsEnabled = hasProfile;
            ExportAuditButton.IsEnabled = hasProfile;

            if (!hasProfile)
            {
                LabModeText.Text = "NO MODEL SELECTED";
                LabProfileMetaText.Text = "Choose an imported measurement to inspect its provenance and correction.";
                LabRawPeakText.Text = "—";
                LabCorrectionPeakText.Text = "—";
                LabPreampText.Text = "—";
                LabAuditText.Text = "No audit selected.";
                return;
            }

            MeasurementProfile profile = _selectedMeasurement!;
            double rawPeak = ProfilePeak(profile, raw: true);
            double correctionPeak = ProfilePeak(profile, raw: false);
            double safePreamp = -1 - correctionPeak;

            LabModeText.Text = profile.ResponseTypeLabel.ToUpperInvariant();
            LabProfileMetaText.Text = profile.Model + " · " + profile.Source + " · " + profile.MeasuredAtLabel +
                "\nRig: " + profile.RigLabel + " · Target: " + profile.TargetLabel;
            LabRawPeakText.Text = FormatDb(rawPeak);
            LabCorrectionPeakText.Text = FormatDb(correctionPeak);
            LabPreampText.Text = FormatDb(Math.Max(-12, Math.Min(11, safePreamp)));
            LabAuditText.Text = profile.PointCount + " points · " + profile.ResponseTypeLabel +
                " · correction = " + (profile.IsRaw ? "target minus raw response" : "imported gainDb") +
                ". Use Export audit to keep the decision trail with the profile.";
        }

        private static double ProfilePeak(MeasurementProfile profile, bool raw)
        {
            double peak = double.NegativeInfinity;
            const int samples = 240;
            for (int index = 0; index <= samples; index++)
            {
                double frequency = 20 * Math.Pow(1000, index / (double)samples);
                double value = raw
                    ? MeasurementProfileIO.RawDbAt(profile, frequency)
                    : MeasurementProfileIO.CorrectionDbAt(profile, frequency);
                if (value > peak) peak = value;
            }
            return double.IsNegativeInfinity(peak) ? 0 : peak;
        }

        private void BuildSavedProfileList()
        {
            SavedProfileList.Children.Clear();

            if (_listening.Count == 0)
            {
                SavedProfileList.Children.Add(EmptyNote(
                    "Nothing saved yet.",
                    "Name a sound above and save it. Every band, the route, the headphone controls and the ceiling travel together."));
                return;
            }

            foreach (ListeningProfile profile in _listening)
            {
                ListeningProfile captured = profile;

                TextBlock name = TextBlockFor(profile.Name, "CardTitle", 12.5);
                TextBlock detail = TextBlockFor(profile.Settings.Route + " · " + profile.SavedAtLabel, "NumericSmall");
                detail.Margin = new Thickness(0, 3, 0, 0);

                TextBlock bands = TextBlockFor(profile.BandSummary, "NumericSmall");
                bands.Margin = new Thickness(0, 5, 0, 0);
                bands.TextWrapping = TextWrapping.Wrap;
                bands.Foreground = FindBrush("TextMuted");

                StackPanel buttons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 8, 0, 0)
                };

                Button follow = new Button
                {
                    Content = "Follow",
                    Style = FindStyle("QuietButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11.5
                };
                follow.Click += (_, _) => FollowListeningProfile(captured);

                Button delete = new Button
                {
                    Content = "Delete",
                    Style = FindStyle("QuietButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11.5,
                    Margin = new Thickness(6, 0, 0, 0)
                };
                delete.Click += (_, _) => DeleteListeningProfile(captured);

                buttons.Children.Add(follow);
                buttons.Children.Add(delete);

                StackPanel content = new StackPanel();
                content.Children.Add(name);
                content.Children.Add(detail);
                content.Children.Add(bands);
                content.Children.Add(buttons);

                SavedProfileList.Children.Add(new Border
                {
                    Style = FindStyle("Card"),
                    Background = FindBrush("SurfaceSunken"),
                    Padding = new Thickness(11, 10, 11, 10),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = content
                });
            }
        }

        private void BuildMeasurementList()
        {
            MeasurementList.Children.Clear();

            if (_measurements.Count == 0)
            {
                MeasurementList.Children.Add(EmptyNote(
                    "No measurements imported.",
                    "Import a frequency response to audition it across the six bands without touching the source data."));
                return;
            }

            foreach (MeasurementProfile profile in _measurements)
            {
                MeasurementProfile captured = profile;

                TextBlock name = TextBlockFor(profile.Model, "CardTitle", 12.5);
                TextBlock detail = TextBlockFor(profile.Source + " · " + profile.MeasuredAtLabel +
                    "\n" + profile.RigLabel + " · " + profile.TargetLabel + " · " + profile.ResponseTypeLabel, "NumericSmall");
                detail.Margin = new Thickness(0, 3, 0, 0);

                TextBlock points = TextBlockFor(profile.PointCount + " points", "NumericSmall");
                points.Margin = new Thickness(0, 5, 0, 0);
                points.Foreground = FindBrush("TextMuted");

                Button openLab = new Button
                {
                    Content = "Open in lab",
                    Style = FindStyle("QuietButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                openLab.Click += (_, _) => OpenMeasurementLab(captured);

                Button audition = new Button
                {
                    Content = "Audition on the bands",
                    Style = FindStyle("QuietButton"),
                    Padding = new Thickness(10, 5, 10, 5),
                    FontSize = 11.5,
                    Margin = new Thickness(6, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                audition.Click += (_, _) => AuditionMeasurement(captured);

                StackPanel actions = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 8, 0, 0)
                };
                actions.Children.Add(openLab);
                actions.Children.Add(audition);

                StackPanel content = new StackPanel();
                content.Children.Add(name);
                content.Children.Add(detail);
                content.Children.Add(points);
                if (!string.IsNullOrWhiteSpace(profile.Notes))
                {
                    TextBlock notes = TextBlockFor(profile.Notes, null, 11.5);
                    notes.TextWrapping = TextWrapping.Wrap;
                    notes.Foreground = FindBrush("TextMuted");
                    notes.Margin = new Thickness(0, 5, 0, 0);
                    content.Children.Add(notes);
                }
                content.Children.Add(actions);

                MeasurementList.Children.Add(new Border
                {
                    Style = FindStyle("Card"),
                    Background = FindBrush("SurfaceSunken"),
                    Padding = new Thickness(11, 10, 11, 10),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = content
                });
            }
        }

        private static StackPanel EmptyNote(string headline, string detail)
        {
            StackPanel panel = new StackPanel();

            TextBlock title = new TextBlock
            {
                Text = headline,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            };

            TextBlock body = new TextBlock
            {
                Text = detail,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
                Opacity = 0.75
            };

            panel.Children.Add(title);
            panel.Children.Add(body);
            return panel;
        }

        private void UpdateNamePlaceholder()
        {
            ProfileNamePlaceholder.Visibility = ProfileNameBox.Text.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void OpenStorageFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = ProfileStore.Folder,
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                SaveStatusText.Text = "Could not open the folder: " + error.Message;
            }
        }

        // ================================================================ tick

        private void OnTick(object? sender, EventArgs e)
        {
            _engine.ReadMeters(out double left, out double right, out double momentary, out double reductionDb);
            bool playing = _engine.State == PlaybackState.Playing;

            Meter.SetLevels(left, right, reductionDb, playing);

            PeakReadoutText.Text = "L " + MeterBar.FormatDb(AmplitudeToDb(left)) +
                                   "  R " + MeterBar.FormatDb(AmplitudeToDb(right));
            ReductionReadoutText.Text = "CEILING " + reductionDb.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
            ReductionReadoutText.Foreground = reductionDb < -0.1 ? FindBrush("CoolFill") : FindBrush("TextMuted");

            if (playing)
            {
                _engine.Processor.CopySpectrumWindow(_spectrumScratch);
                _analyzer.Compute(_spectrumScratch, GraphSampleRate);
                Curve.SetSpectrum(_analyzer.MagnitudesDb, GraphSampleRate, true);
                Curve.InvalidateVisual();
            }
            else if (Curve.HasSpectrum)
            {
                Curve.SetSpectrum(Array.Empty<float>(), GraphSampleRate, false);
                Curve.InvalidateVisual();
            }

            TimeSpan duration = _engine.Duration;
            if (duration > TimeSpan.Zero)
            {
                TimeSpan position = _engine.Position;

                if (!_userSeeking && Math.Abs(position.TotalSeconds - _lastPositionSeconds) > 0.02)
                {
                    _updating = true;
                    try
                    {
                        PositionSlider.Value = Math.Max(0, Math.Min(1, position.TotalSeconds / duration.TotalSeconds));
                    }
                    finally
                    {
                        _updating = false;
                    }
                    _lastPositionSeconds = position.TotalSeconds;
                }

                PositionText.Text = FormatTime(position) + " / " + FormatTime(duration);
            }
            else
            {
                PositionText.Text = "— / —";
            }

            PlayButton.Content = playing ? "❚❚" : "▶";

            (string label, string color) = _engine.State switch
            {
                PlaybackState.Playing => ("Processing", "GoodFill"),
                PlaybackState.Paused => ("Paused", "AccentFill"),
                PlaybackState.Ended => ("Ended", "TextMuted"),
                PlaybackState.Faulted => ("Fault", "BadFill"),
                _ => ("Idle", "TextMuted")
            };

            StatusText.Text = label;
            StatusDot.Fill = FindBrush(color);
            DetailText.Text = _engine.StatusDetail.ToUpperInvariant();
            SourceText.Text = _engine.SourceName.ToUpperInvariant();
        }

        private static double AmplitudeToDb(double amplitude)
        {
            double magnitude = Math.Abs(amplitude);
            return magnitude < 1e-6 ? -120 : 20 * Math.Log10(magnitude);
        }

        private static string FormatTime(TimeSpan value)
        {
            return value.TotalHours >= 1
                ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : value.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }

        private void UpdateProbeToggles()
        {
            _updating = true;
            try
            {
                ProbePinkToggle.IsChecked = _engine.SourceName.Contains("pink", StringComparison.OrdinalIgnoreCase);
                ProbeToneToggle.IsChecked = _engine.SourceName.Contains("1 kHz", StringComparison.OrdinalIgnoreCase);
                ProbeSweepToggle.IsChecked = _engine.SourceName.Contains("sweep", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _updating = false;
            }
        }

        // ================================================================ helpers

        private static void SelectExclusive(List<ToggleButton> buttons, string tag)
        {
            foreach (ToggleButton button in buttons)
            {
                button.IsChecked = button.Tag is string value && string.Equals(value, tag, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string FormatDb(double value)
        {
            if (value <= -99) return "−∞ dB";
            return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " dB";
        }

        private Style? FindStyle(string key) => TryFindResource(key) as Style;

        private Brush FindBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

        private TextBlock TextBlockFor(string text, string? styleKey = null, double? fontSize = null)
        {
            TextBlock block = new TextBlock { Text = text };

            if (!string.IsNullOrEmpty(styleKey))
            {
                Style? style = FindStyle(styleKey!);
                if (style != null) block.Style = style;
            }

            if (fontSize.HasValue) block.FontSize = fontSize.Value;
            return block;
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Space is the transport key, but only when a text field or a button does not
            // already own it.
            if (e.Key == Key.Space && Keyboard.FocusedElement is not TextBox && Keyboard.FocusedElement is not ButtonBase)
            {
                _engine.Toggle();
                e.Handled = true;
            }
        }

        /// <summary>Used by the headless preview renderer to stage a representative state.</summary>
        public void StageForPreview(string? presetId, Route? route, bool playing)
        {
            if (presetId != null) _settings.ApplyPreset(presetId);
            if (route.HasValue)
            {
                _settings.Route = route.Value;
                if (route.Value == Route.Headphones) _settings.Target = HeadphoneTarget.ClosedBackPunch;
            }

            if (playing)
            {
                _settings.PreampDb = -2.5;
                _settings.CeilingDb = -1.0;
            }

            RefreshAll();

            if (playing)
            {
                // A representative spectrum so the preview shows the analyzer in use.
                float[] synthetic = new float[_analyzer.BinCount];
                Random random = new Random(7);
                for (int bin = 1; bin < synthetic.Length; bin++)
                {
                    double frequency = _analyzer.BinFrequency(bin, 48000);
                    double tilt = -4.5 * Math.Log10(Math.Max(20, frequency) / 20.0);
                    double ripple = random.NextDouble() * 6 - 3;
                    synthetic[bin] = (float)Math.Max(-96, Math.Min(-6, tilt + ripple - 12));
                }
                Curve.SetSpectrum(synthetic, 48000, true);
            }
        }
    }
}
