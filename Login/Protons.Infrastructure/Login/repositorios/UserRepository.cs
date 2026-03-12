using System.Globalization;
using Microsoft.Data.Sqlite;
using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;
using Protons.Infrastructure.Login.Database;

namespace Protons.Infrastructure.Login.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly SqliteDb _db;

    public UserRepository(SqliteDb db)
    {
        _db = db;
    }

    public User? GetByEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Email = $email LIMIT 1";
        cmd.Parameters.AddWithValue("$email", email);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public User? GetById(int id)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);

        using var reader = cmd.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    public IReadOnlyList<User> GetPendentes()
    {
        var list = new List<User>();
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Status = $status ORDER BY CriadoEmUtc ASC";
        cmd.Parameters.AddWithValue("$status", UserStatus.Pendente.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public IReadOnlyList<User> ListarAtivos()
    {
        var list = new List<User>();
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM Users WHERE Status = $status ORDER BY Nome COLLATE NOCASE ASC";
        cmd.Parameters.AddWithValue("$status", UserStatus.Ativo.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }

        return list;
    }

    public IReadOnlyList<User> ListarPorAdmin(int adminId)
    {
        var list = new List<User>();
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM Users
WHERE ResponsavelAdminId = $adminId
  AND Status != $excluido
ORDER BY Nome COLLATE NOCASE ASC";
        cmd.Parameters.AddWithValue("$adminId", adminId);
        cmd.Parameters.AddWithValue("$excluido", UserStatus.Excluido.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public IReadOnlyList<User> ListarTodos()
    {
        var list = new List<User>();
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
SELECT * FROM Users
WHERE Status != $excluido
ORDER BY Nome COLLATE NOCASE ASC";
        cmd.Parameters.AddWithValue("$excluido", UserStatus.Excluido.ToString());

        using var reader = cmd.ExecuteReader();
        while (reader.Read()) list.Add(Map(reader));
        return list;
    }

    public bool HasAnyUsers()
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM Users LIMIT 1";
        var result = cmd.ExecuteScalar();
        return result is not null && result != DBNull.Value;
    }

    public int Create(User user)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
INSERT INTO Users (Empresa, Nome, CPF, Cargo, Email, SenhaHash, SenhaSalt, IteracoesPBKDF2, Status, Role, FalhasLogin, LockoutsConsecutivos, LockoutAteUtc, CriadoEmUtc, AtualizadoEmUtc, UltimoLoginUtc, ResponsavelAdminId, ExcluidoEmUtc)
VALUES ($empresa, $nome, $cpf, $cargo, $email, $hash, $salt, $iter, $status, $role, $falhas, $lockoutsConsec, $lockout, $criado, $atualizado, $ultimo, $adminId, $excluido);
SELECT last_insert_rowid();
";
        Bind(cmd, user);

        var result = cmd.ExecuteScalar();
        if (result is null || result == DBNull.Value)
            throw new InvalidOperationException("Create user failed: no identity returned. Check constraints and connection.");
        var id = Convert.ToInt64(result);
        return (int)id;
    }

    public void Update(User user)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Users SET
  Empresa = $empresa,
  Nome = $nome,
  CPF = $cpf,
  Cargo = $cargo,
  Email = $email,
  SenhaHash = $hash,
  SenhaSalt = $salt,
  IteracoesPBKDF2 = $iter,
  Status = $status,
  Role = $role,
  FalhasLogin = $falhas,
  LockoutsConsecutivos = $lockoutsConsec,
  LockoutAteUtc = $lockout,
  CriadoEmUtc = $criado,
  AtualizadoEmUtc = $atualizado,
  UltimoLoginUtc = $ultimo,
  ResponsavelAdminId = $adminId,
  ExcluidoEmUtc = $excluido
WHERE Id = $id;
";
        Bind(cmd, user);
        cmd.Parameters.AddWithValue("$id", user.Id);
        cmd.ExecuteNonQuery();
    }

    public void ExcluirSoft(int userId, DateTime excluidoEmUtc)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Users SET
  Status = $excluido,
  ExcluidoEmUtc = $excluidoEm,
  AtualizadoEmUtc = $now
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$excluido", UserStatus.Excluido.ToString());
        cmd.Parameters.AddWithValue("$excluidoEm", excluidoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$now", excluidoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.ExecuteNonQuery();
    }

    public void TransferirParaAdmin(int userId, int novoAdminId, DateTime nowUtc)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        cmd.CommandText = @"
UPDATE Users SET
  ResponsavelAdminId = $novoAdmin,
  AtualizadoEmUtc = $now
WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$novoAdmin", novoAdminId);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.ExecuteNonQuery();
    }

    public void RedistribuirTarefas(int usuarioOrigemId, int usuarioDestinoId, DateTime nowUtc)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        // Reatribui tarefas Agendada ou Ativa do usuário excluído para o admin destino.
        cmd.CommandText = @"
UPDATE Tarefas SET
  ResponsavelUserId = $destino,
  AtualizadoEmUtc = $now
WHERE ResponsavelUserId = $origem
  AND Status IN ('Agendada', 'Ativa', 'Pendente');";
        cmd.Parameters.AddWithValue("$destino", usuarioDestinoId);
        cmd.Parameters.AddWithValue("$origem", usuarioOrigemId);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void RedistribuirSubordinados(int deAdminId, int paraAdminId, DateTime nowUtc)
    {
        using var connection = new SqliteConnection(_db.ConnectionString);
        connection.Open();
        var cmd = connection.CreateCommand();
        // Reatribui todos os subordinados não-excluídos do admin excluído para o novo responsável.
        cmd.CommandText = @"
UPDATE Users SET
  ResponsavelAdminId = $paraAdmin,
  AtualizadoEmUtc = $now
WHERE ResponsavelAdminId = $deAdmin
  AND Status != $excluido;";
        cmd.Parameters.AddWithValue("$paraAdmin", paraAdminId);
        cmd.Parameters.AddWithValue("$deAdmin", deAdminId);
        cmd.Parameters.AddWithValue("$now", nowUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$excluido", UserStatus.Excluido.ToString());
        cmd.ExecuteNonQuery();
    }

    public LockoutUpdateResult IncrementarFalhaLogin(int userId, DateTime nowUtc, int maxFalhas, DateTime lockoutAteUtc)
    {
        using var connection = _db.Open();
        using var transaction = connection.BeginTransaction();

        var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
UPDATE Users
SET
  FalhasLogin = CASE
    WHEN FalhasLogin + 1 >= $maxFalhas THEN 0
    ELSE FalhasLogin + 1
  END,
  LockoutsConsecutivos = CASE
    WHEN FalhasLogin + 1 >= $maxFalhas THEN LockoutsConsecutivos + 1
    ELSE LockoutsConsecutivos
  END,
  LockoutAteUtc = CASE
    WHEN FalhasLogin + 1 >= $maxFalhas THEN $lockoutAteUtc
    ELSE LockoutAteUtc
  END,
  AtualizadoEmUtc = $nowUtc
WHERE Id = $id;
";
        cmd.Parameters.AddWithValue("$id", userId);
        cmd.Parameters.AddWithValue("$maxFalhas", maxFalhas);
        cmd.Parameters.AddWithValue("$lockoutAteUtc", lockoutAteUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$nowUtc", nowUtc.ToString("o"));
        cmd.ExecuteNonQuery();

        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = @"
SELECT FalhasLogin, LockoutsConsecutivos, LockoutAteUtc
FROM Users WHERE Id = $id;
";
        select.Parameters.AddWithValue("$id", userId);
        using var reader = select.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("Usuário não encontrado para atualizar falhas.");

        var falhas = reader.GetInt32(0);
        var lockouts = reader.GetInt32(1);
        var lockoutRaw = reader.IsDBNull(2) ? null : reader.GetString(2);
        var lockout = string.IsNullOrWhiteSpace(lockoutRaw) ? (DateTime?)null : DateTime.Parse(lockoutRaw, null, System.Globalization.DateTimeStyles.RoundtripKind);
        var aplicado = falhas == 0 && lockout.HasValue;

        transaction.Commit();
        return new LockoutUpdateResult(falhas, lockouts, lockout, aplicado);
    }

    private static void Bind(SqliteCommand cmd, User user)
    {
        cmd.Parameters.AddWithValue("$empresa", user.Empresa);
        cmd.Parameters.AddWithValue("$nome", user.Nome);
        cmd.Parameters.AddWithValue("$cpf", user.Cpf);
        cmd.Parameters.AddWithValue("$cargo", user.Cargo);
        cmd.Parameters.AddWithValue("$email", user.Email);
        cmd.Parameters.AddWithValue("$hash", user.SenhaHash);
        cmd.Parameters.AddWithValue("$salt", user.SenhaSalt);
        cmd.Parameters.AddWithValue("$iter", user.IteracoesPbkdf2);
        cmd.Parameters.AddWithValue("$status", user.Status.ToString());
        cmd.Parameters.AddWithValue("$role", user.Role.ToString());
        cmd.Parameters.AddWithValue("$falhas", user.FalhasLogin);
        cmd.Parameters.AddWithValue("$lockoutsConsec", user.LockoutsConsecutivos);
        cmd.Parameters.AddWithValue("$lockout", (object?)user.LockoutAteUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$criado", user.CriadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$atualizado", user.AtualizadoEmUtc.ToString("o"));
        cmd.Parameters.AddWithValue("$ultimo", (object?)user.UltimoLoginUtc?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$adminId", (object?)user.ResponsavelAdminId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$excluido", (object?)user.ExcluidoEmUtc?.ToString("o") ?? DBNull.Value);
    }

    private static User Map(SqliteDataReader reader)
    {
        return new User
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Empresa = reader.GetString(reader.GetOrdinal("Empresa")),
            Nome = reader.GetString(reader.GetOrdinal("Nome")),
            Cpf = reader.GetString(reader.GetOrdinal("CPF")),
            Cargo = reader.GetString(reader.GetOrdinal("Cargo")),
            Email = reader.GetString(reader.GetOrdinal("Email")),
            SenhaHash = reader.GetString(reader.GetOrdinal("SenhaHash")),
            SenhaSalt = reader.GetString(reader.GetOrdinal("SenhaSalt")),
            IteracoesPbkdf2 = reader.GetInt32(reader.GetOrdinal("IteracoesPBKDF2")),
            Status = Enum.TryParse<UserStatus>(reader.GetString(reader.GetOrdinal("Status")), true, out var status) ? status : UserStatus.Pendente,
            Role = Enum.TryParse<UserRole>(reader.GetString(reader.GetOrdinal("Role")), true, out var role) ? role : UserRole.Usuario,
            FalhasLogin = reader.GetInt32(reader.GetOrdinal("FalhasLogin")),
            LockoutsConsecutivos = reader.GetInt32(reader.GetOrdinal("LockoutsConsecutivos")),
            LockoutAteUtc = TryReadDate(reader, "LockoutAteUtc"),
            CriadoEmUtc = ReadRequiredDate(reader, "CriadoEmUtc"),
            AtualizadoEmUtc = ReadRequiredDate(reader, "AtualizadoEmUtc"),
            UltimoLoginUtc = TryReadDate(reader, "UltimoLoginUtc"),
            ResponsavelAdminId = TryReadNullableInt(reader, "ResponsavelAdminId"),
            ExcluidoEmUtc = TryReadDate(reader, "ExcluidoEmUtc")
        };
    }

    private static int? TryReadNullableInt(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }
        catch (IndexOutOfRangeException)
        {
            return null; // coluna ainda não existe (pré-v12)
        }
    }

    private static DateTime? TryReadDate(SqliteDataReader reader, string column)
    {
        try
        {
            var ordinal = reader.GetOrdinal(column);
            if (reader.IsDBNull(ordinal))
                return null;
            var raw = reader.GetString(ordinal);
            return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime()
                : null;
        }
        catch (IndexOutOfRangeException)
        {
            return null; // coluna inexistente em schema pré-migração
        }
    }

    private static DateTime ReadRequiredDate(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
            return DateTime.UtcNow;
        var raw = reader.GetString(ordinal);
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.UtcNow;
    }
}
