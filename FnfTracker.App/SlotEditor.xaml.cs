using System.Windows;
using System.Windows.Controls;
using FnfTracker.Core;

namespace FnfTracker.App;

/// <summary>Class + spec pickers (and an optional Name-Realm box) for one player.</summary>
public partial class SlotEditor : UserControl
{
    public SlotEditor()
    {
        InitializeComponent();
        var names = new List<string> { "" };
        names.AddRange(GameData.Classes.Select(c => c.Name));
        ClassBox.ItemsSource = names;
        ClassBox.SelectedIndex = 0;
    }

    public string Header
    {
        get => HeaderText.Text;
        set => HeaderText.Text = value;
    }

    public bool ShowName
    {
        get => NameGrid.Visibility == Visibility.Visible;
        set => NameGrid.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    public PlayerSlot Slot
    {
        get => new()
        {
            Class = ClassBox.SelectedItem as string ?? "",
            Spec = SpecBox.SelectedItem as string ?? "",
            Name = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim(),
        };
        set
        {
            var cls = GameData.Classes.FirstOrDefault(c =>
                string.Equals(c.Name, value?.Class, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
            ClassBox.SelectedItem = cls;
            ApplyClass(cls);
            SpecBox.SelectedItem = GameData.SpecsFor(cls).FirstOrDefault(s =>
                string.Equals(s, value?.Spec, StringComparison.OrdinalIgnoreCase));
            NameBox.Text = value?.Name ?? "";
        }
    }

    public void Clear() => Slot = new PlayerSlot();

    public void FocusClass() => ClassBox.Focus();

    private void ClassBox_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ApplyClass(ClassBox.SelectedItem as string);

    private void ApplyClass(string? cls)
    {
        var previous = SpecBox.SelectedItem as string;
        var specs = GameData.SpecsFor(cls);
        SpecBox.ItemsSource = specs;
        SpecBox.SelectedItem = specs.Contains(previous) ? previous : null;
        Swatch.Background = Theme.ForClass(cls);
    }

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        NameHint.Visibility = string.IsNullOrEmpty(NameBox.Text) ? Visibility.Visible : Visibility.Collapsed;
}
