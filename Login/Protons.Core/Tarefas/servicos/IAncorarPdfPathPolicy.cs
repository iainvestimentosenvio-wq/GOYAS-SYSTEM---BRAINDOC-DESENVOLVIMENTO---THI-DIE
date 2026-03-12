using System.IO;
using System.Linq;

namespace Protons.Core.Tarefas.Services;

public enum AncorarPdfPathTipo
{
    PastaMonitorada = 0,
    PdfModelo = 1
}

public sealed record PathPolicyResult(
    bool Permitido,
    string? MensagemErro = null)
{
    public static PathPolicyResult Ok() => new(true, null);

    public static PathPolicyResult Falha(string mensagemErro) => new(false, mensagemErro);
}

public interface IAncorarPdfPathPolicy
{
    PathPolicyResult Validar(string caminho, AncorarPdfPathTipo tipo, int clienteId);
}

/// <summary>
/// Política permissiva para ambientes de desenvolvimento e testes.
/// Em produção, prefira <see cref="AncorarPdfSafePathPolicy"/>.
/// </summary>
public sealed class AncorarPdfAllowAllPathPolicy : IAncorarPdfPathPolicy
{
    public PathPolicyResult Validar(string caminho, AncorarPdfPathTipo tipo, int clienteId)
    {
        return PathPolicyResult.Ok();
    }
}

/// <summary>
/// Política de segurança que rejeita path traversal, caminhos relativos
/// e extensões incorretas para PDF modelo.
/// </summary>
public sealed class AncorarPdfSafePathPolicy : IAncorarPdfPathPolicy
{
    private static readonly char[] PathSeparators =
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public PathPolicyResult Validar(string caminho, AncorarPdfPathTipo tipo, int clienteId)
    {
        if (string.IsNullOrWhiteSpace(caminho))
            return PathPolicyResult.Falha("Caminho não pode ser vazio.");

        if (!Path.IsPathRooted(caminho))
            return PathPolicyResult.Falha("Caminho deve ser absoluto (iniciar com / ou letra de unidade).");

        var partes = caminho.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (partes.Any(p => p == ".."))
            return PathPolicyResult.Falha("Caminho não pode conter componentes de navegação (..).");

        if (tipo == AncorarPdfPathTipo.PdfModelo)
        {
            var extensao = Path.GetExtension(caminho.AsSpan());
            if (!extensao.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return PathPolicyResult.Falha("PDF modelo deve ter extensão .pdf.");
        }

        return PathPolicyResult.Ok();
    }
}
