using FluentAssertions;
using Moq;
using Protons.Core.Tarefas.Services;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Interface;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// Matriz de testes de timezone/DST para o binding DataPicker + Reset() do AncorarPdfConfiguracaoViewModel.
///
/// Objetivo: prevenir regressao de binding de data em producao cobrindo multiplos fusos e edge cases.
/// - DatePicker (DataSelecionada: DateTimeOffset? date-only offset=0) deve funcionar sem excecao em
///   qualquer timezone.
/// - Reset() deve usar a data/hora LOCAL do timezone da maquina (via TimeProvider), nao UTC.
/// - DataSelecionada setter deve normalizar offset para 0 e extrair apenas Y/M/D.
///
/// Timezones cobertos:
///   UTC              — offset=0, sem DST.
///   America/Sao_Paulo— BRT UTC-3 fixo (sem DST desde 2019).
///   Europe/Berlin    — CET UTC+1 / CEST UTC+2 (tem DST).
///
/// Auditoria ref: Item 12 — "Fortalecer testes de UI/data binding e timezone matrix".
/// </summary>
[Trait("Checklist", "C13")]
[Trait("Category", "C13_G3_TzUi")]
public sealed class AncorarPdfChecklist13ViewModelTimezoneMatrixTests
{
    // ------------------------------------------------------------------
    // Helpers de fuso
    // ------------------------------------------------------------------

    private static TimeZoneInfo TzUtc() => TimeZoneInfo.Utc;

    private static TimeZoneInfo TzSaoPaulo() =>
        TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private static TimeZoneInfo TzBerlin() =>
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private static AncorarPdfConfiguracaoViewModel CriarVm(
        DateTimeOffset utcNow,
        TimeZoneInfo timezone)
    {
        var service = new Mock<IAncorarPdfConfiguracaoService>(MockBehavior.Strict);
        return new AncorarPdfConfiguracaoViewModel(
            service.Object,
            new AncorarPdfNullFilePicker(),
            () => { },
            (_, _) => { },
            (_, _, _, _) => { },
            onSaveSucesso: null,
            timeProvider: new FixedTimeProvider(utcNow, timezone));
    }

    // ------------------------------------------------------------------
    // VT01–VT02: Reset() com UTC (offset=0)
    // ------------------------------------------------------------------

    [Fact]
    public void VT01_Reset_UTC_DataSelecionadaUsaDataUtcEHoraUtc()
    {
        // UTC: local = UTC; data e hora devem ser exatamente o instante fornecido.
        var utcNow = new DateTimeOffset(2026, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());

        vm.Reset();

        vm.DataSelecionada.Should().NotBeNull();
        vm.DataSelecionada!.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Month.Should().Be(6);
        vm.DataSelecionada.Value.Day.Should().Be(15);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero,
            "DataSelecionada e sempre date-only com offset=0");
        vm.HoraSelecionada.Should().Be(14);
        vm.MinutoSelecionado.Should().Be(30);
    }

    [Fact]
    public void VT02_Reset_UTC_MeiaDia_HoraSelecionadaCorreta()
    {
        // 00:01:00 UTC: hora=0, minuto=1
        var utcNow = new DateTimeOffset(2026, 1, 1, 0, 1, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());

        vm.Reset();

        vm.HoraSelecionada.Should().Be(0);
        vm.MinutoSelecionado.Should().Be(1);
        vm.DataSelecionada!.Value.Day.Should().Be(1);
        vm.DataSelecionada.Value.Month.Should().Be(1);
    }

    // ------------------------------------------------------------------
    // VT03–VT05: Reset() com America/Sao_Paulo (BRT UTC-3)
    // ------------------------------------------------------------------

    [Fact]
    public void VT03_Reset_SaoPaulo_DataSelecionadaUsaHoraBrt()
    {
        // UTC 18:30 → BRT 15:30 (UTC-3)
        var utcNow = new DateTimeOffset(2026, 6, 15, 18, 30, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzSaoPaulo());

        vm.Reset();

        vm.DataSelecionada!.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Month.Should().Be(6);
        vm.DataSelecionada.Value.Day.Should().Be(15);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero);
        vm.HoraSelecionada.Should().Be(15, "18h UTC - 3h = 15h BRT");
        vm.MinutoSelecionado.Should().Be(30);
    }

    [Fact]
    public void VT04_Reset_SaoPaulo_PertoDaMeiaNoite_DataAnteriorAoUtc()
    {
        // UTC 02:00 → BRT 23:00 do DIA ANTERIOR (02h - 3h = -1h → dia anterior 23h)
        var utcNow = new DateTimeOffset(2026, 3, 15, 2, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzSaoPaulo());

        vm.Reset();

        vm.DataSelecionada!.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Month.Should().Be(3);
        vm.DataSelecionada.Value.Day.Should().Be(14, "UTC 02:00 no dia 15 = BRT 23:00 no dia 14");
        vm.HoraSelecionada.Should().Be(23);
        vm.MinutoSelecionado.Should().Be(0);
    }

    [Fact]
    public void VT05_Reset_SaoPaulo_OutubroHistoricoSemDst_DataEHoraCorretas()
    {
        // Outubro era mes de DST historico; desde 2019 nao tem. Verifica ausencia de salto.
        // UTC 08:00 → BRT 05:00 (UTC-3)
        var utcNow = new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzSaoPaulo());

        vm.Reset();

        vm.HoraSelecionada.Should().Be(5, "America/Sao_Paulo nao tem DST em 2026; offset fixo UTC-3");
        vm.DataSelecionada!.Value.Day.Should().Be(18);
    }

    // ------------------------------------------------------------------
    // VT06–VT08: Reset() com Europe/Berlin (CET/CEST)
    // ------------------------------------------------------------------

    [Fact]
    public void VT06_Reset_Berlin_InvernoCET_DataSelecionadaCorreta()
    {
        // Janeiro = CET (UTC+1). UTC 21:45 → CET 22:45
        var utcNow = new DateTimeOffset(2026, 1, 20, 21, 45, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzBerlin());

        vm.Reset();

        vm.DataSelecionada!.Value.Day.Should().Be(20);
        vm.DataSelecionada.Value.Month.Should().Be(1);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero);
        vm.HoraSelecionada.Should().Be(22, "UTC+1 em janeiro (CET): 21h UTC + 1h = 22h CET");
        vm.MinutoSelecionado.Should().Be(45);
    }

    [Fact]
    public void VT07_Reset_Berlin_VeraoCEST_DataSelecionadaCorreta()
    {
        // Julho = CEST (UTC+2). UTC 20:00 → CEST 22:00
        var utcNow = new DateTimeOffset(2026, 7, 15, 20, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzBerlin());

        vm.Reset();

        vm.DataSelecionada!.Value.Day.Should().Be(15);
        vm.DataSelecionada.Value.Month.Should().Be(7);
        vm.HoraSelecionada.Should().Be(22, "UTC+2 em julho (CEST): 20h UTC + 2h = 22h CEST");
        vm.MinutoSelecionado.Should().Be(0);
    }

    [Fact]
    public void VT08_Reset_Berlin_CruzarMeiaNoteParaProximoDia()
    {
        // UTC 23:30 em CET (UTC+1) = 00:30 do dia seguinte local
        var utcNow = new DateTimeOffset(2026, 1, 15, 23, 30, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzBerlin());

        vm.Reset();

        vm.DataSelecionada!.Value.Day.Should().Be(16,
            "UTC 23:30 do dia 15 = CET 00:30 do dia 16; DataSelecionada deve usar data local");
        vm.DataSelecionada.Value.Month.Should().Be(1);
        vm.HoraSelecionada.Should().Be(0);
        vm.MinutoSelecionado.Should().Be(30);
    }

    // ------------------------------------------------------------------
    // VT09–VT12: DataSelecionada setter — normalizacao e edge cases
    // ------------------------------------------------------------------

    [Fact]
    public void VT09_DataSelecionada_SetterComOffsetPositivo_NormalizaParaOffsetZero()
    {
        // DateTimeOffset com UTC+5:30 (India): setter deve normalizar offset para 0
        var utcNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());
        vm.Reset();

        var dataComOffset = new DateTimeOffset(2026, 8, 10, 14, 30, 0, TimeSpan.FromMinutes(330));
        vm.DataSelecionada = dataComOffset;

        vm.DataSelecionada.Should().NotBeNull();
        vm.DataSelecionada!.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Month.Should().Be(8);
        vm.DataSelecionada.Value.Day.Should().Be(10);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero,
            "setter de DataSelecionada sempre normaliza offset para 0 (contrato date-only)");
        vm.DataSelecionada.Value.Hour.Should().Be(0);
        vm.DataSelecionada.Value.Minute.Should().Be(0);
    }

    [Fact]
    public void VT10_DataSelecionada_SetterComOffsetNegativo_NormalizaParaOffsetZero()
    {
        // DateTimeOffset com UTC-8 (US Pacific): setter deve normalizar offset para 0
        var utcNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());
        vm.Reset();

        var dataComOffset = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.FromHours(-8));
        vm.DataSelecionada = dataComOffset;

        vm.DataSelecionada!.Value.Day.Should().Be(20);
        vm.DataSelecionada.Value.Month.Should().Be(11);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero);
        vm.DataSelecionada.Value.Hour.Should().Be(0);
    }

    [Fact]
    public void VT11_DataSelecionada_SetterComNull_NaoLancaEUsaFallback()
    {
        // Setter com null: sem excecao e DataSelecionada recebe fallback (today)
        var utcNow = new DateTimeOffset(2026, 4, 5, 10, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());
        vm.Reset();

        Action act = () => { vm.DataSelecionada = null; };

        act.Should().NotThrow("setter com null deve usar fallback sem lancar excecao");
        vm.DataSelecionada.Should().NotBeNull(
            "apos setter null, DataSelecionada recebe fallback (DateTime.Today)");
        vm.DataSelecionada!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void VT12_DataSelecionada_BindingBidirecional_RoundTrip()
    {
        // Simula o ciclo set-get do DatePicker (binding bidirecional):
        // Picker chama setter com valor, getter deve retornar Y/M/D consistente.
        var utcNow = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var vm = CriarVm(utcNow, TzUtc());
        vm.Reset();

        // Round-trip 1
        vm.DataSelecionada = new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.Zero);
        vm.DataSelecionada!.Value.Day.Should().Be(20);
        vm.DataSelecionada.Value.Month.Should().Be(4);
        vm.DataSelecionada.Value.Year.Should().Be(2026);

        // Round-trip 2: data diferente
        vm.DataSelecionada = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        vm.DataSelecionada!.Value.Day.Should().Be(31,
            "setter extrai apenas data; 23:59:59 nao deve avancar para o dia seguinte");
        vm.DataSelecionada.Value.Month.Should().Be(12);
        vm.DataSelecionada.Value.Year.Should().Be(2026);
        vm.DataSelecionada.Value.Hour.Should().Be(0);
        vm.DataSelecionada.Value.Minute.Should().Be(0);

        // Round-trip 3: setter com offset nao-zero (simula DatePicker em timezone diferente)
        vm.DataSelecionada = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.FromHours(3));
        vm.DataSelecionada!.Value.Day.Should().Be(15);
        vm.DataSelecionada.Value.Month.Should().Be(6);
        vm.DataSelecionada.Value.Offset.Should().Be(TimeSpan.Zero,
            "independente do offset de entrada, getter retorna offset=0");
    }

    // ------------------------------------------------------------------
    // Classe auxiliar
    // ------------------------------------------------------------------

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;
        private readonly TimeZoneInfo _localTimeZone;

        public FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo localTimeZone)
        {
            _utcNow = utcNow;
            _localTimeZone = localTimeZone;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override TimeZoneInfo LocalTimeZone => _localTimeZone;
    }
}
