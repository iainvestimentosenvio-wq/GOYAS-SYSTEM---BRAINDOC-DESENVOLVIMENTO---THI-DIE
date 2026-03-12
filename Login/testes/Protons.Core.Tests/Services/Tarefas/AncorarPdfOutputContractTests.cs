using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Protons.Core.Tarefas.Models;
using Xunit;

namespace Protons.Core.Tests.Services.Tarefas;

/// <summary>
/// Contrato de saída consumer-driven para AncorarPdf.
///
/// Contexto: consumidores futuros (relatórios, integrações, UI) dependem da forma
/// estável de SaidaVariavelAncorada (SchemaVersion=1). Qualquer mudança breaking
/// neste contrato deve incrementar SchemaVersion e ter testes de migração.
///
/// Anti-duplicidade: C3 testa persistência de execução; C6 testa pipeline completo.
/// Este arquivo testa a FORMA DO CONTRATO de saída, independente de banco ou motor.
/// </summary>
[Trait("Checklist", "Contract")]
[Trait("Category", "Contract_Saida")]
public sealed class AncorarPdfOutputContractTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ──────────────────────────────────────────────────────────────────────────
    //  CONTRATO: SaidaVariavelItem — o menor átomo da saída
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CT01_SaidaVariavelItem_tem_todos_campos_do_contrato_v1()
    {
        // Consumidores esperam: Chave, Tipo, ValorBruto, ValorNormalizado, Confianca,
        //                       CorTemplate, Pagina, BboxRelativo (opcional)
        var item = new SaidaVariavelItem
        {
            Chave = "razao_social",
            Tipo = "texto",
            ValorBruto = "EMPRESA LTDA",
            ValorNormalizado = "EMPRESA LTDA",
            Confianca = 0.95,
            CorTemplate = "#FF6B35",
            Pagina = 1,
            BboxRelativo = new BboxRelativo(0.1, 0.2, 0.3, 0.05)
        };

        item.Chave.Should().NotBeNullOrEmpty("Chave identifica a variável para o consumidor");
        item.Tipo.Should().NotBeNullOrEmpty("Tipo permite ao consumidor interpretar o valor");
        item.ValorBruto.Should().NotBeNull("ValorBruto é o texto antes de normalização");
        item.ValorNormalizado.Should().NotBeNull("ValorNormalizado é o valor pronto para consumo");
        item.Confianca.Should().BeInRange(0.0, 1.0, "Confianca deve ser normalizada entre 0 e 1");
        item.CorTemplate.Should().NotBeNullOrEmpty("CorTemplate é necessário para renderização visual");
        item.Pagina.Should().BeGreaterThan(0, "Pagina começa em 1");
        item.BboxRelativo.Should().NotBeNull("BboxRelativo permite localização no PDF");
    }

    [Fact]
    public void CT02_SaidaVariavelItem_confianca_zero_e_valida_quando_sem_match()
    {
        // Confiança = 0.0 indica que nenhuma palavra foi encontrada no bbox — é válido (não erro)
        var item = new SaidaVariavelItem { Chave = "vencimento", Confianca = 0.0, ValorBruto = "" };
        item.Confianca.Should().Be(0.0);
        item.ValorBruto.Should().BeEmpty("sem match = string vazia, não null");
        item.ValorNormalizado.Should().BeEmpty();
    }

    [Fact]
    public void CT03_SaidaVariavelItem_tipo_default_e_texto()
    {
        var item = new SaidaVariavelItem { Chave = "x" };
        item.Tipo.Should().Be("texto", "tipo padrão é 'texto' para compatibilidade retroativa");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  CONTRATO: SaidaVariavelAncorada — payload consolidado por ciclo
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CT04_SaidaVariavelAncorada_schema_version_1_e_estavel()
    {
        // SchemaVersion identifica o contrato. Consumidores devem rejeitar versões desconhecidas.
        var saida = new SaidaVariavelAncorada();
        saida.SchemaVersion.Should().Be(1,
            "SchemaVersion=1 é o contrato atual; mudanças breaking incrementam para 2+");
    }

    [Fact]
    public void CT05_SaidaVariavelAncorada_campos_identidade_sao_obrigatorios()
    {
        var saida = CriarSaidaCompleta();

        saida.SaidaId.Should().NotBeNullOrEmpty("SaidaId é a chave primária do contrato");
        saida.ExecucaoId.Should().NotBeNullOrEmpty("ExecucaoId liga a saída à execução rastreável");
        saida.TarefaId.Should().BeGreaterThan(0, "TarefaId identifica qual configuração gerou a saída");
        saida.ClienteId.Should().BeGreaterThan(0, "ClienteId é mandatório para segregação de dados");
        saida.ArquivoHash.Should().NotBeNullOrEmpty("ArquivoHash garante rastreabilidade do PDF processado");
        saida.PayloadHashSha256.Should().NotBeNullOrEmpty("PayloadHash é necessário para deduplicação");
    }

    [Fact]
    public void CT06_SaidaVariavelAncorada_payload_hash_e_deterministico()
    {
        // Dois objetos idênticos devem produzir o mesmo hash (necessário para dedup)
        var saida = CriarSaidaCompleta();

        var hash1 = AncorarPdfPayloadHash.Computar(saida);
        var hash2 = AncorarPdfPayloadHash.Computar(saida);

        hash1.Should().Be(hash2, "hash deve ser determinístico para o mesmo conteúdo");
        hash1.Should().HaveLength(64, "SHA-256 em hex tem 64 caracteres");
    }

    [Fact]
    public void CT07_SaidaVariavelAncorada_payload_hash_muda_com_variavel_diferente()
    {
        // Mudança em qualquer campo das variáveis deve produzir hash diferente
        var saidaA = CriarSaidaCompleta(valorNorm: "EMPRESA A");
        var saidaB = CriarSaidaCompleta(valorNorm: "EMPRESA B");

        var hashA = AncorarPdfPayloadHash.Computar(saidaA);
        var hashB = AncorarPdfPayloadHash.Computar(saidaB);

        hashA.Should().NotBe(hashB, "payloads diferentes devem ter hashes diferentes");
    }

    [Fact]
    public void CT07A_SaidaVariavelAncorada_payload_hash_v2_ignora_execucao_e_data_execucao()
    {
        var baseline = CriarSaidaCompleta(valorNorm: "EMPRESA A");
        var variacaoMetadadoExecucao = baseline with
        {
            ExecucaoId = Guid.NewGuid().ToString("N"),
            DataExecucaoUtc = baseline.DataExecucaoUtc.AddHours(6),
            CriadoEmUtc = baseline.CriadoEmUtc.AddHours(6)
        };

        var hashBaseline = AncorarPdfPayloadHash.Computar(baseline);
        var hashVariacao = AncorarPdfPayloadHash.Computar(variacaoMetadadoExecucao);

        hashBaseline.Should().Be(hashVariacao,
            "V2 deve deduplicar por conteúdo real e ignorar metadados voláteis de execução");
    }

    [Fact]
    public void CT07B_SaidaVariavelAncorada_payload_hash_v2_muda_com_schema_version()
    {
        var v1 = CriarSaidaCompleta(valorNorm: "EMPRESA A") with { SchemaVersion = 1 };
        var v2 = v1 with { SchemaVersion = 2 };

        var hashV1 = AncorarPdfPayloadHash.Computar(v1);
        var hashV2 = AncorarPdfPayloadHash.Computar(v2);

        hashV1.Should().NotBe(hashV2,
            "schemaVersion deve participar do hash para isolar versões de contrato");
    }

    [Fact]
    public void CT08_SaidaVariavelAncorada_serializa_e_desserializa_sem_perda()
    {
        // Garantia de round-trip: saída persistida em JSON pode ser reconstituída fielmente
        var original = CriarSaidaCompleta();

        var json = JsonSerializer.Serialize(original, JsonOpts);
        var reconstituida = JsonSerializer.Deserialize<SaidaVariavelAncorada>(json, JsonOpts);

        reconstituida.Should().NotBeNull();
        reconstituida!.SaidaId.Should().Be(original.SaidaId);
        reconstituida.SchemaVersion.Should().Be(original.SchemaVersion);
        reconstituida.TarefaId.Should().Be(original.TarefaId);
        reconstituida.ClienteId.Should().Be(original.ClienteId);
        reconstituida.Variaveis.Should().HaveCount(original.Variaveis.Count);
        reconstituida.Variaveis[0].Chave.Should().Be(original.Variaveis[0].Chave);
        reconstituida.Variaveis[0].ValorNormalizado.Should().Be(original.Variaveis[0].ValorNormalizado);
        reconstituida.Variaveis[0].Confianca.Should().Be(original.Variaveis[0].Confianca);
    }

    [Fact]
    public void CT09_SaidaVariavelAncorada_json_contem_campos_esperados_pelo_consumidor()
    {
        // Consumidores externos (relatórios, APIs futuras) dependem destes campos no JSON
        var saida = CriarSaidaCompleta();
        var json = JsonSerializer.Serialize(saida, JsonOpts);

        json.Should().Contain("\"saidaId\"", "saidaId é identificador primário no contrato JSON");
        json.Should().Contain("\"schemaVersion\"", "schemaVersion permite versionamento de contrato");
        json.Should().Contain("\"tarefaId\"", "tarefaId identifica origem da execução");
        json.Should().Contain("\"clienteId\"", "clienteId garante segregação por cliente");
        json.Should().Contain("\"variaveis\"", "variaveis é o payload principal");
        json.Should().Contain("\"payloadHashSha256\"", "hash garante integridade e dedup");
        json.Should().Contain("\"chave\"", "campo Chave de cada variável");
        json.Should().Contain("\"valorNormalizado\"", "valorNormalizado é o campo mais consumido");
        json.Should().Contain("\"confianca\"", "confianca permite consumidores filtrarem por qualidade");
    }

    [Fact]
    public void CT10_BboxRelativo_normalizado_entre_0_e_1()
    {
        // BboxRelativo usa coordenadas normalizadas — consumidores dependem deste invariante
        var bbox = new BboxRelativo(X: 0.1, Y: 0.2, Largura: 0.3, Altura: 0.05);

        bbox.X.Should().BeInRange(0.0, 1.0);
        bbox.Y.Should().BeInRange(0.0, 1.0);
        bbox.Largura.Should().BeInRange(0.0, 1.0);
        bbox.Altura.Should().BeInRange(0.0, 1.0);

        // Bbox não pode ultrapassar a página
        (bbox.X + bbox.Largura).Should().BeLessThanOrEqualTo(1.0,
            "X + Largura não pode ultrapassar 1.0 (largura da página)");
        (bbox.Y + bbox.Altura).Should().BeLessThanOrEqualTo(1.0,
            "Y + Altura não pode ultrapassar 1.0 (altura da página)");
    }

    [Fact]
    public void CT11_Variaveis_ordenadas_por_chave_para_hash_deterministico()
    {
        // AncorarPdfPayloadHash.Computar deve ordenar variáveis por Chave (RFC 8785 simplificado)
        // Garantia: saida com variáveis em ordens diferentes produz o mesmo hash
        var saidaABC = new SaidaVariavelAncorada
        {
            SaidaId = "s1", ExecucaoId = "e1", TarefaId = 1, ClienteId = 1,
            ArquivoPath = "/tmp/a.pdf", ArquivoHash = "abc", ArquivoNomeLogico = "a.pdf",
            DataExecucaoUtc = DateTime.UnixEpoch, SchemaVersion = 1,
            Variaveis = [
                new SaidaVariavelItem { Chave = "a", ValorNormalizado = "1" },
                new SaidaVariavelItem { Chave = "b", ValorNormalizado = "2" },
                new SaidaVariavelItem { Chave = "c", ValorNormalizado = "3" }
            ]
        };

        var saidaCBA = saidaABC with
        {
            Variaveis = [
                new SaidaVariavelItem { Chave = "c", ValorNormalizado = "3" },
                new SaidaVariavelItem { Chave = "b", ValorNormalizado = "2" },
                new SaidaVariavelItem { Chave = "a", ValorNormalizado = "1" }
            ]
        };

        var hashABC = AncorarPdfPayloadHash.Computar(saidaABC);
        var hashCBA = AncorarPdfPayloadHash.Computar(saidaCBA);

        hashABC.Should().Be(hashCBA,
            "hash deve ser estável independente da ordem das variáveis no payload");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static SaidaVariavelAncorada CriarSaidaCompleta(string valorNorm = "PROTONS LTDA")
    {
        var saida = new SaidaVariavelAncorada
        {
            SaidaId = Guid.NewGuid().ToString("N"),
            ExecucaoId = Guid.NewGuid().ToString("N"),
            TarefaId = 42,
            ClienteId = 7,
            ArquivoPath = "/dados/relatorio_2026.pdf",
            ArquivoHash = "abc123def456",
            ArquivoNomeLogico = "relatorio_2026",
            DataExecucaoUtc = DateTime.UtcNow,
            SchemaVersion = 1,
            Variaveis =
            [
                new SaidaVariavelItem
                {
                    Chave = "razao_social",
                    Tipo = "texto",
                    ValorBruto = valorNorm,
                    ValorNormalizado = valorNorm,
                    Confianca = 0.98,
                    CorTemplate = "#FF6B35",
                    Pagina = 1,
                    BboxRelativo = new BboxRelativo(0.1, 0.05, 0.5, 0.03)
                },
                new SaidaVariavelItem
                {
                    Chave = "valor_total",
                    Tipo = "moeda_brl",
                    ValorBruto = "R$ 12.345,67",
                    ValorNormalizado = "12345.67",
                    Confianca = 1.0,
                    CorTemplate = "#2196F3",
                    Pagina = 2,
                    BboxRelativo = new BboxRelativo(0.6, 0.8, 0.3, 0.04)
                }
            ],
            CriadoEmUtc = DateTime.UtcNow
        };

        // Computar hash canônico (como o motor faz antes de persistir)
        return saida with { PayloadHashSha256 = AncorarPdfPayloadHash.Computar(saida) };
    }
}
