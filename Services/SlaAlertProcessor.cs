using gestao_chamados.Data;
using gestao_chamados.Models;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Services;

public enum SlaAlertKind
{
    /// <summary>Consumiu o percentual de alerta (padrão 80%).</summary>
    Warning,
    /// <summary>Prazo estourado: o chamado foi escalonado.</summary>
    Breach
}

public sealed record SlaAlert(
    SlaAlertKind Kind,
    int TicketId,
    string Title,
    string CreatedByUserId,
    string? AssignedToUserId);

/// <summary>
/// Varre os chamados com o relógio correndo, marca os que entraram "em risco" ou venceram
/// e devolve os alertas a enviar. Cada alerta dispara no máximo uma vez por prazo.
/// Separado do serviço em segundo plano para poder ser testado só com um banco.
/// </summary>
public static class SlaAlertProcessor
{
    public static async Task<IReadOnlyList<SlaAlert>> ProcessAsync(
        ApplicationDbContext db, SlaCalculator sla, DateTime nowUtc, CancellationToken ct = default)
    {
        var candidates = await db.Tickets
            .Where(t => t.SlaPausedAtUtc == null
                && t.DueAtUtc != null
                && (t.SlaWarnedAtUtc == null || t.SlaEscalatedAtUtc == null)
                && !SlaPolicy.ClockStoppedStatuses.Contains(t.Status))
            .ToListAsync(ct);

        var alerts = new List<SlaAlert>();
        foreach (var ticket in candidates)
        {
            var snapshot = sla.Evaluate(ticket, nowUtc);

            if (snapshot.Breached)
            {
                if (ticket.SlaEscalatedAtUtc is null)
                {
                    ticket.SlaEscalatedAtUtc = nowUtc;
                    ticket.SlaWarnedAtUtc ??= nowUtc;
                    alerts.Add(ToAlert(SlaAlertKind.Breach, ticket));
                }
            }
            else if (ticket.SlaWarnedAtUtc is null && snapshot.Consumed >= sla.WarnAt)
            {
                ticket.SlaWarnedAtUtc = nowUtc;
                alerts.Add(ToAlert(SlaAlertKind.Warning, ticket));
            }
        }

        if (alerts.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return alerts;
    }

    private static SlaAlert ToAlert(SlaAlertKind kind, Ticket t) =>
        new(kind, t.Id, t.Title, t.CreatedByUserId, t.AssignedToUserId);
}
