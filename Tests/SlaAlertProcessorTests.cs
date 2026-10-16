using gestao_chamados.Data;
using gestao_chamados.Models;
using gestao_chamados.Services;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Tests;

public class SlaAlertProcessorTests : IDisposable
{
    private readonly TestDatabase _db = new();

    // Quinta-feira 15/10/2026.
    private static DateTime Utc(int hour, int minute = 0, int day = 15) =>
        new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    // Sem horário comercial: o relógio corre direto e as contas ficam fáceis de acompanhar.
    private static SlaCalculator Sla() => new(new SlaOptions
    {
        WarnAtPercent = 80,
        BusinessHours = new BusinessHoursOptions { Enabled = false }
    });

    public void Dispose() => _db.Dispose();

    private async Task<IReadOnlyList<SlaAlert>> RunAsync(SlaCalculator sla, DateTime now)
    {
        await using var context = _db.CreateContext();
        return await SlaAlertProcessor.ProcessAsync(context, sla, now);
    }

    [Fact]
    public async Task WarnsOnceWhenTheTicketReachesTheThreshold()
    {
        var sla = Sla();
        var ticket = _db.AddTicket(sla, TicketPriority.High, Utc(8)); // 4 h: limite 12h

        Assert.Empty(await RunAsync(sla, Utc(10)));

        var alerts = await RunAsync(sla, Utc(11, 30)); // 87,5%
        var alert = Assert.Single(alerts);
        Assert.Equal(SlaAlertKind.Warning, alert.Kind);
        Assert.Equal(ticket.Id, alert.TicketId);

        Assert.Empty(await RunAsync(sla, Utc(11, 45))); // não repete
    }

    [Fact]
    public async Task EscalatesOnceWhenTheDeadlineIsMissed()
    {
        var sla = Sla();
        _db.AddTicket(sla, TicketPriority.High, Utc(8));

        await RunAsync(sla, Utc(11, 30)); // aviso
        var alerts = await RunAsync(sla, Utc(12, 30));

        Assert.Equal(SlaAlertKind.Breach, Assert.Single(alerts).Kind);
        Assert.Empty(await RunAsync(sla, Utc(13)));

        await using var context = _db.CreateContext();
        var stored = await context.Tickets.SingleAsync();
        Assert.NotNull(stored.SlaWarnedAtUtc);
        Assert.NotNull(stored.SlaEscalatedAtUtc);
    }

    [Fact]
    public async Task GoingStraightToBreachStillEscalatesWithoutAWarning()
    {
        var sla = Sla();
        _db.AddTicket(sla, TicketPriority.High, Utc(8));

        var alerts = await RunAsync(sla, Utc(15)); // o monitor ficou fora do ar

        Assert.Equal(SlaAlertKind.Breach, Assert.Single(alerts).Kind);
    }

    [Theory]
    [InlineData(TicketStatus.WaitingUser)]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    [InlineData(TicketStatus.Cancelled)]
    public async Task IgnoresTicketsWhoseClockIsStopped(TicketStatus status)
    {
        var sla = Sla();
        _db.AddTicket(sla, TicketPriority.High, Utc(8), status);

        Assert.Empty(await RunAsync(sla, Utc(20)));
    }

    [Fact]
    public async Task AlertCarriesTheResponsiblePeople()
    {
        var sla = Sla();
        _db.AddTicket(sla, TicketPriority.High, Utc(8), assignedTo: TestDatabase.AgentId);

        var alert = Assert.Single(await RunAsync(sla, Utc(11, 30)));

        Assert.Equal(TestDatabase.AgentId, alert.AssignedToUserId);
        Assert.Equal(TestDatabase.RequesterId, alert.CreatedByUserId);
    }

    [Fact]
    public async Task BusinessHoursAreRespectedOvernight()
    {
        var sla = new SlaCalculator(new SlaOptions
        {
            WarnAtPercent = 80,
            BusinessHours = new BusinessHoursOptions { Enabled = true, TimeZone = "UTC", Start = "08:00", End = "18:00" }
        });
        _db.AddTicket(sla, TicketPriority.High, Utc(17)); // 4 h úteis: 1 h na quinta + 3 h na sexta

        // Quinta 23h e sexta 07h: ainda dentro de 25% do prazo, nada a avisar.
        Assert.Empty(await RunAsync(sla, Utc(23)));
        Assert.Empty(await RunAsync(sla, Utc(7, day: 16)));

        // Sexta 10h30: 1 h + 2h30 = 3h30 de 4 h.
        Assert.Equal(SlaAlertKind.Warning, Assert.Single(await RunAsync(sla, Utc(10, 30, day: 16))).Kind);
    }

    [Fact]
    public async Task IgnoresStoppedStatusesEvenWhenThePauseInstantIsMissing()
    {
        var sla = Sla();
        _db.AddTicket(sla, TicketPriority.High, Utc(8), TicketStatus.Resolved);

        await using (var context = _db.CreateContext())
        {
            await context.Tickets.ExecuteUpdateAsync(s => s.SetProperty(t => t.SlaPausedAtUtc, (DateTime?)null));
        }

        Assert.Empty(await RunAsync(sla, Utc(20)));
    }
}
