namespace Protons.Core.Clientes.Models;

public sealed class PaginacaoResultado<T>
{
    public required IReadOnlyList<T> Itens { get; init; }
    public required int PaginaAtual { get; init; }
    public required int TamanhoPagina { get; init; }
    public required int TotalItens { get; init; }
    public string? Termo { get; init; }

    public int TotalPaginas => TotalItens <= 0 ? 1 : (int)Math.Ceiling(TotalItens / (double)TamanhoPagina);
    public bool TemPaginaAnterior => PaginaAtual > 1;
    public bool TemProximaPagina => PaginaAtual < TotalPaginas;
}
