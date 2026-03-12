using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.Dominio;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// C9 — Gate G2 (blocking): Contratos adversariais de domínio.
/// Verifica invariantes não cobertos por C1–C8: edge cases de SelecaoNomeAproximadoPorCiclo,
/// modelos de motor PDF, format dos ErroCodigos e backoff monotônico.
/// Sem banco de dados, sem I/O.
/// </summary>
[Trait("Checklist", "C9")]
[Trait("Category", "C9_G2_Contratos")]
public sealed class AncorarPdfChecklist09ContractTests
{
    // --- SelecaoNomeAproximadoPorCiclo — edge cases adversariais ---

    [Fact]
    public void T01_SelecionarMelhor_lista_vazia_retorna_null()
    {
        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(
            Array.Empty<CandidatoArquivoNomeAproximado>(), limiarSimilaridade: 0.75);

        melhor.Should().BeNull("lista vazia não pode retornar candidato");
    }

    [Fact]
    public void T02_SelecionarMelhor_todos_abaixo_limiar_retorna_null()
    {
        var candidatos = new[]
        {
            new CandidatoArquivoNomeAproximado("nota", "/tmp/a.pdf", 0.30, DateTime.UtcNow),
            new CandidatoArquivoNomeAproximado("nota", "/tmp/b.pdf", 0.50, DateTime.UtcNow),
            new CandidatoArquivoNomeAproximado("nota", "/tmp/c.pdf", 0.74, DateTime.UtcNow)
        };

        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(candidatos, 0.75);

        melhor.Should().BeNull("nenhum candidato atinge o limiar");
    }

    [Fact]
    public void T03_SelecionarMelhor_candidato_exatamente_no_limiar_eh_incluido()
    {
        // Borderline: sim == limiar deve ser incluído (condição >=, não >).
        var candidatos = new[]
        {
            new CandidatoArquivoNomeAproximado("relatorio", "/tmp/a.pdf", 0.75,
                new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc))
        };

        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(candidatos, 0.75);

        melhor.Should().NotBeNull("candidato exatamente no limiar deve ser aceito (>=)");
        melhor!.ArquivoPath.Should().Be("/tmp/a.pdf");
    }

    [Fact]
    public void T04_SelecionarMelhor_desempate_mtime_mais_recente_vence()
    {
        // Mesma similaridade: mtime mais recente deve ser selecionado antes do mais antigo.
        // Tiebreak: sim desc → mtime desc → path asc.
        var mais_antigo = new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var mais_recente = new DateTime(2026, 2, 1, 10, 0, 0, DateTimeKind.Utc);

        var candidatos = new[]
        {
            new CandidatoArquivoNomeAproximado("relatorio", "/tmp/z.pdf", 0.90, mais_recente),
            new CandidatoArquivoNomeAproximado("relatorio", "/tmp/a.pdf", 0.90, mais_antigo)
        };

        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(candidatos, 0.80);

        melhor!.ArquivoPath.Should().Be("/tmp/z.pdf",
            "mtime mais recente vence quando a similaridade é igual");
    }

    [Fact]
    public void T05_SelecionarMelhor_sim_maior_vence_independente_de_mtime()
    {
        // Similaridade tem prioridade sobre mtime: candidato com sim alta mas mtime antigo ganha.
        var mtime_antigo = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var mtime_recente = new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc);

        var candidatos = new[]
        {
            new CandidatoArquivoNomeAproximado("relatorio", "/tmp/b.pdf", 0.80, mtime_recente),
            new CandidatoArquivoNomeAproximado("relatorio", "/tmp/a.pdf", 0.99, mtime_antigo)
        };

        var melhor = SelecaoNomeAproximadoPorCiclo.SelecionarMelhor(candidatos, 0.75);

        melhor!.ArquivoPath.Should().Be("/tmp/a.pdf",
            "similaridade alta vence mesmo com mtime mais antigo");
    }

    // --- Modelos PdfMotor (C6) — invariantes não verificados em C6/C7/C8 ---

    [Fact]
    public void T06_PdfPalavra_record_tem_campos_texto_e_bbox_acessiveis()
    {
        var bbox = new BboxRelativo(0.1, 0.2, 0.3, 0.05);
        var palavra = new PdfPalavra("Contrato", bbox);

        palavra.Texto.Should().Be("Contrato");
        palavra.Bbox.X.Should().BeApproximately(0.1, 1e-9);
        palavra.Bbox.Y.Should().BeApproximately(0.2, 1e-9);
        palavra.Bbox.Largura.Should().BeApproximately(0.3, 1e-9);
        palavra.Bbox.Altura.Should().BeApproximately(0.05, 1e-9);
    }

    [Fact]
    public void T07_PdfPaginaTexto_aceita_lista_de_palavras_vazia()
    {
        var pagina = new PdfPaginaTexto(1, 595.0, 842.0, new List<PdfPalavra>());

        pagina.Numero.Should().Be(1);
        pagina.LarguraPt.Should().Be(595.0);
        pagina.AlturaPt.Should().Be(842.0);
        pagina.Palavras.Should().BeEmpty("página sem texto é válida");
    }

    [Fact]
    public void T08_SelecaoArquivoResultado_campos_obrigatorios_nao_nulos()
    {
        var result = new SelecaoArquivoResultado(
            "/tmp/relatorio.pdf",
            "relatorio",
            "abc123def456abc123def456abc123def456abc123def456abc123def456abc1",
            12345L,
            new DateTime(2026, 2, 26, 0, 0, 0, DateTimeKind.Utc));

        result.ArquivoPath.Should().NotBeNullOrEmpty();
        result.NomeEsperadoLogico.Should().NotBeNullOrEmpty();
        result.ArquivoHash.Should().NotBeNullOrEmpty();
        result.TamanhoBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void T09_ValidacaoClienteResultado_invalido_deve_ter_motivo()
    {
        var invalido = new ValidacaoClienteResultado(false, "cpf_cnpj_nao_encontrado");
        var valido = new ValidacaoClienteResultado(true);

        invalido.Valido.Should().BeFalse();
        invalido.Motivo.Should().NotBeNullOrEmpty("resultado inválido precisa de motivo");

        valido.Valido.Should().BeTrue();
        valido.Motivo.Should().BeNull("resultado válido não deve ter motivo");
    }

    [Fact]
    public void T10_CandidatoArquivoTexto_similaridade_pode_ser_zero_ou_um()
    {
        // Verifica que o record aceita valores limítrofes válidos do domínio.
        var zero = new CandidatoArquivoTexto("/p.pdf", "p", 0.0, DateTime.UtcNow, 0L, "");
        var um = new CandidatoArquivoTexto("/q.pdf", "q", 1.0, DateTime.UtcNow, 1024L, "hash");

        zero.Similaridade.Should().Be(0.0);
        um.Similaridade.Should().Be(1.0);
        um.TamanhoBytes.Should().Be(1024L);
    }

    // --- AncorarPdfConfig — invariante de backoff ---

    [Fact]
    public void T11_AncorarPdfConfig_RetryBackoffSegundos_eh_monotonicamente_crescente()
    {
        var config = new AncorarPdfConfig();
        var backoff = config.RetryBackoffSegundos;

        backoff.Should().NotBeEmpty("backoff não pode ser lista vazia");

        for (var i = 1; i < backoff.Count; i++)
        {
            backoff[i].Should().BeGreaterThan(backoff[i - 1],
                $"backoff[{i}]={backoff[i]} deve ser maior que backoff[{i - 1}]={backoff[i - 1]}");
        }
    }

    // --- ErroCodigos — invariantes de formato e completude ---

    [Fact]
    public void T12_ErroCodigos_NEG_e_TEC_totalizam_7_e_sao_distintos()
    {
        var todos = new[]
        {
            AncorarPdfErroCodigos.NegPdfSemTexto,
            AncorarPdfErroCodigos.NegClienteNaoValidado,
            AncorarPdfErroCodigos.NegArquivoNaoEncontrado,
            AncorarPdfErroCodigos.NegConfigNaoEncontrada,
            AncorarPdfErroCodigos.TecFalhaIo,
            AncorarPdfErroCodigos.TecFalhaTimeout,
            AncorarPdfErroCodigos.TecFalhaGeral
        };

        todos.Should().HaveCount(7, "7 códigos de erro operacional (4 NEG + 3 TEC)");
        todos.Distinct().Should().HaveCount(7, "todos os códigos devem ser únicos");
    }

    [Fact]
    public void T13_ErroCodigos_tipos_de_evento_sao_snake_case_sem_traco()
    {
        // Tipos de evento (ex: "tarefa_agendada") devem ser lowercase e sem traços.
        // Distingue dos códigos de erro (que usam "ANCORA-NEG-XXX").
        var eventos = new[]
        {
            AncorarPdfErroCodigos.TarefaAgendada,
            AncorarPdfErroCodigos.ExecucaoIniciada,
            AncorarPdfErroCodigos.ExecucaoConcluida,
            AncorarPdfErroCodigos.ExecucaoFalhou,
            AncorarPdfErroCodigos.MisfireDetectado,
            AncorarPdfErroCodigos.BacklogDecisao
        };

        eventos.Should().OnlyContain(
            e => e == e.ToLowerInvariant() && !e.Contains('-'),
            "tipos de evento devem ser snake_case: lowercase sem traço");
    }
}
