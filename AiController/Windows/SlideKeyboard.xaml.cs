using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AiController.Services;

namespace AiController.Windows;

/// <summary>
/// Mode-chips + pinned-snippets on-screen keyboard -- mirrors slide_keyboard.py's
/// mode bar (PRO/BUBBLY/CASUAL/BOLD/BIG toggle, persisted to the same ptt_mode
/// convention this app's TextStyles/AppPaths.PttModePath use) and its pinned
/// snippet slots (persistent quick-insert text, separate from the OS clipboard).
///
/// Distinct from the plain grid keyboard (OnScreenKeyboardWindow, which exists
/// for quick single-key input): this is the richer variant for dictation-style
/// workflows, bound via KeyboardAction(KeyboardMode.Slide).
///
/// Pin editing reuses this same on-screen key grid rather than assuming a
/// physical keyboard is available to type a pin's text: toggling "Edit Pins"
/// and picking a slot redirects key presses into a local edit buffer (shown in
/// EditBox) instead of injecting them into the external focus target, exactly
/// the same never-assume-a-physical-keyboard posture as the rest of this app.
/// </summary>
public partial class SlideKeyboard : Window
{
    private const int PinCount = 5;

    private IntPtr _focusTargetWindow;
    private bool _shiftOn;
    private TextStyleMode _mode = TextStyleMode.Pro;
    private List<string> _pins = new();
    private bool _editArmed;
    private int? _editingPinIndex;
    private readonly StringBuilder _editBuffer = new();

    public SlideKeyboard()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => NativeWindowHelper.SetNoActivate(this);
        LoadMode();
        LoadPins();
    }

    public void CaptureFocusTarget() => _focusTargetWindow = InputInjector.ActiveWindow();

    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }
        CaptureFocusTarget();
        BuildModeBar();
        BuildPinBar();
        BuildKeys();
        Show();
    }

    // ---- mode chips (PRO/BUBBLY/CASUAL/BOLD/BIG) ----

    private void LoadMode()
    {
        try
        {
            if (File.Exists(AppPaths.PttModePath))
            {
                var raw = File.ReadAllText(AppPaths.PttModePath).Trim();
                if (Enum.TryParse<TextStyleMode>(raw, ignoreCase: true, out var mode)) _mode = mode;
            }
        }
        catch (IOException) { /* fall back to the default mode */ }
    }

    private void SaveMode()
    {
        try
        {
            AppPaths.EnsureExists();
            File.WriteAllText(AppPaths.PttModePath, _mode.ToString().ToLowerInvariant());
        }
        catch (IOException) { /* best-effort -- mode just won't persist across restarts */ }
    }

    private void BuildModeBar()
    {
        ModeBar.Children.Clear();
        foreach (var mode in Enum.GetValues<TextStyleMode>())
        {
            var btn = new System.Windows.Controls.Button
            {
                Content = mode.ToString().ToUpperInvariant(),
                Style = (Style)FindResource("KeyButtonStyle"),
            };
            if (mode == _mode) btn.BorderThickness = new Thickness(3);
            btn.Click += (_, _) =>
            {
                _mode = mode;
                SaveMode();
                BuildModeBar();
            };
            ModeBar.Children.Add(btn);
        }
    }

    // ---- pinned snippets ----

    private void LoadPins()
    {
        _pins = new List<string>(new string[PinCount]);
        try
        {
            if (File.Exists(AppPaths.PinnedSnippetsPath))
            {
                var loaded = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.PinnedSnippetsPath));
                if (loaded != null)
                    for (var i = 0; i < Math.Min(PinCount, loaded.Count); i++) _pins[i] = loaded[i];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Corrupt or unreadable pins file -- fall back to empty slots.
        }
        for (var i = 0; i < PinCount; i++) _pins[i] ??= "";
    }

    private void SavePins()
    {
        try
        {
            AppPaths.EnsureExists();
            File.WriteAllText(AppPaths.PinnedSnippetsPath, JsonSerializer.Serialize(_pins));
        }
        catch (IOException) { /* best-effort -- pins just won't persist across restarts */ }
    }

    private void BuildPinBar()
    {
        PinBar.Children.Clear();
        for (var i = 0; i < PinCount; i++)
        {
            var index = i;
            var label = string.IsNullOrEmpty(_pins[index]) ? $"Pin {index + 1}" : _pins[index];
            var btn = new System.Windows.Controls.Button
            {
                Content = Truncate(label, 12),
                Style = (Style)FindResource("KeyButtonStyle"),
                MinWidth = 90,
            };
            if (_editingPinIndex == index) btn.BorderThickness = new Thickness(3);
            btn.Click += (_, _) => OnPinClicked(index);
            PinBar.Children.Add(btn);
        }

        var editToggle = new System.Windows.Controls.Button
        {
            Content = _editingPinIndex.HasValue ? "Save Pin" : (_editArmed ? "Cancel Edit" : "Edit Pins"),
            Style = (Style)FindResource("KeyButtonStyle"),
        };
        editToggle.Click += (_, _) =>
        {
            if (_editingPinIndex.HasValue)
            {
                CommitPinEdit();
                _editArmed = false;
            }
            else
            {
                _editArmed = !_editArmed;
            }
            BuildPinBar();
        };
        PinBar.Children.Add(editToggle);
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] + "…" : s;

    private void OnPinClicked(int index)
    {
        if (_editArmed)
        {
            _editingPinIndex = index;
            _editBuffer.Clear();
            _editBuffer.Append(_pins[index]);
            EditBox.Text = _editBuffer.ToString();
            EditBox.Visibility = Visibility.Visible;
            BuildPinBar();
            return;
        }

        if (string.IsNullOrEmpty(_pins[index])) return;
        InjectText(_pins[index]);
    }

    private void CommitPinEdit()
    {
        if (_editingPinIndex is int index)
        {
            _pins[index] = _editBuffer.ToString();
            SavePins();
        }
        _editingPinIndex = null;
        EditBox.Visibility = Visibility.Collapsed;
    }

    // ---- key grid (shared layout with OnScreenKeyboardWindow) ----

    private void BuildKeys()
    {
        KeyRows.Children.Clear();
        foreach (var row in (_shiftOn ? KeyboardLayout.RowsUpper : KeyboardLayout.RowsLower))
        {
            var rowPanel = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            };
            foreach (var key in row)
            {
                var btn = new System.Windows.Controls.Button { Content = key, Style = (Style)FindResource("KeyButtonStyle") };
                if (key == "Space") btn.MinWidth = 220;
                btn.Click += (_, _) => OnKeyPressed(key);
                rowPanel.Children.Add(btn);
            }
            KeyRows.Children.Add(rowPanel);
        }
    }

    private void OnKeyPressed(string key)
    {
        if (key == "Shift")
        {
            _shiftOn = !_shiftOn;
            BuildKeys();
            return;
        }

        if (_editingPinIndex.HasValue) EditPinKeyPressed(key);
        else InjectKeyPressed(key);

        if (_shiftOn)
        {
            _shiftOn = false;
            BuildKeys();
        }
    }

    /// <summary>Route a key press into the local pin-edit buffer instead of the
    /// external focus target -- see the class doc comment for why.</summary>
    private void EditPinKeyPressed(string key)
    {
        if (key == "Bksp")
        {
            if (_editBuffer.Length > 0) _editBuffer.Length--;
        }
        else if (key == "Space")
        {
            _editBuffer.Append(' ');
        }
        else if (!KeyboardLayout.SpecialKeys.ContainsKey(key))
        {
            // Enter/Tab/arrows/Esc have no meaning in a single-line pin buffer.
            _editBuffer.Append(key);
        }
        EditBox.Text = _editBuffer.ToString();
    }

    private void InjectKeyPressed(string key)
    {
        try
        {
            if (KeyboardLayout.SpecialKeys.TryGetValue(key, out var vk))
                InputInjector.GuardedKey(_focusTargetWindow, vk);
            else
                InjectText(key);
        }
        catch (InputInjector.FocusLostException)
        {
            // Skip and move on, never fire blind into whatever ended up foreground instead.
        }
    }

    /// <summary>Apply the active mode's style transform before injecting --
    /// mirrors ptt_pynput.py applying PRO/BUBBLY/CASUAL/BOLD/BIG right before typing.</summary>
    private void InjectText(string text) =>
        InputInjector.GuardedType(_focusTargetWindow, TextStyles.Apply(text, _mode));
}
