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
    private readonly Dictionary<ControllerInput, ComboBox> _mappingCombos = new();
    private readonly Dictionary<ControllerInput, TextBox> _mappingParams = new();

    public SettingsWindow(ControllerProfile profile)
    {
        InitializeComponent();
        _profile = profile;

        ApiKeyBox.Text = TryReadApiKey();
        AutostartCheckBox.IsChecked = AutostartService.IsEnabled();

        var rows = new StackPanel();
        foreach (var input in Enum.GetValues<ControllerInput>())
        {
            var currentAction = _profile.Resolve(input);

            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

            var label = new TextBlock { Text = input.ToString(), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var combo = new ComboBox { ItemsSource = Enum.GetValues<ActionKind>(), Width = 110 };
            combo.SelectedItem = KindOf(currentAction);
            Grid.SetColumn(combo, 1);

            var param = new TextBox { Width = 100, Margin = new Thickness(6, 0, 0, 0), Text = ParamOf(currentAction) };
            Grid.SetColumn(param, 2);

            _mappingCombos[input] = combo;
            _mappingParams[input] = param;

            row.Children.Add(label);
            row.Children.Add(combo);
            row.Children.Add(param);
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.EnsureExists();
        File.WriteAllText(AppPaths.GroqApiKeyPath, ApiKeyBox.Text.Trim());

        AutostartService.SetEnabled(AutostartCheckBox.IsChecked == true);

        foreach (var (input, combo) in _mappingCombos)
        {
            var kind = (ActionKind)combo.SelectedItem;
            var param = _mappingParams[input].Text.Trim();
            ButtonAction action = kind switch
            {
                ActionKind.Key => new KeyAction(param),
                ActionKind.Mouse => new MouseAction(MouseButton.Left, MouseActionKind.Click),
                ActionKind.Voice => new VoiceAction(VoiceMode.Toggle),
                ActionKind.Keyboard => new KeyboardAction(KeyboardMode.Simple),
                ActionKind.Launch => new LaunchAction(param),
                _ => ButtonAction.None(),
            };
            _profile.Mappings[input] = action;
        }
        ProfileStore.Save(_profile);

        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
