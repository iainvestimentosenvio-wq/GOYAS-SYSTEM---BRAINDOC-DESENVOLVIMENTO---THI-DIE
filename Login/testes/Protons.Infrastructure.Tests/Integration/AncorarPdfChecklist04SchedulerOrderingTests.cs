using FluentAssertions;
using Moq;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Repositories;
using Protons.Infrastructure.Tarefas.Services;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// Testes de aceite para ordenacao deterministica do AncorarPdfSchedulerRuntime.
///
/// Requisito: quando multiplas tarefas estao vencidas no mesmo tick,
///   1. Menor PrioridadeExecucao numerico dispara primeiro (1 = mais urgente).
///   2. Em caso de empate de prioridade, menor CriadoEmUtc dispara primeiro (FIFO).
///   3. LimitePorTick e respeitado — tarefas excedentes ficam para o proximo tick.
///   4. Misfire ja sinalizado nao gera backlog duplicado no mesmo ciclo.
/// </summary>
[Trait("Checklist", "C4")]
[Trait("Category", "C4_G_Ordering")]
public sealed class AncorarPdfChecklist04SchedulerOrderingTests
{
    // Limiar alto: nenhuma tarefa vira misfire nos testes de ordenacao.
    private const int MisfireAltoS = 3600;

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static AncorarPdfSchedulerAgendamentoAtivo Agendamento(
        int tarefaId, int prioridade, DateTime criadoEmUtc, DateTime vencimentoUtc) =>
        new()
        {
            TarefaId           = tarefaId,
            ClienteId          = 1,
            VencimentoUtc      = vencimentoUtc,
            CriadoEmUtc        = criadoEmUtc,
            PrioridadeExecucao = prioridade,
            TimezoneId         = "UTC"
        };

    /// <summary>
    /// Executa exatamente o tick de startup do runtime e retorna os TarefaIds
    /// na ordem em que TryMarkAsInProgress foi invocado.
    /// </summary>
    private static IReadOnlyList<int> ExecutarUmTick(
        IReadOnlyList<AncorarPdfSchedulerAgendamentoAtivo> agendamentos,
        DateTime referenciaUtc,
        int limitePorTick = 200)
    {
        var ordem = new List<int>();
        var gate  = new SemaphoreSlim(0, agendamentos.Count);

        var mockCfg = new Mock<IAncorarPdfConfiguracaoRepository>(MockBehavior.Loose);
        mockCfg.Setup(r => r.ListarAgendamentosAtivosAte(It.IsAny<DateTime>(), It.IsAny<int>()))
               .Returns(agendamentos);
        mockCfg.Setup(r => r.RegistrarEventoScheduler(
                   It.IsAny<AncorarPdfSchedulerEventoRegistro>()));

        var mockTarefas = new Mock<ITarefaRepository>(MockBehavior.Loose);
        mockTarefas
            .Setup(r => r.TryMarkAsInProgress(It.IsAny<int>(), It.IsAny<DateTime>()))
            .Callback<int, DateTime>((id, _) =>
            {
                lock (ordem) ordem.Add(id);
                gate.Release();
            })
            .Returns(true);

        var tp = new StaticTimeProvider(referenciaUtc);

        // Intervalo longo: apenas o tick imediato de startup e executado.
        var runtime = new AncorarPdfSchedulerRuntime(
            mockCfg.Object,
            mockTarefas.Object,
            tp,
            intervaloPolling: TimeSpan.FromSeconds(60),
            limitePorTick: limitePorTick,
            misfireAtrasoMinimoSegundos: MisfireAltoS);

        runtime.Start();

        // Aguarda cada item ser processado (maximo 2 segundos por item).
        for (var i = 0; i < Math.Min(agendamentos.Count, limitePorTick); i++)
            gate.Wait(TimeSpan.FromSeconds(2));

        runtime.Stop();

        return ordem.AsReadOnly();
    }

    // ------------------------------------------------------------------
    // G1 — Ordenacao por PrioridadeExecucao ASC
    // ------------------------------------------------------------------

    [Fact]
    public void Ordering_PrioridadeNumerica_MenorNumeroDisparaPrimeiro()
    {
        var refUtc = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-1);
        var criado = refUtc.AddDays(-1);

        var agendamentos = new List<AncorarPdfSchedulerAgendamentoAtivo>
        {
            // Inseridos na ordem inversa para confirmar que o sort atua.
            Agendamento(tarefaId: 10, prioridade: 5, criadoEmUtc: criado, vencimentoUtc: venc),
            Agendamento(tarefaId: 20, prioridade: 1, criadoEmUtc: criado, vencimentoUtc: venc),
        };

        var ordem = ExecutarUmTick(agendamentos, refUtc);

        ordem.Should().HaveCount(2);
        ordem[0].Should().Be(20, "prioridade 1 (menor numero = mais urgente) deve disparar antes da prioridade 5");
        ordem[1].Should().Be(10);
    }

    [Fact]
    public void Ordering_TresPrioridades_DisparamEmOrdemCrescente()
    {
        var refUtc = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-1);
        var criado = refUtc.AddDays(-1);

        var agendamentos = new List<AncorarPdfSchedulerAgendamentoAtivo>
        {
            // Ordem inversa da esperada.
            Agendamento(tarefaId: 300, prioridade: 3, criadoEmUtc: criado, vencimentoUtc: venc),
            Agendamento(tarefaId: 100, prioridade: 1, criadoEmUtc: criado, vencimentoUtc: venc),
            Agendamento(tarefaId: 200, prioridade: 2, criadoEmUtc: criado, vencimentoUtc: venc),
        };

        var ordem = ExecutarUmTick(agendamentos, refUtc);

        ordem.Should().ContainInOrder(new[] { 100, 200, 300 },
            "prioridades 1, 2, 3 devem disparar nessa ordem ASC");
    }

    // ------------------------------------------------------------------
    // G2 — Empate de prioridade: CriadoEmUtc ASC (FIFO)
    // ------------------------------------------------------------------

    [Fact]
    public void Ordering_MesmaPrioridade_TarefaMaisAntiga_DisparaPrimeiro_FIFO()
    {
        var refUtc = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-1);

        var agendamentos = new List<AncorarPdfSchedulerAgendamentoAtivo>
        {
            Agendamento(tarefaId: 200, prioridade: 3, criadoEmUtc: refUtc.AddDays(-1),  vencimentoUtc: venc),
            Agendamento(tarefaId: 100, prioridade: 3, criadoEmUtc: refUtc.AddDays(-10), vencimentoUtc: venc),
        };

        var ordem = ExecutarUmTick(agendamentos, refUtc);

        ordem.Should().ContainInOrder(new[] { 100, 200 },
            "mesma prioridade: tarefa criada primeiro (CriadoEmUtc ASC) dispara antes (FIFO)");
    }

    [Fact]
    public void Ordering_MesmaPrioridade_HoraDistingueQuandoMesmoDia()
    {
        var refUtc = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-1);

        var agendamentos = new List<AncorarPdfSchedulerAgendamentoAtivo>
        {
            Agendamento(tarefaId: 999, prioridade: 2, criadoEmUtc: refUtc.AddHours(-1), vencimentoUtc: venc),
            Agendamento(tarefaId: 111, prioridade: 2, criadoEmUtc: refUtc.AddHours(-3), vencimentoUtc: venc),
        };

        var ordem = ExecutarUmTick(agendamentos, refUtc);

        ordem.Should().ContainInOrder(new[] { 111, 999 },
            "111 foi criada 2h antes de 999 com mesma prioridade: deve disparar primeiro");
    }

    // ------------------------------------------------------------------
    // G3 — LimitePorTick limita o numero processado por tick
    // ------------------------------------------------------------------

    [Fact]
    public void Ordering_LimitePorTick2_ProcessaApenasAsDuasMaisUrgentes()
    {
        var refUtc = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-1);
        var criado = refUtc.AddDays(-1);

        // 5 tarefas com prioridades 1-5.
        var agendamentos = Enumerable.Range(1, 5)
            .Select(i => Agendamento(i * 10, prioridade: i, criadoEmUtc: criado, vencimentoUtc: venc))
            .ToList();

        // limitePorTick=2: o mock de ListarAgendamentosAtivosAte deve respeitar o limite.
        var ordemDisparos = new List<int>();
        var gate = new SemaphoreSlim(0, 2);

        var mockCfg = new Mock<IAncorarPdfConfiguracaoRepository>(MockBehavior.Loose);
        mockCfg.Setup(r => r.ListarAgendamentosAtivosAte(It.IsAny<DateTime>(), It.IsAny<int>()))
               .Returns<DateTime, int>((_, lim) => agendamentos.Take(lim).ToList());
        mockCfg.Setup(r => r.RegistrarEventoScheduler(
                   It.IsAny<AncorarPdfSchedulerEventoRegistro>()));

        var mockTarefas = new Mock<ITarefaRepository>(MockBehavior.Loose);
        mockTarefas
            .Setup(r => r.TryMarkAsInProgress(It.IsAny<int>(), It.IsAny<DateTime>()))
            .Callback<int, DateTime>((id, _) =>
            {
                ordemDisparos.Add(id);
                gate.Release();
            })
            .Returns(true);

        var tp = new StaticTimeProvider(refUtc);

        var runtime = new AncorarPdfSchedulerRuntime(
            mockCfg.Object,
            mockTarefas.Object,
            tp,
            intervaloPolling: TimeSpan.FromSeconds(60),
            limitePorTick: 2,
            misfireAtrasoMinimoSegundos: MisfireAltoS);

        runtime.Start();
        gate.Wait(TimeSpan.FromSeconds(2));
        gate.Wait(TimeSpan.FromSeconds(2));
        runtime.Stop();

        ordemDisparos.Should().HaveCount(2,
            "limitePorTick=2 deve processar exatamente 2 tarefas por tick");
        ordemDisparos[0].Should().Be(10, "prioridade 1 (tarefaId=10) e a mais urgente");
        ordemDisparos[1].Should().Be(20, "prioridade 2 (tarefaId=20) e a segunda");
    }

    // ------------------------------------------------------------------
    // G4 — Misfire ja sinalizado: segundo tick nao duplica backlog
    // ------------------------------------------------------------------

    [Fact]
    public void Ordering_MisfireJaSinalizado_NaoDuplicaBacklogNoMesmoTick()
    {
        var refUtc = new DateTime(2026, 5, 20, 10, 0, 0, DateTimeKind.Utc);
        var venc   = refUtc.AddSeconds(-100); // 100s > limiar 30s → BacklogRegistrado

        var agendamento = Agendamento(tarefaId: 42, prioridade: 1,
            criadoEmUtc: refUtc.AddDays(-1), vencimentoUtc: venc);

        var agendamentos = new List<AncorarPdfSchedulerAgendamentoAtivo> { agendamento };

        var backlogCount = 0;
        var primeiroTick = new SemaphoreSlim(0, 1);

        var mockCfg = new Mock<IAncorarPdfConfiguracaoRepository>(MockBehavior.Loose);
        mockCfg.Setup(r => r.ListarAgendamentosAtivosAte(It.IsAny<DateTime>(), It.IsAny<int>()))
               .Returns(agendamentos)
               .Callback(() => primeiroTick.Release());
        mockCfg.Setup(r => r.RegistrarBacklogPendente(
                   It.IsAny<AncorarPdfSchedulerBacklogRegistro>()))
               .Callback(() => Interlocked.Increment(ref backlogCount))
               .Returns(1L);
        mockCfg.Setup(r => r.RegistrarEventoScheduler(
                   It.IsAny<AncorarPdfSchedulerEventoRegistro>()));

        var tp = new StaticTimeProvider(refUtc);

        // Intervalo curto (50ms) para forcar multiplos ticks.
        var runtime = new AncorarPdfSchedulerRuntime(
            mockCfg.Object,
            Mock.Of<ITarefaRepository>(),
            tp,
            intervaloPolling: TimeSpan.FromMilliseconds(50),
            limitePorTick: 200,
            misfireAtrasoMinimoSegundos: 30);

        runtime.Start();
        // Espera o primeiro tick completar, depois mais 2 ticks adicionais.
        primeiroTick.Wait(TimeSpan.FromSeconds(2));
        Thread.Sleep(200);
        runtime.Stop();

        backlogCount.Should().Be(1,
            "mesma chave (TarefaId:VencimentoUtc) nao deve gerar multiplos backlogs em ticks consecutivos");
    }

    // ------------------------------------------------------------------
    // Providers de tempo fixo para injecao
    // ------------------------------------------------------------------

    private sealed class StaticTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
            => new DateTimeOffset(utcNow, TimeSpan.Zero);
    }
}
