using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Protons.Core.Tarefas.Services;

namespace Protons.UI.Configuration;

internal sealed class AppSettingsAncorarPdfPathPolicy : IAncorarPdfPathPolicy
{
    private readonly IReadOnlyList<string> _allowedNetworkRoots;
    private readonly StringComparison _pathComparison;

    public AppSettingsAncorarPdfPathPolicy(IEnumerable<string>? allowedNetworkRoots)
    {
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        _allowedNetworkRoots = (allowedNetworkRoots ?? [])
            .Select(NormalizarPath)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(Comparer())
            .Select(GarantirSeparadorFinal)
            .ToArray();
    }

    public PathPolicyResult Validar(string caminho, AncorarPdfPathTipo tipo, int clienteId)
    {
        if (string.IsNullOrWhiteSpace(caminho))
            return PathPolicyResult.Falha("Caminho obrigatório para configuração do ancorar_pdf.");

        var caminhoNormalizado = NormalizarPath(caminho);
        if (string.IsNullOrWhiteSpace(caminhoNormalizado))
            return PathPolicyResult.Falha("Caminho inválido para configuração do ancorar_pdf.");

        if (EhPathRede(caminhoNormalizado) && !EstaDentroAllowlistRede(caminhoNormalizado))
        {
            return PathPolicyResult.Falha(
                "Caminho de rede não autorizado para ancorar_pdf. Ajuste a allowlist em appsettings.");
        }

        // Pasta monitorada: aceita qualquer caminho válido; a pasta pode ser criada no futuro quando a tarefa rodar.
        // Não exige Directory.Exists — o usuário pode selecionar qualquer pasta do computador.

        if (tipo == AncorarPdfPathTipo.PdfModelo && !File.Exists(caminhoNormalizado))
            return PathPolicyResult.Falha("PDF modelo não existe ou está inacessível.");

        return PathPolicyResult.Ok();
    }

    private bool EstaDentroAllowlistRede(string caminho)
    {
        if (_allowedNetworkRoots.Count == 0)
            return false;

        var normalizado = GarantirSeparadorFinal(caminho);
        foreach (var root in _allowedNetworkRoots)
        {
            if (normalizado.StartsWith(root, _pathComparison))
                return true;
        }

        return false;
    }

    private static bool EhPathRede(string caminho)
    {
        return caminho.StartsWith(@"\\", StringComparison.Ordinal) ||
               caminho.StartsWith("//", StringComparison.Ordinal);
    }

    private static string NormalizarPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GarantirSeparadorFinal(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
            return path;

        return $"{path}{Path.DirectorySeparatorChar}";
    }

    private IEqualityComparer<string> Comparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
