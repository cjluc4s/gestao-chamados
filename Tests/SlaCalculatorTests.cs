using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class SlaCalculatorTests
{
    // 2026-10-15 é quinta-feira; 16 sexta; 17 sábado; 19 segunda.
    private static DateTime Utc(int day, int hour, int minute = 0) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static SlaCalculator Calculator(bool businessHours = true, int warnAt = 80) =>
        new(new SlaOptions
        {
            WarnAtPercent = warnAt,
            AlwaysOnPriorities = [TicketPriority.Critical],
            BusinessHours = new BusinessHoursOptions
            {
                Enabled = businessHours,
                TimeZone = "UTC",
                Start = "08:00",
                End = "18:00",
                Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]
            }
        });

    private static Ticket NewTicket(SlaCalculator sla, TicketPriority priority, DateTime createdUtc)
    {
        var ticket = new Ticket { Priority = priority, CreatedAtUtc = createdUtc, Status = TicketStatus.Open };
        sla.Start(ticket);
        return ticket;
    }

    private static void Move(SlaCalculator sla, Ticket ticket, TicketStatus to, DateTime nowUtc)
    {
        var from = ticket.Status;
        ticket.Status = to;
        sla.OnStatusChanged(ticket, from, to, nowUtc);
    }

    [Fact]
    public void Start_CountsOnlyBusinessHours()
    {
        var sla = Calculator();

        // Média = 12 h úteis: quinta 10h → 8 h na quinta + 4 h na sexta.
        Assert.Equal(Utc(16, 12), NewTicket(sla, TicketPriority.Medium, Utc(15, 10)).DueAtUtc);
    }

    [Fact]
    public void Start_AlwaysOnPrioritiesRunAroundTheClock()
    {
        var sla = Calculator();

        Assert.Equal(Utc(17, 12), NewTicket(sla, TicketPriority.Critical, Utc(17, 10)).DueAtUtc);
        Assert.False(sla.UsesBusinessHours(TicketPriority.Critical));
        Assert.True(sla.UsesBusinessHours(TicketPriority.High));
    }

    [Fact]
    public void Start_WithBusinessHoursDisabledUsesWallClockTime()
    {
        var sla = Calculator(businessHours: false);

        Assert.Equal(Utc(17, 10), NewTicket(sla, TicketPriority.Low, Utc(16, 10)).DueAtUtc);
        Assert.Null(sla.Calendar);
    }

    [Fact]
    public void WaitingForUser_PausesTheClockAndExtendsTheDeadlineOnResume()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Medium, Utc(15, 10)); // limite: sexta 12h

        Move(sla, ticket, TicketStatus.WaitingUser, Utc(15, 14));
        Assert.Equal(Utc(15, 14), ticket.SlaPausedAtUtc);

        // Quinta 14h → sexta 10h = 4 h + 2 h úteis parado.
        Move(sla, ticket, TicketStatus.InProgress, Utc(16, 10));

        Assert.Null(ticket.SlaPausedAtUtc);
        Assert.Equal(6 * 3600, ticket.SlaPausedSeconds);
        Assert.Equal(Utc(16, 18), ticket.DueAtUtc); // 18 h úteis desde quinta 10h
    }

    [Fact]
    public void Evaluate_WhilePausedDoesNotAdvanceNorBreach()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Medium, Utc(15, 10));
        Move(sla, ticket, TicketStatus.WaitingUser, Utc(15, 12));

        var earlier = sla.Evaluate(ticket, Utc(15, 13));
        var muchLater = sla.Evaluate(ticket, Utc(30, 13));

        Assert.Equal(SlaState.Paused, muchLater.State);
        Assert.False(muchLater.Breached);
        Assert.Equal(earlier.Consumed, muchLater.Consumed, 6);
        Assert.Equal(2.0 / 12.0, muchLater.Consumed, 6);
    }

    [Fact]
    public void Evaluate_ReportsOkAtRiskAndBreached()
    {
        var sla = Calculator(warnAt: 80);
        var ticket = NewTicket(sla, TicketPriority.High, Utc(15, 8)); // 4 h úteis: limite 12h

        Assert.Equal(SlaState.Ok, sla.Evaluate(ticket, Utc(15, 10)).State);

        var risk = sla.Evaluate(ticket, Utc(15, 11, 30)); // 3h30 de 4h = 87,5%
        Assert.Equal(SlaState.AtRisk, risk.State);
        Assert.Equal(TimeSpan.FromMinutes(30), risk.Remaining);

        var late = sla.Evaluate(ticket, Utc(15, 13));
        Assert.Equal(SlaState.Breached, late.State);
        Assert.True(late.Breached);
        Assert.Equal(TimeSpan.FromHours(1), late.Overdue);
    }

    [Fact]
    public void Evaluate_OvernightTimeDoesNotConsumeBusinessSla()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.High, Utc(15, 17)); // 1 h na quinta + 3 h na sexta

        var nextMorning = sla.Evaluate(ticket, Utc(16, 8));

        Assert.Equal(0.25, nextMorning.Consumed, 6);
        Assert.Equal(SlaState.Ok, nextMorning.State);
    }

    [Fact]
    public void Evaluate_ResolvedOnTimeStaysWithinSlaForever()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Medium, Utc(15, 10));
        Move(sla, ticket, TicketStatus.Resolved, Utc(15, 15));

        var years = sla.Evaluate(ticket, new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(SlaState.Done, years.State);
        Assert.False(years.Breached);
    }

    [Fact]
    public void Evaluate_ResolvedLateStaysBreached()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.High, Utc(15, 8)); // limite 12h
        Move(sla, ticket, TicketStatus.Resolved, Utc(15, 14));

        var snapshot = sla.Evaluate(ticket, Utc(20, 9));

        Assert.Equal(SlaState.Done, snapshot.State);
        Assert.True(snapshot.Breached);
    }

    [Fact]
    public void ReopeningAResolvedTicket_DoesNotChargeTheTimeItWasResolved()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Medium, Utc(15, 10)); // limite sexta 12h
        Move(sla, ticket, TicketStatus.Resolved, Utc(15, 12));

        // Reaberto na segunda 9h: quinta 12h → sexta 18h + segunda 8h→9h = 6 h + 10 h + 1 h = 17 h úteis parado.
        Move(sla, ticket, TicketStatus.InProgress, Utc(19, 9));

        Assert.Equal(17 * 3600, ticket.SlaPausedSeconds);
        Assert.False(sla.Evaluate(ticket, Utc(19, 9)).Breached);
    }

    [Fact]
    public void ResolvedThenClosed_KeepsTheOriginalStopInstant()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Medium, Utc(15, 10));
        Move(sla, ticket, TicketStatus.Resolved, Utc(15, 12));

        Move(sla, ticket, TicketStatus.Closed, Utc(16, 12));

        Assert.Equal(Utc(15, 12), ticket.SlaPausedAtUtc);
    }

    [Fact]
    public void RecalculateDue_CountsFromCreationAndRearmsTheAlerts()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.High, Utc(15, 8)); // limite 12h
        ticket.SlaWarnedAtUtc = Utc(15, 11);
        ticket.SlaEscalatedAtUtc = Utc(15, 12, 1);

        // Prioridade rebaixada para Baixa (24 h úteis) quinta 13h: voltou para dentro do prazo.
        ticket.Priority = TicketPriority.Low;
        sla.RecalculateDue(ticket, Utc(15, 13));

        Assert.Equal(Utc(19, 12), ticket.DueAtUtc); // quinta 10 h + sexta 10 h + segunda 4 h
        Assert.Null(ticket.SlaEscalatedAtUtc);
        Assert.Null(ticket.SlaWarnedAtUtc); // 5 h de 24 h: abaixo de 80%
    }

    [Fact]
    public void RecalculateDue_RaisingPriorityOfAnOldTicketMakesItOverdue()
    {
        var sla = Calculator();
        var ticket = NewTicket(sla, TicketPriority.Low, Utc(15, 8));

        ticket.Priority = TicketPriority.High;
        sla.RecalculateDue(ticket, Utc(15, 17));

        Assert.True(sla.Evaluate(ticket, Utc(15, 17)).Breached);
    }

    [Fact]
    public void Constructor_FailsFastOnInvalidCalendar()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SlaCalculator(new SlaOptions { BusinessHours = new BusinessHoursOptions { Start = "18:00", End = "08:00" } }));
    }
}
