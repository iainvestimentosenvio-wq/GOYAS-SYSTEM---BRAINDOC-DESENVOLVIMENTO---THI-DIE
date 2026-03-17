using System.Buffers;
using System.Security.Cryptography;
using Protons.Core.Tarefas.Models;
using Protons.Core.Tarefas.Services;

namespace Protons.Infrastructure.Tarefas.Services;

// Descobre PDFs em pasta, seleciona o melhor por similaridade Jaro-Winkler,
// verifica estabilidade (mtime estável 500ms) e computa SHA-256 do arquivo selecionado.
public sealed class AncorarPdfSeletorArquivoPasta : IAncorarPdfSeletorArquivo
{
    // Janela de estabilidade: mtime deve ser estável por esta duração antes de processar.
    private static readonly TimeSpan EstabilidadeMinima = TimeSpan.FromMilliseconds(500);

    public async Task<SelecaoArquivoResultado?> SelecionarMelhorAsync(
        string pastaPath,
        string nomeReferencia,
        double limiarSimilaridade,
        bool monitorarSubpastas,
        string cicloId,
        CancellationToken ct)
    {
        if (!Directory.Exists(pastaPath))
            return null;

        var searchOption = monitorarSubpastas
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        // Descoberta: monta candidatos com JaroWinkler por nome de arquivo (sem extensão).
        var todos = new List<string>();
        var candidatos = new List<CandidatoArquivoTexto>();
        foreach (var arquivo in Directory.EnumerateFiles(pastaPath, "*.pdf", searchOption))
        {
            ct.ThrowIfCancellationRequested();
            todos.Add(arquivo);
            var nomeArquivo = Path.GetFileNameWithoutExtension(arquivo);
            var sim = JaroWinkler(nomeArquivo.AsSpan(), nomeReferencia.AsSpan());
            if (sim < limiarSimilaridade)
                continue;

            FileInfo fi;
            try { fi = new FileInfo(arquivo); }
            catch (Exception) { continue; }

            candidatos.Add(new CandidatoArquivoTexto(
                arquivo, nomeArquivo, sim, fi.LastWriteTimeUtc, fi.Length,
                ArquivoHash: string.Empty));
        }

        if (candidatos.Count == 0)
        {
            // Fallback: sem match por nome — usa o PDF mais antigo da pasta.
            var fallback = todos
                .OrderBy(f => File.GetLastWriteTimeUtc(f))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (fallback is null) return null;
            candidatos.Add(new CandidatoArquivoTexto(
                fallback, Path.GetFileNameWithoutExtension(fallback), 0.0,
                File.GetLastWriteTimeUtc(fallback), new FileInfo(fallback).Length, ""));
        }

        // Seleciona melhor: similaridade desc, mtime asc (mais antigo por ciclo), path asc (determinístico).
        var melhor = candidatos
            .OrderByDescending(c => c.Similaridade)
            .ThenBy(c => c.MtimeUtc)
            .ThenBy(c => c.ArquivoPath, StringComparer.OrdinalIgnoreCase)
            .First();

        // Verifica estabilidade: aguarda e confirma que mtime não mudou.
        await Task.Delay(EstabilidadeMinima, ct);

        DateTime mtimeAtual;
        try { mtimeAtual = File.GetLastWriteTimeUtc(melhor.ArquivoPath); }
        catch (Exception) { return null; }

        if (mtimeAtual != melhor.MtimeUtc)
            return null; // arquivo ainda sendo escrito

        // Verifica abertura para leitura (arquivo não está bloqueado).
        try
        {
            using var fs = new FileStream(melhor.ArquivoPath, FileMode.Open,
                FileAccess.Read, FileShare.Read);
        }
        catch (Exception)
        {
            return null;
        }

        string hash;
        try { hash = await ComputarHashAsync(melhor.ArquivoPath, ct); }
        catch (Exception) { return null; }

        return new SelecaoArquivoResultado(
            melhor.ArquivoPath,
            melhor.NomeEsperadoLogico,
            hash,
            melhor.TamanhoBytes,
            melhor.MtimeUtc);
    }

    // Tamanho do buffer de leitura para SHA-256: 80 KB — equilibra throughput e pressão GC.
    private const int HashBufferSize = 81920;

    private static async Task<string> ComputarHashAsync(string path, CancellationToken ct)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: HashBufferSize, useAsync: true);

        // ArrayPool elimina alocação de 80KB no heap por arquivo em alto throughput.
        var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            int read;
            while ((read = await fs.ReadAsync(buffer.AsMemory(0, HashBufferSize), ct)) > 0)
                sha.AppendData(buffer, 0, read);
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // Implementação de Jaro-Winkler (~50 linhas, sem dependências externas).
    // Referência: https://en.wikipedia.org/wiki/Jaro%E2%80%93Winkler_distance
    private static double JaroWinkler(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2)
    {
        if (s1.IsEmpty && s2.IsEmpty) return 1.0;
        if (s1.IsEmpty || s2.IsEmpty) return 0.0;

        var jaro = Jaro(s1, s2);
        // Prefixo comum: até 4 caracteres.
        var prefixLen = 0;
        var maxPrefix = Math.Min(4, Math.Min(s1.Length, s2.Length));
        for (var i = 0; i < maxPrefix; i++)
        {
            if (char.ToLowerInvariant(s1[i]) == char.ToLowerInvariant(s2[i]))
                prefixLen++;
            else
                break;
        }
        const double P = 0.1; // fator de escala Winkler
        return jaro + prefixLen * P * (1.0 - jaro);
    }

    private static double Jaro(ReadOnlySpan<char> s1, ReadOnlySpan<char> s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matchWindow = Math.Max(len1, len2) / 2 - 1;
        if (matchWindow < 0) matchWindow = 0;

        Span<bool> matched1 = stackalloc bool[len1];
        Span<bool> matched2 = stackalloc bool[len2];
        matched1.Clear();
        matched2.Clear();

        var matches = 0;
        for (var i = 0; i < len1; i++)
        {
            var start = Math.Max(0, i - matchWindow);
            var end = Math.Min(i + matchWindow + 1, len2);
            for (var j = start; j < end; j++)
            {
                if (matched2[j]) continue;
                if (char.ToLowerInvariant(s1[i]) != char.ToLowerInvariant(s2[j])) continue;
                matched1[i] = true;
                matched2[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0.0;

        // Transposes
        var t = 0;
        var k = 0;
        for (var i = 0; i < len1; i++)
        {
            if (!matched1[i]) continue;
            while (!matched2[k]) k++;
            if (char.ToLowerInvariant(s1[i]) != char.ToLowerInvariant(s2[k])) t++;
            k++;
        }

        var m = (double)matches;
        return (m / len1 + m / len2 + (m - t / 2.0) / m) / 3.0;
    }
}
