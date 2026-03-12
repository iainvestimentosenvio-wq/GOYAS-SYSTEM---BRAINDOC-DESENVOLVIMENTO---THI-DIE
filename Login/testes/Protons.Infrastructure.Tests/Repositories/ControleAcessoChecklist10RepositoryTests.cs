using FluentAssertions;
using Microsoft.Data.Sqlite;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Tarefas.Models;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Protons.Infrastructure.Tarefas.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Repositories;

/// <summary>
/// C10 — Gate G3 (blocking): Testes de repositório para controle de acesso hierárquico.
/// Anti-duplicidade: cobre ResponsavelAdminId, ListarPorAdmin, ListarTodos,
/// ExcluirSoft, TransferirParaAdmin, RedistribuirTarefas — não cobertos em C1-C9.
/// </summary>
[Trait("Checklist", "C10")]
[Trait("Category", "C10_G3_Repositorios")]
public sealed class ControleAcessoChecklist10RepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDb _db;

    public ControleAcessoChecklist10RepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"c10_repo_{Guid.NewGuid():N}.db");
        _db = new SqliteDb(_dbPath);
        _db.EnsureCreated();
    }

    // --- ResponsavelAdminId ---

    [Fact]
    public void T01_Create_com_ResponsavelAdminId_persiste_e_recupera()
    {
        var repo = new UserRepository(_db);
        var adminId = repo.Create(CriarUser("admin@c10.test", UserRole.Admin));
        var userId = repo.Create(CriarUser("user@c10.test", UserRole.Usuario, responsavelAdminId: adminId));

        var recuperado = repo.GetById(userId);

        recuperado.Should().NotBeNull();
        recuperado!.ResponsavelAdminId.Should().Be(adminId,
            "ResponsavelAdminId deve ser persistido e recuperado corretamente");
    }

    [Fact]
    public void T02_Supremo_criado_sem_ResponsavelAdminId()
    {
        var repo = new UserRepository(_db);
        var supremoId = repo.Create(CriarUser("supremo@c10.test", UserRole.Supremo, responsavelAdminId: null));

        var supremo = repo.GetById(supremoId);

        supremo.Should().NotBeNull();
        supremo!.ResponsavelAdminId.Should().BeNull("Supremo não tem admin responsável");
    }

    // --- ListarPorAdmin ---

    [Fact]
    public void T03_ListarPorAdmin_retorna_apenas_subordinados_diretos()
    {
        var repo = new UserRepository(_db);
        var admin1Id = repo.Create(CriarUser("admin1@c10.test", UserRole.Admin));
        var admin2Id = repo.Create(CriarUser("admin2@c10.test", UserRole.Admin));
        var user1Id = repo.Create(CriarUser("u1@c10.test", UserRole.Usuario, responsavelAdminId: admin1Id));
        var user2Id = repo.Create(CriarUser("u2@c10.test", UserRole.Usuario, responsavelAdminId: admin1Id));
        repo.Create(CriarUser("u3@c10.test", UserRole.Usuario, responsavelAdminId: admin2Id)); // outro admin

        var subordinados = repo.ListarPorAdmin(admin1Id);

        subordinados.Should().HaveCount(2);
        subordinados.Select(u => u.Id).Should().BeEquivalentTo(new[] { user1Id, user2Id });
    }

    [Fact]
    public void T04_ListarPorAdmin_nao_retorna_usuarios_excluidos()
    {
        var repo = new UserRepository(_db);
        var adminId = repo.Create(CriarUser("admin@c10t4.test", UserRole.Admin));
        var userId = repo.Create(CriarUser("user@c10t4.test", UserRole.Usuario, responsavelAdminId: adminId));
        repo.ExcluirSoft(userId, DateTime.UtcNow);

        var subordinados = repo.ListarPorAdmin(adminId);

        subordinados.Should().BeEmpty("excluídos não devem aparecer na lista");
    }

    // --- ListarTodos ---

    [Fact]
    public void T05_ListarTodos_retorna_todos_nao_excluidos()
    {
        var repo = new UserRepository(_db);
        var a = repo.Create(CriarUser("a@c10t5.test", UserRole.Admin));
        var b = repo.Create(CriarUser("b@c10t5.test", UserRole.Usuario));
        var c = repo.Create(CriarUser("c@c10t5.test", UserRole.Usuario));
        repo.ExcluirSoft(c, DateTime.UtcNow); // excluído

        var todos = repo.ListarTodos();

        todos.Select(u => u.Id).Should().Contain(new[] { a, b });
        todos.Select(u => u.Id).Should().NotContain(c, "excluído não aparece em ListarTodos");
    }

    // --- ExcluirSoft ---

    [Fact]
    public void T06_ExcluirSoft_define_status_Excluido_e_ExcluidoEmUtc()
    {
        var repo = new UserRepository(_db);
        var userId = repo.Create(CriarUser("excluir@c10.test", UserRole.Usuario));
        var agora = DateTime.UtcNow;

        repo.ExcluirSoft(userId, agora);

        var user = repo.GetById(userId);
        user.Should().NotBeNull();
        user!.Status.Should().Be(UserStatus.Excluido);
        user.ExcluidoEmUtc.Should().NotBeNull();
        user.ExcluidoEmUtc!.Value.Should().BeCloseTo(agora, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void T07_ExcluirSoft_preserva_dados_historicos()
    {
        var repo = new UserRepository(_db);
        var email = $"historico.{Guid.NewGuid():N}@c10.test";
        var userId = repo.Create(CriarUser(email, UserRole.Usuario, nome: "João Histórico"));

        repo.ExcluirSoft(userId, DateTime.UtcNow);

        var user = repo.GetById(userId); // ainda recuperável
        user.Should().NotBeNull("histórico deve ser preservado");
        user!.Nome.Should().Be("João Histórico", "dados originais intactos");
        user.Email.Should().Be(email.ToLowerInvariant());
    }

    // --- TransferirParaAdmin ---

    [Fact]
    public void T08_TransferirParaAdmin_atualiza_ResponsavelAdminId()
    {
        var repo = new UserRepository(_db);
        var admin1Id = repo.Create(CriarUser("adm1@c10t8.test", UserRole.Admin));
        var admin2Id = repo.Create(CriarUser("adm2@c10t8.test", UserRole.Admin));
        var userId = repo.Create(CriarUser("user@c10t8.test", UserRole.Usuario, responsavelAdminId: admin1Id));

        repo.TransferirParaAdmin(userId, admin2Id, DateTime.UtcNow);

        var user = repo.GetById(userId);
        user!.ResponsavelAdminId.Should().Be(admin2Id,
            "após transferência o ResponsavelAdminId deve apontar para o novo admin");
    }

    // --- RedistribuirTarefas ---

    [Fact]
    public void T09_RedistribuirTarefas_move_tarefas_agendadas_para_destino()
    {
        var userRepo = new UserRepository(_db);
        var tarefaRepo = new SqliteTarefaRepository(_db);

        var adminId = userRepo.Create(CriarUser("adm@c10t9.test", UserRole.Admin));
        var userId = userRepo.Create(CriarUser("user@c10t9.test", UserRole.Usuario, responsavelAdminId: adminId));

        // Cria cliente para a tarefa
        using var conn = new SqliteConnection(_db.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Clientes (CodigoCliente, Nome, TipoDocumento, DocumentoHash, DocumentoCipher, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc, ProtecaoVersao)
VALUES ($cod, 'CLI', 'CNPJ', $hash, $cipher, 1, $uid, $now, $now, 1);
SELECT last_insert_rowid();";
        var docHash = $"hash_{Guid.NewGuid():N}";
        cmd.Parameters.AddWithValue("$cod", $"C10-{Guid.NewGuid():N}"[..10]);
        cmd.Parameters.AddWithValue("$hash", docHash);
        cmd.Parameters.AddWithValue("$cipher", $"cipher_{Guid.NewGuid():N}");
        cmd.Parameters.AddWithValue("$uid", adminId);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        var clienteId = Convert.ToInt32(cmd.ExecuteScalar());

        var tarefaId = tarefaRepo.Create(new Tarefa
        {
            ClienteId = clienteId,
            FerramentaId = "generica",
            Titulo = "Tarefa a redistribuir",
            VencimentoUtc = DateTime.UtcNow.AddHours(2),
            ResponsavelUserId = userId,
            Status = TarefaStatus.Agendada,
            Recorrencia = TarefaRecorrencia.Nenhuma,
            EsteiraId = 1,
            Ativa = true,
            CriadoPorUserId = userId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        });

        userRepo.RedistribuirTarefas(userId, adminId, DateTime.UtcNow);

        var tarefa = tarefaRepo.GetById(tarefaId);
        tarefa.Should().NotBeNull();
        tarefa!.ResponsavelUserId.Should().Be(adminId,
            "tarefa deve ter sido redistribuída para o admin");
    }

    // --- TarefaAlteracao ---

    [Fact]
    public void T10_TarefaAlteracaoRepository_registra_e_lista_por_tarefa()
    {
        var userRepo = new UserRepository(_db);
        var adminId = userRepo.Create(CriarUser("admin@c10t10.test", UserRole.Admin));

        // Cria tarefa mínima via SQL direto (sem cliente)
        int tarefaId;
        using (var conn = new SqliteConnection(_db.ConnectionString))
        {
            conn.Open();
            using var cmdCliente = conn.CreateCommand();
            cmdCliente.CommandText = @"
INSERT INTO Clientes (CodigoCliente, Nome, TipoDocumento, DocumentoHash, DocumentoCipher, Ativo, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc, ProtecaoVersao)
VALUES ($cod, 'CLI', 'CNPJ', $hash, $cipher, 1, $uid, $now, $now, 1);
SELECT last_insert_rowid();";
            cmdCliente.Parameters.AddWithValue("$cod", $"C10-T10-{Guid.NewGuid():N}"[..12]);
            cmdCliente.Parameters.AddWithValue("$hash", $"hash-t10-{Guid.NewGuid():N}");
            cmdCliente.Parameters.AddWithValue("$cipher", $"cipher-t10");
            cmdCliente.Parameters.AddWithValue("$uid", adminId);
            cmdCliente.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            var clienteId = Convert.ToInt32(cmdCliente.ExecuteScalar());

            using var cmdTarefa = conn.CreateCommand();
            cmdTarefa.CommandText = @"
INSERT INTO Tarefas (ClienteId, FerramentaId, Titulo, VencimentoUtc, ResponsavelUserId, Status, Recorrencia, Ativa, CriadoPorUserId, CriadoEmUtc, AtualizadoEmUtc)
VALUES ($cid, 'generica', 'T10', $venc, $uid, 'Agendada', 'Nenhuma', 1, $uid, $now, $now);
SELECT last_insert_rowid();";
            cmdTarefa.Parameters.AddWithValue("$cid", clienteId);
            cmdTarefa.Parameters.AddWithValue("$venc", DateTime.UtcNow.AddHours(1).ToString("o"));
            cmdTarefa.Parameters.AddWithValue("$uid", adminId);
            cmdTarefa.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
            tarefaId = Convert.ToInt32(cmdTarefa.ExecuteScalar());
        }

        var repo = new SqliteTarefaAlteracaoRepository(_db);
        repo.RegistrarAlteracao(new TarefaAlteracao
        {
            TarefaId = tarefaId,
            AlteradoPorUserId = adminId,
            AlteradoPorNome = "Admin C10",
            CampoAlterado = "Status",
            ValorAnterior = "Agendada",
            ValorNovo = "Concluida",
            AlteradoEmUtc = DateTime.UtcNow
        });

        var lista = repo.ListarPorTarefa(tarefaId);

        lista.Should().HaveCount(1);
        lista[0].CampoAlterado.Should().Be("Status");
        lista[0].ValorAnterior.Should().Be("Agendada");
        lista[0].ValorNovo.Should().Be("Concluida");
        lista[0].AlteradoPorNome.Should().Be("Admin C10");
    }

    // --- RedistribuirSubordinados ---

    [Fact]
    public void T11_RedistribuirSubordinados_reatribui_usuarios_para_novo_admin()
    {
        var repo = new UserRepository(_db);
        var adminExcluidoId = repo.Create(CriarUser("adm_excluido@c10t11.test", UserRole.Admin));
        var novoAdminId = repo.Create(CriarUser("supremo@c10t11.test", UserRole.Supremo));
        var user1Id = repo.Create(CriarUser("u1@c10t11.test", UserRole.Usuario, responsavelAdminId: adminExcluidoId));
        var user2Id = repo.Create(CriarUser("u2@c10t11.test", UserRole.Usuario, responsavelAdminId: adminExcluidoId));
        // user3 pertence a outro admin — não deve ser afetado
        repo.Create(CriarUser("u3@c10t11.test", UserRole.Usuario, responsavelAdminId: novoAdminId));

        repo.RedistribuirSubordinados(adminExcluidoId, novoAdminId, DateTime.UtcNow);

        repo.GetById(user1Id)!.ResponsavelAdminId.Should().Be(novoAdminId,
            "subordinados do admin excluído devem ser reatribuídos");
        repo.GetById(user2Id)!.ResponsavelAdminId.Should().Be(novoAdminId,
            "todos os subordinados devem ser reatribuídos");
        // user3 não foi afetado
        var user3 = repo.ListarPorAdmin(novoAdminId)
            .First(u => u.Email == "u3@c10t11.test");
        user3.ResponsavelAdminId.Should().Be(novoAdminId,
            "usuário de outro admin não deve ser afetado");
    }

    [Fact]
    public void T12_RedistribuirSubordinados_nao_afeta_usuarios_excluidos()
    {
        var repo = new UserRepository(_db);
        var adminId = repo.Create(CriarUser("adm@c10t12.test", UserRole.Admin));
        var supremoId = repo.Create(CriarUser("sup@c10t12.test", UserRole.Supremo));
        var userId = repo.Create(CriarUser("usr@c10t12.test", UserRole.Usuario, responsavelAdminId: adminId));

        // Exclui o usuário antes de redistribuir
        repo.ExcluirSoft(userId, DateTime.UtcNow);
        repo.RedistribuirSubordinados(adminId, supremoId, DateTime.UtcNow);

        // Usuário excluído não deve aparecer em ListarPorAdmin do supremo
        var subordinados = repo.ListarPorAdmin(supremoId);
        subordinados.Should().NotContain(u => u.Id == userId,
            "usuários excluídos não são redistribuídos");
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    // --- Helpers ---

    private static User CriarUser(
        string email,
        UserRole role,
        string nome = "Usuário C10",
        int? responsavelAdminId = null)
    {
        return new User
        {
            Empresa = "Protons",
            Nome = nome,
            Cpf = "11144477735",
            Cargo = "Cargo",
            Email = email.ToLowerInvariant(),
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100_000,
            Status = UserStatus.Ativo,
            Role = role,
            ResponsavelAdminId = responsavelAdminId,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };
    }
}
