using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

// Valida se o PDF contém CPF/CNPJ coerente com o documento oficial do cliente.
// Resultados possíveis:
//   Valido=true  → CPF/CNPJ válido encontrado e bate exatamente com o documento do cliente
//   Motivo="cpf_cnpj_nao_encontrado" → nenhum CPF/CNPJ detectado no texto
//   Motivo="cliente_nao_validado"    → CPF/CNPJ detectado, porém sem match exato com o documento do cliente
public interface IAncorarPdfValidadorCliente
{
    ValidacaoClienteResultado Validar(IReadOnlyList<PdfPaginaTexto> paginas, int clienteId);
}
