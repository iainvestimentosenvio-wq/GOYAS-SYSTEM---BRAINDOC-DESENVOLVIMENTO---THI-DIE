using System;
using System.Collections.Generic;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

public enum AncorarPdfFerramentaInteracao
{
    Navegar = 0,
    SmartClick = 1,
    RetanguloManual = 2
}

internal static class AncorarPdfFerramentaInteracaoCatalogo
{
    public static IReadOnlyList<AncorarPdfFerramentaInteracao> Todas { get; } =
        Enum.GetValues<AncorarPdfFerramentaInteracao>();

    public static string ObterTitulo(AncorarPdfFerramentaInteracao ferramenta) => ferramenta switch
    {
        AncorarPdfFerramentaInteracao.Navegar => "Navegar",
        AncorarPdfFerramentaInteracao.SmartClick => "Smart click",
        _ => "Retângulo manual"
    };

    public static string ObterInstrucao(AncorarPdfFerramentaInteracao ferramenta) => ferramenta switch
    {
        AncorarPdfFerramentaInteracao.Navegar => "Modo navegação ativo. Use o canvas para inspecionar o PDF.",
        AncorarPdfFerramentaInteracao.SmartClick => "Smart click ativo. Clique em um texto do PDF para detectar variável.",
        _ => "Retângulo manual ativo. Arraste no PDF para criar uma âncora manual."
    };
}
