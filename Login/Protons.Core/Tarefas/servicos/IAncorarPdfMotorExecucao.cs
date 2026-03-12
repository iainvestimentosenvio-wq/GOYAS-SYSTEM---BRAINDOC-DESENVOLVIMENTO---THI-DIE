using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

public interface IAncorarPdfMotorExecucao
{
    Task<AncorarPdfMotorResultado> ExecutarAsync(AncorarPdfFilaItem item, CancellationToken ct);
}

public sealed record AncorarPdfMotorResultado(
    bool Sucesso,
    AncorarPdfFilaFalhaCategoria Categoria,
    string? ErroCodigo = null,
    string? ErroDetalhe = null,
    AncorarPdfError? Error = null);
