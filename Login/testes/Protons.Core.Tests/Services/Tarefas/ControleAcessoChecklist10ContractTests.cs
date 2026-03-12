using FluentAssertions;
using Protons.Core.Login.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C10 — Gate G2 (blocking): Contratos de domínio para controle de acesso.
/// Zero dependências externas — apenas modelos e enums do Core.
/// Anti-duplicidade: C1–C9 não cobrem hierarquia de roles, soft-delete, audit trail de tarefas.
/// </summary>
[Trait("Checklist", "C10")]
[Trait("Category", "C10_G2_Contratos")]
public sealed class ControleAcessoChecklist10ContractTests
{
    // --- UserRole ---

    [Fact]
    public void T01_UserRole_Supremo_tem_valor_2()
    {
        ((int)UserRole.Supremo).Should().Be(2,
            "Supremo deve ter valor numérico 2 (preserva compatibilidade com enum existente)");
    }

    [Fact]
    public void T02_UserRole_Admin_continua_valor_1()
    {
        ((int)UserRole.Admin).Should().Be(1, "Admin = 1 deve permanecer estável");
        ((int)UserRole.Usuario).Should().Be(0, "Usuario = 0 deve permanecer estável");
    }

    [Fact]
    public void T03_UserRole_enum_tem_3_valores()
    {
        Enum.GetValues<UserRole>().Should().HaveCount(3,
            "apenas Usuario, Admin e Supremo devem existir");
    }

    // --- UserStatus ---

    [Fact]
    public void T04_UserStatus_Excluido_tem_valor_3()
    {
        ((int)UserStatus.Excluido).Should().Be(3,
            "Excluido deve ter valor 3 (após Bloqueado=2)");
    }

    [Fact]
    public void T05_UserStatus_valores_anteriores_intactos()
    {
        ((int)UserStatus.Pendente).Should().Be(0);
        ((int)UserStatus.Ativo).Should().Be(1);
        ((int)UserStatus.Bloqueado).Should().Be(2);
    }

    // --- User model ---

    [Fact]
    public void T06_User_ResponsavelAdminId_e_nullable()
    {
        var user = new User { Id = 1, Nome = "X", Email = "x@x.com" };
        user.ResponsavelAdminId.Should().BeNull("valor default deve ser null");
        user.ResponsavelAdminId = 99;
        user.ResponsavelAdminId.Should().Be(99);
    }

    [Fact]
    public void T07_User_ExcluidoEmUtc_e_nullable()
    {
        var user = new User { Id = 1, Nome = "X", Email = "x@x.com" };
        user.ExcluidoEmUtc.Should().BeNull("não excluído por padrão");
        user.ExcluidoEmUtc = DateTime.UtcNow;
        user.ExcluidoEmUtc.Should().NotBeNull();
    }

    [Fact]
    public void T08_Supremo_nao_tem_ResponsavelAdminId()
    {
        // Supremo nunca tem admin responsável (é o topo da hierarquia).
        var supremo = new User { Id = 1, Role = UserRole.Supremo, ResponsavelAdminId = null };
        supremo.ResponsavelAdminId.Should().BeNull("Supremo está no topo, sem responsável");
    }

    // --- TarefaAlteracao ---

    [Fact]
    public void T09_TarefaAlteracao_campos_obrigatorios()
    {
        var alteracao = new TarefaAlteracao
        {
            TarefaId = 1,
            AlteradoPorUserId = 2,
            AlteradoPorNome = "Admin",
            CampoAlterado = "Status",
            ValorAnterior = "Agendada",
            ValorNovo = "Concluida",
            AlteradoEmUtc = DateTime.UtcNow
        };

        alteracao.TarefaId.Should().Be(1);
        alteracao.AlteradoPorUserId.Should().Be(2);
        alteracao.AlteradoPorNome.Should().Be("Admin");
        alteracao.CampoAlterado.Should().Be("Status");
        alteracao.ValorAnterior.Should().Be("Agendada");
        alteracao.ValorNovo.Should().Be("Concluida");
    }

    [Fact]
    public void T10_TarefaAlteracao_valores_podem_ser_null()
    {
        // ValorAnterior null = criação de campo; ValorNovo null = remoção de campo.
        var alteracao = new TarefaAlteracao
        {
            TarefaId = 1, AlteradoPorUserId = 1, AlteradoPorNome = "Admin",
            CampoAlterado = "ConcluidaEmUtc",
            ValorAnterior = null,
            ValorNovo = DateTime.UtcNow.ToString("o")
        };
        alteracao.ValorAnterior.Should().BeNull();
        alteracao.ValorNovo.Should().NotBeNull();
    }

    // --- ControleAcessoModels ---

    [Fact]
    public void T11_ControleAcessoResultado_sucesso_sem_mensagem()
    {
        var ok = new ControleAcessoResultado(true);
        ok.Sucesso.Should().BeTrue();
        ok.Mensagem.Should().BeNull();
    }

    [Fact]
    public void T12_ControleAcessoResultado_falha_com_mensagem()
    {
        var falha = new ControleAcessoResultado(false, "Usuário não encontrado.");
        falha.Sucesso.Should().BeFalse();
        falha.Mensagem.Should().Be("Usuário não encontrado.");
    }

    [Fact]
    public void T13_TransferenciaUsuarioEntrada_campos_obrigatorios()
    {
        var entrada = new TransferenciaUsuarioEntrada(
            UsuarioId: 5,
            NovoAdminId: 2,
            ExecutorId: 1,
            Motivo: "Reorganização de equipe");

        entrada.UsuarioId.Should().Be(5);
        entrada.NovoAdminId.Should().Be(2);
        entrada.ExecutorId.Should().Be(1);
        entrada.Motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void T14_ExclusaoUsuarioEntrada_campos_obrigatorios()
    {
        var entrada = new ExclusaoUsuarioEntrada(
            UsuarioId: 7,
            ExecutorId: 1,
            Motivo: "Desligamento");

        entrada.UsuarioId.Should().Be(7);
        entrada.ExecutorId.Should().Be(1);
        entrada.Motivo.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void T15_Hierarquia_roles_Supremo_maior_que_Admin_maior_que_Usuario()
    {
        // Garante ordenação numérica correta da hierarquia.
        ((int)UserRole.Supremo).Should().BeGreaterThan((int)UserRole.Admin);
        ((int)UserRole.Admin).Should().BeGreaterThan((int)UserRole.Usuario);
    }

    [Fact]
    public void T16_ControleAcessoResultado_com_orfaos_transferidos()
    {
        // Resultado de exclusão de admin com 3 subordinados.
        var resultado = new ControleAcessoResultado(true, null, 3);
        resultado.Sucesso.Should().BeTrue();
        resultado.Mensagem.Should().BeNull();
        resultado.UsuariosOrfaosTransferidos.Should().Be(3,
            "deve informar quantos subordinados foram redistribuídos ao Supremo");
    }

    [Fact]
    public void T17_ControleAcessoResultado_sem_orfaos_valor_zero_por_padrao()
    {
        // Exclusão de usuário comum (sem subordinados) — campo padrão = 0.
        var resultado = new ControleAcessoResultado(true);
        resultado.UsuariosOrfaosTransferidos.Should().Be(0,
            "exclusão de usuário comum não gera órfãos");
    }
}
