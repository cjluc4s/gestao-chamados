using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace gestao_chamados.Tests;

public class RuleMatcherTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static TicketSnapshot Ticket(
        string title = "Servidor fora do ar",
        string description = "Ninguém consegue acessar",
        int categoryId = 1,
        TicketPriority priority = TicketPriority.Medium,
        TicketStatus status = TicketStatus.Open,
        DateTime? lastActivity = null) =>
        new(1, title, description, categoryId, priority, status, lastActivity ?? Now.AddHours(-1), null);

    private static AutomationRule Rule(AutomationTrigger trigger = AutomationTrigger.TicketCreated) =>
        new() { Name = "r", Trigger = trigger, ActionType = AutomationActionType.NotifyAdmins };

    [Fact]
    public void RuleWithoutConditions_MatchesAnyTicket()
    {
        Assert.True(RuleMatcher.Matches(Rule(), Ticket(), Now));
    }

    [Fact]
    public void DisabledRule_NeverMatches()
    {
        var rule = Rule();
        rule.IsEnabled = false;

        Assert.False(RuleMatcher.Matches(rule, Ticket(), Now));
    }

    [Fact]
    public void AllConditionsMustMatch()
    {
        var rule = Rule();
        rule.ConditionCategoryId = 1;
        rule.ConditionPriority = TicketPriority.Medium;
        rule.ConditionStatus = TicketStatus.Open;

        Assert.True(RuleMatcher.Matches(rule, Ticket(), Now));
        Assert.False(RuleMatcher.Matches(rule, Ticket(categoryId: 2), Now));
        Assert.False(RuleMatcher.Matches(rule, Ticket(priority: TicketPriority.High), Now));
        Assert.False(RuleMatcher.Matches(rule, Ticket(status: TicketStatus.InProgress), Now));
    }

    [Theory]
    [InlineData("fora do ar", true)]
    [InlineData("FORA DO AR", true)]
    [InlineData("urgente, fora do ar", true)]       // basta uma
    [InlineData("urgente, parado", false)]
    [InlineData("acessar", true)]                   // vale também para a descrição
    [InlineData("  ,  ", true)]                     // só separadores = sem filtro
    [InlineData(null, true)]
    public void Keywords_MatchTitleOrDescriptionIgnoringCase(string? keywords, bool expected)
    {
        var rule = Rule();
        rule.ConditionKeywords = keywords;

        Assert.Equal(expected, RuleMatcher.Matches(rule, Ticket(), Now));
    }

    [Fact]
    public void Keywords_IgnoreAccents()
    {
        var rule = Rule();
        rule.ConditionKeywords = "conexao, impressora";

        Assert.True(RuleMatcher.Matches(rule, Ticket(title: "Sem conexão com a rede"), Now));
        Assert.False(RuleMatcher.Matches(rule, Ticket(title: "Sem acesso"), Now));
    }

    [Fact]
    public void Inactivity_NeedsTheConfiguredHoursWithoutActivity()
    {
        var rule = Rule(AutomationTrigger.Inactivity);
        rule.ConditionInactiveHours = 72;

        Assert.False(RuleMatcher.Matches(rule, Ticket(lastActivity: Now.AddHours(-71)), Now));
        Assert.True(RuleMatcher.Matches(rule, Ticket(lastActivity: Now.AddHours(-72)), Now));
        Assert.True(RuleMatcher.Matches(rule, Ticket(lastActivity: Now.AddDays(-10)), Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    public void Inactivity_WithoutValidHoursNeverMatches(int? hours)
    {
        var rule = Rule(AutomationTrigger.Inactivity);
        rule.ConditionInactiveHours = hours;

        Assert.False(RuleMatcher.Matches(rule, Ticket(lastActivity: Now.AddYears(-1)), Now));
    }
}

public class AutomationRuleValidatorTests
{
    private static AutomationRule Valid() => new()
    {
        Name = "Regra",
        Trigger = AutomationTrigger.TicketCreated,
        ActionType = AutomationActionType.NotifyAdmins
    };

    private static List<string> Fields(AutomationRule rule) =>
        AutomationRuleValidator.Validate(rule).Select(e => e.Field).ToList();

    [Fact]
    public void ValidRule_HasNoErrors()
    {
        Assert.Empty(AutomationRuleValidator.Validate(Valid()));
    }

    [Fact]
    public void Name_IsRequired()
    {
        var rule = Valid();
        rule.Name = "   ";

        Assert.Contains("Name", Fields(rule));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(24 * 365, true)]
    [InlineData(24 * 365 + 1, false)]
    public void Inactivity_RequiresHoursInRange(int? hours, bool ok)
    {
        var rule = Valid();
        rule.Trigger = AutomationTrigger.Inactivity;
        rule.ConditionInactiveHours = hours;

        Assert.Equal(ok, !Fields(rule).Contains("ConditionInactiveHours"));
    }

    [Fact]
    public void AssignToAgent_RequiresAnAgent()
    {
        var rule = Valid();
        rule.ActionType = AutomationActionType.AssignToAgent;

        Assert.Contains("ActionAgentId", Fields(rule));

        rule.ActionAgentId = "agent-1";
        Assert.Empty(Fields(rule));
    }

    [Fact]
    public void SetPriority_RequiresAPriorityDifferentFromTheCondition()
    {
        var rule = Valid();
        rule.ActionType = AutomationActionType.SetPriority;
        Assert.Contains("ActionPriority", Fields(rule));

        rule.ActionPriority = TicketPriority.High;
        rule.ConditionPriority = TicketPriority.High;
        Assert.Contains("ActionPriority", Fields(rule)); // não mudaria nada

        rule.ConditionPriority = TicketPriority.Low;
        Assert.Empty(Fields(rule));
    }

    [Fact]
    public void ChangeStatus_CannotTargetOpenOrTheSameStatusAsTheCondition()
    {
        var rule = Valid();
        rule.ActionType = AutomationActionType.ChangeStatus;
        Assert.Contains("ActionStatus", Fields(rule));

        rule.ActionStatus = TicketStatus.Open;
        Assert.Contains("ActionStatus", Fields(rule));

        rule.ActionStatus = TicketStatus.Closed;
        rule.ConditionStatus = TicketStatus.Closed;
        Assert.Contains("ActionStatus", Fields(rule));

        rule.ConditionStatus = TicketStatus.Resolved;
        Assert.Empty(Fields(rule));
    }

    [Fact]
    public void Normalize_ClearsFieldsThatDoNotApply()
    {
        var rule = Valid();
        rule.Name = "  Regra  ";
        rule.Trigger = AutomationTrigger.TicketCreated;
        rule.ConditionInactiveHours = 48;
        rule.ConditionKeywords = " fora do ar ,, urgente ";
        rule.ActionType = AutomationActionType.NotifyAdmins;
        rule.ActionAgentId = "x";
        rule.ActionPriority = TicketPriority.High;
        rule.ActionStatus = TicketStatus.Closed;
        rule.ActionMessage = "  aviso ";

        AutomationRuleValidator.Normalize(rule);

        Assert.Equal("Regra", rule.Name);
        Assert.Null(rule.ConditionInactiveHours);
        Assert.Equal("fora do ar, urgente", rule.ConditionKeywords);
        Assert.Null(rule.ActionAgentId);
        Assert.Null(rule.ActionPriority);
        Assert.Null(rule.ActionStatus);
        Assert.Equal("aviso", rule.ActionMessage);
    }
}

public class AutomationPolicyTests
{
    [Theory]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed, true)]
    [InlineData(TicketStatus.WaitingUser, TicketStatus.Closed, true)]
    [InlineData(TicketStatus.WaitingUser, TicketStatus.Cancelled, true)]   // diferente do atendente: automação pode cancelar
    [InlineData(TicketStatus.Open, TicketStatus.Cancelled, true)]
    [InlineData(TicketStatus.Closed, TicketStatus.Cancelled, false)]       // já encerrado
    [InlineData(TicketStatus.Cancelled, TicketStatus.InProgress, false)]   // só um Admin reabre
    [InlineData(TicketStatus.Closed, TicketStatus.InProgress, false)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Open, false)]
    [InlineData(TicketStatus.Open, TicketStatus.Open, false)]
    public void CanApplyStatus_FollowsTheWorkflow(TicketStatus from, TicketStatus to, bool expected)
    {
        Assert.Equal(expected, AutomationPolicy.CanApplyStatus(from, to));
    }

    [Fact]
    public void FormatMessage_ReplacesPlaceholdersAndFallsBackToTheDefault()
    {
        Assert.Equal("Chamado #7: Impressora", AutomationPolicy.FormatMessage("Chamado #{id}: {titulo}", "padrão", 7, "Impressora"));
        Assert.Equal("padrão 7", AutomationPolicy.FormatMessage("  ", "padrão {ID}", 7, "x"));
    }
}

public class AutomationDescriberTests
{
    [Fact]
    public void Describe_BuildsAReadableSentence()
    {
        var rule = new AutomationRule
        {
            Name = "r",
            Trigger = AutomationTrigger.TicketCreated,
            ConditionCategoryId = 2,
            ConditionPriority = TicketPriority.High,
            ConditionKeywords = "vpn, rede",
            ActionType = AutomationActionType.AssignToAgent
        };

        Assert.Equal(
            "Quando um chamado for criado, se a categoria for \"Acesso\" e a prioridade for Alta e o texto citar \"vpn\" ou \"rede\", então atribuir a Suporte 01.",
            AutomationDescriber.Describe(rule, "Acesso", "Suporte 01"));
    }

    [Fact]
    public void Describe_InactivityShowsDaysForWholeDays()
    {
        var rule = new AutomationRule
        {
            Name = "r",
            Trigger = AutomationTrigger.Inactivity,
            ConditionInactiveHours = 72,
            ConditionStatus = TicketStatus.Resolved,
            ActionType = AutomationActionType.ChangeStatus,
            ActionStatus = TicketStatus.Closed
        };

        Assert.Equal(
            "Quando um chamado ficar parado por 3 dias, se o status for \"Resolvido\", então mudar o status para \"Fechado\".",
            AutomationDescriber.Describe(rule, null, null));
    }

    [Fact]
    public void Describe_StatusChangedPutsTheStatusInTheTrigger()
    {
        var rule = new AutomationRule
        {
            Name = "r",
            Trigger = AutomationTrigger.StatusChanged,
            ConditionStatus = TicketStatus.Resolved,
            ActionType = AutomationActionType.NotifyRequester
        };

        Assert.Equal("Quando o status mudar para \"Resolvido\", então avisar o solicitante.", AutomationDescriber.Describe(rule, null, null));
    }

    [Fact]
    public void EveryTemplate_IsAValidRule()
    {
        foreach (var template in AutomationTemplates.All)
        {
            var form = AutomationTemplates.Create(template.Key);
            Assert.NotNull(form);

            var rule = new AutomationRule();
            form!.ApplyTo(rule);
            AutomationRuleValidator.Normalize(rule);

            Assert.Empty(AutomationRuleValidator.Validate(rule));
        }
    }
}

public class AutomationEngineTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestDatabase _db = new();
    private readonly RecordingNotifier _notifier = new();

    // Sem horário comercial: relógio corrido, contas simples.
    private static readonly SlaCalculator Sla = new(new SlaOptions { BusinessHours = new BusinessHoursOptions { Enabled = false } });

    public void Dispose() => _db.Dispose();

    private sealed class RecordingNotifier : TicketNotifier
    {
        public RecordingNotifier() : base(null!)
        {
        }

        public List<(string Group, string Message)> Sent { get; } = [];
        public List<string> Changed { get; } = [];

        public override Task NotifyAsync(string group, string message)
        {
            Sent.Add((group, message));
            return Task.CompletedTask;
        }

        public override Task TicketChangedAsync(int ticketId, string kind, string? byUserId, string createdByUserId,
            string? assignedToUserId, string? previousAssigneeId = null, bool assignmentChanged = false)
        {
            Changed.Add(kind);
            return Task.CompletedTask;
        }
    }

    private int AddRule(Action<AutomationRule> configure)
    {
        var rule = new AutomationRule { Name = "Regra de teste", Trigger = AutomationTrigger.TicketCreated, ActionType = AutomationActionType.NotifyAdmins };
        configure(rule);

        using var context = _db.CreateContext();
        context.AutomationRules.Add(rule);
        context.SaveChanges();
        return rule.Id;
    }

    private AutomationEngine Engine(ApplicationDbContext context) =>
        new(context, Sla, _notifier, NullLogger<AutomationEngine>.Instance);

    [Fact]
    public async Task AssignRule_AssignsMatchingTicketsAndNotifiesTheAgent()
    {
        AddRule(r =>
        {
            r.ConditionCategoryId = 2;
            r.ActionType = AutomationActionType.AssignToAgent;
            r.ActionAgentId = TestDatabase.AgentId;
        });
        var created = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddMinutes(-1), categoryId: 2);
        var other = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddMinutes(-1), categoryId: 1);

        await using var context = _db.CreateContext();
        var engine = Engine(context);

        var done = await engine.RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == created.Id), Now);
        var skipped = await engine.RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == other.Id), Now);

        Assert.Single(done);
        Assert.Empty(skipped);

        await using var check = _db.CreateContext();
        Assert.Equal(TestDatabase.AgentId, (await check.Tickets.FindAsync(created.Id))!.AssignedToUserId);
        Assert.Null((await check.Tickets.FindAsync(other.Id))!.AssignedToUserId);
        Assert.Single(check.AutomationLogs);
        Assert.Contains(_notifier.Sent, n => n.Group == TestDatabase.AgentId);
        Assert.Contains("automation", _notifier.Changed);
    }

    [Fact]
    public async Task AssignRule_IgnoresUsersWhoAreNotAgents()
    {
        AddRule(r =>
        {
            r.ActionType = AutomationActionType.AssignToAgent;
            r.ActionAgentId = TestDatabase.RequesterId; // existe, mas não é Atendente
        });
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddMinutes(-1));

        await using var context = _db.CreateContext();
        var done = await Engine(context).RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);

        Assert.Empty(done);
        Assert.Empty(context.AutomationLogs);
    }

    [Fact]
    public async Task SetPriorityRule_RaisesPriorityAndRecalculatesTheDeadline()
    {
        AddRule(r =>
        {
            r.ConditionKeywords = "fora do ar";
            r.ActionType = AutomationActionType.SetPriority;
            r.ActionPriority = TicketPriority.Critical;
        });
        var ticket = _db.AddTicket(Sla, TicketPriority.Low, Now.AddMinutes(-1), title: "Sistema fora do ar");

        await using (var context = _db.CreateContext())
        {
            await Engine(context).RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);
        }

        await using var check = _db.CreateContext();
        var stored = await check.Tickets.FindAsync(ticket.Id);
        Assert.Equal(TicketPriority.Critical, stored!.Priority);
        Assert.Equal(stored.CreatedAtUtc.AddHours(2), stored.DueAtUtc); // Crítica = 2 h contadas da abertura
    }

    [Fact]
    public async Task ChangeStatusRule_RecordsTheChangeInTheHistoryAsAutomation()
    {
        AddRule(r =>
        {
            r.Trigger = AutomationTrigger.StatusChanged;
            r.ConditionStatus = TicketStatus.Resolved;
            r.ActionType = AutomationActionType.ChangeStatus;
            r.ActionStatus = TicketStatus.Closed;
        });
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddHours(-5), TicketStatus.Resolved);

        await using (var context = _db.CreateContext())
        {
            await Engine(context).RunAsync(AutomationTrigger.StatusChanged, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);
        }

        await using var check = _db.CreateContext();
        var stored = await check.Tickets.Include(t => t.StatusHistory).SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(TicketStatus.Closed, stored.Status);

        var entry = Assert.Single(stored.StatusHistory);
        Assert.Equal(AutomationUser.Id, entry.ChangedByUserId);
        Assert.Equal(TicketStatus.Resolved, entry.FromStatus);
        Assert.Equal(TicketStatus.Closed, entry.ToStatus);
        Assert.Contains(_notifier.Sent, n => n.Group == TestDatabase.RequesterId);
    }

    [Fact]
    public async Task ChangeStatusRule_RespectsTheWorkflow()
    {
        AddRule(r =>
        {
            r.ActionType = AutomationActionType.ChangeStatus;
            r.ActionStatus = TicketStatus.InProgress; // de Fechado ninguém sai por automação
        });
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddHours(-5), TicketStatus.Closed);

        await using var context = _db.CreateContext();
        var done = await Engine(context).RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);

        Assert.Empty(done);
        Assert.Equal(TicketStatus.Closed, (await context.Tickets.FindAsync(ticket.Id))!.Status);
    }

    [Fact]
    public async Task NotifyRules_ReachTheRightPeople()
    {
        AddRule(r => { r.Name = "A"; r.ActionType = AutomationActionType.NotifyAdmins; r.ActionMessage = "Chamado {id} — {titulo}"; });
        AddRule(r => { r.Name = "B"; r.ActionType = AutomationActionType.NotifyTeam; });
        AddRule(r => { r.Name = "C"; r.ActionType = AutomationActionType.NotifyRequester; });
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddMinutes(-1), assignedTo: TestDatabase.AgentId, title: "Impressora");

        await using var context = _db.CreateContext();
        await Engine(context).RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);

        Assert.Contains(_notifier.Sent, n => n.Group == "Admins" && n.Message == $"Chamado {ticket.Id} — Impressora");
        Assert.Contains(_notifier.Sent, n => n.Group == TestDatabase.AgentId);   // com responsável, só ele (não a fila)
        Assert.DoesNotContain(_notifier.Sent, n => n.Group == "Agents");
        Assert.Contains(_notifier.Sent, n => n.Group == TestDatabase.RequesterId);
    }

    [Fact]
    public async Task DisabledAndOtherTriggerRules_DoNothing()
    {
        AddRule(r => r.IsEnabled = false);
        AddRule(r => r.Trigger = AutomationTrigger.SlaBreached);
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddMinutes(-1));

        await using var context = _db.CreateContext();
        var done = await Engine(context).RunAsync(AutomationTrigger.TicketCreated, await context.Tickets.FirstAsync(t => t.Id == ticket.Id), Now);

        Assert.Empty(done);
        Assert.Empty(_notifier.Sent);
    }

    [Fact]
    public async Task Inactivity_ClosesStaleResolvedTicketsOnce()
    {
        AddRule(r =>
        {
            r.Trigger = AutomationTrigger.Inactivity;
            r.ConditionStatus = TicketStatus.Resolved;
            r.ConditionInactiveHours = 72;
            r.ActionType = AutomationActionType.ChangeStatus;
            r.ActionStatus = TicketStatus.Closed;
        });
        var stale = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-10), TicketStatus.Resolved, updatedAtUtc: Now.AddDays(-4));
        var recent = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-10), TicketStatus.Resolved, updatedAtUtc: Now.AddDays(-1));
        var open = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-10), TicketStatus.InProgress, updatedAtUtc: Now.AddDays(-4));

        await using var context = _db.CreateContext();
        var engine = Engine(context);

        Assert.Equal(1, await engine.RunInactivityAsync(Now));
        Assert.Equal(0, await engine.RunInactivityAsync(Now.AddMinutes(1))); // nada novo

        await using var check = _db.CreateContext();
        Assert.Equal(TicketStatus.Closed, (await check.Tickets.FindAsync(stale.Id))!.Status);
        Assert.Equal(TicketStatus.Resolved, (await check.Tickets.FindAsync(recent.Id))!.Status);
        Assert.Equal(TicketStatus.InProgress, (await check.Tickets.FindAsync(open.Id))!.Status);
    }

    [Fact]
    public async Task Inactivity_NotifyOnlyRuleRepeatsOnlyAfterNewActivity()
    {
        AddRule(r =>
        {
            r.Trigger = AutomationTrigger.Inactivity;
            r.ConditionInactiveHours = 48;
            r.ActionType = AutomationActionType.NotifyRequester;
        });
        var ticket = _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-5), TicketStatus.WaitingUser, updatedAtUtc: Now.AddDays(-3));

        await using var context = _db.CreateContext();
        var engine = Engine(context);

        Assert.Equal(1, await engine.RunInactivityAsync(Now));
        Assert.Equal(0, await engine.RunInactivityAsync(Now.AddHours(1))); // já avisou neste período parado

        // Alguém mexe no chamado e ele volta a ficar parado por mais de 48 h: pode avisar de novo.
        await using (var touch = _db.CreateContext())
        {
            await touch.Tickets.Where(t => t.Id == ticket.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.UpdatedAtUtc, Now.AddHours(2)));
        }

        Assert.Equal(0, await engine.RunInactivityAsync(Now.AddHours(40)));
        Assert.Equal(1, await engine.RunInactivityAsync(Now.AddHours(60)));
    }

    [Fact]
    public async Task Inactivity_NeverTouchesClosedOrCancelledTickets()
    {
        AddRule(r =>
        {
            r.Trigger = AutomationTrigger.Inactivity;
            r.ConditionInactiveHours = 1;
            r.ActionType = AutomationActionType.NotifyAdmins;
        });
        _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-5), TicketStatus.Closed, updatedAtUtc: Now.AddDays(-4));
        _db.AddTicket(Sla, TicketPriority.Medium, Now.AddDays(-5), TicketStatus.Cancelled, updatedAtUtc: Now.AddDays(-4));

        await using var context = _db.CreateContext();

        Assert.Equal(0, await Engine(context).RunInactivityAsync(Now));
    }
}
