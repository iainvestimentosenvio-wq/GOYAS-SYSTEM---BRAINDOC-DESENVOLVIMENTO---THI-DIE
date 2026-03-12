using Protons.Core.Login.Models;

namespace Protons.Core.Login.Services;

public interface IUserDirectoryService
{
    IReadOnlyList<UserDirectoryItem> ListarAtivos();
}
