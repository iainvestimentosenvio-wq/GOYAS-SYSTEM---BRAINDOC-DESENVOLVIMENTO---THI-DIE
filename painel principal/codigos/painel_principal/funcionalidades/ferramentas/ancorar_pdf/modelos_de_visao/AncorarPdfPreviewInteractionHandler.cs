using System;
using System.Collections.Generic;
using System.Linq;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>
/// Encapsula a lógica de interação com preview: seleção de cor, captura, substituição de âncora.
/// Extraído de AncorarPdfConfiguracaoViewModel para reduzir acoplamento.
/// </summary>
internal sealed class AncorarPdfPreviewInteractionHandler
{
    private readonly AncorarPdfPreviewState _previewState;
    private readonly IAncorarPdfPreviewAdapter _previewAdapter;
    private readonly Action<string, string?> _registrarEvento;
    private readonly IAncorarPdfPreviewInteractionContext _context;

    public AncorarPdfPreviewInteractionHandler(
        AncorarPdfPreviewState previewState,
        IAncorarPdfPreviewAdapter previewAdapter,
        Action<string, string?> registrarEvento,
        IAncorarPdfPreviewInteractionContext context)
    {
        _previewState = previewState;
        _previewAdapter = previewAdapter;
        _registrarEvento = registrarEvento;
        _context = context;
    }

    public void SelecionarCor(string? corHex)
    {
        if (string.IsNullOrWhiteSpace(corHex) || !AncorarPdfPalettePolicy.EhCorPermitida(corHex))
            return;

        _context.CorSelecionada = corHex;
        _previewState.DefinirCorAtiva(corHex);
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(_context.FerramentaInteracaoSelecionada);
        _context.AtualizarEstadoPreviewBindings();
    }

    public void SelecionarFerramentaInteracao(AncorarPdfFerramentaInteracao ferramenta)
    {
        _context.FerramentaInteracaoSelecionada = ferramenta;
        _context.StatusInteracaoPreview = AncorarPdfFerramentaInteracaoCatalogo.ObterInstrucao(ferramenta);
        _context.LimparDeteccaoSmartSeNecessario(ferramenta);
        _registrarEvento("ancorar_pdf_c2_preview_tool_change", $"ferramenta={ferramenta} cor={_context.CorSelecionada}");
    }

    public void OnPreviewSelecaoCapturada(AncorarPdfPreviewSelection selecao)
    {
        if (!_context.PodeEditar)
            return;

        if (string.IsNullOrWhiteSpace(_context.CorSelecionada) || !AncorarPdfPalettePolicy.EhCorPermitida(_context.CorSelecionada))
        {
            var coresUsadas = _context.Ancoras.Select(a => a.CorHex).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var proxCor = AncorarPdfPalettePolicy.CoresFixas.FirstOrDefault(c => !coresUsadas.Contains(c));
            if (proxCor is null)
            {
                _context.StatusInteracaoPreview = AncorarPdfPalettePolicy.MensagemLimiteAncorasAtingido;
                return;
            }
            _context.CorSelecionada = proxCor;
        }

        var existenteMesmaCor = _context.Ancoras.FirstOrDefault(x =>
            string.Equals(x.CorHex, _context.CorSelecionada, StringComparison.OrdinalIgnoreCase));

        if (existenteMesmaCor is not null)
        {
            _context.CorPendenteSubstituicao = _context.CorSelecionada;
            _context.SelecaoPendenteSubstituicao = selecao;
            _context.ConfirmacaoSubstituicaoCorAberta = true;
            _context.StatusInteracaoPreview = "Cor já existente. Confirme a substituição para aplicar nova seleção.";
            return;
        }

        var novoEstado = _context.ConverterAncorasAtuais();
        novoEstado.Add(AncorarPdfAncorasEditorState.CriarAncoraPorSelecao(_context.CorSelecionada, novoEstado.Count, selecao));
        _context.AplicarEstadoComHistorico(novoEstado);
        var novaAncora = _context.Ancoras.LastOrDefault(x =>
            string.Equals(x.CorHex, _context.CorSelecionada, StringComparison.OrdinalIgnoreCase));
        _context.AncoraSelecionada = novaAncora;
        _context.StatusInteracaoPreview = "Seleção aplicada no preview.";
        var ctx = novaAncora is not null
            ? AncorarPdfLogAncoraHelper.FormatarContextoAncora(novaAncora, _context.CorSelecionada)
            : $"tipo=selecao total={_context.AncorasCount}";
        _registrarEvento("ancorar_pdf_c2_preview_render", ctx);
    }

    public void ConfirmarSubstituicaoCor(string substituirRaw)
    {
        var substituir = string.Equals(substituirRaw, "True", StringComparison.OrdinalIgnoreCase);
        if (!_context.ConfirmacaoSubstituicaoCorAberta)
            return;

        var cor = _context.CorPendenteSubstituicao;
        var selecao = _context.SelecaoPendenteSubstituicao;
        _context.ConfirmacaoSubstituicaoCorAberta = false;
        _context.CorPendenteSubstituicao = string.Empty;
        _context.SelecaoPendenteSubstituicao = null;

        if (!substituir)
            return;

        var novoEstado = _context.ConverterAncorasAtuais();
        var indice = novoEstado.FindIndex(x => string.Equals(x.CorHex, cor, StringComparison.OrdinalIgnoreCase));
        if (indice < 0)
            return;

        var antiga = novoEstado[indice];
        novoEstado[indice] = selecao is null
            ? AncorarPdfAncorasEditorState.CriarAncoraPadrao(cor, indice)
            : AncorarPdfAncorasEditorState.CriarAncoraPorSelecao(cor, indice, selecao);
        _context.AplicarEstadoComHistorico(novoEstado);
        var novaAncora = _context.Ancoras.FirstOrDefault(x =>
            string.Equals(x.CorHex, cor, StringComparison.OrdinalIgnoreCase));
        _context.AncoraSelecionada = novaAncora;
        _context.StatusInteracaoPreview = "Substituição aplicada no preview.";
        var ctx = $"cor={cor} antiga_nome={AncorarPdfLogAncoraHelper.Sanitizar(antiga.Metadado.NomeExibido)} antiga_chave={AncorarPdfLogAncoraHelper.Sanitizar(antiga.Metadado.ChaveTecnica)}";
        if (novaAncora is not null)
            ctx += $" nova_nome={AncorarPdfLogAncoraHelper.Sanitizar(novaAncora.NomeExibido)} nova_chave={AncorarPdfLogAncoraHelper.Sanitizar(novaAncora.ChaveTecnica)}";
        _registrarEvento("ancorar_pdf_c2_anchor_replace", ctx);
    }
}

/// <summary>
/// Contrato mínimo para o handler de preview interagir com a VM.
/// </summary>
internal interface IAncorarPdfPreviewInteractionContext
{
    bool PodeEditar { get; }
    string CorSelecionada { get; set; }
    AncorarPdfFerramentaInteracao FerramentaInteracaoSelecionada { get; set; }
    AncorarPdfAncoraItemViewModel? AncoraSelecionada { get; set; }
    string StatusInteracaoPreview { get; set; }
    bool ConfirmacaoSubstituicaoCorAberta { get; set; }
    string CorPendenteSubstituicao { get; set; }
    AncorarPdfPreviewSelection? SelecaoPendenteSubstituicao { get; set; }
    IEnumerable<AncorarPdfAncoraItemViewModel> Ancoras { get; }
    int AncorasCount { get; }
    List<AncorarPdfTemplateAncora> ConverterAncorasAtuais();
    void AplicarEstadoComHistorico(List<AncorarPdfTemplateAncora> estado);
    void AtualizarEstadoPreviewBindings();
    void LimparDeteccaoSmartSeNecessario(AncorarPdfFerramentaInteracao ferramenta);
}
