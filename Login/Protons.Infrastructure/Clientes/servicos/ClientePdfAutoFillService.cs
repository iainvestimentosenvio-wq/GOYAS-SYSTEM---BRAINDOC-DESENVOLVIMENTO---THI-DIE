using System.Text.RegularExpressions;
using Protons.Core.Clientes.Services;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Clientes.Services;

/// <summary>
/// Implementação que extrai dados de cliente de um PDF usando o extrator de texto existente
/// e regex patterns para identificar CNPJ, CPF, razão social, email, telefone, etc.
/// </summary>
public sealed class ClientePdfAutoFillService : IClientePdfAutoFillService
{
    private readonly IAncorarPdfExtratorTexto _extrator;

    public ClientePdfAutoFillService(IAncorarPdfExtratorTexto extrator)
    {
        _extrator = extrator;
    }

    public async Task<ClientePdfAutoFillResult> ExtrairDadosClienteAsync(string pdfPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return new ClientePdfAutoFillResult { Sucesso = false, Mensagem = "Arquivo PDF não encontrado." };

        IReadOnlyList<PdfPaginaTexto> paginas;
        try
        {
            paginas = await _extrator.ExtrairAsync(pdfPath, ct);
        }
        catch (Exception ex)
        {
            return new ClientePdfAutoFillResult { Sucesso = false, Mensagem = $"Erro ao ler PDF: {ex.Message}" };
        }

        if (paginas.Count == 0)
            return new ClientePdfAutoFillResult { Sucesso = false, Mensagem = "PDF sem texto extraível." };

        var textoCompleto = string.Join("\n", paginas.SelectMany(p => p.Palavras.Select(w => w.Texto)));
        var linhas = ReconstruirLinhas(paginas);

        return new ClientePdfAutoFillResult
        {
            Sucesso = true,
            Cnpj = ExtrairCnpj(textoCompleto),
            Cpf = ExtrairCpf(textoCompleto),
            RazaoSocial = ExtrairValorAposLabel(linhas, "Raz(a|ã)o Social", "Nome/Raz(a|ã)o"),
            NomeFantasia = ExtrairValorAposLabel(linhas, "Nome Fantasia", "Fantasia"),
            Email = ExtrairEmail(textoCompleto),
            Telefone = ExtrairTelefone(textoCompleto),
            Cep = ExtrairCep(textoCompleto),
            Endereco = ExtrairValorAposLabel(linhas, "Endere(c|ç)o", "Logradouro")
        };
    }

    private static string? ExtrairCnpj(string texto)
    {
        var match = Regex.Match(texto, @"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b");
        if (!match.Success) return null;
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length == 14 ? digits : null;
    }

    private static string? ExtrairCpf(string texto)
    {
        var match = Regex.Match(texto, @"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b");
        if (!match.Success) return null;
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length == 11 ? digits : null;
    }

    private static string? ExtrairEmail(string texto)
    {
        var match = Regex.Match(texto, @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b");
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static string? ExtrairTelefone(string texto)
    {
        var match = Regex.Match(texto, @"\(?\d{2}\)?\s?\d{4,5}-?\d{4}");
        if (!match.Success) return null;
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length is >= 10 and <= 11 ? digits : null;
    }

    private static string? ExtrairCep(string texto)
    {
        var match = Regex.Match(texto, @"\b\d{5}-?\d{3}\b");
        if (!match.Success) return null;
        var digits = new string(match.Value.Where(char.IsDigit).ToArray());
        return digits.Length == 8 ? digits : null;
    }

    private static string? ExtrairValorAposLabel(IReadOnlyList<string> linhas, params string[] labelsRegex)
    {
        foreach (var linha in linhas)
        {
            foreach (var label in labelsRegex)
            {
                var match = Regex.Match(linha, $@"{label}\s*:?\s*(.+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var valor = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(valor) && valor.Length >= 3)
                        return valor;
                }
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ReconstruirLinhas(IReadOnlyList<PdfPaginaTexto> paginas)
    {
        const double deltaLinha = 0.005;
        var linhas = new List<string>();

        foreach (var pagina in paginas)
        {
            var palavras = pagina.Palavras.OrderBy(p => p.Bbox.Y).ThenBy(p => p.Bbox.X).ToList();
            if (palavras.Count == 0) continue;

            var linhaAtual = new List<string> { palavras[0].Texto };
            var yBase = palavras[0].Bbox.Y;

            for (var i = 1; i < palavras.Count; i++)
            {
                if (Math.Abs(palavras[i].Bbox.Y - yBase) <= deltaLinha)
                {
                    linhaAtual.Add(palavras[i].Texto);
                }
                else
                {
                    linhas.Add(string.Join(" ", linhaAtual));
                    linhaAtual = [palavras[i].Texto];
                    yBase = palavras[i].Bbox.Y;
                }
            }
            linhas.Add(string.Join(" ", linhaAtual));
        }

        return linhas;
    }
}
