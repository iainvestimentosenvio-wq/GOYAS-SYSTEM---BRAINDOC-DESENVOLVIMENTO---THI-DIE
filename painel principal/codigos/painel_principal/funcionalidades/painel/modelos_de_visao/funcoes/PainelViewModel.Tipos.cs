using System;
using Avalonia;
using Avalonia.Media;

namespace Protons.UI.Painel.ViewModels;

public sealed record NotaFiscalResumo(
    string Numero,
    string Fornecedor,
    string Status,
    string Data,
    string StatusCor,
    string StatusForeground);

public enum CategoriaTimelineEsteira
{
    Futuras = 0,
    Atrasadas = 1,
    Realizadas = 2
}

public sealed record TarefaTimelineItem(
    int Id,
    string Titulo,
    string Responsavel,
    string HorarioTexto,
    string StatusTexto,
    string CorStatus,
    bool TemErro,
    bool Cancelada,
    int? GapMinutosAteProxima,
    bool TemGap,
    string BadgeTempo,
    bool TemConexao);

public sealed record HistoricoExecucaoItem(
    int Id,
    int ClienteId,
    string Cliente,
    string Tarefa,
    string Usuario,
    string Status,
    string CorStatus,
    string FluxoHora);

public sealed record FerramentaTarefaLateralItem(
    string FerramentaId,
    string Nome,
    string Descricao,
    string CorFundo,
    string CorBorda,
    string CorTexto,
    string Selo,
    string CategoriaTag,
    bool EhExtratorPdf = false,
    string? ImagemCardUri = null,
    string? ImagemGhostUri = null)
{
    public bool EhIconeGenerico => !EhExtratorPdf;
}

public sealed record NovaTarefaDropPayload(
    string FerramentaId,
    string NomeFerramenta,
    int EsteiraId,
    DateTime TempoAlvoUtc);

public sealed class UsuarioPermissaoPainel
{
    public int UserId { get; init; }
    public string Nome { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }
    public bool Permitido { get; set; }
    public bool PodeEditar { get; set; }
}

public sealed record TarefaReguaItem(
    int TarefaId,
    string Titulo,
    DateTime VencimentoUtc,
    string HorarioTexto,
    string StatusTexto,
    string CorStatus,
    bool EhAtrasada,
    bool EhRecorrente,
    int? EsteiraId,
    string Responsavel,
    bool TemErro,
    int AgendadoPorUserId = 0,
    string AgendadoPorNome = "",
    string AgendadoPorIniciais = "",
    DateTime CriadoEmUtc = default)
{
    private static readonly string[] AvatarPalette =
    [
        "#2563EB",
        "#0D9488",
        "#7C3AED",
        "#EA580C",
        "#DC2626",
        "#0891B2",
        "#4F46E5",
        "#16A34A"
    ];

    public string AvatarNome => string.IsNullOrWhiteSpace(AgendadoPorNome) ? Responsavel : AgendadoPorNome;

    public string AvatarIniciais
    {
        get
        {
            var texto = string.IsNullOrWhiteSpace(AgendadoPorIniciais)
                ? ExtrairIniciais(AvatarNome)
                : AgendadoPorIniciais.Trim().ToUpperInvariant();

            return string.IsNullOrWhiteSpace(texto) ? "?" : texto;
        }
    }

    public string AvatarCorHex
    {
        get
        {
            var seed = AgendadoPorUserId != 0 ? AgendadoPorUserId : CalcularHashDeterministico(AvatarNome ?? string.Empty);
            var index = Math.Abs(seed) % AvatarPalette.Length;
            return AvatarPalette[index];
        }
    }

    public DateTime CriadoEmUtcOrdenacao => CriadoEmUtc == default ? VencimentoUtc : CriadoEmUtc;

    // Construtor de compatibilidade (manter codigo existente funcionando)
    public TarefaReguaItem(
        int TarefaId,
        string Titulo,
        DateTime VencimentoUtc,
        string HorarioTexto,
        string StatusTexto,
        string CorStatus,
        bool EhAtrasada,
        bool EhRecorrente,
        int? EsteiraId,
        string Responsavel)
        : this(TarefaId, Titulo, VencimentoUtc, HorarioTexto, StatusTexto,
               CorStatus, EhAtrasada, EhRecorrente, EsteiraId, Responsavel, false,
               0, Responsavel, ExtrairIniciais(Responsavel), default)
    {
    }

    private static string ExtrairIniciais(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
            return "?";

        var partes = nome
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (partes.Length == 0)
            return "?";

        if (partes.Length == 1)
            return partes[0][0].ToString().ToUpperInvariant();

        return string.Concat(
            partes[0][0].ToString().ToUpperInvariant(),
            partes[1][0].ToString().ToUpperInvariant());
    }

    private static int CalcularHashDeterministico(string texto)
    {
        unchecked
        {
            var hash = 23;
            foreach (var caractere in texto)
                hash = (hash * 31) + caractere;

            return hash;
        }
    }
}

public sealed record ClienteResumoPainel(
    int Id,
    string CodigoCliente,
    string Nome,
    string? NomeFantasia,
    string? GrupoEmpresarialNome,
    string TipoDocumento,
    string Documento,
    string? Email,
    string? Telefone);

public sealed record ClienteSugestaoPainel(
    int ClienteId,
    string Titulo,
    string Subtitulo,
    string TextoSelecao);

public enum SeletorClienteTipoItem
{
    Grupo = 0,
    Cliente = 1,
    Vazio = 2
}

public sealed record SeletorClienteItemPainel(
    SeletorClienteTipoItem Tipo,
    int? ClienteId,
    string? GrupoNome,
    string TextoPrincipal,
    string? TextoSecundario,
    int NivelIndentacao,
    bool GrupoExpandido,
    string GrupoChave,
    bool Destacado)
{
    public bool EhGrupo => Tipo == SeletorClienteTipoItem.Grupo;
    public bool EhCliente => Tipo == SeletorClienteTipoItem.Cliente;
    public bool EhVazio => Tipo == SeletorClienteTipoItem.Vazio;
    public bool TemTextoSecundario => !string.IsNullOrWhiteSpace(TextoSecundario);
    public string IconeGrupo => GrupoExpandido ? "v" : ">";
    public Thickness MargemConteudo => new Thickness(NivelIndentacao * 18, 0, 0, 0);
    public FontWeight PesoTextoPrincipal => Destacado ? FontWeight.SemiBold : FontWeight.Normal;
}

public sealed record GrupoEmpresarialPainel(
    int Id,
    string Nome);

public enum EscopoHistoricoExecucao
{
    Global = 0,
    Cliente = 1
}
