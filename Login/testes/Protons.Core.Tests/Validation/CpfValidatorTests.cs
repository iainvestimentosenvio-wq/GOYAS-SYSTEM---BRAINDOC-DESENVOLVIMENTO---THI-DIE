using FluentAssertions;
using Protons.Core.Login.Validation;
using Xunit;

namespace Protons.Core.Tests.Validation;

public class CpfValidatorTests
{
    [Theory]
    [InlineData("123.456.789-09")] // Valid CPF
    [InlineData("529.982.247-25")] // Valid CPF
    [InlineData("111.444.777-35")] // Valid CPF
    [InlineData("12345678909")] // Valid CPF without formatting
    [InlineData("52998224725")] // Valid CPF without formatting
    [InlineData("000.000.001-91")] // Valid edge case CPF
    public void EhValido_ShouldReturnTrue_ForValidCpfs(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("111.111.111-11")] // All same digits
    [InlineData("222.222.222-22")] // All same digits
    [InlineData("333.333.333-33")] // All same digits
    [InlineData("444.444.444-44")] // All same digits
    [InlineData("555.555.555-55")] // All same digits
    [InlineData("666.666.666-66")] // All same digits
    [InlineData("777.777.777-77")] // All same digits
    [InlineData("888.888.888-88")] // All same digits
    [InlineData("999.999.999-99")] // All same digits
    [InlineData("000.000.000-00")] // All same digits
    public void EhValido_ShouldReturnFalse_ForSameDigitCpfs(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("123.456.789-10")] // Wrong check digit
    [InlineData("123.456.789-00")] // Wrong check digit
    [InlineData("529.982.247-26")] // Wrong check digit (last digit +1)
    [InlineData("529.982.247-24")] // Wrong check digit (last digit -1)
    [InlineData("111.444.777-36")] // Wrong check digit
    public void EhValido_ShouldReturnFalse_ForInvalidCheckDigits(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("")] // Empty string
    [InlineData(" ")] // Whitespace
    [InlineData("   ")] // Multiple whitespaces
    [InlineData("\t")] // Tab
    [InlineData("\n")] // Newline
    public void EhValido_ShouldReturnFalse_ForEmptyOrWhitespace(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EhValido_ShouldReturnFalse_ForNull()
    {
        // Act
        var result = CpfValidator.EhValido(null);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("123.456.789")] // Missing check digits
    [InlineData("123.456")] // Too short
    [InlineData("12345678")] // Too short
    [InlineData("1234567890")] // Only 10 digits
    [InlineData("123456789012")] // 12 digits (too long)
    [InlineData("123.456.789-091")] // 12 digits with formatting
    public void EhValido_ShouldReturnFalse_ForInvalidLength(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("123.456.789-09")] // Standard format with dots and dash
    [InlineData("12345678909")] // Only numbers
    [InlineData("123 456 789 09")] // Spaces instead of dots
    [InlineData("123-456-789-09")] // Dashes everywhere
    [InlineData("123/456/789/09")] // Slashes
    [InlineData("(123)(456)(789)(09)")] // Parentheses
    public void EhValido_ShouldHandleVariousFormats(string cpf)
    {
        // Arrange
        var hasValidCheckDigits = cpf.Replace(".", "")
            .Replace("-", "")
            .Replace(" ", "")
            .Replace("/", "")
            .Replace("(", "")
            .Replace(")", "") == "12345678909";

        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        if (hasValidCheckDigits)
        {
            result.Should().BeTrue(because: "validator should extract digits and validate");
        }
    }

    [Theory]
    [InlineData("abc.def.ghi-jk")] // Letters
    [InlineData("123.abc.789-09")] // Mixed letters and numbers
    [InlineData("###.###.###-##")] // Special characters only
    [InlineData("@@@.@@@.@@@-@@")] // Special characters only
    public void EhValido_ShouldReturnFalse_ForNonNumericInput(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("12345678909", "123.456.789-09")] // Same CPF, different formats
    [InlineData("52998224725", "529.982.247-25")] // Same CPF, different formats
    public void EhValido_ShouldTreatFormattedAndUnformattedEqually(string unformatted, string formatted)
    {
        // Act
        var resultUnformatted = CpfValidator.EhValido(unformatted);
        var resultFormatted = CpfValidator.EhValido(formatted);

        // Assert
        resultUnformatted.Should().Be(resultFormatted);
        resultUnformatted.Should().BeTrue();
    }

    [Fact]
    public void EhValido_ShouldValidateRealWorldCpfs()
    {
        // Arrange - These are generated valid CPFs (not real people)
        var validCpfs = new[]
        {
            "123.456.789-09",
            "529.982.247-25",
            "111.444.777-35",
            "000.000.001-91"
        };

        // Act & Assert
        foreach (var cpf in validCpfs)
        {
            CpfValidator.EhValido(cpf).Should().BeTrue($"CPF {cpf} should be valid");
        }
    }

    [Fact]
    public void EhValido_ShouldRejectInvalidRealWorldCpfs()
    {
        // Arrange - Invalid CPFs (wrong check digits or format issues)
        var invalidCpfs = new[]
        {
            "123.456.789-00", // Wrong check digit
            "111.111.111-11", // All same digits
            "000.000.000-00", // All zeros
            "12345678900",    // Wrong check digit
            "999.999.999-99", // All same digits
            "529.982.247-26"  // Wrong check digit (last digit +1)
        };

        // Act & Assert
        foreach (var cpf in invalidCpfs)
        {
            CpfValidator.EhValido(cpf).Should().BeFalse($"CPF {cpf} should be invalid");
        }
    }

    [Theory]
    [InlineData("123.456.789-09 ")] // Trailing space
    [InlineData(" 123.456.789-09")] // Leading space
    [InlineData(" 123.456.789-09 ")] // Both
    [InlineData("123.456.789-09\n")] // Trailing newline
    [InlineData("\t123.456.789-09")] // Leading tab
    public void EhValido_ShouldHandleLeadingAndTrailingWhitespace(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert - Should still validate correctly after trimming
        result.Should().BeTrue();
    }

    [Fact]
    public void EhValido_ShouldValidateEdgeCases()
    {
        // Arrange & Act & Assert
        CpfValidator.EhValido("000.000.001-91").Should().BeTrue(because: "very low CPF numbers should work");
        CpfValidator.EhValido("999.999.999-91").Should().BeFalse(because: "all 9s is invalid");
        CpfValidator.EhValido("000.000.000-00").Should().BeFalse(because: "all 0s is invalid");
    }

    [Theory]
    [InlineData("12345678909")] // 11 digits
    [InlineData("123.456.789-09")] // Formatted
    public void EhValido_ShouldExtractOnlyDigits(string cpf)
    {
        // Act
        var result = CpfValidator.EhValido(cpf);

        // Assert
        result.Should().BeTrue(because: "validator should extract only digits for validation");
    }

    [Fact]
    public void EhValido_Performance_ShouldValidateQuickly()
    {
        // Arrange
        const string validCpf = "123.456.789-09";
        const int iterations = 10000;

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            CpfValidator.EhValido(validCpf);
        }
        sw.Stop();

        // Assert - Should validate 10k CPFs quickly even on slower environments
        sw.ElapsedMilliseconds.Should().BeLessThan(1000,
            because: "CPF validation should be fast enough for production use");
    }
}
