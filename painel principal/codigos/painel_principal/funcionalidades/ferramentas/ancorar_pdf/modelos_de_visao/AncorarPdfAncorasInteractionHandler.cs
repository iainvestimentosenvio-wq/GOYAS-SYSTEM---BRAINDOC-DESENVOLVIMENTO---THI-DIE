using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula a lógica de interação com âncoras: CRUD, undo/redo, destaque por hover.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfAncorasInteractionHandler
{
    private readonly AncorarPdfAncorasEditorState _ancorasEditorState;
    private readonly Action<string, string?> _registrarEvento;
    private readonly Action<string, long, string, string?> _registrarMetrica;
    private readonly IAncorarPdfAncorasInteractionContext _context;

    public AncorarPdfAncorasInteractionHandler(
        AncorarPdfAncorasEditorState ancorasEditorState,
        Action<string, string?> registrarEvento,
        Action<string, long, string, string?> registrarMetrica,
        IAncorarPdfAncorasInteractionContext context)
    {
        _ancorasEditorState = ancorasEditorState;
        _registrarEvento = registrarEvento;
        _registrarMetrica = registrarMetrica;
        _context = context;
    }

    public void AdicionarAncora()
    {
        var started = Stopwatch.StartNew();

        var existenteMesmaCor = _context.Ancoras.FirstOrDefault(x =>
            string.Equals(x.CorHex, _context.CorSelecionada, StringComparison.OrdinalIgnoreCase));

        if (existenteMesmaCor is not null)
        {
            _context.CorPendenteSubstituicao = _context.CorSelecionada;
            _context.ConfirmacaoSubstituicaoCorAberta = true;
            return;
        }

        var novoEstado = _context.ConverterAncorasAtuais();
        novoEstado.Add(AncorarPdfAncorasEditorState.CriarAncoraPadrao(_context.CorSelecionada, novoEstado.Count));
        _context.AplicarEstadoComHistorico(novoEstado);
        _context.AncoraSelecionada = _context.Ancoras.LastOrDefault(x =>
            string.Equals(x.CorHex, _context.CorSelecionada, StringComparison.OrdinalIgnoreCase));

        _registrarEvento("ancorar_pdf_c2_anchor_add", $"cor={_context.CorSelecionada} total={_context.AncorasCount}");
        _registrarMetrica("ancorar_pdf_c2_anchor_hit_test_ms", started.ElapsedMilliseconds, "ms", $"total={_context.AncorasCount}");
    }

    public void RemoverAncora(AncorarPdfAncoraItemViewModel? ancora)
    {
        if (!_context.PodeEditar || ancora is null)
            return;

        var novoEstado = _context.ConverterAncorasAtuais();
        var removidos = novoEstado.RemoveAll(a =>
            string.Equals(a.CorHex, ancora.CorHex, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Metadado.ChaveTecnica, ancora.ChaveTecnica, StringComparison.OrdinalIgnoreCase));

        if (removidos == 0)
            return;

        var indicePreferido = Math.Min(novoEstado.Count, _context.Ancoras.ToList().FindIndex(x => ReferenceEquals(x, ancora)));
        _context.AplicarEstadoComHistorico(novoEstado);
        var ancorasAtualizadas = _context.Ancoras.ToList();
        _context.AncoraSelecionada = ancorasAtualizadas.Count == 0
            ? null
            : ancorasAtualizadas[Math.Clamp(indicePreferido, 0, ancorasAtualizadas.Count - 1)];
    }

    public void DestacarAncoraPorHover(AncorarPdfAncoraItemViewModel? ancora)
    {
        _context.AtualizarDestaquePreview(ancora);
        _registrarEvento(
            "ancorar_pdf_c2_hover_highlight",
            ancora is null ? "estado=limpo" : $"chave={ancora.ChaveTecnica}");
    }

    public void SelecionarAncora(AncorarPdfAncoraItemViewModel? ancora)
    {
        if (ancora is null)
            return;

        _context.AncoraSelecionada = ancora;
        _registrarEvento("ancorar_pdf_c2_anchor_select", $"chave={ancora.ChaveTecnica}");
    }

    public void Desfazer()
    {
        if (!_ancorasEditorState.TentarUndo(out var estado))
            return;

        _context.AplicarEstadoAncoras(estado, registrarHistorico: false);
        _registrarEvento("ancorar_pdf_c2_undo", $"total={_context.AncorasCount}");
    }

    public void Refazer()
    {
        if (!_ancorasEditorState.TentarRedo(out var estado))
            return;

        _context.AplicarEstadoAncoras(estado, registrarHistorico: false);
        _registrarEvento("ancorar_pdf_c2_redo", $"total={_context.AncorasCount}");
    }
}

/// <summary>
/// Contrato mínimo para o handler de âncoras interagir com a VM.
/// </summary>
internal interface IAncorarPdfAncorasInteractionContext
{
    bool PodeEditar { get; }
    string CorSelecionada { get; }
    AncorarPdfAncoraItemViewModel? AncoraSelecionada { get; set; }
    string CorPendenteSubstituicao { get; set; }
    bool ConfirmacaoSubstituicaoCorAberta { get; set; }
    IEnumerable<AncorarPdfAncoraItemViewModel> Ancoras { get; }
    int AncorasCount { get; }
    List<AncorarPdfTemplateAncora> ConverterAncorasAtuais();
    void AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado);
    void AplicarEstadoAncoras(IReadOnlyList<AncorarPdfTemplateAncora> estado, bool registrarHistorico);
    void AtualizarDestaquePreview(AncorarPdfAncoraItemViewModel? ancora);
}
