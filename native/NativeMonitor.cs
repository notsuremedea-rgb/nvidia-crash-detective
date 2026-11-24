using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NvidiaCrashDetective
{
    internal static class Fields
    {
        public static readonly string[] All = {
            "name", "driver_version", "clocks.current.graphics", "clocks.current.memory",
            "temperature.gpu", "temperature.memory", "power.draw", "power.limit",
            "memory.used", "memory.total", "utilization.gpu", "utilization.memory",
            "utilization.encoder", "utilization.decoder", "pcie.link.gen.gpucurrent",
            "pcie.link.gen.max", "pcie.link.width.current", "pcie.link.width.max", "pstate",
            "clocks_event_reasons.active"
        };
    }

    internal sealed class Sample
    {
        public DateTime Time = DateTime.Now;
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public string Error = "";

        public double? Number(string key)
        {
            string value;
            double number;
            if (Values.TryGetValue(key, out value) &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                return number;
            return null;
        }
    }

    internal static class Telemetry
    {
        public static Sample Read()
        {
            var sample = new Sample();
            try
            {
                var info = new ProcessStartInfo {
                    FileName = "nvidia-smi.exe",
                    Arguments = "--query-gpu=" + string.Join(",", Fields.All) + " --format=csv,noheader,nounits --id=0",
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                };
                using (var process = Process.Start(info))
                {
                    if (!process.WaitForExit(3000))
                    {
                        try { process.Kill(); } catch { }
                        sample.Error = "Telemetry query timed out";
                        return sample;
                    }
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    string error = process.StandardError.ReadToEnd().Trim();
                    if (process.ExitCode != 0)
                    {
                        sample.Error = "NVIDIA query failed: " + (error.Length > 0 ? error : output);
                        return sample;
                    }
                    List<string> row = ParseCsv(output);
                    if (row.Count != Fields.All.Length)
                    {
                        sample.Error = string.Format("Incomplete NVIDIA response ({0}/{1} fields)", row.Count, Fields.All.Length);
                        return sample;
                    }
                    for (int i = 0; i < Fields.All.Length; i++)
                        sample.Values[Fields.All[i]] = Clean(row[i]);
                }
            }
            catch (Exception ex) { sample.Error = "Telemetry unavailable: " + ex.Message; }
            return sample;
        }

        public static string Warning(Sample sample)
        {
            if (sample.Error.Length > 0) return sample.Error;
            double? temp = sample.Number("temperature.gpu");
            double? clock = sample.Number("clocks.current.graphics");
            if (temp.HasValue && (temp.Value < 1 || temp.Value > 115))
                return "Implausible GPU temperature: " + temp.Value.ToString("0.##") + " C";
            if (clock.HasValue && (clock.Value < 0 || clock.Value > 6000))
                return "Implausible GPU clock: " + clock.Value.ToString("0.##") + " MHz";
            return "";
        }

        private static string Clean(string value)
        {
            value = value.Trim();
            string lower = value.ToLowerInvariant();
            return lower == "n/a" || lower == "[not supported]" || lower == "not supported" ? "" : value;
        }

        private static List<string> ParseCsv(string line)
        {
            var result = new List<string>();
            var value = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"') { value.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { result.Add(value.ToString().Trim()); value.Clear(); }
                else value.Append(c);
            }
            result.Add(value.ToString().Trim());
            return result;
        }
    }

    internal sealed class MetricCard
    {
        public string Key;
        public string Unit;
        public TextBlock Value;
        public TextBlock Range;
        public double? Min;
        public double? Max;
    }

    internal sealed class HistoryGraph : Canvas
    {
        public IList<Sample> Samples;
        public HistoryGraph() { Background = Brush("#151B18"); ClipToBounds = true; }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            var grid = new Pen(Brush("#26312B"), 1);
            for (int i = 1; i <= 3; i++) dc.DrawLine(grid, new Point(12, 28 + (h - 42) * i / 4), new Point(w - 12, 28 + (h - 42) * i / 4));
            DrawText(dc, "LAST 60 SECONDS", 14, 8, "#9EAAA3", 11);
            DrawSeries(dc, "utilization.gpu", 100, "#76B900");
            DrawSeries(dc, "temperature.gpu", 110, "#F4B942");
            DrawSeries(dc, "power.draw", 200, "#4BD5D0");
            DrawText(dc, "GPU %", w - 160, 8, "#76B900", 10);
            DrawText(dc, "TEMP °C", w - 110, 8, "#F4B942", 10);
            DrawText(dc, "POWER W", w - 52, 8, "#4BD5D0", 10);
        }

        private void DrawSeries(DrawingContext dc, string key, double ceiling, string colour)
        {
            if (Samples == null || Samples.Count < 2) return;
            var geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                bool begun = false;
                for (int i = 0; i < Samples.Count; i++)
                {
                    double? value = Samples[i].Number(key);
                    if (!value.HasValue) continue;
                    var point = new Point(12 + i * (ActualWidth - 24) / 59.0,
                        ActualHeight - 12 - Math.Min(value.Value / ceiling, 1) * (ActualHeight - 42));
                    if (!begun) { context.BeginFigure(point, false, false); begun = true; }
                    else context.LineTo(point, true, false);
                }
            }
            dc.DrawGeometry(null, new Pen(Brush(colour), 2), geometry);
        }

        private static void DrawText(DrawingContext dc, string text, double x, double y, string colour, double size)
        {
            dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"), size, Brush(colour)), new Point(x, y));
        }

        private static Brush Brush(string colour) { return (Brush)new BrushConverter().ConvertFromString(colour); }
        public void Refresh() { InvalidateVisual(); }
    }

    internal sealed class MainWindow : Window
    {
        private static readonly Brush Bg = B("#0B0F0D"), Card = B("#151B18"), Text = B("#F1F7F3");
        private static readonly Brush Muted = B("#9EAAA3"), Green = B("#76B900"), Red = B("#FF5C5C"), Amber = B("#F4B942");
        private readonly List<Sample> samples = new List<Sample>();
        private readonly Dictionary<string, MetricCard> metrics = new Dictionary<string, MetricCard>();
        private readonly DispatcherTimer sampleTimer = new DispatcherTimer(), watchdog = new DispatcherTimer();
        private readonly DateTime started = DateTime.UtcNow;
        private DateTime? lastSuccess;
        private bool queryRunning, incidentLatched, loggingFailed, isClosed;
        private StreamWriter log;
        private string pendingEvent = "";
        private TextBlock gpuLabel, status, details, logPath;
        private Button logButton;
        private HistoryGraph graph;

        public MainWindow()
        {
            Title = "NVIDIA Crash Detective"; Width = 1180; Height = 780; MinWidth = 900; MinHeight = 650;
            Background = Bg; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Content = BuildUi();
            sampleTimer.Interval = TimeSpan.FromSeconds(1); sampleTimer.Tick += async delegate { await RequestSample(); };
            watchdog.Interval = TimeSpan.FromMilliseconds(250); watchdog.Tick += delegate { GuardLogging(Watchdog); };
            Loaded += async delegate { sampleTimer.Start(); watchdog.Start(); await RequestSample(); };
            Closed += delegate { isClosed=true; sampleTimer.Stop(); watchdog.Stop(); GuardLogging(delegate { if (log != null) log.Dispose(); }); };
        }

        private UIElement BuildUi()
        {
            var root = new DockPanel { Margin = new Thickness(24, 18, 24, 18) };
            var top = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.Children.Add(T("NVIDIA CRASH DETECTIVE", 25, Text, FontWeights.SemiBold));
            gpuLabel = T("Connecting…", 13, Muted); gpuLabel.Margin = new Thickness(20, 8, 0, 0); Grid.SetColumn(gpuLabel, 1); top.Children.Add(gpuLabel);
            status = T("● CONNECTING", 13, Amber, FontWeights.SemiBold); status.Margin = new Thickness(0, 8, 0, 0); Grid.SetColumn(status, 2); top.Children.Add(status);
            DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

            var cards = new UniformGrid { Columns = 4, Rows = 2, Margin = new Thickness(-6, 0, -6, 6) };
            AddMetric(cards, "GPU CLOCK", "clocks.current.graphics", "MHz", Green);
            AddMetric(cards, "MEMORY CLOCK", "clocks.current.memory", "MHz", B("#4BD5D0"));
            AddMetric(cards, "GPU TEMP", "temperature.gpu", "°C", Amber);
            AddMetric(cards, "MEM JUNCTION", "temperature.memory", "°C", Amber);
            AddMetric(cards, "POWER", "power.draw", "W", Green);
            AddMetric(cards, "GPU LOAD", "utilization.gpu", "%", B("#4BD5D0"));
            AddMetric(cards, "VRAM", "memory.used", "MiB", Green);
            AddMetric(cards, "ENC / DEC", "media", "%", B("#4BD5D0"));
            DockPanel.SetDock(cards, Dock.Top); root.Children.Add(cards);

            var controls = new Grid { Margin = new Thickness(0, 12, 0, 0), Height = 52 };
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            logButton = Button("START LOG", Green, B("#071000")); logButton.Click += delegate { GuardLogging(ToggleLog); }; controls.Children.Add(logButton);
            var mark = Button("MARK EVENT", Card, Text); mark.Margin = new Thickness(10, 0, 0, 0); mark.Click += delegate { MarkEvent(); }; Grid.SetColumn(mark, 1); controls.Children.Add(mark);
            logPath = T("Auto incidents → " + LogDirectory(), 12, Muted); logPath.Margin = new Thickness(14, 13, 0, 0); Grid.SetColumn(logPath, 2); controls.Children.Add(logPath);
            var safe = T("Monitor only • no tuning controls", 11, Muted); safe.Margin = new Thickness(0, 14, 0, 0); Grid.SetColumn(safe, 3); controls.Children.Add(safe);
            DockPanel.SetDock(controls, Dock.Bottom); root.Children.Add(controls);

            var middle = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            middle.ColumnDefinitions.Add(new ColumnDefinition()); middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(275) });
            graph = new HistoryGraph { Samples = samples, Margin = new Thickness(0, 0, 12, 0) };
            middle.Children.Add(Border(graph));
            var detailStack = new StackPanel { Margin = new Thickness(16, 14, 12, 12) };
            detailStack.Children.Add(T("LINK & LIMITS", 14, Text, FontWeights.SemiBold));
            details = T("Waiting for telemetry…", 13, Muted); details.FontFamily = new FontFamily("Consolas"); details.Margin = new Thickness(0, 12, 0, 0); details.TextWrapping = TextWrapping.Wrap; detailStack.Children.Add(details);
            var detailBorder = Border(detailStack); Grid.SetColumn(detailBorder, 1); middle.Children.Add(detailBorder);
            root.Children.Add(middle); return root;
        }

        private void AddMetric(Panel panel, string title, string key, string unit, Brush colour)
        {
            var stack = new StackPanel { Margin = new Thickness(15, 11, 10, 10) };
            stack.Children.Add(T(title, 12, Muted, FontWeights.SemiBold));
            var value = T("—", 31, colour, FontWeights.SemiBold);
            var range = T("MIN —   MAX —", 10, Muted); stack.Children.Add(value); stack.Children.Add(range);
            var border = Border(stack); border.Margin = new Thickness(6); panel.Children.Add(border);
            metrics[key] = new MetricCard { Key = key, Unit = unit, Value = value, Range = range };
        }

        private async Task RequestSample()
        {
            if (queryRunning) return; queryRunning = true;
            Sample sample = await Task.Run(() => Telemetry.Read()); queryRunning = false; if(!isClosed) GuardLogging(delegate { Consume(sample); });
        }

        private void Consume(Sample sample)
        {
            string warning = Telemetry.Warning(sample);
            if (warning.Length > 0) { RecordIncident("GPU TELEMETRY INVALID: " + warning, sample); return; }
            lastSuccess = DateTime.UtcNow; incidentLatched = false;
            samples.Add(sample); if (samples.Count > 60) samples.RemoveAt(0);
            gpuLabel.Text = V(sample, "name", "NVIDIA GPU") + "  •  Driver " + V(sample, "driver_version", "—");
            status.Text = loggingFailed ? "● LOG SAVE FAILED" : log != null ? "● LOGGING" : "● LIVE"; status.Foreground = loggingFailed ? Red : Green;
            foreach (MetricCard card in metrics.Values) UpdateMetric(card, sample);
            details.Text = string.Format("PCIe current   Gen {0} x{1}\nPCIe maximum   Gen {2} x{3}\n\nPerformance state   {4}\nPower limit         {5} W\n\nActive limiter flags\n{6}",
                V(sample, "pcie.link.gen.gpucurrent", "—"), V(sample, "pcie.link.width.current", "—"),
                V(sample, "pcie.link.gen.max", "—"), V(sample, "pcie.link.width.max", "—"), V(sample, "pstate", "—"),
                V(sample, "power.limit", "—"), V(sample, "clocks_event_reasons.active", "None reported"));
            if (log != null) { WriteRow(sample, pendingEvent); pendingEvent = ""; }
            graph.Refresh();
        }

        private void UpdateMetric(MetricCard card, Sample sample)
        {
            string display; double? number;
            if (card.Key == "media")
            {
                display = V(sample, "utilization.encoder", "—") + " / " + V(sample, "utilization.decoder", "—");
                double? enc = sample.Number("utilization.encoder"), dec = sample.Number("utilization.decoder");
                number = enc.HasValue && dec.HasValue ? Math.Max(enc.Value, dec.Value) : (enc ?? dec);
            }
            else if (card.Key == "memory.used") { display = V(sample, card.Key, "—") + " / " + V(sample, "memory.total", "—"); number = sample.Number(card.Key); }
            else { display = V(sample, card.Key, "N/A"); number = sample.Number(card.Key); }
            card.Value.Text = display + (display == "N/A" || display == "—" ? "" : " " + card.Unit);
            if (!number.HasValue) return;
            card.Min = card.Min.HasValue ? Math.Min(card.Min.Value, number.Value) : number;
            card.Max = card.Max.HasValue ? Math.Max(card.Max.Value, number.Value) : number;
            card.Range.Text = string.Format("MIN {0:g} {2}   MAX {1:g} {2}", card.Min, card.Max, card.Unit);
        }

        private void Watchdog()
        {
            DateTime baseline = lastSuccess ?? started;
            if ((DateTime.UtcNow - baseline).TotalSeconds > 6) RecordIncident("GPU TELEMETRY STALL DETECTED", null);
        }

        private void RecordIncident(string message, Sample sample)
        {
            if (incidentLatched) return; incidentLatched = true; status.Text = "● " + message; status.Foreground = Red;
            if (log == null) StartLog("incident");
            WriteRow(sample ?? new Sample(), message);
            logPath.Text = "Incident saved and flushed: " + ((FileStream)log.BaseStream).Name;
        }

        private void GuardLogging(Action action)
        {
            try { action(); }
            catch (IOException ex) { LogFailure(ex); }
            catch (UnauthorizedAccessException ex) { LogFailure(ex); }
        }
        private void LogFailure(Exception ex)
        {
            var failed=log; log=null; loggingFailed=true;
            try { if(failed!=null) failed.Dispose(); } catch { }
            status.Text="● LOG SAVE FAILED"; status.Foreground=Red;
            logPath.Text="Recording stopped: "+ex.Message+" Resolve folder/disk access, then press START LOG.";
            logButton.Content="START LOG"; logButton.Background=Green; logButton.Foreground=B("#071000");
        }
        private void ToggleLog()
        {
            if (log != null)
            {
                string path = ((FileStream)log.BaseStream).Name; log.Dispose(); log = null;
                logButton.Content = "START LOG"; logButton.Background = Green; logButton.Foreground = B("#071000"); logPath.Text = "Saved: " + path; return;
            }
            StartLog("session");
        }

        private void StartLog(string kind)
        {
            string directory = LogDirectory();
            Directory.CreateDirectory(directory);
            string path = System.IO.Path.Combine(directory, "gpu-" + kind + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0,8) + ".csv");
            log = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(true));
            log.WriteLine("local_time," + string.Join(",", Fields.All) + ",event"); log.Flush();
            foreach (Sample sample in samples) WriteRow(sample, "PREBUFFER");
            loggingFailed=false; logButton.Content = "STOP LOG"; logButton.Background = Red; logButton.Foreground = Text; logPath.Text = path;
        }

        private static string LogDirectory()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop)) desktop = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(desktop, "NVDetective", "Logs");
        }

        private void MarkEvent()
        {
            if (log == null) { MessageBox.Show(this, "Start a CSV log, then use Mark Event during a glitch.", "Start logging first", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            pendingEvent = "USER_MARK"; logPath.Text = "Event marker queued for next sample";
        }

        private void WriteRow(Sample sample, string eventText)
        {
            var row = new List<string> { Csv(sample.Time.ToString("yyyy-MM-ddTHH:mm:ss.fff")) };
            foreach (string field in Fields.All) row.Add(Csv(V(sample, field, "")));
            row.Add(Csv(eventText)); log.WriteLine(string.Join(",", row)); log.Flush();
        }

        private static string V(Sample sample, string key, string fallback) { string value; return sample.Values.TryGetValue(key, out value) && value.Length > 0 ? value : fallback; }
        private static string Csv(string value) { return value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value; }
        private static Border Border(UIElement child) { return new Border { Child = child, Background = Card, BorderBrush = B("#26312B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(2) }; }
        private static TextBlock T(string text, double size, Brush colour, FontWeight? weight = null) { return new TextBlock { Text = text, FontSize = size, Foreground = colour, FontFamily = new FontFamily("Segoe UI"), FontWeight = weight ?? FontWeights.Normal }; }
        private static Button Button(string text, Brush bg, Brush fg) { return new Button { Content = text, Background = bg, Foreground = fg, BorderThickness = new Thickness(0), Padding = new Thickness(22, 9, 22, 9), FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand }; }
        private static Brush B(string colour) { return (Brush)new BrushConverter().ConvertFromString(colour); }
    }

    internal static class Program
    {
        [STAThread]
        public static void Main()
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            app.Run(new MainWindow());
        }
    }
}
