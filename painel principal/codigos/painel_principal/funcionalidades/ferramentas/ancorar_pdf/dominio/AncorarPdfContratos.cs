using System;
using System.Collections.Generic;
using System.Linq;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;

public enum AncorarPdfRecorrencia
{
    Unica = 0,
    Diaria = 1,
    Semanal = 2,
    Mensal = 3
}

public enum AncorarPdfExecucaoStatus
{
    Agendada = 0,
    Executando = 1,
    Concluida = 2,
    Falhou = 3,
    Cancelada = 4,
    Atrasada = 5
}

public sealed record NomeEsperadoPdfConfig(
    string NomeLogico,
    double LimiarSimilaridade = 0.75);

public sealed record AncorarPdfConfig
{
    public int ClienteId { get; init; }
    public int EsteiraId { get; init; }
    public string NomeTarefa { get; init; } = string.Empty;
    public AncorarPdfRecorrencia Recorrencia { get; init; } = AncorarPdfRecorrencia.Unica;
    public string TimezoneLocal { get; init; } = TimeZoneInfo.Local.Id;
    public bool MonitorarSubpastas { get; init; } = false;
    public List<NomeEsperadoPdfConfig> NomesEsperados { get; init; } = new();
    public double LimiarSimilaridadeNome { get; init; } = 0.75;
    public bool ValidacaoClienteAtiva { get; init; } = true;
    public int RetryTentativas { get; init; } = 3;
    public List<int> RetryBackoffSegundos { get; init; } = new() { 5, 20, 60 };
    public int ProgramadoPorUserId { get; init; }
    public string ProgramadoPorNome { get; init; } = string.Empty;
    public DateTime ProgramadoEmUtc { get; init; }
    public DateTime ProgramadoEmLocal { get; init; }
}

public sealed record AncorarPdfExecucaoCiclo
{
    public string CicloId { get; init; } = string.Empty;
    public int TarefaId { get; init; }
    public DateTime JanelaAlvoUtc { get; init; }
    public AncorarPdfExecucaoStatus Status { get; init; } = AncorarPdfExecucaoStatus.Agendada;
    public bool ExecutadaComAtraso { get; init; }
    public string? ErroCodigo { get; init; }
    public string? ErroDetalhe { get; init; }
}

public sealed record ArquivoProcessadoCiclo
{
    public int TarefaId { get; init; }
    public string CicloId { get; init; } = string.Empty;
    public string NomeEsperadoLogico { get; init; } = string.Empty;
    public string ArquivoHash { get; init; } = string.Empty;
    public string ArquivoPath { get; init; } = string.Empty;
    public long TamanhoBytes { get; init; }
    public DateTime MtimeUtc { get; init; }
}

public sealed record PoliticaAcessoTarefa(
    bool PodeVisualizar,
    bool PodeEditar,
    bool PodeDuplicar,
    bool PodeExcluir);

// IDs da ferramenta: use FerramentaTarefaIds em Protons.Core.Tarefas.Models (fonte unica de verdade).

public sealed record CandidatoArquivoNomeAproximado(
    string NomeEsperadoLogico,
    string ArquivoPath,
    double Similaridade,
    DateTime MtimeUtc);

public static class SelecaoNomeAproximadoPorCiclo
{
    // Ordenacao deterministica: similaridade desc, mtime desc, path asc.
    public static CandidatoArquivoNomeAproximado? SelecionarMelhor(
        IEnumerable<CandidatoArquivoNomeAproximado> candidatos,
        double limiarSimilaridade)
    {
        return candidatos
            .Where(c => c.Similaridade >= limiarSimilaridade)
            .OrderByDescending(c => c.Similaridade)
            .ThenByDescending(c => c.MtimeUtc)
            .ThenBy(c => c.ArquivoPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    // Regra de idempotencia por ciclo: um nome esperado processa no maximo uma vez.
    public static bool PodeProcessarNoCiclo(bool nomeEsperadoJaProcessadoNoCiclo)
    {
        return !nomeEsperadoJaProcessadoNoCiclo;
    }
}
