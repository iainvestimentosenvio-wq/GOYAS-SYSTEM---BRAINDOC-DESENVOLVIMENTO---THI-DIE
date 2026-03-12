using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// Matriz de testes de timezone/DST para o AncorarPdfScheduleCalculator.
///
/// Timezones cobertos:
///   UTC              — sem DST, offset=0 fixo.
///   America/Sao_Paulo— sem DST desde 2019 (BRT UTC-3 fixo).
///   Europe/Berlin    — CET (UTC+1) inverno / CEST (UTC+2) verao.
///
/// Transicoes Europe/Berlin 2026:
///   Spring forward: 29 mar 2026, 02:00 CET → 03:00 CEST  (gap 02:00–02:59 invalido).
///   Fall back     : 25 out 2026, 03:00 CEST → 02:00 CET  (overlap 02:00–02:59 ambiguo).
///
/// Objetivo: prevenir regressao de DST em producao cobrindo casos que C4 (America/New_York) nao cobre.
/// Auditoria ref: Item 12 — "Fortalecer testes de UI/data binding e timezone matrix".
/// </summary>
[Trait("Checklist", "C13")]
[Trait("Category", "C13_G2_TzCore")]
public sealed class AncorarPdfChecklist13TimezoneMatrixCoreTests
{
    private const string TzUtc = "UTC";
    private const string TzSaoPaulo = "America/Sao_Paulo";
    private const string TzBerlin = "Europe/Berlin";

    // ------------------------------------------------------------------
    // UTC — sem DST, conversao trivial
    // ------------------------------------------------------------------

    [Fact]
    public void TZ01_Utc_HorarioNormal_ResolveSemAlteracao()
    {
        // UTC não tem offset: local == UTC
        var local = new DateTime(2026, 6, 15, 14, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzUtc);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 6, 15, 14, 30, 0, DateTimeKind.Utc));
        utc.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void TZ02_Utc_Meia_Noite_PreservaPrecisaoDeSegundos()
    {
        // 00:00:37 UTC → 00:00:37 UTC; segundos preservados
        var local = new DateTime(2026, 3, 29, 0, 0, 37);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzUtc);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc!.Value.Second.Should().Be(37);
        utc.Value.Hour.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // America/Sao_Paulo — sem DST desde 2019, BRT = UTC-3 fixo
    // ------------------------------------------------------------------

    [Fact]
    public void TZ03_SaoPaulo_Verao_NaoTemDst_ConverteCorretamente()
    {
        // Janeiro = historicamente seria verao (BRST), mas DST abolido em 2019.
        // America/Sao_Paulo deve ser BRT UTC-3 o ano todo.
        // 10:00:00 local BRT → 13:00:00 UTC
        var local = new DateTime(2026, 1, 20, 10, 0, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzSaoPaulo);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal,
            "America/Sao_Paulo nao tem DST desde 2019; todos os horarios devem ser Normal");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 1, 20, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TZ04_SaoPaulo_Inverno_ConverteCorretamente()
    {
        // Julho = historicamente inverno (BRT). Esperado UTC-3.
        // 15:45:30 BRT → 18:45:30 UTC
        var local = new DateTime(2026, 7, 10, 15, 45, 30);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzSaoPaulo);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 7, 10, 18, 45, 30, DateTimeKind.Utc));
        utc.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void TZ05_SaoPaulo_OutubroHistoricoDeVirada_AindaEhNormal()
    {
        // Outubro era o mes da transicao historica para BRST.
        // Desde 2019 nao ha mais transicao; deve retornar Normal.
        var local = new DateTime(2026, 10, 18, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzSaoPaulo);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal,
            "America/Sao_Paulo aboliu DST em 2019; outubro nao deve gerar transicao");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 10, 18, 5, 30, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------
    // Europe/Berlin — CET (UTC+1) inverno / CEST (UTC+2) verao
    // ------------------------------------------------------------------

    [Fact]
    public void TZ06_Berlin_VeraoNormal_CEST_ConverteCorretamente()
    {
        // 15 jul 2026: Berlin em CEST (UTC+2)
        // 14:00:00 CEST → 12:00:00 UTC
        var local = new DateTime(2026, 7, 15, 14, 0, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzBerlin);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TZ07_Berlin_InvernoNormal_CET_ConverteCorretamente()
    {
        // 15 jan 2026: Berlin em CET (UTC+1)
        // 09:00:00 CET → 08:00:00 UTC
        var local = new DateTime(2026, 1, 15, 9, 0, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(local, TzBerlin);

        resolucao.Should().Be(AncorarPdfDstResolucao.Normal);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc));
    }

    // ------------------------------------------------------------------
    // Europe/Berlin — Spring forward 29 mar 2026
    // 02:00 CET (01:00 UTC) salta para 03:00 CEST (01:00 UTC)
    // Gap invalido: 02:00–02:59 CET nao existe
    // ------------------------------------------------------------------

    [Fact]
    public void TZ08_Berlin_SpringForward_HorarioInvalido_AvancarPolicy()
    {
        // 02:30 invalido → Avancar: 03:30 CEST (UTC+2) = 01:30 UTC
        var localInvalido = new DateTime(2026, 3, 29, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzBerlin,
            AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido);

        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoAvancado,
            "02:30 em 29 mar 2026 nao existe em Berlin; politica Avancar deve retornar InvalidoAvancado");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TZ09_Berlin_SpringForward_HorarioInvalido_RecuarPolicy()
    {
        // 02:30 invalido → Recuar: 01:30 CET (UTC+1) = 00:30 UTC
        var localInvalido = new DateTime(2026, 3, 29, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzBerlin,
            AncorarPdfDstHorarioInvalidoPolicy.RecuarParaHorarioValidoAnterior);

        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoRecuado);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 3, 29, 0, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TZ10_Berlin_SpringForward_HorarioInvalido_PularPolicy_RetornaNull()
    {
        // 02:30 invalido → Pular: null
        var localInvalido = new DateTime(2026, 3, 29, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localInvalido,
            TzBerlin,
            AncorarPdfDstHorarioInvalidoPolicy.PularOcorrenciaComAuditoria);

        utc.Should().BeNull("politica Pular deve retornar null para horario invalido no gap DST de Berlin");
        resolucao.Should().Be(AncorarPdfDstResolucao.InvalidoPulado);
    }

    // ------------------------------------------------------------------
    // Europe/Berlin — Fall back 25 out 2026
    // 03:00 CEST (01:00 UTC) recua para 02:00 CET (01:00 UTC)
    // Ambiguo: 02:00–02:59 existe duas vezes (CEST e CET)
    // ------------------------------------------------------------------

    [Fact]
    public void TZ11_Berlin_FallBack_HorarioAmbiguo_PreferirMaisCedo_CEST()
    {
        // 02:30 ambiguo → PreferirOffsetMaisCedo = CEST (UTC+2) = 00:30 UTC (primeira ocorrencia)
        var localAmbiguo = new DateTime(2026, 10, 25, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo,
            TzBerlin,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo);

        resolucao.Should().Be(AncorarPdfDstResolucao.AmbiguoResolvido,
            "02:30 em 25 out 2026 e ambiguo em Berlin; deve ser resolvido pela politica");
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc),
            "PreferirOffsetMaisCedo = CEST (UTC+2), primeira ocorrencia = 00:30 UTC");
    }

    [Fact]
    public void TZ12_Berlin_FallBack_HorarioAmbiguo_PreferirMaisTarde_CET()
    {
        // 02:30 ambiguo → PreferirOffsetMaisTarde = CET (UTC+1) = 01:30 UTC (segunda ocorrencia)
        var localAmbiguo = new DateTime(2026, 10, 25, 2, 30, 0);

        var (utc, resolucao) = AncorarPdfScheduleCalculator.ResolverLocalParaUtc(
            localAmbiguo,
            TzBerlin,
            ambiguoPolicy: AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisTarde);

        resolucao.Should().Be(AncorarPdfDstResolucao.AmbiguoResolvido);
        utc.Should().NotBeNull();
        utc!.Value.Should().Be(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Utc),
            "PreferirOffsetMaisTarde = CET (UTC+1), segunda ocorrencia = 01:30 UTC");
        (utc.Value - new DateTime(2026, 10, 25, 0, 30, 0, DateTimeKind.Utc))
            .Should().Be(TimeSpan.FromHours(1),
            "as duas politicas de fall-back devem diferir exatamente 1h para Berlin");
    }
}
