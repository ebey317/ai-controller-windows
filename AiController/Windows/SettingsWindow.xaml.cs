using System.IO;
using System.Windows;
using System.Windows.Controls;
using AiController.Models;
using AiController.Services;
using ComboBox = System.Windows.Controls.ComboBox;

namespace AiController.Windows;

/// <summary>Settings UI -- mirrors Android's SettingsActivity/ButtonMappingAdapter: API key entry, autostart toggle, per-button action mapping.</summary>
public partial class SettingsWindow : Window
{
    private readonly ControllerProfile _profile;
    private readonly Dictionary<ControllerInput, ComboBox> _mappingCombos = new();

    public SettingsWindow(ControllerProfile profile)
    {
        InitializeComponent();
        _profile = profile;

        ApiKeyBox.Text = TryReadApiKey();
        AutostartCheckBox.IsChecked = AutostartService.IsEnabled();

        var rows = new StackPanel();
        foreach (var input in Enum.GetValues<ControllerInput>())
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock { Text = input.ToString(), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(label, 0);

            var combo = new ComboBox { ItemsSource = Enum.GetValues<ActionType>(), Width = 160 };
            combo.SelectedItem = _profile.Resolve(input).Type;
            Grid.SetColumn(combo, 1);
            _mappingCombos[input] = combo;

            row.Children.Add(label);
            row.Children.Add(combo);
            rows.Children.Add(row);
        }
        MappingsList.Content = rows;
    }

    private static string TryReadApiKey()
    {
        var configFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiController", "groq_api_key.txt");
        return File.Exists(configFile) ? File.ReadAllText(configFile).Trim() : "";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiController");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "groq_api_key.txt"), ApiKeyBox.Text.Trim());

        AutostartService.SetEnabled(AutostartCheckBox.IsChecked == true);

        foreach (var (input, combo) in _mappingCombos)
        {
            var actionType = (ActionType)combo.SelectedItem;
            _profile.Mappings[input] = new ButtonAction(actionType);
        }
        ProfileStore.Save(_profile);

        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
