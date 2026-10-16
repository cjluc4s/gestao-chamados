using gestao_chamados.Data;
using gestao_chamados.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace gestao_chamados.Tests;

/// <summary>
/// Banco SQLite em memória com as migrations reais aplicadas. Além de permitir testar o código que
/// fala com o banco, valida que as migrations rodam do zero.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public const string RequesterId = "user-1";
    public const string AgentId = "agent-1";

    public TestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.Migrate();

        context.Users.AddRange(
            new ApplicationUser { Id = RequesterId, UserName = "user@test", NormalizedUserName = "USER@TEST", Email = "user@test", FullName = "Usuário Teste" },
            new ApplicationUser { Id = AgentId, UserName = "agent@test", NormalizedUserName = "AGENT@TEST", Email = "agent@test", FullName = "Atendente Teste" });
        context.Categories.AddRange(new Category { Id = 1, Name = "Geral" }, new Category { Id = 2, Name = "Acesso" });

        // Papel de Atendente e usuário interno das automações (o app cria os dois na inicialização).
        context.Roles.Add(new IdentityRole { Id = "role-agent", Name = RoleNames.Agent, NormalizedName = "AGENT" });
        context.UserRoles.Add(new IdentityUserRole<string> { UserId = AgentId, RoleId = "role-agent" });
        context.Users.Add(new ApplicationUser
        {
            Id = AutomationUser.Id,
            UserName = AutomationUser.UserName,
            NormalizedUserName = AutomationUser.UserName.ToUpperInvariant(),
            FullName = AutomationUser.FullName
        });
        context.SaveChanges();
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public Ticket AddTicket(
        SlaCalculator sla,
        TicketPriority priority,
        DateTime createdUtc,
        TicketStatus status = TicketStatus.Open,
        string? assignedTo = null,
        int categoryId = 1,
        string title = "Chamado de teste",
        DateTime? updatedAtUtc = null)
    {
        using var context = CreateContext();
        var ticket = new Ticket
        {
            Title = title,
            Description = "Descrição",
            Priority = priority,
            CategoryId = categoryId,
            CreatedByUserId = RequesterId,
            AssignedToUserId = assignedTo,
            CreatedAtUtc = createdUtc,
            UpdatedAtUtc = updatedAtUtc,
            Status = TicketStatus.Open
        };
        sla.Start(ticket);

        if (status != TicketStatus.Open)
        {
            ticket.Status = status;
            sla.OnStatusChanged(ticket, TicketStatus.Open, status, createdUtc.AddMinutes(1));
        }

        context.Tickets.Add(ticket);
        context.SaveChanges();
        return ticket;
    }

    public void Dispose() => _connection.Dispose();
}
