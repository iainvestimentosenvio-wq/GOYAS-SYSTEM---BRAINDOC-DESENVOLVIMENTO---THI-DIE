using Avalonia.Controls;
using Avalonia.Input;
using Protons.UI.Login.ViewModels;

namespace Protons.UI.Login.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
    }

    private void OnLoginKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is not LoginViewModel vm)
            return;

        if (vm.EntrarCommand.CanExecute(null))
        {
            vm.EntrarCommand.Execute(null);
            e.Handled = true;
        }
    }
}
