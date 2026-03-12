using Protons.Core.Login.Models;

namespace Protons.Core.Login.Repositories;

public interface IUserRepository
{
    User? GetByEmail(string? email);
    User? GetById(int id);
    IReadOnlyList<User> GetPendentes();
    IReadOnlyList<User> ListarAtivos();
    /// <summary>Lista subordinados diretos de um admin/supremo (todos que têm ResponsavelAdminId = adminId).</summary>
    IReadOnlyList<User> ListarPorAdmin(int adminId);
    /// <summary>Lista todos os usuários ativos (uso exclusivo do Supremo).</summary>
    IReadOnlyList<User> ListarTodos();
    bool HasAnyUsers();
    int Create(User user);
    void Update(User user);
    /// <summary>Exclusão suave: define Status=Excluido e ExcluidoEmUtc, preserva histórico.</summary>
    void ExcluirSoft(int userId, DateTime excluidoEmUtc);
    /// <summary>Transfere usuário para outro admin atomicamente.</summary>
    void TransferirParaAdmin(int userId, int novoAdminId, DateTime nowUtc);
    /// <summary>Reatribui todas as tarefas Agendada/Ativa do usuário excluído para outro usuário.</summary>
    void RedistribuirTarefas(int usuarioOrigemId, int usuarioDestinoId, DateTime nowUtc);
    /// <summary>
    ///   Reatribui todos os subordinados de um admin excluído para outro responsável.
    ///   Chamado quando um Admin é excluído para evitar que seus subordinados fiquem órfãos.
    /// </summary>
    void RedistribuirSubordinados(int deAdminId, int paraAdminId, DateTime nowUtc);
    LockoutUpdateResult IncrementarFalhaLogin(int userId, DateTime nowUtc, int maxFalhas, DateTime lockoutAteUtc);
}

public sealed record LockoutUpdateResult(int FalhasLogin, int LockoutsConsecutivos, DateTime? LockoutAteUtc, bool LockoutAplicado);
