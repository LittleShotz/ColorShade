using System.Windows;
using System.Windows.Controls;

namespace ColorShade;

internal sealed class PresetNameDialog : Window
{
    private readonly TextBox _name;
    internal string PresetName => _name.Text.Trim();
    internal PresetNameDialog(string initial)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Title = "Name your preset"; Width = 420; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock { Text = "Preset name", Margin = new Thickness(0, 0, 0, 10), FontSize = 18 });
        _name = new TextBox { Text = initial.Length > 60 ? initial[..60] : initial, MaxLength = 60 };
        layout.Children.Add(_name);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save", IsDefault = true };
        save.Click += (_, _) => { if (PresetName.Length > 0) DialogResult = true; else _name.Focus(); };
        buttons.Children.Add(cancel); buttons.Children.Add(save); layout.Children.Add(buttons); Content = layout;
        Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
