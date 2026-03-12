namespace Protons.Core.Login.Services;

public interface IPasswordHasher
{
    (string Hash, string Salt, int Iterations) HashPassword(string password, int? iterations = null);
    bool Verify(string password, string hash, string salt, int iterations);
}
