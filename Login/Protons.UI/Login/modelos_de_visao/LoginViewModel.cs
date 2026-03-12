using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Core.Login.Validation;
using Protons.Core.Tarefas.Services;
using Protons.UI.Common;
using Protons.UI.Login.Services;
using PainelVM = Protons.UI.Painel.ViewModels.PainelViewModel;

namespace Protons.UI.Login.ViewModels;

public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly LocalSettings _settings;
    private readonly IAuditLogQueryService _auditLogQuery;
    private readonly IClienteService _clienteService;
    private readonly ITarefaService _tarefaService;
    private readonly IUserDirectoryService _userDirectoryService;
    private readonly IAncorarPdfConfiguracaoService _ancorarPdfConfiguracaoService;
    private readonly Action<ViewModelBase> _navigate;

    [ObservableProperty]
    private string? _email;

    [ObservableProperty]
    private string? _senha;

    [ObservableProperty]
    private bool _lembrarEmail;

    /// <summary>Quando true, a senha é exibida em texto claro; quando false, mascarada com •.</summary>
    [ObservableProperty]
    private bool _mostrarSenha;

    [ObservableProperty]
    private string? _mensagem;

    /// <summary>Caractere usado para mascarar a senha (vazio se MostrarSenha).</summary>
    public char SenhaPasswordChar => MostrarSenha ? '\0' : '•';

    partial void OnMostrarSenhaChanged(bool value)
    {
        OnPropertyChanged(nameof(SenhaPasswordChar));
    }

    [ObservableProperty]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        EntrarCommand.NotifyCanExecuteChanged();
        CriarContaCommand.NotifyCanExecuteChanged();
    }

    public LoginViewModel(
        IAuthService authService,
        LocalSettings settings,
        IAuditLogQueryService auditLogQuery,
        IClienteService clienteService,
        ITarefaService tarefaService,
        IUserDirectoryService userDirectoryService,
        IAncorarPdfConfiguracaoService ancorarPdfConfiguracaoService,
        Action<ViewModelBase> navigate)
    {
        _authService = authService;
        _settings = settings;
        _auditLogQuery = auditLogQuery;
        _clienteService = clienteService;
        _tarefaService = tarefaService;
        _userDirectoryService = userDirectoryService;
        _ancorarPdfConfiguracaoService = ancorarPdfConfiguracaoService;
        _navigate = navigate;

        var last = _settings.LoadLastEmail();
        if (!string.IsNullOrWhiteSpace(last))
        {
            Email = last;
            LembrarEmail = true;
        }
    }

    private bool CanEntrar() => !IsBusy;
    private bool CanCriarConta() => !IsBusy;

    [RelayCommand]
    private void ToggleMostrarSenha()
    {
        MostrarSenha = !MostrarSenha;
    }

    [RelayCommand(CanExecute = nameof(CanEntrar))]
    private async Task Entrar()
    {
        var email = (Email ?? string.Empty).Trim();
        var senha = Senha ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            Mensagem = "Informe o e-mail.";
            return;
        }

        if (!EmailValidator.EhValido(email))
        {
            Mensagem = "Informe um e-mail válido.";
            return;
        }

        if (string.IsNullOrWhiteSpace(senha))
        {
            Mensagem = "Informe a senha.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => _authService.Autenticar(email, senha, false));

            // CORREÇÃO 1.2: Voltar para UI thread antes de navegar
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Mensagem = result.Mensagem;
                if (result.Sucesso && result.UserId.HasValue)
                {
                    if (LembrarEmail)
                        _settings.SaveLastEmail(email);
                    else
                        _settings.SaveLastEmail(null);
                    Senha = null;
                    try
                    {
                        _navigate(new PainelVM(
                            _authService,
                            _settings,
                            _auditLogQuery,
                            _clienteService,
                            _tarefaService,
                            _userDirectoryService,
                            _ancorarPdfConfiguracaoService,
                            _navigate,
                            result.UserId.Value,
                            email,
                            result.Nome,
                            result.Role));
                    }
                    catch (Exception ex)
                    {
                        OpsLogger.WriteError("Login: erro ao abrir PainelViewModel", ex);
                        Mensagem = "Erro ao abrir o painel. Tente novamente.";
                    }
                }
            });
        }
        catch (Exception ex)
        {
            OpsLogger.WriteError("login: erro inesperado", ex);
            Mensagem = "Erro ao realizar login. Tente novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCriarConta))]
    private void CriarConta()
    {
        _navigate(new RegisterViewModel(
            _authService,
            _settings,
            _auditLogQuery,
            _clienteService,
            _tarefaService,
            _userDirectoryService,
            _ancorarPdfConfiguracaoService,
            _navigate));
    }
}
