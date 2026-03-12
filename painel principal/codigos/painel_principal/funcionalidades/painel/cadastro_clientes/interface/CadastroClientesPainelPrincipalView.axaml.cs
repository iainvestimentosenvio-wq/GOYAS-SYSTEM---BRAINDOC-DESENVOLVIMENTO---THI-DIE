using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Views.PainelPrincipal.Funcionalidades.CadastroClientes;

public partial class CadastroClientesPainelPrincipalView : UserControl
{
    public CadastroClientesPainelPrincipalView()
    {
        InitializeComponent();
    }

    private void OnCadastroCampoKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not Control controle || DataContext is not PainelViewModel vm)
            return;

        if (controle is ComboBox combo && combo.IsDropDownOpen)
            return;

        var campoAtual = ObterCampoPorControle(controle);
        if (string.IsNullOrWhiteSpace(campoAtual))
            return;

        if (!vm.ValidarCampoCadastroParaNavegacao(campoAtual))
        {
            e.Handled = true;
            return;
        }

        if (string.Equals(campoAtual, "telefone", StringComparison.Ordinal))
        {
            var abriuConfirmacao = vm.SolicitarSalvarClienteViaEnter();
            if (abriuConfirmacao)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (ConfirmarSalvarCadastroButton.IsVisible)
                        ConfirmarSalvarCadastroButton.Focus(NavigationMethod.Tab, KeyModifiers.None);
                }, DispatcherPriority.Input);
            }

            e.Handled = true;
            return;
        }

        FocarProximoCampo(campoAtual);
        e.Handled = true;
    }

    private void OnSalvarClienteButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not PainelViewModel vm)
            return;

        if (vm.SolicitarSalvarClienteViaEnter())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ConfirmarSalvarCadastroButton.IsVisible)
                    ConfirmarSalvarCadastroButton.Focus(NavigationMethod.Tab, KeyModifiers.None);
            }, DispatcherPriority.Input);
        }

        e.Handled = true;
    }

    private void OnCadastroRootKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || DataContext is not PainelViewModel vm)
            return;

        if (vm.ExibirConfirmacaoSalvarCadastro)
        {
            vm.CancelarSalvarClienteCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (vm.ExibirConfirmacaoInativarCliente)
        {
            vm.CancelarInativarClienteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private string? ObterCampoPorControle(Control controle)
    {
        if (ReferenceEquals(controle, CodigoClienteCadastroTextBox))
            return "codigo";
        if (ReferenceEquals(controle, NomeClienteCadastroTextBox))
            return "nome";
        if (ReferenceEquals(controle, NomeFantasiaClienteCadastroTextBox))
            return "fantasia";
        if (ReferenceEquals(controle, DocumentoClienteCadastroTextBox))
            return "documento";
        if (ReferenceEquals(controle, GrupoEmpresarialCadastroComboBox))
            return "grupo";
        if (ReferenceEquals(controle, EmailClienteCadastroTextBox))
            return "email";
        if (ReferenceEquals(controle, TelefoneClienteCadastroTextBox))
            return "telefone";

        return null;
    }

    private void FocarProximoCampo(string campoAtual)
    {
        Control? proximo = campoAtual switch
        {
            "codigo" => NomeClienteCadastroTextBox,
            "nome" => NomeFantasiaClienteCadastroTextBox,
            "fantasia" => DocumentoClienteCadastroTextBox,
            "documento" => GrupoEmpresarialCadastroComboBox,
            "grupo" => EmailClienteCadastroTextBox,
            "email" => TelefoneClienteCadastroTextBox,
            _ => null
        };

        if (proximo is null)
            return;

        Dispatcher.UIThread.Post(() => FocarControle(proximo), DispatcherPriority.Input);
    }

    private static void FocarControle(Control controle)
    {
        controle.Focus(NavigationMethod.Tab, KeyModifiers.None);

        if (controle is TextBox textBox)
        {
            var tamanhoTexto = textBox.Text?.Length ?? 0;
            textBox.SelectionStart = tamanhoTexto;
            textBox.SelectionEnd = tamanhoTexto;
        }
    }
}
