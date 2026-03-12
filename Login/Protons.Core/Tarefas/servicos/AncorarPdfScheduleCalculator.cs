using Protons.Core.Tarefas.Models;

namespace Protons.Core.Tarefas.Services;

/// <summary>
/// Resultado da resolucao de um horario local para UTC considerando transicoes DST.
/// </summary>
public enum AncorarPdfDstResolucao
{
    /// <summary>Conversao normal, sem ambiguidade nem horario invalido.</summary>
    Normal = 0,

    /// <summary>Horario ambiguo resolvido pela politica configurada.</summary>
    AmbiguoResolvido = 1,

    /// <summary>Horario invalido (gap DST): avancado para o proximo horario valido.</summary>
    InvalidoAvancado = 2,

    /// <summary>Horario invalido (gap DST): recuado para o horario valido anterior.</summary>
    InvalidoRecuado = 3,

    /// <summary>Horario invalido (gap DST): pulado conforme politica de auditoria.</summary>
    InvalidoPulado = 4
}

/// <summary>
/// Calculador de agendamento enterprise para o ancorar_pdf.
/// Responsabilidades:
///   - Converter horario local para UTC respeitando o timezone da tarefa (nao o timezone da maquina).
///   - Aplicar precisao de segundos ao horario.
///   - Resolver transicoes DST (gap e overlap) usando as politicas configuradas por tarefa.
///   - Calcular a proxima ocorrencia de recorrencia com fallback de dia mensal.
///
/// Usa TimeZoneInfo nativo do .NET 8, que suporta IDs IANA via OS tzdata em Linux/macOS
/// e IDs Windows-style em Windows. Nenhum pacote externo necessario.
/// </summary>
public static class AncorarPdfScheduleCalculator
{
    // ------------------------------------------------------------------
    // API publica
    // ------------------------------------------------------------------

    /// <summary>
    /// Converte um horario local para UTC usando o timezone especifico da tarefa.
    /// Aplica a politica correta para horarios invalidos (gap DST) e ambiguos (overlap DST).
    /// </summary>
    /// <param name="localDateTime">Horario local do usuario (Kind deve ser Unspecified ou Local).</param>
    /// <param name="timezoneId">ID IANA (ex: "America/New_York") ou Windows (ex: "Eastern Standard Time").</param>
    /// <param name="invalidoPolicy">Politica para horario que cai no gap da virada de horario de verao.</param>
    /// <param name="ambiguoPolicy">Politica para horario que existe duas vezes ao retroceder o relogio.</param>
    /// <returns>
    ///   UTC resolvido (null apenas se invalidoPolicy = PularOcorrenciaComAuditoria) e o enum de resolucao.
    /// </returns>
    public static (DateTime? Utc, AncorarPdfDstResolucao Resolucao) ResolverLocalParaUtc(
        DateTime localDateTime,
        string timezoneId,
        AncorarPdfDstHorarioInvalidoPolicy invalidoPolicy = AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido,
        AncorarPdfDstHorarioAmbiguoPolicy ambiguoPolicy = AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo)
    {
        var tz = TryFindTimezone(timezoneId);

        // TimeZoneInfo exige DateTimeKind.Unspecified para IsInvalidTime / IsAmbiguousTime.
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        // --- Horario invalido (gap DST: ex. 02:30 nao existe durante spring-forward) ---
        if (tz.IsInvalidTime(local))
        {
            var delta = GetDstDelta(tz, local);

            return invalidoPolicy switch
            {
                AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido =>
                    (TimeZoneInfo.ConvertTimeToUtc(local.Add(delta), tz), AncorarPdfDstResolucao.InvalidoAvancado),

                AncorarPdfDstHorarioInvalidoPolicy.RecuarParaHorarioValidoAnterior =>
                    (TimeZoneInfo.ConvertTimeToUtc(local.Add(-delta), tz), AncorarPdfDstResolucao.InvalidoRecuado),

                AncorarPdfDstHorarioInvalidoPolicy.PularOcorrenciaComAuditoria =>
                    (null, AncorarPdfDstResolucao.InvalidoPulado),

                _ => (TimeZoneInfo.ConvertTimeToUtc(local.Add(delta), tz), AncorarPdfDstResolucao.InvalidoAvancado)
            };
        }

        // --- Horario ambiguo (overlap DST: ex. 01:30 existe duas vezes durante fall-back) ---
        if (tz.IsAmbiguousTime(local))
        {
            var offsets = tz.GetAmbiguousTimeOffsets(local);

            // offsets.Max() = menos negativo = maior offset UTC = horario MAIS CEDO em UTC (primeiro a ocorrer).
            // offsets.Min() = mais negativo  = menor offset UTC  = horario MAIS TARDE em UTC (segundo a ocorrer).
            var offset = ambiguoPolicy switch
            {
                AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo         => offsets.Max(),
                AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisTarde        => offsets.Min(),
                AncorarPdfDstHorarioAmbiguoPolicy.ExecutarUmaVezNoOffsetMaisCedo => offsets.Max(),
                _ => offsets.Max()
            };

            // UTC = LocalTime - Offset  (ex.: 01:30 local - (-04:00) = 05:30 UTC para EDT)
            var utcAmbiguo = DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
            return (utcAmbiguo, AncorarPdfDstResolucao.AmbiguoResolvido);
        }

        // --- Horario normal ---
        return (TimeZoneInfo.ConvertTimeToUtc(local, tz), AncorarPdfDstResolucao.Normal);
    }

    /// <summary>
    /// Calcula a proxima ocorrencia de uma tarefa recorrente a partir do vencimento atual.
    /// Aplica precisao de segundos, DST e fallback de dia mensal.
    /// </summary>
    /// <param name="vencimentoAtualUtc">UTC do vencimento que acabou de ser processado.</param>
    /// <param name="recorrencia">Tipo de recorrencia da tarefa.</param>
    /// <param name="timezoneId">Timezone da tarefa (nao da maquina).</param>
    /// <param name="agendamentoSegundo">Componente de segundos do horario (0-59).</param>
    /// <param name="diaRecorrenciaMensal">Dia preferencial para recorrencia mensal (1-31).</param>
    /// <param name="invalidoPolicy">Politica para horario invalido na proxima ocorrencia.</param>
    /// <param name="ambiguoPolicy">Politica para horario ambiguo na proxima ocorrencia.</param>
    /// <returns>UTC da proxima ocorrencia, ou null se nao houver recorrencia ou se a politica mandar pular.</returns>
    public static DateTime? CalcularProximaOcorrencia(
        DateTime vencimentoAtualUtc,
        TarefaRecorrencia recorrencia,
        string timezoneId,
        int agendamentoSegundo = 0,
        int? diaRecorrenciaMensal = null,
        AncorarPdfDstHorarioInvalidoPolicy invalidoPolicy = AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido,
        AncorarPdfDstHorarioAmbiguoPolicy ambiguoPolicy = AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo)
    {
        if (recorrencia == TarefaRecorrencia.Nenhuma)
            return null;

        var tz = TryFindTimezone(timezoneId);

        // Converte para local no timezone correto da tarefa.
        var localAtual = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(vencimentoAtualUtc, DateTimeKind.Utc), tz);

        // Aplica precisao de segundos.
        var seg = Math.Clamp(agendamentoSegundo, 0, 59);
        var localBase = new DateTime(
            localAtual.Year, localAtual.Month, localAtual.Day,
            localAtual.Hour, localAtual.Minute, seg,
            DateTimeKind.Unspecified);

        var proximoLocal = recorrencia switch
        {
            TarefaRecorrencia.Horaria => localBase.AddHours(1),
            TarefaRecorrencia.Diaria  => localBase.AddDays(1),
            TarefaRecorrencia.Semanal => localBase.AddDays(7),
            TarefaRecorrencia.Mensal  => CalcularProximoMes(localBase, diaRecorrenciaMensal),
            _ => (DateTime?)null
        };

        if (proximoLocal is null)
            return null;

        var (utc, _) = ResolverLocalParaUtc(proximoLocal.Value, timezoneId, invalidoPolicy, ambiguoPolicy);
        return utc;
    }

    /// <summary>
    /// Aplica o componente de segundos a um horario local sem alterar data/hora/minuto.
    /// Util para normalizar o AgendamentoSegundo antes da conversao para UTC.
    /// </summary>
    public static DateTime AplicarSegundos(DateTime local, int agendamentoSegundo)
    {
        var seg = Math.Clamp(agendamentoSegundo, 0, 59);
        if (local.Second == seg)
            return local;

        return new DateTime(
            local.Year, local.Month, local.Day,
            local.Hour, local.Minute, seg,
            local.Kind);
    }

    // ------------------------------------------------------------------
    // Helpers privados
    // ------------------------------------------------------------------

    /// <summary>
    /// Localiza o timezone pelo ID. Tenta IANA primeiro (Linux/macOS),
    /// depois Windows-style. Fallback silencioso para UTC para nao travar o scheduler.
    /// </summary>
    internal static TimeZoneInfo TryFindTimezone(string? timezoneId)
    {
        if (string.IsNullOrWhiteSpace(timezoneId))
            return TimeZoneInfo.Utc;

        var id = timezoneId.Trim();

        // Tentativa direta (IANA em Linux/macOS, Windows em Windows).
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz))
            return tz!;

        // Em ambiente Windows, tenta mapear IANA → Windows via BCL.
        // TimeZoneInfo.FindSystemTimeZoneById lanc,a excecao se nao encontrar.
        // O TryFindSystemTimeZoneById ja cobre isso — se chegou aqui, nao existe.
        return TimeZoneInfo.Utc;
    }

    /// <summary>
    /// Retorna o delta de horario de verao para a regra de ajuste vigente no horario informado.
    /// Usado para calcular o deslocamento correto ao resolver horario invalido (gap DST).
    /// </summary>
    private static TimeSpan GetDstDelta(TimeZoneInfo tz, DateTime localApprox)
    {
        // Percorre regras de ajuste em ordem decrescente de abrangencia para o ano.
        foreach (var rule in tz.GetAdjustmentRules().OrderByDescending(r => r.DateStart))
        {
            if (localApprox.Year >= rule.DateStart.Year && localApprox.Year <= rule.DateEnd.Year)
            {
                // DaylightDelta e sempre positivo (ex.: 01:00:00 para a maioria das zonas).
                var delta = rule.DaylightDelta;
                return delta > TimeSpan.Zero ? delta : TimeSpan.FromHours(1);
            }
        }

        // Fallback conservador de 1 hora (cobre 99% dos casos de DST no mundo).
        return TimeSpan.FromHours(1);
    }

    /// <summary>
    /// Calcula o proximo dia de vencimento para recorrencia mensal.
    /// Se o dia preferencial nao existir no mes seguinte (ex.: dia 31 em abril),
    /// usa o ultimo dia do mes — comportamento RFC 5545 / Quartz-compatible.
    /// </summary>
    private static DateTime CalcularProximoMes(DateTime localBase, int? diaRecorrenciaMensal)
    {
        var proximoMesPrimeiroDia = new DateTime(localBase.Year, localBase.Month, 1).AddMonths(1);
        var diaAlvo = Math.Clamp(diaRecorrenciaMensal ?? localBase.Day, 1, 31);
        var ultimoDia = DateTime.DaysInMonth(proximoMesPrimeiroDia.Year, proximoMesPrimeiroDia.Month);
        var dia = Math.Min(diaAlvo, ultimoDia);

        return new DateTime(
            proximoMesPrimeiroDia.Year,
            proximoMesPrimeiroDia.Month,
            dia,
            localBase.Hour,
            localBase.Minute,
            localBase.Second,
            DateTimeKind.Unspecified);
    }
}
