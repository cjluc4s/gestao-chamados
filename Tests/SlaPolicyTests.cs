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
}
