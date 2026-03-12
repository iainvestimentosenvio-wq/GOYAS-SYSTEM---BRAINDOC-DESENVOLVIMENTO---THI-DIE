using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

public sealed partial class AncorarPdfAncoraItemViewModel : ObservableObject
{
    public static IReadOnlyList<AncorarPdfModoAncora> ModosAncora { get; } = Enum.GetValues<AncorarPdfModoAncora>();

    [ObservableProperty] private string _corHex = AncorarPdfPalettePolicy.CoresFixas[0];
    [ObservableProperty] private int _pagina = 1;
    [ObservableProperty] private double _xRel;
    [ObservableProperty] private double _yRel;
    [ObservableProperty] private double _larguraRel = 0.15;
    [ObservableProperty] private double _alturaRel = 0.08;

    [ObservableProperty] private string _nomeExibido = string.Empty;
    [ObservableProperty] private string _chaveTecnica = string.Empty;
    [ObservableProperty] private string _tipoEsperado = "texto";
    [ObservableProperty] private string _exemploEsperado = string.Empty;
    [ObservableProperty] private string _regraNormalizacao = string.Empty;
    [ObservableProperty] private bool _destacadaNoPreview;
    [ObservableProperty] private bool _selecionadaNaLista;
    [ObservableProperty] private double _zoomPreview = 1.0;
    [ObservableProperty] private double _highlightOpacity = 0.40;

    [ObservableProperty] private AncorarPdfModoAncora _modoAncora = AncorarPdfModoAncora.RegiaoFixa;
    [ObservableProperty] private string _textoAncora = string.Empty;
    [ObservableProperty] private double _larguraExtracaoRel = 0.2;
    [ObservableProperty] private double _alturaExtracaoRel = 0.05;

    public string PosicaoResumo => ModoAncora == AncorarPdfModoAncora.RegiaoFixa
        ? FormattableString.Invariant($"p{Pagina} x={XRel:0.000} y={YRel:0.000} w={LarguraRel:0.000} h={AlturaRel:0.000}")
        : FormattableString.Invariant($"p{Pagina} texto=\"{TextoAncora}\" {ModoAncora}");
    public double PreviewX => XRel * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview;
    public double PreviewY => YRel * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview;
    public double PreviewLargura => LarguraRel * AncorarPdfPreviewLayout.BaseWidth * ZoomPreview;
    public double PreviewAltura => AlturaRel * AncorarPdfPreviewLayout.BaseHeight * ZoomPreview;

    public string CorHexBackground
    {
        get
        {
            if (CorHex.Length != 7 || !CorHex.StartsWith('#'))
                return "#33FFFFFF";
            var alpha = ((int)Math.Round(Math.Clamp(HighlightOpacity, 0.30, 0.45) * 255)).ToString("X2");
            return $"#{alpha}{CorHex[1..]}";
        }
    }

    /// <summary>True quando o modo exige texto âncora (TextoADireita ou TextoAbaixo).</summary>
    public bool EhModoTexto => ModoAncora != AncorarPdfModoAncora.RegiaoFixa;

    public string RegraNormalizacaoInferida => TipoEsperado switch
    {
        "cpf"                                     => "cpf",
        "cnpj"                                    => "cnpj",
        "moeda_brl" or "moeda"                    => "moeda_brl",
        "data_br" or "data" or "data_extenso"     => "data_br",
        "inteiro" or "numero"                     => "inteiro",
        _                                         => string.Empty
    };

    partial void OnPaginaChanged(int value) => OnPropertyChanged(nameof(PosicaoResumo));
    partial void OnXRelChanged(double value) => AtualizarPropsPreview();
    partial void OnYRelChanged(double value) => AtualizarPropsPreview();
    partial void OnLarguraRelChanged(double value) => AtualizarPropsPreview();
    partial void OnAlturaRelChanged(double value) => AtualizarPropsPreview();
    partial void OnZoomPreviewChanged(double value) => AtualizarPropsPreview();
    partial void OnModoAncoraChanged(AncorarPdfModoAncora value)
    {
        OnPropertyChanged(nameof(PosicaoResumo));
        OnPropertyChanged(nameof(EhModoTexto));
    }
    partial void OnTextoAncoraChanged(string value) => OnPropertyChanged(nameof(PosicaoResumo));
    partial void OnCorHexChanged(string value) => OnPropertyChanged(nameof(CorHexBackground));
    partial void OnHighlightOpacityChanged(double value) => OnPropertyChanged(nameof(CorHexBackground));
    partial void OnTipoEsperadoChanged(string value) => OnPropertyChanged(nameof(RegraNormalizacaoInferida));

    private string? ObterRegraNormalizacaoParaSalvar()
    {
        if (!string.IsNullOrWhiteSpace(RegraNormalizacao))
            return RegraNormalizacao.Trim();
        return string.IsNullOrWhiteSpace(RegraNormalizacaoInferida) ? null : RegraNormalizacaoInferida;
    }

    public void AtualizarZoomPreview(double zoom)
    {
        ZoomPreview = Math.Clamp(zoom, 0.50, 4.00);
    }

    public AncorarPdfTemplateAncora ToModel(int ordem)
    {
        return new AncorarPdfTemplateAncora
        {
            Ordem = ordem,
            CorHex = CorHex,
            Pagina = Math.Max(1, Pagina),
            XRel = Math.Clamp(XRel, 0, 1),
            YRel = Math.Clamp(YRel, 0, 1),
            LarguraRel = Math.Clamp(LarguraRel, 0, 1),
            AlturaRel = Math.Clamp(AlturaRel, 0, 1),
            ModoAncora = ModoAncora,
            TextoAncora = string.IsNullOrWhiteSpace(TextoAncora) ? null : TextoAncora.Trim(),
            LarguraExtracaoRel = Math.Clamp(LarguraExtracaoRel, 0.01, 1),
            AlturaExtracaoRel = Math.Clamp(AlturaExtracaoRel, 0.01, 1),
            Metadado = new AncorarPdfTemplateMetadado
            {
                NomeExibido = NomeExibido,
                ChaveTecnica = ChaveTecnica,
                TipoEsperado = TipoEsperado,
                ExemploEsperado = string.IsNullOrWhiteSpace(ExemploEsperado) ? null : ExemploEsperado,
                RegraNormalizacao = ObterRegraNormalizacaoParaSalvar()
            }
        };
    }

    public static AncorarPdfAncoraItemViewModel FromModel(AncorarPdfTemplateAncora model, double zoomPreview, double highlightOpacity = 0.40)
    {
        return new AncorarPdfAncoraItemViewModel
        {
            CorHex = model.CorHex,
            Pagina = model.Pagina,
            XRel = model.XRel,
            YRel = model.YRel,
            LarguraRel = model.LarguraRel,
            AlturaRel = model.AlturaRel,
            ModoAncora = model.ModoAncora,
            TextoAncora = model.TextoAncora ?? string.Empty,
            LarguraExtracaoRel = model.LarguraExtracaoRel,
            AlturaExtracaoRel = model.AlturaExtracaoRel,
            NomeExibido = model.Metadado.NomeExibido,
            ChaveTecnica = model.Metadado.ChaveTecnica,
            TipoEsperado = model.Metadado.TipoEsperado,
            ExemploEsperado = model.Metadado.ExemploEsperado ?? string.Empty,
            RegraNormalizacao = model.Metadado.RegraNormalizacao ?? string.Empty,
            ZoomPreview = Math.Clamp(zoomPreview, 0.50, 4.00),
            HighlightOpacity = Math.Clamp(highlightOpacity, 0.30, 0.45)
        };
    }

    private void AtualizarPropsPreview()
    {
        OnPropertyChanged(nameof(PosicaoResumo));
        OnPropertyChanged(nameof(PreviewX));
        OnPropertyChanged(nameof(PreviewY));
        OnPropertyChanged(nameof(PreviewLargura));
        OnPropertyChanged(nameof(PreviewAltura));
    }
}

public sealed record TextoDestaqueSmartItem(
    double PreviewX, double PreviewY,
    double PreviewLargura, double PreviewAltura,
    string CorHexBackground);
