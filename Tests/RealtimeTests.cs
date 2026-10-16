using gestao_chamados.Services;

namespace gestao_chamados.Tests;

public class PresenceTrackerTests
{
    private static PresenceViewer Ana => new("u-ana", "Ana Souza");
    private static PresenceViewer Bruno => new("u-bruno", "Bruno Lima");

    [Fact]
    public void Join_ReturnsEveryoneViewingTheTicket()
    {
        var presence = new PresenceTracker();

        presence.Join(1, "c1", Bruno);
        var viewers = presence.Join(1, "c2", Ana);

        Assert.Equal(["u-ana", "u-bruno"], viewers.Select(v => v.UserId)); // ordenado por nome
    }

    [Fact]
    public void SameUserInTwoTabsAppearsOnce()
    {
        var presence = new PresenceTracker();

        presence.Join(1, "tab-1", Ana);
        var viewers = presence.Join(1, "tab-2", Ana);

        Assert.Single(viewers);
    }

    [Fact]
    public void Leave_KeepsTheUserWhileAnotherTabIsOpen()
    {
        var presence = new PresenceTracker();
        presence.Join(1, "tab-1", Ana);
        presence.Join(1, "tab-2", Ana);

        Assert.Single(presence.Leave(1, "tab-1"));
        Assert.Empty(presence.Leave(1, "tab-2"));
    }

    [Fact]
    public void ViewersOfDifferentTicketsAreIndependent()
    {
        var presence = new PresenceTracker();
        presence.Join(1, "c1", Ana);
        presence.Join(2, "c2", Bruno);

        Assert.Equal("u-ana", Assert.Single(presence.Viewers(1)).UserId);
        Assert.Equal("u-bruno", Assert.Single(presence.Viewers(2)).UserId);
        Assert.Empty(presence.Viewers(3));
    }

    [Fact]
    public void LeaveAll_RemovesTheConnectionFromEveryTicketAndReportsWhoRemains()
    {
        var presence = new PresenceTracker();
        presence.Join(1, "c1", Ana);
        presence.Join(1, "c2", Bruno);
        presence.Join(2, "c1", Ana);

        var affected = presence.LeaveAll("c1");

        Assert.Equal(2, affected.Count);
        Assert.Equal("u-bruno", Assert.Single(affected.Single(a => a.TicketId == 1).Viewers).UserId);
        Assert.Empty(affected.Single(a => a.TicketId == 2).Viewers);
        Assert.Empty(presence.LeaveAll("c1")); // idempotente
    }

    [Fact]
    public void TryGetViewer_OnlyFindsConnectionsThatJoined()
    {
        var presence = new PresenceTracker();
        presence.Join(1, "c1", Ana);

        Assert.True(presence.TryGetViewer(1, "c1", out var viewer));
        Assert.Equal("Ana Souza", viewer.Name);
        Assert.False(presence.TryGetViewer(1, "outra", out _));
        Assert.False(presence.TryGetViewer(2, "c1", out _));
    }
}

public class TicketNotifierAudienceTests
{
    [Fact]
    public void UnassignedTicket_ReachesAdminsRequesterAndAllAgents()
    {
        var audience = TicketNotifier.AudienceFor("requester", assignedToUserId: null);

        Assert.Equivalent(new[] { "Admins", "requester", "Agents" }, audience);
    }

    [Fact]
    public void AssignedTicket_ReachesOnlyThePeopleWhoCanSeeIt()
    {
        var audience = TicketNotifier.AudienceFor("requester", assignedToUserId: "agent-1");

        Assert.Equivalent(new[] { "Admins", "requester", "agent-1" }, audience);
        Assert.DoesNotContain("Agents", audience); // outros atendentes não enxergam este chamado
    }

    [Fact]
    public void TakingATicketFromTheQueue_AlsoNotifiesAgentsSoTheirQueueRefreshes()
    {
        var audience = TicketNotifier.AudienceFor("requester", "agent-1", previousAssigneeId: null, assignmentChanged: true);

        Assert.Equivalent(new[] { "Admins", "requester", "agent-1", "Agents" }, audience);
    }

    [Fact]
    public void ReassigningATicket_NotifiesBothAgentsButNotTheQueue()
    {
        var audience = TicketNotifier.AudienceFor("requester", "agent-2", previousAssigneeId: "agent-1", assignmentChanged: true);

        Assert.Equivalent(new[] { "Admins", "requester", "agent-1", "agent-2" }, audience);
    }

    [Fact]
    public void ReturningATicketToTheQueue_NotifiesThePreviousAgentAndAllAgents()
    {
        var audience = TicketNotifier.AudienceFor("requester", null, previousAssigneeId: "agent-1", assignmentChanged: true);

        Assert.Equivalent(new[] { "Admins", "requester", "agent-1", "Agents" }, audience);
    }

    [Fact]
    public void AudienceHasNoDuplicates()
    {
        // O solicitante é também o responsável (ex.: Admin abrindo e atendendo o próprio chamado).
        var audience = TicketNotifier.AudienceFor("same", "same", previousAssigneeId: "same", assignmentChanged: true);

        Assert.Equal(audience.Count, audience.Distinct().Count());
    }
}
