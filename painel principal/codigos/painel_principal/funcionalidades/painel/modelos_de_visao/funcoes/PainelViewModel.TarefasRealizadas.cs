using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.ViewModels;

public sealed partial class PainelViewModel
{
    private const string DataHoraCompletaFormato = "dd/MM/yyyy HH:mm";
    private const string HoraFormato = "HH:mm";
    private const int HistoricoInicialExibido = 100;
    private const int HistoricoCarregarMaisIncremento = 50;

    private readonly Dictionary<int, string> _mapaNomesUsuarios = new();
    private List<HistoricoExecucaoItem> _historicoCache = [];
    private CancellationTokenSource? _cargaTarefasCts;
    private int _cargaTarefasVersao;
    private int _cargaHistoricoVersao;
    private int _cargaKpiVersao;

    [ObservableProperty] private bool _carregandoTarefasCliente;
    [ObservableProperty] private string _resumoTarefasCliente = "Selecione um cliente para carregar tarefas.";
    [ObservableProperty] private string? _mensagemTarefasCliente;
    [ObservableProperty] private string _ultimaAtualizacaoTarefas = "Aguardando carga de tarefas.";
    [ObservableProperty] private bool _painelKpisDockAberto = true;
    [ObservableProperty] private EscopoHistoricoExecucao _escopoHistoricoSelecionado = EscopoHistoricoExecucao.Global;
    [ObservableProperty] private string _resumoHistoricoExecucao = "Histórico global ativo.";
    [ObservableProperty] private bool _painelHistoricoExecucaoDockAberto = true;

    [ObservableProperty] private int _pendenciasFuturasKpi;
    [ObservableProperty] private int _pendenciasAtrasadasKpi;
    [ObservableProperty] private int _totalRealizadasKpi;
    [ObservableProperty] private int _agendadasHojeKpi;
    [ObservableProperty] private int _realizadasHojeKpi;
    [ObservableProperty] private int _pendentesHojeKpi;
    [ObservableProperty] private int _atrasadasHojeKpi;
    [ObservableProperty] private int _pendenciasProgramadasHojeKpi;
    [ObservableProperty] private int _pendenciasProgramadasAmanhaKpi;
    [ObservableProperty] private int _pendenciasProgramadasDepoisKpi;
    [ObservableProperty] private string _pendenciasProgramadasProximaExecucaoTexto = "Sem próxima execução";

    [ObservableProperty] private bool _timelineExpandida;
    [ObservableProperty] private string _textoBotaoExpandirTimeline = "Expandir";
    [ObservableProperty] private CategoriaTimelineEsteira _categoriaTimelineSelecionada = CategoriaTimelineEsteira.Futuras;

    [ObservableProperty] private bool _temAlertaGapCurto;
    [ObservableProperty] private string _alertaGapCurtoTexto = "";
    [ObservableProperty] private bool _temMaisHistorico;

    public bool AbaFuturasSelecionada => CategoriaTimelineSelecionada == CategoriaTimelineEsteira.Futuras;
    public bool AbaAtrasadasSelecionada => CategoriaTimelineSelecionada == CategoriaTimelineEsteira.Atrasadas;
    public bool AbaRealizadasSelecionada => CategoriaTimelineSelecionada == CategoriaTimelineEsteira.Realizadas;

    public ObservableCollection<TarefaTimelineItem> TimelinePendenciasFuturas { get; } = new();
    public ObservableCollection<TarefaTimelineItem> TimelinePendenciasAtrasadas { get; } = new();
    public ObservableCollection<TarefaTimelineItem> TimelineRealizadas { get; } = new();
    public ObservableCollection<TarefaTimelineItem> TimelineExibida { get; } = new();

    public ObservableCollection<HistoricoExecucaoItem> HistoricoExecucao { get; } = new();
    public string IconePainelKpisDock => PainelKpisDockAberto ? "›" : "‹";
    public string TooltipPainelKpisDock => PainelKpisDockAberto ? "Recolher indicadores" : "Abrir indicadores";
    public bool HistoricoGlobalSelecionado => EscopoHistoricoSelecionado == EscopoHistoricoExecucao.Global;
    public bool HistoricoClienteSelecionado => EscopoHistoricoSelecionado == EscopoHistoricoExecucao.Cliente;
    public string IconePainelHistoricoExecucaoDock => PainelHistoricoExecucaoDockAberto ? "˄" : "˅";
    public string TooltipPainelHistoricoExecucaoDock => PainelHistoricoExecucaoDockAberto ? "Recolher histórico" : "Abrir histórico";
    public string ExecucoesHojeEscopoTexto => IsSupremo
        ? "Toda a hierarquia visível"
        : IsAdmin
            ? "Equipe e clientes visíveis"
            : "Suas esteiras visíveis";
    public string ExecucoesHojeMetaTexto => AgendadasHojeKpi <= 0
        ? "Nenhuma agendada hoje"
        : $"{RealizadasHojeKpi} de {AgendadasHojeKpi} agendadas";
    public string ExecucoesHojeStatusTexto => AgendadasHojeKpi <= 0
        ? "Aguardando novas execuções"
        : $"{PendentesHojeKpi} pendentes • {AtrasadasHojeKpi} atrasadas";
    public string ExecucoesHojePercentualTexto => AgendadasHojeKpi <= 0
        ? "Sem agenda"
        : $"{Math.Round((RealizadasHojeKpi / (double)AgendadasHojeKpi) * 100, MidpointRounding.AwayFromZero):0}% do dia";
    public double ExecucoesHojePonteiroAngulo => AgendadasHojeKpi <= 0
        ? -78d
        : Math.Round(-78d + (RealizadasHojeKpi / (double)AgendadasHojeKpi) * 156d, 2);
    public GridLength ExecucoesHojeRealizadasPeso => CriarPesoSegmento(RealizadasHojeKpi);
    public GridLength ExecucoesHojePendentesPeso => CriarPesoSegmento(PendentesHojeKpi);
    public GridLength ExecucoesHojeAtrasadasPeso => CriarPesoSegmento(AtrasadasHojeKpi);
    public string ExecucoesHojeRealizadasLegenda => $"Realizadas {RealizadasHojeKpi}";
    public string ExecucoesHojePendentesLegenda => $"Pendentes {PendentesHojeKpi}";
    public string ExecucoesHojeAtrasadasLegenda => $"Atrasadas {AtrasadasHojeKpi}";
    public string PendenciasProgramadasDistribuicaoTexto => PendenciasFuturasKpi <= 0
        ? "Nada previsto"
        : $"{PendenciasProgramadasHojeKpi} hoje • {PendenciasProgramadasAmanhaKpi} amanhã";
    public string PendenciasProgramadasResumoTexto => PendenciasFuturasKpi <= 0
        ? "Sem tarefas abertas no cliente atual"
        : $"{PendenciasProgramadasDepoisKpi} depois • {PendenciasProgramadasProximaExecucaoTexto}";
    public double PendenciasProgramadasHojeBarraAltura => CriarAlturaMiniBarra(PendenciasProgramadasHojeKpi);
    public double PendenciasProgramadasAmanhaBarraAltura => CriarAlturaMiniBarra(PendenciasProgramadasAmanhaKpi);
    public double PendenciasProgramadasDepoisBarraAltura => CriarAlturaMiniBarra(PendenciasProgramadasDepoisKpi);
    public double OperacaoHojeConcluidasBarraLargura => CriarLarguraMiniBarraOperacional(RealizadasHojeKpi);
    public double OperacaoHojePendentesBarraLargura => CriarLarguraMiniBarraOperacional(PendentesHojeKpi);
    public double OperacaoHojeAtrasadasBarraLargura => CriarLarguraMiniBarraOperacional(AtrasadasHojeKpi);
    public double OperacaoHojeAmanhaBarraLargura => CriarLarguraMiniBarraOperacional(PendenciasProgramadasAmanhaKpi);
    public double OperacaoHojeDepoisBarraLargura => CriarLarguraMiniBarraOperacional(PendenciasProgramadasDepoisKpi);

    // KPI Card 2 — Agendadas para hoje
    public string AgendadasHojeSubtexto => AgendadasHojeKpi <= 0
        ? "Nenhuma programada para hoje"
        : RealizadasHojeKpi == AgendadasHojeKpi
            ? "Todas concluídas hoje"
            : $"{RealizadasHojeKpi} concluída{(RealizadasHojeKpi == 1 ? "" : "s")} de {AgendadasHojeKpi} agendadas";

    // KPI Card 3 — Total agendadas (esteira futura)
    public string TotalAgendadasSubtexto => PendenciasFuturasKpi <= 0
        ? "Sem execuções pendentes"
        : $"Próxima: {PendenciasProgramadasProximaExecucaoTexto}";

    // KPI Card 4 — Erros
    public bool TemErrosCriticos => ErrosCriticos > 0;
    public bool NaoTemErrosCriticos => ErrosCriticos <= 0;
    public string ErrosSubtexto => ErrosCriticos <= 0
        ? "Nenhum erro registrado"
        : ErrosCriticos == 1
            ? "1 tarefa falhou — clique para ver"
            : $"{ErrosCriticos} tarefas falharam — clique para ver";

    partial void OnCategoriaTimelineSelecionadaChanged(CategoriaTimelineEsteira value)
    {
        OnPropertyChanged(nameof(AbaFuturasSelecionada));
        OnPropertyChanged(nameof(AbaAtrasadasSelecionada));
        OnPropertyChanged(nameof(AbaRealizadasSelecionada));
        AtualizarTimelineExibida();
    }

    partial void OnTimelineExpandidaChanged(bool value)
    {
        TextoBotaoExpandirTimeline = value ? "Recolher" : "Expandir";
        AtualizarTimelineExibida();
    }

    partial void OnPainelKpisDockAbertoChanged(bool value)
    {
        OnPropertyChanged(nameof(IconePainelKpisDock));
        OnPropertyChanged(nameof(TooltipPainelKpisDock));
    }

    partial void OnPainelHistoricoExecucaoDockAbertoChanged(bool value)
    {
        OnPropertyChanged(nameof(IconePainelHistoricoExecucaoDock));
        OnPropertyChanged(nameof(TooltipPainelHistoricoExecucaoDock));
    }

    partial void OnAgendadasHojeKpiChanged(int value) => NotificarEstadoExecucoesHoje();
    partial void OnRealizadasHojeKpiChanged(int value) => NotificarEstadoExecucoesHoje();
    partial void OnPendentesHojeKpiChanged(int value) => NotificarEstadoExecucoesHoje();
    partial void OnAtrasadasHojeKpiChanged(int value) => NotificarEstadoExecucoesHoje();
    partial void OnPendenciasFuturasKpiChanged(int value) => NotificarEstadoPendenciasProgramadas();
    partial void OnErrosCriticosChanged(int value) => NotificarEstadoErros();
    partial void OnPendenciasProgramadasHojeKpiChanged(int value) => NotificarEstadoPendenciasProgramadas();
    partial void OnPendenciasProgramadasAmanhaKpiChanged(int value) => NotificarEstadoPendenciasProgramadas();
    partial void OnPendenciasProgramadasDepoisKpiChanged(int value) => NotificarEstadoPendenciasProgramadas();
    partial void OnPendenciasProgramadasProximaExecucaoTextoChanged(string value) => NotificarEstadoPendenciasProgramadas();

    partial void OnEscopoHistoricoSelecionadoChanged(EscopoHistoricoExecucao value)
    {
        OnPropertyChanged(nameof(HistoricoGlobalSelecionado));
        OnPropertyChanged(nameof(HistoricoClienteSelecionado));
        DispararComSeguranca(CarregarHistoricoExecucaoAsync(), "historico_escopo_carga_falha");
    }

    private void InicializarTarefasRealizadas()
    {
        LimparEstadoTarefas("Selecione um cliente para carregar tarefas.");
        DispararComSeguranca(CarregarHistoricoExecucaoAsync(), "historico_carga_inicial_falha");
        DispararComSeguranca(CarregarKpiExecucoesHojeGlobalAsync(), "kpi_execucoes_hoje_carga_inicial_falha");
    }

    private CancellationToken IniciarNovaCargaTarefas(out int versao)
    {
        versao = Interlocked.Increment(ref _cargaTarefasVersao);
        _cargaTarefasCts?.Cancel();
        _cargaTarefasCts?.Dispose();
        _cargaTarefasCts = new CancellationTokenSource();
        return _cargaTarefasCts.Token;
    }

    private void CancelarCargaTarefasAtiva()
    {
        Interlocked.Increment(ref _cargaTarefasVersao);
        _cargaTarefasCts?.Cancel();
        _cargaTarefasCts?.Dispose();
        _cargaTarefasCts = null;
    }

    private bool CargaTarefasAindaAtual(CancellationToken token, int versao, int? clienteIdEsperado)
    {
        if (token.IsCancellationRequested || versao != Volatile.Read(ref _cargaTarefasVersao))
            return false;

        return !clienteIdEsperado.HasValue || ClienteContextoId == clienteIdEsperado.Value;
    }

    private bool CargaHistoricoAindaAtual(
        int versao,
        EscopoHistoricoExecucao escopoEsperado,
        int? clienteIdEsperado,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || versao != Volatile.Read(ref _cargaHistoricoVersao))
            return false;

        if (EscopoHistoricoSelecionado != escopoEsperado)
            return false;

        return escopoEsperado != EscopoHistoricoExecucao.Cliente || ClienteContextoId == clienteIdEsperado;
    }

    private bool CargaKpiAindaAtual(int versao, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested && versao == Volatile.Read(ref _cargaKpiVersao);
    }

    private async Task CarregarTarefasClienteAsync()
    {
        if (!ClienteContextoId.HasValue)
        {
            CancelarCargaTarefasAtiva();
            CarregandoTarefasCliente = false;
            LimparEstadoTarefas("Selecione um cliente para carregar tarefas.");
            await CarregarHistoricoExecucaoAsync();
            await CarregarKpiExecucoesHojeGlobalAsync();
            return;
        }

        var clienteIdCarga = ClienteContextoId.Value;
        var token = IniciarNovaCargaTarefas(out var versaoCarga);

        CarregandoTarefasCliente = true;
        MensagemTarefasCliente = null;

        try
        {
            CarregarDiretorioUsuarios();

            var filtro = new TarefaFiltroConsulta
            {
                ClienteId = clienteIdCarga,
                SomenteMinhasTarefas = !IsAdmin
            };

            var tarefas = await Task.Run(() => _tarefaService.Buscar(filtro, _userId), token);
            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            var agoraUtc = DateTime.UtcNow;

            var futurasBase = tarefas
                .Where(t => (t.Status == TarefaStatus.Agendada || t.Status == TarefaStatus.EmAndamento) && NormalizarUtc(t.VencimentoUtc) >= agoraUtc)
                .OrderBy(t => NormalizarUtc(t.VencimentoUtc))
                .ThenBy(t => t.Id)
                .ToList();

            var atrasadasBase = tarefas
                .Where(t => (t.Status == TarefaStatus.Agendada || t.Status == TarefaStatus.EmAndamento) && NormalizarUtc(t.VencimentoUtc) < agoraUtc)
                .OrderBy(t => NormalizarUtc(t.VencimentoUtc))
                .ThenBy(t => t.Id)
                .ToList();

            var realizadasBase = tarefas
                .Where(t => t.Status == TarefaStatus.Concluida)
                .OrderByDescending(t => NormalizarUtc(t.ConcluidaEmUtc ?? t.AtualizadoEmUtc))
                .ThenByDescending(t => t.Id)
                .ToList();

            var errosBase = tarefas
                .Where(t => t.Status == TarefaStatus.Bloqueada)
                .OrderByDescending(t => NormalizarUtc(t.AtualizadoEmUtc))
                .ThenByDescending(t => t.Id)
                .ToList();

            var futurasTimeline = MapearTimeline(futurasBase, agoraUtc, CategoriaTimelineEsteira.Futuras);
            var atrasadasTimeline = MapearTimeline(atrasadasBase, agoraUtc, CategoriaTimelineEsteira.Atrasadas);
            var realizadasTimeline = MapearTimeline(realizadasBase, agoraUtc, CategoriaTimelineEsteira.Realizadas);

            AtualizarColecao(TimelinePendenciasFuturas, futurasTimeline);
            AtualizarColecao(TimelinePendenciasAtrasadas, atrasadasTimeline);
            AtualizarColecao(TimelineRealizadas, realizadasTimeline);

            PendenciasFuturasKpi = futurasTimeline.Count;
            PendenciasAtrasadasKpi = atrasadasTimeline.Count;
            TotalRealizadasKpi = realizadasTimeline.Count;
            ErrosCriticos = errosBase.Count;
            AtualizarKpiPendenciasProgramadas(futurasBase);

            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            var total = PendenciasFuturasKpi + PendenciasAtrasadasKpi + TotalRealizadasKpi + ErrosCriticos;
            ResumoTarefasCliente = total == 0
                ? "Nenhuma tarefa encontrada para o cliente selecionado."
                : $"{total} tarefa(s): {PendenciasFuturasKpi} futuras, {PendenciasAtrasadasKpi} atrasadas, {TotalRealizadasKpi} realizadas, {ErrosCriticos} com erro.";
            UltimaAtualizacaoTarefas = $"Atualizado em {DateTime.Now:dd/MM/yyyy HH:mm}.";

            await CarregarHistoricoExecucaoAsync(
                tarefas,
                agoraUtc,
                clienteIdCarga,
                EscopoHistoricoSelecionado,
                token);
            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            AtualizarAlertaGap(futurasTimeline, atrasadasTimeline);
            await CarregarKpiExecucoesHojeGlobalAsync(token);
            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            AlimentarEsteirasComTarefas(tarefas, agoraUtc);

            AtualizarTimelineExibida();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            MensagemTarefasCliente = "Falha ao carregar tarefas do cliente.";
            ResumoTarefasCliente = "Tarefas indisponíveis no momento.";
            UltimaAtualizacaoTarefas = "Falha na atualização.";
            LimparColecoesEsteira();
            await CarregarKpiExecucoesHojeGlobalAsync(token);
            if (!CargaTarefasAindaAtual(token, versaoCarga, clienteIdCarga))
                return;

            RegistrarErroPainel("tarefas_busca_falha", ex, $"cliente_id={ClienteContextoId?.ToString() ?? "0"}");
        }
        finally
        {
            if (versaoCarga == Volatile.Read(ref _cargaTarefasVersao))
                CarregandoTarefasCliente = false;
        }
    }

    [RelayCommand]
    private async Task AtualizarTarefasCliente()
    {
        RegistrarInteracaoPainel();
        var corr = GerarCorrelationId();
        RegistrarEventoPainel("tarefa_buscar_inicio", $"corr={corr} cliente_id={ClienteContextoId?.ToString() ?? "0"}");

        await CarregarTarefasClienteAsync();

        var total = PendenciasFuturasKpi + PendenciasAtrasadasKpi + TotalRealizadasKpi + ErrosCriticos;
        RegistrarEventoPainel("tarefa_buscar_sucesso", $"corr={corr} cliente_id={ClienteContextoId?.ToString() ?? "0"} total={total}");
    }

    [RelayCommand]
    private void SelecionarCategoriaTimeline(string categoria)
    {
        RegistrarInteracaoPainel();

        if (!Enum.TryParse<CategoriaTimelineEsteira>(categoria, true, out var categoriaSelecionada))
            return;

        CategoriaTimelineSelecionada = categoriaSelecionada;
    }

    [RelayCommand]
    private void AlternarExpandirTimeline()
    {
        RegistrarInteracaoPainel();
        TimelineExpandida = !TimelineExpandida;
    }

    [RelayCommand]
    private void AlternarPainelKpisDock()
    {
        RegistrarInteracaoPainel();
        PainelKpisDockAberto = !PainelKpisDockAberto;
        RegistrarEventoPainel("kpis_dock_toggle", $"aberto={PainelKpisDockAberto}");
    }

    [RelayCommand]
    private void VerTodasExecucoes()
    {
        RegistrarInteracaoPainel();
        TimelineExpandida = true;
        CategoriaTimelineSelecionada = CategoriaTimelineEsteira.Realizadas;
    }

    [RelayCommand]
    private void AlternarPainelHistoricoExecucaoDock()
    {
        RegistrarInteracaoPainel();
        PainelHistoricoExecucaoDockAberto = !PainelHistoricoExecucaoDockAberto;
        RegistrarEventoPainel("historico_dock_toggle", $"aberto={PainelHistoricoExecucaoDockAberto}");
    }

    [RelayCommand]
    private void SelecionarEscopoHistorico(string escopo)
    {
        RegistrarInteracaoPainel();
        if (!Enum.TryParse<EscopoHistoricoExecucao>(escopo, true, out var escopoSelecionado))
            return;

        EscopoHistoricoSelecionado = escopoSelecionado;
    }

    [RelayCommand]
    private async Task NavegarParaHistoricoItem(HistoricoExecucaoItem? item)
    {
        if (item is null)
            return;

        RegistrarInteracaoPainel();
        if (!ClienteContextoId.HasValue || ClienteContextoId.Value != item.ClienteId)
        {
            await AtivarContextoClientePorIdAsync(item.ClienteId, "historico");
        }

        NavegarParaTarefa(item.Id);
    }

    private void LimparEstadoEsteiraParaNovoCliente()
    {
        LimparColecoesEsteira();

        MensagemTarefasCliente = null;
        ResumoTarefasCliente = "Carregando tarefas do cliente selecionado...";
        UltimaAtualizacaoTarefas = "Atualizando...";
        TimelineExpandida = false;
        CategoriaTimelineSelecionada = CategoriaTimelineEsteira.Futuras;
        LimparEstadoEsteirasSemCliente();
    }

    private void LimparEstadoTarefas(string resumo)
    {
        LimparColecoesEsteira();

        ResumoTarefasCliente = resumo;
        MensagemTarefasCliente = null;
        UltimaAtualizacaoTarefas = "Aguardando carga de tarefas.";
        TimelineExpandida = false;
        CategoriaTimelineSelecionada = CategoriaTimelineEsteira.Futuras;
    }

    private void LimparColecoesEsteira()
    {
        TimelinePendenciasFuturas.Clear();
        TimelinePendenciasAtrasadas.Clear();
        TimelineRealizadas.Clear();
        TimelineExibida.Clear();
        HistoricoExecucao.Clear();
        _historicoCache = [];
        TemMaisHistorico = false;

        PendenciasFuturasKpi = 0;
        PendenciasAtrasadasKpi = 0;
        TotalRealizadasKpi = 0;
        AgendadasHojeKpi = 0;
        RealizadasHojeKpi = 0;
        PendentesHojeKpi = 0;
        AtrasadasHojeKpi = 0;
        PendenciasProgramadasHojeKpi = 0;
        PendenciasProgramadasAmanhaKpi = 0;
        PendenciasProgramadasDepoisKpi = 0;
        PendenciasProgramadasProximaExecucaoTexto = "Sem próxima execução";
        NotasHoje = 0;
        ErrosCriticos = 0;

        TemAlertaGapCurto = false;
        AlertaGapCurtoTexto = string.Empty;
    }

    private void AtualizarTimelineExibida()
    {
        IReadOnlyList<TarefaTimelineItem> origem = CategoriaTimelineSelecionada switch
        {
            CategoriaTimelineEsteira.Futuras => TimelinePendenciasFuturas,
            CategoriaTimelineEsteira.Atrasadas => TimelinePendenciasAtrasadas,
            CategoriaTimelineEsteira.Realizadas => TimelineRealizadas,
            _ => []
        };

        var limite = TimelineExpandida ? 20 : 4;
        AtualizarColecao(TimelineExibida, origem.Take(limite).ToList());
    }

    private async Task CarregarHistoricoExecucaoAsync(
        IReadOnlyList<Tarefa>? tarefasContexto = null,
        DateTime? agoraUtc = null,
        int? clienteContextoEsperado = null,
        EscopoHistoricoExecucao? escopoEsperado = null,
        CancellationToken cancellationToken = default)
    {
        var instante = agoraUtc ?? DateTime.UtcNow;
        var versaoCarga = Interlocked.Increment(ref _cargaHistoricoVersao);
        var escopoCarga = escopoEsperado ?? EscopoHistoricoSelecionado;
        var clienteIdCarga = clienteContextoEsperado ?? ClienteContextoId;

        if (escopoCarga == EscopoHistoricoExecucao.Global)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tarefas = await Task.Run(() => _tarefaService.BuscarHistoricoGlobal(_userId, 200), cancellationToken);
            if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
                return;

            sw.Stop();
            RegistrarMetricaPainel("historico_global_ms", sw.ElapsedMilliseconds, "ms", "escopo=global");
            var clientes = await Task.Run(() => _clienteService.ListarTodosPorUsuario(_userId), cancellationToken);
            if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
                return;

            var mapaClientes = clientes.ToDictionary(c => c.Id, c => c.Nome);

            _historicoCache = tarefas
                .OrderByDescending(ObterDataHistorico)
                .ThenByDescending(t => t.Id)
                .Select(t =>
                {
                    var clienteNome = mapaClientes.TryGetValue(t.ClienteId, out var nome) ? nome : $"Cliente {t.ClienteId}";
                    return MapearHistorico(t, t.ClienteId, clienteNome, instante);
                })
                .ToList();

            var exibirInicial = _historicoCache.Take(HistoricoInicialExibido).ToList();

            if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
                return;

            ResumoHistoricoExecucao = _historicoCache.Count == 0
                ? "Histórico global sem execuções recentes."
                : $"Histórico global ativo ({_historicoCache.Count} item(ns)).";
            AtualizarColecao(HistoricoExecucao, exibirInicial);
            TemMaisHistorico = _historicoCache.Count > HistoricoInicialExibido;
            return;
        }

        if (!clienteIdCarga.HasValue)
        {
            if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
                return;

            ResumoHistoricoExecucao = "Selecione um cliente para ver histórico por cliente.";
            HistoricoExecucao.Clear();
            _historicoCache = [];
            TemMaisHistorico = false;
            return;
        }

        var tarefasCliente = tarefasContexto;
        if (tarefasCliente is null)
        {
            var filtro = new TarefaFiltroConsulta
            {
                ClienteId = clienteIdCarga.Value,
                SomenteMinhasTarefas = !IsAdmin
            };

            tarefasCliente = await Task.Run(() => _tarefaService.Buscar(filtro, _userId), cancellationToken);
            if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
                return;
        }

        var clienteNomeContexto = string.IsNullOrWhiteSpace(ClienteContextoNome) ? "Cliente" : ClienteContextoNome;
        _historicoCache = tarefasCliente
            .OrderByDescending(ObterDataHistorico)
            .ThenByDescending(t => t.Id)
            .Select(t => MapearHistorico(t, clienteIdCarga.Value, clienteNomeContexto, instante))
            .ToList();

        var exibirInicialCliente = _historicoCache.Take(HistoricoInicialExibido).ToList();

        if (!CargaHistoricoAindaAtual(versaoCarga, escopoCarga, clienteIdCarga, cancellationToken))
            return;

        ResumoHistoricoExecucao = _historicoCache.Count == 0
            ? "Histórico por cliente sem execuções recentes."
            : $"Histórico do cliente ativo ({_historicoCache.Count} item(ns)).";
        AtualizarColecao(HistoricoExecucao, exibirInicialCliente);
        TemMaisHistorico = _historicoCache.Count > HistoricoInicialExibido;
    }

    [RelayCommand]
    private void CarregarMaisHistorico()
    {
        RegistrarInteracaoPainel();
        var exibidos = HistoricoExecucao.Count;
        if (exibidos >= _historicoCache.Count)
        {
            TemMaisHistorico = false;
            return;
        }

        var proximos = _historicoCache
            .Skip(exibidos)
            .Take(HistoricoCarregarMaisIncremento)
            .ToList();
        foreach (var item in proximos)
            HistoricoExecucao.Add(item);

        TemMaisHistorico = HistoricoExecucao.Count < _historicoCache.Count;
    }

    private async Task CarregarKpiExecucoesHojeGlobalAsync(CancellationToken cancellationToken = default)
    {
        var versaoCarga = Interlocked.Increment(ref _cargaKpiVersao);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tarefasVisiveis = await BuscarTarefasVisiveisNoEscopoAsync(cancellationToken);
            if (!CargaKpiAindaAtual(versaoCarga, cancellationToken))
                return;

            sw.Stop();
            RegistrarMetricaPainel("kpi_tarefas_ms", sw.ElapsedMilliseconds, "ms", "escopo=global");
            AtualizarKpiTarefasHoje(tarefasVisiveis, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (!CargaKpiAindaAtual(versaoCarga, cancellationToken))
                return;

            AgendadasHojeKpi = 0;
            RealizadasHojeKpi = 0;
            PendentesHojeKpi = 0;
            AtrasadasHojeKpi = 0;
            NotasHoje = 0;
            RegistrarErroPainel("kpi_execucoes_hoje_carga_falha", ex, $"user_id={_userId}");
        }
    }

    // Consolida o escopo visível do usuário para que o KPI diário reflita as esteiras acessíveis.
    private async Task<IReadOnlyList<Tarefa>> BuscarTarefasVisiveisNoEscopoAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var clientesVisiveis = _clienteService.ListarTodosPorUsuario(_userId);
            if (clientesVisiveis.Count == 0)
                return (IReadOnlyList<Tarefa>)Array.Empty<Tarefa>();

            var clienteIds = clientesVisiveis
                .Where(c => c.Id > 0)
                .Select(c => c.Id)
                .Distinct()
                .ToList();

            if (clienteIds.Count == 0)
                return (IReadOnlyList<Tarefa>)Array.Empty<Tarefa>();

            cancellationToken.ThrowIfCancellationRequested();
            var tarefas = _tarefaService.BuscarPorClienteIds(
                clienteIds,
                termo: null,
                status: null,
                vencimentoInicioUtc: null,
                vencimentoFimUtc: null,
                somenteMinhasTarefas: !IsAdmin,
                solicitanteUserId: _userId);

            var tarefasProcessadas = new HashSet<int>();
            var tarefasVisiveis = tarefas
                .Where(t => t.Ativa && tarefasProcessadas.Add(t.Id))
                .ToList();

            return (IReadOnlyList<Tarefa>)tarefasVisiveis;
        }, cancellationToken);
    }

    private void AtualizarKpiTarefasHoje(IReadOnlyList<Tarefa> tarefas, DateTime agoraUtc)
    {
        var hojeLocal = DateTime.Now.Date;
        var tarefasHoje = tarefas
            .Where(t => t.Ativa && ConverterUtcParaLocal(NormalizarUtc(t.VencimentoUtc)).Date == hojeLocal)
            .ToList();

        var realizadasHoje = tarefasHoje.Count(t => t.Status == TarefaStatus.Concluida);
        var atrasadasHoje = tarefasHoje.Count(t =>
            t.Status != TarefaStatus.Concluida &&
            NormalizarUtc(t.VencimentoUtc) < agoraUtc);
        var pendentesHoje = Math.Max(0, tarefasHoje.Count - realizadasHoje - atrasadasHoje);

        AgendadasHojeKpi = tarefasHoje.Count;
        RealizadasHojeKpi = realizadasHoje;
        PendentesHojeKpi = pendentesHoje;
        AtrasadasHojeKpi = atrasadasHoje;
        NotasHoje = realizadasHoje;
    }

    private void AtualizarKpiPendenciasProgramadas(IReadOnlyList<Tarefa> futurasBase)
    {
        var hojeLocal = DateTime.Now.Date;
        var amanhaLocal = hojeLocal.AddDays(1);

        PendenciasProgramadasHojeKpi = futurasBase.Count(t => ConverterUtcParaLocal(NormalizarUtc(t.VencimentoUtc)).Date == hojeLocal);
        PendenciasProgramadasAmanhaKpi = futurasBase.Count(t => ConverterUtcParaLocal(NormalizarUtc(t.VencimentoUtc)).Date == amanhaLocal);
        PendenciasProgramadasDepoisKpi = Math.Max(0, futurasBase.Count - PendenciasProgramadasHojeKpi - PendenciasProgramadasAmanhaKpi);

        var proxima = futurasBase
            .OrderBy(t => NormalizarUtc(t.VencimentoUtc))
            .FirstOrDefault();

        PendenciasProgramadasProximaExecucaoTexto = proxima is null
            ? "Sem próxima execução"
            : FormatarProximaExecucao(proxima);
    }

    private void AtualizarAlertaGap(IReadOnlyList<TarefaTimelineItem> futuras, IReadOnlyList<TarefaTimelineItem> atrasadas)
    {
        var itemGapCurto = futuras.Concat(atrasadas)
            .FirstOrDefault(t => t.GapMinutosAteProxima.HasValue && t.GapMinutosAteProxima.Value < 30);

        if (itemGapCurto is null)
        {
            TemAlertaGapCurto = false;
            AlertaGapCurtoTexto = string.Empty;
            return;
        }

        TemAlertaGapCurto = true;
        AlertaGapCurtoTexto =
            $"Atenção: intervalo curto ({itemGapCurto.GapMinutosAteProxima} min) após '{itemGapCurto.Titulo}'.";
    }

    private List<TarefaTimelineItem> MapearTimeline(IReadOnlyList<Tarefa> tarefas, DateTime agoraUtc, CategoriaTimelineEsteira categoria)
    {
        var resultado = new List<TarefaTimelineItem>(tarefas.Count);

        for (var i = 0; i < tarefas.Count; i++)
        {
            var tarefa = tarefas[i];
            var proxima = i < tarefas.Count - 1 ? tarefas[i + 1] : null;
            var vencimentoUtc = NormalizarUtc(tarefa.VencimentoUtc);
            var local = ConverterUtcParaLocal(vencimentoUtc);
            var gapMinutos = proxima is null
                ? (int?)null
                : (int)Math.Round((NormalizarUtc(proxima.VencimentoUtc) - vencimentoUtc).TotalMinutes, MidpointRounding.AwayFromZero);

            var cancelada = categoria == CategoriaTimelineEsteira.Atrasadas && (agoraUtc - vencimentoUtc) > TimeSpan.FromHours(2);
            var statusTexto = cancelada ? "CANCELADO" : ObterStatusTimeline(tarefa.Status, categoria);
            var corStatus = cancelada ? CorCancelado : ObterCorStatusTimeline(tarefa.Status, categoria);

            resultado.Add(new TarefaTimelineItem(
                tarefa.Id,
                tarefa.Titulo,
                ResolverNomeUsuario(tarefa.ResponsavelUserId),
                local.ToString(HoraFormato),
                statusTexto,
                corStatus,
                tarefa.Status == TarefaStatus.Bloqueada,
                cancelada,
                gapMinutos,
                gapMinutos.HasValue,
                gapMinutos.HasValue ? $"{gapMinutos}m" : string.Empty,
                proxima is not null));
        }

        return resultado;
    }

    private HistoricoExecucaoItem MapearHistorico(Tarefa tarefa, int clienteId, string clienteNome, DateTime agoraUtc)
    {
        var dataEvento = ObterDataHistorico(tarefa);
        var dataEventoLocal = ConverterUtcParaLocal(dataEvento);
        var vencimentoUtc = NormalizarUtc(tarefa.VencimentoUtc);

        string status;
        string cor;

        if (tarefa.Status == TarefaStatus.Concluida)
        {
            status = "SUCESSO";
            cor = CorVerde;
        }
        else if (tarefa.Status == TarefaStatus.Bloqueada)
        {
            status = "ERRO";
            cor = CorVermelha;
        }
        else if (vencimentoUtc < agoraUtc)
        {
            status = "CANCELADO";
            cor = CorCancelado;
        }
        else
        {
            status = "PENDENTE";
            cor = CorAmarela;
        }

        return new HistoricoExecucaoItem(
            tarefa.Id,
            clienteId,
            clienteNome,
            tarefa.Titulo,
            ResolverNomeUsuario(tarefa.ResponsavelUserId),
            status,
            cor,
            dataEventoLocal.ToString(DataHoraCompletaFormato));
    }

    private static DateTime ObterDataHistorico(Tarefa tarefa)
    {
        if (tarefa.Status == TarefaStatus.Concluida)
            return NormalizarUtc(tarefa.ConcluidaEmUtc ?? tarefa.AtualizadoEmUtc);

        return NormalizarUtc(tarefa.AtualizadoEmUtc);
    }

    private static string ObterStatusTimeline(TarefaStatus status, CategoriaTimelineEsteira categoria)
    {
        if (categoria == CategoriaTimelineEsteira.Realizadas)
            return "SUCESSO";

        return ObterStatusTextoPadrao(status);
    }

    private static string ObterCorStatusTimeline(TarefaStatus status, CategoriaTimelineEsteira categoria)
    {
        if (categoria == CategoriaTimelineEsteira.Realizadas)
            return CorVerde;

        return ObterCorStatusPadrao(status);
    }

    private static DateTime NormalizarUtc(DateTime valor)
    {
        return valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
        };
    }

    private GridLength CriarPesoSegmento(int quantidade)
    {
        if (AgendadasHojeKpi <= 0 || quantidade <= 0)
            return new GridLength(0, GridUnitType.Star);

        return new GridLength(quantidade, GridUnitType.Star);
    }

    private double CriarAlturaMiniBarra(int quantidade)
    {
        var maximo = Math.Max(1, Math.Max(PendenciasProgramadasHojeKpi, Math.Max(PendenciasProgramadasAmanhaKpi, PendenciasProgramadasDepoisKpi)));
        if (quantidade <= 0)
            return 6;

        return Math.Round(8 + ((double)quantidade / maximo) * 22, MidpointRounding.AwayFromZero);
    }

    private double CriarLarguraMiniBarraOperacional(int quantidade)
    {
        var maximo = Math.Max(
            1,
            Math.Max(
                Math.Max(RealizadasHojeKpi, PendentesHojeKpi),
                Math.Max(AtrasadasHojeKpi, Math.Max(PendenciasProgramadasAmanhaKpi, PendenciasProgramadasDepoisKpi))));

        if (quantidade <= 0)
            return 12;

        return Math.Round(12 + ((double)quantidade / maximo) * 92, MidpointRounding.AwayFromZero);
    }

    private void NotificarEstadoExecucoesHoje()
    {
        OnPropertyChanged(nameof(ExecucoesHojeMetaTexto));
        OnPropertyChanged(nameof(ExecucoesHojeStatusTexto));
        OnPropertyChanged(nameof(ExecucoesHojePercentualTexto));
        OnPropertyChanged(nameof(ExecucoesHojePonteiroAngulo));
        OnPropertyChanged(nameof(ExecucoesHojeRealizadasPeso));
        OnPropertyChanged(nameof(ExecucoesHojePendentesPeso));
        OnPropertyChanged(nameof(ExecucoesHojeAtrasadasPeso));
        OnPropertyChanged(nameof(ExecucoesHojeRealizadasLegenda));
        OnPropertyChanged(nameof(ExecucoesHojePendentesLegenda));
        OnPropertyChanged(nameof(ExecucoesHojeAtrasadasLegenda));
        OnPropertyChanged(nameof(OperacaoHojeConcluidasBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojePendentesBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeAtrasadasBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeAmanhaBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeDepoisBarraLargura));
        OnPropertyChanged(nameof(AgendadasHojeSubtexto));
    }

    private void NotificarEstadoPendenciasProgramadas()
    {
        OnPropertyChanged(nameof(PendenciasProgramadasDistribuicaoTexto));
        OnPropertyChanged(nameof(PendenciasProgramadasResumoTexto));
        OnPropertyChanged(nameof(PendenciasProgramadasHojeBarraAltura));
        OnPropertyChanged(nameof(PendenciasProgramadasAmanhaBarraAltura));
        OnPropertyChanged(nameof(PendenciasProgramadasDepoisBarraAltura));
        OnPropertyChanged(nameof(OperacaoHojeConcluidasBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojePendentesBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeAtrasadasBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeAmanhaBarraLargura));
        OnPropertyChanged(nameof(OperacaoHojeDepoisBarraLargura));
        OnPropertyChanged(nameof(TotalAgendadasSubtexto));
    }

    private void NotificarEstadoErros()
    {
        OnPropertyChanged(nameof(TemErrosCriticos));
        OnPropertyChanged(nameof(NaoTemErrosCriticos));
        OnPropertyChanged(nameof(ErrosSubtexto));
    }

    private static string FormatarProximaExecucao(Tarefa tarefa)
    {
        var vencimentoLocal = ConverterUtcParaLocal(NormalizarUtc(tarefa.VencimentoUtc));
        var hojeLocal = DateTime.Now.Date;

        if (vencimentoLocal.Date == hojeLocal)
            return $"Próxima {vencimentoLocal:HH:mm}";
        if (vencimentoLocal.Date == hojeLocal.AddDays(1))
            return $"Amanhã {vencimentoLocal:HH:mm}";

        return $"{vencimentoLocal:dd/MM HH:mm}";
    }

    private static DateTime ConverterUtcParaLocal(DateTime valor)
    {
        return NormalizarUtc(valor).ToLocalTime();
    }

    private static void AtualizarColecao<T>(ObservableCollection<T> destino, IReadOnlyList<T> origem)
    {
        destino.Clear();
        foreach (var item in origem)
            destino.Add(item);
    }

    private void CarregarDiretorioUsuarios()
    {
        _mapaNomesUsuarios.Clear();
        foreach (var user in _userDirectoryService.ListarAtivos())
        {
            _mapaNomesUsuarios[user.Id] = user.Nome;
        }
    }

    private string ResolverNomeUsuario(int userId)
    {
        if (_mapaNomesUsuarios.TryGetValue(userId, out var nome))
            return nome;

        return $"Usuario {userId}";
    }
}
