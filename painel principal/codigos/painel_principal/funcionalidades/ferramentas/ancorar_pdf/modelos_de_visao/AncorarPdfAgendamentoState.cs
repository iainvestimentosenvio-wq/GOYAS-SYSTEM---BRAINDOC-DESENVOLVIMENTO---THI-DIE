using System;
using Protons.Core.Tarefas.Models;

namespace Protons.UI.Painel.Funcionalidades.Ferramentas.AncorarPdf.ModelosDeVisao;

internal sealed class AncorarPdfAgendamentoState
{
    public static DateTimeOffset ToDateOnlyOffset(DateTime date) =>
        new(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero);

    public TarefaRecorrencia MapRecorrenciaParaDominio(string recorrencia)
    {
        return recorrencia switch
        {
            "Diaria" => TarefaRecorrencia.Diaria,
            "Semanal" => TarefaRecorrencia.Semanal,
            "Mensal" => TarefaRecorrencia.Mensal,
            _ => TarefaRecorrencia.Nenhuma
        };
    }

    public string MapRecorrenciaParaUi(TarefaRecorrencia recorrencia)
    {
        return recorrencia switch
        {
            TarefaRecorrencia.Diaria => "Diaria",
            TarefaRecorrencia.Semanal => "Semanal",
            TarefaRecorrencia.Mensal => "Mensal",
            _ => "Unica"
        };
    }

    public AncorarPdfModoSelecao MapModoSelecaoParaDominio(string modoSelecao)
    {
        return Enum.TryParse<AncorarPdfModoSelecao>(modoSelecao, true, out var modo)
            ? modo
            : AncorarPdfModoSelecao.RetanguloLivre;
    }

    public string MapModoSelecaoParaUi(AncorarPdfModoSelecao modoSelecao)
    {
        return modoSelecao.ToString();
    }

    public AncorarPdfDstHorarioInvalidoPolicy MapDstHorarioInvalidoPolicyParaDominio(string policy)
    {
        return Enum.TryParse<AncorarPdfDstHorarioInvalidoPolicy>(policy, true, out var result)
            ? result
            : AncorarPdfDstHorarioInvalidoPolicy.AvancarParaProximoHorarioValido;
    }

    public AncorarPdfDstHorarioAmbiguoPolicy MapDstHorarioAmbiguoPolicyParaDominio(string policy)
    {
        return Enum.TryParse<AncorarPdfDstHorarioAmbiguoPolicy>(policy, true, out var result)
            ? result
            : AncorarPdfDstHorarioAmbiguoPolicy.PreferirOffsetMaisCedo;
    }

    public string MapDstHorarioInvalidoPolicyParaUi(AncorarPdfDstHorarioInvalidoPolicy policy)
    {
        return policy.ToString();
    }

    public string MapDstHorarioAmbiguoPolicyParaUi(AncorarPdfDstHorarioAmbiguoPolicy policy)
    {
        return policy.ToString();
    }

    public string NormalizarTimezoneIdUi(string? timezoneId)
    {
        var tz = string.IsNullOrWhiteSpace(timezoneId)
            ? TimeZoneInfo.Local.Id
            : timezoneId.Trim();

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(tz);
            return tz;
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException("Timezone inválida para este sistema.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException("Timezone inválida para este sistema.");
        }
    }

    public DateTime ConverterUtcParaTimezone(DateTime utc, string timezoneId)
    {
        var utcKind = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timezoneId) ? TimeZoneInfo.Local.Id : timezoneId.Trim());
            return TimeZoneInfo.ConvertTimeFromUtc(utcKind, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utcKind, TimeZoneInfo.Local);
        }
    }
}
