namespace Protons.Core.Login.Models;

/// <summary>Resultado de operação de controle de acesso.</summary>
/// <param name="Sucesso">Indica se a operação foi bem-sucedida.</param>
/// <param name="Mensagem">Mensagem de erro ou informação adicional.</param>
/// <param name="UsuariosOrfaosTransferidos">
///   Quantidade de usuários subordinados do admin excluído que foram redistribuídos
///   para o executor. Maior que zero apenas quando um Admin com subordinados é excluído.
/// </param>
public sealed record ControleAcessoResultado(
    bool Sucesso,
    string? Mensagem = null,
    int UsuariosOrfaosTransferidos = 0);

/// <summary>Resumo de usuário para listagem hierárquica.</summary>
public sealed record UsuarioHierarquiaItem(
    int Id,
    string Nome,
    string Email,
    string Empresa,
    string Cargo,
    UserRole Role,
    UserStatus Status,
    int? ResponsavelAdminId,
    DateTime CriadoEmUtc);

/// <summary>Entrada para transferência de usuário entre admins (operação do Supremo).</summary>
public sealed record TransferenciaUsuarioEntrada(
    int UsuarioId,
    int NovoAdminId,
    int ExecutorId,
    string Motivo);

/// <summary>Entrada para exclusão suave de usuário.</summary>
public sealed record ExclusaoUsuarioEntrada(
    int UsuarioId,
    int ExecutorId,
    string Motivo);
