using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

/// <summary>Estado do editor de âncoras com undo/redo.</summary>
internal sealed class AncorarPdfAncorasEditorState
{
    private readonly AncorarPdfCommandStackState _commandStack;

    public AncorarPdfAncorasEditorState(int maxHistorico = 120)
    {
        _commandStack = new AncorarPdfCommandStackState(maxHistorico);
    }

    public bool PodeUndo => _commandStack.PodeUndo;
    public bool PodeRedo => _commandStack.PodeRedo;
    public IReadOnlyList<AncorarPdfTemplateAncora> EstadoAtual => _commandStack.EstadoAtual;

    public void DefinirEstadoInicial(IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        _commandStack.DefinirEstadoInicial(estado);
    }

    public void AplicarNovoEstado(IReadOnlyList<AncorarPdfTemplateAncora> novoEstado)
    {
        _commandStack.AplicarNovoEstado(novoEstado);
    }

    public bool TentarUndo(out IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        return _commandStack.TentarUndo(out estado);
    }

    public bool TentarRedo(out IReadOnlyList<AncorarPdfTemplateAncora> estado)
    {
        return _commandStack.TentarRedo(out estado);
    }

    public static List<AncorarPdfTemplateAncora> ConverterAncorasAtuais(ObservableCollection<AncorarPdfAncoraItemViewModel> ancoras)
    {
        return ancoras
            .Select((a, idx) => a.ToModel(ordem: idx))
            .ToList();
    }

    public static AncorarPdfTemplateAncora CriarAncoraPadrao(string corHex, int indice)
    {
        var coluna = indice % 5;
        var linha = indice / 5;

        return new AncorarPdfTemplateAncora
        {
            Ordem = indice,
            CorHex = corHex,
            Pagina = 1,
            XRel = Math.Clamp(0.05 + coluna * 0.18, 0, 1),
            YRel = Math.Clamp(0.08 + linha * 0.28, 0, 1),
            LarguraRel = 0.16,
            AlturaRel = 0.08,
            Metadado = new AncorarPdfTemplateMetadado
            {
                NomeExibido = $"Variável {indice + 1}",
                ChaveTecnica = $"variavel_{indice + 1}",
                TipoEsperado = "texto"
            }
        };
    }

    public static AncorarPdfTemplateAncora CriarAncoraPorSelecao(string corHex, int indice, AncorarPdfPreviewSelection selecao)
    {
        return new AncorarPdfTemplateAncora
        {
            Ordem = indice,
            CorHex = corHex,
            Pagina = Math.Max(1, selecao.Pagina),
            XRel = Math.Clamp(selecao.XRel, 0, 1),
            YRel = Math.Clamp(selecao.YRel, 0, 1),
            LarguraRel = Math.Clamp(selecao.LarguraRel, 0, 1),
            AlturaRel = Math.Clamp(selecao.AlturaRel, 0, 1),
            Metadado = new AncorarPdfTemplateMetadado
            {
                NomeExibido = $"Variável {indice + 1}",
                ChaveTecnica = $"variavel_{indice + 1}",
                TipoEsperado = "texto"
            }
        };
    }
}
