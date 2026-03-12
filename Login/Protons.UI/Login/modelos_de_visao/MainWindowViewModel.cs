using System;
using Protons.Core.Clientes.Services;
using Protons.Core.Login.Models;
using Protons.Core.Login.Services;
using Protons.Core.Tarefas.Services;
using Protons.UI.Login.Services;
using PainelVM = Protons.UI.Painel.ViewModels.PainelViewModel;

namespace Protons.UI.Login.ViewModels;

/// <summary>
/// ViewModel raiz da janela principal. Atua como roteador de tela: decide entre o fluxo
/// normal de login e o modo de desenvolvimento com bypass de autenticação.
/// </summary>
/// <remarks>
/// O <see cref="DisplayedViewModel"/> é vinculado ao <c>ViewLocator</c> via AXAML e
/// controla qual View é renderizada em tempo de execução.
///
/// Fluxo normal: LoadingView → LoginView → (após autenticação) → PainelView.
/// Fluxo bypass: LoadingView → PainelView (direto, sem login).
///
/// O bypass só é ativado se TODAS as condições forem satisfeitas simultaneamente:
/// compilação DEBUG, variável de ambiente <c>PROTONS_PAINEL_DIRETO_HABILITADO=1</c>,
/// variável <c>PROTONS_PAINEL_DIRETO=1</c> e ambiente identificado como Development.
/// Em Release, <c>IsPainelDiretoAtivo()</c> retorna false incondicionalmente via <c>#if</c>.
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
    private ViewModelBase _currentViewModel;
    private ViewModelBase _displayedViewModel;

    public MainWindowViewModel()
    {
        _currentViewModel = new LoadingViewModel();
        _displayedViewModel = _currentViewModel;
    }

    /// <summary>
    /// Ponto de entrada pós-DI. Recebe todos os serviços resolvidos pelo contêiner e
    /// despacha para o fluxo correto (bypass ou login normal).
    /// </summary>
    /// <remarks>
    /// Chamado uma única vez por <c>App.axaml.cs</c> após a resolução completa do
    /// contêiner de injeção de dependência. Não deve ser chamado novamente após a
    /// inicialização — o estado interno não suporta reinicialização.
    /// </remarks>
    public void Initialize(
        IAuthService authService,
        LocalSettings settings,
        IAuditLogQueryService auditLogQuery,
        IClienteService clienteService,
        ITarefaService tarefaService,
        IUserDirectoryService userDirectoryService,
        IAncorarPdfConfiguracaoService ancorarPdfConfiguracaoService)
    {
        if (IsPainelDiretoAtivo())
        {
            CurrentViewModel = CriarPainelDireto(authService, settings, auditLogQuery, clienteService, tarefaService, userDirectoryService, ancorarPdfConfiguracaoService);
            SetDisplayedViewModel(CurrentViewModel);
            return;
        }

        CurrentViewModel = new LoginViewModel(authService, settings, auditLogQuery, clienteService, tarefaService, userDirectoryService, ancorarPdfConfiguracaoService, NavigateTo);
        SetDisplayedViewModel(CurrentViewModel);
    }

    /// <summary>
    /// ViewModel atualmente ativo na pilha de navegação interna.
    /// Mudanças nesta propriedade disparam <c>PropertyChanged</c> para atualizar o binding.
    /// </summary>
    public ViewModelBase CurrentViewModel
    {
        get => _currentViewModel;
        private set => SetProperty(ref _currentViewModel, value);
    }

    /// <summary>
    /// ViewModel vinculado diretamente ao <c>ViewLocator</c> no AXAML.
    /// Em geral é igual a <see cref="CurrentViewModel"/>, mas pode divergir durante
    /// transições animadas ou estados intermediários.
    /// </summary>
    public ViewModelBase DisplayedViewModel
    {
        get => _displayedViewModel;
        private set => SetProperty(ref _displayedViewModel, value);
    }

    /// <summary>
    /// Callback de navegação injetado nos ViewModels filhos (ex: <c>LoginViewModel</c>).
    /// Permite que ViewModels filhos solicitem navegação sem acoplamento direto à janela.
    /// </summary>
    private void NavigateTo(ViewModelBase viewModel)
    {
        CurrentViewModel = viewModel;
    }

    /// <summary>
    /// Sincroniza <see cref="DisplayedViewModel"/> com um ViewModel específico.
    /// Exposto como <c>internal</c> para permitir testes de integração sem reflexão.
    /// </summary>
    internal void SetDisplayedViewModel(ViewModelBase viewModel)
    {
        DisplayedViewModel = viewModel;
    }

    /// <summary>
    /// Instancia o <see cref="PainelVM"/> com um usuário dev sintético lido das variáveis
    /// de ambiente, evitando a necessidade de um registro real no banco de dados.
    /// </summary>
    /// <remarks>
    /// Variáveis de ambiente utilizadas (com valores padrão):
    /// <list type="bullet">
    ///   <item><c>PROTONS_PAINEL_USER_ID</c> → padrão: 1</item>
    ///   <item><c>PROTONS_PAINEL_EMAIL</c> → padrão: painel.dev@protons.local</item>
    ///   <item><c>PROTONS_PAINEL_NOME</c> → padrão: Painel Dev</item>
    ///   <item><c>PROTONS_PAINEL_ROLE</c> → padrão: Admin (aceita "admin" ou "usuario")</item>
    /// </list>
    /// </remarks>
    private PainelVM CriarPainelDireto(
        IAuthService authService,
        LocalSettings settings,
        IAuditLogQueryService auditLogQuery,
        IClienteService clienteService,
        ITarefaService tarefaService,
        IUserDirectoryService userDirectoryService,
        IAncorarPdfConfiguracaoService ancorarPdfConfiguracaoService)
    {
        var userId = LerInteiro("PROTONS_PAINEL_USER_ID", 1);
        var email = LerTexto("PROTONS_PAINEL_EMAIL", "painel.dev@protons.local");
        var nome = LerTexto("PROTONS_PAINEL_NOME", "Painel Dev");
        var role = LerRole("PROTONS_PAINEL_ROLE", UserRole.Admin);

        return new PainelVM(
            authService,
            settings,
            auditLogQuery,
            clienteService,
            tarefaService,
            userDirectoryService,
            ancorarPdfConfiguracaoService,
            NavigateTo,
            userId,
            email,
            nome,
            role);
    }

    /// <summary>
    /// Porta de entrada do bypass. Retorna <c>true</c> somente se o conjunto completo
    /// de guardas de segurança for satisfeito.
    /// </summary>
    /// <remarks>
    /// Guardas avaliadas em ordem (curto-circuito):
    /// <list type="number">
    ///   <item>Compilação DEBUG — em Release retorna <c>false</c> via diretiva <c>#if</c>, sem avaliar nada mais.</item>
    ///   <item><see cref="IsPainelDiretoPermitidoPorPolitica"/> — <c>PROTONS_PAINEL_DIRETO_HABILITADO=1</c> deve estar definida.</item>
    ///   <item><c>PROTONS_PAINEL_DIRETO=1</c> — a sessão deve ter solicitado o bypass explicitamente.</item>
    ///   <item><see cref="IsDevelopmentEnvironment"/> — o ambiente deve ser Development/Dev/Local.</item>
    /// </list>
    /// A exigência de múltiplas variáveis é intencional: impede ativação acidental
    /// por configuração parcial de ambiente.
    /// </remarks>
    private static bool IsPainelDiretoAtivo()
    {
#if !DEBUG
        return false;
#else
        if (!IsPainelDiretoPermitidoPorPolitica())
            return false;

        var raw = Environment.GetEnvironmentVariable("PROTONS_PAINEL_DIRETO");
        var bypassSolicitado = string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        if (!bypassSolicitado)
            return false;

        return IsDevelopmentEnvironment();
#endif
    }

    /// <summary>
    /// Guarda de política administrativa: verifica se o bypass foi explicitamente
    /// habilitado pelo operador via <c>PROTONS_PAINEL_DIRETO_HABILITADO</c>.
    /// </summary>
    /// <remarks>
    /// Existe para permitir que ambientes de CI ou builds especiais bloqueiem o bypass
    /// independentemente das demais variáveis. Quando ausente ou vazia, nega por padrão
    /// (fail-closed).
    /// </remarks>
    private static bool IsPainelDiretoPermitidoPorPolitica()
    {
        var raw = Environment.GetEnvironmentVariable("PROTONS_PAINEL_DIRETO_HABILITADO");
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifica se o processo está rodando em ambiente de desenvolvimento consultando,
    /// em ordem de prioridade: <c>PROTONS_ENVIRONMENT</c>, <c>DOTNET_ENVIRONMENT</c>,
    /// <c>ASPNETCORE_ENVIRONMENT</c>.
    /// </summary>
    /// <remarks>
    /// A cascata de variáveis garante compatibilidade com projetos que usam apenas
    /// as convenções do ASP.NET Core ou do .NET genérico. O valor "local" é aceito
    /// para ambientes de desenvolvedor local sem infraestrutura de orquestração.
    /// </remarks>
    private static bool IsDevelopmentEnvironment()
    {
        var ambiente = LerTexto(
            "PROTONS_ENVIRONMENT",
            LerTexto("DOTNET_ENVIRONMENT", LerTexto("ASPNETCORE_ENVIRONMENT", string.Empty)));

        return string.Equals(ambiente, "development", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ambiente, "dev", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ambiente, "local", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Lê uma variável de ambiente como inteiro positivo. Retorna <paramref name="padrao"/>
    /// se a variável estiver ausente, inválida ou não positiva.
    /// </summary>
    private static int LerInteiro(string nomeVariavel, int padrao)
    {
        var raw = Environment.GetEnvironmentVariable(nomeVariavel);
        return int.TryParse(raw, out var valor) && valor > 0 ? valor : padrao;
    }

    /// <summary>
    /// Lê uma variável de ambiente como texto. Retorna <paramref name="padrao"/>
    /// se a variável estiver ausente ou contiver apenas espaços.
    /// </summary>
    private static string LerTexto(string nomeVariavel, string padrao)
    {
        var raw = Environment.GetEnvironmentVariable(nomeVariavel);
        return string.IsNullOrWhiteSpace(raw) ? padrao : raw;
    }

    /// <summary>
    /// Lê uma variável de ambiente como <see cref="UserRole"/>. Aceita "admin" (case-insensitive);
    /// qualquer outro valor ou ausência resulta em <paramref name="padrao"/>.
    /// </summary>
    private static UserRole LerRole(string nomeVariavel, UserRole padrao)
    {
        var raw = Environment.GetEnvironmentVariable(nomeVariavel);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return padrao;
        }

        return string.Equals(raw, "admin", StringComparison.OrdinalIgnoreCase)
            ? UserRole.Admin
            : UserRole.Usuario;
    }
}
