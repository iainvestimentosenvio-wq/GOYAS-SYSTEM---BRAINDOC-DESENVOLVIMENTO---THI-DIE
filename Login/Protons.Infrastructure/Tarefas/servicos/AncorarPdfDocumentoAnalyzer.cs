using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

public sealed class AncorarPdfDocumentoAnalyzer : IAncorarPdfDocumentoAnalyzer
{
    private const string AnalyzerVersion = "analyzer_v2";
    private const int TokensMinimosRotaNativa = 25;
    private const double CoberturaMinimaRotaNativa = 0.015;
    private const double CoberturaMinimaSemOcr = 0.005;
    private readonly IAncorarPdfExtratorTexto _extratorNativo;
    private readonly Func<int, string, IAncorarPdfExtratorTexto> _ocrFactory;
    private readonly Action<string, string?>? _onLog;
    private const int MaxCacheEntries = 10;
    private readonly ConcurrentDictionary<string, AncorarPdfDocumentoAnalise> _analysisCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReadOnlyList<PdfPaginaTexto>> _ocrPagesCache = new(StringComparer.Ordinal);

    public AncorarPdfDocumentoAnalyzer(
        IAncorarPdfExtratorTexto extratorNativo,
        Func<int, string, IAncorarPdfExtratorTexto> ocrFactory,
        Action<string, string?>? onLog = null)
    {
        _extratorNativo = extratorNativo;
        _ocrFactory = ocrFactory;
        _onLog = onLog;
    }

    public async Task<AncorarPdfDocumentoAnalise> AnalisarAsync(
        string arquivoPath,
        AncorarPdfDocumentoAnalyzerOptions options,
        CancellationToken ct)
    {
        var cacheKey = BuildAnalysisCacheKey(arquivoPath, options);
        if (_analysisCache.TryGetValue(cacheKey, out var cacheHit))
            return cacheHit;

        var nativeSw = Stopwatch.StartNew();
        var paginasNativas = await _extratorNativo.ExtrairAsync(arquivoPath, ct).ConfigureAwait(false);
        nativeSw.Stop();

        IReadOnlyList<PdfPaginaTexto>? paginasOcr = null;
        if (options.AnalyzerV2Ativo && options.HybridOcrAtivo && DeveExecutarOcr(paginasNativas))
            paginasOcr = await GetOrLoadOcrPagesAsync(arquivoPath, options, ct).ConfigureAwait(false);

        var paginas = new List<AncorarPdfPaginaAnalise>(paginasNativas.Count);
        var diagnosticos = new List<AncorarPdfExtracaoDiagnostico>(paginasNativas.Count);
        var familia = string.Empty;

        for (var i = 0; i < paginasNativas.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var paginaNativa = paginasNativas[i];
            var paginaOcr = paginasOcr is not null && i < paginasOcr.Count ? paginasOcr[i] : null;
            var coberturaNativa = CalcularCobertura(paginaNativa.Palavras.Select(p => p.Bbox));
            var rota = DeterminarRotaPagina(paginaNativa, coberturaNativa, options);
            var tokensMesclados = CriarTokensMesclados(paginaNativa, paginaOcr, rota, options);
            var tokensNativos = paginaNativa.Palavras.Count;
            var tokensOcr = paginaOcr?.Palavras.Count ?? 0;
            var qualidadePagina = CalcularQualidadePagina(tokensNativos, tokensOcr, coberturaNativa, rota);
            var motivoRota = CriarMotivoRota(paginaNativa, coberturaNativa, rota, options);
            var paginaAnalise = new AncorarPdfPaginaAnalise(
                Numero: paginaNativa.Numero,
                LarguraPt: paginaNativa.LarguraPt,
                AlturaPt: paginaNativa.AlturaPt,
                Tokens: tokensMesclados,
                RotaExtracao: rota,
                CoberturaTextoNativo: coberturaNativa,
                TokensNativos: tokensNativos,
                TokensOcr: tokensOcr,
                OcrExecutado: rota != AncorarPdfRotaExtracao.Nativo && tokensOcr > 0,
                MotivoRota: motivoRota,
                RotacaoDetectada: false,
                QualidadePagina: qualidadePagina);
            paginas.Add(paginaAnalise);
            diagnosticos.Add(new AncorarPdfExtracaoDiagnostico(
                Pagina: paginaNativa.Numero,
                RotaExtracao: rota,
                CoberturaTextoNativo: coberturaNativa,
                TokensNativos: tokensNativos,
                TokensOcr: tokensOcr,
                OcrExecutado: paginaAnalise.OcrExecutado,
                MotivoRota: motivoRota,
                LatenciaNativoMs: nativeSw.ElapsedMilliseconds,
                LatenciaOcrMs: paginaAnalise.OcrExecutado ? options.TimeoutPaginaOcrMs : 0,
                Avisos: CriarAvisosPagina(rota, tokensNativos, tokensOcr, coberturaNativa)));
        }

        if (paginas.Count > 0)
            familia = InferirFamiliaDocumento(paginas[0].Tokens);

        var analise = new AncorarPdfDocumentoAnalise(arquivoPath, paginas, diagnosticos, familia);
        EvictIfNeeded(_analysisCache);
        _analysisCache[cacheKey] = analise;
        return analise;
    }

    public async Task<IReadOnlyList<AncorarPdfTokenAnalise>> AnalisarRegiaoAsync(
        string arquivoPath,
        AncorarPdfAnaliseRegiaoRequest request,
        AncorarPdfDocumentoAnalyzerOptions options,
        CancellationToken ct)
    {
        var analise = await AnalisarAsync(arquivoPath, options, ct).ConfigureAwait(false);
        var pagina = analise.ObterPagina(request.Pagina);
        if (pagina is null)
            return [];

        var tokensNaRegiao = FiltrarTokensPorRegiao(pagina.Tokens, request.Regiao);
        if (tokensNaRegiao.Count > 0)
            return tokensNaRegiao;

        if (!options.HybridOcrAtivo)
            return [];

        var paginasOcr = await GetOrLoadOcrPagesAsync(
            arquivoPath,
            options with
            {
                OcrDpi = Math.Clamp(request.OcrDpi, 150, 600),
                OcrLang = string.IsNullOrWhiteSpace(request.OcrLang) ? options.OcrLang : request.OcrLang.Trim().ToLowerInvariant()
            },
            ct).ConfigureAwait(false);

        if (request.Pagina < 1 || request.Pagina > paginasOcr.Count)
            return [];

        var ocrPagina = paginasOcr[request.Pagina - 1];
        var tokensOcr = ocrPagina.Palavras
            .Select(p => new AncorarPdfTokenAnalise(
                p.Texto,
                p.Bbox,
                AncorarPdfTextoOrigem.OcrRegiao,
                ConfiancaFonte: 0.78))
            .ToArray();

        var encontrados = FiltrarTokensPorRegiao(tokensOcr, request.Regiao);
        if (encontrados.Count > 0 || !request.RetryExpandido)
            return encontrados;

        var expandida = ExpandirRegiao(request.Regiao, 0.03, 0.02);
        return FiltrarTokensPorRegiao(tokensOcr, expandida);
    }

    private async Task<IReadOnlyList<PdfPaginaTexto>> GetOrLoadOcrPagesAsync(
        string arquivoPath,
        AncorarPdfDocumentoAnalyzerOptions options,
        CancellationToken ct)
    {
        var key = BuildOcrCacheKey(arquivoPath, options);
        if (_ocrPagesCache.TryGetValue(key, out var cacheHit))
            return cacheHit;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Math.Clamp(options.TimeoutDocumentoMs, 1000, 60000));

        var configSnapshot = AncorarPdfOcrConfigContext.Current;
        var syntheticConfig = configSnapshot ?? new AncorarPdfConfiguracaoTarefa
        {
            OcrFallbackAtivo = options.HybridOcrAtivo,
            OcrDpi = Math.Clamp(options.OcrDpi, 150, 600),
            OcrLang = string.IsNullOrWhiteSpace(options.OcrLang) ? "por+eng" : options.OcrLang.Trim().ToLowerInvariant()
        };

        AncorarPdfOcrConfigContext.Current = syntheticConfig;
        try
        {
            var ocrExtractor = _ocrFactory(syntheticConfig.OcrDpi, syntheticConfig.OcrLang);
            var paginas = await ocrExtractor.ExtrairAsync(arquivoPath, timeoutCts.Token).ConfigureAwait(false);
            EvictIfNeeded(_ocrPagesCache);
            _ocrPagesCache[key] = paginas;
            return paginas;
        }
        finally
        {
            AncorarPdfOcrConfigContext.Current = configSnapshot;
        }
    }

    private static bool DeveExecutarOcr(IReadOnlyList<PdfPaginaTexto> paginasNativas)
    {
        foreach (var pagina in paginasNativas)
        {
            var cobertura = CalcularCobertura(pagina.Palavras.Select(p => p.Bbox));
            if (pagina.Palavras.Count == 0 || cobertura < CoberturaMinimaRotaNativa || pagina.Palavras.Count < TokensMinimosRotaNativa)
                return true;
        }

        return false;
    }

    private static AncorarPdfRotaExtracao DeterminarRotaPagina(
        PdfPaginaTexto pagina,
        double coberturaNativa,
        AncorarPdfDocumentoAnalyzerOptions options)
    {
        if (!options.AnalyzerV2Ativo || !options.HybridOcrAtivo)
            return AncorarPdfRotaExtracao.Nativo;

        if (pagina.Palavras.Count == 0 || coberturaNativa < CoberturaMinimaSemOcr)
            return AncorarPdfRotaExtracao.OcrPagina;

        if (pagina.Palavras.Count < TokensMinimosRotaNativa || coberturaNativa < CoberturaMinimaRotaNativa)
            return AncorarPdfRotaExtracao.OcrPagina;

        return AncorarPdfRotaExtracao.Nativo;
    }

    private static IReadOnlyList<AncorarPdfTokenAnalise> CriarTokensMesclados(
        PdfPaginaTexto paginaNativa,
        PdfPaginaTexto? paginaOcr,
        AncorarPdfRotaExtracao rota,
        AncorarPdfDocumentoAnalyzerOptions options)
    {
        var tokens = new List<AncorarPdfTokenAnalise>(paginaNativa.Palavras.Count + (paginaOcr?.Palavras.Count ?? 0));
        foreach (var palavra in paginaNativa.Palavras)
        {
            tokens.Add(new AncorarPdfTokenAnalise(
                palavra.Texto,
                palavra.Bbox,
                AncorarPdfTextoOrigem.Nativo,
                ConfiancaFonte: 1.0));
        }

        if (paginaOcr is null || !options.HybridOcrAtivo || rota == AncorarPdfRotaExtracao.Nativo)
            return tokens;

        foreach (var palavraOcr in paginaOcr.Palavras)
        {
            if (ExisteTokenNativoEquivalente(tokens, palavraOcr))
                continue;

            tokens.Add(new AncorarPdfTokenAnalise(
                palavraOcr.Texto,
                palavraOcr.Bbox,
                AncorarPdfTextoOrigem.OcrPagina,
                ConfiancaFonte: 0.78));
        }

        return tokens
            .OrderBy(t => t.Bbox.Y)
            .ThenBy(t => t.Bbox.X)
            .ToArray();
    }

    private static bool ExisteTokenNativoEquivalente(
        IReadOnlyList<AncorarPdfTokenAnalise> tokensExistentes,
        PdfPalavra palavraOcr)
    {
        foreach (var token in tokensExistentes)
        {
            if (token.Origem != AncorarPdfTextoOrigem.Nativo)
                continue;

            if (!IntersectaFortemente(token.Bbox, palavraOcr.Bbox))
                continue;

            if (string.Equals(NormalizarTexto(token.Texto), NormalizarTexto(palavraOcr.Texto), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IntersectaFortemente(BboxRelativo a, BboxRelativo b)
    {
        var intersecaoX = Math.Max(0, Math.Min(a.X + a.Largura, b.X + b.Largura) - Math.Max(a.X, b.X));
        var intersecaoY = Math.Max(0, Math.Min(a.Y + a.Altura, b.Y + b.Altura) - Math.Max(a.Y, b.Y));
        var areaIntersecao = intersecaoX * intersecaoY;
        if (areaIntersecao <= 0)
            return false;

        var areaA = Math.Max(a.Largura * a.Altura, 1e-9);
        var areaB = Math.Max(b.Largura * b.Altura, 1e-9);
        var razao = areaIntersecao / Math.Min(areaA, areaB);
        return razao >= 0.60;
    }

    private static IReadOnlyList<AncorarPdfTokenAnalise> FiltrarTokensPorRegiao(
        IReadOnlyList<AncorarPdfTokenAnalise> tokens,
        BboxRelativo regiao)
    {
        return tokens
            .Where(t => IntersectaFortementeOuContem(regiao, t.Bbox))
            .OrderBy(t => DistanciaCentroAoCentro(regiao, t.Bbox))
            .ToArray();
    }

    private static bool IntersectaFortementeOuContem(BboxRelativo regiao, BboxRelativo token)
    {
        if (token.X >= regiao.X &&
            token.Y >= regiao.Y &&
            token.X + token.Largura <= regiao.X + regiao.Largura &&
            token.Y + token.Altura <= regiao.Y + regiao.Altura)
            return true;

        return IntersectaFortemente(regiao, token);
    }

    private static double DistanciaCentroAoCentro(BboxRelativo a, BboxRelativo b)
    {
        var ax = a.X + (a.Largura / 2);
        var ay = a.Y + (a.Altura / 2);
        var bx = b.X + (b.Largura / 2);
        var by = b.Y + (b.Altura / 2);
        var dx = ax - bx;
        var dy = ay - by;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static BboxRelativo ExpandirRegiao(BboxRelativo regiao, double margemX, double margemY)
    {
        var x = Math.Max(0, regiao.X - margemX);
        var y = Math.Max(0, regiao.Y - margemY);
        var x2 = Math.Min(1, regiao.X + regiao.Largura + margemX);
        var y2 = Math.Min(1, regiao.Y + regiao.Altura + margemY);
        return new BboxRelativo(x, y, x2 - x, y2 - y);
    }

    private static double CalcularCobertura(IEnumerable<BboxRelativo> caixas)
    {
        var cobertura = 0.0;
        foreach (var caixa in caixas)
            cobertura += Math.Clamp(caixa.Largura, 0, 1) * Math.Clamp(caixa.Altura, 0, 1);

        return Math.Clamp(cobertura, 0, 1);
    }

    private static double CalcularQualidadePagina(
        int tokensNativos,
        int tokensOcr,
        double coberturaNativa,
        AncorarPdfRotaExtracao rota)
    {
        var scoreNativo = Math.Min(0.55, coberturaNativa * 6.0);
        var scoreTokens = Math.Min(0.30, (tokensNativos / 100.0) + (tokensOcr / 180.0));
        var bonusRota = rota == AncorarPdfRotaExtracao.Nativo ? 0.15 : 0.08;
        return Math.Clamp(scoreNativo + scoreTokens + bonusRota, 0.10, 1.0);
    }

    private static string CriarMotivoRota(
        PdfPaginaTexto pagina,
        double coberturaNativa,
        AncorarPdfRotaExtracao rota,
        AncorarPdfDocumentoAnalyzerOptions options)
    {
        if (!options.AnalyzerV2Ativo)
            return "analyzer_v2_desabilitado";

        if (!options.HybridOcrAtivo)
            return "hybrid_ocr_desabilitado";

        return rota switch
        {
            AncorarPdfRotaExtracao.Nativo when pagina.Palavras.Count >= TokensMinimosRotaNativa =>
                $"tokens_nativos={pagina.Palavras.Count} cobertura={coberturaNativa:0.000}",
            AncorarPdfRotaExtracao.OcrPagina when pagina.Palavras.Count == 0 =>
                "pagina_sem_texto_nativo",
            AncorarPdfRotaExtracao.OcrPagina when coberturaNativa < CoberturaMinimaSemOcr =>
                $"cobertura_nativa_baixa={coberturaNativa:0.000}",
            AncorarPdfRotaExtracao.OcrPagina =>
                $"texto_parcial tokens={pagina.Palavras.Count} cobertura={coberturaNativa:0.000}",
            _ => "rota_desconhecida"
        };
    }

    private static IReadOnlyList<string> CriarAvisosPagina(
        AncorarPdfRotaExtracao rota,
        int tokensNativos,
        int tokensOcr,
        double coberturaNativa)
    {
        var avisos = new List<string>(2);
        if (rota != AncorarPdfRotaExtracao.Nativo)
            avisos.Add("ocr_utilizado");
        if (tokensNativos == 0 && tokensOcr == 0)
            avisos.Add("pagina_sem_tokens");
        if (coberturaNativa < CoberturaMinimaSemOcr)
            avisos.Add("cobertura_nativa_critica");
        return avisos;
    }

    private static string InferirFamiliaDocumento(IReadOnlyList<AncorarPdfTokenAnalise> tokens)
    {
        var texto = string.Join(' ', tokens.Take(120).Select(t => NormalizarTexto(t.Texto)));
        if (texto.Contains("danfe", StringComparison.OrdinalIgnoreCase) ||
            texto.Contains("nota fiscal", StringComparison.OrdinalIgnoreCase))
            return "danfe_nfe";
        if (texto.Contains("boleto", StringComparison.OrdinalIgnoreCase))
            return "boleto";
        if (texto.Contains("recibo", StringComparison.OrdinalIgnoreCase))
            return "recibo";
        if (texto.Contains("fatura", StringComparison.OrdinalIgnoreCase))
            return "fatura";
        return "documento_generico";
    }

    private static string NormalizarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return string.Empty;

        return string.Join(" ", texto
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .Trim();
    }

    private static string BuildAnalysisCacheKey(string arquivoPath, AncorarPdfDocumentoAnalyzerOptions options)
    {
        var fingerprint = BuildFingerprint(arquivoPath, options.ArquivoHashHint);
        return $"{AnalyzerVersion}|analysis|{fingerprint}|v2={options.AnalyzerV2Ativo}|hybrid={options.HybridOcrAtivo}|dpi={options.OcrDpi}|lang={options.OcrLang}";
    }

    private static string BuildOcrCacheKey(string arquivoPath, AncorarPdfDocumentoAnalyzerOptions options)
    {
        var fingerprint = BuildFingerprint(arquivoPath, options.ArquivoHashHint);
        return $"{AnalyzerVersion}|ocr|{fingerprint}|dpi={options.OcrDpi}|lang={options.OcrLang}";
    }

    private static string BuildFingerprint(string arquivoPath, string? arquivoHashHint)
    {
        if (!string.IsNullOrWhiteSpace(arquivoHashHint))
            return arquivoHashHint.Trim().ToLowerInvariant();

        try
        {
            var fi = new FileInfo(arquivoPath);
            var raw = $"{arquivoPath}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
            var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
        catch (Exception)
        {
            var fallback = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(arquivoPath));
            return Convert.ToHexString(fallback).ToLowerInvariant();
        }
    }

    private static void EvictIfNeeded<T>(ConcurrentDictionary<string, T> cache)
    {
        if (cache.Count < MaxCacheEntries)
            return;

        var keysToRemove = cache.Keys.Take(cache.Count - MaxCacheEntries + 1).ToList();
        foreach (var key in keysToRemove)
            cache.TryRemove(key, out _);
    }
}
