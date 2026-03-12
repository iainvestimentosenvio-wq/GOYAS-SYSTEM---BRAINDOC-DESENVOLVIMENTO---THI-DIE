using FluentAssertions;
using Protons.Core.Login.Validation;
using Xunit;

namespace Protons.Core.Tests.Validation;

public sealed class PasswordPolicyTests
{
    [Fact]
    public void EhValida_DeveRejeitarSenhaNulaOuVazia()
    {
        PasswordPolicy.EhValida(null).Should().BeFalse();
        PasswordPolicy.EhValida(string.Empty).Should().BeFalse();
        PasswordPolicy.EhValida("   ").Should().BeFalse();
    }

    [Fact]
    public void EhValida_DeveRejeitarSenhaComMenosDeDozeCaracteres()
    {
        PasswordPolicy.EhValida("Abc@12345").Should().BeFalse();
    }

    [Theory]
    [InlineData("somenteletrasfortes")] // apenas minusculas
    [InlineData("SOMENTELETRASFORTES")] // apenas maiusculas
    [InlineData("12345678901234")] // apenas digitos
    [InlineData("!!!!!!!!!!!!")] // apenas especiais
    [InlineData("Senhafortesemnumero")] // maiuscula + minuscula
    [InlineData("SENHA1234567890")] // maiuscula + digito
    [InlineData("senha1234567890")] // minuscula + digito
    public void EhValida_DeveRejeitarSemComplexidadeMinima(string senha)
    {
        PasswordPolicy.EhValida(senha).Should().BeFalse();
    }

    [Theory]
    [InlineData("SenhaForte123")] // maiuscula + minuscula + digito
    [InlineData("Senhaforte@#12")] // maiuscula + minuscula + especial + digito
    [InlineData("SENHAforte!!!")] // maiuscula + minuscula + especial
    public void EhValida_DeveAceitarQuandoAtingeComplexidadeMinima(string senha)
    {
        PasswordPolicy.EhValida(senha).Should().BeTrue();
    }

    [Fact]
    public void EhValidaParaLogin_DeveAceitarSenhaLegadaNaoVazia()
    {
        PasswordPolicy.EhValidaParaLogin("12345678").Should().BeTrue();
    }

    [Fact]
    public void EhValidaParaLogin_DeveRejeitarNulaOuVazia()
    {
        PasswordPolicy.EhValidaParaLogin(null).Should().BeFalse();
        PasswordPolicy.EhValidaParaLogin(string.Empty).Should().BeFalse();
        PasswordPolicy.EhValidaParaLogin(" ").Should().BeFalse();
    }

    [Fact]
    public void EhValidaParaLogin_DeveRejeitarAcimaDoMaximo()
    {
        var senhaMuitoLonga = new string('A', PasswordPolicy.MaxLength + 1);
        PasswordPolicy.EhValidaParaLogin(senhaMuitoLonga).Should().BeFalse();
    }

    [Fact]
    public void ObterRequisitos_DeveConterTextoEsperado()
    {
        var requisitos = PasswordPolicy.ObterRequisitos();

        requisitos.Should().Contain(PasswordPolicy.MinLength.ToString());
        requisitos.Should().Contain(PasswordPolicy.MaxLength.ToString());
        requisitos.Should().Contain("maiúscula");
        requisitos.Should().Contain("minúscula");
        requisitos.Should().Contain("número");
        requisitos.Should().Contain("especial");
    }
}
