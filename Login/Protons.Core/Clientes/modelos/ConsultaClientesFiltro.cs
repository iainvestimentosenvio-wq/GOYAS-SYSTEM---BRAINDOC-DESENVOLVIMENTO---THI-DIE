namespace Protons.Core.Clientes.Models;

public sealed class ConsultaClientesFiltro
{
    public const int PaginaMinima = 1;
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    public string? Termo { get; init; }
    public int Pagina { get; init; } = PaginaMinima;
    public int TamanhoPagina { get; init; } = TamanhoPadrao;

    public static ConsultaClientesFiltro Criar(string? termo, int pagina, int tamanhoPagina)
    {
        var termoNormalizado = string.IsNullOrWhiteSpace(termo) ? null : termo.Trim();
        var paginaNormalizada = pagina < PaginaMinima ? PaginaMinima : pagina;
        var tamanhoNormalizado = tamanhoPagina <= 0 ? TamanhoPadrao : tamanhoPagina;
        if (tamanhoNormalizado > TamanhoMaximo)
            tamanhoNormalizado = TamanhoMaximo;

        return new ConsultaClientesFiltro
        {
            Termo = termoNormalizado,
            Pagina = paginaNormalizada,
            TamanhoPagina = tamanhoNormalizado
        };
    }
}
