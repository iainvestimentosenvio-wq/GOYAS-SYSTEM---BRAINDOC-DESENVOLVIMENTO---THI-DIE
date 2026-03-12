using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.ViewModels;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Wizard de criação de tarefas AncorarPdf acionado pelo drop na régua.
/// Passo 1 = Painel Básico, Passo 2 = Editor de Âncoras (tela cheia, gerido pelo AncorarPdfConfiguracaoViewModel),
/// Passo 3 = Confirmação SIM.
/// Edição de tarefas existentes continua usando AncorarPdfConfiguracaoViewModel diretamente (sem wizard).
/// </summary>
public sealed partial class AncorarPdfAgendamentoBasicoViewModel : ObservableObject
{
    private readonly IAncorarPdfConfiguracaoService _service;
    private readonly ITarefaService _tarefaService;
    private readonly AncorarPdfConfiguracaoViewModel _configuracaoVm;
    private readonly Action _fecharWizard;
    private IAncorarPdfFilePicker _filePicker;
    private readonly TimeProvider _timeProvider;

    private int _clienteId;
    private int _esteiraId;
    private int _userId;
    private string _userName = string.Empty;

    private AncorarPdfFluxoEstadoAncoras? _ancoraStagging;

    [ObservableProperty] private AncorarPdfEtapaFluxo _etapaAtual;
    [ObservableProperty] private bool _estaAtivo;
    [ObservableProperty] private string _nomeTarefa = string.Empty;
    [ObservableProperty] private DateTimeOffset? _dataSelecionada;
    [ObservableProperty] private int _horaSelecionado;
    [ObservableProperty] private int _minutoSelecionado;
    [ObservableProperty] private string _recorrenciaSelecionada = "Unica";
    [ObservableProperty] private string _pastaMonitoradaPath = string.Empty;
    [ObservableProperty] private string _pdfModeloPath = string.Empty;
    [ObservableProperty] private string _mensagem = string.Empty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _conflitoMesmoHorarioDetectado;
    [ObservableProperty] private string _conflitoMensagem = string.Empty;
    [ObservableProperty] private bool _confirmacaoConflitoAceita;

    public static IReadOnlyList<string> RecorrenciasPermitidas { get; } =
        ["Unica", "Diaria", "Semanal", "Mensal"];

    public int QuantidadeAncoras => _ancoraStagging?.Ancoras.Count ?? 0;
    public bool PodeAgendar => QuantidadeAncoras > 0;
    public bool PodeConfirmar => _ancoraStagging is not null;
    public bool PodeConfirmarConflitoMesmoHorario => ConflitoMesmoHorarioDetectado && !IsBusy;
    public bool PodeAvancarParaAncoras => !string.IsNullOrWhiteSpace(PdfModeloPath)
                                        && !string.IsNullOrWhiteSpace(NomeTarefa);

    // ── Controle de horário inteligente ──────────────────────────────────────

    // Hora atual do sistema (injetável via TimeProvider para testes determinísticos).
    private DateTime AgendamentoNow => _timeProvider.GetLocalNow().DateTime;

    /// <summary>Mínimo de hora permitido: 0 para datas futuras; hora atual para hoje.</summary>
    public int HoraMinima
    {
        get
        {
            var agora = AgendamentoNow;
            var dataSel = DataSelecionada?.Date ?? DateTime.Today;
            if (dataSel.Date != agora.Date) return 0;
            // Se estamos no último minuto da última hora (23:59+), amanhã começa em 00:00.
            var minTime = agora.AddMinutes(1);
            return minTime.Date > agora.Date ? 0 : minTime.Hour;
        }
    }

    /// <summary>Mínimo de minuto permitido: 0 quando a hora já ultrapassa a mínima; minuto+1 para hora corrente hoje.</summary>
    public int MinutoMinimo
    {
        get
        {
            var agora = AgendamentoNow;
            var dataSel = DataSelecionada?.Date ?? DateTime.Today;
            if (dataSel.Date != agora.Date) return 0;
            var minTime = agora.AddMinutes(1);
            if (minTime.Date > agora.Date) return 0; // virou meia-noite
            return HoraSelecionado == minTime.Hour ? minTime.Minute : 0;
        }
    }

    /// <summary>True quando a data selecionada é hoje — exibe hint de "apenas horários futuros".</summary>
    public bool IsHoje => (DataSelecionada?.Date ?? DateTime.Today) == AgendamentoNow.Date;

    public string ResumoAncoras => QuantidadeAncoras == 0
        ? "Nenhuma âncora configurada"
        : $"{QuantidadeAncoras} âncora{(QuantidadeAncoras == 1 ? "" : "s")} configurada{(QuantidadeAncoras == 1 ? "" : "s")} ✓";

    public bool IsEtapaBasico => EtapaAtual == AncorarPdfEtapaFluxo.Basico;
    public bool IsEtapaConfirmacao => EtapaAtual == AncorarPdfEtapaFluxo.Confirmacao;
    public bool MostrarAcoesConfirmacaoPadrao => IsEtapaConfirmacao && !ConflitoMesmoHorarioDetectado;
    public bool MostrarAcoesConfirmacaoConflito => IsEtapaConfirmacao && ConflitoMesmoHorarioDetectado;

    partial void OnEtapaAtualChanged(AncorarPdfEtapaFluxo value)
    {
        OnPropertyChanged(nameof(IsEtapaBasico));
        OnPropertyChanged(nameof(IsEtapaConfirmacao));
        OnPropertyChanged(nameof(MostrarAcoesConfirmacaoPadrao));
        OnPropertyChanged(nameof(MostrarAcoesConfirmacaoConflito));
    }

    partial void OnConflitoMesmoHorarioDetectadoChanged(bool value)
    {
        OnPropertyChanged(nameof(PodeConfirmarConflitoMesmoHorario));
        OnPropertyChanged(nameof(MostrarAcoesConfirmacaoPadrao));
        OnPropertyChanged(nameof(MostrarAcoesConfirmacaoConflito));
        ConfirmarConflitoMesmoHorarioCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(PodeConfirmarConflitoMesmoHorario));
        ConfirmarConflitoMesmoHorarioCommand.NotifyCanExecuteChanged();
    }

    public AncorarPdfAgendamentoBasicoViewModel(
        IAncorarPdfConfiguracaoService service,
        ITarefaService tarefaService,
        AncorarPdfConfiguracaoViewModel configuracaoVm,
        Action fecharWizard,
        IAncorarPdfFilePicker? filePicker = null,
        TimeProvider? timeProvider = null)
    {
        _service = service;
        _tarefaService = tarefaService;
        _configuracaoVm = configuracaoVm;
        _fecharWizard = fecharWizard;
        _filePicker = filePicker ?? new AncorarPdfNullFilePicker();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Substitui o FilePicker pelo picker real (StorageProvider) após o TopLevel estar disponível.
    /// </summary>
    public void AtualizarFilePicker(IAncorarPdfFilePicker filePicker)
    {
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
    }

    /// <summary>Abre o wizard para criação de nova tarefa via drop.</summary>
    public void IniciarPorDrop(NovaTarefaDropPayload payload, int clienteId, int userId, string userName)
    {
        if (clienteId <= 0)
        {
            Mensagem = "Cliente não selecionado. Selecione um cliente no topo do painel antes de criar uma tarefa.";
            EstaAtivo = false;
            return;
        }

        try
        {
            _clienteId = clienteId;
            _esteiraId = payload.EsteiraId;
            _userId = userId;
            _userName = userName;
            _ancoraStagging = null;

            var horarioLocal = payload.TempoAlvoUtc.ToLocalTime();
            // Se o horário alvo já passou (drop em slot passado), avançar para agora+1min.
            var agora = AgendamentoNow;
            var horario = horarioLocal < agora.AddMinutes(1) ? agora.AddMinutes(1) : horarioLocal;
            NomeTarefa = "Ancorar PDF";
            // Definir hora/minuto ANTES da data para que OnDataSelecionadaChanged
            // não sobrescreva com valores antigos.
            HoraSelecionado = horario.Hour;
            MinutoSelecionado = horario.Minute;
            // DateTime.SpecifyKind(..., Unspecified) evita ArgumentException no construtor
            // DateTimeOffset quando Kind=Local e offset=0 não batem com o timezone da máquina.
            DataSelecionada = new DateTimeOffset(
                DateTime.SpecifyKind(horario.Date, DateTimeKind.Unspecified),
                TimeSpan.Zero);
            RecorrenciaSelecionada = "Unica";
            PastaMonitoradaPath = string.Empty;
            PdfModeloPath = string.Empty;
            Mensagem = string.Empty;
            LimparEstadoConflito();
            EtapaAtual = AncorarPdfEtapaFluxo.Basico;
            EstaAtivo = true;

            NotifyAncoraProps();
            AvancarParaAncorasCommand.NotifyCanExecuteChanged();
        }
        catch (Exception ex)
        {
            // Falha na inicialização do wizard não pode derrubar o app.
            // Exibir mensagem de erro e não abrir o wizard.
            Mensagem = $"Erro ao abrir wizard: {ex.Message}";
            EstaAtivo = false;
        }
    }

    public void ResetSilencioso()
    {
        _clienteId = 0;
        _esteiraId = 0;
        _userId = 0;
        _userName = string.Empty;
        _ancoraStagging = null;

        NomeTarefa = string.Empty;
        DataSelecionada = new DateTimeOffset(
            DateTime.SpecifyKind(AgendamentoNow.Date, DateTimeKind.Unspecified),
            TimeSpan.Zero);
        HoraSelecionado = 0;
        MinutoSelecionado = 0;
        RecorrenciaSelecionada = "Unica";
        PastaMonitoradaPath = string.Empty;
        PdfModeloPath = string.Empty;
        Mensagem = string.Empty;
        IsBusy = false;
        LimparEstadoConflito();
        EtapaAtual = AncorarPdfEtapaFluxo.Basico;
        EstaAtivo = false;

        OnPropertyChanged(nameof(HoraMinima));
        OnPropertyChanged(nameof(MinutoMinimo));
        OnPropertyChanged(nameof(IsHoje));
        OnPropertyChanged(nameof(PodeAvancarParaAncoras));
        NotifyAncoraProps();
        AvancarParaAncorasCommand.NotifyCanExecuteChanged();
    }

    partial void OnNomeTarefaChanged(string value)
    {
        AvancarParaAncorasCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PodeAvancarParaAncoras));
    }

    partial void OnPdfModeloPathChanged(string value)
    {
        AvancarParaAncorasCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(PodeAvancarParaAncoras));
    }

    partial void OnDataSelecionadaChanged(DateTimeOffset? value)
    {
        OnPropertyChanged(nameof(HoraMinima));
        OnPropertyChanged(nameof(MinutoMinimo));
        OnPropertyChanged(nameof(IsHoje));
        LimparEstadoConflito();
        ClampHorarioSePassado();
    }

    partial void OnHoraSelecionadoChanged(int value)
    {
        LimparEstadoConflito();

        // Quando a hora muda, o mínimo de minuto pode mudar também.
        OnPropertyChanged(nameof(MinutoMinimo));
        // Se hoje e minuto ficou abaixo do mínimo, avançar.
        var minMin = MinutoMinimo;
        if (minMin > 0 && MinutoSelecionado < minMin)
            MinutoSelecionado = minMin;
    }

    partial void OnMinutoSelecionadoChanged(int value)
    {
        LimparEstadoConflito();
    }

    partial void OnRecorrenciaSelecionadaChanged(string value)
    {
        LimparEstadoConflito();
    }

    /// <summary>
    /// Quando o usuário seleciona hoje e a hora/minuto já passaram, avança para o próximo
    /// minuto disponível. Chamado automaticamente ao mudar DataSelecionada.
    /// </summary>
    private void ClampHorarioSePassado()
    {
        var agora = AgendamentoNow;
        var dataSel = DataSelecionada?.Date ?? DateTime.Today;
        if (dataSel.Date != agora.Date) return; // data futura → sem restrição

        var minTime = agora.AddMinutes(1);
        // Virou meia-noite? Não há horário válido para hoje → avançar data para amanhã.
        if (minTime.Date > agora.Date)
        {
            DataSelecionada = new DateTimeOffset(
                DateTime.SpecifyKind(minTime.Date, DateTimeKind.Unspecified), TimeSpan.Zero);
            HoraSelecionado = 0;
            MinutoSelecionado = 0;
            return;
        }

        if (HoraSelecionado < minTime.Hour)
        {
            HoraSelecionado = minTime.Hour;
            MinutoSelecionado = minTime.Minute;
        }
        else if (HoraSelecionado == minTime.Hour && MinutoSelecionado < minTime.Minute)
        {
            MinutoSelecionado = minTime.Minute;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeAvancarParaAncoras))]
    private void AvancarParaAncoras()
    {
        // PdfModeloPath pode ser uma pasta — resolve o PDF mais antigo dentro dela
        string pdfParaEditor = PdfModeloPath;
        if (Directory.Exists(PdfModeloPath))
        {
            var pdfEncontrado = Directory.EnumerateFiles(PdfModeloPath, "*.pdf", SearchOption.TopDirectoryOnly)
                .OrderBy(f => File.GetLastWriteTimeUtc(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (pdfEncontrado is null)
            {
                Mensagem = "A pasta ainda não contém nenhum arquivo PDF. " +
                           "Adicione um PDF à pasta e tente novamente, ou configure as âncoras depois.";
                return;
            }
            pdfParaEditor = pdfEncontrado;
        }

        Mensagem = string.Empty;
        _configuracaoVm.EnterAnchorOnlyMode(
            pdfParaEditor,
            OnAncorasConcluidas,
            onCancelado: () =>
            {
                Mensagem = string.Empty;
                EtapaAtual = AncorarPdfEtapaFluxo.Basico;
            });
        EtapaAtual = AncorarPdfEtapaFluxo.AncorasTelaCheia;
    }

    private void OnAncorasConcluidas(AncorarPdfFluxoEstadoAncoras estado)
    {
        _ancoraStagging = estado;
        EtapaAtual = AncorarPdfEtapaFluxo.Basico;
        NotifyAncoraProps();
    }

    [RelayCommand]
    private void AbrirConfirmacao()
    {
        if (!PodeAgendar) return;

        if (string.IsNullOrWhiteSpace(PastaMonitoradaPath))
        {
            Mensagem = "Selecione a pasta monitorada antes de agendar.";
            return;
        }

        // Validar que o horário escolhido está no futuro.
        var agendamentoLocal = ObterAgendamentoLocal();
        if (agendamentoLocal <= AgendamentoNow)
        {
            Mensagem = "O horário selecionado já passou. Escolha uma data/hora futura.";
            ClampHorarioSePassado(); // avança automaticamente para o próximo válido
            return;
        }

        Mensagem = string.Empty;
        LimparEstadoConflito();
        EtapaAtual = AncorarPdfEtapaFluxo.Confirmacao;
    }

    private DateTime ObterAgendamentoLocal()
    {
        var datePart = DataSelecionada?.Date ?? DateTime.Today;
        return new DateTime(
            datePart.Year, datePart.Month, datePart.Day,
            Math.Clamp(HoraSelecionado, 0, 23),
            Math.Clamp(MinutoSelecionado, 0, 59),
            0,
            DateTimeKind.Local);
    }

    [RelayCommand]
    private void VoltarDoConfirmar()
    {
        EtapaAtual = AncorarPdfEtapaFluxo.Basico;
    }

    [RelayCommand]
    private async Task ConfirmarAgendamentoAsync()
    {
        if (_ancoraStagging is null || IsBusy)
            return;

        if (_clienteId <= 0)
        {
            Mensagem = "Cliente não selecionado. Feche e selecione um cliente no topo do painel.";
            return;
        }

        IsBusy = true;
        Mensagem = string.Empty;

        try
        {
            var ancoras = _ancoraStagging.Ancoras.ToList();
            var agendamentoLocal = ObterAgendamentoLocal();

            if (!ConfirmacaoConflitoAceita)
            {
                var conflitoMensagem = await DetectarConflitoMesmoHorarioAsync(agendamentoLocal);
                if (!string.IsNullOrWhiteSpace(conflitoMensagem))
                {
                    ConflitoMesmoHorarioDetectado = true;
                    ConflitoMensagem = conflitoMensagem;
                    Mensagem = conflitoMensagem;
                    return;
                }
            }

            ConflitoMesmoHorarioDetectado = false;
            ConflitoMensagem = string.Empty;

            var recorrencia = RecorrenciaSelecionada switch
            {
                "Diaria" => TarefaRecorrencia.Diaria,
                "Semanal" => TarefaRecorrencia.Semanal,
                "Mensal" => TarefaRecorrencia.Mensal,
                _ => TarefaRecorrencia.Nenhuma
            };

            var entrada = new AncorarPdfSalvarEntrada
            {
                TarefaId = null,
                ClienteId = _clienteId,
                EsteiraId = _esteiraId,
                NomeTarefaPersonalizado = NomeTarefa.Trim(),
                AgendamentoLocal = agendamentoLocal,
                Recorrencia = recorrencia,
                PastaMonitoradaPath = PastaMonitoradaPath,
                PdfModeloPath = _ancoraStagging.PdfModeloPath,
                NomeReferenciaArquivo = Path.GetFileNameWithoutExtension(_ancoraStagging.PdfModeloPath),
                ValidacaoClienteAtiva = true,
                LimiarSimilaridadeNome = 0.75,
                HighlightOpacity = 0.40,
                TemplateAncoras = ancoras,
                ProgramadoPorUserId = _userId,
                ProgramadoPorNome = _userName
            };

            await Task.Run(() => _service.CriarOuAtualizar(entrada, _userId));
            Fechar();
        }
        catch (Exception ex)
        {
            Mensagem = ex.Message.Contains("ClienteId", StringComparison.OrdinalIgnoreCase)
                ? $"Erro ao salvar: {ex.Message} Verifique se um cliente está selecionado no painel."
                : $"Erro ao salvar: {ex.Message}";
            EtapaAtual = AncorarPdfEtapaFluxo.Basico;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(PodeConfirmarConflitoMesmoHorario))]
    private async Task ConfirmarConflitoMesmoHorarioAsync()
    {
        ConfirmacaoConflitoAceita = true;
        ConflitoMesmoHorarioDetectado = false;
        ConflitoMensagem = string.Empty;
        Mensagem = string.Empty;
        await ConfirmarAgendamentoAsync();
    }

    [RelayCommand]
    private void CancelarConflitoMesmoHorario()
    {
        LimparEstadoConflito();
        Mensagem = string.Empty;
        EtapaAtual = AncorarPdfEtapaFluxo.Basico;
    }

    [RelayCommand]
    private async Task SelecionarPastaAsync()
    {
        var path = await _filePicker.SelecionarPastaAsync();
        if (!string.IsNullOrWhiteSpace(path))
            PastaMonitoradaPath = path;
    }

    [RelayCommand]
    private async Task SelecionarPdfModeloAsync()
    {
        var path = await _filePicker.SelecionarPdfModeloPastaWizardAsync();
        if (string.IsNullOrWhiteSpace(path))
            return;

        PdfModeloPath = path;
        if (Directory.Exists(path))
        {
            Mensagem = "Modo wizard: pasta selecionada. Vamos usar um PDF dessa pasta para configurar as âncoras.";
        }
    }

    [RelayCommand]
    private void Fechar()
    {
        ResetSilencioso();
        _fecharWizard();
    }

    private void NotifyAncoraProps()
    {
        OnPropertyChanged(nameof(QuantidadeAncoras));
        OnPropertyChanged(nameof(PodeAgendar));
        OnPropertyChanged(nameof(ResumoAncoras));
    }

    private async Task<string?> DetectarConflitoMesmoHorarioAsync(DateTime agendamentoLocal)
    {
        if (_clienteId <= 0 || _esteiraId <= 0)
            return null;

        var filtro = new TarefaFiltroConsulta
        {
            ClienteId = _clienteId,
            SomenteMinhasTarefas = false
        };

        var alvoUtc = NormalizarParaMinutoUtc(agendamentoLocal.ToUniversalTime());
        var tarefas = await Task.Run(() => _tarefaService.Buscar(filtro, _userId));
        var conflitos = tarefas
            .Where(t =>
                (t.EsteiraId ?? 0) == _esteiraId &&
                t.Status != TarefaStatus.Concluida &&
                NormalizarParaMinutoUtc(t.VencimentoUtc) == alvoUtc)
            .OrderBy(t => t.CriadoEmUtc)
            .ThenBy(t => t.Id)
            .ToList();

        if (conflitos.Count == 0)
            return null;

        var horarioTexto = agendamentoLocal.ToString("dd/MM/yyyy HH:mm");
        if (conflitos.Count == 1)
            return $"Conflito de horário: já existe a tarefa '{conflitos[0].Titulo}' na esteira {_esteiraId} em {horarioTexto}. Deseja salvar mesmo assim?";

        return $"Conflito de horário: já existem {conflitos.Count} tarefa(s) na esteira {_esteiraId} em {horarioTexto}. Deseja salvar mesmo assim?";
    }

    private static DateTime NormalizarParaMinutoUtc(DateTime valor)
    {
        var utc = valor.Kind switch
        {
            DateTimeKind.Utc => valor,
            DateTimeKind.Local => valor.ToUniversalTime(),
            _ => DateTime.SpecifyKind(valor, DateTimeKind.Utc)
        };

        return new DateTime(
            utc.Year,
            utc.Month,
            utc.Day,
            utc.Hour,
            utc.Minute,
            0,
            DateTimeKind.Utc);
    }

    private void LimparEstadoConflito()
    {
        ConfirmacaoConflitoAceita = false;
        ConflitoMesmoHorarioDetectado = false;
        ConflitoMensagem = string.Empty;

        if (!string.IsNullOrWhiteSpace(Mensagem) &&
            Mensagem.Contains("Conflito de horário", StringComparison.OrdinalIgnoreCase))
        {
            Mensagem = string.Empty;
        }
    }
}
