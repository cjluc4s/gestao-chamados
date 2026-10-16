using gestao_chamados.Data;
using gestao_chamados.Models;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Services;

/// <summary>
/// Executa as regras de automação. Roda no mesmo <see cref="ApplicationDbContext"/> da requisição
/// (ou do monitor), então as mudanças que faz no chamado são gravadas junto.
///
/// Salvaguardas:
/// • o que a automação muda <b>não</b> dispara outras regras (sem laços);
/// • regras de inatividade agem uma vez por "período parado": só voltam a agir depois que alguém mexer no chamado;
/// • as regras rodam em ordem de criação, cada uma vendo o chamado já alterado pelas anteriores.
/// </summary>
public sealed class AutomationEngine
{
    private readonly ApplicationDbContext _db;
    private readonly SlaCalculator _sla;
    private readonly TicketNotifier _notifier;
    private readonly ILogger<AutomationEngine> _logger;

    public AutomationEngine(ApplicationDbContext db, SlaCalculator sla, TicketNotifier notifier, ILogger<AutomationEngine> logger)
    {
        _db = db;
        _sla = sla;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>Avalia as regras de um gatilho para um chamado já carregado (rastreado pelo contexto).</summary>
    public async Task<IReadOnlyList<string>> RunAsync(
        AutomationTrigger trigger, Ticket ticket, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        var rules = await _db.AutomationRules.AsNoTracking()
            .Where(r => r.IsEnabled && r.Trigger == trigger)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return [];
        }

        var executed = new List<string>();
        var notifications = new List<(string?[] Groups, string Message)>();
        var changed = false;

        foreach (var rule in rules)
        {
            if (!RuleMatcher.Matches(rule, Snapshot(ticket), now))
            {
                continue;
            }

            if (trigger == AutomationTrigger.Inactivity && await AlreadyRanSinceLastActivityAsync(rule.Id, ticket, ct))
            {
                continue;
            }

            var outcome = await ApplyAsync(rule, ticket, now, ct);
            if (outcome is null)
            {
                continue;
            }

            changed |= outcome.ChangedTicket;
            notifications.AddRange(outcome.Notifications);
            executed.Add($"{rule.Name}: {outcome.Description}");

            _db.AutomationLogs.Add(new AutomationLog
            {
                RuleId = rule.Id,
                TicketId = ticket.Id,
                ExecutedAtUtc = now,
                Description = Truncate(outcome.Description, 300)
            });
        }

        if (executed.Count == 0)
        {
            return [];
        }

        if (changed)
        {
            ticket.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        foreach (var (groups, message) in notifications)
        {
            await _notifier.NotifyOthersAsync(null, message, groups);
        }

        await _notifier.TicketChangedAsync(ticket.Id, "automation", null, ticket.CreatedByUserId, ticket.AssignedToUserId, null, assignmentChanged: true);

        _logger.LogInformation("Automação no chamado #{TicketId}: {Executed}", ticket.Id, string.Join("; ", executed));
        return executed;
    }

    /// <summary>Carrega o chamado e avalia as regras (usado pelo monitor de SLA).</summary>
    public async Task<IReadOnlyList<string>> RunForTicketAsync(
        AutomationTrigger trigger, int ticketId, DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == ticketId, ct);
        return ticket is null ? [] : await RunAsync(trigger, ticket, nowUtc, ct);
    }

    /// <summary>Varre os chamados parados e aplica as regras de inatividade. Devolve quantas ações foram feitas.</summary>
    public async Task<int> RunInactivityAsync(DateTime? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;

        var rules = await _db.AutomationRules.AsNoTracking()
            .Where(r => r.IsEnabled && r.Trigger == AutomationTrigger.Inactivity && r.ConditionInactiveHours != null)
            .ToListAsync(ct);

        if (rules.Count == 0)
        {
            return 0;
        }

        // Só vale a pena olhar chamados parados há pelo menos o menor prazo configurado.
        var shortest = TimeSpan.FromHours(rules.Min(r => r.ConditionInactiveHours!.Value));
        var cutoff = now - shortest;

        var candidateIds = await _db.Tickets.AsNoTracking()
            .Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Cancelled
                && (t.UpdatedAtUtc ?? t.CreatedAtUtc) <= cutoff)
            .Select(t => t.Id)
            .ToListAsync(ct);

        var actions = 0;
        foreach (var id in candidateIds)
        {
            var ticket = await _db.Tickets.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (ticket is null)
            {
                continue;
            }

            actions += (await RunAsync(AutomationTrigger.Inactivity, ticket, now, ct)).Count;
            _db.ChangeTracker.Clear(); // não acumula centenas de chamados na memória
        }

        return actions;
    }

    private static TicketSnapshot Snapshot(Ticket t) =>
        new(t.Id, t.Title, t.Description, t.CategoryId, t.Priority, t.Status, t.UpdatedAtUtc ?? t.CreatedAtUtc, t.AssignedToUserId);

    private async Task<bool> AlreadyRanSinceLastActivityAsync(int ruleId, Ticket ticket, CancellationToken ct)
    {
        var lastActivity = ticket.UpdatedAtUtc ?? ticket.CreatedAtUtc;
        return await _db.AutomationLogs.AnyAsync(l => l.RuleId == ruleId && l.TicketId == ticket.Id && l.ExecutedAtUtc >= lastActivity, ct);
    }

    private sealed record Outcome(string Description, bool ChangedTicket, List<(string?[] Groups, string Message)> Notifications);

    /// <summary>Aplica a ação da regra. Devolve null quando não havia nada a fazer (já estava como a regra queria, ação inválida...).</summary>
    private async Task<Outcome?> ApplyAsync(AutomationRule rule, Ticket ticket, DateTime now, CancellationToken ct)
    {
        var notifications = new List<(string?[] Groups, string Message)>();

        switch (rule.ActionType)
        {
            case AutomationActionType.AssignToAgent:
            {
                if (TicketWorkflowPolicy.IsFinal(ticket.Status)
                    || rule.ActionAgentId is null
                    || ticket.AssignedToUserId == rule.ActionAgentId
                    || !await IsAgentAsync(rule.ActionAgentId, ct))
                {
                    return null;
                }

                var agentName = await _db.Users.AsNoTracking().Where(u => u.Id == rule.ActionAgentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "atendente";
                ticket.AssignedToUserId = rule.ActionAgentId;
                notifications.Add(([rule.ActionAgentId], $"A regra \"{rule.Name}\" atribuiu o chamado #{ticket.Id} a você: {ticket.Title}"));

                return new Outcome($"atribuído a {agentName}", true, notifications);
            }

            case AutomationActionType.SetPriority:
            {
                if (rule.ActionPriority is not { } newPriority || newPriority == ticket.Priority || !Enum.IsDefined(newPriority))
                {
                    return null;
                }

                var old = ticket.Priority;
                ticket.Priority = newPriority;
                _sla.RecalculateDue(ticket, now);

                return new Outcome($"prioridade alterada de {old.Label()} para {newPriority.Label()}", true, notifications);
            }

            case AutomationActionType.ChangeStatus:
            {
                if (rule.ActionStatus is not { } newStatus || !AutomationPolicy.CanApplyStatus(ticket.Status, newStatus))
                {
                    return null;
                }

                var old = ticket.Status;
                ticket.Status = newStatus;
                TicketLifecycle.OnStatusChanged(_sla, ticket, old, newStatus, now);

                _db.TicketStatusHistory.Add(new TicketStatusHistory
                {
                    TicketId = ticket.Id,
                    FromStatus = old,
                    ToStatus = newStatus,
                    ChangedByUserId = AutomationUser.Id,
                    ChangedAtUtc = now
                });

                notifications.Add(([ticket.CreatedByUserId],
                    $"Chamado #{ticket.Id}: o status foi alterado automaticamente para \"{newStatus.Label()}\"."));

                return new Outcome($"status alterado de \"{old.Label()}\" para \"{newStatus.Label()}\"", true, notifications);
            }

            case AutomationActionType.NotifyAdmins:
                notifications.Add((["Admins"], Message(rule, ticket, $"Regra \"{rule.Name}\": chamado #{{id}} — {{titulo}}")));
                return new Outcome("administradores avisados", false, notifications);

            case AutomationActionType.NotifyTeam:
                notifications.Add(([ticket.AssignedToUserId ?? "Agents"], Message(rule, ticket, $"Regra \"{rule.Name}\": chamado #{{id}} — {{titulo}}")));
                return new Outcome(ticket.AssignedToUserId is null ? "atendentes avisados" : "responsável avisado", false, notifications);

            case AutomationActionType.NotifyRequester:
                notifications.Add(([ticket.CreatedByUserId], Message(rule, ticket, "Novidade no seu chamado #{id}: {titulo}")));
                return new Outcome("solicitante avisado", false, notifications);

            default:
                return null;
        }
    }

    private static string Message(AutomationRule rule, Ticket ticket, string defaultTemplate) =>
        AutomationPolicy.FormatMessage(rule.ActionMessage, defaultTemplate, ticket.Id, ticket.Title);

    private Task<bool> IsAgentAsync(string userId, CancellationToken ct) =>
        _db.UserRoles.AnyAsync(ur => ur.UserId == userId
            && _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == RoleNames.Agent), ct);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
