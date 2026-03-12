using FluentAssertions;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Login.Validation;
using Protons.Infrastructure.Login.Database;
using Protons.Infrastructure.Login.Repositories;
using Xunit;

namespace Protons.Infrastructure.Tests.Integration;

/// <summary>
/// C10 — Gate G4 (blocking): Testes de integração release quality para controle de acesso.
/// Anti-duplicidade: cobre AuthService C10 (primeiro usuário = Supremo, aprovação com hierarquia,
/// exclusão suave com redistribuição, transferência entre admins) — não cobertos em C1-C9.
/// </summary>
[Trait("Checklist", "C10")]
[Trait("Category", "C10_G4_Integracao")]
public sealed class ControleAcessoChecklist10IntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDb _db;
    private readonly IUserRepository _userRepo;
    private readonly IAuthService _authService;

    public ControleAcessoChecklist10IntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"c10_int_{Guid.NewGuid():N}.db");
        _db = new SqliteDb(_dbPath);
        _db.EnsureCreated();
        _userRepo = new UserRepository(_db);
        _authService = CriarAuthService(_userRepo);
    }

    // --- Primeiro usuário = Supremo ---

    [Fact]
    public void T01_Primeiro_usuario_registrado_recebe_role_Supremo()
    {
        var resultado = _authService.CriarConta(CriarUserModel("supremo@c10.test"), "Senha@1234567");

        resultado.Sucesso.Should().BeTrue();
        resultado.Role.Should().Be(UserRole.Supremo,
            "o primeiro usuário do sistema deve ser o Supremo");
    }

    [Fact]
    public void T02_Primeiro_usuario_fica_Ativo_imediatamente()
    {
        _authService.CriarConta(CriarUserModel("sup2@c10.test"), "Senha@1234567");
        var user = _userRepo.GetByEmail("sup2@c10.test");

        user.Should().NotBeNull();
        user!.Status.Should().Be(UserStatus.Ativo,
            "Supremo é ativado automaticamente sem aprovação");
    }

    [Fact]
    public void T03_Segundo_usuario_fica_Pendente()
    {
        _authService.CriarConta(CriarUserModel("sup3@c10.test"), "Senha@1234567");
        _authService.CriarConta(CriarUserModel("user3@c10.test"), "Senha@1234567");

        var user = _userRepo.GetByEmail("user3@c10.test");
        user!.Status.Should().Be(UserStatus.Pendente, "usuários após o primeiro ficam pendentes");
        user.Role.Should().Be(UserRole.Usuario);
    }

    // --- Aprovação com hierarquia (ResponsavelAdminId) ---

    [Fact]
    public void T04_AprovarUsuario_vincula_usuario_ao_admin_aprovador()
    {
        _authService.CriarConta(CriarUserModel("adm4@c10.test"), "Senha@1234567");
        var adminId = _userRepo.GetByEmail("adm4@c10.test")!.Id;
        _authService.CriarConta(CriarUserModel("usr4@c10.test"), "Senha@1234567");
        var userId = _userRepo.GetByEmail("usr4@c10.test")!.Id;

        _authService.AprovarUsuario(userId, adminId, "Aprovado no T04");

        var user = _userRepo.GetById(userId);
        user!.Status.Should().Be(UserStatus.Ativo);
        user.ResponsavelAdminId.Should().Be(adminId,
            "ao aprovar, o usuário é vinculado ao admin que aprovou");
    }

    // --- ListarSubordinados ---

    [Fact]
    public void T05_ListarSubordinados_Admin_ve_apenas_seus_usuarios()
    {
        _authService.CriarConta(CriarUserModel("adm5a@c10.test"), "Senha@1234567");
        var admin1Id = _userRepo.GetByEmail("adm5a@c10.test")!.Id;
        _authService.CriarConta(CriarUserModel("adm5b@c10.test"), "Senha@1234567");
        var admin2Id = _userRepo.GetByEmail("adm5b@c10.test")!.Id;

        _authService.CriarConta(CriarUserModel("ua@c10.test"), "Senha@1234567");
        var uaId = _userRepo.GetByEmail("ua@c10.test")!.Id;
        _authService.AprovarUsuario(uaId, admin1Id, "aprovado");

        _authService.CriarConta(CriarUserModel("ub@c10.test"), "Senha@1234567");
        var ubId = _userRepo.GetByEmail("ub@c10.test")!.Id;
        _authService.AprovarUsuario(ubId, admin2Id, "aprovado");

        var sub1 = _authService.ListarSubordinados(admin1Id, UserRole.Admin);
        sub1.Should().HaveCount(1);
        sub1[0].Id.Should().Be(uaId);

        var sub2 = _authService.ListarSubordinados(admin2Id, UserRole.Admin);
        sub2.Should().HaveCount(1);
        sub2[0].Id.Should().Be(ubId);
    }

    [Fact]
    public void T06_ListarSubordinados_Supremo_ve_todos_os_usuarios()
    {
        _authService.CriarConta(CriarUserModel("sup6@c10.test"), "Senha@1234567");
        var supremoId = _userRepo.GetByEmail("sup6@c10.test")!.Id;
        _authService.CriarConta(CriarUserModel("adm6@c10.test"), "Senha@1234567");
        _authService.CriarConta(CriarUserModel("usr6a@c10.test"), "Senha@1234567");
        _authService.CriarConta(CriarUserModel("usr6b@c10.test"), "Senha@1234567");

        var todos = _authService.ListarSubordinados(supremoId, UserRole.Supremo);

        // Supremo vê todos (incluindo si mesmo)
        todos.Count.Should().BeGreaterThanOrEqualTo(4,
            "Supremo vê todos os usuários não excluídos");
    }

    // --- ExcluirUsuario (soft delete + redistribuição) ---

    [Fact]
    public void T07_ExcluirUsuario_marca_status_Excluido()
    {
        _authService.CriarConta(CriarUserModel("adm7@c10.test"), "Senha@1234567");
        var adminId = _userRepo.GetByEmail("adm7@c10.test")!.Id;
        _authService.CriarConta(CriarUserModel("usr7@c10.test"), "Senha@1234567");
        var userId = _userRepo.GetByEmail("usr7@c10.test")!.Id;
        _authService.AprovarUsuario(userId, adminId, "ok");

        var resultado = _authService.ExcluirUsuario(new ExclusaoUsuarioEntrada(
            UsuarioId: userId,
            ExecutorId: adminId,
            Motivo: "Desligamento T07"));

        resultado.Sucesso.Should().BeTrue();
        var user = _userRepo.GetById(userId);
        user!.Status.Should().Be(UserStatus.Excluido);
        user.ExcluidoEmUtc.Should().NotBeNull();
    }

    [Fact]
    public void T08_ExcluirUsuario_Supremo_retorna_falha()
    {
        _authService.CriarConta(CriarUserModel("sup8@c10.test"), "Senha@1234567");
        var supremoId = _userRepo.GetByEmail("sup8@c10.test")!.Id;

        var resultado = _authService.ExcluirUsuario(new ExclusaoUsuarioEntrada(
            UsuarioId: supremoId,
            ExecutorId: supremoId,
            Motivo: "Tentativa inválida"));

        resultado.Sucesso.Should().BeFalse("Supremo não pode ser excluído");
        resultado.Mensagem.Should().NotBeNullOrWhiteSpace();
    }

    // --- TransferirUsuario ---

    [Fact]
    public void T09_TransferirUsuario_muda_ResponsavelAdminId()
    {
        _authService.CriarConta(CriarUserModel("sup9@c10.test"), "Senha@1234567");
        var supremoId = _userRepo.GetByEmail("sup9@c10.test")!.Id;

        // Criar admin1 e promovê-lo para Admin via repositório (setup direto de pré-condição)
        _authService.CriarConta(CriarUserModel("adm9a@c10.test"), "Senha@1234567");
        var admin1 = _userRepo.GetByEmail("adm9a@c10.test")!;
        admin1.Role = UserRole.Admin;
        admin1.Status = UserStatus.Ativo;
        _userRepo.Update(admin1);
        var admin1Id = admin1.Id;

        // Criar admin2 e promovê-lo para Admin via repositório
        _authService.CriarConta(CriarUserModel("adm9b@c10.test"), "Senha@1234567");
        var admin2 = _userRepo.GetByEmail("adm9b@c10.test")!;
        admin2.Role = UserRole.Admin;
        admin2.Status = UserStatus.Ativo;
        _userRepo.Update(admin2);
        var admin2Id = admin2.Id;

        _authService.CriarConta(CriarUserModel("usr9@c10.test"), "Senha@1234567");
        var userId = _userRepo.GetByEmail("usr9@c10.test")!.Id;
        _authService.AprovarUsuario(userId, admin1Id, "vinculado ao admin1");

        var resultado = _authService.TransferirUsuario(new TransferenciaUsuarioEntrada(
            UsuarioId: userId,
            NovoAdminId: admin2Id,
            ExecutorId: supremoId,
            Motivo: "Reorganização"));

        resultado.Sucesso.Should().BeTrue();
        var user = _userRepo.GetById(userId);
        user!.ResponsavelAdminId.Should().Be(admin2Id,
            "após transferência, ResponsavelAdminId aponta para o novo admin");
    }

    [Fact]
    public void T10_TransferirUsuario_destino_invalido_retorna_falha()
    {
        _authService.CriarConta(CriarUserModel("sup10@c10.test"), "Senha@1234567");
        var supremoId = _userRepo.GetByEmail("sup10@c10.test")!.Id;
        _authService.CriarConta(CriarUserModel("usr10@c10.test"), "Senha@1234567");
        var userId = _userRepo.GetByEmail("usr10@c10.test")!.Id;

        var resultado = _authService.TransferirUsuario(new TransferenciaUsuarioEntrada(
            UsuarioId: userId,
            NovoAdminId: 999_999, // não existe
            ExecutorId: supremoId,
            Motivo: "Destino inválido"));

        resultado.Sucesso.Should().BeFalse("admin de destino inexistente deve retornar falha");
    }

    // --- Admin excluído redistribui subordinados ---

    [Fact]
    public void T11_ExcluirAdmin_com_subordinados_redistribui_e_retorna_contagem()
    {
        // Supremo (primeiro usuário)
        _authService.CriarConta(CriarUserModel("sup11@c10.test"), "Senha@1234567");
        var supremoId = _userRepo.GetByEmail("sup11@c10.test")!.Id;

        // Admin promovido via repo (setup direto)
        _authService.CriarConta(CriarUserModel("adm11@c10.test"), "Senha@1234567");
        var admin = _userRepo.GetByEmail("adm11@c10.test")!;
        admin.Role = UserRole.Admin;
        admin.Status = UserStatus.Ativo;
        _userRepo.Update(admin);
        var adminId = admin.Id;

        // Dois subordinados do admin
        _authService.CriarConta(CriarUserModel("u11a@c10.test"), "Senha@1234567");
        var u1 = _userRepo.GetByEmail("u11a@c10.test")!.Id;
        _authService.AprovarUsuario(u1, adminId, "vinculado");

        _authService.CriarConta(CriarUserModel("u11b@c10.test"), "Senha@1234567");
        var u2 = _userRepo.GetByEmail("u11b@c10.test")!.Id;
        _authService.AprovarUsuario(u2, adminId, "vinculado");

        var resultado = _authService.ExcluirUsuario(new ExclusaoUsuarioEntrada(
            UsuarioId: adminId,
            ExecutorId: supremoId,
            Motivo: "Admin desligado T11"));

        resultado.Sucesso.Should().BeTrue();
        resultado.UsuariosOrfaosTransferidos.Should().Be(2,
            "ambos os subordinados do admin excluído devem ser contados");

        // Subordinados devem agora estar sob o Supremo
        _userRepo.GetById(u1)!.ResponsavelAdminId.Should().Be(supremoId,
            "subordinado u1 redistribuído para o Supremo");
        _userRepo.GetById(u2)!.ResponsavelAdminId.Should().Be(supremoId,
            "subordinado u2 redistribuído para o Supremo");
    }

    [Fact]
    public void T12_TransferirUsuario_com_executor_nao_supremo_retorna_falha()
    {
        _authService.CriarConta(CriarUserModel("sup12@c10.test"), "Senha@1234567");

        _authService.CriarConta(CriarUserModel("adm12@c10.test"), "Senha@1234567");
        var admin = _userRepo.GetByEmail("adm12@c10.test")!;
        admin.Role = UserRole.Admin;
        admin.Status = UserStatus.Ativo;
        _userRepo.Update(admin);
        var adminId = admin.Id;

        _authService.CriarConta(CriarUserModel("usr12@c10.test"), "Senha@1234567");
        var userId = _userRepo.GetByEmail("usr12@c10.test")!.Id;

        // Executor é um usuário comum (não Supremo) — deve falhar
        var resultado = _authService.TransferirUsuario(new TransferenciaUsuarioEntrada(
            UsuarioId: userId,
            NovoAdminId: adminId,
            ExecutorId: userId, // usuário tentando se transferir (inválido)
            Motivo: "Tentativa inválida"));

        resultado.Sucesso.Should().BeFalse(
            "apenas o Supremo pode transferir usuários entre administradores");
        resultado.Mensagem.Should().NotBeNullOrWhiteSpace();
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }

    // --- Helpers ---

    private static IAuthService CriarAuthService(IUserRepository userRepo)
    {
        var hasher = new PasswordHasher();
        var auditRepo = new FakeAuditRepository();
        var audit = new AuditService(auditRepo);
        return new AuthService(userRepo, hasher, audit, "c10-test", "0.0.0", false);
    }

    private static User CriarUserModel(string email) => new()
    {
        Empresa = "Protons",
        Nome = "Usuário C10",
        Cpf = "11144477735",
        Cargo = "Cargo",
        Email = email
    };

    /// <summary>Implementação mínima de IAuditLogRepository para testes (sem dependências de DB).</summary>
    private sealed class FakeAuditRepository : IAuditLogRepository
    {
        public void Insert(AuditLogEntry entry) { }
        public string? GetLastHash() => null;
        public void InsertWithHashChain(AuditLogEntry entry, Func<AuditLogEntry, string?, string> computeHash) { }
        public IReadOnlyList<AuditLogEntry> GetPage(int page, int pageSize) => Array.Empty<AuditLogEntry>();
    }
}
