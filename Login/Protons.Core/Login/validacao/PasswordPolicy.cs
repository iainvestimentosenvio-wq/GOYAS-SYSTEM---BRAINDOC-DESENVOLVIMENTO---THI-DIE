namespace Protons.Core.Login.Validation;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 256;

    public static bool EhValida(string? senha)
    {
        if (!EhValidaParaLogin(senha))
            return false;

        if (senha!.Length < MinLength)
            return false;

        var grupos = 0;
        if (senha.Any(char.IsUpper))
            grupos++;
        if (senha.Any(char.IsLower))
            grupos++;
        if (senha.Any(char.IsDigit))
            grupos++;
        if (senha.Any(EhCaractereEspecial))
            grupos++;

        return grupos >= 3;
    }

    public static bool EhValidaParaLogin(string? senha)
    {
        return !string.IsNullOrWhiteSpace(senha) &&
               senha.Length <= MaxLength;
    }

    public static string ObterRequisitos()
    {
        return $"A senha deve ter entre {MinLength} e {MaxLength} caracteres e incluir pelo menos 3 destes 4 grupos: letra maiúscula, letra minúscula, número e caractere especial.";
    }

    private static bool EhCaractereEspecial(char ch)
    {
        return !char.IsLetterOrDigit(ch) && !char.IsWhiteSpace(ch);
    }
}
