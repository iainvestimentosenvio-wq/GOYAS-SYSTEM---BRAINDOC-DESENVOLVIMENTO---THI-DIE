using FluentAssertions;
using Protons.Core.Clientes.Models;
using Protons.Core.Clientes.Validation;

namespace Protons.Core.Tests.Validation;

public sealed class DocumentoClienteValidatorTests
{
    [Fact]
    public void Analisar_DeveDetectarCpfValido()
    {
        var analise = DocumentoClienteValidator.Analisar("123.456.789-09");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Valido);
        analise.TipoDocumento.Should().Be(TipoDocumentoCliente.CPF);
        analise.DocumentoSomenteDigitos.Should().Be("12345678909");
        analise.CaracteresPermitidos.Should().BeTrue();
    }

    [Fact]
    public void Analisar_DeveDetectarCnpjValido()
    {
        var analise = DocumentoClienteValidator.Analisar("04.252.011/0001-10");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Valido);
        analise.TipoDocumento.Should().Be(TipoDocumentoCliente.CNPJ);
        analise.DocumentoSomenteDigitos.Should().Be("04252011000110");
        analise.CaracteresPermitidos.Should().BeTrue();
    }

    [Fact]
    public void Analisar_DeveRetornarInvalido_QuandoCaracteresNaoPermitidos()
    {
        var analise = DocumentoClienteValidator.Analisar("04.252.011/0001-10ABC");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Invalido);
        analise.CaracteresPermitidos.Should().BeFalse();
    }

    [Fact]
    public void Analisar_DeveRetornarIncompleto_QuandoDocumentoAindaNaoTemTamanhoMinimo()
    {
        var analise = DocumentoClienteValidator.Analisar("123.456");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Incompleto);
        analise.TipoDocumento.Should().BeNull();
    }

    [Fact]
    public void Analisar_DeveRetornarIncompleto_QuandoDocumentoTemDozeDigitos()
    {
        var analise = DocumentoClienteValidator.Analisar("123456789012");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Incompleto);
        analise.TipoDocumento.Should().BeNull();
    }

    [Fact]
    public void Analisar_DeveRetornarInvalido_QuandoDocumentoExcedeQuatorzeDigitos()
    {
        var analise = DocumentoClienteValidator.Analisar("123456789012345");

        analise.Status.Should().Be(StatusAnaliseDocumentoCliente.Invalido);
        analise.TipoDocumento.Should().BeNull();
    }

    [Fact]
    public void AplicarMascaraParcial_DeveAplicarMascaraCpfDuranteDigitacao()
    {
        DocumentoClienteValidator.AplicarMascaraParcial("1234567890")
            .Should().Be("123.456.789-0");
    }

    [Fact]
    public void AplicarMascaraParcial_DeveAplicarMascaraCnpjDuranteDigitacao()
    {
        DocumentoClienteValidator.AplicarMascaraParcial("04252011000110")
            .Should().Be("04.252.011/0001-10");
    }

    [Fact]
    public void FormatarDocumentoCompleto_DeveFormatarCpfECnpj()
    {
        DocumentoClienteValidator.FormatarDocumentoCompleto(TipoDocumentoCliente.CPF, "12345678909")
            .Should().Be("123.456.789-09");
        DocumentoClienteValidator.FormatarDocumentoCompleto(TipoDocumentoCliente.CNPJ, "04252011000110")
            .Should().Be("04.252.011/0001-10");
    }

    [Fact]
    public void DetectarTipoPorDocumento_DeveRetornarTipoCorretoPorQuantidadeDeDigitos()
    {
        DocumentoClienteValidator.DetectarTipoPorDocumento("12345678909").Should().Be(TipoDocumentoCliente.CPF);
        DocumentoClienteValidator.DetectarTipoPorDocumento("04252011000110").Should().Be(TipoDocumentoCliente.CNPJ);
        DocumentoClienteValidator.DetectarTipoPorDocumento("12345").Should().BeNull();
    }
}
