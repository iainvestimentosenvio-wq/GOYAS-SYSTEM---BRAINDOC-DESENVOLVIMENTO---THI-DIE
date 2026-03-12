using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Protons.UI.Painel.ViewModels;

/// <summary>
/// Partial class: gerencia a lista de ferramentas na sidebar e o estado do
/// ghost overlay durante drag-and-drop.
///
/// GHOST OVERLAY - COMO FUNCIONA:
///   - DragGhostVisivel/X/Y controlam visibilidade e posicao do Border no Canvas
///   - A IMAGEM do ghost e carregada pelo code-behind (PainelView.axaml.cs),
///     NAO por binding. A propriedade DragGhostImagemUri existe apenas como
///     metadado; ela NAO e usada como Source da Image no XAML.
///   - _metadeGhostPx e ajustado dinamicamente por DefinirTamanhoGhost()
///     para centralizar o ghost no cursor independente do tamanho do card.
///
/// PARA ADICIONAR NOVA FERRAMENTA:
///   1. Criar novo FerramentaTarefaLateralItem em InicializarListaTarefasLateral()
///   2. Se tiver imagem customizada: definir ImagemGhostUri com URI avares://
///   3. Se NAO tiver: o sistema captura screenshot automatico via RenderTargetBitmap
/// </summary>
public sealed partial class PainelViewModel
{
    // AssetAncoraPdfUriUri is defined in PainelViewModel.Cores.cs

    /// <summary>
    /// Metade do tamanho do ghost em pixels, usado para centralizar no cursor.
    /// Atualizado dinamicamente por DefinirTamanhoGhost() conforme o card arrastado.
    /// </summary>
    private double _metadeGhostPx = 56;

    private const string CategoriaExtracao = "Extracao";
    private const string CategoriaValidacao = "Validacao";
    private const string CategoriaIntegracao = "Integracao";
    private const string CategoriaRevisao = "Revisao";
    private const string CategoriaQualidade = "Qualidade";
    private const string CategoriaRelatorio = "Relatorio";

    [ObservableProperty] private bool _listaTarefasLateralAberta;
    [ObservableProperty] private FerramentaTarefaLateralItem? _ferramentaLateralSelecionada;

    // --- Propriedades do ghost overlay (bindings usados no AXAML) ---
    [ObservableProperty] private bool _dragGhostVisivel;
    [ObservableProperty] private double _dragGhostX = -9999;
    [ObservableProperty] private double _dragGhostY = -9999;
    [ObservableProperty] private string _dragGhostImagemUri = AssetAncoraPdfUri;  // Metadado, NAO binding de Image.Source
    [ObservableProperty] private string _dragGhostNomeFerramenta = string.Empty;

    public ObservableCollection<FerramentaTarefaLateralItem> FerramentasTarefasLateral { get; } = new();
    public bool MostrarBotoesLateral => !ListaTarefasLateralAberta;

    partial void OnListaTarefasLateralAbertaChanged(bool value)
    {
        OnPropertyChanged(nameof(MostrarBotoesLateral));

        if (!value)
            EncerrarDragGhost();
    }

    partial void OnFerramentaLateralSelecionadaChanged(FerramentaTarefaLateralItem? value)
    {
        if (value is null)
            return;

        RegistrarInteracaoPainel();
        StatusNavegacao = $"Ferramenta selecionada: {value.Nome}";
        RegistrarEventoPainel("tarefas_lateral_selecionar", $"ferramenta={value.Nome}");
    }

    public void IniciarDragGhost(FerramentaTarefaLateralItem ferramenta, double cursorX, double cursorY)
    {
        DragGhostNomeFerramenta = ferramenta.Nome;
        DragGhostImagemUri = string.IsNullOrWhiteSpace(ferramenta.ImagemGhostUri)
            ? AssetAncoraPdfUri
            : ferramenta.ImagemGhostUri;

        AtualizarDragGhostPosicao(cursorX, cursorY);
        DragGhostVisivel = true;
    }

    public void AtualizarDragGhostPosicao(double cursorX, double cursorY)
    {
        DragGhostX = cursorX - _metadeGhostPx;
        DragGhostY = cursorY - _metadeGhostPx;
    }

    /// <summary>
    /// Chamado por PainelView.DefinirTamanhoGhost() apos carregar a imagem.
    /// Recalcula o offset para manter o ghost centralizado no cursor.
    /// </summary>
    public void DefinirTamanhoGhost(double largura, double altura)
    {
        _metadeGhostPx = Math.Max(largura, altura) / 2.0;
    }

    public void EncerrarDragGhost()
    {
        DragGhostVisivel = false;
        DragGhostX = -9999;
        DragGhostY = -9999;
        DragGhostNomeFerramenta = string.Empty;
    }

    public void RegistrarEventoDragFerramenta(string eventoId, string? contexto = null)
    {
        RegistrarEventoPainel(eventoId, contexto);
    }

    private void InicializarListaTarefasLateral()
    {
        if (FerramentasTarefasLateral.Count > 0)
            return;

        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "ancorar_pdf",
            "Ancorar PDF",
            "Ancora variaveis de PDF por template",
            "#192C4D",
            "#4FA8FF",
            "#D6EEFF",
            "PDF CORE",
            CategoriaExtracao,
            true,
            AssetAncoraPdfUri,
            AssetAncoraPdfUri));
        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "validador_fiscal",
            "Validador fiscal",
            "Confere regras e consistência",
            "#2B2B57",
            "#5D5AAE",
            "#BBB8FF",
            "Base",
            CategoriaValidacao));
        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "conector_erp",
            "Conector ERP",
            "Envia dados para o ERP",
            "#1F3B36",
            "#2D7A6A",
            "#91EAD5",
            "Base",
            CategoriaIntegracao));
        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "revisao_humana",
            "Revisão humana",
            "Ponto de conferência manual",
            "#3C2D19",
            "#8A6732",
            "#FFD88F",
            "Base",
            CategoriaRevisao));
        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "checklist_qa",
            "Checklist QA",
            "Valida a qualidade final",
            "#3C1F2D",
            "#8A3C63",
            "#FF9CC8",
            "Base",
            CategoriaQualidade));
        FerramentasTarefasLateral.Add(new FerramentaTarefaLateralItem(
            "saida_relatorio",
            "Saída relatório",
            "Gera resumo de execução",
            "#2A3340",
            "#526A86",
            "#A8C3E5",
            "Base",
            CategoriaRelatorio));
    }

    [RelayCommand]
    private void AlternarListaTarefasLateral()
    {
        RegistrarInteracaoPainel();
        InicializarListaTarefasLateral();
        ListaTarefasLateralAberta = !ListaTarefasLateralAberta;

        if (ListaTarefasLateralAberta)
        {
            NavegarPara(DestinoNavegacaoPainel.ImportarDocumentos, "sidebar_tarefas");
        }
        else
        {
            NavegarPara(DestinoNavegacaoPainel.Dashboard, "sidebar_tarefas_fechar");
        }

        RegistrarEventoPainel("tarefas_lateral_toggle", $"aberta={ListaTarefasLateralAberta}");
    }
}
