using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiController.Models;
using AiController.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace AiController.Windows;

/// <summary>
/// Settings UI -- mirrors Android's SettingsActivity/ButtonMappingAdapter: API key
/// entry, autostart toggle, per-button action mapping.
///
/// W1 fix: the mapping row used to be a single ComboBox over the old flat
/// ActionType enum, since that enum was the only data a binding carried. Now
/// that ButtonAction is a polymorphic hierarchy (KeyAction/MouseAction/
/// VoiceAction/KeyboardAction/LaunchAction/NullAction), each row also needs a
/// place to enter the kind-specific parameter (a key name for Key, an app name
/// for Launch) -- ActionKind below is a UI-only enum for the combo box; Save_Click
/// constructs the real ButtonAction subtype from it plus the parameter text.
/// </summary>
public partial class SettingsWindow : Window
{
    private enum ActionKind { None, Key, Mouse, Voice, Keyboard, Launch }

    private readonly ControllerProfile _profile;
    private readonly Dictionary<ControllerInput, ButtonAction> _originalActions = new();
    private readonly Dictionary<ControllerInput, ComboBox> _mappingCombos = new();
    private readonly Dictionary<ControllerInput, TextBox> _mappingParams = new();
    private readonly Dictionary<ControllerInput, ComboBox> _mouseButtonCombos = new();
    private readonly Dictionary<ControllerInput, ComboBox> _mouseKindCombos = new();
    private readonly Dictionary<ControllerInput, ComboBox> _voiceModeCombos = new();
    private readonly Dictionary<ControllerInput, ComboBox> _keyboardModeCombos = new();

    public SettingsWindow(ControllerProfile profile)
    {
        InitializeComponent();
        _profile = profile;

        ApiKeyBox.Text = TryReadApiKey();
        AutostartCheckBox.IsChecked = AutostartService.IsEnabled();

        // Emoji skin tone picker -- every customer picks their own. The combo
        // shows a ✌ emoji rendered in each tone as its own label, so the
        // choice previews exactly what dictation will type.
        SkinToneBox.ItemsSource = new[]
        {
            new SkinToneOption(EmojiSkinTone.Neutral,   "✌️  Neutral"),
            new SkinToneOption(EmojiSkinTone.Light,     "✌🏻  Light"),
            new SkinToneOption(EmojiSkinTone.MediumLight, "✌🏼  Medium-light"),
            new SkinToneOption(EmojiSkinTone.Medium,    "✌🏽  Medium"),
            new SkinToneOption(EmojiSkinTone.MediumDark, "✌🏾  Medium-dark"),
            new SkinToneOption(EmojiSkinTone.Dark,      "✌🏿  Dark (default)"),
        };
        SkinToneBox.DisplayMemberPath = nameof(SkinToneOption.Label);
        SkinToneBox.SelectedValuePath = nameof(SkinToneOption.Tone);
        SkinToneBox.SelectedValue = SkinToneStore.Load();

        var rows = new StackPanel();
        foreach (var input in Enum.GetValues<ControllerInput>())
        {
            var currentAction = _profile.Resolve(input);
            _originalActions[input] = currentAction;

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = input.ToString(), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var combo = new ComboBox { ItemsSource = Enum.GetValues<ActionKind>(), Width = 110 };
            combo.SelectedItem = KindOf(currentAction);
            Grid.SetColumn(combo, 1);

            var param = new TextBox { Width = 100, Margin = new Thickness(6, 0, 0, 0), Text = ParamOf(currentAction) };
            Grid.SetColumn(param, 2);

            // F11 fix: one combo per kind-specific value a row's action can carry
            // that ISN'T the free-text param above -- mouse button, mouse action
            // kind, voice mode, keyboard mode. Always present (not just when that
            // kind is selected) so a kind switch has something to read values from.
            var mouseAction = currentAction as MouseAction;
            var voiceAction = currentAction as VoiceAction;
            var keyboardAction = currentAction as KeyboardAction;

            var mouseButtonCombo = new ComboBox { ItemsSource = Enum.GetValues<MouseButton>(), Width = 70, Margin = new Thickness(6, 0, 0, 0) };
            mouseButtonCombo.SelectedItem = mouseAction?.Button ?? MouseButton.Left;
            Grid.SetColumn(mouseButtonCombo, 3);

            var mouseKindCombo = new ComboBox { ItemsSource = Enum.GetValues<MouseActionKind>(), Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            mouseKindCombo.SelectedItem = mouseAction?.Kind ?? MouseActionKind.Click;
            Grid.SetColumn(mouseKindCombo, 4);

            var voiceModeCombo = new ComboBox { ItemsSource = Enum.GetValues<VoiceMode>(), Width = 100, Margin = new Thickness(6, 0, 0, 0) };
            voiceModeCombo.SelectedItem = voiceAction?.Mode ?? VoiceMode.Toggle;
            Grid.SetColumn(voiceModeCombo, 5);

            var keyboardModeCombo = new ComboBox { ItemsSource = Enum.GetValues<KeyboardMode>(), Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            keyboardModeCombo.SelectedItem = keyboardAction?.Mode ?? KeyboardMode.Simple;
            Grid.SetColumn(keyboardModeCombo, 6);

            _mappingCombos[input] = combo;
            _mappingParams[input] = param;
            _mouseButtonCombos[input] = mouseButtonCombo;
            _mouseKindCombos[input] = mouseKindCombo;
            _voiceModeCombos[input] = voiceModeCombo;
            _keyboardModeCombos[input] = keyboardModeCombo;

            row.Children.Add(label);
            row.Children.Add(combo);
            row.Children.Add(param);
            row.Children.Add(mouseButtonCombo);
            row.Children.Add(mouseKindCombo);
            row.Children.Add(voiceModeCombo);
            row.Children.Add(keyboardModeCombo);
            rows.Children.Add(row);
        }
        MappingsList.Content = rows;
    }

    private static ActionKind KindOf(ButtonAction action) => action switch
    {
        KeyAction => ActionKind.Key,
        MouseAction => ActionKind.Mouse,
        VoiceAction => ActionKind.Voice,
        KeyboardAction => ActionKind.Keyboard,
        LaunchAction => ActionKind.Launch,
        _ => ActionKind.None,
    };

    /// <summary>The one free-text parameter a row's action can carry -- a key name
    /// for KeyAction, an app name for LaunchAction. Ignored (and left blank) for
    /// action kinds that don't take one.</summary>
    private static string ParamOf(ButtonAction action) => action switch
    {
        KeyAction k => k.KeyText,
        LaunchAction l => l.AppTarget,
        _ => "",
    };

    private static string TryReadApiKey() =>
        File.Exists(AppPaths.GroqApiKeyPath) ? File.ReadAllText(AppPaths.GroqApiKeyPath).Trim() : "";

    /// <summary>Combo-box item for the skin-tone picker: the tone enum plus a
    /// label that previews the tone on a ✌ emoji.</summary>
    public record SkinToneOption(EmojiSkinTone Tone, string Label);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureExists();
        File.WriteAllText(AppPaths.GroqApiKeyPath, ApiKeyBox.Text.Trim());

        AutostartService.SetEnabled(AutostartCheckBox.IsChecked == true);

        var tone = SkinToneBox.SelectedValue is EmojiSkinTone t ? t : EmojiSkinTone.Dark;
        SkinToneStore.Save(tone);
        TextStyles.SetSkinTone(tone);

        foreach (var (input, combo) in _mappingCombos)
        {
            var kind = (ActionKind)combo.SelectedItem;
            var param = _mappingParams[input].Text.Trim();
            var mouseButton = _mouseButtonCombos[input].SelectedItem is MouseButton mb ? mb : MouseButton.Left;
            var mouseKind = _mouseKindCombos[input].SelectedItem is MouseActionKind mk ? mk : MouseActionKind.Click;
            var voiceMode = _voiceModeCombos[input].SelectedItem is VoiceMode vm ? vm : VoiceMode.Toggle;
            var keyboardMode = _keyboardModeCombos[input].SelectedItem is KeyboardMode km ? km : KeyboardMode.Simple;

            var original = _originalActions[input];
            ButtonAction action;
            if (kind == KindOf(original))
            {
                // F11 fix: kind unchanged -- update the EXISTING action in place so
                // values this row has no control for (e.g. KeyAction.Modifier)
                // survive the save, instead of getting silently dropped by
                // rebuilding a fresh default-valued instance every time.
                action = original;
                switch (action)
                {
                    case KeyAction k: k.KeyText = param; break;
                    case MouseAction m: m.Button = mouseButton; m.Kind = mouseKind; break;
                    case VoiceAction v: v.Mode = voiceMode; break;
                    case KeyboardAction kb: kb.Mode = keyboardMode; break;
                    case LaunchAction l: l.AppTarget = param; break;
                }
            }
            else
            {
                action = kind switch
                {
                    ActionKind.Key => new KeyAction(param),
                    ActionKind.Mouse => new MouseAction(mouseButton, mouseKind),
                    ActionKind.Voice => new VoiceAction(voiceMode),
                    ActionKind.Keyboard => new KeyboardAction(keyboardMode),
                    ActionKind.Launch => new LaunchAction(param),
                    _ => ButtonAction.None(),
                };
            }
            _profile.Mappings[input] = action;
        }
        // F10 fix: pass the profile's own name (desktop/browser/iptv) instead of
        // relying on ProfileStore.Save's unrelated "profile" default parameter,
        // which silently wrote to the wrong file.
        ProfileStore.Save(_profile, _profile.Name);

        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
