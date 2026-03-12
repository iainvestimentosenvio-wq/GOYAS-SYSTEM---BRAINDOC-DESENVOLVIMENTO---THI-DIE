namespace Protons.Core.Tarefas.Models;

// Palavra extraída do PDF com bbox normalizado (0.0–1.0, origem top-left).
public sealed record PdfPalavra(string Texto, BboxRelativo Bbox);

// Página completa com todas as palavras extraídas.
public sealed record PdfPaginaTexto(
    int Numero,
    double LarguraPt,
    double AlturaPt,
    IReadOnlyList<PdfPalavra> Palavras);

// Candidato descoberto no filesystem antes de seleção pelo JaroWinkler.
public sealed record CandidatoArquivoTexto(
    string ArquivoPath,
    string NomeEsperadoLogico,
    double Similaridade,
    DateTime MtimeUtc,
    long TamanhoBytes,
    string ArquivoHash);

// Resultado de validação de cliente (CPF/CNPJ no PDF vs. ClienteId configurado).
public sealed record ValidacaoClienteResultado(bool Valido, string? Motivo = null);

// Resultado final da seleção de arquivo: pós-estabilidade e hash computado.
public sealed record SelecaoArquivoResultado(
    string ArquivoPath,
    string NomeEsperadoLogico,
    string ArquivoHash,
    long TamanhoBytes,
    DateTime MtimeUtc);
