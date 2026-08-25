using System.Diagnostics;

namespace HowToFish.RouletteTrainer.App;

internal sealed class MainForm : Form
{
    private readonly Label _installStatus = new();
    private readonly Label _processStatus = new();
    private readonly Label _compatibility = new();
    private readonly Label _current = new();
    private readonly Label _hint = new();
    private readonly Label _message = new();
    private readonly TextBox _path = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Dictionary<string, ModeButton> _modeButtons = new();
    private GameInstallation? _installation;

    internal MainForm()
    {
        Text = "RO — How to Fish Roulette Operator";
        ClientSize = new Size(920, 750); MinimumSize = new Size(936, 789);
        BackColor = Color.FromArgb(14, 10, 32); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f); StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); DoubleBuffered = true;
        BuildUi(); SelectGame(GameInstallation.GuessGame());
        _timer.Tick += (_, _) => RefreshStatus(); _timer.Start();
    }

    private void BuildUi()
    {
        var mark = new PictureBox { Location = new Point(25, 14), Size = new Size(104, 104), SizeMode = PictureBoxSizeMode.Zoom };
        using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("RouletteFishPixel.png"))
            if (stream is not null) using (var source = Image.FromStream(stream)) mark.Image = new Bitmap(source);
        Controls.Add(mark);
        Controls.Add(TextLabel("HOW TO FISH  •  UNIVERSAL ROULETTE TOOL", 136, 22, 9f, Color.FromArgb(63, 236, 211), true));
        Controls.Add(TextLabel("Roulette Operator", 131, 43, 28f, Color.FromArgb(255, 247, 215), true));
        Controls.Add(TextLabel("Known-good v1 physics. Patched locally for each compatible game build.", 136, 88, 10f, Color.FromArgb(193, 184, 220)));
        Controls.Add(TextLabel("RO v1.0  •  UNIVERSAL", 732, 47, 8f, Color.FromArgb(255, 211, 72), true));

        var status = PanelAt(30, 128, 860, 125);
        status.Controls.Add(Caption("INSTALLATION", 22, 16)); status.Controls.Add(Caption("GAME PROCESS", 450, 16));
        _installStatus.SetBounds(22, 42, 390, 30); _installStatus.Font = new Font("Segoe UI Semibold", 15f);
        _processStatus.SetBounds(450, 42, 370, 30); _processStatus.Font = new Font("Segoe UI Semibold", 15f);
        _compatibility.SetBounds(22, 83, 798, 24); _compatibility.ForeColor = Color.FromArgb(151, 220, 213);
        status.Controls.AddRange(new Control[] { _installStatus, _processStatus, _compatibility }); Controls.Add(status);

        var modes = PanelAt(30, 270, 860, 275); modes.BackColor = Color.FromArgb(27, 21, 61);
        modes.Controls.Add(Caption("SELECTED COLOR", 22, 16));
        _current.SetBounds(22, 42, 300, 42); _current.Font = new Font("Segoe UI Semibold", 23f); modes.Controls.Add(_current);
        _hint.SetBounds(350, 45, 470, 34); _hint.ForeColor = Color.FromArgb(197, 190, 224); _hint.TextAlign = ContentAlignment.MiddleRight; modes.Controls.Add(_hint);
        AddMode(modes, "NONE", "FREE", "Original roulette behavior", 22, Color.FromArgb(164, 144, 239));
        AddMode(modes, "BLACK", "BLACK", "Force black", 229, Color.FromArgb(232, 236, 245));
        AddMode(modes, "RED", "RED", "Force red", 436, Color.FromArgb(255, 66, 104));
        AddMode(modes, "GREEN", "GREEN", "Force green", 643, Color.FromArgb(40, 232, 139));
        Controls.Add(modes);

        Controls.Add(Caption("GAME EXECUTABLE", 30, 565));
        _path.SetBounds(30, 589, 660, 36); _path.BackColor = Color.FromArgb(29, 23, 63); _path.ForeColor = Color.FromArgb(232, 226, 247); _path.BorderStyle = BorderStyle.FixedSingle; _path.ReadOnly = true;
        var browse = ButtonAt("BROWSE…", 702, 589, 188, 36, Color.FromArgb(91, 65, 177)); browse.Click += (_, _) => Browse();
        Controls.AddRange(new Control[] { _path, browse });

        var install = ButtonAt("INSTALL / REPAIR", 30, 645, 260, 48, Color.FromArgb(15, 192, 166));
        var restore = ButtonAt("RESTORE ORIGINAL", 302, 645, 220, 48, Color.FromArgb(148, 52, 92));
        var open = ButtonAt("OPEN GAME", 534, 645, 172, 48, Color.FromArgb(92, 66, 178));
        var refresh = ButtonAt("REFRESH", 718, 645, 172, 48, Color.FromArgb(92, 66, 178));
        install.Click += (_, _) => Execute(() => _installation!.Install(Path.Combine(AppContext.BaseDirectory, "payload")), "Build-specific patch created and installed. FREE mode is active.");
        restore.Click += (_, _) => Execute(() => _installation!.Restore(), "The verified original game assembly was restored.");
        open.Click += (_, _) => Execute(() => Process.Start(new ProcessStartInfo(_installation!.ExePath) { UseShellExecute = true }), "Game started.");
        refresh.Click += (_, _) => RefreshStatus(); Controls.AddRange(new Control[] { install, restore, open, refresh });
        _message.SetBounds(32, 711, 855, 25); _message.ForeColor = Color.FromArgb(111, 232, 211); Controls.Add(_message);
    }

    private void AddMode(Control parent, string mode, string title, string subtitle, int x, Color accent)
    {
        var button = new ModeButton { Text = title, Subtitle = subtitle, Accent = accent, Location = new Point(x, 108), Size = new Size(195, 128), BackColor = Color.FromArgb(38, 29, 79), ForeColor = Color.FromArgb(255, 250, 235) };
        button.Click += (_, _) => Execute(() => _installation!.SetMode(mode), "Mode changed to " + (mode == "NONE" ? "FREE" : mode) + ".");
        _modeButtons[mode] = button; parent.Controls.Add(button);
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog { Filter = "Game executables (*.exe)|*.exe", Title = "Select the How to Fish game executable" };
        if (dialog.ShowDialog(this) == DialogResult.OK) SelectGame(dialog.FileName);
    }

    private void SelectGame(string? path)
    {
        _installation = path is null ? null : new GameInstallation(path); _path.Text = path ?? "Select the game executable…";
        if (path is not null) Settings.Save(path); RefreshStatus();
    }

    private void RefreshStatus()
    {
        try
        {
            if (_installation is null || !_installation.IsValid)
            {
                _installStatus.Text = "SELECT THE GAME"; _installStatus.ForeColor = Color.FromArgb(255, 202, 72);
                _processStatus.Text = "—"; _compatibility.Text = "The executable must be next to a Unity *_Data folder.";
                _current.Text = "FREE"; _hint.Text = "Install the trainer before choosing a color."; SetHighlights("NONE"); return;
            }
            var inspection = _installation.Inspection; var pid = _installation.RunningPid();
            _installStatus.Text = _installation.IsInstalled ? "READY TO PLAY" : inspection.IsCompatible ? "READY TO INSTALL" : "INCOMPATIBLE BUILD";
            _installStatus.ForeColor = _installation.IsInstalled ? Color.FromArgb(47, 232, 172) : inspection.IsCompatible ? Color.FromArgb(255, 205, 72) : Color.FromArgb(255, 82, 116);
            _processStatus.Text = pid is null ? "CLOSED" : "RUNNING  •  PID " + pid;
            _processStatus.ForeColor = pid is null ? Color.FromArgb(188, 180, 216) : Color.FromArgb(47, 232, 172);
            _compatibility.Text = inspection.IsCompatible ? "✓ " + inspection.Details : "✕ " + inspection.Details;
            var mode = _installation.ReadMode(); _current.Text = mode == "NONE" ? "FREE" : mode;
            _current.ForeColor = mode switch { "RED" => Color.FromArgb(255, 82, 116), "GREEN" => Color.FromArgb(47, 232, 154), "BLACK" => Color.FromArgb(232, 236, 245), _ => Color.FromArgb(187, 164, 255) };
            _hint.Text = mode == "NONE" ? "The game uses its original roulette behavior." : "The known-good v1 controller will target " + mode.ToLowerInvariant() + ".";
            SetHighlights(mode);
        }
        catch (Exception ex) { _message.ForeColor = Color.FromArgb(255, 92, 121); _message.Text = ex.Message; }
    }

    private void SetHighlights(string active) { foreach (var pair in _modeButtons) { pair.Value.Active = pair.Key == active; pair.Value.Invalidate(); } }
    private void Execute(Action action, string success)
    {
        try { if (_installation is null) throw new InvalidOperationException("Select the game first."); action(); _message.ForeColor = Color.FromArgb(111, 232, 211); _message.Text = success; }
        catch (Exception ex) { _message.ForeColor = Color.FromArgb(255, 92, 121); _message.Text = ex.Message; }
        RefreshStatus();
    }

    private static GlowPanel PanelAt(int x, int y, int width, int height) => new() { Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(31, 25, 68) };
    private static Label Caption(string text, int x, int y) => TextLabel(text, x, y, 8f, Color.FromArgb(90, 224, 207), true);
    private static Label TextLabel(string text, int x, int y, float size, Color color, bool semibold = false) => new() { Text = text, ForeColor = color, Font = new Font(semibold ? "Segoe UI Semibold" : "Segoe UI", size), Location = new Point(x, y), AutoSize = true };
    private static Button ButtonAt(string text, int x, int y, int width, int height, Color color)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, height), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false };
        button.FlatAppearance.BorderSize = 0; return button;
    }
}
