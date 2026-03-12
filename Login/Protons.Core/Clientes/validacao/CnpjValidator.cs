namespace Protons.Core.Clientes.Validation;

public static class CnpjValidator
{
    private const int TotalDigitos = 14;
    private const int IndiceDigitoVerificador1 = 12;
    private const int IndiceDigitoVerificador2 = 13;
    private const int ModuloDivisor = 11;
    private const int LimiteRestoZero = 2;

    public static bool EhValido(string? cnpj)
    {
        if (string.IsNullOrWhiteSpace(cnpj))
            return false;

        var digits = new string(cnpj.Where(char.IsDigit).ToArray());
        if (digits.Length != TotalDigitos)
            return false;

        if (digits.Distinct().Count() == 1)
            return false;

        var numbers = digits.Select(c => c - '0').ToArray();

        var multipliers1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
        var multipliers2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

        var sum1 = 0;
        for (var i = 0; i < multipliers1.Length; i++)
        {
            sum1 += numbers[i] * multipliers1[i];
        }

        var remainder1 = sum1 % ModuloDivisor;
        var digit1 = remainder1 < LimiteRestoZero ? 0 : ModuloDivisor - remainder1;
        if (numbers[IndiceDigitoVerificador1] != digit1)
            return false;

        var sum2 = 0;
        for (var i = 0; i < multipliers2.Length; i++)
        {
            sum2 += numbers[i] * multipliers2[i];
        }

        var remainder2 = sum2 % ModuloDivisor;
        var digit2 = remainder2 < LimiteRestoZero ? 0 : ModuloDivisor - remainder2;
        return numbers[IndiceDigitoVerificador2] == digit2;
    }
}
