using CommunityToolkit.Mvvm.ComponentModel;

namespace Protons.UI.Painel.ViewModels;

public enum DestinoNavegacaoPainel
{
    Dashboard,
    CadastroClientes,
    ImportarDocumentos,
    Pendencias,
    Relatorios,
    Configuracoes
}

public sealed partial class PainelViewModel
{
    [ObservableProperty] private DestinoNavegacaoPainel _destinoNavegacaoAtual = DestinoNavegacaoPainel.Dashboard;
    [ObservableProperty] private string _statusNavegacao = "Dashboard ativo.";
    [ObservableProperty] private int? _clienteContextoId;
    [ObservableProperty] private string _clienteContextoNome = "Nenhum cliente selecionado";
    [ObservableProperty] private string _clienteContextoResumo = "Selecione um cliente para abrir o contexto contábil.";

    private void NavegarPara(DestinoNavegacaoPainel destino, string origem)
    {
        DestinoNavegacaoAtual = destino;
        RegistrarEventoPainel("navegacao_painel", $"origem={origem} destino={destino}");
    }

    partial void OnDestinoNavegacaoAtualChanged(DestinoNavegacaoPainel value)
    {
        StatusNavegacao = value switch
        {
            DestinoNavegacaoPainel.Dashboard => "Dashboard ativo.",
            DestinoNavegacaoPainel.CadastroClientes => "Cadastro de clientes ativo.",
            DestinoNavegacaoPainel.ImportarDocumentos => "Tarefas em seleção na barra lateral.",
            DestinoNavegacaoPainel.Pendencias => "Pendências em acompanhamento.",
            DestinoNavegacaoPainel.Relatorios => "Relatórios em preparação.",
            DestinoNavegacaoPainel.Configuracoes => "Configurações em preparação.",
            _ => "Navegação ativa."
        };
    }
}
