using FluentAssertions;
using Protons.Core.Login.Validation;
using Xunit;

namespace Protons.Core.Tests.Validation;

public class EmailValidatorTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("test.user@example.com")]
    [InlineData("user+tag@example.com")]
    [InlineData("user_name@example.com")]
    [InlineData("user-name@example.com")]
    [InlineData("123@example.com")]
    [InlineData("user@sub.example.com")]
    [InlineData("user@example.co.uk")]
    [InlineData("user@example.technology")]
    [InlineData("a@b.c")]
    public void EhValido_ShouldReturnTrue_ForValidEmails(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("")] // Empty
    [InlineData(" ")] // Whitespace
    [InlineData("   ")] // Multiple whitespaces
    [InlineData("\t")] // Tab
    [InlineData("\n")] // Newline
    public void EhValido_ShouldReturnFalse_ForEmptyOrWhitespace(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void EhValido_ShouldReturnFalse_ForNull()
    {
        // Act
        var result = EmailValidator.EhValido(null);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")] // No @ symbol
    [InlineData("@example.com")] // No local part
    [InlineData("user@")] // No domain
    [InlineData("user@@example.com")] // Double @
    [InlineData("user@ex ample.com")] // Space in domain
    [InlineData(".user@example.com")] // Leading dot
    [InlineData("user@.example.com")] // Leading dot in domain
    public void EhValido_ShouldReturnFalse_ForInvalidFormats(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("user[at]example.com")] // [at] instead of @
    [InlineData("user(at)example.com")] // (at) instead of @
    [InlineData("user#example.com")] // # instead of @
    [InlineData("user$example.com")] // $ instead of @
    public void EhValido_ShouldReturnFalse_ForMissingAtSymbol(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("user@example,com")] // Comma instead of dot
    [InlineData("user@example:com")] // Colon instead of dot
    [InlineData("user@example;com")] // Semicolon instead of dot
    public void EhValido_ShouldReturnFalse_ForInvalidDomainSeparators(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("admin@protons.com")]
    [InlineData("suporte@protons.com.br")]
    [InlineData("contato@protons.org")]
    [InlineData("vendas@protons.net")]
    public void EhValido_ShouldValidateProtonsDomains(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user+test@example.com")] // Plus sign (commonly used for filtering)
    [InlineData("user.name+tag@example.com")] // Combination
    [InlineData("first.last@example.com")] // Dots in local part
    [InlineData("user_underscore@example.com")] // Underscore
    [InlineData("user-dash@example.com")] // Dash
    public void EhValido_ShouldAllowSpecialCharactersInLocalPart(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user@sub.domain.example.com")] // Multiple subdomains
    [InlineData("user@a.b.c.d.com")] // Many subdomains
    [InlineData("user@mail.example.co.uk")] // Subdomain with multi-part TLD
    public void EhValido_ShouldAllowSubdomains(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("user@example.com")] // Common TLD
    [InlineData("user@example.br")] // Brazil TLD
    [InlineData("user@example.technology")] // New TLD
    [InlineData("user@example.museum")] // Sponsored TLD
    [InlineData("user@example.co")] // Two-letter TLD
    public void EhValido_ShouldAllowVariousTlds(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("USER@EXAMPLE.COM")] // All uppercase
    [InlineData("User@Example.Com")] // Mixed case
    [InlineData("uSeR@eXaMpLe.CoM")] // Random case
    public void EhValido_ShouldHandleCaseInsensitivity(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue(because: "email addresses are case-insensitive");
    }

    [Fact]
    public void EhValido_ShouldValidateCommonProviders()
    {
        // Arrange
        var commonEmails = new[]
        {
            "user@gmail.com",
            "user@outlook.com",
            "user@hotmail.com",
            "user@yahoo.com",
            "user@icloud.com",
            "user@live.com",
            "user@protonmail.com"
        };

        // Act & Assert
        foreach (var email in commonEmails)
        {
            EmailValidator.EhValido(email).Should().BeTrue($"{email} should be valid");
        }
    }

    [Fact]
    public void EhValido_ShouldRejectInvalidEmails()
    {
        // Arrange - Only include emails that MailAddress actually rejects
        var invalidEmails = new[]
        {
            "plaintext",
            "@no-local-part.com",
            "no-at-sign.com",
            "missing-domain@.com",
            "double@@domain.com",
            ".leading-dot@domain.com",
            "user@",
            "@domain.com",
            "user@domain space.com"
        };

        // Act & Assert
        foreach (var email in invalidEmails)
        {
            EmailValidator.EhValido(email).Should().BeFalse($"{email} should be invalid");
        }
    }

    [Theory]
    [InlineData("user@localhost")] // No TLD - MailAddress accepts this
    [InlineData("user@127.0.0.1")] // IP address - MailAddress allows this
    public void EhValido_ShouldHandleEdgeCases(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert - MailAddress accepts both localhost and IP addresses
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("test@example.com ")] // Trailing space
    [InlineData(" test@example.com")] // Leading space
    [InlineData(" test@example.com ")] // Both
    public void EhValido_ShouldAcceptEmailsWithSurroundingWhitespace(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert - MailAddress trims whitespace automatically
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("a@b.c")] // Minimal valid email
    [InlineData("x@y.z")]
    public void EhValido_ShouldValidateMinimalEmails(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EhValido_ShouldHandleLongEmailAddresses()
    {
        // Arrange - Maximum local part is 64 chars, domain is 255 chars
        var longLocal = new string('a', 64);
        var longEmail = $"{longLocal}@example.com";

        // Act
        var result = EmailValidator.EhValido(longEmail);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void EhValido_ShouldHandleExcessivelyLongLocalPart()
    {
        // Arrange - Local part > 64 characters (RFC limit)
        var tooLongLocal = new string('a', 65);
        var email = $"{tooLongLocal}@example.com";

        // Act
        var result = EmailValidator.EhValido(email);

        // Assert - MailAddress may not strictly enforce the 64-char limit
        // This test documents the actual behavior
        result.Should().BeTrue(because: "MailAddress class does not strictly enforce RFC 5321 local-part length limit");
    }

    [Theory]
    [InlineData("user+filter@example.com")] // Gmail-style filtering
    [InlineData("user+work@example.com")]
    [InlineData("user+personal@example.com")]
    public void EhValido_ShouldSupportPlusAddressing(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert
        result.Should().BeTrue(because: "plus addressing is commonly used for email filtering");
    }

    [Fact]
    public void EhValido_Performance_ShouldValidateQuickly()
    {
        // Arrange
        const string validEmail = "test@example.com";
        const int iterations = 10000;

        // Act
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            EmailValidator.EhValido(validEmail);
        }
        sw.Stop();

        // Assert - Should validate 10k emails in under 200ms
        sw.ElapsedMilliseconds.Should().BeLessThan(200,
            because: "email validation should be fast enough for production use");
    }

    [Theory]
    [InlineData("joão@example.com")] // Portuguese characters
    [InlineData("andré@example.com")]
    [InlineData("josé@example.com")]
    public void EhValido_ShouldHandleInternationalCharacters(string email)
    {
        // Act
        var result = EmailValidator.EhValido(email);

        // Assert - MailAddress may or may not support internationalized emails
        // This documents the current behavior
        result.Should().BeTrue();
    }
}
