using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula a lógica de salvar configuração: validação, montagem da entrada e chamada ao serviço.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfSaveHandler
{
    private readonly IAncorarPdfConfiguracaoService _service;
    private readonly AncorarPdfSaveOrchestrator _saveOrchestrator;
    private readonly AncorarPdfAgendamentoState _agendamentoState;
    private readonly Action<string, string?> _registrarEvento;
    private readonly Action<string, long, string, string?> _registrarMetrica;
    private readonly IAncorarPdfSaveContext _context;

    public AncorarPdfSaveHandler(
        IAncorarPdfConfiguracaoService service,
        AncorarPdfSaveOrchestrator saveOrchestrator,
        AncorarPdfAgendamentoState agendamentoState,
        Action<string, string?> registrarEvento,
        Action<string, long, string, string?> registrarMetrica,
        IAncorarPdfSaveContext context)
    {
        _service = service;
        _saveOrchestrator = saveOrchestrator;
        _agendamentoState = agendamentoState;
        _registrarEvento = registrarEvento;
        _registrarMetrica = registrarMetrica;
        _context = context;
    }

    public async Task SalvarAsync()
    {
        if (!await _saveOrchestrator.TentarIniciarAsync())
        {
            _context.Mensagem = "Ja existe um salvamento em andamento. Aguarde finalizar.";
            return;
        }

        var input = _context.BuildValidationInput();
        if (!_saveOrchestrator.Validar(input, _agendamentoState.NormalizarTimezoneIdUi, out var erroLocal))
        {
            var correlationId = AncorarPdfSaveOrchestrator.GerarCorrelationId();
            var erroTipado = _saveOrchestrator.CriarErroValidacao(erroLocal, correlationId);
            _context.UltimoErroTipado = erroTipado;
            _context.Mensagem = _saveOrchestrator.ResolverMensagemUsuario(erroTipado);
            _registrarEvento("ancorar_pdf_c2_save_validation_fail",
                $"corr={correlationId} code={erroTipado.Code} category={erroTipado.Category} retryable={erroTipado.Retryable}");
            _saveOrchestrator.FinalizarComSeguranca();
            return;
        }

        var started = Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            _context.IsBusy = true;
            _context.Mensagem = string.Empty;
            _context.UltimoErroTipado = null;

            var entrada = _context.BuildEntrada();
            var resultado = await Task.Run(() => _service.CriarOuAtualizar(entrada, _context.SolicitanteUserId), cts.Token);

            _context.ApplySaveResult(resultado);
            _context.Mensagem = "Configuração salva com sucesso.";
            _registrarEvento("ancorar_pdf_c2_save_ok", $"tarefa_id={resultado.TarefaId} versao={resultado.VersaoTemplate}");
            _context.OnSaveSucesso?.Invoke(resultado);
        }
        catch (OperationCanceledException)
        {
            var correlationId = AncorarPdfSaveOrchestrator.GerarCorrelationId();
            var erroTipado = _saveOrchestrator.CriarErroTimeout(TimeSpan.FromSeconds(30), correlationId);
            _context.UltimoErroTipado = erroTipado;
            _context.Mensagem = _saveOrchestrator.ResolverMensagemUsuario(erroTipado);
            _registrarEvento("ancorar_pdf_c2_save_timeout",
                $"corr={correlationId} code={erroTipado.Code} retryable={erroTipado.Retryable} tarefa_id={_context.TarefaId?.ToString() ?? "novo"}");
        }
        catch (Exception ex)
        {
            var correlationId = AncorarPdfSaveOrchestrator.GerarCorrelationId();
            var erroTipado = _saveOrchestrator.MapearErro(ex, correlationId);
            _context.UltimoErroTipado = erroTipado;
            _context.Mensagem = _saveOrchestrator.ResolverMensagemUsuario(erroTipado);
            if (erroTipado.Code == AncorarPdfErrorCode.InvalidPathPolicy ||
                ex.Message.Contains("não autorizado", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("allowlist", StringComparison.OrdinalIgnoreCase))
            {
                _registrarEvento("ancorar_pdf_c2_picker_network_denied", $"corr={correlationId} cliente_id={_context.ClienteId}");
            }
            _registrarEvento("ancorar_pdf_c2_save_fail",
                $"corr={correlationId} code={erroTipado.Code} category={erroTipado.Category} retryable={erroTipado.Retryable} erro={ex.GetType().Name}");
        }
        finally
        {
            _context.IsBusy = false;
            _registrarMetrica("ancorar_pdf_c2_save_ms", started.ElapsedMilliseconds, "ms", $"tarefa_id={_context.TarefaId?.ToString() ?? "novo"}");
            _saveOrchestrator.FinalizarComSeguranca();
        }
    }
}

internal interface IAncorarPdfSaveContext
{
    bool IsBusy { get; set; }
    string Mensagem { get; set; }
    AncorarPdfError? UltimoErroTipado { get; set; }
    int? TarefaId { get; set; }
    int ClienteId { get; set; }
    int EsteiraId { get; set; }
    int VersaoTemplate { get; set; }
    string ProgramadoPorResumo { get; set; }
    int SolicitanteUserId { get; }
    Action<AncorarPdfConfiguracaoTarefa>? OnSaveSucesso { get; }
    AncorarPdfSaveValidationInput BuildValidationInput();
    AncorarPdfSalvarEntrada BuildEntrada();
    void ApplySaveResult(AncorarPdfConfiguracaoTarefa resultado);
}
