using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Compunet.YoloV8;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ScottPlot;
using ScottPlot.WinForms;

// --- ALIASES TO RESOLVE AMBIGUITIES ---
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Label = System.Windows.Forms.Label;
using Orientation = System.Windows.Forms.Orientation;

namespace AiVideoAuditor
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AuditorForm());
        }
    }

    public class AuditorForm : Form
    {
        // ---------------------------------------------------------------------------
        // CONFIGURATION
        // ---------------------------------------------------------------------------
        const string GeminiApiKey = "api-key";
        const string VideoPath = "15333293_640_360_50fps.mp4";
        const string YoloModelPath = "yolov8n.onnx";
        const int ReportingIntervalSeconds = 15;

        // Reusable client to prevent socket exhaustion
        private static readonly HttpClient httpClient = new HttpClient();

        // ---------------------------------------------------------------------------
        // UI CONTROLS
        // ---------------------------------------------------------------------------
        private PictureBox videoBox;
        private FormsPlot liveGraph;
        private ScottPlot.Plottables.DataStreamer countStreamer;
        private RichTextBox insightLog;
        private Label statusLabel;

        public AuditorForm()
        {
            SetupUI();
            this.FormClosing += (s, e) => Environment.Exit(0);
            this.Shown += async (s, e) => await Task.Run(ProcessVideoAsync);
        }

        private void SetupUI()
        {
            this.Text = "AI Video Auditor";
            this.Size = new Size(1200, 800); // Made slightly larger for better viewing
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.White;

            // 1. Top Status Label
            statusLabel = new Label
            {
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                Text = "🔴 Starting Model...",
                Height = 40,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.WhiteSmoke
            };
            this.Controls.Add(statusLabel);

            // 2. Main Splitter (Left: Video | Right: Graph & Logs)
            var mainSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 600, // Width of the video feed
                SplitterWidth = 6,      // Thickness of the draggable divider
                BackColor = Color.LightGray // Makes the divider slightly visible
            };
            this.Controls.Add(mainSplitter);
            mainSplitter.BringToFront(); // CRITICAL: Ensures it fills the space below the top label

            // 3. Left Side: Video Feed
            videoBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            mainSplitter.Panel1.Controls.Add(videoBox);

            // 4. Right Side Splitter (Top: Graph | Bottom: Logs)
            var rightSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 350, // Height of the graph
                SplitterWidth = 6,
                BackColor = Color.LightGray
            };
            mainSplitter.Panel2.Controls.Add(rightSplitter);

            // 5. Top Right: Live Graph (ScottPlot 5)
            liveGraph = new FormsPlot { Dock = DockStyle.Fill, BackColor = Color.White };

            countStreamer = liveGraph.Plot.Add.DataStreamer(150);
            countStreamer.ViewScrollLeft();

            liveGraph.Plot.Title("Real-Time People Count");
            liveGraph.Plot.XLabel("Frames");
            liveGraph.Plot.YLabel("Detected Count");
            rightSplitter.Panel1.Controls.Add(liveGraph);

            // 6. Bottom Right: Insights Log
            insightLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Segoe UI", 10),
                BackColor = Color.WhiteSmoke,
                BorderStyle = BorderStyle.None // Removed border for a cleaner fit inside the splitter
            };

            var logPadding = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10), BackColor = Color.WhiteSmoke };
            logPadding.Controls.Add(insightLog);
            rightSplitter.Panel2.Controls.Add(logPadding);
        }

        // ---------------------------------------------------------------------------
        // CORE PROCESSING LOGIC
        // ---------------------------------------------------------------------------
        private async Task ProcessVideoAsync()
        {
            using var yolo = new YoloPredictor(YoloModelPath);
            using var capture = new VideoCapture(VideoPath);

            if (!capture.IsOpened())
            {
                UpdateUI(() => statusLabel.Text = "Error: Could not open video.");
                return;
            }

            var eventLogs = new List<FrameData>();
            var lastReportTime = DateTime.Now;
            using var mat = new Mat();

            UpdateUI(() => statusLabel.Text = "🔴 Live - Processing frames...");

            while (true)
            {
                capture.Read(mat);
                if (mat.Empty()) break;

                // Run YOLO detection
                Cv2.ImEncode(".jpg", mat, out byte[] imageBytes);
                var result = yolo.Detect(imageBytes);

                int currentCount = 0;
                foreach (var detection in result)
                {
                    string label = detection.Name.ToString().ToLower().Trim();
                    if (label.Contains("person") || label == "0") currentCount++;
                }

                string timeStr = DateTime.Now.ToString("HH:mm:ss");
                eventLogs.Add(new FrameData { Timestamp = timeStr, Count = currentCount });

                // Pass the data point to the ScottPlot DataStreamer
                countStreamer.Add(currentCount);

                UpdateUI(() =>
                {
                    // 1. Update Video Feed (Disposing old image prevents RAM leaks)
                    var oldImage = videoBox.Image;
                    videoBox.Image = BitmapConverter.ToBitmap(mat);
                    oldImage?.Dispose();

                    // 2. Redraw the ScottPlot graph
                    liveGraph.Refresh();

                    // 3. Update Status
                    statusLabel.Text = $"Live Detect: {currentCount} people";
                });

                // Check Gemini interval
                var elapsed = (DateTime.Now - lastReportTime).TotalSeconds;
                if (elapsed >= ReportingIntervalSeconds)
                {
                    var aggregatedData = AggregateTelemetry(eventLogs);
                    eventLogs.Clear();
                    lastReportTime = DateTime.Now;

                    // Fire & forget the API call so it doesn't stutter the live video feed
                    _ = FetchAndDisplayGeminiInsightAsync(aggregatedData);
                }

                await Task.Delay(10); // Throttle slightly to keep UI responsive
            }

            UpdateUI(() => statusLabel.Text = "✅ Video processing complete.");
        }

        private void UpdateUI(Action action)
        {
            if (this.InvokeRequired) this.Invoke(action);
            else action();
        }

        // ---------------------------------------------------------------------------
        // TELEMETRY & API 
        // ---------------------------------------------------------------------------
        static Dictionary<string, object> AggregateTelemetry(List<FrameData> logs)
        {
            if (logs.Count == 0) return new Dictionary<string, object>();

            var counts = logs.Select(x => x.Count).ToList();
            double mean = counts.Average();
            int firstCount = counts.First();
            int lastCount = counts.Last();
            string trend = lastCount > firstCount ? "rising" : (lastCount < firstCount ? "falling" : "stable");

            return new Dictionary<string, object>
            {
                { "window_start", logs.First().Timestamp },
                { "window_end", logs.Last().Timestamp },
                { "samples", counts.Count },
                { "min", counts.Min() },
                { "max", counts.Max() },
                { "mean", Math.Round(mean, 1) },
                { "trend", trend }
            };
        }

        private async Task FetchAndDisplayGeminiInsightAsync(Dictionary<string, object> aggData)
        {
            UpdateUI(() => insightLog.AppendText($"[⏱️ Aggregating {aggData["samples"]} frames...] Requesting insight...\n"));

            string jsonData = JsonSerializer.Serialize(aggData);
            string prompt = $@"You are an AI Security Auditor. Analyse this {ReportingIntervalSeconds}s telemetry window:
{jsonData}
Reply with 2-4 sentences. State the crowd trend, flag any potential risks, and suggest action if needed.";

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            string jsonPayload = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            string requestUri = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={GeminiApiKey}";

            try
            {
                var response = await httpClient.PostAsync(requestUri, content);
                response.EnsureSuccessStatusCode();

                string responseString = await response.Content.ReadAsStringAsync();
                using JsonDocument doc = JsonDocument.Parse(responseString);
                var textResult = doc.RootElement
                                    .GetProperty("candidates")[0]
                                    .GetProperty("content")
                                    .GetProperty("parts")[0]
                                    .GetProperty("text")
                                    .GetString();

                UpdateUI(() =>
                {
                    insightLog.SelectionColor = Color.Blue;
                    insightLog.AppendText($"🤖 GEMINI INSIGHT ({DateTime.Now:HH:mm:ss}):\n");
                    insightLog.SelectionColor = Color.Black;
                    insightLog.AppendText($"{textResult.Trim()}\n\n");
                    insightLog.ScrollToCaret();
                });
            }
            catch (Exception ex)
            {
                UpdateUI(() => insightLog.AppendText($"\n⚠️ API Error: {ex.Message}\n\n"));
            }
        }
    }

    class FrameData
    {
        public string Timestamp { get; set; }
        public int Count { get; set; }
    }
}