using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// Testes de aceite para o tratamento de transicoes DST no AncorarPdfScheduleCalculator.
///
/// Timezone: America/New_York (EST UTC-5 / EDT UTC-4)
/// Spring forward 2026: 8 mar 2026, 02:00 EST → 03:00 EDT (gap de 02:00-02:59)
/// Fall back    2026: 1 nov 2026, 02:00 EDT → 01:00 EST (overlap de 01:00-01:59)
/// </summary>
[Trait("Checklist", "C4")]
[Trait("Category", "C4_G_Dst")]
public sealed class AncorarPdfChecklist04SchedulerDstTests
{
    private const string TzNewYork = "America/New_York";

    // ------------------------------------------------------------------
    // Conversao de horario normal (sem DST)
    // ------------------------------------------------------------------

    [Fact]
    public void Resolver_HorarioNormal_VeraoEDT_RetornaUtcCorreto()
    {
        // 15 jul 2026: New York esta em EDT (UTC-4)
        // 10:30:45 local EDT → 14:30:45 UTC
        var local = new DateTime(2026, 7, 15, 10, 30, 45);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            local, TzNewYork);

        utc.Should().Be(new DateTime(2026, 7, 15, 14, 30, 45, DateTimeKind.Utc));
        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Resolver_HorarioNormal_InvernoEST_RetornaUtcCorreto()
    {
        // 15 jan 2026: New York esta em EST (UTC-5)
        // 09:00:00 local EST → 14:00:00 UTC
        var local = new DateTime(2026, 1, 15, 9, 0, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            local, TzNewYork);

        utc.Should().Be(new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc));
        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
    }

    // ------------------------------------------------------------------
    // Spring forward 2026 — 8 mar 2026 02:00 EST → 03:00 EDT
    // Horario invalido: qualquer hora entre 02:00:00 e 02:59:59
    // ------------------------------------------------------------------

    [Fact]
    public void Resolver_SpringForward_HorarioInvalido_AvancarParaProximoValido()
    {
        // 02:30 nao existe em 8 mar 2026 (New York spring-forward)
        // Politica Avancar: 02:30 + 1h = 03:30 EDT → 03:30 - 4h = 07:30 UTC
        var localInvalido = new DateTime(2026, 3, 8, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzNewYork,
            AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido);

        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoAvancado,
            "horario invalido deve retornar InvalidoAvancado quando politica e Avancar");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 3, 8, 7, 30, 0, DateTimeKind.Utc));
        utc.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Resolver_SpringForward_HorarioInvalido_RecuarParaAnteriorValido()
    {
        // 02:30 nao existe → Recuar: 02:30 - 1h = 01:30 EST → 01:30 + 5h = 06:30 UTC
        var localInvalido = new DateTime(2026, 3, 8, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzNewYork,
            AncorarPdfDstHorarioInvalidoPolicy.RecuarParaHorarioValidoAnterior);

        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoRecuado);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Resolver_SpringForward_HorarioInvalido_PularOcorrencia_RetornaNull()
    {
        // 02:30 nao existe → Pular: resultado deve ser null
        var localInvalido = new DateTime(2026, 3, 8, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzNewYork,
            AncorarPdfDstHorarioInvalidoPolicy.PularOcorrenciaComAuditoria);

        utc.Should().BeNull("politica Pular deve retornar null para horario invalido DST");
        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoPulado);
    }

    [Fact]
    public void Resolver_SpringForward_LimiteInferiorGap_InvalidioBorderCase()
    {
        // 02:00:00 e o primeiro segundo invalido durante spring-forward
        var limite = new DateTime(2026, 3, 8, 2, 0, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            limite,
            TzNewYork,
            AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido);

        // 02:00 + 1h = 03:00 EDT = 07:00 UTC
        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoAvancado);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 3, 8, 7, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------
    // Fall back 2026 — 1 nov 2026 02:00 EDT → 01:00 EST
    // Horario ambiguo: qualquer hora entre 01:00:00 e 01:59:59
    // ------------------------------------------------------------------

    [Fact]
    public void Resolver_FallBack_HorarioAmbiguo_PreferirOffsetMaisCedo_RetornaPrimeiraOcorrenciaUtc()
    {
        // 01:30 existe duas vezes em 1 nov 2026
        // PreferirOffsetMaisCedo = primeira ocorrencia em UTC = horario de verao EDT (-4)
        // 01:30 local - (-4h) = 01:30 + 4h = 05:30 UTC
        var localAmbiguo = new DateTime(2026, 11, 1, 1, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo,
            TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);

        resolucao.Should().Be(AncorarPdfDstResolucao.AmbiguoResolvido,
            "horario ambiguo deve ser resolvido pela politica");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc),
            "PreferirOffsetMaisCedo deve resultar na primeira ocorrencia UTC (EDT, 05:30)");
    }

    [Fact]
    public void Resolver_FallBack_HorarioAmbiguo_PreferirOffsetMaisTarde_RetornaSegundaOcorrenciaUtc()
    {
        // PreferirOffsetMaisTarde = segunda ocorrencia em UTC = horario padrao EST (-5)
        // 01:30 local - (-5h) = 01:30 + 5h = 06:30 UTC
        var localAmbiguo = new DateTime(2026, 11, 1, 1, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo,
            TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisTarde);

        resolucao.Should().Be(AncorarPdfDstResolucao.AmbiguoResolvido);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 11, 1, 6, 30, 0, DateTimeKind.Utc),
            "PreferirOffsetMaisTarde deve resultar na segunda ocorrencia UTC (EST, 06:30)");
    }

    [Fact]
    public void Resolver_FallBack_HorarioAmbiguo_ExecutarUmaVezMaisCedo_ComportaComoMaisCedo()
    {
        // ExecutarUmaVezNoOffsetMaisCedo = mesmo que PreferirOffsetMaisCedo (idempotencia via DB)
        var localAmbiguo = new DateTime(2026, 11, 1, 1, 30, 0);

        var (utcUmaVez, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo, TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.ExecutarUmaVezNoOffsetMaisCedo);

        var (utcMaisCedo, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo, TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);

        utcUmaVez.Should().Be(utcMaisCedo,
            "ExecutarUmaVezNoOffsetMaisCedo deve produzir o mesmo UTC que PreferirOffsetMaisCedo");
    }

    [Fact]
    public void Resolver_FallBack_AsPoliricasProduzemUTCsDiferentes()
    {
        // Garante que as duas politicas de ambiguidade sao distinguiveis
        var localAmbiguo = new DateTime(2026, 11, 1, 1, 0, 0);

        var (utcCedo, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo, TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);

        var (utcTarde, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo, TzNewYork,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisTarde);

        utcCedo.Should().BeBefore(utcTarde.GetValueOrDefault(),
            "as duas politicas devem produzir UTCs diferentes durante fall-back");
        (utcTarde!.Value - utcCedo!.Value).Should().Be(TimeSpan.FromHours(1));
    }

    // ------------------------------------------------------------------
    // AplicarSegundos
    // ------------------------------------------------------------------

    [Fact]
    public void AplicarSegundos_SubstituiComponenteSegundoSemAlterarResto()
    {
        var base_ = new DateTime(2026, 6, 10, 14, 30, 0);
        var resultado = AncorarPdfScheduleCalculator.AplicarSegundos(base_, 45);
        resultado.Should().Be(new DateTime(2026, 6, 10, 14, 30, 45));
    }

    [Fact]
    public void AplicarSegundos_Zero_NaoAlteraQuandoJaEhZero()
    {
        var base_ = new DateTime(2026, 6, 10, 14, 30, 0);
        var resultado = AncorarPdfScheduleCalculator.AplicarSegundos(base_, 0);
        resultado.Should().Be(base_);
    }

    [Fact]
    public void AplicarSegundos_Clampado_ValorForaDeFaixa()
    {
        var base_ = new DateTime(2026, 6, 10, 14, 30, 0);
        var r1 = AncorarPdfScheduleCalculator.AplicarSegundos(base_, -1);
        var r2 = AncorarPdfScheduleCalculator.AplicarSegundos(base_, 60);
        r1.Second.Should().Be(0);
        r2.Second.Should().Be(59);
    }

    // ------------------------------------------------------------------
    // Fallback de timezone invalido
    // ------------------------------------------------------------------

    [Fact]
    public void Resolver_TimezoneInvalido_FallbackParaUtc_NaoLancaExcecao()
    {
        var local = new DateTime(2026, 5, 1, 12, 0, 0);

        Action act = () => AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            local, "Timezone/Invalido/Inexistente");

        act.Should().NotThrow("timezone invalido deve usar UTC como fallback silencioso");
    }

    [Fact]
    public void Resolver_TimezoneNuloOuVazio_FallbackParaUtc()
    {
        var local = new DateTime(2026, 5, 1, 12, 0, 0);
        var (utc, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, "");
        utc.Should().Be(DateTime.SpecifyKind(local, DateTimeKind.Utc),
            "timezone vazio deve tratar o horario como UTC");
    }

    // ------------------------------------------------------------------
    // Precisao de segundos preservada na conversao
    // ------------------------------------------------------------------

    [Fact]
    public void Resolver_SegundosSaoPreservadosNaConversao()
    {
        // 10:30:37 local deve manter :37 no UTC resultante
        var local = new DateTime(2026, 8, 20, 10, 30, 37);
        var (utc, _) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzNewYork);
        utc!.Value.Second.Should().Be(37, "precisao de segundos deve ser preservada na conversao UTC");
    }
}
