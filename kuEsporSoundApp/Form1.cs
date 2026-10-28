using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
namespace kuEsporSoundApp;

public partial class Form1 : Form
{
    // Windows' own global hotkey functions (user32.dll). No keyboard hooks are used.
    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr windowHandle, int hotkeyId, uint modifiers, uint virtualKeyCode);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr windowHandle, int hotkeyId);

    const int WindowMessageHotkey = 0x0312;
    const uint ModifierAlt = 0x0001;
    const uint ModifierControl = 0x0002;
    const uint ModifierShift = 0x0004;
    const uint ModifierNoRepeat = 0x4000; // do not re-trigger while the key is held down

    class AppRow
    {
        public string AppName = "";
        public GroupBox AppGroupBox = null!;
        public Label PercentLabel = null!;
        public TrackBar VolumeSlider = null!;
        public Button AssignKeyButton = null!, RemoveKeyButton = null!;
        public int HotkeyId;
        public Keys Key, Modifiers;
        public bool IsLowered;
        public Dictionary<int, float> OriginalVolumes = new(); // process id -> volume before lowering
    }

    readonly Dictionary<string, AppRow> appRows = new();
    readonly Dictionary<int, AppRow> hotkeyRows = new();
    Dictionary<string, int[]> settings = new(); // app name -> { target percent, key, modifier keys }
    readonly string settingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KuESportsSoundMixer", "settings.json");
    readonly Panel rowContainer = new() { Dock = DockStyle.Fill, AutoScroll = true };
    AppRow? rowWaitingForKey;
    int nextHotkeyId = 1;
    bool isRealExit;
    NotifyIcon notifyIcon = null!;

    public Form1()
    {
        InitializeComponent();
        Text = "kuE-sports sound mixer";
        ClientSize = new Size(440, 480);
        MaximizeBox = false;
        KeyPreview = true;
        Controls.Add(rowContainer);

        // Window and tray icon come from the .ico file set in the project properties.
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)!;

        // Notification area (system tray)
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => ShowMainWindow());
        trayMenu.Items.Add("Exit", null, (_, _) => { isRealExit = true; Close(); });
        notifyIcon = new NotifyIcon { Icon = Icon, Text = "kuE-sports sound mixer", ContextMenuStrip = trayMenu, Visible = true };
        notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        try { settings = JsonSerializer.Deserialize<Dictionary<string, int[]>>(File.ReadAllText(settingsFilePath)) ?? new(); } catch { }

        var refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        refreshTimer.Tick += (_, _) => RefreshApps();
        refreshTimer.Start();
        Load += (_, _) => RefreshApps();

        FormClosing += (_, closingArgs) =>
        {
            if (!isRealExit && closingArgs.CloseReason == CloseReason.UserClosing)
            {
                closingArgs.Cancel = true; // the X button only hides the window to the tray
                Hide();
                return;
            }
            notifyIcon.Visible = false;
            foreach (var row in appRows.Values.Where(row => row.IsLowered)) ToggleVolume(row); // restore original volumes
        };
    }

    void ShowMainWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); }

    // ---------- Audio sessions ----------
    static string? GetProcessName(int processId)
    {
        if (processId == 0) return "Windows system sounds"; // Windows system sounds
        try { return Process.GetProcessById(processId).ProcessName; } catch { return null; }
    }

    // Returns every audio session (one per app that plays or has played sound) on all active output devices.
    static List<(string appName, AudioSessionControl session)> GetAudioSessions()
    {
        var sessionList = new List<(string, AudioSessionControl)>();
        try
        {
            using var deviceEnumerator = new MMDeviceEnumerator();
            foreach (var device in deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                try
                {
                    var sessionManager = device.AudioSessionManager;
                    sessionManager.RefreshSessions();
                    for (int index = 0; index < sessionManager.Sessions.Count; index++)
                    {
                        var session = sessionManager.Sessions[index];
                        if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                        var appName = GetProcessName((int)session.GetProcessID);
                        if (appName != null) sessionList.Add((appName, session));
                    }
                }
                catch { }
            }
        }
        catch { }
        return sessionList;
    }

    void RefreshApps()
    {
        var foundAppNames = GetAudioSessions().Select(sessionInfo => sessionInfo.appName).Distinct().ToHashSet();

        foreach (var appName in foundAppNames.OrderByDescending(name => name).Where(name => !appRows.ContainsKey(name)))
            AddRow(appName);

        foreach (var appName in appRows.Keys.Where(name => !foundAppNames.Contains(name)).ToList())
        {
            var row = appRows[appName];
            if (row.HotkeyId != 0) { UnregisterHotKey(Handle, row.HotkeyId); hotkeyRows.Remove(row.HotkeyId); }
            row.AppGroupBox.Dispose();
            appRows.Remove(appName);
        }
    }

    // ---------- User interface ----------
    void AddRow(string appName)
    {
        var savedSetting = settings.GetValueOrDefault(appName, new[] { 10, 0, 0 });
        var row = new AppRow { AppName = appName };

        row.VolumeSlider = new TrackBar { Dock = DockStyle.Fill, Maximum = 100, TickFrequency = 10, Value = Math.Clamp(savedSetting[0], 0, 100) };
        row.PercentLabel = new Label { Dock = DockStyle.Right, Width = 40, Text = row.VolumeSlider.Value + "%", TextAlign = ContentAlignment.MiddleCenter };
        row.AssignKeyButton = new Button { Dock = DockStyle.Right, Width = 90, Text = "Bind Key", TabStop = false };
        row.RemoveKeyButton = new Button { Dock = DockStyle.Right, Width = 90, Text = "Remove Key", TabStop = false };
        row.AppGroupBox = new GroupBox { Dock = DockStyle.Top, Height = 70, Text = appName };
        row.AppGroupBox.Controls.AddRange(new Control[] { row.VolumeSlider, row.PercentLabel, row.AssignKeyButton, row.RemoveKeyButton });

        row.RemoveKeyButton.Click += (_, _) =>
        {
            if (rowWaitingForKey == row) rowWaitingForKey = null;
            AssignKey(row, Keys.None, Keys.None);
            SaveSettings(row);
        };

        row.VolumeSlider.Scroll += (_, _) =>
        {
            row.PercentLabel.Text = row.VolumeSlider.Value + "%";
            SaveSettings(row);
            if (row.IsLowered)
                foreach (var (_, session) in GetAudioSessions().Where(sessionInfo => sessionInfo.appName == row.AppName))
                    session.SimpleAudioVolume.Volume = row.VolumeSlider.Value / 100f;
        };

        row.AssignKeyButton.Click += (_, _) => { rowWaitingForKey = row; row.AssignKeyButton.Text = "Press the key..."; };

        rowContainer.Controls.Add(row.AppGroupBox);
        appRows[appName] = row;

        if (savedSetting[1] != 0) AssignKey(row, (Keys)savedSetting[1], (Keys)savedSetting[2]);
        else row.AssignKeyButton.Text = GetKeyText(row);
    }

    // ---------- Hotkey assignment ----------
    protected override void OnKeyDown(KeyEventArgs keyArgs)
    {
        base.OnKeyDown(keyArgs);
        if (rowWaitingForKey == null) return;

        keyArgs.Handled = keyArgs.SuppressKeyPress = true;
        var pressedKey = keyArgs.KeyCode;
        if (pressedKey is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return; // modifier keys alone

        var row = rowWaitingForKey;
        rowWaitingForKey = null;

        if (pressedKey == Keys.Escape) { row.AssignKeyButton.Text = GetKeyText(row); return; } // cancel
        if (pressedKey is Keys.Delete or Keys.Back) pressedKey = Keys.None;                    // clear the hotkey

        AssignKey(row, pressedKey, keyArgs.Modifiers & (Keys.Control | Keys.Alt | Keys.Shift));
        SaveSettings(row);
    }

    void AssignKey(AppRow row, Keys key, Keys modifiers)
    {
        if (row.HotkeyId != 0)
        {
            UnregisterHotKey(Handle, row.HotkeyId);
            hotkeyRows.Remove(row.HotkeyId);
            row.HotkeyId = 0;
        }
        row.Key = row.Modifiers = Keys.None;

        if (key != Keys.None)
        {
            uint windowsModifiers = ModifierNoRepeat
                | (modifiers.HasFlag(Keys.Control) ? ModifierControl : 0)
                | (modifiers.HasFlag(Keys.Alt) ? ModifierAlt : 0)
                | (modifiers.HasFlag(Keys.Shift) ? ModifierShift : 0);

            int newHotkeyId = nextHotkeyId++;
            if (!RegisterHotKey(Handle, newHotkeyId, windowsModifiers, (uint)key))
            {
                row.AssignKeyButton.Text = "Key in use"; // another program already uses this hotkey
                return;
            }
            (row.HotkeyId, row.Key, row.Modifiers) = (newHotkeyId, key, modifiers);
            hotkeyRows[newHotkeyId] = row;
        }
        row.AssignKeyButton.Text = GetKeyText(row);
    }

    static string GetKeyText(AppRow row) => row.Key == Keys.None ? "Bind Key"
        : (row.Modifiers.HasFlag(Keys.Control) ? "Ctrl+" : "")
          + (row.Modifiers.HasFlag(Keys.Alt) ? "Alt+" : "")
          + (row.Modifiers.HasFlag(Keys.Shift) ? "Shift+" : "")
          + (row.Key >= Keys.D0 && row.Key <= Keys.D9 ? ((char)row.Key).ToString() : row.Key.ToString());

    // ---------- Lower / restore volume ----------
    protected override void WndProc(ref Message windowMessage)
    {
        if (windowMessage.Msg == WindowMessageHotkey && hotkeyRows.TryGetValue(windowMessage.WParam.ToInt32(), out var row))
            ToggleVolume(row);
        base.WndProc(ref windowMessage);
    }

    void ToggleVolume(AppRow row)
    {
        float targetVolume = row.VolumeSlider.Value / 100f;

        foreach (var (_, session) in GetAudioSessions().Where(sessionInfo => sessionInfo.appName == row.AppName))
        {
            var volumeControl = session.SimpleAudioVolume;
            int processId = (int)session.GetProcessID;

            if (!row.IsLowered)
            {
                row.OriginalVolumes[processId] = volumeControl.Volume;
                volumeControl.Volume = targetVolume;
            }
            else if (row.OriginalVolumes.TryGetValue(processId, out var originalVolume))
            {
                volumeControl.Volume = originalVolume;
            }
        }

        row.IsLowered = !row.IsLowered && row.OriginalVolumes.Count > 0;
        if (!row.IsLowered) row.OriginalVolumes.Clear();
        row.AppGroupBox.Text = row.IsLowered ? row.AppName + " (on working)" : row.AppName;
    }

    // ---------- Settings ----------
    void SaveSettings(AppRow row)
    {
        settings[row.AppName] = new[] { row.VolumeSlider.Value, (int)row.Key, (int)row.Modifiers };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
            File.WriteAllText(settingsFilePath, JsonSerializer.Serialize(settings));
        }
        catch { }
    }
}