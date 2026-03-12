using System.Text.RegularExpressions;
using Protons.Core.Clientes.Repositories;
using Protons.Core.Clientes.Validation;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

// Valida se o PDF contém CPF/CNPJ coerente com o documento oficial do cliente.
// Estratégia enterprise:
// 1) Resolve o cliente por Id no repositório;
// 2) Obtém e valida o documento oficial (CPF/CNPJ com dígitos verificadores);
// 3) Extrai documentos do PDF e valida candidatos;
// 4) Exige match exato com o documento oficial (sem substring por clienteId).
public sealed class AncorarPdfValidadorClienteRegex : IAncorarPdfValidadorCliente
{
    private readonly IClienteRepository _clientes;

    public AncorarPdfValidadorClienteRegex(IClienteRepository clientes)
    {
        _clientes = clientes ?? throw new ArgumentNullException(nameof(clientes));
    }

    public ValidacaoClienteResultado Validar(IReadOnlyList<PdfPaginaTexto> paginas, int clienteId)
    {
        var cliente = _clientes.GetById(clienteId);
        if (cliente is null)
            return new ValidacaoClienteResultado(false, "cliente_nao_encontrado");

        var documentoCliente = DocumentoClienteValidator.ExtrairSomenteDigitos(cliente.Documento);
        if (string.IsNullOrWhiteSpace(documentoCliente))
            return new ValidacaoClienteResultado(false, "cliente_documento_nao_configurado");

        var analiseDocumentoCliente = DocumentoClienteValidator.Analisar(documentoCliente);
        if (analiseDocumentoCliente.Status != StatusAnaliseDocumentoCliente.Valido)
            return new ValidacaoClienteResultado(false, "cliente_documento_invalido");

        var textoCompleto = string.Join(" ", paginas.SelectMany(p => p.Palavras.Select(w => w.Texto)));

        var documentosEncontrados = new List<string>();
        foreach (Match m in AncorarPdfRegexCatalog.CpfPattern().Matches(textoCompleto))
            documentosEncontrados.Add(AncorarPdfRegexCatalog.Normalizar(m.Value));
        foreach (Match m in AncorarPdfRegexCatalog.CnpjPattern().Matches(textoCompleto))
            documentosEncontrados.Add(AncorarPdfRegexCatalog.Normalizar(m.Value));

        if (documentosEncontrados.Count == 0)
            return new ValidacaoClienteResultado(false, "cpf_cnpj_nao_encontrado");

        var documentosValidos = documentosEncontrados
            .Select(DocumentoClienteValidator.ExtrairSomenteDigitos)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.Ordinal)
            .Where(d =>
            {
                var analise = DocumentoClienteValidator.Analisar(d);
                return analise.Status == StatusAnaliseDocumentoCliente.Valido;
            })
            .ToList();

        if (documentosValidos.Count == 0)
            return new ValidacaoClienteResultado(false, "cpf_cnpj_invalido_no_pdf");

        var bateu = documentosValidos.Any(d =>
            string.Equals(d, documentoCliente, StringComparison.Ordinal));

        return bateu
            ? new ValidacaoClienteResultado(true)
            : new ValidacaoClienteResultado(false, "cliente_nao_validado");
    }
}
