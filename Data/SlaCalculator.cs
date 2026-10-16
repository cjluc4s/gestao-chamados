using gestao_chamados.Models;
using Microsoft.Extensions.Options;

namespace gestao_chamados.Data;

public enum SlaState
{
    /// <summary>Dentro do prazo, com folga.</summary>
    Ok,
    /// <summary>Consumiu o percentual de alerta (padrão 80%) e ainda não venceu.</summary>
    AtRisk,
    /// <summary>Prazo estourado e o chamado ainda precisa de atendimento.</summary>
    Breached,
    /// <summary>Relógio pausado: aguardando o solicitante.</summary>
    Paused,
    /// <summary>Resolvido, fechado ou cancelado: o prazo não corre mais (ver <see cref="SlaSnapshot.Breached"/>).</summary>
    Done
}

/// <param name="Breached">Prazo foi (ou está) estourado — vale também para chamados já encerrados.</param>
/// <param name="Consumed">Fração do prazo consumida (1.0 = 100%; pode passar disso).</param>
/// <param name="Remaining">Tempo restante (útil, quando aplicável); zero se vencido.</param>
/// <param name="Overdue">Tempo de atraso (útil, quando aplicável); zero se dentro do prazo.</param>
public sealed record SlaSnapshot(SlaState State, bool Breached, double Consumed, TimeSpan Remaining, TimeSpan Overdue);

/// <summary>
/// Cálculo de SLA com horário comercial e relógio pausável. Sem banco: recebe e altera apenas o
/// próprio <see cref="Ticket"/>. Registrado como singleton com a seção "Sla" da configuração.
///
/// Modelo: o tempo "ativo" de um chamado é o tempo (útil, quando aplicável) desde a abertura,
/// menos o tempo em que o relógio ficou parado (<see cref="Ticket.SlaPausedSeconds"/>).
/// O limite é sempre <c>abertura + orçamento da prioridade + tempo parado</c>, em tempo útil —
/// por isso pode ser recalculado do zero a qualquer momento (troca de prioridade, retomada).
/// </summary>
public sealed class SlaCalculator
{
    private readonly BusinessCalendar? _calendar;

    public SlaCalculator(IOptions<SlaOptions> options) : this(options.Value)
    {
    }

    public SlaCalculator(SlaOptions options)
    {
        Options = options;
        WarnAt = Math.Clamp(options.WarnAtPercent, 1, 100) / 100.0;
        _calendar = options.BusinessHours.Enabled ? new BusinessCalendar(options.BusinessHours) : null;
    }

    public SlaOptions Options { get; }

    /// <summary>Fração do prazo (0–1) a partir da qual o chamado entra "em risco".</summary>
    public double WarnAt { get; }

    /// <summary>Calendário de horário comercial, ou null quando o SLA corre 24x7.</summary>
    public BusinessCalendar? Calendar => _calendar;

    public bool UsesBusinessHours(TicketPriority priority) =>
        _calendar is not null && !Options.AlwaysOnPriorities.Contains(priority);

    public TimeSpan Budget(TicketPriority priority) => SlaPolicy.GetDeadline(priority);

    /// <summary>Soma tempo (útil ou corrido, conforme a prioridade) a uma data.</summary>
    public DateTime Add(DateTime fromUtc, TicketPriority priority, TimeSpan duration) =>
        UsesBusinessHours(priority) ? _calendar!.AddBusinessTime(fromUtc, duration) : fromUtc.Add(duration);

    /// <summary>Tempo (útil ou corrido, conforme a prioridade) entre duas datas.</summary>
    public TimeSpan Between(DateTime fromUtc, DateTime toUtc, TicketPriority priority)
    {
        if (toUtc <= fromUtc)
        {
            return TimeSpan.Zero;
        }

        return UsesBusinessHours(priority) ? _calendar!.BusinessTimeBetween(fromUtc, toUtc) : toUtc - fromUtc;
    }

    /// <summary>Limite do SLA para um chamado aberto em <paramref name="createdUtc"/>.</summary>
    public DateTime ComputeDue(DateTime createdUtc, TicketPriority priority, TimeSpan paused) =>
        Add(createdUtc, priority, Budget(priority) + paused);

    /// <summary>Inicializa o SLA de um chamado novo.</summary>
    public void Start(Ticket ticket)
    {
        ticket.SlaPausedAtUtc = null;
        ticket.SlaPausedSeconds = 0;
        ticket.SlaWarnedAtUtc = null;
        ticket.SlaEscalatedAtUtc = null;
        ticket.DueAtUtc = ComputeDue(ticket.CreatedAtUtc, ticket.Priority, TimeSpan.Zero);
    }

    /// <summary>
    /// Deve ser chamado sempre que o status muda (antes ou depois de gravar o novo status, desde que
    /// <paramref name="from"/> e <paramref name="to"/> sejam informados). Para e retoma o relógio.
    /// </summary>
    public void OnStatusChanged(Ticket ticket, TicketStatus from, TicketStatus to, DateTime nowUtc)
    {
        var wasStopped = SlaPolicy.IsClockStopped(from);
        var willStop = SlaPolicy.IsClockStopped(to);

        if (!wasStopped && willStop)
        {
            ticket.SlaPausedAtUtc = nowUtc;
        }
        else if (wasStopped && !willStop)
        {
            if (ticket.SlaPausedAtUtc is { } pausedAt)
            {
                ticket.SlaPausedSeconds += (long)Between(pausedAt, nowUtc, ticket.Priority).TotalSeconds;
            }

            ticket.SlaPausedAtUtc = null;
            RecalculateDue(ticket, nowUtc);
        }
    }

    /// <summary>
    /// Recalcula o limite (ex.: depois de trocar a prioridade). Se o chamado voltou a estar
    /// dentro do prazo, zera os avisos para que possam disparar de novo.
    /// </summary>
    public void RecalculateDue(Ticket ticket, DateTime nowUtc)
    {
        ticket.DueAtUtc = ComputeDue(ticket.CreatedAtUtc, ticket.Priority, TimeSpan.FromSeconds(ticket.SlaPausedSeconds));

        var snapshot = Evaluate(ticket, nowUtc);
        if (!snapshot.Breached)
        {
            ticket.SlaEscalatedAtUtc = null;
        }

        if (snapshot.Consumed < WarnAt)
        {
            ticket.SlaWarnedAtUtc = null;
        }
    }

    public SlaSnapshot Evaluate(Ticket ticket, DateTime nowUtc)
    {
        var effectiveNow = ticket.SlaPausedAtUtc ?? nowUtc;
        var budget = Budget(ticket.Priority);
        var active = Between(ticket.CreatedAtUtc, effectiveNow, ticket.Priority) - TimeSpan.FromSeconds(ticket.SlaPausedSeconds);
        if (active < TimeSpan.Zero)
        {
            active = TimeSpan.Zero;
        }

        var consumed = active.TotalSeconds / budget.TotalSeconds;
        var due = ticket.DueAtUtc ?? ComputeDue(ticket.CreatedAtUtc, ticket.Priority, TimeSpan.FromSeconds(ticket.SlaPausedSeconds));
        var breached = due < effectiveNow;

        var remaining = breached ? TimeSpan.Zero : Between(effectiveNow, due, ticket.Priority);
        var overdue = breached ? Between(due, effectiveNow, ticket.Priority) : TimeSpan.Zero;

        SlaState state;
        if (SlaPolicy.IsDone(ticket.Status))
        {
            state = SlaState.Done;
        }
        else if (ticket.Status == TicketStatus.WaitingUser)
        {
            state = SlaState.Paused;
        }
        else if (breached)
        {
            state = SlaState.Breached;
        }
        else if (consumed >= WarnAt)
        {
            state = SlaState.AtRisk;
        }
        else
        {
            state = SlaState.Ok;
        }

        return new SlaSnapshot(state, breached, consumed, remaining, overdue);
    }
}
