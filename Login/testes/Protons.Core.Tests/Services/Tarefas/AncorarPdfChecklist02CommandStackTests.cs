using FluentAssertions;
using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tests.Services.Tarefas;

public sealed class AncorarPdfChecklist02CommandStackTests
{
    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_CommandStack")]
    [Trait("ChecklistGate", "C2_F8")]
    [Trait("Category", "C2_F8")]
    public void C2_F8_CommandStack_DeveAplicarUndoERedoDeterministico()
    {
        var stack = new AncorarPdfCommandStackState(maxHistorico: 20);

        stack.DefinirEstadoInicial([]);
        stack.PodeUndo.Should().BeFalse();
        stack.PodeRedo.Should().BeFalse();

        var estado1 = new[] { CriarAncora("#4A90D9", ordem: 0) };
        var estado2 = new[] { CriarAncora("#4A90D9", ordem: 0), CriarAncora("#F5D547", ordem: 1) };

        stack.AplicarNovoEstado(estado1);
        stack.AplicarNovoEstado(estado2);

        stack.EstadoAtual.Should().HaveCount(2);
        stack.PodeUndo.Should().BeTrue();
        stack.PodeRedo.Should().BeFalse();

        stack.TentarUndo(out var aposUndo).Should().BeTrue();
        aposUndo.Should().HaveCount(1);
        aposUndo[0].CorHex.Should().Be("#4A90D9");

        stack.TentarRedo(out var aposRedo).Should().BeTrue();
        aposRedo.Should().HaveCount(2);
        aposRedo[1].CorHex.Should().Be("#F5D547");
    }

    [Fact]
    [Trait("ChecklistGate", "C2_G2_Core_CommandStack")]
    [Trait("ChecklistGate", "C2_F8")]
    [Trait("Category", "C2_F8")]
    public void C2_F8_CommandStack_DeveRespeitarLimiteDeHistoricoSemCustosDeTrimPesado()
    {
        var stack = new AncorarPdfCommandStackState(maxHistorico: 10);
        stack.DefinirEstadoInicial([]);

        for (var i = 1; i <= 14; i++)
        {
            stack.AplicarNovoEstado([CriarAncora("#4A90D9", ordem: i)]);
        }

        // Com historico maximo 10, somente os 10 estados mais recentes devem permanecer para undo.
        stack.TentarUndo(out var undo1).Should().BeTrue();
        undo1[0].Ordem.Should().Be(13);

        stack.TentarUndo(out var undo2).Should().BeTrue();
        undo2[0].Ordem.Should().Be(12);

        stack.TentarUndo(out var undo3).Should().BeTrue();
        undo3[0].Ordem.Should().Be(11);

        // Consumir os 7 undos restantes (total de 10).
        for (var i = 0; i < 7; i++)
            stack.TentarUndo(out _).Should().BeTrue();

        stack.TentarUndo(out _).Should().BeFalse();
    }

    private static AncorarPdfTemplateAncora CriarAncora(string corHex, int ordem)
    {
        return new AncorarPdfTemplateAncora
        {
            Ordem = ordem,
            CorHex = corHex,
            Pagina = 1,
            XRel = 0.1 + ordem * 0.1,
            YRel = 0.1,
            LarguraRel = 0.2,
            AlturaRel = 0.08,
            Metadado = new AncorarPdfTemplateMetadado
            {
                NomeExibido = $"Campo {ordem}",
                ChaveTecnica = $"campo_{ordem}",
                TipoEsperado = "texto"
            }
        };
    }
}
