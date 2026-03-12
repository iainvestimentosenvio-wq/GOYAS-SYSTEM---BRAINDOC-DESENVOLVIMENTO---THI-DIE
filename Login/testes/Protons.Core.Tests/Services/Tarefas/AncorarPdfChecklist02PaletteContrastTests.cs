using FluentAssertions;
using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tests.Services.Tarefas;

public sealed class AncorarPdfChecklist02PaletteContrastTests
{
    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Palette")]
    [Trait("ChecklistGate", "C2_F6")]
    [Trait("Category", "C2_F6")]
    public void C2_F6_PaletaDeveTer10CoresFixasSemDuplicidade()
    {
        AncorarPdfPalettePolicy.CoresFixas.Should().HaveCount(10);
        AncorarPdfPalettePolicy.CoresFixas.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(10);
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_Palette")]
    [Trait("ChecklistGate", "C2_F6")]
    [Trait("Category", "C2_F6")]
    public void C2_F6_CoresDaPaletaDevemAtenderContrasteMinimoNoOverlay()
    {
        foreach (var cor in AncorarPdfPalettePolicy.CoresFixas)
        {
            AncorarPdfPalettePolicy.TemContrasteMinimoSobreBranco(cor)
                .Should().BeTrue($"A cor {cor} precisa manter contraste minimo em fundo branco.");
        }
    }
}
