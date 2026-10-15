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
        var deadline = SlaPolicy.GetDeadline(priority);

        Assert.Equal(TimeSpan.FromHours(expectedHours), deadline);
    }

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(TicketStatus.Open)]
    [InlineData(TicketStatus.InProgress)]
    [InlineData(TicketStatus.WaitingUser)]
    public void IsBreached_TrueWhenOverdueAndStillOpen(TicketStatus status)
    {
        Assert.True(SlaPolicy.IsBreached(Now.AddMinutes(-1), status, Now));
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void IsBreached_FalseOnceResolvedOrClosed(TicketStatus status)
    {
        Assert.False(SlaPolicy.IsBreached(Now.AddHours(-5), status, Now));
    }

    [Fact]
    public void IsBreached_FalseBeforeDeadlineOrWithoutDeadline()
    {
        Assert.False(SlaPolicy.IsBreached(Now.AddMinutes(1), TicketStatus.Open, Now));
        Assert.False(SlaPolicy.IsBreached(null, TicketStatus.Open, Now));
    }
}
