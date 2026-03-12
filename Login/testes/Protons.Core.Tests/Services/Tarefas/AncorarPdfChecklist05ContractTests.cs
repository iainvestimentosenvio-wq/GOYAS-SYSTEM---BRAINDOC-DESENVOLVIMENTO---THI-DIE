using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C5 — Gate G1 (blocking): Contratos dos modelos de fila de execução.
/// Verifica invariantes estáticos sem banco de dados.
/// </summary>
[Trait("Checklist", "C5")]
[Trait("Category", "C5_G1_Contratos")]
public sealed class AncorarPdfChecklist05ContractTests
{
    [Fact]
    public void T01_FilaItem_inicializa_com_defaults_corretos()
    {
        var item = new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = 1,
            ClienteId = 1,
            CicloId = "ciclo-001",
            JanelaAlvoUtc = DateTime.UtcNow,
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "scheduler",
            EnfileiradoEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = DateTime.UtcNow
        };

        item.Status.Should().Be(AncorarPdfFilaStatus.Aguardando);
        item.TentativasMaximas.Should().Be(3);
        item.TentativaAtual.Should().Be(0);
        item.PrioridadeExecucao.Should().Be(3);
        item.CategoriaFalha.Should().BeNull();
        item.ErroCodigo.Should().BeNull();
        item.ErroDetalhe.Should().BeNull();
        item.IniciadoEmUtc.Should().BeNull();
        item.FinalizadoEmUtc.Should().BeNull();
    }

    [Fact]
    public void T02_FilaEnfileirarResultado_enfileirado_tem_id()
    {
        var filaItemId = Guid.NewGuid().ToString("N");
        var resultado = new AncorarPdfFilaEnfileirarResultado(true, filaItemId);

        resultado.Enfileirado.Should().BeTrue();
        resultado.FilaItemId.Should().Be(filaItemId);
        resultado.FilaItemId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void T03_FilaFalhaCategoria_tecnica_nao_equal_negocio()
    {
        var tecnica = AncorarPdfFilaFalhaCategoria.Tecnica;
        var negocio = AncorarPdfFilaFalhaCategoria.Negocio;

        tecnica.Should().NotBe(negocio);
        tecnica.Should().NotBe(AncorarPdfFilaFalhaCategoria.Nenhuma);
        negocio.Should().NotBe(AncorarPdfFilaFalhaCategoria.Nenhuma);
    }

    [Fact]
    public void T04_CancelarResultado_falha_tem_motivo()
    {
        var resultado = new AncorarPdfFilaCancelarResultado(false, "item_nao_aguardando");

        resultado.Cancelado.Should().BeFalse();
        resultado.MotivoFalha.Should().NotBeNullOrEmpty();
        resultado.MotivoFalha.Should().Be("item_nao_aguardando");
    }

    [Fact]
    public void T05_FilaItem_cancelado_nao_tem_categoriaFalha()
    {
        var item = new AncorarPdfFilaItem
        {
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = 1,
            ClienteId = 1,
            CicloId = "ciclo-001",
            JanelaAlvoUtc = DateTime.UtcNow,
            Status = AncorarPdfFilaStatus.Cancelado,
            EnfileiradoPorUserId = 0,
            EnfileiradoPorNome = "scheduler",
            EnfileiradoEmUtc = DateTime.UtcNow,
            CanceladoPorNome = "usuario_teste",
            CanceladoEmUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N"),
            CriadoEmUtc = DateTime.UtcNow
        };

        // Cancelamento ≠ falha: CategoriaFalha deve ser null
        item.CategoriaFalha.Should().BeNull();
        item.ErroCodigo.Should().BeNull();
        item.Status.Should().Be(AncorarPdfFilaStatus.Cancelado);
        item.CanceladoPorNome.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void T06_AncorarPdfExecucaoLease_expiry_future_valida()
    {
        var acquired = DateTime.UtcNow;
        var expires = acquired.AddMinutes(5);

        var lease = new AncorarPdfExecucaoLease
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            FilaItemId = Guid.NewGuid().ToString("N"),
            TarefaId = 1,
            ClienteId = 1,
            WorkerId = "worker-0",
            AcquiredAtUtc = acquired,
            ExpiresAtUtc = expires,
            Ativa = true
        };

        lease.ExpiresAtUtc.Should().BeAfter(lease.AcquiredAtUtc);
        (lease.ExpiresAtUtc - lease.AcquiredAtUtc).TotalSeconds.Should().BeGreaterThan(0);
        lease.Ativa.Should().BeTrue();
    }

    [Fact]
    public void T07_MotorResultado_sucesso_sem_erroCodigo()
    {
        var resultado = new AncorarPdfMotorResultado(true, AncorarPdfFilaFalhaCategoria.Nenhuma);

        resultado.Sucesso.Should().BeTrue();
        resultado.Categoria.Should().Be(AncorarPdfFilaFalhaCategoria.Nenhuma);
        resultado.ErroCodigo.Should().BeNull();
        resultado.ErroDetalhe.Should().BeNull();
    }
}
