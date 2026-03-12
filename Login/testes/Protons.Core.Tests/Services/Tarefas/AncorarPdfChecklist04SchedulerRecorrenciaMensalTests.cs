using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// Testes de aceite para calculo de proxima ocorrencia de recorrencia mensal.
/// Cobre: dia normal, fallback de ultimo dia do mes (31→30/28/29), fevereiro,
/// anos bissextos, precisao de segundos e integracao com DST.
/// </summary>
[Trait("Checklist", "C4")]
[Trait("Category", "C4_G_RecorrenciaMensal")]
public sealed class AncorarPdfChecklist04SchedulerRecorrenciaMensalTests
{
    private const string TzUtc       = "UTC";
    private const string TzNewYork   = "America/New_York";
    private const string TzSaoPaulo  = "America/Sao_Paulo";

    // ------------------------------------------------------------------
    // Fallback de dia (requisito: usar ultimo dia do mes)
    // ------------------------------------------------------------------

    [Fact]
    public void Mensal_Dia31_EmAbril_DeveFallbackParaDia30()
    {
        // Tarefa configurada para dia 31, vence em marco (31 dias).
        // Proximo mes = abril (30 dias) → dia = 30 (ultimo dia).
        var vencimentoUtc = new DateTime(2026, 3, 31, 10, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            agendamentoSegundo: 0,
            diaRecorrenciaMensal: 31);

        proximo.Should().NotBeNull();
        proximo!.Value.Day.Should().Be(30, "abril tem 30 dias; fallback do dia 31 deve ser dia 30");
        proximo.Value.Month.Should().Be(4);
        proximo.Value.Year.Should().Be(2026);
    }

    [Fact]
    public void Mensal_Dia31_EmJaneiro_DeveFallbackParaDia28EmFevereiroNaoBissexto()
    {
        // Tarefa configurada para dia 31. Jan→Fev (2026 nao e bissexto) → dia 28.
        var vencimentoUtc = new DateTime(2026, 1, 31, 8, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 31);

        proximo.Should().NotBeNull();
        proximo!.Value.Day.Should().Be(28, "fevereiro 2026 nao e bissexto: fallback do dia 31 e o dia 28");
        proximo.Value.Month.Should().Be(2);
        proximo.Value.Year.Should().Be(2026);
    }

    [Fact]
    public void Mensal_Dia31_EmJaneiro_BissextoDeveFallbackParaDia29()
    {
        // 2028 e ano bissexto.
        var vencimentoUtc = new DateTime(2028, 1, 31, 8, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 31);

        proximo.Should().NotBeNull();
        proximo!.Value.Day.Should().Be(29, "fevereiro 2028 e bissexto: fallback do dia 31 e o dia 29");
        proximo.Value.Month.Should().Be(2);
        proximo.Value.Year.Should().Be(2028);
    }

    [Fact]
    public void Mensal_Dia30_EmFevereiro_BissextoDeveFallbackParaDia29()
    {
        var vencimentoUtc = new DateTime(2028, 1, 30, 12, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 30);

        proximo.Should().NotBeNull();
        proximo!.Value.Day.Should().Be(29, "fevereiro 2028 tem 29 dias; fallback do dia 30 e o dia 29");
        proximo.Value.Month.Should().Be(2);
    }

    [Fact]
    public void Mensal_Dia29_EmFevereiro_NaoBissextoDeveFallbackParaDia28()
    {
        var vencimentoUtc = new DateTime(2026, 1, 29, 12, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 29);

        proximo.Should().NotBeNull();
        proximo!.Value.Day.Should().Be(28, "fevereiro 2026 nao e bissexto: fallback do dia 29 e o dia 28");
        proximo.Value.Month.Should().Be(2);
    }

    // ------------------------------------------------------------------
    // Dia normal (sem fallback)
    // ------------------------------------------------------------------

    [Fact]
    public void Mensal_Dia15_SempreRetornaDia15()
    {
        // Dia 15 existe em todos os meses — nunca deve aplicar fallback.
        var vencimentoUtc = new DateTime(2026, 1, 15, 14, 30, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 15);

        proximo!.Value.Day.Should().Be(15);
        proximo.Value.Month.Should().Be(2);
        proximo.Value.Year.Should().Be(2026);
    }

    [Fact]
    public void Mensal_Dezembro_ProximoMesEJaneiroDoAnoSeguinte()
    {
        var vencimentoUtc = new DateTime(2026, 12, 10, 9, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            diaRecorrenciaMensal: 10);

        proximo.Should().NotBeNull();
        proximo!.Value.Month.Should().Be(1, "proximo mes apos dezembro e janeiro do ano seguinte");
        proximo.Value.Year.Should().Be(2027);
        proximo.Value.Day.Should().Be(10);
    }

    // ------------------------------------------------------------------
    // Precisao de segundos
    // ------------------------------------------------------------------

    [Fact]
    public void Mensal_AgendamentoSegundo_EPreservadoNaProximaOcorrencia()
    {
        var vencimentoUtc = new DateTime(2026, 3, 15, 10, 30, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzUtc,
            agendamentoSegundo: 45,
            diaRecorrenciaMensal: 15);

        proximo!.Value.Second.Should().Be(45,
            "o AgendamentoSegundo deve ser preservado na proxima ocorrencia mensal");
        proximo.Value.Minute.Should().Be(30);
        proximo.Value.Hour.Should().Be(10);
    }

    // ------------------------------------------------------------------
    // Recorrencia Nenhuma deve retornar null
    // ------------------------------------------------------------------

    [Fact]
    public void Nenhuma_RetornaNull()
    {
        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            DateTime.UtcNow,
            TarefaRecorrencia.Nenhuma,
            TzUtc);

        proximo.Should().BeNull("recorrencia Nenhuma nao deve calcular proxima ocorrencia");
    }

    // ------------------------------------------------------------------
    // Outros tipos de recorrencia (sanidade)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(TarefaRecorrencia.Diaria,  1,  0)]
    [InlineData(TarefaRecorrencia.Semanal, 7,  0)]
    [InlineData(TarefaRecorrencia.Horaria, 0,  1)]
    public void OutrasRecorrencias_IncrementamPeriodoCorreto(
        TarefaRecorrencia recorrencia, int diasEsperados, int horasEsperadas)
    {
        var base_ = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            base_, recorrencia, TzUtc);

        proximo.Should().NotBeNull();

        var esperado = base_
            .AddDays(diasEsperados)
            .AddHours(horasEsperadas);

        proximo!.Value.Should().Be(esperado,
            $"recorrencia {recorrencia} deve incrementar {diasEsperados}d + {horasEsperadas}h");
    }

    // ------------------------------------------------------------------
    // Integracao: mensal + DST (America/New_York)
    // ------------------------------------------------------------------

    [Fact]
    public void Mensal_ComTimezone_ConvertidoCorretamenteParaUtc()
    {
        // Tarefa em America/New_York, dia 15 de cada mes as 10:00 local.
        // Vencimento atual: 15 jan 2026 10:00 EST = 15:00 UTC
        // Proximo: 15 fev 2026 10:00 EST = 15:00 UTC (ainda inverno)
        var vencimentoUtc = new DateTime(2026, 1, 15, 15, 0, 0, DateTimeKind.Utc);

        var proximo = AncorarPdfScheduleCalculator.CalcularProximaOcorrencia(
            vencimentoUtc,
            TarefaRecorrencia.Mensal,
            TzNewYork,
            diaRecorrenciaMensal: 15);

        proximo.Should().NotBeNull();
        proximo!.Value.Month.Should().Be(2);
        proximo.Value.Day.Should().Be(15);
        // Fev 2026 ainda e inverno (EST, UTC-5) → 10:00 EST = 15:00 UTC
        proximo.Value.Hour.Should().Be(15, "fevereiro ainda e EST (-5): 10:00 local = 15:00 UTC");
    }
}
