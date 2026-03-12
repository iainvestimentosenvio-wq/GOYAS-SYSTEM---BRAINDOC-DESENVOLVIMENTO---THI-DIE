using Protons.Core.Login.Models;
using Protons.Core.Login.Repositories;

namespace Protons.Core.Login.Services;

public sealed class UserDirectoryService : IUserDirectoryService
{
    private readonly IUserRepository _users;

    public UserDirectoryService(IUserRepository users)
    {
        _users = users;
    }

    public IReadOnlyList<UserDirectoryItem> ListarAtivos()
    {
        return _users
            .ListarAtivos()
            .Select(u => new UserDirectoryItem
            {
                Id = u.Id,
                Nome = u.Nome,
                Email = u.Email,
                Role = u.Role
            })
            .ToList();
    }
}
