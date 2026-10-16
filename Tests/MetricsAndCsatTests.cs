using gestao_chamados.Data;
using gestao_chamados.Models;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Tests;

public class FeedbackPolicyTests
{
    [Theory]
    [InlineData(TicketStatus.Resolved, true, false, true)]
    [InlineData(TicketStatus.Closed, true, false, true)]
    [InlineData(TicketStatus.Open, true, false, false)]
    [InlineData(TicketStatus.InProgress, true, false, false)]
    [InlineData(TicketStatus.WaitingUser, true, false, false)]
    [InlineData(TicketStatus.Cancelled, true, false, false)]
    [InlineData(TicketStatus.Resolved, false, false, false)] // só o solicitante avalia
    [InlineData(TicketStatus.Resolved, true, true, false)]   // uma avaliação por chamado
    public void CanRate_OnlyRequesterOnceAfterResolution(TicketStatus status, bool isCreator, bool alreadyRated, bool expected)
    {
        Assert.Equal(expected, FeedbackPolicy.CanRate(status, isCreator, alreadyRated));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Validate_AcceptsRatingsFromOneToFive(int rating)
    {
        Assert.Null(FeedbackPolicy.Validate(rating, "ótimo"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Validate_RejectsRatingsOutOfRange(int rating)
    {
        Assert.NotNull(FeedbackPolicy.Validate(rating, null));
    }

    [Fact]
    public void Validate_RejectsTooLongComments()
    {
        Assert.Null(FeedbackPolicy.Validate(4, new string('a', FeedbackPolicy.MaxCommentLength)));
        Assert.NotNull(FeedbackPolicy.Validate(4, new string('a', FeedbackPolicy.MaxCommentLength + 1)));
    }
}

public class TicketLifecycleTests
{
    private static readonly DateTime T0 = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

    private static SlaCalculator Sla() => new(new SlaOptions { BusinessHours = new BusinessHoursOptions { Enabled = false } });

    private static Ticket NewTicket(SlaCalculator sla)
    {
        var ticket = new Ticket { Priority = TicketPriority.Medium, CreatedAtUtc = T0, CreatedByUserId = "requester" };
        sla.Start(ticket);
        return ticket;
    }

    private static void Move(SlaCalculator sla, Ticket ticket, TicketStatus to, DateTime now)
    {
        var from = ticket.Status;
        ticket.Status = to;
        TicketLifecycle.OnStatusChanged(sla, ticket, from, to, now);
    }

    [Fact]
    public void Resolving_StampsTheResolutionDateAndClosingKeepsIt()
    {
        var sla = Sla();
        var ticket = NewTicket(sla);

        Move(sla, ticket, TicketStatus.Resolved, T0.AddHours(3));
        Move(sla, ticket, TicketStatus.Closed, T0.AddHours(30));

        Assert.Equal(T0.AddHours(3), ticket.ResolvedAtUtc);
    }

    [Fact]
    public void ClosingDirectly_AlsoCountsAsResolved()
    {
        var sla = Sla();
        var ticket = NewTicket(sla);

        Move(sla, ticket, TicketStatus.Closed, T0.AddHours(1));

        Assert.Equal(T0.AddHours(1), ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Reopening_ClearsTheResolutionDateSoItCountsAgain()
    {
        var sla = Sla();
        var ticket = NewTicket(sla);
        Move(sla, ticket, TicketStatus.Resolved, T0.AddHours(3));

        Move(sla, ticket, TicketStatus.InProgress, T0.AddHours(5));
        Assert.Null(ticket.ResolvedAtUtc);

        Move(sla, ticket, TicketStatus.Resolved, T0.AddHours(8));
        Assert.Equal(T0.AddHours(8), ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Cancelling_IsNotAResolution()
    {
        var sla = Sla();
        var ticket = NewTicket(sla);

        Move(sla, ticket, TicketStatus.Cancelled, T0.AddHours(1));

        Assert.Null(ticket.ResolvedAtUtc);
    }

    [Fact]
    public void FirstResponse_IsTheFirstStaffCommentByAnotherPerson()
    {
        var ticket = NewTicket(Sla());

        TicketLifecycle.OnComment(ticket, "requester", authorIsStaff: false, T0.AddMinutes(5));
        Assert.Null(ticket.FirstResponseAtUtc); // o próprio solicitante não conta

        TicketLifecycle.OnComment(ticket, "agent", authorIsStaff: true, T0.AddMinutes(20));
        TicketLifecycle.OnComment(ticket, "agent", authorIsStaff: true, T0.AddMinutes(40));

        Assert.Equal(T0.AddMinutes(20), ticket.FirstResponseAtUtc); // só a primeira
    }

    [Fact]
    public void FirstResponse_IgnoresStaffCommentingOnTheirOwnTicket()
    {
        var ticket = NewTicket(Sla());

        TicketLifecycle.OnComment(ticket, "requester", authorIsStaff: true, T0.AddMinutes(5)); // admin que abriu o chamado

        Assert.Null(ticket.FirstResponseAtUtc);
    }
}

public class MetricsCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime From = Now.AddDays(-7);

    private static SlaCalculator Sla() => new(new SlaOptions { BusinessHours = new BusinessHoursOptions { Enabled = false } });

    private static TicketMetricRow Row(
        int id,
        DateTime created,
        TicketStatus status = TicketStatus.Open,
        TicketPriority priority = TicketPriority.Medium,
        string category = "Geral",
        DateTime? firstResponse = null,
        DateTime? resolved = null,
        long paused = 0,
        DateTime? pausedAt = null,
        DateTime? due = null,
        string? agentId = null,
        string? agentName = null,
        int? rating = null) =>
        new(id, priority, category, status, created, firstResponse, resolved, paused, pausedAt ?? resolved, due ?? created.AddHours(12), agentId, agentName, rating);

    private static MetricsReport Compute(params TicketMetricRow[] rows) =>
        MetricsCalculator.Compute(rows, From, Now, Sla(), TimeZoneInfo.Utc);

    [Fact]
    public void EmptyData_ProducesEmptyMetricsWithoutDividingByZero()
    {
        var report = Compute();

        Assert.Equal(0, report.Opened);
        Assert.Equal(0, report.Resolved);
        Assert.Null(report.AvgFirstResponse);
        Assert.Null(report.AvgResolution);
        Assert.Null(report.SlaMetPercent);
        Assert.Null(report.AvgRating);
        Assert.Null(report.RatingResponsePercent);
        Assert.Empty(report.Agents);
        Assert.Equal(8, report.Daily.Count);
    }

    [Fact]
    public void CountsOpenedAndResolvedOnlyInsideThePeriod()
    {
        var report = Compute(
            Row(1, Now.AddDays(-2)),                                                     // aberto no período
            Row(2, Now.AddDays(-30), TicketStatus.Resolved, resolved: Now.AddDays(-1)),  // aberto antes, resolvido no período
            Row(3, Now.AddDays(-30), TicketStatus.Resolved, resolved: Now.AddDays(-20))); // fora do período

        Assert.Equal(1, report.Opened);
        Assert.Equal(1, report.Resolved);
    }

    [Fact]
    public void Backlog_CountsEveryTicketThatStillNeedsAttention()
    {
        var report = Compute(
            Row(1, Now.AddDays(-60)), // aberto há muito tempo continua na fila
            Row(2, Now.AddDays(-1), TicketStatus.InProgress),
            Row(3, Now.AddDays(-1), TicketStatus.WaitingUser),
            Row(4, Now.AddDays(-1), TicketStatus.Resolved, resolved: Now.AddHours(-5)),
            Row(5, Now.AddDays(-1), TicketStatus.Cancelled));

        Assert.Equal(3, report.Backlog);
    }

    [Fact]
    public void AverageFirstResponse_IsMeasuredFromCreation()
    {
        var created = Now.AddDays(-2);
        var report = Compute(
            Row(1, created, firstResponse: created.AddMinutes(10)),
            Row(2, created, firstResponse: created.AddMinutes(50)),
            Row(3, created)); // sem resposta ainda: fora da média

        Assert.Equal(TimeSpan.FromMinutes(30), report.AvgFirstResponse);
    }

    [Fact]
    public void AverageResolution_DiscountsTheTimeWaitingForTheRequester()
    {
        var created = Now.AddDays(-3);
        var report = Compute(
            Row(1, created, TicketStatus.Resolved, resolved: created.AddHours(10), paused: 4 * 3600),
            Row(2, created, TicketStatus.Resolved, resolved: created.AddHours(4)));

        // (10 h - 4 h parado) e 4 h → média de 5 h.
        Assert.Equal(TimeSpan.FromHours(5), report.AvgResolution);
    }

    [Fact]
    public void SlaMet_ComparesTheDeadlineWithTheMomentTheClockStopped()
    {
        var created = Now.AddDays(-3);
        var report = Compute(
            Row(1, created, TicketStatus.Resolved, resolved: created.AddHours(5), due: created.AddHours(12)),  // no prazo
            Row(2, created, TicketStatus.Closed, resolved: created.AddHours(20), due: created.AddHours(12)),   // atrasado
            Row(3, created, TicketStatus.Resolved, resolved: created.AddHours(2), due: created.AddHours(12)),  // no prazo
            Row(4, created, TicketStatus.Resolved, resolved: created.AddHours(8), due: created.AddHours(12)));  // no prazo

        Assert.Equal(75.0, report.SlaMetPercent);
        Assert.Equal(4, report.SlaEvaluated);
    }

    [Fact]
    public void Csat_AveragesRatingsOfResolvedTicketsAndReportsTheResponseRate()
    {
        var created = Now.AddDays(-3);
        var report = Compute(
            Row(1, created, TicketStatus.Resolved, resolved: created.AddHours(1), rating: 5),
            Row(2, created, TicketStatus.Resolved, resolved: created.AddHours(1), rating: 4),
            Row(3, created, TicketStatus.Resolved, resolved: created.AddHours(1), rating: 4),
            Row(4, created, TicketStatus.Resolved, resolved: created.AddHours(1)));

        Assert.Equal(13.0 / 3, report.AvgRating!.Value, 6);
        Assert.Equal(3, report.RatingCount);
        Assert.Equal(75.0, report.RatingResponsePercent);
        Assert.Equal([0, 0, 0, 2, 1], report.RatingDistribution);
    }

    [Fact]
    public void Daily_BucketsByLocalDay()
    {
        // 02:00 UTC em São Paulo (UTC-3) ainda é o dia anterior.
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "E. South America Standard Time" : "America/Sao_Paulo");
        var created = new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc);

        var report = MetricsCalculator.Compute([Row(1, created)], From, Now, Sla(), saoPaulo);

        var point = report.Daily.Single(d => d.Opened == 1);
        Assert.Equal(new DateOnly(2026, 10, 13), point.Day);
    }

    [Fact]
    public void Agents_AreRankedByResolvedTicketsWithTheirOwnAverages()
    {
        var created = Now.AddDays(-3);
        var report = Compute(
            Row(1, created, TicketStatus.Resolved, resolved: created.AddHours(2), agentId: "a", agentName: "Ana", rating: 5),
            Row(2, created, TicketStatus.Resolved, resolved: created.AddHours(4), agentId: "a", agentName: "Ana", rating: 3),
            Row(3, created, TicketStatus.Resolved, resolved: created.AddHours(6), agentId: "b", agentName: "Bruno"),
            Row(4, created, TicketStatus.Resolved, resolved: created.AddHours(1))); // sem responsável: fora do ranking

        Assert.Equal(["Ana", "Bruno"], report.Agents.Select(a => a.Name));
        var ana = report.Agents[0];
        Assert.Equal(2, ana.Resolved);
        Assert.Equal(TimeSpan.FromHours(3), ana.AvgResolution);
        Assert.Equal(4.0, ana.AvgRating);
        Assert.Null(report.Agents[1].AvgRating);
    }

    [Fact]
    public void Breakdowns_ListHighestPriorityFirstAndTopCategories()
    {
        var created = Now.AddDays(-1);
        var rows = new List<TicketMetricRow>
        {
            Row(1, created, priority: TicketPriority.Low),
            Row(2, created, priority: TicketPriority.Critical),
            Row(3, created, priority: TicketPriority.Critical)
        };
        for (var i = 0; i < 8; i++)
        {
            rows.Add(Row(10 + i, created, category: $"Cat {i}"));
        }

        var report = MetricsCalculator.Compute(rows, From, Now, Sla(), TimeZoneInfo.Utc);

        Assert.Equal(TicketPriority.Critical, report.ByPriority[0].Key);
        Assert.Equal(2, report.ByPriority[0].Value);
        Assert.Equal(6, report.ByCategory.Count);
    }
}

public class FeedbackStorageTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public void OnlyOneFeedbackPerTicketIsStored()
    {
        var sla = new SlaCalculator(new SlaOptions { BusinessHours = new BusinessHoursOptions { Enabled = false } });
        var ticket = _db.AddTicket(sla, TicketPriority.Medium, new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc), TicketStatus.Resolved);

        using var context = _db.CreateContext();
        context.TicketFeedbacks.Add(new TicketFeedback { TicketId = ticket.Id, Rating = 5, CreatedByUserId = TestDatabase.RequesterId });
        context.SaveChanges();

        using var second = _db.CreateContext();
        second.TicketFeedbacks.Add(new TicketFeedback { TicketId = ticket.Id, Rating = 1, CreatedByUserId = TestDatabase.RequesterId });

        Assert.Throws<DbUpdateException>(() => second.SaveChanges());
    }
}
