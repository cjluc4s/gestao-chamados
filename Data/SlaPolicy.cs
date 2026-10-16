using gestao_chamados.Models;

namespace gestao_chamados.Data;

/// <summary>
/// Regras de SLA que não dependem de configuração. As que dependem do calendário
/// (horário comercial) ficam em <see cref="SlaCalculator"/>.
/// </summary>
public static class SlaPolicy
{
    /// <summary>
    /// Status em que o relógio do SLA está parado: a espera do solicitante não conta contra
    /// o atendimento, e chamado resolvido/fechado/cancelado não tem mais prazo correndo.
    /// Array (e não método) para poder ser usado também dentro de consultas do EF.
    /// </summary>
    public static readonly TicketStatus[] ClockStoppedStatuses =
        [TicketStatus.WaitingUser, TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled];

    /// <summary>Chamados que não pedem mais ação do atendimento (não entram em "vencido" nem "em risco").</summary>
    public static readonly TicketStatus[] DoneStatuses =
        [TicketStatus.Resolved, TicketStatus.Closed, TicketStatus.Cancelled];

    public static bool IsClockStopped(TicketStatus status) => ClockStoppedStatuses.Contains(status);

    public static bool IsDone(TicketStatus status) => DoneStatuses.Contains(status);

    public static TimeSpan GetDeadline(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Critical => TimeSpan.FromHours(2),
            TicketPriority.High => TimeSpan.FromHours(4),
            TicketPriority.Medium => TimeSpan.FromHours(12),
            _ => TimeSpan.FromHours(24)
        };
    }

    /// <summary>
    /// Vencido = o limite ficou para trás do "agora efetivo". Com o relógio parado, o agora
    /// efetivo é o instante em que ele parou: assim um chamado já atrasado antes de pausar
    /// continua atrasado, e um resolvido a tempo continua "no prazo" para sempre.
    /// Em consultas EF a mesma conta é <c>t.DueAtUtc &lt; (t.SlaPausedAtUtc ?? agora)</c>.
    /// </summary>
    public static bool IsBreached(DateTime? dueAtUtc, DateTime? pausedAtUtc, DateTime nowUtc) =>
        dueAtUtc.HasValue && dueAtUtc.Value < (pausedAtUtc ?? nowUtc);
}
