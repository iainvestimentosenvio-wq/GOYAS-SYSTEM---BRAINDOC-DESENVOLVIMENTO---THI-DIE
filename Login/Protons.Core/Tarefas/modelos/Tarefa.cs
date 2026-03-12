namespace Protons.Core.Tarefas.Models;

public sealed class Tarefa
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public string FerramentaId { get; set; } = "generica";
    public string Titulo { get; set; } = string.Empty;
    public DateTime VencimentoUtc { get; set; }
    public int ResponsavelUserId { get; set; }
    public TarefaStatus Status { get; set; } = TarefaStatus.Agendada;
    public TarefaRecorrencia Recorrencia { get; set; } = TarefaRecorrencia.Nenhuma;
    public int? DiaRecorrenciaMensal { get; set; }
    public int? EsteiraId { get; set; }
    public TimeOnly? HorarioRecorrencia { get; set; }
    public DayOfWeek? DiaSemanaRecorrencia { get; set; }
    public bool Ativa { get; set; } = true;
    public int CriadoPorUserId { get; set; }
    public DateTime CriadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEmUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ConcluidaEmUtc { get; set; }
}
