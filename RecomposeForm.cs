using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace UVRAudioTrackExtractor
{
    // Ricompone sei canali mono (o WAV stereo da UVR, convertiti in mono)
    // in un'unica traccia 5.1, anche partendo da cartelle di chunk elaborati.
    public sealed class RecomposeForm : Form
    {
        private sealed class Source
        {
            public string Name;
            public string Path;
            public List<string> Files = new List<string>();
            public List<double> Durations = new List<double>();
            public bool IsChunks { get { return Files.Count > 1; } }
            public double Duration { get { return Durations.Sum(); } }
        }

        private sealed class AudioInfo
        {
            public double Duration;
            public int Channels;
            public int SampleRate;
        }

        private readonly string ffmpeg, ffprobe;
        private readonly TextBox[] inputBoxes = new TextBox[7];
        private readonly Label[] inputLabels = new Label[7];
        private TextBox txtOutput, txtLog;
        private ComboBox cmbLayout;
        private NumericUpDown numCenter;
        private TrackBar trkVoiceDb;
        private Label lblVoiceDb, lblPreviewStatus;
        private TextBox txtPreviewPosition;
        private Button btnPreview, btnStopPreview;
        private SoundPlayer previewPlayer;
        private string previewFolder;
        private Process previewProcess;
        private readonly object previewLock = new object();
        private int previewGeneration;
        private bool previewBusy;
        private const int PreviewSeconds = 12;
        private Button btnStart, btnCancel, btnOutput, btnAuto;
        private ProgressBar progress;
        private Label lblStatus;
        private volatile bool cancelRequested;
        private volatile Process activeProcess;
        private readonly object processLock = new object();
        private bool busy, closeAfterCancel;

        public RecomposeForm(string ffmpegPath, string ffprobePath)
        {
            ffmpeg = ffmpegPath;
            ffprobe = ffprobePath;
            Text = "UVR Audio Track Extractor V5 - Ricomponi audio 5.1";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(980, 780);
            Size = new Size(1120, 895);
            Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(246, 248, 251);
            BuildUI();
            Log("Ricomposizione offline pronta. Seleziona 6 WAV o 6 cartelle di chunk UVR.");
        }

        private void BuildUI()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(14);
            root.ColumnCount = 1;
            root.RowCount = 8;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 298));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            Label title = new Label();
            title.Text = "Ricostruzione audio 5.1 da WAV / chunk UVR";
            title.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(32, 55, 84);
            title.Dock = DockStyle.Fill;
            root.Controls.Add(title, 0, 0);

            FlowLayoutPanel toolbar = new FlowLayoutPanel();
            toolbar.Dock = DockStyle.Fill;
            root.Controls.Add(toolbar, 0, 1);
            btnAuto = new Button();
            btnAuto.Text = "Riconosci automaticamente i 6 canali da una cartella...";
            btnAuto.AutoSize = true;
            btnAuto.Click += AutoDetect;
            toolbar.Controls.Add(btnAuto);
            Label toolbarHint = new Label();
            toolbarHint.Text = "Oppure scegli un file WAV o una cartella chunk per ogni canale.";
            toolbarHint.AutoSize = true;
            toolbarHint.Margin = new Padding(12, 7, 0, 0);
            toolbar.Controls.Add(toolbarHint);

            GroupBox sourceGroup = new GroupBox();
            sourceGroup.Text = "1. Sorgenti: WAV completi o cartelle contenenti i chunk UVR elaborati";
            sourceGroup.Dock = DockStyle.Fill;
            root.Controls.Add(sourceGroup, 0, 2);
            TableLayoutPanel rows = new TableLayoutPanel();
            rows.Dock = DockStyle.Fill;
            rows.Padding = new Padding(9, 8, 9, 8);
            rows.ColumnCount = 4;
            rows.RowCount = 8;
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
            for (int i = 0; i < 7; i++)
                rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
            rows.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            sourceGroup.Controls.Add(rows);

            string[] labels = { "FL (sinistro)", "FR (destro)", "FC (centrale)", "LFE (bassi)",
                                "SL (surround S)", "SR (surround D)", "VOCE ITA (opt.)" };
            for (int i = 0; i < 7; i++)
            {
                int index = i;
                Label lab = new Label();
                lab.Text = labels[i];
                lab.TextAlign = ContentAlignment.MiddleLeft;
                lab.Dock = DockStyle.Fill;
                inputLabels[i] = lab;
                rows.Controls.Add(lab, 0, i);

                TextBox box = new TextBox();
                box.Dock = DockStyle.Fill;
                box.Margin = new Padding(2, 5, 3, 2);
                box.ReadOnly = true;
                box.TextChanged += delegate { StopPreview(); };
                inputBoxes[i] = box;
                rows.Controls.Add(box, 1, i);

                Button wav = new Button();
                wav.Text = "File WAV...";
                wav.Dock = DockStyle.Fill;
                wav.Click += delegate { BrowseWav(index); };
                rows.Controls.Add(wav, 2, i);

                Button chunks = new Button();
                chunks.Text = "Chunk...";
                chunks.Dock = DockStyle.Fill;
                chunks.Click += delegate { BrowseChunks(index); };
                rows.Controls.Add(chunks, 3, i);
            }

            Label hint = new Label();
            hint.Text = "VOCE ITA è facoltativa: se presente viene mixata nel centrale FC. Per eliminarla, usa il pulsante Svuota voce.";
            hint.ForeColor = Color.FromArgb(70, 80, 94);
            hint.Dock = DockStyle.Fill;
            rows.SetColumnSpan(hint, 4);
            rows.Controls.Add(hint, 0, 7);

            GroupBox optsGroup = new GroupBox();
            optsGroup.Text = "2. Layout e mix del centrale";
            optsGroup.Dock = DockStyle.Fill;
            root.Controls.Add(optsGroup, 0, 3);
            FlowLayoutPanel opts = new FlowLayoutPanel();
            opts.Dock = DockStyle.Fill;
            opts.Padding = new Padding(10, 6, 4, 2);
            opts.WrapContents = true;
            optsGroup.Controls.Add(opts);
            opts.Controls.Add(MakeText("Layout surround:"));
            cmbLayout = new ComboBox();
            cmbLayout.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLayout.Width = 130;
            cmbLayout.Items.Add("5.1(side) - SL/SR");
            cmbLayout.Items.Add("5.1(back) - BL/BR");
            cmbLayout.SelectedIndex = 0;
            cmbLayout.SelectedIndexChanged += delegate { UpdateLabels(); StopPreview(); };
            opts.Controls.Add(cmbLayout);

            opts.Controls.Add(MakeText("FC originale:"));
            numCenter = MakeGain(1.00M);
            numCenter.ValueChanged += delegate { StopPreview(); };
            opts.Controls.Add(numCenter);
            opts.Controls.Add(MakeText("Voce italiana:"));
            trkVoiceDb = new TrackBar();
            trkVoiceDb.Minimum = -12;
            trkVoiceDb.Maximum = 12;
            trkVoiceDb.TickFrequency = 3;
            trkVoiceDb.SmallChange = 1;
            trkVoiceDb.LargeChange = 3;
            trkVoiceDb.Value = 6;
            trkVoiceDb.Width = 185;
            trkVoiceDb.Height = 42;
            trkVoiceDb.AutoSize = false;
            trkVoiceDb.Margin = new Padding(0, 0, 0, 0);
            opts.Controls.Add(trkVoiceDb);
            lblVoiceDb = MakeText("+6 dB");
            lblVoiceDb.Width = 58;
            lblVoiceDb.Margin = new Padding(2, 9, 2, 2);
            opts.Controls.Add(lblVoiceDb);
            trkVoiceDb.ValueChanged += delegate { UpdateVoiceDbLabel(); StopPreview(); };
            Button clearVoice = new Button();
            clearVoice.Text = "Svuota voce";
            clearVoice.AutoSize = true;
            clearVoice.Click += delegate { inputBoxes[6].Text = ""; };
            opts.Controls.Add(clearVoice);
            Label note = new Label();
            note.AutoSize = true;
            note.Text = "Senza voce opzionale, i sei canali restano separati e non viene applicato alcun mix.";
            note.Margin = new Padding(12, 9, 2, 0);
            opts.Controls.Add(note);

            GroupBox previewGroup = new GroupBox();
            previewGroup.Text = "3. Anteprima audio (12 secondi, ascolto stereo del mix 5.1)";
            previewGroup.Dock = DockStyle.Fill;
            root.Controls.Add(previewGroup, 0, 4);
            FlowLayoutPanel previewRow = new FlowLayoutPanel();
            previewRow.Dock = DockStyle.Fill;
            previewRow.Padding = new Padding(10, 8, 4, 2);
            previewRow.WrapContents = true;
            previewGroup.Controls.Add(previewRow);
            previewRow.Controls.Add(MakeText("Inizio (hh:mm:ss):"));
            txtPreviewPosition = new TextBox();
            txtPreviewPosition.Text = "00:01:00";
            txtPreviewPosition.Width = 85;
            txtPreviewPosition.Margin = new Padding(0, 5, 10, 0);
            txtPreviewPosition.TextChanged += delegate { StopPreview(); };
            previewRow.Controls.Add(txtPreviewPosition);
            btnPreview = new Button();
            btnPreview.Text = "Ascolta anteprima";
            btnPreview.AutoSize = true;
            btnPreview.Click += StartPreview;
            previewRow.Controls.Add(btnPreview);
            btnStopPreview = new Button();
            btnStopPreview.Text = "Ferma audio";
            btnStopPreview.AutoSize = true;
            btnStopPreview.Enabled = false;
            btnStopPreview.Click += delegate { StopPreview(); };
            previewRow.Controls.Add(btnStopPreview);
            lblPreviewStatus = MakeText("Seleziona un punto del film. Non modifica i sorgenti.");
            previewRow.Controls.Add(lblPreviewStatus);

            GroupBox targetGroup = new GroupBox();
            targetGroup.Text = "4. File audio 5.1 da creare (WAV 24 bit, FLAC 24 bit o AC3 640 kbps)";
            targetGroup.Dock = DockStyle.Fill;
            root.Controls.Add(targetGroup, 0, 5);
            TableLayoutPanel target = new TableLayoutPanel();
            target.Dock = DockStyle.Fill;
            target.Padding = new Padding(8, 8, 8, 8);
            target.ColumnCount = 2;
            target.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            target.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
            targetGroup.Controls.Add(target);
            txtOutput = new TextBox();
            txtOutput.Dock = DockStyle.Fill;
            txtOutput.ReadOnly = true;
            txtOutput.Margin = new Padding(4, 9, 4, 0);
            target.Controls.Add(txtOutput, 0, 0);
            btnOutput = new Button();
            btnOutput.Text = "Salva come...";
            btnOutput.Dock = DockStyle.Fill;
            btnOutput.Click += ChooseOutput;
            target.Controls.Add(btnOutput, 1, 0);

            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.ColumnCount = 4;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 195));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
            root.Controls.Add(actions, 0, 6);
            btnStart = new Button();
            btnStart.Text = "RICOMPONI AUDIO 5.1";
            btnStart.Dock = DockStyle.Fill;
            btnStart.BackColor = Color.FromArgb(34, 127, 107);
            btnStart.ForeColor = Color.White;
            btnStart.FlatStyle = FlatStyle.Flat;
            btnStart.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            btnStart.Click += StartRecompose;
            actions.Controls.Add(btnStart, 0, 0);
            btnCancel = new Button();
            btnCancel.Text = "Annulla";
            btnCancel.Dock = DockStyle.Fill;
            btnCancel.Enabled = false;
            btnCancel.Click += delegate { CancelRecompose(); };
            actions.Controls.Add(btnCancel, 1, 0);
            progress = new ProgressBar();
            progress.Minimum = 0;
            progress.Maximum = 1000;
            progress.Dock = DockStyle.Fill;
            progress.Margin = new Padding(12, 11, 10, 10);
            actions.Controls.Add(progress, 2, 0);
            lblStatus = new Label();
            lblStatus.Text = "In attesa";
            lblStatus.Dock = DockStyle.Fill;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            actions.Controls.Add(lblStatus, 3, 0);

            GroupBox logs = new GroupBox();
            logs.Text = "Registro operazioni";
            logs.Dock = DockStyle.Fill;
            root.Controls.Add(logs, 0, 7);
            txtLog = new TextBox();
            txtLog.Dock = DockStyle.Fill;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(24, 32, 45);
            txtLog.ForeColor = Color.FromArgb(228, 237, 247);
            txtLog.Font = new Font("Consolas", 9F);
            logs.Controls.Add(txtLog);

            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                StopPreview();
                if (!busy) return;
                DialogResult result = MessageBox.Show(
                    "Annullare la ricomposizione, cancellare i temporanei e chiudere?",
                    "Operazione in corso", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                e.Cancel = true;
                if (result == DialogResult.Yes) {
                    closeAfterCancel = true;
                    CancelRecompose();
                }
            };
        }

        private static Label MakeText(string value)
        {
            Label label = new Label();
            label.Text = value;
            label.AutoSize = true;
            label.Margin = new Padding(12, 9, 4, 2);
            return label;
        }

        private static NumericUpDown MakeGain(decimal value)
        {
            NumericUpDown n = new NumericUpDown();
            n.Minimum = 0;
            n.Maximum = 2;
            n.DecimalPlaces = 2;
            n.Increment = 0.05M;
            n.Value = value;
            n.Width = 64;
            n.Margin = new Padding(1, 4, 2, 2);
            return n;
        }

        private void UpdateVoiceDbLabel()
        {
            int db = trkVoiceDb.Value;
            lblVoiceDb.Text = (db > 0 ? "+" : "") +
                              db.ToString(CultureInfo.InvariantCulture) + " dB";
        }

        private void UpdateLabels()
        {
            bool back = cmbLayout.SelectedIndex == 1;
            inputLabels[4].Text = back ? "BL (surround S)" : "SL (surround S)";
            inputLabels[5].Text = back ? "BR (surround D)" : "SR (surround D)";
        }

        private void BrowseWav(int index)
        {
            if (busy) return;
            using (OpenFileDialog d = new OpenFileDialog()) {
                d.Title = "Scegli WAV per " + inputLabels[index].Text;
                d.Filter = "File audio WAV|*.wav|Tutti i file audio|*.wav;*.flac;*.aiff;*.aif";
                if (d.ShowDialog(this) == DialogResult.OK)
                    inputBoxes[index].Text = d.FileName;
            }
        }

        private void BrowseChunks(int index)
        {
            if (busy) return;
            using (FolderBrowserDialog d = new FolderBrowserDialog()) {
                d.Description = "Cartella contenente solo i chunk WAV di " + inputLabels[index].Text;
                if (d.ShowDialog(this) == DialogResult.OK)
                    inputBoxes[index].Text = d.SelectedPath;
            }
        }

        private void ChooseOutput(object sender, EventArgs e)
        {
            if (busy) return;
            using (SaveFileDialog d = new SaveFileDialog()) {
                d.Title = "Salva il 5.1 ricomposto";
                d.Filter = "FLAC 5.1 lossless (*.flac)|*.flac|WAV PCM 24 bit (*.wav)|*.wav|AC3 5.1 640 kbps (*.ac3)|*.ac3";
                d.FilterIndex = 1;
                d.DefaultExt = "";
                d.AddExtension = true;
                d.OverwritePrompt = false; // Non distruggere mai un risultato precedente.
                d.FileName = "ITALIANO_5.1_RICOMPOSTO";
                if (d.ShowDialog(this) == DialogResult.OK) {
                    if (File.Exists(d.FileName)) {
                        MessageBox.Show("Il file esiste gia. Scegli un nuovo nome.", "Protezione file");
                        return;
                    }
                    txtOutput.Text = d.FileName;
                }
            }
        }

        // Auto-detect prudente: non indovina se trova piu' di una serie per canale.
        private void AutoDetect(object sender, EventArgs e)
        {
            if (busy) return;
            using (FolderBrowserDialog d = new FolderBrowserDialog()) {
                d.Description = "Cartella principale dei sei canali (anche con sottocartelle UVR_CHUNKS)";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string root = d.SelectedPath;
                try {
                    string[] wavs = Directory.GetFiles(root, "*.wav", SearchOption.AllDirectories);
                    string[] names = cmbLayout.SelectedIndex == 1
                        ? new string[] { "FL", "FR", "FC", "LFE", "BL", "BR" }
                        : new string[] { "FL", "FR", "FC", "LFE", "SL", "SR" };
                    string[] detected = new string[6];
                    for (int i = 0; i < 6; i++) {
                        string channel = names[i];
                        List<string> matching = wavs.Where(path =>
                            Regex.IsMatch(Path.GetFileNameWithoutExtension(path),
                                @"(^|[_\-.])" + channel + @"([_\-.]|$)",
                                RegexOptions.IgnoreCase)).ToList();
                        if (matching.Count == 0)
                            throw new IOException("Non ho trovato il canale " + channel + ".");
                        List<IGrouping<string, string>> groups = matching
                            .GroupBy(p => Path.GetDirectoryName(p), StringComparer.OrdinalIgnoreCase).ToList();
                        if (groups.Count != 1)
                            throw new IOException("Piu' cartelle possibili per " + channel +
                                ": scegli manualmente il file o la cartella corretta.");
                        if (matching.Count == 1) detected[i] = matching[0];
                        else {
                            // Una cartella deve contenere solo una sequenza di chunk WAV.
                            ValidateChunkNumbers(matching);
                            detected[i] = groups[0].Key;
                        }
                    }
                    for (int i = 0; i < 6; i++) inputBoxes[i].Text = detected[i];
                    Log("Riconosciuti automaticamente 6 canali. Controlla i percorsi prima di avviare.");
                } catch (Exception ex) {
                    MessageBox.Show(ex.Message + "\r\n\r\nPuoi scegliere i sei canali manualmente.",
                        "Riconoscimento incompleto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private static int ChunkNumber(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Match m = Regex.Match(name, @"(?:^|[_\-.])chunk[_-]?(\d+)(?:[_\-.]|$)",
                                  RegexOptions.IgnoreCase);
            if (!m.Success) return -1;
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static List<string> ValidateChunkNumbers(IEnumerable<string> paths)
        {
            List<string> files = paths.ToList();
            if (files.Count < 2) return files;
            List<KeyValuePair<int, string>> indexed = new List<KeyValuePair<int, string>>();
            foreach (string file in files) {
                int index = ChunkNumber(file);
                if (index < 0)
                    throw new IOException("File senza indice '_chunk_0000': " + file);
                indexed.Add(new KeyValuePair<int, string>(index, file));
            }
            indexed.Sort((a, b) => a.Key.CompareTo(b.Key));
            for (int i = 1; i < indexed.Count; i++) {
                if (indexed[i].Key != indexed[i - 1].Key + 1)
                    throw new IOException("Chunk mancanti o duplicati tra " +
                        Path.GetFileName(indexed[i - 1].Value) + " e " +
                        Path.GetFileName(indexed[i].Value));
            }
            return indexed.Select(x => x.Value).ToList();
        }

        private static Source LoadSource(string path, string name)
        {
            Source source = new Source();
            source.Path = path;
            source.Name = name;
            if (File.Exists(path)) {
                source.Files.Add(path);
            } else if (Directory.Exists(path)) {
                List<string> files = Directory.GetFiles(path, "*.wav", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count == 0)
                    throw new IOException("Nessun WAV nella cartella " + name + ": " + path);
                if (files.Count == 1) source.Files.Add(files[0]);
                else source.Files = ValidateChunkNumbers(files);
            } else throw new IOException("Sorgente non trovata per " + name + ": " + path);
            return source;
        }

        private void RegisterProcess(Process proc)
        {
            lock (processLock) activeProcess = proc;
            if (cancelRequested) {
                try { if (!proc.HasExited) proc.Kill(); } catch (Exception) { }
                throw new OperationCanceledException();
            }
        }

        private void ClearProcess(Process proc)
        {
            lock (processLock) {
                if (activeProcess == proc) activeProcess = null;
            }
        }

        private AudioInfo ProbeAudio(string path)
        {
            if (cancelRequested) throw new OperationCanceledException();
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = ffprobe;
            psi.Arguments = "-v error -select_streams a:0 -show_entries " +
                "stream=channels,sample_rate:format=duration -of json " + Quote(path);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            string json, error;
            using (Process proc = new Process()) {
                proc.StartInfo = psi;
                proc.Start();
                RegisterProcess(proc);
                try {
                    json = proc.StandardOutput.ReadToEnd();
                    error = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    if (cancelRequested) throw new OperationCanceledException();
                    if (proc.ExitCode != 0)
                        throw new IOException("FFprobe: " + Path.GetFileName(path) + ": " + error);
                } finally { ClearProcess(proc); }
            }
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> data =
                serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (data == null) throw new IOException("Risposta ffprobe non valida: " + path);
            object rawStreams, rawFormat;
            if (!data.TryGetValue("streams", out rawStreams))
                throw new IOException("Nessuna traccia audio: " + path);
            object[] streams = rawStreams as object[];
            if (streams == null || streams.Length == 0)
                throw new IOException("Nessuna traccia audio: " + path);
            Dictionary<string, object> stream = streams[0] as Dictionary<string, object>;
            Dictionary<string, object> format = data.TryGetValue("format", out rawFormat)
                ? rawFormat as Dictionary<string, object> : null;
            AudioInfo info = new AudioInfo();
            info.Channels = Convert.ToInt32(ReadValue(stream, "channels", "0"), CultureInfo.InvariantCulture);
            info.SampleRate = Convert.ToInt32(ReadValue(stream, "sample_rate", "0"), CultureInfo.InvariantCulture);
            string durationText = ReadValue(format, "duration", "0");
            if (!double.TryParse(durationText, NumberStyles.Float, CultureInfo.InvariantCulture,
                                 out info.Duration) || info.Duration <= 0)
                throw new IOException("Durata audio non rilevabile: " + path);
            if (info.Channels < 1 || info.Channels > 2)
                throw new IOException("Ogni ingresso deve essere mono o stereo: " + path +
                    " (" + info.Channels + " canali).");
            if (info.SampleRate < 8000)
                throw new IOException("Frequenza audio non valida: " + path);
            return info;
        }

        private static string ReadValue(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null) return fallback;
            return Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        private static string Quote(string text)
        {
            return "\"" + text.Replace("\"", "\\\"") + "\"";
        }

        private static string Number(double n)
        {
            return n.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static void ValidateDurations(List<Source> sources)
        {
            Source center = sources[2];
            foreach (Source s in sources) {
                if (s == center) continue;
                if (Math.Abs(s.Duration - center.Duration) > (s.Name == "VOCE" ? 0.5 : 0.25))
                    throw new IOException("Durate non sincronizzate: FC=" +
                        Number(center.Duration) + " s, " + s.Name + "=" +
                        Number(s.Duration) + " s. Controlla i chunk UVR e i ritardi.");
                if (s.Name == "VOCE") continue;
                if (s.IsChunks && center.IsChunks) {
                    if (s.Files.Count != center.Files.Count)
                        throw new IOException("Numero chunk diverso: " + s.Name + " (" +
                            s.Files.Count + "), FC (" + center.Files.Count + ").");
                    for (int i = 0; i < s.Durations.Count; i++) {
                        if (Math.Abs(s.Durations[i] - center.Durations[i]) > 0.10)
                            throw new IOException("Chunk " + i + " non allineato: " + s.Name +
                                " / FC. Differenza superiore a 100 ms.");
                    }
                }
            }
        }

        private static string MakeConcatFile(Source source, string staging, int id)
        {
            string listPath = Path.Combine(staging, "input_" + id + ".ffconcat");
            StringBuilder list = new StringBuilder();
            list.AppendLine("ffconcat version 1.0");
            foreach (string path in source.Files) {
                string escaped = Path.GetFullPath(path).Replace('\\', '/').Replace("'", "'\\''");
                list.Append("file '").Append(escaped).AppendLine("'");
            }
            File.WriteAllText(listPath, list.ToString(), new UTF8Encoding(false));
            return listPath;
        }

        private string MakeFFmpegArgs(List<Source> sources, string staging, string stagedOutput,
                                      string extension, double duration, bool back,
                                      decimal centerGain, decimal voiceGain,
                                      bool preview = false, double previewStart = 0)
        {
            StringBuilder args = new StringBuilder();
            // Un solo thread per il grafo evita troncamenti sporadici di pochi
            // millisecondi osservati con join + FLAC su alcune versioni FFmpeg.
            args.Append("-hide_banner -loglevel error -nostdin -nostats -filter_complex_threads 1 ");
            args.Append(preview ? "-y " : "-progress pipe:1 -n ");
            for (int i = 0; i < sources.Count; i++) {
                Source s = sources[i];
                if (preview)
                    args.Append("-ss ").Append(Number(previewStart)).Append(" -t ")
                        .Append(Number(duration)).Append(" ");
                if (s.IsChunks) {
                    string concat = MakeConcatFile(s, staging, i);
                    args.Append("-f concat -safe 0 -i ").Append(Quote(concat)).Append(" ");
                } else {
                    args.Append("-i ").Append(Quote(s.Files[0])).Append(" ");
                }
            }
            string layout = back ? "5.1" : "5.1(side)";
            string[] channels = back
                ? new string[] { "FL", "FR", "FC", "LFE", "BL", "BR" }
                : new string[] { "FL", "FR", "FC", "LFE", "SL", "SR" };
            StringBuilder filter = new StringBuilder();
            for (int i = 0; i < sources.Count; i++) {
                if (i > 0) filter.Append(";");
                filter.Append("[").Append(i).Append(":a:0]")
                      .Append("aresample=48000,")
                      .Append("aformat=sample_fmts=fltp:channel_layouts=mono,")
                      .Append("asetpts=N/SR/TB,apad,atrim=duration=").Append(Number(duration));
                if (i == 2 && sources.Count == 7)
                    filter.Append(",volume=").Append(Number((double)centerGain));
                if (i == 6)
                    filter.Append(",volume=").Append(Number((double)voiceGain));
                filter.Append("[x").Append(i).Append("]");
            }
            if (sources.Count == 7) {
                filter.Append(";[x2][x6]amix=inputs=2:duration=first:normalize=0,")
                      .Append("alimiter=limit=0.95:latency=1[center]");
            }
            filter.Append(";");
            for (int i = 0; i < 6; i++)
                filter.Append(i == 2 && sources.Count == 7 ? "[center]" : "[x" + i + "]");
            // amerge + channelmap esplicito: ordine fisso e campioni completi.
            // join su alcune build FFmpeg puo' terminare con anticipo di un frame.
            filter.Append("amerge=inputs=6,channelmap=map=");
            for (int i = 0; i < 6; i++) {
                if (i > 0) filter.Append("|");
                filter.Append(i).Append("-").Append(channels[i]);
            }
            filter.Append(":channel_layout=").Append(layout);
            if (preview) {
                // L'anteprima riproduce lo stesso mix e lo converte in stereo
                // con pesi conservativi per ascoltarlo anche su un PC non 5.1.
                string leftSurround = back ? "BL" : "SL";
                string rightSurround = back ? "BR" : "SR";
                filter.Append("[surround];[surround]pan=stereo|c0=0.55*FL+0.38*FC+0.25*")
                      .Append(leftSurround).Append("+0.08*LFE|c1=0.55*FR+0.38*FC+0.25*")
                      .Append(rightSurround)
                      .Append("+0.08*LFE,alimiter=limit=0.95:latency=1[out]");
            } else filter.Append("[out]");
            args.Append("-filter_complex ").Append(Quote(filter.ToString()))
                .Append(" -map ").Append(Quote("[out]"))
                .Append(" -ar 48000 ");
            if (preview) args.Append("-c:a pcm_s16le -ac 2 ");
            else if (extension == ".wav") args.Append("-c:a pcm_s24le ");
            else if (extension == ".flac")
                args.Append("-c:a flac -sample_fmt s32 -bits_per_raw_sample 24 -compression_level 8 ");
            else if (extension == ".ac3") args.Append("-c:a ac3 -b:a 640k ");
            else throw new IOException("Formato di uscita non supportato: " + extension);
            args.Append(Quote(stagedOutput));
            return args.ToString();
        }

        private static double ParseProgress(string time)
        {
            TimeSpan parsed;
            return TimeSpan.TryParse(time, CultureInfo.InvariantCulture, out parsed)
                ? parsed.TotalSeconds : 0;
        }


        private static double ParsePreviewPosition(string text)
        {
            string[] parts = text.Trim().Split(':');
            if (parts.Length != 3)
                throw new FormatException("Inserisci il punto d'inizio come hh:mm:ss (es. 01:28:53).");
            int hours, minutes, seconds;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seconds) ||
                hours < 0 || hours > 23 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59)
                throw new FormatException("Orario non valido. Usa hh:mm:ss (es. 01:28:53).");
            return new TimeSpan(hours, minutes, seconds).TotalSeconds;
        }

        // Cancella l'anteprima precedente, anche durante la generazione.
        // Chiamato solo dal thread dell'interfaccia; la generazione e' in background.
        private void StopPreview()
        {
            Interlocked.Increment(ref previewGeneration);
            lock (previewLock) {
                try {
                    if (previewProcess != null && !previewProcess.HasExited)
                        previewProcess.Kill();
                } catch (Exception) { }
            }
            if (previewPlayer != null) {
                try { previewPlayer.Stop(); previewPlayer.Dispose(); } catch (Exception) { }
                previewPlayer = null;
            }
            if (previewFolder != null) {
                DeletePreviewFolder(previewFolder);
                previewFolder = null;
            }
            previewBusy = false;
            if (btnPreview != null) btnPreview.Enabled = !busy;
            if (btnStopPreview != null) btnStopPreview.Enabled = false;
            if (lblPreviewStatus != null) lblPreviewStatus.Text = "Pronto per una nuova anteprima.";
        }

        private static void DeletePreviewFolder(string folder)
        {
            try { if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void StartPreview(object sender, EventArgs e)
        {
            if (busy || previewBusy) return;
            double start;
            try { start = ParsePreviewPosition(txtPreviewPosition.Text); }
            catch (FormatException ex) {
                MessageBox.Show(ex.Message, "Punto d'inizio anteprima", MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                return;
            }
            string[] paths = inputBoxes.Select(t => t.Text.Trim()).ToArray();
            if (paths.Take(6).Any(string.IsNullOrEmpty)) {
                MessageBox.Show("Seleziona i 6 canali (FL, FR, FC, LFE e surround) per ascoltare il mix.",
                    "Anteprima", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            StopPreview();
            int job = Interlocked.Increment(ref previewGeneration);
            decimal centerGain = numCenter.Value;
            decimal voiceGain = (decimal)Math.Pow(10.0, trkVoiceDb.Value / 20.0);
            bool back = cmbLayout.SelectedIndex == 1;
            previewBusy = true;
            btnPreview.Enabled = false;
            btnStopPreview.Enabled = true;
            lblPreviewStatus.Text = "Preparazione dell'estratto in corso...";
            // Specifica ThreadStart: il compilatore .NET Framework altrimenti
            // trova ambigua la scelta fra ThreadStart e ParameterizedThreadStart.
            Thread worker = new Thread(new ThreadStart(delegate {
                GeneratePreview(job, paths, start, back, centerGain, voiceGain);
            }));
            worker.IsBackground = true;
            worker.Start();
        }

        private void GeneratePreview(int job, string[] paths, double start, bool back,
                                     decimal centerGain, decimal voiceGain)
        {
            string folder = null;
            try {
                if (job != Volatile.Read(ref previewGeneration)) return;
                string[] names = back
                    ? new string[] { "FL", "FR", "FC", "LFE", "BL", "BR", "VOCE" }
                    : new string[] { "FL", "FR", "FC", "LFE", "SL", "SR", "VOCE" };
                List<Source> sources = new List<Source>();
                for (int i = 0; i < paths.Length; i++) {
                    if (i == 6 && string.IsNullOrWhiteSpace(paths[i])) continue;
                    sources.Add(LoadSource(paths[i], names[i]));
                }
                folder = Path.Combine(Path.GetTempPath(), "UVR_PREVIEW_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                string wave = Path.Combine(folder, "anteprima_stereo.wav");
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ffmpeg;
                psi.Arguments = MakeFFmpegArgs(sources, folder, wave, ".wav", PreviewSeconds,
                                                back, centerGain, voiceGain, true, start);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                string error;
                using (Process proc = new Process()) {
                    proc.StartInfo = psi;
                    proc.Start();
                    lock (previewLock) {
                        previewProcess = proc;
                        if (job != Volatile.Read(ref previewGeneration)) {
                            try { proc.Kill(); } catch (Exception) { }
                        }
                    }
                    try {
                        // L'anteprima non usa -progress: stderr contiene solo gli errori.
                        error = proc.StandardError.ReadToEnd();
                        proc.StandardOutput.ReadToEnd();
                        proc.WaitForExit();
                        if (job != Volatile.Read(ref previewGeneration)) return;
                        if (proc.ExitCode != 0)
                            throw new IOException("FFmpeg (anteprima): " + error);
                    } finally {
                        lock (previewLock) {
                            if (previewProcess == proc) previewProcess = null;
                        }
                    }
                }
                if (!File.Exists(wave) || new FileInfo(wave).Length < 4096)
                    throw new IOException("L'anteprima audio non e' stata prodotta correttamente.");
                string readyFolder = folder;
                OnUI(delegate {
                    if (job != Volatile.Read(ref previewGeneration)) {
                        DeletePreviewFolder(readyFolder);
                        return;
                    }
                    try {
                        SoundPlayer player = new SoundPlayer(Path.Combine(readyFolder, "anteprima_stereo.wav"));
                        player.Load();
                        previewPlayer = player;
                        previewFolder = readyFolder;
                        player.Play();
                        previewBusy = false;
                        btnPreview.Enabled = true;
                        btnStopPreview.Enabled = true;
                        lblPreviewStatus.Text = "Riproduzione stereo: " + PreviewSeconds + " s (mix 5.1).";
                    } catch (Exception ex) {
                        previewBusy = false;
                        btnPreview.Enabled = true;
                        btnStopPreview.Enabled = false;
                        DeletePreviewFolder(readyFolder);
                        MessageBox.Show(ex.Message, "Riproduzione anteprima", MessageBoxButtons.OK,
                                        MessageBoxIcon.Error);
                    }
                });
                folder = null; // La cartella passa al player, che la elimina con Ferma audio.
            } catch (Exception ex) {
                string failure = ex.Message;
                OnUI(delegate {
                    if (job != Volatile.Read(ref previewGeneration)) return;
                    previewBusy = false;
                    btnPreview.Enabled = true;
                    btnStopPreview.Enabled = false;
                    lblPreviewStatus.Text = "Impossibile preparare l'anteprima.";
                    MessageBox.Show(failure, "Anteprima audio", MessageBoxButtons.OK,
                                    MessageBoxIcon.Error);
                });
            } finally {
                if (folder != null) DeletePreviewFolder(folder);
            }
        }

        private void StartRecompose(object sender, EventArgs e)
        {
            if (busy) return;
            StopPreview();
            string[] paths = inputBoxes.Select(t => t.Text.Trim()).ToArray();
            if (paths.Take(6).Any(string.IsNullOrEmpty)) {
                MessageBox.Show("Seleziona FL, FR, FC, LFE e i due canali surround.", "Sorgenti mancanti");
                return;
            }
            if (string.IsNullOrWhiteSpace(txtOutput.Text)) {
                MessageBox.Show("Scegli il file di uscita WAV, FLAC o AC3.", "Uscita mancante");
                return;
            }
            if (File.Exists(txtOutput.Text)) {
                MessageBox.Show("Il file di uscita esiste gia. Scegli un nome diverso.",
                    "File protetto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string extension = Path.GetExtension(txtOutput.Text).ToLowerInvariant();
            if (extension != ".wav" && extension != ".flac" && extension != ".ac3") {
                MessageBox.Show("Scegli WAV, FLAC o AC3.", "Formato non supportato");
                return;
            }
            string output = txtOutput.Text;
            bool back = cmbLayout.SelectedIndex == 1;
            decimal centerGain = numCenter.Value;
            // FFmpeg volume usa un fattore lineare, mentre lo slider mostra dB.
            // +6 dB = 10^(6/20) = ~1.9953, senza arrotondamento a 2.00.
            decimal voiceGain = (decimal)Math.Pow(10.0, trkVoiceDb.Value / 20.0);
            busy = true;
            cancelRequested = false;
            btnStart.Enabled = false;
            btnCancel.Enabled = true;
            btnOutput.Enabled = false;
            btnAuto.Enabled = false;
            btnPreview.Enabled = false;
            progress.Value = 0;
            lblStatus.Text = "Verifica sorgenti...";
            Thread worker = new Thread(delegate() {
                RunRecompose(paths, output, extension, back, centerGain, voiceGain);
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void RunRecompose(string[] paths, string output, string extension,
                                  bool back, decimal centerGain, decimal voiceGain)
        {
            string error = null;
            bool cancelled = false, completed = false;
            string staging = null;
            try {
                string[] names = back
                    ? new string[] { "FL", "FR", "FC", "LFE", "BL", "BR", "VOCE" }
                    : new string[] { "FL", "FR", "FC", "LFE", "SL", "SR", "VOCE" };
                List<Source> sources = new List<Source>();
                for (int i = 0; i < paths.Length; i++) {
                    if (i == 6 && string.IsNullOrWhiteSpace(paths[i])) continue;
                    Source source = LoadSource(paths[i], names[i]);
                    sources.Add(source);
                    Log(names[i] + ": " + source.Files.Count + " file WAV.");
                }
                if (sources.Count != 6 && sources.Count != 7)
                    throw new IOException("Servono sei canali e, facoltativamente, la voce.");
                // Evita di sovrascrivere i file sorgenti.
                foreach (Source source in sources)
                    if (source.Files.Any(f => string.Equals(Path.GetFullPath(f),
                        Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("L'uscita coincide con una sorgente: " + output);
                string outputFolder = Path.GetDirectoryName(Path.GetFullPath(output));
                Directory.CreateDirectory(outputFolder);
                staging = Path.Combine(outputFolder, ".UVR_RICOMPONE_TEMP_" +
                                       Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                int fileCount = sources.Sum(s => s.Files.Count);
                int checkedCount = 0;
                foreach (Source source in sources) {
                    foreach (string file in source.Files) {
                        if (cancelRequested) throw new OperationCanceledException();
                        AudioInfo info = ProbeAudio(file);
                        source.Durations.Add(info.Duration);
                        checkedCount++;
                        SetProgress(0.15 * checkedCount / fileCount, "Verifica WAV " +
                                    checkedCount + "/" + fileCount);
                    }
                    Log(source.Name + ": durata " + Number(source.Duration) +
                        " s, " + source.Files.Count + " file.");
                }
                ValidateDurations(sources);
                double duration = sources[2].Duration;
                Log("Durate verificate. Ricomposizione 5.1 " +
                    (back ? "(back)" : "(side)") +
                    (sources.Count == 7 ? " con voce italiana nel centrale." : "."));
                if (cancelRequested) throw new OperationCanceledException();
                string stagedOutput = Path.Combine(staging, "audio_ricomposto" + extension);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = ffmpeg;
                psi.Arguments = MakeFFmpegArgs(sources, staging, stagedOutput, extension,
                                              duration, back, centerGain, voiceGain);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                StringBuilder stderr = new StringBuilder();
                using (Process proc = new Process()) {
                    proc.StartInfo = psi;
                    proc.Start();
                    RegisterProcess(proc);
                    try {
                        Thread errorReader = new Thread(delegate() {
                            try {
                                string line;
                                while ((line = proc.StandardError.ReadLine()) != null) {
                                    if (stderr.Length < 40000) stderr.AppendLine(line);
                                }
                            } catch (Exception) { }
                        });
                        errorReader.IsBackground = true;
                        errorReader.Start();
                        string lineOut;
                        while ((lineOut = proc.StandardOutput.ReadLine()) != null) {
                            if (cancelRequested) {
                                try { if (!proc.HasExited) proc.Kill(); } catch (Exception) { }
                                break;
                            }
                            if (lineOut.StartsWith("out_time=")) {
                                double time = ParseProgress(lineOut.Substring(9));
                                SetProgress(0.15 + 0.84 * Math.Min(1, time / duration),
                                            "Ricomposizione 5.1");
                            }
                        }
                        proc.WaitForExit();
                        errorReader.Join(2000);
                        if (cancelRequested) throw new OperationCanceledException();
                        if (proc.ExitCode != 0)
                            throw new IOException("FFmpeg ha restituito codice " +
                                proc.ExitCode + ":\r\n" + stderr.ToString());
                    } finally { ClearProcess(proc); }
                }
                if (!File.Exists(stagedOutput) || new FileInfo(stagedOutput).Length < 4096)
                    throw new IOException("Il file audio finale non e' stato generato correttamente.");
                if (cancelRequested) throw new OperationCanceledException();
                if (File.Exists(output))
                    throw new IOException("Il file finale e' stato creato da un altro programma: " + output);
                File.Move(stagedOutput, output);
                completed = true;
                SetProgress(1, "Completato");
                Log("Audio 5.1 creato: " + output);
            } catch (OperationCanceledException) {
                cancelled = true;
            } catch (Exception ex) {
                error = ex.Message;
            } finally {
                lock (processLock) activeProcess = null;
                try {
                    if (staging != null && Directory.Exists(staging))
                        Directory.Delete(staging, true);
                } catch (Exception ex) {
                    error = (error == null ? "" : error + "\r\n") +
                        "Pulizia temporanei non riuscita: " + ex.Message +
                        "\r\nCartella: " + staging;
                }
                bool wasCancelled = (cancelled || cancelRequested) && !completed;
                string finalError = error;
                OnUI(delegate {
                    busy = false;
                    btnStart.Enabled = true;
                    btnCancel.Enabled = false;
                    btnOutput.Enabled = true;
                    btnAuto.Enabled = true;
                    btnPreview.Enabled = true;
                    if (finalError != null) {
                        lblStatus.Text = "Errore";
                        Log("ERRORE: " + finalError);
                        MessageBox.Show(finalError, "Ricomposizione 5.1",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    } else if (wasCancelled) {
                        lblStatus.Text = "Annullato e ripulito";
                        Log("Ricomposizione annullata: temporanei eliminati. Sorgenti intatte.");
                    } else {
                        lblStatus.Text = "Completato";
                        MessageBox.Show("Audio 5.1 ricomposto!\r\n\r\n" + output,
                            "Operazione completata", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    if (closeAfterCancel) {
                        closeAfterCancel = false;
                        Close();
                    }
                });
            }
        }

        private void CancelRecompose()
        {
            cancelRequested = true;
            btnCancel.Enabled = false;
            lblStatus.Text = "Annullamento e pulizia...";
            lock (processLock) {
                try {
                    if (activeProcess != null && !activeProcess.HasExited)
                        activeProcess.Kill();
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
            try {
                BeginInvoke((MethodInvoker)delegate {
                    if (!IsDisposed) action();
                });
            } catch (InvalidOperationException) { }
        }

        private void SetProgress(double fraction, string text)
        {
            int n = (int)Math.Round(Math.Max(0, Math.Min(1, fraction)) * 1000);
            OnUI(delegate {
                progress.Value = n;
                lblStatus.Text = text + " " +
                    (n / 10.0).ToString("0.0", CultureInfo.InvariantCulture) + "%";
            });
        }

        private void Log(string message)
        {
            OnUI(delegate {
                txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") +
                    "] " + message + Environment.NewLine);
            });
        }
    }
}
