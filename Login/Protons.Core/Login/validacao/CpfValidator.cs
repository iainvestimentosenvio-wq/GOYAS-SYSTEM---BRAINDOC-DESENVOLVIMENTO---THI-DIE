namespace Protons.Core.Login.Validation;

public static class CpfValidator
{
    private const int TotalDigitos = 11;
    private const int IndiceDigitoVerificador1 = 9;
    private const int IndiceDigitoVerificador2 = 10;

    public static bool EhValido(string? cpf)
    {
        if (string.IsNullOrWhiteSpace(cpf))
            return false;

        var digits = new string(cpf.Where(char.IsDigit).ToArray());
        if (digits.Length != TotalDigitos)
            return false;

        if (digits.Distinct().Count() == 1)
            return false;

        var numbers = digits.Select(c => c - '0').ToArray();

        var sum1 = 0;
        for (var i = 0; i < IndiceDigitoVerificador1; i++)
        {
            sum1 += numbers[i] * (10 - i);
        }

        var mod1 = (sum1 * 10) % TotalDigitos;
        if (mod1 == 10) mod1 = 0;
        if (numbers[IndiceDigitoVerificador1] != mod1)
            return false;

        var sum2 = 0;
        for (var i = 0; i < IndiceDigitoVerificador2; i++)
        {
            sum2 += numbers[i] * (TotalDigitos - i);
        }

        var mod2 = (sum2 * 10) % TotalDigitos;
        if (mod2 == 10) mod2 = 0;
        return numbers[IndiceDigitoVerificador2] == mod2;
    }
}
