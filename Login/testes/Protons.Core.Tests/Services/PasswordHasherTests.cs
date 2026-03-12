using FluentAssertions;
using Protons.Core.Login.Services;
using Xunit;

namespace Protons.Core.Tests.Services;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher;

    public PasswordHasherTests()
    {
        _hasher = new PasswordHasher();
    }

    [Fact]
    public void HashPassword_ShouldGenerateUniqueHashForSamePassword()
    {
        // Arrange
        const string password = "SecurePassword123!";

        // Act
        var result1 = _hasher.HashPassword(password);
        var result2 = _hasher.HashPassword(password);

        // Assert - Hashes should be different due to unique salts
        result1.Hash.Should().NotBe(result2.Hash);
        result1.Salt.Should().NotBe(result2.Salt);
        result1.Iterations.Should().Be(result2.Iterations);
    }

    [Fact]
    public void HashPassword_ShouldGenerateValidBase64EncodedOutput()
    {
        // Arrange
        const string password = "TestPassword456";

        // Act
        var result = _hasher.HashPassword(password);

        // Assert - Both hash and salt should be valid Base64 strings
        Action decodeHash = () => Convert.FromBase64String(result.Hash);
        Action decodeSalt = () => Convert.FromBase64String(result.Salt);

        decodeHash.Should().NotThrow();
        decodeSalt.Should().NotThrow();
    }

    [Fact]
    public void HashPassword_ShouldUseDefaultIterations_WhenNotSpecified()
    {
        // Arrange
        const string password = "Password";
        const int expectedIterations = 100_000;

        // Act
        var result = _hasher.HashPassword(password);

        // Assert
        result.Iterations.Should().Be(expectedIterations);
    }

    [Fact]
    public void HashPassword_ShouldUseCustomIterations_WhenSpecified()
    {
        // Arrange
        const string password = "Password";
        const int customIterations = 50_000;

        // Act
        var result = _hasher.HashPassword(password, customIterations);

        // Assert
        result.Iterations.Should().Be(customIterations);
    }

    [Fact]
    public void Verify_ShouldReturnTrue_WhenPasswordMatches()
    {
        // Arrange
        const string password = "CorrectPassword123!";
        var (hash, salt, iterations) = _hasher.HashPassword(password);

        // Act
        var isValid = _hasher.Verify(password, hash, salt, iterations);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenPasswordDoesNotMatch()
    {
        // Arrange
        const string correctPassword = "CorrectPassword123!";
        const string wrongPassword = "WrongPassword456!";
        var (hash, salt, iterations) = _hasher.HashPassword(correctPassword);

        // Act
        var isValid = _hasher.Verify(wrongPassword, hash, salt, iterations);

        // Assert
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Short")]
    [InlineData("VeryLongPasswordWithManyCharacters123456789!@#$%^&*()")]
    [InlineData("Senha123!@#")]
    [InlineData("P@ssw0rd_ComplexO_2024")]
    public void HashPassword_ShouldHandleVariousPasswordLengths(string password)
    {
        // Act
        var result = _hasher.HashPassword(password);

        // Assert
        result.Hash.Should().NotBeNullOrWhiteSpace();
        result.Salt.Should().NotBeNullOrWhiteSpace();
        result.Iterations.Should().BeGreaterThan(0);

        // Verify the password can be verified
        var isValid = _hasher.Verify(password, result.Hash, result.Salt, result.Iterations);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldHandleSlightPasswordVariations()
    {
        // Arrange
        const string originalPassword = "Password123";
        var (hash, salt, iterations) = _hasher.HashPassword(originalPassword);

        // Act & Assert - Similar but different passwords should all fail
        _hasher.Verify("password123", hash, salt, iterations).Should().BeFalse(); // case change
        _hasher.Verify("Password124", hash, salt, iterations).Should().BeFalse(); // last char change
        _hasher.Verify("Password123 ", hash, salt, iterations).Should().BeFalse(); // trailing space
        _hasher.Verify(" Password123", hash, salt, iterations).Should().BeFalse(); // leading space
        _hasher.Verify("Password12", hash, salt, iterations).Should().BeFalse(); // shorter
        _hasher.Verify("Password1234", hash, salt, iterations).Should().BeFalse(); // longer
    }

    [Fact]
    public void Verify_ShouldBeConsistent_WhenCalledMultipleTimes()
    {
        // Arrange
        const string password = "ConsistentPassword789!";
        var (hash, salt, iterations) = _hasher.HashPassword(password);

        // Act - Verify multiple times
        var results = Enumerable.Range(0, 100)
            .Select(_ => _hasher.Verify(password, hash, salt, iterations))
            .ToList();

        // Assert - All verifications should return true
        results.Should().AllSatisfy(result => result.Should().BeTrue());
    }

    [Fact]
    public void Verify_ShouldBeTimingAttackResistant()
    {
        // Arrange
        const string password = "SecurePassword!";
        var (hash, salt, iterations) = _hasher.HashPassword(password);
        const int testRuns = 50;

        // Act - Measure time for correct password
        var correctTimes = new List<long>();
        for (int i = 0; i < testRuns; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _hasher.Verify(password, hash, salt, iterations);
            sw.Stop();
            correctTimes.Add(sw.ElapsedTicks);
        }

        // Act - Measure time for wrong password
        var wrongTimes = new List<long>();
        for (int i = 0; i < testRuns; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _hasher.Verify("WrongPassword!", hash, salt, iterations);
            sw.Stop();
            wrongTimes.Add(sw.ElapsedTicks);
        }

        // Assert - Average times should be similar (within 50% variance)
        // This tests that FixedTimeEquals is being used
        var correctAvg = correctTimes.Average();
        var wrongAvg = wrongTimes.Average();
        var percentDiff = Math.Abs(correctAvg - wrongAvg) / Math.Max(correctAvg, wrongAvg) * 100;

        // Note: This is a heuristic test. Timing attacks are complex, but having
        // similar execution times is a good indicator of protection
        percentDiff.Should().BeLessThan(50,
            because: "verification time should not significantly vary based on password correctness");
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(200_000)]
    public void HashPassword_ShouldWorkWithDifferentIterations(int iterations)
    {
        // Arrange
        const string password = "TestPassword";

        // Act
        var result = _hasher.HashPassword(password, iterations);

        // Assert
        result.Iterations.Should().Be(iterations);
        var isValid = _hasher.Verify(password, result.Hash, result.Salt, result.Iterations);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldFail_WhenIterationCountMismatch()
    {
        // Arrange
        const string password = "Password123";
        var (hash, salt, iterations) = _hasher.HashPassword(password, 100_000);

        // Act - Verify with wrong iteration count
        var isValid = _hasher.Verify(password, hash, salt, 50_000);

        // Assert - Should fail because iteration count is part of the verification
        isValid.Should().BeFalse();
    }

    [Fact]
    public void HashPassword_ShouldGenerateExpectedSaltSize()
    {
        // Arrange
        const string password = "Test";

        // Act
        var result = _hasher.HashPassword(password);
        var saltBytes = Convert.FromBase64String(result.Salt);

        // Assert - Salt should be 16 bytes (128 bits) as per implementation
        saltBytes.Length.Should().Be(16);
    }

    [Fact]
    public void HashPassword_ShouldGenerateExpectedHashSize()
    {
        // Arrange
        const string password = "Test";

        // Act
        var result = _hasher.HashPassword(password);
        var hashBytes = Convert.FromBase64String(result.Hash);

        // Assert - Hash should be 32 bytes (256 bits) as per SHA256
        hashBytes.Length.Should().Be(32);
    }

    [Fact]
    public void Verify_ShouldFail_WhenSaltIsCorrupted()
    {
        // Arrange
        const string password = "Password123";
        var (hash, salt, iterations) = _hasher.HashPassword(password);
        var corruptedSalt = Convert.ToBase64String(new byte[16]); // All zeros

        // Act
        var isValid = _hasher.Verify(password, hash, corruptedSalt, iterations);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldFail_WhenHashIsCorrupted()
    {
        // Arrange
        const string password = "Password123";
        var (hash, salt, iterations) = _hasher.HashPassword(password);
        var corruptedHash = Convert.ToBase64String(new byte[32]); // All zeros

        // Act
        var isValid = _hasher.Verify(password, corruptedHash, salt, iterations);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public void HashPassword_ShouldHandleUnicodeCharacters()
    {
        // Arrange - Portuguese and special characters
        const string password = "Sénha_Pôrtüguês_123!@#$%^&*()_+[]{}|;':\"<>,.?/`~";

        // Act
        var result = _hasher.HashPassword(password);

        // Assert
        result.Hash.Should().NotBeNullOrWhiteSpace();
        var isValid = _hasher.Verify(password, result.Hash, result.Salt, result.Iterations);
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HashPassword_ShouldThrow_WhenPasswordIsNullOrWhitespace(string? password)
    {
        var act = () => _hasher.HashPassword(password!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenPasswordIsNull()
    {
        var (hash, salt, iterations) = _hasher.HashPassword("x");
        _hasher.Verify(null!, hash, salt, iterations).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenHashIsNull()
    {
        var (hash, salt, iterations) = _hasher.HashPassword("x");
        _hasher.Verify("x", null!, salt, iterations).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenSaltIsNull()
    {
        var (hash, salt, iterations) = _hasher.HashPassword("x");
        _hasher.Verify("x", hash, null!, iterations).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")] // invalid Base64
    [InlineData("!!!")]
    public void Verify_ShouldReturnFalse_WhenHashOrSaltInvalidBase64(string invalid)
    {
        var (hash, salt, iterations) = _hasher.HashPassword("x");
        _hasher.Verify("x", invalid, salt, iterations).Should().BeFalse();
        _hasher.Verify("x", hash, invalid, iterations).Should().BeFalse();
    }

    [Fact]
    public void Verify_ShouldReturnFalse_WhenIterationsZeroOrNegative()
    {
        var (hash, salt, _) = _hasher.HashPassword("x");
        _hasher.Verify("x", hash, salt, 0).Should().BeFalse();
        _hasher.Verify("x", hash, salt, -1).Should().BeFalse();
    }
}
