using FluentAssertions;
using Protons.UI.Painel.ViewModels;
using Protons.UI.Painel.Views.PainelPrincipal.Controles;

namespace Protons.Infrastructure.Tests.Integration;

public sealed class EsteiraTarefasControlLayoutTests
{
    [Fact]
    public void ColisaoMesmoMinuto_DeveManterMaisAntigaNaDireita()
    {
        var minutoBase = new DateTime(2026, 3, 4, 15, 30, 0, DateTimeKind.Utc);
        var entradas = new List<EsteiraTarefasControl.ColisaoEntrada>
        {
            new(IndiceOriginal: 0, VencimentoUtc: minutoBase, CriadoEmUtcOrdenacao: minutoBase.AddMinutes(-30), TarefaId: 100), // mais antiga
            new(IndiceOriginal: 1, VencimentoUtc: minutoBase.AddSeconds(8), CriadoEmUtcOrdenacao: minutoBase.AddMinutes(-10), TarefaId: 101),
            new(IndiceOriginal: 2, VencimentoUtc: minutoBase.AddSeconds(21), CriadoEmUtcOrdenacao: minutoBase.AddMinutes(-1), TarefaId: 102)  // mais nova
        };

        var offsets = EsteiraTarefasControl.CalcularOffsetsColisaoPorMinuto(
            entradas,
            larguraCard: 212,
            espacamento: 14);

        offsets[0].Should().Be(0); // mais antiga fica mais à direita
        offsets[1].Should().BeLessThan(offsets[0]);
        offsets[2].Should().BeLessThan(offsets[1]);
    }

    [Fact]
    public void ZoomTemporal_NaoDeveAlterarDimensaoDoCard()
    {
        var tamanhoZoomBaixo = EsteiraTarefasControl.ObterTamanhoCardNoZoom(0.15);
        var tamanhoZoomAlto = EsteiraTarefasControl.ObterTamanhoCardNoZoom(0.95);

        tamanhoZoomBaixo.Width.Should().Be(tamanhoZoomAlto.Width);
        tamanhoZoomBaixo.Height.Should().Be(tamanhoZoomAlto.Height);
        tamanhoZoomBaixo.Width.Should().Be(212);
        tamanhoZoomBaixo.Height.Should().Be(46);
    }

    [Fact]
    public void TarefaReguaItem_ConstrutorAntigo_DeveContinuarCompativel()
    {
        var item = new TarefaReguaItem(
            TarefaId: 33,
            Titulo: "Compatibilidade",
            VencimentoUtc: new DateTime(2026, 3, 4, 18, 0, 0, DateTimeKind.Utc),
            HorarioTexto: "18:00",
            StatusTexto: "AGENDADA",
            CorStatus: "#22C55E",
            EhAtrasada: false,
            EhRecorrente: false,
            EsteiraId: 2,
            Responsavel: "Thiago Lima");

        item.TarefaId.Should().Be(33);
        item.AvatarIniciais.Should().Be("TL");
        item.AgendadoPorNome.Should().NotBeNullOrWhiteSpace();
    }
}
