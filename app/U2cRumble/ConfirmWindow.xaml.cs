using System.Windows;

namespace U2cRumble;

/// <summary>A small dark confirmation dialog, so a write is never one accidental click away.</summary>
public partial class ConfirmWindow : Window
{
    private ConfirmWindow()
    {
        InitializeComponent();
    }

    /// <summary>Asks before a step that writes; returns true only when the user confirms.</summary>
    public static bool Ask(Window owner, string title, string message, string confirm)
    {
        var dialog = new ConfirmWindow { Owner = owner };
        dialog.TitleText.Text = title;
        dialog.MessageText.Text = message;
        dialog.ConfirmButton.Content = confirm;
        return dialog.ShowDialog() == true;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
