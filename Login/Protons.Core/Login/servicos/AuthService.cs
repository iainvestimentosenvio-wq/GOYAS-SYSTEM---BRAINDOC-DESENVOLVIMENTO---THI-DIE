using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Core.Login.Validation;

namespace Protons.Core.Login.Services;


public sealed class AuthService : IAuthService
{
    private const int MaxFalhas = 5;

    private readonly IUserRepository _users;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAuditService _audit;
    private readonly string _maquina;
    private readonly string _versaoApp;
    private readonly bool _usarHashChain;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        IUserRepository users,
        IPasswordHasher passwordHasher,
        IAuditService audit,
        string maquina,
        string versaoApp,
        bool usarHashChain,
        TimeProvider? timeProvider = null)
    {
        _users = users;
        _passwordHasher = passwordHasher;
        _audit = audit;
        _maquina = maquina ?? string.Empty;
        _versaoApp = string.IsNullOrWhiteSpace(versaoApp) ? "0.0.0" : versaoApp;
        _usarHashChain = usarHashChain;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AuthResult Autenticar(string email, string senha, bool modoAdmin)
    {
        var emailNorm = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(emailNorm) || !EmailValidator.EhValido(emailNorm) || !PasswordPolicy.EhValidaParaLogin(senha))
        {
            RegistrarAudit(null, emailNorm, "LOGIN_FALHA", "ERRO", "Credenciais inválidas");
            return Falha("E-mail ou senha inválidos.");
        }

        var user = _users.GetByEmail(emailNorm);
        if (user is null)
        {
            RegistrarAudit(null, emailNorm, "LOGIN_FALHA", "ERRO", "Usuário não encontrado");
            return Falha("E-mail ou senha inválidos.");
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (user.LockoutAteUtc.HasValue && user.LockoutAteUtc.Value > nowUtc)
        {
            var tempoRestante = user.LockoutAteUtc.Value - nowUtc;
            var mensagemTempo = tempoRestante.TotalMinutes >= 1
                ? $"Tente novamente em {(int)tempoRestante.TotalMinutes} minuto(s)."
                : $"Tente novamente em {(int)tempoRestante.TotalSeconds} segundo(s).";
            return new AuthResult
            {
                Sucesso = false,
                Bloqueado = true,
                Mensagem = $"Conta bloqueada temporariamente. {mensagemTempo}"
            };
        }

        var senhaOk = _passwordHasher.Verify(senha, user.SenhaHash, user.SenhaSalt, user.IteracoesPbkdf2);
        if (!senhaOk)
        {
            nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
            var proximaDuracao = CalcularLockoutDuracao(user.LockoutsConsecutivos + 1);
            var lockoutAte = nowUtc.Add(proximaDuracao);
            var update = _users.IncrementarFalhaLogin(user.Id, nowUtc, MaxFalhas, lockoutAte);

            if (update.LockoutAplicado)
            {
                RegistrarAudit(user.Id, user.Email, "LOCKOUT", "ERRO",
                    $"Bloqueio nível {update.LockoutsConsecutivos} por {(int)proximaDuracao.TotalMinutes} minutos");
            }

            RegistrarAudit(user.Id, user.Email, "LOGIN_FALHA", "ERRO", "Senha inválida");
            return Falha("E-mail ou senha inválidos.");
        }

        if (user.Status == UserStatus.Pendente)
        {
            RegistrarAudit(user.Id, user.Email, "LOGIN_FALHA", "ERRO", "Usuário pendente");
            return new AuthResult { Sucesso = false, Pendente = true, Mensagem = "Sua conta está pendente de aprovação." };
        }

        if (user.Status == UserStatus.Bloqueado)
        {
            RegistrarAudit(user.Id, user.Email, "LOGIN_FALHA", "ERRO", "Usuário bloqueado");
            return new AuthResult { Sucesso = false, Bloqueado = true, Mensagem = "Conta bloqueada. Procure o administrador." };
        }

        if (modoAdmin && user.Role != UserRole.Admin && user.Role != UserRole.Supremo)
        {
            RegistrarAudit(user.Id, user.Email, "LOGIN_FALHA", "ERRO", "Sem permissão de admin");
            return new AuthResult { Sucesso = false, SemPermissao = true, Mensagem = "Você não tem permissão para acessar o modo administrador." };
        }

        user.FalhasLogin = 0;
        user.LockoutsConsecutivos = 0;
        user.LockoutAteUtc = null;
        nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        user.UltimoLoginUtc = nowUtc;
        user.AtualizadoEmUtc = nowUtc;
        _users.Update(user);

        RegistrarAudit(user.Id, user.Email, "LOGIN_SUCESSO", "OK", "Login realizado");
        return new AuthResult
        {
            Sucesso = true,
            Mensagem = "Login realizado com sucesso.",
            UserId = user.Id,
            Nome = user.Nome,
            Role = user.Role
        };
    }

    public AuthResult CriarConta(User novoUsuario, string senha)
    {
        novoUsuario.Email = (novoUsuario.Email ?? string.Empty).Trim().ToLowerInvariant();
        novoUsuario.Empresa = (novoUsuario.Empresa ?? string.Empty).Trim();
        novoUsuario.Nome = (novoUsuario.Nome ?? string.Empty).Trim();
        novoUsuario.Cargo = (novoUsuario.Cargo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(novoUsuario.Empresa) || string.IsNullOrWhiteSpace(novoUsuario.Nome) || string.IsNullOrWhiteSpace(novoUsuario.Cargo))
        {
            return Falha("Preencha Empresa, Nome e Cargo.");
        }
        if (novoUsuario.Empresa.Length > InputLimits.MaxEmpresa ||
            novoUsuario.Nome.Length > InputLimits.MaxNome ||
            novoUsuario.Cargo.Length > InputLimits.MaxCargo ||
            novoUsuario.Email.Length > InputLimits.MaxEmail ||
            (novoUsuario.Cpf?.Length ?? 0) > InputLimits.MaxCpf)
        {
            return Falha("Um ou mais campos excedem o limite de tamanho permitido.");
        }
        if (!EmailValidator.EhValido(novoUsuario.Email) || !CpfValidator.EhValido(novoUsuario.Cpf))
        {
            return Falha("Dados inválidos. Verifique e tente novamente.");
        }

        if (!PasswordPolicy.EhValida(senha))
        {
            return Falha(PasswordPolicy.ObterRequisitos());
        }

        if (_users.GetByEmail(novoUsuario.Email) is not null)
        {
            return Falha("E-mail já cadastrado.");
        }

        var (hash, salt, iterations) = _passwordHasher.HashPassword(senha);
        novoUsuario.SenhaHash = hash;
        novoUsuario.SenhaSalt = salt;
        novoUsuario.IteracoesPbkdf2 = iterations;
        var isFirstUser = !_users.HasAnyUsers();
        novoUsuario.Status = isFirstUser ? UserStatus.Ativo : UserStatus.Pendente;
        novoUsuario.Role = isFirstUser ? UserRole.Supremo : UserRole.Usuario;
        novoUsuario.ResponsavelAdminId = null; // Supremo não tem admin responsável
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        novoUsuario.CriadoEmUtc = nowUtc;
        novoUsuario.AtualizadoEmUtc = nowUtc;

        var id = _users.Create(novoUsuario);
        var detalhes = isFirstUser
            ? "Conta criada como Supremo (primeiro usuário)"
            : "Conta criada com status pendente";
        RegistrarAudit(id, novoUsuario.Email, "CRIAR_CONTA", "OK", detalhes);

        return new AuthResult
        {
            Sucesso = true,
            Mensagem = isFirstUser
                ? "Conta criada como Supremo (primeiro usuário)."
                : "Cadastro enviado para aprovação.",
            Role = novoUsuario.Role
        };
    }

    public void AprovarUsuario(int userId, int adminId, string motivo)
    {
        ValidarExecutorAdmin(adminId, "APROVAR_USUARIO");

        var user = _users.GetById(userId);
        if (user is null)
        {
            RegistrarAudit(adminId, null, "APROVAR_USUARIO", "ERRO", $"Usuário inexistente: {userId}");
            return;
        }

        user.Status = UserStatus.Ativo;
        user.AtualizadoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;
        if (user.ResponsavelAdminId is null)
            user.ResponsavelAdminId = adminId;
        _users.Update(user);

        RegistrarAudit(adminId, user.Email, "APROVAR_USUARIO", "OK", motivo);
    }

    public void RejeitarUsuario(int userId, int adminId, string motivo)
    {
        ValidarExecutorAdmin(adminId, "REJEITAR_USUARIO");

        var user = _users.GetById(userId);
        if (user is null)
        {
            RegistrarAudit(adminId, null, "REJEITAR_USUARIO", "ERRO", $"Usuário inexistente: {userId}");
            return;
        }

        user.Status = UserStatus.Bloqueado;
        user.AtualizadoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _users.Update(user);

        RegistrarAudit(adminId, user.Email, "REJEITAR_USUARIO", "OK", motivo);
    }

    public void PromoverUsuarioAdmin(int userId, int adminId, string motivo)
    {
        ValidarExecutorAdmin(adminId, "PROMOVER_ADMIN");

        var user = _users.GetById(userId);
        if (user is null)
        {
            RegistrarAudit(adminId, null, "PROMOVER_ADMIN", "ERRO", $"Usuário inexistente: {userId}");
            return;
        }

        user.Role = UserRole.Admin;
        if (user.Status == UserStatus.Pendente)
            user.Status = UserStatus.Ativo;
        user.AtualizadoEmUtc = _timeProvider.GetUtcNow().UtcDateTime;
        _users.Update(user);

        RegistrarAudit(adminId, user.Email, "PROMOVER_ADMIN", "OK", motivo);
    }

    public IReadOnlyList<User> ListarPendentes()
    {
        return _users.GetPendentes();
    }

    public void RegistrarLogout(int userId, string email)
    {
        RegistrarAudit(userId, email, "LOGOUT", "OK", "Logout registrado");
    }

    public ControleAcessoResultado ExcluirUsuario(ExclusaoUsuarioEntrada entrada)
    {
        ValidarExecutorAdmin(entrada.ExecutorId, "EXCLUIR_USUARIO");

        var user = _users.GetById(entrada.UsuarioId);
        if (user is null)
        {
            RegistrarAudit(entrada.ExecutorId, null, "EXCLUIR_USUARIO", "ERRO", $"Usuário inexistente: {entrada.UsuarioId}");
            return new ControleAcessoResultado(false, "Usuário não encontrado.");
        }

        if (user.Role == UserRole.Supremo)
        {
            RegistrarAudit(entrada.ExecutorId, user.Email, "EXCLUIR_USUARIO", "ERRO", "Tentativa de excluir o Supremo");
            return new ControleAcessoResultado(false, "O usuário Supremo não pode ser excluído.");
        }

        // O destinatário das tarefas e subordinados é o admin responsável pelo usuário excluído,
        // ou o executor (Supremo) quando não há responsável definido.
        var adminDestinoId = user.ResponsavelAdminId ?? entrada.ExecutorId;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        // Conta subordinados ANTES do soft-delete para saber se há órfãos.
        var subordinados = user.Role == UserRole.Admin
            ? _users.ListarPorAdmin(user.Id)
            : [];

        // Redistribui tarefas ativas/pendentes para o admin destino.
        _users.RedistribuirTarefas(user.Id, adminDestinoId, nowUtc);

        // Se o excluído era Admin, reatribui seus subordinados ao admin destino.
        if (subordinados.Count > 0)
            _users.RedistribuirSubordinados(user.Id, adminDestinoId, nowUtc);

        // Soft delete — preserva histórico completo.
        _users.ExcluirSoft(user.Id, nowUtc);

        RegistrarAudit(entrada.ExecutorId, user.Email, "EXCLUIR_USUARIO", "OK",
            $"{entrada.Motivo} tarefa_destino_admin={adminDestinoId} subordinados_redistribuidos={subordinados.Count}");
        return new ControleAcessoResultado(true, null, subordinados.Count);
    }

    public ControleAcessoResultado TransferirUsuario(TransferenciaUsuarioEntrada entrada)
    {
        var user = _users.GetById(entrada.UsuarioId);
        if (user is null)
        {
            RegistrarAudit(entrada.ExecutorId, null, "TRANSFERIR_USUARIO", "ERRO", $"Usuário inexistente: {entrada.UsuarioId}");
            return new ControleAcessoResultado(false, "Usuário não encontrado.");
        }

        // Valida que o executor tem autoridade de Supremo para realizar a transferência.
        var executor = _users.GetById(entrada.ExecutorId);
        if (executor is null || executor.Role != UserRole.Supremo)
        {
            RegistrarAudit(entrada.ExecutorId, user.Email, "TRANSFERIR_USUARIO", "ERRO",
                "Executor sem autoridade de Supremo");
            return new ControleAcessoResultado(false, "Apenas o Supremo pode transferir usuários entre administradores.");
        }

        var novoAdmin = _users.GetById(entrada.NovoAdminId);
        if (novoAdmin is null
            || (novoAdmin.Role != UserRole.Admin && novoAdmin.Role != UserRole.Supremo)
            || novoAdmin.Status == UserStatus.Excluido)
        {
            RegistrarAudit(entrada.ExecutorId, user.Email, "TRANSFERIR_USUARIO", "ERRO",
                $"Destino inválido: {entrada.NovoAdminId}");
            return new ControleAcessoResultado(false, "Admin de destino inválido ou excluído.");
        }

        _users.TransferirParaAdmin(user.Id, entrada.NovoAdminId, _timeProvider.GetUtcNow().UtcDateTime);

        RegistrarAudit(entrada.ExecutorId, user.Email, "TRANSFERIR_USUARIO", "OK",
            $"{entrada.Motivo} novo_admin={entrada.NovoAdminId}");
        return new ControleAcessoResultado(true);
    }

    public IReadOnlyList<User> ListarSubordinados(int adminId, UserRole adminRole)
    {
        if (adminRole == UserRole.Supremo)
            return _users.ListarTodos();

        return _users.ListarPorAdmin(adminId);
    }

    private void ValidarExecutorAdmin(int executorId, string acao)
    {
        var executor = _users.GetById(executorId);
        if (executor is null || executor.Role is not (UserRole.Admin or UserRole.Supremo))
        {
            RegistrarAudit(executorId, null, acao, "ERRO", "Executor sem permissão de administrador");
            throw new InvalidOperationException("Operação permitida somente para Admin ou Supremo.");
        }
    }

    private void RegistrarAudit(int? userId, string? email, string acao, string resultado, string detalhes)
    {
        _audit.Registrar(new AuditLogEntry
        {
            UserId = userId,
            EmailSnapshot = email,
            Acao = acao,
            Resultado = resultado,
            Detalhes = detalhes,
            Maquina = _maquina,
            VersaoApp = _versaoApp
        }, _usarHashChain);
    }

    private static AuthResult Falha(string mensagem)
    {
        return new AuthResult { Sucesso = false, Mensagem = mensagem };
    }

    /// <summary>
    /// Calcula a duração do lockout com backoff exponencial.
    /// 1º lockout: 15 minutos
    /// 2º lockout: 30 minutos
    /// 3º+ lockout: 1 hora
    /// </summary>
    private static TimeSpan CalcularLockoutDuracao(int lockoutsConsecutivos)
    {
        return lockoutsConsecutivos switch
        {
            <= 1 => TimeSpan.FromMinutes(15),
            2 => TimeSpan.FromMinutes(30),
            _ => TimeSpan.FromHours(1)
        };
    }
}
