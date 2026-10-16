using gestao_chamados.Data;
using gestao_chamados.Models;

namespace gestao_chamados.Tests;

public class SlaPolicyTests
{
    [Theory]
    [InlineData(TicketPriority.Critical, 2)]
    [InlineData(TicketPriority.High, 4)]
    [InlineData(TicketPriority.Medium, 12)]
    [InlineData(TicketPriority.Low, 24)]
    public void GetDeadline_ReturnsExpectedWindow(TicketPriority priority, double expectedHours)
    {
        Assert.Equal(TimeSpan.FromHours(expectedHours), SlaPolicy.GetDeadline(priority));
    }

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IsBreached_TrueWhenDeadlinePassedAndClockRunning()
    {
        Assert.True(SlaPolicy.IsBreached(Now.AddMinutes(-1), pausedAtUtc: null, Now));
    }

    [Fact]
    public void IsBreached_FalseBeforeDeadlineOrWithoutDeadline()
    {
        Assert.False(SlaPolicy.IsBreached(Now.AddMinutes(1), null, Now));
        Assert.False(SlaPolicy.IsBreached(null, null, Now));
    }

    [Fact]
    public void IsBreached_StoppedClockUsesTheInstantItStopped()
    {
        var due = Now.AddHours(-5);

        // Parou antes do limite: continua "no prazo" por mais tempo que passe.
        Assert.False(SlaPolicy.IsBreached(due, pausedAtUtc: due.AddHours(-1), Now));

        // Já estava atrasado quando parou: continua atrasado.
        Assert.True(SlaPolicy.IsBreached(due, pausedAtUtc: due.AddHours(1), Now));
    }

    [Theory]
    [InlineData(TicketStatus.Open, false, false)]
    [InlineData(TicketStatus.InProgress, false, false)]
    [InlineData(TicketStatus.WaitingUser, true, false)]
    [InlineData(TicketStatus.Resolved, true, true)]
    [InlineData(TicketStatus.Closed, true, true)]
    [InlineData(TicketStatus.Cancelled, true, true)]
    public void StatusGroups_MatchTheSlaRules(TicketStatus status, bool clockStopped, bool done)
    {
        Assert.Equal(clockStopped, SlaPolicy.IsClockStopped(status));
        Assert.Equal(done, SlaPolicy.IsDone(status));
    }

    [Fact]
    public void EveryStatus_IsCoveredByTheStatusGroups()
    {
        // Se alguém criar um status novo, este teste obriga a decidir se o relógio para.
        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            if (SlaPolicy.IsDone(status))
            {
                Assert.True(SlaPolicy.IsClockStopped(status), $"{status} é 'encerrado' mas o relógio não para.");
            }
        }
    }
}
