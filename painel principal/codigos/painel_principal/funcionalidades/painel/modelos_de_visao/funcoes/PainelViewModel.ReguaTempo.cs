using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    private const int MaxOcorrenciasRecorrentes = 500;
    private const int MaxHistoricoExecucaoAutomaticaConcluida = 4096;

    [ObservableProperty] private double _nivelZoomRegua = 0.5;
    [ObservableProperty] private DateTime _centroTemporalRegua = DateTime.UtcNow;
    [ObservableProperty] private DateTime _horaAtualRegua = DateTime.UtcNow;
    [ObservableProperty] private bool _modoAutomaticoRegua = true;
    [ObservableProperty] private bool _painelEsteirasDockAberto;
    [ObservableProperty] private bool _configuracaoTarefaAberta;
    [ObservableProperty] private string _configuracaoTarefaTitulo = string.Empty;
    [ObservableProperty] private string _configuracaoTarefaSubtitulo = string.Empty;
    [ObservableProperty] private string _configuracaoTarefaDetalhes = string.Empty;
    [ObservableProperty] private int? _configuracaoTarefaId;
    [ObservableProperty] private bool _configuracaoTarefaSomenteLeitura;
    [ObservableProperty] private bool _configuracaoTarefaEhAncorarPdf;
    [ObservableProperty] private bool _backlogExecucaoPendente;
    [ObservableProperty] private string _backlogExecucaoResumo = string.Empty;
    // C7: modo "Escolher quais executar" — exibe lista de backlog com checkboxes.
    [ObservableProperty] private bool _backlogModoSelecaoAtivo;
    [ObservableProperty] private bool _confirmacaoRemoverEsteiraAberta;
    [ObservableProperty] private int? _confirmacaoRemoverEsteiraId;
    [ObservableProperty] private string _confirmacaoRemoverEsteiraMensagem = string.Empty;
    [ObservableProperty] private string _confirmacaoRemoverEsteiraTitulo = "Remover esteira";

    public ObservableCollection<EsteiraViewModel> Esteiras { get; } = new();
    /// <summary>C7: lista de itens de backlog com flag de seleção para "Escolher quais executar".</summary>
    public ObservableCollection<BacklogItemSelecionavelVm> BacklogItemsSelecionaveis { get; } = new();

    private readonly HashSet<int> _esteirasCanceladasNaSessao = new();
    private readonly HashSet<long> _tarefasBacklogPendentes = new();
    private readonly SemaphoreSlim _execucaoAutomaticaSerial = new(1, 1);
    private readonly object _execucaoAutomaticaLock = new();
    private readonly HashSet<string> _ciclosExecucaoAutomaticaEmExecucao = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ciclosExecucaoAutomaticaConcluidos = new(StringComparer.Ordinal);
    private readonly Queue<string> _ordemCiclosExecucaoAutomaticaConcluidos = new();
    // C7: itens detalhados do backlog para preencher BacklogItemsSelecionaveis.
    private List<AncorarPdfBacklogPendenteItem> _backlogItensDetalhados = new();
    private readonly Dictionary<int, EstadoEsteirasCliente> _estadoEsteirasPorCliente = new();
    private readonly Dictionary<string, Action<NovaTarefaDropPayload>> _handlersDropFerramenta = new(StringComparer.OrdinalIgnoreCase);
    private int _proximoIdEsteira = 1;
    public bool PodeUsarEsteiras => ClienteContextoId is > 0;
    public bool ConfiguracaoTarefaEhGenerica => !ConfiguracaoTarefaEhAncorarPdf;
    public string IconePainelEsteirasDock => PainelEsteirasDockAberto ? "›" : "‹";
    public string TooltipPainelEsteirasDock => PainelEsteirasDockAberto ? "Recolher esteiras" : "Abrir esteiras";

    partial void OnConfiguracaoTarefaEhAncorarPdfChanged(bool value)
    {
        OnPropertyChanged(nameof(ConfiguracaoTarefaEhGenerica));
        OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivel));
        OnPropertyChanged(nameof(ModalConfiguracaoTarefaVisivel));
    }

    partial void OnConfiguracaoTarefaAbertaChanged(bool value)
    {
        OnPropertyChanged(nameof(ModalConfiguracaoTarefaVisivel));
    }

    partial void OnPainelEsteirasDockAbertoChanged(bool value)
    {
        OnPropertyChanged(nameof(IconePainelEsteirasDock));
        OnPropertyChanged(nameof(TooltipPainelEsteirasDock));
    }

    private void InicializarReguaTempo()
    {
        PainelEsteirasDockAberto = false;
        LimparEstadoEsteirasSemCliente();
    }

    private void AtualizarDisponibilidadeEsteiras()
    {
        OnPropertyChanged(nameof(PodeUsarEsteiras));
        AlternarPainelEsteirasDockCommand.NotifyCanExecuteChanged();
        AdicionarEsteiraCommand.NotifyCanExecuteChanged();
        AbrirConfigNovaTarefaPorDropCommand.NotifyCanExecuteChanged();
    }

    private bool BloquearUsoEsteirasSemCliente(string acao)
    {
        if (PodeUsarEsteiras)
            return false;

        MensagemTarefasCliente = "Selecione um cliente antes de soltar a ferramenta na esteira.";
        RegistrarEventoPainel("regua_uso_bloqueado_sem_cliente", $"acao={acao}");
        return true;
    }

    private void EncerrarFluxosTarefaPorMudancaContexto()
    {
        AncorarPdfAgendamentoBasico.ResetSilencioso();
        AncorarPdfConfiguracao.Reset();
        ConfiguracaoTarefaAberta = false;
        ConfiguracaoTarefaId = null;
        ConfiguracaoTarefaTitulo = string.Empty;
        ConfiguracaoTarefaEhAncorarPdf = false;
        ConfiguracaoTarefaSomenteLeitura = false;
        ConfiguracaoTarefaSubtitulo = string.Empty;
        ConfiguracaoTarefaDetalhes = string.Empty;
        OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivel));
    }

    private void AtualizarHoraAtualRegua()
    {
        HoraAtualRegua = DateTime.UtcNow;

        // Movimento automatico da esteira
        if (ModoAutomaticoRegua)
        {
            var anterior = CentroTemporalRegua;
            CentroTemporalRegua = HoraAtualRegua; // Sincroniza com tempo real
            foreach (var esteira in Esteiras)
            {
                esteira.CentroTemporal = HoraAtualRegua;
            }

            // Verificar execucao automatica
            VerificarExecucaoAutomatica(anterior, CentroTemporalRegua);
        }
    }

    private void VerificarExecucaoAutomatica(DateTime centroAnterior, DateTime centroAtual)
    {
        if (AncorarPdfMisfireBacklogPolicy.DetectarMisfire(centroAnterior, centroAtual, out var atraso))
        {
            PrepararBacklogComDecisaoExplicita(centroAnterior, centroAtual, atraso);
        }
    }

    private void PrepararBacklogComDecisaoExplicita(DateTime centroAnterior, DateTime centroAtual, TimeSpan atraso)
    {
        DispararComSeguranca(
            SincronizarBacklogPersistenteAsync(atraso),
            "regua_backlog_sync_falha");

        RegistrarEventoPainel(
            "regua_misfire_backlog_detectado",
            $"atraso_segundos={(int)atraso.TotalSeconds} janela_inicio={centroAnterior:o} janela_fim={centroAtual:o}");
    }

    [RelayCommand]
    private void ResolverBacklogExecucao(bool executarPendentes)
    {
        if (!BacklogExecucaoPendente)
            return;

        var backlogPendentes = _tarefasBacklogPendentes.ToList();
        BacklogExecucaoPendente = false;
        BacklogExecucaoResumo = string.Empty;
        _tarefasBacklogPendentes.Clear();

        RegistrarEventoPainel(
            "regua_misfire_backlog_decisao",
            $"executar_pendentes={executarPendentes} total={backlogPendentes.Count}");

        foreach (var backlogId in backlogPendentes)
        {
            var entrada = new AncorarPdfBacklogDecisaoEntrada
            {
                BacklogId = backlogId,
                ExecutarPendentes = executarPendentes
            };

            DispararComSeguranca(
                Task.Run(() => _ancorarPdfConfiguracaoService.RegistrarDecisaoBacklog(entrada, _userId)),
                "regua_execucao_backlog_falha");
        }

        DispararComSeguranca(CarregarTarefasClienteAsync(), "regua_backlog_recarregar_tarefas_falha");
    }

    private bool PodeAlternarPainelEsteirasDock() => PodeUsarEsteiras;

    [RelayCommand(CanExecute = nameof(PodeAlternarPainelEsteirasDock))]
    private void AlternarPainelEsteirasDock()
    {
        if (BloquearUsoEsteirasSemCliente("alternar_dock"))
            return;

        RegistrarInteracaoPainel();
        PainelEsteirasDockAberto = !PainelEsteirasDockAberto;
        RegistrarEventoPainel("regua_dock_toggle", $"aberto={PainelEsteirasDockAberto}");
    }

    /// <summary>
    /// C7: abre o modo de seleção individual de itens do backlog pendente.
    /// Popula BacklogItemsSelecionaveis com todos os itens pré-selecionados.
    /// </summary>
    [RelayCommand]
    private void AbrirSelecaoBacklog()
    {
        if (!BacklogExecucaoPendente || _backlogItensDetalhados.Count == 0)
            return;

        BacklogItemsSelecionaveis.Clear();
        foreach (var item in _backlogItensDetalhados.OrderBy(x => x.DetectadoEmUtc))
        {
            var localTime = item.JanelaAlvoUtc.ToLocalTime();
            BacklogItemsSelecionaveis.Add(new BacklogItemSelecionavelVm
            {
                BacklogId = item.BacklogId,
                TarefaId = item.TarefaId,
                Titulo = ResolverNomeTarefaBacklog(item.TarefaId, localTime),
                JanelaAlvoUtc = item.JanelaAlvoUtc,
                AtrasoSegundos = item.AtrasoSegundos,
                Selecionado = true
            });
        }

        BacklogModoSelecaoAtivo = true;
        RegistrarEventoPainel("regua_backlog_modo_selecao_aberto",
            $"total_itens={BacklogItemsSelecionaveis.Count}");
    }

    /// <summary>
    /// C7: confirma a seleção — executa apenas os itens marcados, ignora os desmarcados.
    /// </summary>
    [RelayCommand]
    private void ConfirmarSelecaoBacklog()
    {
        if (!BacklogModoSelecaoAtivo)
            return;

        var selecionados = BacklogItemsSelecionaveis.Where(x => x.Selecionado).Select(x => x.BacklogId).ToList();
        var ignorados = BacklogItemsSelecionaveis.Where(x => !x.Selecionado).Select(x => x.BacklogId).ToList();

        BacklogModoSelecaoAtivo = false;
        BacklogItemsSelecionaveis.Clear();
        BacklogExecucaoPendente = false;
        BacklogExecucaoResumo = string.Empty;
        _tarefasBacklogPendentes.Clear();

        RegistrarEventoPainel("regua_backlog_selecao_confirmada",
            $"executar={selecionados.Count} ignorar={ignorados.Count}");

        foreach (var backlogId in selecionados)
        {
            var entrada = new AncorarPdfBacklogDecisaoEntrada
            {
                BacklogId = backlogId,
                ExecutarPendentes = true
            };
            DispararComSeguranca(
                Task.Run(() => _ancorarPdfConfiguracaoService.RegistrarDecisaoBacklog(entrada, _userId)),
                "regua_execucao_backlog_selecionado_falha");
        }

        foreach (var backlogId in ignorados)
        {
            var entrada = new AncorarPdfBacklogDecisaoEntrada
            {
                BacklogId = backlogId,
                ExecutarPendentes = false
            };
            DispararComSeguranca(
                Task.Run(() => _ancorarPdfConfiguracaoService.RegistrarDecisaoBacklog(entrada, _userId)),
                "regua_execucao_backlog_ignorado_falha");
        }

        DispararComSeguranca(CarregarTarefasClienteAsync(), "regua_backlog_recarregar_tarefas_falha");
    }

    /// <summary>C7: cancela o modo de seleção sem tomar nenhuma ação.</summary>
    [RelayCommand]
    private void CancelarSelecaoBacklog()
    {
        BacklogModoSelecaoAtivo = false;
        BacklogItemsSelecionaveis.Clear();
        RegistrarEventoPainel("regua_backlog_selecao_cancelada", null);
    }

    private string ResolverNomeTarefaBacklog(int tarefaId, DateTime localTime)
    {
        // Tenta resolver o título da tarefa via coleção atual da régua.
        var nomeRegua = Esteiras
            .SelectMany(e => e.Tarefas)
            .FirstOrDefault(t => t.TarefaId == tarefaId)
            ?.Titulo;

        return string.IsNullOrWhiteSpace(nomeRegua)
            ? $"Tarefa #{tarefaId} — {localTime:dd/MM HH:mm}"
            : $"{nomeRegua} — {localTime:dd/MM HH:mm}";
    }

    private async System.Threading.Tasks.Task SincronizarBacklogPersistenteAsync(TimeSpan? atrasoDetectado)
    {
        try
        {
            if (!ClienteContextoId.HasValue || ClienteContextoId.Value <= 0)
                return;

            var pendencias = await Task.Run(() =>
                _ancorarPdfConfiguracaoService.ListarBacklogPendente(ClienteContextoId.Value, _userId, limite: 200));

            // C7: armazena itens detalhados para o modo de seleção individual.
            _backlogItensDetalhados = pendencias.ToList();
            _tarefasBacklogPendentes.Clear();
            foreach (var item in pendencias)
                _tarefasBacklogPendentes.Add(item.BacklogId);

            if (_tarefasBacklogPendentes.Count == 0)
            {
                BacklogExecucaoPendente = false;
                BacklogExecucaoResumo = string.Empty;
                _backlogItensDetalhados.Clear();
                return;
            }

            var atraso = atrasoDetectado ?? TimeSpan.FromSeconds(Math.Max(0, pendencias.Max(x => x.AtrasoSegundos)));
            BacklogExecucaoPendente = true;
            BacklogExecucaoResumo = AncorarPdfMisfireBacklogPolicy.CriarResumoBacklog(_tarefasBacklogPendentes.Count, atraso);
        }
        catch (Exception ex)
        {
            RegistrarErroPainel("regua_backlog_sync_falha", ex);
        }
    }

    [RelayCommand]
    private void CentralizarAgora()
    {
        RegistrarInteracaoPainel();
        CentroTemporalRegua = DateTime.UtcNow;
        foreach (var esteira in Esteiras)
        {
            esteira.CentroTemporal = CentroTemporalRegua;
        }
        ModoAutomaticoRegua = true; // Reativar modo automatico
    }

    [RelayCommand]
    private void NavegarParaTarefa(int tarefaId)
    {
        foreach (var esteira in Esteiras)
        {
            var tarefa = esteira.Tarefas.FirstOrDefault(t => t.TarefaId == tarefaId);
            if (tarefa != null)
            {
                esteira.CentroTemporal = tarefa.VencimentoUtc;
                CentroTemporalRegua = tarefa.VencimentoUtc;
                ModoAutomaticoRegua = false; // Pausar movimento
                RegistrarEventoPainel("regua_navegacao_tarefa", $"tarefa_id={tarefaId}");
                break;
            }
        }
    }

    private bool PodeAdicionarEsteira() => PodeUsarEsteiras;

    [RelayCommand(CanExecute = nameof(PodeAdicionarEsteira))]
    private void AdicionarEsteira()
    {
        if (BloquearUsoEsteirasSemCliente("adicionar_esteira"))
            return;

        RegistrarInteracaoPainel();
        var novoId = ObterProximoIdEsteira();
        var novaOrdem = Esteiras.Count + 1;
        var centroBase = Esteiras.Count > 0 ? Esteiras[0].CentroTemporal : DateTime.UtcNow;
        var zoomBase = Esteiras.Count > 0 ? Esteiras[0].NivelZoom : 0.5;
        var novaEsteira = CriarEsteiraVm(novoId, $"Esteira {novaOrdem}", novaOrdem, zoomBase, centroBase);
        Esteiras.Add(novaEsteira);
        RegistrarEventoPainel("regua_esteira_adicionada", $"id={novaEsteira.Id} nome={novaEsteira.Nome}");
    }

    [RelayCommand]
    private void SolicitarRemoverEsteira(int esteiraId)
    {
        RegistrarInteracaoPainel();
        var esteira = Esteiras.FirstOrDefault(e => e.Id == esteiraId);
        if (esteira is null)
            return;

        if (Esteiras.Count <= 1)
            return; // Manter ao menos uma esteira

        var tarefasNaEsteira = esteira.Tarefas.Count;
        ConfirmacaoRemoverEsteiraId = esteiraId;
        ConfirmacaoRemoverEsteiraTitulo = $"Remover {esteira.Nome}";
        ConfirmacaoRemoverEsteiraMensagem = tarefasNaEsteira > 0
            ? $"Esta esteira possui {tarefasNaEsteira} tarefa(s). Se remover, as tarefas serão ocultadas desta esteira apenas nesta sessão e não serão executadas automaticamente aqui. Deseja continuar?"
            : "Deseja remover esta esteira de tarefas?";
        ConfirmacaoRemoverEsteiraAberta = true;
    }

    [RelayCommand]
    private void CancelarRemoverEsteira()
    {
        ConfirmacaoRemoverEsteiraAberta = false;
        ConfirmacaoRemoverEsteiraId = null;
        ConfirmacaoRemoverEsteiraMensagem = string.Empty;
    }

    [RelayCommand]
    private void ConfirmarRemoverEsteira()
    {
        if (!ConfirmacaoRemoverEsteiraId.HasValue)
            return;

        RemoverEsteiraInterna(ConfirmacaoRemoverEsteiraId.Value);

        ConfirmacaoRemoverEsteiraAberta = false;
        ConfirmacaoRemoverEsteiraId = null;
        ConfirmacaoRemoverEsteiraMensagem = string.Empty;
    }

    private void RemoverEsteiraInterna(int esteiraId)
    {
        var esteira = Esteiras.FirstOrDefault(e => e.Id == esteiraId);
        if (esteira is null)
            return;

        if (Esteiras.Count <= 1)
            return;

        _esteirasCanceladasNaSessao.Add(esteiraId);
        Esteiras.Remove(esteira);
        ReordenarEsteiras();
        RegistrarEventoPainel("regua_esteira_removida", $"id={esteiraId} tarefas_ocultadas_sessao={esteira.Tarefas.Count}");
    }

    [RelayCommand]
    private async Task AbrirConfigTarefa(int tarefaId)
    {
        RegistrarInteracaoPainel();

        var somenteLeitura = await DeterminarModoSomenteLeituraParaTarefaAsync(tarefaId);
        var tarefa = await _tarefaService.ObterPorIdAsync(tarefaId, _userId);

        if (tarefa is not null && FerramentaTarefaIds.EhAncorarPdf(tarefa.FerramentaId))
        {
            try
            {
                await AncorarPdfConfiguracao.AbrirExistenteAsync(
                    tarefa,
                    _userId,
                    string.IsNullOrWhiteSpace(Nome) ? Email : Nome!,
                    somenteLeitura,
                    solicitanteEhAdmin: IsAdmin);

                ConfiguracaoTarefaEhAncorarPdf = true;
                ConfiguracaoTarefaId = tarefaId;
                ConfiguracaoTarefaTitulo = "Ancorar PDF";
                ConfiguracaoTarefaSomenteLeitura = somenteLeitura;
                ConfiguracaoTarefaSubtitulo = somenteLeitura ? "Leitura da tarefa passada" : "Edicao da tarefa";
                ConfiguracaoTarefaDetalhes = $"Ferramenta: {FerramentaTarefaIds.AncorarPdfCanonico} | tarefa_id={tarefaId}";
                ConfiguracaoTarefaAberta = true;

                RegistrarEventoPainel(
                    "regua_config_tarefa_abrir",
                    $"tarefa_id={tarefaId} ferramenta={FerramentaTarefaIds.AncorarPdfCanonico} modo={(somenteLeitura ? "leitura" : "edicao")}");
                return;
            }
            catch (Exception ex)
            {
                AncorarPdfConfiguracao.Reset();
                RegistrarErroPainel("regua_config_tarefa_ancorar_pdf_falha", ex, $"tarefa_id={tarefaId}");
            }
        }

        ConfiguracaoTarefaId = tarefaId;
        ConfiguracaoTarefaTitulo = $"Tarefa #{tarefaId}";
        ConfiguracaoTarefaEhAncorarPdf = false;
        ConfiguracaoTarefaSomenteLeitura = somenteLeitura;
        ConfiguracaoTarefaSubtitulo = somenteLeitura ? "Leitura da tarefa passada" : "Edição da tarefa";
        ConfiguracaoTarefaDetalhes = somenteLeitura
            ? "Esta tarefa já está no passado e abre em modo leitura (edição bloqueada)."
            : "Configuração detalhada será conectada no próximo passo.";
        ConfiguracaoTarefaAberta = true;
        RegistrarEventoPainel(
            "regua_config_tarefa_abrir",
            $"tarefa_id={tarefaId} modo={(somenteLeitura ? "leitura" : "edicao")}");
    }

    private async Task<bool> DeterminarModoSomenteLeituraParaTarefaAsync(int tarefaId)
    {
        var agoraUtc = HoraAtualRegua == default ? DateTime.UtcNow : HoraAtualRegua;

        var itemRegua = Esteiras
            .SelectMany(e => e.Tarefas)
            .FirstOrDefault(t => t.TarefaId == tarefaId);

        if (itemRegua is not null)
            return NormalizarUtc(itemRegua.VencimentoUtc) < NormalizarUtc(agoraUtc);

        var tarefa = await _tarefaService.ObterPorIdAsync(tarefaId, _userId);
        if (tarefa is null)
            return true;

        return NormalizarUtc(tarefa.VencimentoUtc) < NormalizarUtc(agoraUtc);
    }

    private bool PodeAbrirConfigNovaTarefaPorDrop(NovaTarefaDropPayload? payload) => PodeUsarEsteiras;

    [RelayCommand(CanExecute = nameof(PodeAbrirConfigNovaTarefaPorDrop))]
    private void AbrirConfigNovaTarefaPorDrop(NovaTarefaDropPayload payload)
    {
        if (BloquearUsoEsteirasSemCliente("abrir_config_por_drop"))
            return;

        RegistrarInteracaoPainel();
        HandleDropFerramenta(payload);
    }

    private void HandleDropFerramenta(NovaTarefaDropPayload payload)
    {
        InicializarHandlersDropFerramenta();

        if (_handlersDropFerramenta.TryGetValue(payload.FerramentaId, out var handler))
        {
            handler(payload);
            return;
        }

        AbrirModalGenericoPorFerramenta(payload);
    }

    private void InicializarHandlersDropFerramenta()
    {
        if (_handlersDropFerramenta.Count > 0)
            return;

        _handlersDropFerramenta[FerramentaTarefaIds.AncorarPdfCanonico] = AbrirModalAncorarPdfPorFerramenta;

        // Nesta fase, as demais ferramentas compartilham o modal generico.
        _handlersDropFerramenta["validador_fiscal"] = AbrirModalGenericoPorFerramenta;
        _handlersDropFerramenta["conector_erp"] = AbrirModalGenericoPorFerramenta;
        _handlersDropFerramenta["revisao_humana"] = AbrirModalGenericoPorFerramenta;
        _handlersDropFerramenta["checklist_qa"] = AbrirModalGenericoPorFerramenta;
        _handlersDropFerramenta["saida_relatorio"] = AbrirModalGenericoPorFerramenta;
    }

    private void AbrirModalAncorarPdfPorFerramenta(NovaTarefaDropPayload payload)
    {
        if (BloquearUsoEsteirasSemCliente("abrir_wizard_ancorar_pdf"))
            return;

        // Garantir EsteiraId válido: usar primeira esteira se payload.EsteiraId não existir.
        var esteiraIdValido = payload.EsteiraId;
        if (Esteiras.Count > 0 && Esteiras.All(e => e.Id != payload.EsteiraId))
            esteiraIdValido = Esteiras[0].Id;
        var payloadCorrigido = payload with { EsteiraId = esteiraIdValido };

        try
        {
            // Garante que o modal de edição antigo não cubra o wizard (limpa estado stale).
            AncorarPdfConfiguracao.Reset();
            ConfiguracaoTarefaAberta = false;
            ConfiguracaoTarefaEhAncorarPdf = false;

            // Wizard C11: nova tarefa via drop abre o painel básico (não o modal completo).
            AncorarPdfAgendamentoBasico.IniciarPorDrop(
                payloadCorrigido,
                ClienteContextoId!.Value,
                _userId,
                string.IsNullOrWhiteSpace(Nome) ? Email : Nome!);

            // Pré-aviso visual de conflito (checagem final acontece no salvar do wizard).
            var alvoUtcNorm = NormalizarUtcParaMinuto(payloadCorrigido.TempoAlvoUtc);
            var esteira = Esteiras.FirstOrDefault(e => e.Id == payloadCorrigido.EsteiraId);
            var conflito = esteira?.Tarefas.FirstOrDefault(t =>
                NormalizarUtcParaMinuto(t.VencimentoUtc) == alvoUtcNorm);
            if (conflito is not null)
                AncorarPdfAgendamentoBasico.Mensagem = $"Conflito: já existe tarefa '{conflito.Titulo}' neste horário na esteira.";

            RegistrarEventoPainel(
                "regua_config_nova_tarefa_ancorar_pdf_wizard",
                $"ferramenta_id={payloadCorrigido.FerramentaId} esteira_id={payloadCorrigido.EsteiraId}");
        }
        catch (Exception ex)
        {
            RegistrarEventoPainel(
                "erro_abrir_wizard_ancorar_pdf",
                $"ferramenta_id={payload.FerramentaId} erro={ex.GetType().Name}: {ex.Message}");
        }
    }

    private void AbrirModalGenericoPorFerramenta(NovaTarefaDropPayload payload)
    {
        if (BloquearUsoEsteirasSemCliente("abrir_modal_generico"))
            return;

        var esteira = Esteiras.FirstOrDefault(x => x.Id == payload.EsteiraId);
        var nomeEsteira = esteira?.NomeSalvo ?? $"Esteira {payload.EsteiraId}";
        var horarioLocal = payload.TempoAlvoUtc.ToLocalTime();

        ConfiguracaoTarefaId = null;
        ConfiguracaoTarefaEhAncorarPdf = false;
        ConfiguracaoTarefaTitulo = payload.NomeFerramenta;
        ConfiguracaoTarefaSubtitulo = $"Na {nomeEsteira}";
        ConfiguracaoTarefaDetalhes = $"Ferramenta: {payload.FerramentaId} | Horario sugerido: {horarioLocal:dd/MM/yyyy HH:mm}";
        ConfiguracaoTarefaAberta = true;
        RegistrarEventoPainel(
            "regua_config_nova_tarefa",
            $"ferramenta_id={payload.FerramentaId} ferramenta_nome={payload.NomeFerramenta} esteira_id={payload.EsteiraId}");
    }

    [RelayCommand]
    private void FecharConfigTarefa()
    {
        AncorarPdfConfiguracao.Reset();
        ConfiguracaoTarefaAberta = false;
        ConfiguracaoTarefaId = null;
        ConfiguracaoTarefaEhAncorarPdf = false;
        ConfiguracaoTarefaSomenteLeitura = false;
        ConfiguracaoTarefaSubtitulo = string.Empty;
        ConfiguracaoTarefaDetalhes = string.Empty;
        // Propagar visibilidade combinada em caso de fechar pelo modal de edição.
        OnPropertyChanged(nameof(ConfiguracaoAncorarPdfVisivel));
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ExecutarTarefaAgora(int tarefaId)
    {
        RegistrarInteracaoPainel();

        try
        {
            await _tarefaService.AlterarStatusAsync(tarefaId, TarefaStatus.Concluida, _userId);

            RegistrarEventoPainel("regua_tarefa_executada_manualmente", $"tarefa_id={tarefaId}");
            await CarregarTarefasClienteAsync();
        }
        catch (Exception ex)
        {
            RegistrarErroPainel("regua_executar_tarefa_falha", ex, $"tarefa_id={tarefaId}");
        }
    }

    public async Task ExecutarTarefaAutomaticaAsync(int tarefaId, DateTime tempoExecucaoUtc)
    {
        var chaveCiclo = CriarChaveCicloExecucaoAutomatica(tarefaId, tempoExecucaoUtc);
        if (!TentarMarcarInicioExecucaoAutomatica(chaveCiclo))
        {
            RegistrarEventoPainel("regua_tarefa_execucao_automatica_ignorada", $"tarefa_id={tarefaId} ciclo={chaveCiclo}");
            return;
        }

        await _execucaoAutomaticaSerial.WaitAsync();
        var sucesso = false;
        try
        {
            await _tarefaService.AlterarStatusAsync(tarefaId, TarefaStatus.Concluida, _userId);
            RegistrarEventoPainel("regua_tarefa_executada_automatica", $"tarefa_id={tarefaId} ciclo={chaveCiclo}");
            await CarregarTarefasClienteAsync();
            sucesso = true;
        }
        catch (Exception ex)
        {
            RegistrarErroPainel("regua_execucao_automatica_falha", ex, $"tarefa_id={tarefaId} ciclo={chaveCiclo}");
        }
        finally
        {
            _execucaoAutomaticaSerial.Release();
            FinalizarExecucaoAutomatica(chaveCiclo, sucesso);
        }
    }

    private void AlimentarEsteirasComTarefas(IReadOnlyList<Tarefa> tarefas, DateTime agoraUtc)
    {
        foreach (var esteira in Esteiras)
            esteira.Tarefas.Clear();

        var (viewportInicio, viewportFim) = ObterJanelaViewportEsteiras();

        foreach (var tarefa in tarefas)
        {
            var ocorrencias = GerarOcorrencias(tarefa, viewportInicio, viewportFim, agoraUtc);

            foreach (var ocorrencia in ocorrencias)
            {
                if (ocorrencia.EsteiraId.HasValue && _esteirasCanceladasNaSessao.Contains(ocorrencia.EsteiraId.Value))
                    continue;

                var esteiraAlvo = EncontrarOuCriarEsteira(ocorrencia.EsteiraId);
                esteiraAlvo.Tarefas.Add(ocorrencia);
            }
        }
    }

    private IEnumerable<TarefaReguaItem> GerarOcorrencias(Tarefa tarefa, DateTime viewportInicio, DateTime viewportFim, DateTime agoraUtc)
    {
        if (tarefa.Recorrencia == TarefaRecorrencia.Nenhuma)
        {
            var vencUtc = NormalizarUtc(tarefa.VencimentoUtc);
            var local = ConverterUtcParaLocal(vencUtc);
            var ehAtrasada = vencUtc < agoraUtc && tarefa.Status != TarefaStatus.Concluida;
            var agendadoPorNome = ResolverNomeUsuario(tarefa.CriadoPorUserId);
            var agendadoPorIniciais = ExtrairIniciaisPessoa(agendadoPorNome);

            yield return new TarefaReguaItem(
                tarefa.Id,
                tarefa.Titulo,
                vencUtc,
                local.ToString("HH:mm"),
                ObterStatusTextoRegua(tarefa.Status, ehAtrasada),
                ObterCorStatusRegua(tarefa.Status, ehAtrasada),
                ehAtrasada,
                false,
                tarefa.EsteiraId,
                ResolverNomeUsuario(tarefa.ResponsavelUserId),
                tarefa.Status == TarefaStatus.Bloqueada,
                tarefa.CriadoPorUserId,
                agendadoPorNome,
                agendadoPorIniciais,
                NormalizarUtc(tarefa.CriadoEmUtc));
            yield break;
        }

        // Tarefas recorrentes: gerar ocorrencias dentro do viewport
        var intervalo = tarefa.Recorrencia switch
        {
            TarefaRecorrencia.Horaria => TimeSpan.FromHours(1),
            TarefaRecorrencia.Diaria => TimeSpan.FromDays(1),
            TarefaRecorrencia.Semanal => TimeSpan.FromDays(7),
            TarefaRecorrencia.Mensal => TimeSpan.FromDays(30),
            _ => TimeSpan.Zero
        };

        if (intervalo == TimeSpan.Zero)
            yield break;

        var baseUtc = NormalizarUtc(tarefa.VencimentoUtc);
        var contador = 0;

        // Encontrar a primeira ocorrencia dentro ou antes do viewport
        var atual = baseUtc;
        if (atual > viewportFim)
            yield break;

        while (atual < viewportInicio && contador < MaxOcorrenciasRecorrentes)
        {
            atual = AvancarRecorrencia(atual, tarefa.Recorrencia, tarefa);
            contador++;
        }

        // Gerar ocorrencias dentro do viewport
        while (atual <= viewportFim && contador < MaxOcorrenciasRecorrentes)
        {
            var localOcorrencia = ConverterUtcParaLocal(atual);
            var ehAtrasadaOcorrencia = atual < agoraUtc && tarefa.Status != TarefaStatus.Concluida;
            var agendadoPorNome = ResolverNomeUsuario(tarefa.CriadoPorUserId);
            var agendadoPorIniciais = ExtrairIniciaisPessoa(agendadoPorNome);

            yield return new TarefaReguaItem(
                tarefa.Id,
                tarefa.Titulo,
                atual,
                localOcorrencia.ToString("HH:mm"),
                ObterStatusTextoRegua(tarefa.Status, ehAtrasadaOcorrencia),
                ObterCorStatusRegua(tarefa.Status, ehAtrasadaOcorrencia),
                ehAtrasadaOcorrencia,
                true,
                tarefa.EsteiraId,
                ResolverNomeUsuario(tarefa.ResponsavelUserId),
                tarefa.Status == TarefaStatus.Bloqueada,
                tarefa.CriadoPorUserId,
                agendadoPorNome,
                agendadoPorIniciais,
                NormalizarUtc(tarefa.CriadoEmUtc));

            atual = AvancarRecorrencia(atual, tarefa.Recorrencia, tarefa);
            contador++;
        }
    }

    private static DateTime AvancarRecorrencia(DateTime atual, TarefaRecorrencia recorrencia, Tarefa tarefa)
    {
        return recorrencia switch
        {
            TarefaRecorrencia.Horaria => atual.AddHours(1),
            TarefaRecorrencia.Diaria => atual.AddDays(1),
            TarefaRecorrencia.Semanal => atual.AddDays(7),
            TarefaRecorrencia.Mensal => AvancarMensal(atual, tarefa.DiaRecorrenciaMensal),
            _ => atual.AddDays(1)
        };
    }

    private static string ExtrairIniciaisPessoa(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return "?";

        var partes = nome
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length == 0)
            return "?";

        if (partes.Length == 1)
            return partes[0][0].ToString().ToUpperInvariant();

        return string.Concat(
            partes[0][0].ToString().ToUpperInvariant(),
            partes[1][0].ToString().ToUpperInvariant());
    }

    private static DateTime NormalizarUtcParaMinuto(DateTime valor)
    {
        var utc = NormalizarUtc(valor);
        return new DateTime(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc);
    }

    private static DateTime AvancarMensal(DateTime atual, int? diaPreferencial)
    {
        var local = atual.Kind == DateTimeKind.Utc ? atual.ToLocalTime() : atual;
        var proximoMes = new DateTime(local.Year, local.Month, 1, local.Hour, local.Minute, local.Second, local.Kind)
            .AddMonths(1);
        var dia = diaPreferencial ?? local.Day;
        var ultimoDia = DateTime.DaysInMonth(proximoMes.Year, proximoMes.Month);
        dia = Math.Min(Math.Max(1, dia), ultimoDia);

        var resultado = new DateTime(proximoMes.Year, proximoMes.Month, dia, local.Hour, local.Minute, local.Second, local.Kind);
        return atual.Kind == DateTimeKind.Utc ? resultado.ToUniversalTime() : resultado;
    }

    private EsteiraViewModel EncontrarOuCriarEsteira(int? esteiraId)
    {
        if (esteiraId.HasValue)
        {
            if (_esteirasCanceladasNaSessao.Contains(esteiraId.Value))
                return ObterOuCriarEsteiraFallback();

            var encontrada = Esteiras.FirstOrDefault(e => e.Id == esteiraId.Value);
            if (encontrada is not null)
                return encontrada;

            var centroBase = Esteiras.Count > 0 ? Esteiras[0].CentroTemporal : DateTime.UtcNow;
            var zoomBase = Esteiras.Count > 0 ? Esteiras[0].NivelZoom : 0.5;
            var novaEsteira = CriarEsteiraVm(esteiraId.Value, $"Esteira {esteiraId.Value}", Esteiras.Count + 1, zoomBase, centroBase);
            Esteiras.Add(novaEsteira);
            _proximoIdEsteira = Math.Max(_proximoIdEsteira, esteiraId.Value + 1);
            return novaEsteira;
        }

        return ObterOuCriarEsteiraFallback();
    }

    private EsteiraViewModel ObterOuCriarEsteiraFallback()
    {
        if (Esteiras.Count > 0)
            return Esteiras[0];

        var principal = CriarEsteiraVm(ObterProximoIdEsteira(), "Esteira 1", 1, 0.5, DateTime.UtcNow);
        Esteiras.Add(principal);
        return principal;
    }

    private void CriarEsteiraPadraoSeNecessario()
    {
        if (Esteiras.Count > 0)
            return;

        var primeira = CriarEsteiraVm(ObterProximoIdEsteira(), "Esteira 1", 1, 0.5, DateTime.UtcNow);
        Esteiras.Add(primeira);
    }

    private static EsteiraViewModel CriarEsteiraVm(int id, string nome, int ordem, double nivelZoom, DateTime centroTemporal)
    {
        return new EsteiraViewModel
        {
            Id = id,
            Nome = nome,
            NomeSalvo = nome,
            NomeEmEdicao = nome,
            NomePersonalizadoSalvo = false,
            Ordem = ordem,
            NivelZoom = Math.Clamp(nivelZoom, 0.0, 1.0),
            CentroTemporal = centroTemporal
        };
    }

    private static EsteiraViewModel CriarEsteiraVm(EstadoEsteira estado)
    {
        return new EsteiraViewModel
        {
            Id = estado.Id,
            Nome = estado.Nome,
            NomeSalvo = estado.NomeSalvo,
            NomeEmEdicao = estado.NomeEmEdicao,
            NomePersonalizadoSalvo = estado.NomePersonalizadoSalvo,
            Ordem = estado.Ordem,
            NivelZoom = Math.Clamp(estado.NivelZoom, 0.0, 1.0),
            CentroTemporal = estado.CentroTemporal
        };
    }

    private int ObterProximoIdEsteira()
    {
        while (_esteirasCanceladasNaSessao.Contains(_proximoIdEsteira))
            _proximoIdEsteira++;

        return _proximoIdEsteira++;
    }

    private void ReordenarEsteiras()
    {
        var ordem = 1;
        foreach (var esteira in Esteiras.OrderBy(e => e.Ordem).ToList())
        {
            esteira.Ordem = ordem;
            ordem++;
        }
    }

    private (DateTime Inicio, DateTime Fim) ObterJanelaViewportEsteiras()
    {
        if (Esteiras.Count == 0)
        {
            var centro = CentroTemporalRegua;
            return (centro.AddDays(-180), centro.AddDays(180));
        }

        var minimoCentro = Esteiras.Min(e => e.CentroTemporal);
        var maximoCentro = Esteiras.Max(e => e.CentroTemporal);
        return (minimoCentro.AddDays(-180), maximoCentro.AddDays(180));
    }

    private void SalvarEstadoEsteirasCliente(int clienteId)
    {
        if (clienteId <= 0)
            return;

        var estado = new EstadoEsteirasCliente(
            Esteiras
                .OrderBy(e => e.Ordem)
                .Select(e => new EstadoEsteira(
                    e.Id,
                    e.Nome,
                    e.NomeSalvo,
                    e.NomeEmEdicao,
                    e.NomePersonalizadoSalvo,
                    e.Ordem,
                    e.NivelZoom,
                    e.CentroTemporal))
                .ToList(),
            new HashSet<int>(_esteirasCanceladasNaSessao),
            _proximoIdEsteira,
            ModoAutomaticoRegua,
            CentroTemporalRegua);

        _estadoEsteirasPorCliente[clienteId] = estado;
    }

    private void RestaurarEstadoEsteirasCliente(int clienteId)
    {
        if (clienteId <= 0)
        {
            LimparEstadoEsteirasSemCliente();
            return;
        }

        if (!_estadoEsteirasPorCliente.TryGetValue(clienteId, out var estado))
        {
            PrepararEstadoEsteirasClienteSemLayout();
            return;
        }

        AplicarEstadoEsteirasCliente(estado);
    }

    private void AplicarEstadoEsteirasCliente(EstadoEsteirasCliente estado)
    {
        Esteiras.Clear();
        _esteirasCanceladasNaSessao.Clear();

        foreach (var esteira in estado.Esteiras.OrderBy(e => e.Ordem))
        {
            Esteiras.Add(CriarEsteiraVm(esteira));
        }

        _esteirasCanceladasNaSessao.UnionWith(estado.EsteirasCanceladas);

        if (Esteiras.Count == 0)
            CriarEsteiraPadraoSeNecessario();

        var maiorIdAtual = Esteiras.Count == 0 ? 0 : Esteiras.Max(e => e.Id);
        _proximoIdEsteira = Math.Max(Math.Max(1, estado.ProximoIdEsteira), maiorIdAtual + 1);

        ModoAutomaticoRegua = estado.ModoAutomaticoRegua;
        CentroTemporalRegua = estado.CentroTemporalReferencia;
        NivelZoomRegua = Esteiras.Count == 0 ? 0.5 : Esteiras[0].NivelZoom;

        ConfirmacaoRemoverEsteiraAberta = false;
        ConfirmacaoRemoverEsteiraId = null;
        ConfirmacaoRemoverEsteiraMensagem = string.Empty;

        BacklogExecucaoPendente = false;
        BacklogExecucaoResumo = string.Empty;
        BacklogModoSelecaoAtivo = false;
        BacklogItemsSelecionaveis.Clear();
        _tarefasBacklogPendentes.Clear();
        _backlogItensDetalhados.Clear();
        LimparControleExecucaoAutomatica();
    }

    private void PrepararEstadoEsteirasClienteSemLayout()
    {
        ResetarEstadoEsteirasBase(fecharDock: false);
        CriarEsteiraPadraoSeNecessario();
    }

    private void LimparEstadoEsteirasSemCliente()
    {
        ResetarEstadoEsteirasBase(fecharDock: true);
    }

    private void ResetarEstadoEsteirasBase(bool fecharDock)
    {
        _esteirasCanceladasNaSessao.Clear();
        Esteiras.Clear();
        _proximoIdEsteira = 1;
        ModoAutomaticoRegua = true;
        CentroTemporalRegua = DateTime.UtcNow;
        HoraAtualRegua = DateTime.UtcNow;
        NivelZoomRegua = 0.5;

        ConfirmacaoRemoverEsteiraAberta = false;
        ConfirmacaoRemoverEsteiraId = null;
        ConfirmacaoRemoverEsteiraMensagem = string.Empty;

        BacklogExecucaoPendente = false;
        BacklogExecucaoResumo = string.Empty;
        BacklogModoSelecaoAtivo = false;
        BacklogItemsSelecionaveis.Clear();
        _tarefasBacklogPendentes.Clear();
        _backlogItensDetalhados.Clear();
        LimparControleExecucaoAutomatica();
        if (fecharDock)
            PainelEsteirasDockAberto = false;
    }

    private sealed record EstadoEsteirasCliente(
        List<EstadoEsteira> Esteiras,
        HashSet<int> EsteirasCanceladas,
        int ProximoIdEsteira,
        bool ModoAutomaticoRegua,
        DateTime CentroTemporalReferencia);

    private sealed record EstadoEsteira(
        int Id,
        string Nome,
        string NomeSalvo,
        string NomeEmEdicao,
        bool NomePersonalizadoSalvo,
        int Ordem,
        double NivelZoom,
        DateTime CentroTemporal);

    private static string ObterStatusTextoRegua(TarefaStatus status, bool ehAtrasada)
    {
        if (ehAtrasada) return "ATRASADA";
        return ObterStatusTextoPadraoRegua(status);
    }

    private static string ObterCorStatusRegua(TarefaStatus status, bool ehAtrasada)
    {
        if (ehAtrasada) return CorVermelha;

        return ObterCorStatusPadrao(status);
    }

    private static string CriarChaveCicloExecucaoAutomatica(int tarefaId, DateTime tempoExecucaoUtc)
    {
        var tempoUtcNormalizado = NormalizarUtc(tempoExecucaoUtc);
        return $"{tarefaId}:{tempoUtcNormalizado.Ticks}";
    }

    private bool TentarMarcarInicioExecucaoAutomatica(string chaveCiclo)
    {
        lock (_execucaoAutomaticaLock)
        {
            if (_ciclosExecucaoAutomaticaConcluidos.Contains(chaveCiclo) ||
                _ciclosExecucaoAutomaticaEmExecucao.Contains(chaveCiclo))
            {
                return false;
            }

            _ciclosExecucaoAutomaticaEmExecucao.Add(chaveCiclo);
            return true;
        }
    }

    private void FinalizarExecucaoAutomatica(string chaveCiclo, bool sucesso)
    {
        lock (_execucaoAutomaticaLock)
        {
            _ciclosExecucaoAutomaticaEmExecucao.Remove(chaveCiclo);
            if (!sucesso)
                return;

            _ciclosExecucaoAutomaticaConcluidos.Add(chaveCiclo);
            _ordemCiclosExecucaoAutomaticaConcluidos.Enqueue(chaveCiclo);

            while (_ordemCiclosExecucaoAutomaticaConcluidos.Count > MaxHistoricoExecucaoAutomaticaConcluida)
            {
                var antigo = _ordemCiclosExecucaoAutomaticaConcluidos.Dequeue();
                _ciclosExecucaoAutomaticaConcluidos.Remove(antigo);
            }
        }
    }

    private void LimparControleExecucaoAutomatica()
    {
        lock (_execucaoAutomaticaLock)
        {
            _ciclosExecucaoAutomaticaEmExecucao.Clear();
            _ciclosExecucaoAutomaticaConcluidos.Clear();
            _ordemCiclosExecucaoAutomaticaConcluidos.Clear();
        }
    }
}

/// <summary>
/// C7: item de backlog pendente com flag de seleção para o modo "Escolher quais executar".
/// Usado em PainelViewModel.BacklogItemsSelecionaveis (ObservableCollection para binding Avalonia).
/// </summary>
public sealed class BacklogItemSelecionavelVm
{
    public long BacklogId { get; init; }
    public int TarefaId { get; init; }

    /// <summary>Título formatado para exibição: "Nome da Tarefa — DD/MM HH:mm".</summary>
    public string Titulo { get; init; } = string.Empty;

    public DateTime JanelaAlvoUtc { get; init; }
    public int AtrasoSegundos { get; init; }

    /// <summary>Flag de seleção — mutable para binding bidirecional do checkbox.</summary>
    public bool Selecionado { get; set; } = true;

    /// <summary>Resumo legível do atraso para exibição na UI.</summary>
    public string AtrasoTexto => AtrasoSegundos >= 3600
        ? $"{AtrasoSegundos / 3600}h {(AtrasoSegundos % 3600) / 60}min de atraso"
        : AtrasoSegundos >= 60
            ? $"{AtrasoSegundos / 60}min de atraso"
            : $"{AtrasoSegundos}s de atraso";
}
