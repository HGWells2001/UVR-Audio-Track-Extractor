using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace UVRAudioTrackExtractor
{
    internal class AudioTrack
    {
        public int Index;
        public string Language;
        public string Codec;
        public int Channels;
        public string Layout;
        public string Title;
        public double Duration;
        public override string ToString() { return "Traccia " + Index; }
    }

    internal class ExportJob
    {
        public AudioTrack Track;
        public List<string> Outputs = new List<string>();
        public List<string> Channels = new List<string>();
        public bool Full;
        public bool Mono;
        public bool Chunked;
        public int ChunkSeconds;
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private TextBox txtVideo, txtFolder, txtLog;
        private ListView lstTracks;
        private Button btnBrowse, btnFolder, btnAnalyze, btnStart, btnStop, btnOpen, btnRecompose;
        private RadioButton radFull, radSplit, radBoth, radMono;
        private CheckBox chkOverwrite, chkChunk, chkKeepWhole;
        private NumericUpDown numChunkSeconds;
        private ProgressBar progress;
        private Label lblStatus;
        private List<AudioTrack> tracks = new List<AudioTrack>();
        private string videoPath = "";
        private double movieDuration = 0;
        private volatile bool cancelRequested = false;
        private volatile Process activeProcess;
        private bool busy = false;
        private bool closeWhenStopped = false;
        private string extractionDestination = "";
        private string ffmpegPath, ffprobePath;
        private readonly object processLock = new object();

        public MainForm()
        {
            Text = "UVR Audio Track Extractor V5 | Offline";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(850, 720);
            Size = new Size(1000, 810);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(246, 248, 251);
            BuildInterface();
            ffmpegPath = FindTool("ffmpeg.exe");
            ffprobePath = FindTool("ffprobe.exe");
            Log("Applicazione offline pronta. FFmpeg: " + ffmpegPath);
            Log("FFprobe: " + ffprobePath);
        }

        private void BuildInterface()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(16);
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
            Controls.Add(root);

            Label heading = new Label();
            heading.Text = "UVR Audio Track Extractor  V5";
            heading.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            heading.ForeColor = Color.FromArgb(32, 55, 84);
            heading.Dock = DockStyle.Fill;
            root.Controls.Add(heading, 0, 0);

            GroupBox source = new GroupBox();
            source.Text = "1. Carica il video";
            source.Dock = DockStyle.Fill;
            root.Controls.Add(source, 0, 1);
            TableLayoutPanel sourceRow = new TableLayoutPanel();
            sourceRow.Dock = DockStyle.Fill;
            sourceRow.Padding = new Padding(8, 5, 8, 7);
            sourceRow.ColumnCount = 3;
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            sourceRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
            source.Controls.Add(sourceRow);
            txtVideo = new TextBox();
            txtVideo.Dock = DockStyle.Fill;
            txtVideo.ReadOnly = true;
            txtVideo.Margin = new Padding(3, 8, 3, 0);
            sourceRow.Controls.Add(txtVideo, 0, 0);
            btnBrowse = MakeButton("Apri video", BrowseVideo);
            sourceRow.Controls.Add(btnBrowse, 1, 0);
            btnAnalyze = MakeButton("Analizza", AnalyzeVideo);
            sourceRow.Controls.Add(btnAnalyze, 2, 0);

            GroupBox selection = new GroupBox();
            selection.Text = "2. Scegli le tracce audio (spunta una o piu righe)";
            selection.Dock = DockStyle.Fill;
            root.Controls.Add(selection, 0, 2);
            TableLayoutPanel selectionLayout = new TableLayoutPanel();
            selectionLayout.Dock = DockStyle.Fill;
            selectionLayout.RowCount = 2;
            selectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            selectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            selection.Controls.Add(selectionLayout);
            lstTracks = new ListView();
            lstTracks.Dock = DockStyle.Fill;
            lstTracks.View = View.Details;
            lstTracks.CheckBoxes = true;
            lstTracks.FullRowSelect = true;
            lstTracks.GridLines = true;
            lstTracks.HideSelection = false;
            lstTracks.Columns.Add("ID", 55);
            lstTracks.Columns.Add("Lingua", 100);
            lstTracks.Columns.Add("Codec", 110);
            lstTracks.Columns.Add("Canali", 75);
            lstTracks.Columns.Add("Layout", 135);
            lstTracks.Columns.Add("Titolo", 350);
            selectionLayout.Controls.Add(lstTracks, 0, 0);
            FlowLayoutPanel selButtons = new FlowLayoutPanel();
            selButtons.Dock = DockStyle.Fill;
            selButtons.FlowDirection = FlowDirection.LeftToRight;
            Button all = new Button();
            all.Text = "Seleziona tutte";
            all.AutoSize = true;
            all.Click += delegate { foreach (ListViewItem item in lstTracks.Items) item.Checked = true; };
            Button none = new Button();
            none.Text = "Deseleziona";
            none.AutoSize = true;
            none.Click += delegate { foreach (ListViewItem item in lstTracks.Items) item.Checked = false; };
            selButtons.Controls.Add(all);
            selButtons.Controls.Add(none);
            selectionLayout.Controls.Add(selButtons, 0, 1);

            GroupBox options = new GroupBox();
            options.Text = "3. Modalita di estrazione e chunk per UVR (WAV PCM 24 bit, 48 kHz)";
            options.Dock = DockStyle.Fill;
            root.Controls.Add(options, 0, 3);
            TableLayoutPanel optionsLayout = new TableLayoutPanel();
            optionsLayout.Dock = DockStyle.Fill;
            optionsLayout.ColumnCount = 1;
            optionsLayout.RowCount = 2;
            optionsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            optionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(optionsLayout);

            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.Dock = DockStyle.Fill;
            opts.Padding = new Padding(10, 3, 5, 0);
            opts.WrapContents = false;
            radFull = new RadioButton();
            radFull.Text = "Traccia intera multicanale";
            radFull.AutoSize = true;
            radFull.Margin = new Padding(6, 7, 18, 3);
            radSplit = new RadioButton();
            radSplit.Text = "Canali separati";
            radSplit.AutoSize = true;
            radSplit.Margin = new Padding(6, 7, 18, 3);
            radBoth = new RadioButton();
            radBoth.Text = "Entrambe";
            radBoth.AutoSize = true;
            radBoth.Checked = true;
            radBoth.Margin = new Padding(6, 7, 18, 3);
            radMono = new RadioButton();
            radMono.Text = "Converti in mono (mix)";
            radMono.AutoSize = true;
            radMono.Margin = new Padding(6, 7, 18, 3);
            ToolTip monoTip = new ToolTip();
            monoTip.SetToolTip(radMono, "Unisce stereo o surround in un solo canale con il downmix FFmpeg. Non isola le voci.");
            opts.Controls.Add(radFull);
            opts.Controls.Add(radSplit);
            opts.Controls.Add(radBoth);
            opts.Controls.Add(radMono);
            optionsLayout.Controls.Add(opts, 0, 0);

            FlowLayoutPanel chunkOpts = new FlowLayoutPanel();
            chunkOpts.Dock = DockStyle.Fill;
            chunkOpts.Padding = new Padding(10, 3, 5, 0);
            chunkOpts.WrapContents = true;
            chkChunk = new CheckBox();
            chkChunk.Text = "Dividi in chunk per UVR";
            chkChunk.Checked = false;
            chkChunk.AutoSize = true;
            chkChunk.Margin = new Padding(6, 8, 8, 3);
            Label chunkLabel = new Label();
            chunkLabel.Text = "Durata:";
            chunkLabel.AutoSize = true;
            chunkLabel.Margin = new Padding(8, 10, 4, 3);
            numChunkSeconds = new NumericUpDown();
            numChunkSeconds.Minimum = 5;
            numChunkSeconds.Maximum = 600;
            numChunkSeconds.Increment = 5;
            numChunkSeconds.Value = 30;
            numChunkSeconds.Width = 65;
            numChunkSeconds.Margin = new Padding(0, 5, 3, 3);
            Label secLabel = new Label();
            secLabel.Text = "secondi";
            secLabel.AutoSize = true;
            secLabel.Margin = new Padding(1, 10, 12, 3);
            chkKeepWhole = new CheckBox();
            chkKeepWhole.Text = "Salva anche WAV interi";
            chkKeepWhole.AutoSize = true;
            chkKeepWhole.Margin = new Padding(7, 8, 4, 3);
            chkChunk.CheckedChanged += delegate {
                numChunkSeconds.Enabled = chkChunk.Checked;
                chkKeepWhole.Enabled = chkChunk.Checked;
            };
            numChunkSeconds.Enabled = chkChunk.Checked;
            chkKeepWhole.Enabled = chkChunk.Checked;
            chunkOpts.Controls.Add(chkChunk);
            chunkOpts.Controls.Add(chunkLabel);
            chunkOpts.Controls.Add(numChunkSeconds);
            chunkOpts.Controls.Add(secLabel);
            chunkOpts.Controls.Add(chkKeepWhole);
            optionsLayout.Controls.Add(chunkOpts, 0, 1);

            GroupBox target = new GroupBox();
            target.Text = "4. Cartella di destinazione";
            target.Dock = DockStyle.Fill;
            root.Controls.Add(target, 0, 4);
            TableLayoutPanel targetRow = new TableLayoutPanel();
            targetRow.Dock = DockStyle.Fill;
            targetRow.Padding = new Padding(8, 3, 8, 5);
            targetRow.ColumnCount = 3;
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            targetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            target.Controls.Add(targetRow);
            txtFolder = new TextBox();
            txtFolder.ReadOnly = true;
            txtFolder.Dock = DockStyle.Fill;
            txtFolder.Margin = new Padding(3, 8, 3, 0);
            targetRow.Controls.Add(txtFolder, 0, 0);
            btnFolder = MakeButton("Scegli cartella", ChooseFolder);
            targetRow.Controls.Add(btnFolder, 1, 0);
            btnOpen = MakeButton("Apri cartella", OpenFolder);
            targetRow.Controls.Add(btnOpen, 2, 0);

            FlowLayoutPanel actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(2, 7, 2, 0);
            actions.WrapContents = true;
            root.Controls.Add(actions, 0, 5);
            btnStart = new Button();
            btnStart.Text = "ESTRAI AUDIO";
            btnStart.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnStart.Width = 155;
            btnStart.Height = 38;
            btnStart.BackColor = Color.FromArgb(37, 104, 172);
            btnStart.ForeColor = Color.White;
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.Click += delegate { StartExtraction(); };
            btnStop = new Button();
            btnStop.Text = "Annulla";
            btnStop.Width = 100;
            btnStop.Height = 38;
            btnStop.Enabled = false;
            btnStop.Click += delegate { CancelExtraction(); };
            chkOverwrite = new CheckBox();
            chkOverwrite.Text = "Sovrascrivi i WAV esistenti";
            chkOverwrite.AutoSize = true;
            chkOverwrite.Margin = new Padding(20, 13, 0, 0);
            actions.Controls.Add(btnStart);
            btnRecompose = new Button();
            btnRecompose.Text = "RICOMPONI 5.1";
            btnRecompose.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnRecompose.Width = 160;
            btnRecompose.Height = 38;
            btnRecompose.BackColor = Color.FromArgb(34, 127, 107);
            btnRecompose.ForeColor = Color.White;
            btnRecompose.FlatStyle = FlatStyle.Flat;
            btnRecompose.Click += delegate {
                if (busy) {
                    MessageBox.Show("Attendi la fine dell'estrazione o premi Annulla.");
                    return;
                }
                using (RecomposeForm form = new RecomposeForm(ffmpegPath, ffprobePath))
                    form.ShowDialog(this);
            };
            actions.Controls.Add(btnRecompose);
            actions.Controls.Add(btnStop);
            actions.Controls.Add(chkOverwrite);

            TableLayoutPanel progressRow = new TableLayoutPanel();
            progressRow.Dock = DockStyle.Fill;
            progressRow.ColumnCount = 2;
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            root.Controls.Add(progressRow, 0, 6);
            progress = new ProgressBar();
            progress.Dock = DockStyle.Fill;
            progress.Minimum = 0;
            progress.Maximum = 1000;
            progress.Margin = new Padding(4, 9, 10, 9);
            lblStatus = new Label();
            lblStatus.Text = "In attesa";
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Dock = DockStyle.Fill;
            progressRow.Controls.Add(progress, 0, 0);
            progressRow.Controls.Add(lblStatus, 1, 0);

            GroupBox logGroup = new GroupBox();
            logGroup.Text = "Registro operazioni";
            logGroup.Dock = DockStyle.Fill;
            root.Controls.Add(logGroup, 0, 7);
            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.FromArgb(24, 32, 45);
            txtLog.ForeColor = Color.FromArgb(228, 237, 247);
            txtLog.Font = new Font("Consolas", 9F);
            txtLog.Dock = DockStyle.Fill;
            logGroup.Controls.Add(txtLog);

            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) {
                    DialogResult r = MessageBox.Show("Interrompere l'estrazione e chiudere?", "Conferma", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (r != DialogResult.Yes) { e.Cancel = true; return; }
                    // Non chiudere finche il worker non ha cancellato i file temporanei.
                    closeWhenStopped = true;
                    CancelExtraction();
                    e.Cancel = true;
                }
            };
        }

        private Button MakeButton(string text, EventHandler handler)
        {
            Button button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(4, 4, 4, 4);
            button.Click += handler;
            return button;
        }

        private string FindTool(string name)
        {
            string home = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = {
                Path.Combine(home, "bin", name),
                Path.Combine(home, name)
            };
            foreach (string candidate in candidates)
                if (File.Exists(candidate)) return candidate;
            return name; // Disponibile nel PATH di Windows
        }

        private void BrowseVideo(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Filter = "File video|*.mkv;*.mp4;*.avi;*.mov;*.m2ts;*.ts;*.webm;*.vob;*.mpg;*.mpeg|Tutti i file|*.*";
                dialog.Title = "Seleziona il film";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                videoPath = dialog.FileName;
                txtVideo.Text = videoPath;
                txtFolder.Text = Path.Combine(Path.GetDirectoryName(videoPath), "UVR_ESTRATTI");
                AnalyzeVideo(null, EventArgs.Empty);
            }
        }

        private void ChooseFolder(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Scegli la cartella per i WAV";
                if (Directory.Exists(txtFolder.Text)) dialog.SelectedPath = txtFolder.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK) txtFolder.Text = dialog.SelectedPath;
            }
        }

        private void OpenFolder(object sender, EventArgs e)
        {
            if (Directory.Exists(txtFolder.Text))
                Process.Start("explorer.exe", "\"" + txtFolder.Text + "\"");
            else
                MessageBox.Show("La cartella non esiste ancora.", "Cartella");
        }

        private static string Val(Dictionary<string, object> obj, string key, string fallback)
        {
            object value;
            if (obj == null || !obj.TryGetValue(key, out value) || value == null) return fallback;
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> Dict(object value)
        {
            return value as Dictionary<string, object>;
        }

        private static int ToInt(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static double ToDouble(string value, double fallback)
        {
            double parsed;
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) ? parsed : fallback;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private void AnalyzeVideo(object sender, EventArgs e)
        {
            if (busy) return;
            if (!File.Exists(videoPath)) {
                MessageBox.Show("Seleziona prima un video.", "Video mancante");
                return;
            }
            try {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ffprobePath;
                psi.Arguments = "-v error -show_streams -show_format -of json " + Quote(videoPath);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                string json, err;
                using (Process proc = Process.Start(psi)) {
                    json = proc.StandardOutput.ReadToEnd();
                    err = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode != 0) throw new Exception(err);
                }
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                serializer.MaxJsonLength = 10000000;
                Dictionary<string, object> data = Dict(serializer.DeserializeObject(json));
                object streamsObj;
                if (data == null || !data.TryGetValue("streams", out streamsObj))
                    throw new Exception("FFprobe non ha restituito le tracce.");
                object[] streams = streamsObj as object[];
                if (streams == null) throw new Exception("Formato di risposta ffprobe inatteso.");
                tracks.Clear();
                lstTracks.Items.Clear();
                Dictionary<string, object> format = null;
                object formatObj;
                if (data.TryGetValue("format", out formatObj)) format = Dict(formatObj);
                movieDuration = ToDouble(Val(format, "duration", "0"), 0);
                foreach (object raw in streams) {
                    Dictionary<string, object> s = Dict(raw);
                    if (Val(s, "codec_type", "") != "audio") continue;
                    Dictionary<string, object> tags = null;
                    object tagsObj;
                    if (s.TryGetValue("tags", out tagsObj)) tags = Dict(tagsObj);
                    AudioTrack t = new AudioTrack();
                    t.Index = ToInt(Val(s, "index", "-1"), -1);
                    t.Language = Val(tags, "language", "und");
                    t.Codec = Val(s, "codec_name", "?");
                    t.Channels = ToInt(Val(s, "channels", "0"), 0);
                    t.Layout = Val(s, "channel_layout", "");
                    t.Title = Val(tags, "title", "");
                    t.Duration = ToDouble(Val(s, "duration", "0"), movieDuration);
                    if (t.Duration <= 0) t.Duration = movieDuration;
                    tracks.Add(t);
                    ListViewItem row = new ListViewItem(t.Index.ToString());
                    row.SubItems.Add(t.Language);
                    row.SubItems.Add(t.Codec);
                    row.SubItems.Add(t.Channels.ToString());
                    row.SubItems.Add(string.IsNullOrEmpty(t.Layout) ? "(non dichiarato)" : t.Layout);
                    row.SubItems.Add(t.Title);
                    row.Tag = t;
                    row.Checked = true;
                    lstTracks.Items.Add(row);
                }
                if (tracks.Count == 0) throw new Exception("Nessuna traccia audio trovata.");
                Log("Analizzato: " + Path.GetFileName(videoPath));
                Log("Tracce audio trovate: " + tracks.Count + ". Durata: " + TimeSpan.FromSeconds(movieDuration).ToString(@"hh\:mm\:ss"));
                lblStatus.Text = tracks.Count + " tracce audio";
            } catch (Exception ex) {
                tracks.Clear();
                lstTracks.Items.Clear();
                MessageBox.Show("Impossibile analizzare il video.\r\n\r\n" + ex.Message +
                    "\r\n\r\nControlla che ffprobe.exe sia in bin oppure nel PATH.", "Errore FFprobe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Log("ERRORE: " + ex.Message);
            }
        }

        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            if (name.Length > 75) name = name.Substring(0, 75);
            return string.IsNullOrEmpty(name) ? "Video" : name;
        }

        private static List<string> ChannelNames(AudioTrack t)
        {
            string layout = (t.Layout ?? "").ToLowerInvariant().Trim();
            if (layout == "mono" && t.Channels == 1) return new List<string> { "FC" };
            if (layout == "stereo" && t.Channels == 2) return new List<string> { "FL", "FR" };
            if (layout == "2.1" && t.Channels == 3) return new List<string> { "FL", "FR", "LFE" };
            if (layout == "3.0" && t.Channels == 3) return new List<string> { "FL", "FR", "FC" };
            if (layout == "quad" && t.Channels == 4) return new List<string> { "FL", "FR", "BL", "BR" };
            if (layout == "quad(side)" && t.Channels == 4) return new List<string> { "FL", "FR", "SL", "SR" };
            if (layout == "5.0" && t.Channels == 5) return new List<string> { "FL", "FR", "FC", "BL", "BR" };
            if (layout == "5.0(side)" && t.Channels == 5) return new List<string> { "FL", "FR", "FC", "SL", "SR" };
            if (layout == "5.1" && t.Channels == 6) return new List<string> { "FL", "FR", "FC", "LFE", "BL", "BR" };
            if (layout == "5.1(side)" && t.Channels == 6) return new List<string> { "FL", "FR", "FC", "LFE", "SL", "SR" };
            if (layout == "7.1" && t.Channels == 8) return new List<string> { "FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR" };
            // Non indovinare il layout: un ordine errato compromette il surround.
            return null;
        }

        private static string MakeOutputPath(string folder, string stem, bool chunked)
        {
            if (!chunked) return Path.Combine(folder, stem + ".wav");
            string chunkFolder = Path.Combine(folder, "UVR_CHUNKS", stem);
            return Path.Combine(chunkFolder, stem + "_chunk_%04d.wav");
        }

        private static string ChunkManifest(string pattern)
        {
            const string suffix = "_chunk_%04d.wav";
            if (!pattern.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Nome del chunk non valido: " + pattern);
            return pattern.Substring(0, pattern.Length - suffix.Length) + "_segments.csv";
        }

        private static IEnumerable<string> ExistingOutputs(ExportJob job)
        {
            foreach (string output in job.Outputs) {
                if (!job.Chunked) {
                    if (File.Exists(output)) yield return output;
                    continue;
                }
                string folder = Path.GetDirectoryName(output);
                if (!Directory.Exists(folder)) continue;
                string pattern = Path.GetFileName(output).Replace("%04d", "*");
                foreach (string found in Directory.GetFiles(folder, pattern))
                    yield return found;
                string manifest = ChunkManifest(output);
                if (File.Exists(manifest)) yield return manifest;
            }
        }

        private List<ExportJob> BuildJobs()
        {
            List<ExportJob> jobs = new List<ExportJob>();
            bool mono = radMono.Checked;
            bool full = mono || radFull.Checked || radBoth.Checked;
            bool split = !mono && (radSplit.Checked || radBoth.Checked);
            bool chunked = chkChunk.Checked;
            int chunkSeconds = (int)numChunkSeconds.Value;
            string prefix = SafeName(Path.GetFileNameWithoutExtension(videoPath));
            bool[] variants = chunked
                ? (chkKeepWhole.Checked ? new bool[] { true, false } : new bool[] { true })
                : new bool[] { false };
            foreach (ListViewItem row in lstTracks.Items) {
                if (!row.Checked) continue;
                AudioTrack track = (AudioTrack)row.Tag;
                List<string> names = new List<string>();
                if (split) {
                    names = ChannelNames(track);
                    if (names == null)
                        throw new Exception("Layout non riconosciuto nella traccia " + track.Index +
                            " (" + track.Channels + " canali, layout '" + track.Layout +
                            "'). Per evitare canali sbagliati, scegli 'Traccia intera multicanale'.");
                }
                string baseName = prefix + "_T" + track.Index + "_" + SafeName(track.Language.ToUpperInvariant());
                foreach (bool asChunks in variants) {
                    ExportJob job = new ExportJob();
                    job.Track = track;
                    job.Full = full;
                    job.Mono = mono;
                    job.Chunked = asChunks;
                    job.ChunkSeconds = chunkSeconds;
                    job.Channels = new List<string>(names);
                    if (full)
                        job.Outputs.Add(MakeOutputPath(txtFolder.Text,
                            baseName + (mono ? "_MONO" : "_" + track.Channels + "CH"), asChunks));
                    foreach (string name in names)
                        job.Outputs.Add(MakeOutputPath(txtFolder.Text, baseName + "_" + name, asChunks));
                    jobs.Add(job);
                }
            }
            return jobs;
        }

        private void StartExtraction()
        {
            if (busy) return;
            if (tracks.Count == 0 || !File.Exists(videoPath)) {
                MessageBox.Show("Carica e analizza prima un video.");
                return;
            }
            if (string.IsNullOrWhiteSpace(txtFolder.Text)) {
                MessageBox.Show("Scegli la cartella di destinazione.");
                return;
            }
            List<ExportJob> jobs;
            try {
                jobs = BuildJobs();
                if (jobs.Count == 0) {
                    MessageBox.Show("Spunta almeno una traccia audio.");
                    return;
                }
                if (!chkOverwrite.Checked) {
                    List<string> existing = jobs.SelectMany(j => ExistingOutputs(j)).Take(10).ToList();
                    if (existing.Count > 0)
                        throw new Exception("Esistono gia file WAV o chunk nella destinazione. " +
                            "Scegli un'altra cartella oppure attiva 'Sovrascrivi'.\\r\\n\\r\\n" +
                            string.Join("\\r\\n", existing.Take(5).ToArray()));
                }
                Directory.CreateDirectory(txtFolder.Text);
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, "Impossibile avviare", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            extractionDestination = Path.GetFullPath(txtFolder.Text);
            busy = true;
            cancelRequested = false;
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnBrowse.Enabled = false;
            btnAnalyze.Enabled = false;
            btnFolder.Enabled = false;
            progress.Value = 0;
            lblStatus.Text = "Preparazione...";
            string input = videoPath;
            bool overwrite = chkOverwrite.Checked;
            Thread worker = new Thread(delegate() { RunJobs(jobs, input, overwrite); });
            worker.IsBackground = true;
            worker.Start();
        }

        private static string MakeFilter(ExportJob job)
        {
            if (job.Mono) {
                // Il downmix di FFmpeg combina i canali (non ne seleziona uno).
                // Per i chunk, prepara frame di 4800 campioni a 48 kHz.
                return "[0:" + job.Track.Index + "]aresample=48000," +
                    "aformat=channel_layouts=mono" +
                    (job.Chunked ? ",asetpts=N/SR/TB,asetnsamples=n=4800:p=0" : "") +
                    "[o0]";
            }
            int count = job.Channels.Count + (job.Full ? 1 : 0);
            string source = "[0:" + job.Track.Index + "]";
            string prep = job.Chunked ? "aresample=48000,asetpts=N/SR/TB," : "";
            string packet = job.Chunked ? ",asetnsamples=n=4800:p=0" : "";
            if (count == 1 && job.Full) {
                if (!job.Chunked) return "";
                return source + "aresample=48000,asetpts=N/SR/TB,asetnsamples=n=4800:p=0[o0]";
            }
            if (count == 1) {
                string one = job.Channels[0];
                return source + prep + "pan=mono|c0=" +
                    (job.Track.Channels == 1 ? "c0" : one) + packet + "[o0]";
            }
            StringBuilder filter = new StringBuilder();
            filter.Append(source).Append(prep).Append("asplit=").Append(count);
            for (int i = 0; i < count; i++) filter.Append("[a").Append(i).Append("]");
            int k = 0;
            if (job.Full) {
                filter.Append(";[a0]").Append(job.Chunked ? "asetnsamples=n=4800:p=0" : "anull").Append("[o0]");
                k = 1;
            }
            for (int i = 0; i < job.Channels.Count; i++) {
                filter.Append(";[a").Append(i + k).Append("]pan=mono|c0=")
                      .Append(job.Track.Channels == 1 ? "c0" : job.Channels[i])
                      .Append(packet).Append("[o").Append(i + k).Append("]");
            }
            return filter.ToString();
        }

        private string MakeArgs(ExportJob job, string input, bool overwrite)
        {
            StringBuilder args = new StringBuilder();
            args.Append("-hide_banner -loglevel error -nostdin -nostats -progress pipe:1 ");
            args.Append(overwrite ? "-y " : "-n ");
            args.Append("-i ").Append(Quote(input)).Append(" ");
            string filter = MakeFilter(job);
            if (filter.Length > 0) args.Append("-filter_complex ").Append(Quote(filter)).Append(" ");
            for (int i = 0; i < job.Outputs.Count; i++) {
                if (filter.Length == 0) args.Append("-map 0:").Append(job.Track.Index).Append(" ");
                else args.Append("-map ").Append(Quote("[o" + i + "]")).Append(" ");
                args.Append("-vn -sn -dn -c:a pcm_s24le -ar 48000 ");
                if (job.Mono) args.Append("-ac 1 ");
                if (job.Chunked) {
                    args.Append("-f segment -segment_time ").Append(job.ChunkSeconds)
                        .Append(" -reset_timestamps 1 -segment_format wav ")
                        .Append("-segment_list_type csv -segment_list ")
                        .Append(Quote(ChunkManifest(job.Outputs[i]))).Append(" ");
                }
                args.Append(Quote(job.Outputs[i])).Append(" ");
            }
            return args.ToString();
        }

        private static double ParseTime(string value)
        {
            TimeSpan parsed;
            return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out parsed) ? parsed.TotalSeconds : 0;
        }

        private static void PrepareOutputFiles(ExportJob job, bool overwrite)
        {
            foreach (string output in job.Outputs) {
                string folder = Path.GetDirectoryName(output);
                Directory.CreateDirectory(folder);
                if (job.Chunked && overwrite) {
                    string mask = Path.GetFileName(output).Replace("%04d", "*");
                    foreach (string oldChunk in Directory.GetFiles(folder, mask))
                        File.Delete(oldChunk);
                    string oldManifest = ChunkManifest(output);
                    if (File.Exists(oldManifest)) File.Delete(oldManifest);
                }
            }
        }

        // Tutti i file dell'estrazione nascono in una cartella temporanea
        // interna alla destinazione. Nessun WAV incompleto viene pubblicato.
        private static string RelativeToFolder(string folder, string path)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Percorso di uscita esterno alla cartella selezionata: " + path);
            return full.Substring(root.Length);
        }

        private static ExportJob StageJob(ExportJob job, string destination, string staging)
        {
            ExportJob staged = new ExportJob();
            staged.Track = job.Track;
            staged.Full = job.Full;
            staged.Mono = job.Mono;
            staged.Chunked = job.Chunked;
            staged.ChunkSeconds = job.ChunkSeconds;
            staged.Channels = new List<string>(job.Channels);
            foreach (string output in job.Outputs)
                staged.Outputs.Add(Path.Combine(staging, RelativeToFolder(destination, output)));
            return staged;
        }

        private static void RemoveEmptyParents(string folder, string stopAt)
        {
            string stop = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
            string current = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            while (current.Length > stop.Length &&
                   current.StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    break;
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(current)) break;
            }
        }

        private static void RemoveStaging(string staging)
        {
            if (!Directory.Exists(staging)) return;
            IOException last = null;
            for (int attempt = 0; attempt < 4; attempt++) {
                try {
                    Directory.Delete(staging, true);
                    return;
                } catch (IOException ex) {
                    last = ex;
                } catch (UnauthorizedAccessException ex) {
                    throw new IOException("Impossibile eliminare i file temporanei: " + staging, ex);
                }
                Thread.Sleep(250);
            }
            throw new IOException("Impossibile eliminare i file temporanei: " + staging, last);
        }

        private void CommitStagedOutputs(List<ExportJob> jobs, string destination,
                                         string staging, bool overwrite)
        {
            // Le sostituzioni sono reversibili fino alla fine del commit.
            string backup = Path.Combine(staging, ".backup");
            List<string> installed = new List<string>();
            List<KeyValuePair<string, string>> saved = new List<KeyValuePair<string, string>>();
            try {
                if (cancelRequested) throw new OperationCanceledException();
                List<string> stagedFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories).ToList();
                if (stagedFiles.Count == 0)
                    throw new IOException("FFmpeg non ha prodotto alcun file.");
                HashSet<string> existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (overwrite) {
                    foreach (ExportJob job in jobs)
                        foreach (string path in ExistingOutputs(job))
                            existing.Add(path);
                }
                foreach (string stageFile in stagedFiles) {
                    string destFile = Path.Combine(destination, RelativeToFolder(staging, stageFile));
                    if (File.Exists(destFile)) {
                        if (!overwrite)
                            throw new IOException("Il file esiste gia: " + destFile);
                        existing.Add(destFile);
                    }
                }
                foreach (string oldFile in existing) {
                    if (cancelRequested) throw new OperationCanceledException();
                    if (!File.Exists(oldFile)) continue;
                    string savedPath = Path.Combine(backup, RelativeToFolder(destination, oldFile));
                    Directory.CreateDirectory(Path.GetDirectoryName(savedPath));
                    File.Move(oldFile, savedPath);
                    saved.Add(new KeyValuePair<string, string>(savedPath, oldFile));
                }
                foreach (string stageFile in stagedFiles) {
                    if (cancelRequested) throw new OperationCanceledException();
                    string destFile = Path.Combine(destination, RelativeToFolder(staging, stageFile));
                    Directory.CreateDirectory(Path.GetDirectoryName(destFile));
                    File.Move(stageFile, destFile);
                    installed.Add(destFile);
                }
                if (cancelRequested) throw new OperationCanceledException();
            } catch (Exception) {
                // Rollback: rimuove i nuovi file e ripristina i precedenti.
                List<string> failures = new List<string>();
                for (int i = installed.Count - 1; i >= 0; i--) {
                    try {
                        if (File.Exists(installed[i])) File.Delete(installed[i]);
                        RemoveEmptyParents(Path.GetDirectoryName(installed[i]), destination);
                    } catch (Exception ex) { failures.Add(ex.Message); }
                }
                for (int i = saved.Count - 1; i >= 0; i--) {
                    try {
                        Directory.CreateDirectory(Path.GetDirectoryName(saved[i].Value));
                        File.Move(saved[i].Key, saved[i].Value);
                    } catch (Exception ex) { failures.Add(ex.Message); }
                }
                if (failures.Count > 0)
                    throw new IOException("Ripristino incompleto. Controlla la cartella temporanea " +
                        staging + ": " + string.Join("; ", failures.ToArray()));
                throw;
            }
        }

        private void RunJobs(List<ExportJob> jobs, string input, bool overwrite)
        {
            string error = null;
            bool cancelled = false;
            bool completed = false;
            string destination = extractionDestination;
            string staging = Path.Combine(destination, ".UVR_TEMP_" + Guid.NewGuid().ToString("N"));
            try {
                Directory.CreateDirectory(staging);
                List<ExportJob> stagedJobs = jobs.Select(j => StageJob(j, destination, staging)).ToList();
                for (int j = 0; j < stagedJobs.Count; j++) {
                    if (cancelRequested) throw new OperationCanceledException();
                    ExportJob job = stagedJobs[j];
                    PrepareOutputFiles(job, false);
                    Log("Estrazione traccia " + job.Track.Index + " [" + job.Track.Language +
                        ", " + job.Track.Channels + "ch, " + job.Track.Layout + "]" +
                        (job.Mono ? " - downmix mono" : "") +
                        (job.Chunked ? " - chunk da " + job.ChunkSeconds + " secondi" : " - WAV interi"));
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = ffmpegPath;
                    psi.Arguments = MakeArgs(job, input, false);
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    StringBuilder stderr = new StringBuilder();
                    using (Process proc = new Process()) {
                        proc.StartInfo = psi;
                        proc.Start();
                        lock (processLock) { activeProcess = proc; }
                        Thread errorReader = new Thread(delegate() {
                            try {
                                string line;
                                while ((line = proc.StandardError.ReadLine()) != null) {
                                    if (stderr.Length < 30000) stderr.AppendLine(line);
                                }
                            } catch (Exception) { }
                        });
                        errorReader.IsBackground = true;
                        errorReader.Start();
                        string outputLine;
                        double duration = job.Track.Duration > 0 ? job.Track.Duration : movieDuration;
                        while ((outputLine = proc.StandardOutput.ReadLine()) != null) {
                            if (cancelRequested) {
                                try { if (!proc.HasExited) proc.Kill(); } catch (Exception) { }
                                break;
                            }
                            if (outputLine.StartsWith("out_time=") && duration > 0) {
                                double seconds = ParseTime(outputLine.Substring(9));
                                double fraction = Math.Min(0.999, Math.Max(0, seconds / duration));
                                SetProgress((j + fraction) / stagedJobs.Count,
                                            "Traccia " + (j + 1) + "/" + stagedJobs.Count);
                            }
                        }
                        proc.WaitForExit();
                        errorReader.Join(2000);
                        lock (processLock) { if (activeProcess == proc) activeProcess = null; }
                        if (cancelRequested) throw new OperationCanceledException();
                        if (proc.ExitCode != 0) {
                            throw new Exception("FFmpeg ha restituito codice " + proc.ExitCode +
                                " per la traccia " + job.Track.Index + ":\r\n" + stderr.ToString());
                        }
                    }
                    Log("Traccia " + job.Track.Index + " pronta nella cartella temporanea.");
                    SetProgress((j + 1.0) / stagedJobs.Count, "Traccia " + (j + 1) + "/" + stagedJobs.Count);
                }
                if (cancelRequested) throw new OperationCanceledException();
                Log("Estrazione terminata. Pubblicazione dei file...");
                CommitStagedOutputs(jobs, destination, staging, overwrite);
                completed = true;
            } catch (OperationCanceledException) {
                cancelled = true;
            } catch (Exception ex) {
                error = ex.Message;
            } finally {
                lock (processLock) { activeProcess = null; }
                try {
                    // Se un rollback e' fallito, conserva i backup per il recupero manuale.
                    if (error == null || !error.StartsWith("Ripristino incompleto.", StringComparison.Ordinal))
                        RemoveStaging(staging);
                    if ((cancelled || cancelRequested) && !completed && error == null)
                        Log("Pulizia completata: nessun WAV o chunk della sessione annullata e' stato conservato.");
                } catch (Exception ex) {
                    error = (error == null ? "" : error + "\r\n") + ex.Message;
                    Log("ERRORE DI PULIZIA: " + ex.Message);
                }
                string finalError = error;
                bool wasCancelled = (cancelled || cancelRequested) && !completed;
                OnUI(delegate() {
                    busy = false;
                    btnStart.Enabled = true;
                    btnStop.Enabled = false;
                    btnBrowse.Enabled = true;
                    btnAnalyze.Enabled = true;
                    btnFolder.Enabled = true;
                    if (finalError != null) {
                        lblStatus.Text = "Errore";
                        Log("ERRORE: " + finalError);
                        MessageBox.Show(finalError, "Errore di estrazione o pulizia",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    } else if (wasCancelled) {
                        lblStatus.Text = "Annullato e ripulito";
                        Log("Operazione annullata: file temporanei eliminati. I file precedenti sono intatti.");
                    } else {
                        progress.Value = 1000;
                        lblStatus.Text = "Completato";
                        Log("Tutte le tracce selezionate sono state estratte.");
                        MessageBox.Show("Estrazione completata!\r\n\r\n" + destination,
                                        "UVR Audio Track Extractor", MessageBoxButtons.OK,
                                        MessageBoxIcon.Information);
                    }
                    if (closeWhenStopped) {
                        closeWhenStopped = false;
                        Close();
                    }
                });
            }
        }

        private void CancelExtraction()
        {
            cancelRequested = true;
            btnStop.Enabled = false;
            lblStatus.Text = "Annullamento e pulizia...";
            lock (processLock) {
                try {
                    if (activeProcess != null && !activeProcess.HasExited) activeProcess.Kill();
                } catch (Exception) { }
            }
        }

        private void OnUI(Action action)
        {
            if (IsDisposed) return;
            if (!InvokeRequired) {
                action();
                return;
            }
            if (!IsHandleCreated) return;
            try { BeginInvoke((MethodInvoker)delegate { if (!IsDisposed) action(); }); }
            catch (InvalidOperationException) { }
        }

        private void SetProgress(double fraction, string label)
        {
            int value = (int)Math.Round(Math.Max(0, Math.Min(1, fraction)) * 1000);
            OnUI(delegate {
                progress.Value = value;
                lblStatus.Text = label + " - " + (value / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            });
        }

        private void Log(string message)
        {
            OnUI(delegate {
                txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine);
            });
        }
    }
}
