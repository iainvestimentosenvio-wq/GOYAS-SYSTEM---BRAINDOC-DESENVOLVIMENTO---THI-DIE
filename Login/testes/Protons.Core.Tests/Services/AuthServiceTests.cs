using FluentAssertions;
using Moq;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Services;
using Protons.Core.Login.Validation;
using Xunit;

namespace Protons.Core.Tests.Services;

public class AuthServiceTests
{
    private readonly Mock<IUserRepository> _userRepositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly Mock<IAuditService> _auditServiceMock;
    private readonly AuthService _authService;

    public AuthServiceTests()
    {
        _userRepositoryMock = new Mock<IUserRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _auditServiceMock = new Mock<IAuditService>();

        _userRepositoryMock.Setup(x => x.IncrementarFalhaLogin(
                It.IsAny<int>(),
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>()))
            .Returns(new LockoutUpdateResult(1, 0, null, false));

        _userRepositoryMock.Setup(x => x.GetById(It.Is<int>(id => id >= 100)))
            .Returns((int id) => new User
            {
                Id = id,
                Nome = "Admin Test",
                Email = $"admin{id}@test.local",
                Cpf = "000.000.000-00",
                SenhaHash = "h",
                SenhaSalt = "s",
                IteracoesPbkdf2 = 1,
                Status = UserStatus.Ativo,
                Role = UserRole.Admin,
                CriadoEmUtc = DateTime.UtcNow,
                AtualizadoEmUtc = DateTime.UtcNow
            });

        _authService = new AuthService(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _auditServiceMock.Object,
            "TestMachine",
            "0.1.0-Test",
            usarHashChain: true
        );
    }

    #region Autenticar Tests

    [Fact]
    public void Autenticar_ShouldReturnSuccess_WhenCredentialsAreValid()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeTrue();
        result.Mensagem.Should().Be("Login realizado com sucesso.");
        result.UserId.Should().Be(user.Id);

        // Verify user was updated
        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.FalhasLogin == 0 &&
            u.LockoutAteUtc == null &&
            u.UltimoLoginUtc.HasValue
        )), Times.Once);

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e => e.Acao == "LOGIN_SUCESSO" && e.Resultado == "OK"),
            true
        ), Times.Once);
    }

    [Fact]
    public void Autenticar_DeveUsarTimeProviderParaAtualizarUltimoLogin()
    {
        var fixedNow = new DateTimeOffset(2026, 3, 1, 12, 30, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var sut = new AuthService(
            _userRepositoryMock.Object,
            _passwordHasherMock.Object,
            _auditServiceMock.Object,
            "TestMachine",
            "0.1.0-Test",
            usarHashChain: true,
            timeProvider: timeProvider);

        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);
        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        _ = sut.Autenticar(email, password, modoAdmin: false);

        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.UltimoLoginUtc == fixedNow.UtcDateTime &&
            u.AtualizadoEmUtc == fixedNow.UtcDateTime)), Times.Once);
    }

    [Fact]
    public void Autenticar_ShouldReturnFailure_WhenEmailIsInvalid()
    {
        // Arrange
        const string invalidEmail = "not-an-email";
        const string password = "password";

        // Act
        var result = _authService.Autenticar(invalidEmail, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be("E-mail ou senha inválidos.");

        // Verify no repository calls were made
        _userRepositoryMock.Verify(x => x.GetByEmail(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Autenticar_ShouldReturnFailure_WhenEmailIsNull()
    {
        // Arrange
        const string password = "SecurePass123";

        // Act
        var result = _authService.Autenticar(null!, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be("E-mail ou senha inválidos.");
        _userRepositoryMock.Verify(x => x.GetByEmail(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Autenticar_ShouldReturnFailure_WhenPasswordIsInvalid()
    {
        // Arrange
        const string email = "user@example.com";
        const string invalidPassword = "   ";

        // Act
        var result = _authService.Autenticar(email, invalidPassword, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be("E-mail ou senha inválidos.");
    }

    [Fact]
    public void Autenticar_ShouldAllowLegacyPassword_WhenHashVerificationIsSuccessful()
    {
        // Arrange
        const string email = "user@example.com";
        const string legacyPassword = "12345678";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(legacyPassword, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, legacyPassword, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeTrue();
        result.Mensagem.Should().Be("Login realizado com sucesso.");
    }

    [Fact]
    public void Autenticar_ShouldReturnFailure_WhenUserNotFound()
    {
        // Arrange
        const string email = "nonexistent@example.com";
        const string password = "SecurePass123";

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns((User?)null);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be("E-mail ou senha inválidos.");

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e => e.Acao == "LOGIN_FALHA" && e.Detalhes == "Usuário não encontrado"),
            true
        ), Times.Once);
    }

    [Fact]
    public void Autenticar_ShouldReturnFailure_WhenPasswordIsWrong()
    {
        // Arrange
        const string email = "user@example.com";
        const string wrongPassword = "WrongPass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(wrongPassword, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(false);

        // Act
        var result = _authService.Autenticar(email, wrongPassword, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be("E-mail ou senha inválidos.");

        // Verify failed login count increased
        _userRepositoryMock.Verify(x => x.IncrementarFalhaLogin(user.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e => e.Acao == "LOGIN_FALHA" && e.Detalhes == "Senha inválida"),
            true
        ), Times.Once);
    }

    [Fact]
    public void Autenticar_ShouldLockoutUser_After5FailedAttempts()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "WrongPass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);
        user.FalhasLogin = 4; // Already has 4 failures

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(false);
        _userRepositoryMock.Setup(x => x.IncrementarFalhaLogin(
                user.Id,
                It.IsAny<DateTime>(),
                It.IsAny<int>(),
                It.IsAny<DateTime>()))
            .Returns(new LockoutUpdateResult(0, 1, DateTime.UtcNow.AddMinutes(15), true));

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();

        // Verify lockout was set (FalhasLogin reseta para 0 apos lockout, LockoutsConsecutivos incrementa)
        _userRepositoryMock.Verify(x => x.IncrementarFalhaLogin(user.Id, It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<DateTime>()), Times.Once);

        // Verify lockout audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e => e.Acao == "LOCKOUT"),
            true
        ), Times.Once);
    }

    [Fact]
    public void Autenticar_ShouldReturnBlocked_WhenUserIsLockedOut()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);
        user.LockoutAteUtc = DateTime.UtcNow.AddMinutes(5); // Locked for 5 more minutes

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Bloqueado.Should().BeTrue();
        result.Mensagem.Should().Contain("bloqueada temporariamente");

        // Verify password was not checked
        _passwordHasherMock.Verify(x => x.Verify(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void Autenticar_ShouldAllowLogin_WhenLockoutHasExpired()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);
        user.LockoutAteUtc = DateTime.UtcNow.AddSeconds(-1); // Lockout expired 1 second ago
        user.FalhasLogin = 5;

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeTrue();

        // Verify lockout and failures were reset
        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.FalhasLogin == 0 &&
            u.LockoutAteUtc == null
        )), Times.Once);
    }

    [Fact]
    public void Autenticar_ShouldReturnPendente_WhenUserStatusIsPendente()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Pendente, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Pendente.Should().BeTrue();
        result.Mensagem.Should().Contain("pendente de aprovação");
    }

    [Fact]
    public void Autenticar_ShouldReturnBlocked_WhenUserStatusIsBloqueado()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(email, UserStatus.Bloqueado, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Bloqueado.Should().BeTrue();
        result.Mensagem.Should().Contain("Conta bloqueada");
    }

    [Fact]
    public void Autenticar_ShouldAllowAdminLogin_WhenUserIsAdmin()
    {
        // Arrange
        const string email = "admin@example.com";
        const string password = "AdminPass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Admin);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: true);

        // Assert
        result.Sucesso.Should().BeTrue();
        result.UserId.Should().Be(user.Id);
    }

    [Fact]
    public void Autenticar_ShouldDenyAdminLogin_WhenUserIsNotAdmin()
    {
        // Arrange
        const string email = "user@example.com";
        const string password = "UserPass123";
        var user = CreateTestUser(email, UserStatus.Ativo, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(email)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: true);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.SemPermissao.Should().BeTrue();
        result.Mensagem.Should().Contain("não tem permissão");
    }

    [Theory]
    [InlineData("USER@EXAMPLE.COM")] // Uppercase
    [InlineData("User@Example.Com")] // Mixed case
    [InlineData(" user@example.com ")] // With spaces
    public void Autenticar_ShouldNormalizeEmail(string email)
    {
        // Arrange
        const string normalizedEmail = "user@example.com";
        const string password = "SecurePass123";
        var user = CreateTestUser(normalizedEmail, UserStatus.Ativo, UserRole.Usuario);

        _userRepositoryMock.Setup(x => x.GetByEmail(normalizedEmail)).Returns(user);
        _passwordHasherMock.Setup(x => x.Verify(password, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2))
            .Returns(true);

        // Act
        var result = _authService.Autenticar(email, password, modoAdmin: false);

        // Assert
        result.Sucesso.Should().BeTrue();
        _userRepositoryMock.Verify(x => x.GetByEmail(normalizedEmail), Times.Once);
    }

    #endregion

    #region CriarConta Tests

    [Fact]
    public void CriarConta_ShouldReturnSuccess_WhenDataIsValid()
    {
        // Arrange
        const string password = "SecurePass123";
        var novoUsuario = new User
        {
            Empresa = "Test Corp",
            Nome = "Test User",
            Cargo = "Dev",
            Email = "newuser@example.com",
            Cpf = "123.456.789-09"
        };

        _userRepositoryMock.Setup(x => x.GetByEmail(novoUsuario.Email)).Returns((User?)null);
        _userRepositoryMock.Setup(x => x.HasAnyUsers()).Returns(true);
        _userRepositoryMock.Setup(x => x.Create(It.IsAny<User>())).Returns(1);
        _passwordHasherMock.Setup(x => x.HashPassword(password, null))
            .Returns(("hash", "salt", 100000));

        // Act
        var result = _authService.CriarConta(novoUsuario, password);

        // Assert
        result.Sucesso.Should().BeTrue();
        result.Mensagem.Should().Contain("aprovação");

        // Verify user was created with correct status
        _userRepositoryMock.Verify(x => x.Create(It.Is<User>(u =>
            u.Status == UserStatus.Pendente &&
            u.Role == UserRole.Usuario &&
            u.SenhaHash == "hash" &&
            u.SenhaSalt == "salt" &&
            u.IteracoesPbkdf2 == 100000
        )), Times.Once);

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e => e.Acao == "CRIAR_CONTA" && e.Resultado == "OK"),
            true
        ), Times.Once);
    }

    [Fact]
    public void CriarConta_ShouldReturnFailure_WhenEmailIsInvalid()
    {
        // Arrange
        const string password = "SecurePass123";
        var novoUsuario = new User
        {
            Empresa = "Test",
            Nome = "Test User",
            Cargo = "Dev",
            Email = "invalid-email",
            Cpf = "123.456.789-09"
        };

        // Act
        var result = _authService.CriarConta(novoUsuario, password);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Contain("Dados inválidos");

        // Verify no user was created
        _userRepositoryMock.Verify(x => x.Create(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void CriarConta_ShouldReturnFailure_WhenCpfIsInvalid()
    {
        // Arrange
        const string password = "SecurePass123";
        var novoUsuario = new User
        {
            Empresa = "Test",
            Nome = "Test User",
            Cargo = "Dev",
            Email = "user@example.com",
            Cpf = "111.111.111-11" // All same digits - invalid
        };

        // Act
        var result = _authService.CriarConta(novoUsuario, password);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Contain("Dados inválidos");
    }

    [Fact]
    public void CriarConta_ShouldReturnFailure_WhenPasswordIsTooShort()
    {
        // Arrange
        const string shortPassword = "123"; // Less than 8 characters
        var novoUsuario = new User
        {
            Empresa = "Test",
            Nome = "Test User",
            Cargo = "Dev",
            Email = "user@example.com",
            Cpf = "123.456.789-09"
        };

        // Act
        var result = _authService.CriarConta(novoUsuario, shortPassword);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Be(PasswordPolicy.ObterRequisitos());
    }

    [Fact]
    public void CriarConta_ShouldReturnFailure_WhenEmailAlreadyExists()
    {
        // Arrange
        const string password = "SecurePass123";
        const string existingEmail = "existing@example.com";
        var novoUsuario = new User
        {
            Empresa = "Test",
            Nome = "Test User",
            Cargo = "Dev",
            Email = existingEmail,
            Cpf = "123.456.789-09"
        };

        var existingUser = CreateTestUser(existingEmail, UserStatus.Ativo, UserRole.Usuario);
        _userRepositoryMock.Setup(x => x.GetByEmail(existingEmail)).Returns(existingUser);

        // Act
        var result = _authService.CriarConta(novoUsuario, password);

        // Assert
        result.Sucesso.Should().BeFalse();
        result.Mensagem.Should().Contain("já cadastrado");

        // Verify no new user was created
        _userRepositoryMock.Verify(x => x.Create(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void CriarConta_ShouldNormalizeEmail()
    {
        // Arrange
        const string password = "SecurePass123";
        var novoUsuario = new User
        {
            Empresa = "Test",
            Nome = "Test User",
            Cargo = "Dev",
            Email = " USER@EXAMPLE.COM ", // Uppercase with spaces
            Cpf = "123.456.789-09"
        };

        _userRepositoryMock.Setup(x => x.GetByEmail("user@example.com")).Returns((User?)null);
        _userRepositoryMock.Setup(x => x.Create(It.IsAny<User>())).Returns(1);
        _passwordHasherMock.Setup(x => x.HashPassword(password, null))
            .Returns(("hash", "salt", 100000));

        // Act
        var result = _authService.CriarConta(novoUsuario, password);

        // Assert
        result.Sucesso.Should().BeTrue();
        _userRepositoryMock.Verify(x => x.GetByEmail("user@example.com"), Times.Once);
    }

    [Fact]
    public void CriarConta_ShouldReturnFailure_WhenEmpresaNomeOuCargoVazios()
    {
        const string password = "SecurePass123";
        _userRepositoryMock.Setup(x => x.GetByEmail(It.IsAny<string>())).Returns((User?)null);

        var semEmpresa = new User { Nome = "Test", Cargo = "Dev", Email = "a@b.com", Cpf = "123.456.789-09" };
        _authService.CriarConta(semEmpresa, password).Sucesso.Should().BeFalse();
        _authService.CriarConta(semEmpresa, password).Mensagem.Should().Contain("Empresa");

        var semNome = new User { Empresa = "Test", Cargo = "Dev", Email = "a@b.com", Cpf = "123.456.789-09" };
        _authService.CriarConta(semNome, password).Sucesso.Should().BeFalse();
        _authService.CriarConta(semNome, password).Mensagem.Should().Contain("Nome");

        var semCargo = new User { Empresa = "Test", Nome = "Test", Email = "a@b.com", Cpf = "123.456.789-09" };
        _authService.CriarConta(semCargo, password).Sucesso.Should().BeFalse();
        _authService.CriarConta(semCargo, password).Mensagem.Should().Contain("Cargo");

        _userRepositoryMock.Verify(x => x.Create(It.IsAny<User>()), Times.Never);
    }

    #endregion

    #region AprovarUsuario Tests

    [Fact]
    public void AprovarUsuario_ShouldUpdateUserStatus_WhenUserExists()
    {
        // Arrange
        const int userId = 1;
        const int adminId = 100;
        const string motivo = "Documentação aprovada";
        var user = CreateTestUser("user@example.com", UserStatus.Pendente, UserRole.Usuario);
        user.Id = userId;

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns(user);

        // Act
        _authService.AprovarUsuario(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.Status == UserStatus.Ativo
        )), Times.Once);

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "APROVAR_USUARIO" &&
                e.Detalhes == motivo
            ),
            true
        ), Times.Once);
    }

    [Fact]
    public void AprovarUsuario_ShouldDoNothing_WhenUserNotFound()
    {
        // Arrange
        const int userId = 999;
        const int adminId = 100;
        const string motivo = "Test";

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns((User?)null);

        // Act
        _authService.AprovarUsuario(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "APROVAR_USUARIO" &&
                e.Resultado == "ERRO" &&
                e.Detalhes!.Contains(userId.ToString())
            ),
            true
        ), Times.Once);
    }

    #endregion

    #region PromoverUsuarioAdmin Tests

    [Fact]
    public void PromoverUsuarioAdmin_ShouldPromoteRoleAndActivate_WhenUserExists()
    {
        // Arrange
        const int userId = 5;
        const int adminId = 100;
        const string motivo = "Promovido para administrador";
        var user = CreateTestUser("operador@example.com", UserStatus.Pendente, UserRole.Usuario);
        user.Id = userId;

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns(user);

        // Act
        _authService.PromoverUsuarioAdmin(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.Id == userId &&
            u.Role == UserRole.Admin &&
            u.Status == UserStatus.Ativo
        )), Times.Once);

        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "PROMOVER_ADMIN" &&
                e.Resultado == "OK" &&
                e.Detalhes == motivo),
            true
        ), Times.Once);
    }

    [Fact]
    public void PromoverUsuarioAdmin_ShouldDoNothing_WhenUserNotFound()
    {
        // Arrange
        const int userId = 999;
        const int adminId = 100;
        const string motivo = "Promocao";

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns((User?)null);

        // Act
        _authService.PromoverUsuarioAdmin(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "PROMOVER_ADMIN" &&
                e.Resultado == "ERRO" &&
                e.Detalhes!.Contains(userId.ToString())),
            true
        ), Times.Once);
    }

    #endregion

    #region RejeitarUsuario Tests

    [Fact]
    public void RejeitarUsuario_ShouldUpdateUserStatus_WhenUserExists()
    {
        // Arrange
        const int userId = 1;
        const int adminId = 100;
        const string motivo = "Documentação inválida";
        var user = CreateTestUser("user@example.com", UserStatus.Pendente, UserRole.Usuario);
        user.Id = userId;

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns(user);

        // Act
        _authService.RejeitarUsuario(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.Is<User>(u =>
            u.Status == UserStatus.Bloqueado
        )), Times.Once);

        // Verify audit log
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "REJEITAR_USUARIO" &&
                e.Detalhes == motivo
            ),
            true
        ), Times.Once);
    }

    [Fact]
    public void RejeitarUsuario_ShouldDoNothing_WhenUserNotFound()
    {
        // Arrange
        const int userId = 999;
        const int adminId = 100;
        const string motivo = "Test";

        _userRepositoryMock.Setup(x => x.GetById(userId)).Returns((User?)null);

        // Act
        _authService.RejeitarUsuario(userId, adminId, motivo);

        // Assert
        _userRepositoryMock.Verify(x => x.Update(It.IsAny<User>()), Times.Never);
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == adminId &&
                e.Acao == "REJEITAR_USUARIO" &&
                e.Resultado == "ERRO" &&
                e.Detalhes!.Contains(userId.ToString())
            ),
            true
        ), Times.Once);
    }

    #endregion

    #region ListarPendentes Tests

    [Fact]
    public void ListarPendentes_ShouldReturnPendingUsers()
    {
        // Arrange
        var pendingUsers = new List<User>
        {
            CreateTestUser("user1@example.com", UserStatus.Pendente, UserRole.Usuario),
            CreateTestUser("user2@example.com", UserStatus.Pendente, UserRole.Usuario)
        };

        _userRepositoryMock.Setup(x => x.GetPendentes()).Returns(pendingUsers);

        // Act
        var result = _authService.ListarPendentes();

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(pendingUsers);
    }

    #endregion

    #region RegistrarLogout Tests

    [Fact]
    public void RegistrarLogout_ShouldRecordAuditLog()
    {
        // Arrange
        const int userId = 1;
        const string email = "user@example.com";

        // Act
        _authService.RegistrarLogout(userId, email);

        // Assert
        _auditServiceMock.Verify(x => x.Registrar(
            It.Is<AuditLogEntry>(e =>
                e.UserId == userId &&
                e.EmailSnapshot == email &&
                e.Acao == "LOGOUT" &&
                e.Resultado == "OK"
            ),
            true
        ), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static User CreateTestUser(string email, UserStatus status, UserRole role)
    {
        return new User
        {
            Id = 1,
            Nome = "Test User",
            Email = email,
            Cpf = "123.456.789-09",
            SenhaHash = "hash",
            SenhaSalt = "salt",
            IteracoesPbkdf2 = 100000,
            Status = status,
            Role = role,
            FalhasLogin = 0,
            LockoutAteUtc = null,
            CriadoEmUtc = DateTime.UtcNow,
            AtualizadoEmUtc = DateTime.UtcNow
        };
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    #endregion
}
