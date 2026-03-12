using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Clientes.Models;
using Protons.UI.Login.ViewModels;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    [RelayCommand]
    private void Sair()
    {
        Dispose();
        RegistrarInteracaoPainel();
        RegistrarEventoPainel("sessao_logout", "motivo=usuario");
        _authService.RegistrarLogout(_userId, _email);
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

    [RelayCommand]
    private void NavegarParaDashboard()
    {
        RegistrarInteracaoPainel();
        ListaTarefasLateralAberta = false;
        NavegarPara(DestinoNavegacaoPainel.Dashboard, "sidebar_dashboard");
    }

    [RelayCommand]
    private void NavegarParaPendencias()
    {
        RegistrarInteracaoPainel();
        ListaTarefasLateralAberta = false;
        NavegarPara(DestinoNavegacaoPainel.Pendencias, "sidebar_pendencias");
        MensagemPendencias = "Central de pendências pronta para integração das tarefas.";
    }

    [RelayCommand]
    private void NavegarParaRelatorios()
    {
        RegistrarInteracaoPainel();
        ListaTarefasLateralAberta = false;
        NavegarPara(DestinoNavegacaoPainel.Relatorios, "sidebar_relatorios");
        DefinirMensagemCadastroCliente(
            "Relatórios em evolução. Estrutura já preparada.",
            ClienteCadastroSeveridade.Info,
            ClienteCadastroCampoErro.Nenhum);
    }

    [RelayCommand]
    private void NavegarParaConfiguracoes()
    {
        RegistrarInteracaoPainel();
        ListaTarefasLateralAberta = false;
        NavegarPara(DestinoNavegacaoPainel.Configuracoes, "sidebar_configuracoes");
        DefinirMensagemCadastroCliente(
            "Configurações em preparação para a próxima fase.",
            ClienteCadastroSeveridade.Info,
            ClienteCadastroCampoErro.Nenhum);
    }

    [RelayCommand]
    private async Task AbrirHistoricoTarefas()
    {
        RegistrarInteracaoPainel();
        RegistrarEventoPainel("tarefas_historico_abrir");
        await CarregarTarefasClienteAsync();
    }
}
