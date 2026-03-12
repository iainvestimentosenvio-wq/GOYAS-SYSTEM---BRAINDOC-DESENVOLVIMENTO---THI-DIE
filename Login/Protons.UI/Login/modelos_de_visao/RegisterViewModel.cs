using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Services;
using Protons.UI.Common;

namespace Protons.UI.Login.ViewModels;

public sealed partial class RegisterViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly Services.LocalSettings _settings;
    private readonly IAuditLogQueryService _auditLogQuery;
    private readonly IClienteService _clienteService;
    private readonly ITarefaService _tarefaService;
    private readonly IUserDirectoryService _userDirectoryService;
    private readonly IAncorarPdfConfiguracaoService _ancorarPdfConfiguracaoService;
    private readonly Action<ViewModelBase> _navigate;

    [ObservableProperty] private string? _nome;
    [ObservableProperty] private string? _cpf;
    [ObservableProperty] private string? _cargo;
    [ObservableProperty] private string? _email;
    [ObservableProperty] private string? _senha;
    [ObservableProperty] private string? _mensagem;
    [ObservableProperty] private bool _mostrarSenha;

    [ObservableProperty] private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    /// <summary>Caractere usado para mascarar a senha (vazio se MostrarSenha).</summary>
    public char SenhaPasswordChar => MostrarSenha ? '\0' : '•';

    partial void OnMostrarSenhaChanged(bool value)
    {
        OnPropertyChanged(nameof(SenhaPasswordChar));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        SalvarCommand.NotifyCanExecuteChanged();
        VoltarCommand.NotifyCanExecuteChanged();
    }

    public RegisterViewModel(
        IAuthService authService,
        Services.LocalSettings settings,
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
    }

    private bool CanSalvar() => !IsBusy;
    private bool CanVoltar() => !IsBusy;

    [RelayCommand]
    private void ToggleMostrarSenha()
    {
        MostrarSenha = !MostrarSenha;
    }

    [RelayCommand(CanExecute = nameof(CanSalvar))]
    private async Task Salvar()
    {
        IsBusy = true;
        const string empresa = "Protons";
        var nome = Nome ?? string.Empty;
        var cpf = Cpf ?? string.Empty;
        var cargo = Cargo ?? string.Empty;
        var email = Email ?? string.Empty;
        var senha = Senha ?? string.Empty;
        var user = new User
        {
            Empresa = empresa,
            Nome = nome,
            Cpf = cpf,
            Cargo = cargo,
            Email = email
        };

        try
        {
            var result = await Task.Run(() => _authService.CriarConta(user, senha));
            if (result.Sucesso)
            {
                if (result.Role == UserRole.Supremo)
                    Mensagem = "Conta criada como Supremo (primeiro usuário). Faça login para continuar.";
                else if (result.Role == UserRole.Admin)
                    Mensagem = "Conta criada como administrador. Faça login para continuar.";
                else
                    Mensagem = "Seja bem-vindo à Prótons! Aguarde a autorização do administrador e acesse pelo login.";
                Senha = null;
            }
            else
            {
                Mensagem = result.Mensagem;
            }
        }
        catch (Exception ex)
        {
            OpsLogger.WriteError("registro: erro inesperado", ex);
            Mensagem = "Erro ao salvar cadastro. Tente novamente.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanVoltar))]
    private void Voltar()
    {
        _navigate(new LoginViewModel(
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
