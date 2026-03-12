using Protons.Core.Login.Models;

namespace Protons.Core.Login.Services;

public interface IAuthService
{
    AuthResult Autenticar(string email, string senha, bool modoAdmin);
    AuthResult CriarConta(User novoUsuario, string senha);
    void AprovarUsuario(int userId, int adminId, string motivo);
    void RejeitarUsuario(int userId, int adminId, string motivo);
    void PromoverUsuarioAdmin(int userId, int adminId, string motivo);
    IReadOnlyList<User> ListarPendentes();
    void RegistrarLogout(int userId, string email);

    // C10 — Controle de acesso avançado
    /// <summary>Exclusão suave: redistribui tarefas ao admin responsável e marca como Excluido.</summary>
    ControleAcessoResultado ExcluirUsuario(ExclusaoUsuarioEntrada entrada);
    /// <summary>Transfere usuário e suas tarefas para outro admin (operação do Supremo).</summary>
    ControleAcessoResultado TransferirUsuario(TransferenciaUsuarioEntrada entrada);
    /// <summary>Lista usuários ativos visíveis para o aprovador (Supremo vê todos; Admin vê os seus).</summary>
    IReadOnlyList<User> ListarSubordinados(int adminId, UserRole adminRole);
}
