using gestao_chamados.Data;
using gestao_chamados.Models;
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
        context.Categories.Add(new Category { Id = 1, Name = "Geral" });
        context.SaveChanges();
    }

    public ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    public Ticket AddTicket(
        SlaCalculator sla,
        TicketPriority priority,
        DateTime createdUtc,
        TicketStatus status = TicketStatus.Open,
        string? assignedTo = null)
    {
        using var context = CreateContext();
        var ticket = new Ticket
        {
            Title = "Chamado de teste",
            Description = "Descrição",
            Priority = priority,
            CategoryId = 1,
            CreatedByUserId = RequesterId,
            AssignedToUserId = assignedTo,
            CreatedAtUtc = createdUtc,
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
