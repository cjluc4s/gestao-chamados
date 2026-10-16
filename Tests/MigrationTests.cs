using gestao_chamados.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace gestao_chamados.Tests;

public class MigrationTests
{
    [Fact]
    public async Task SlaMigration_StopsTheClockOfTicketsThatWereAlreadyStopped()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();

        // Estado antigo do banco: antes das colunas de SLA existirem.
        await migrator.MigrateAsync("20260825175135_InitialCreate");

        await context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO AspNetUsers (Id, FullName, AccessFailedCount, EmailConfirmed, LockoutEnabled, PhoneNumberConfirmed, TwoFactorEnabled)
            VALUES ('u1', 'Fulano', 0, 1, 1, 0, 0);
            INSERT INTO Categories (Id, Name) VALUES (1, 'Geral');");

        foreach (var (id, status) in new[] { (1, 1), (2, 2), (3, 3), (4, 4), (5, 5), (6, 6) })
        {
            await context.Database.ExecuteSqlAsync($@"
                INSERT INTO Tickets (Id, Title, Description, Priority, Status, CategoryId, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc)
                VALUES ({id}, {"T" + id}, 'D', 2, {status}, 1, 'u1', '2026-10-01 10:00:00', '2026-10-02 11:00:00');");
        }

        await migrator.MigrateAsync();

        var paused = new Dictionary<int, string?>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, SlaPausedAtUtc, SlaPausedSeconds FROM Tickets ORDER BY Id";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                paused[reader.GetInt32(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                Assert.Equal(0, reader.GetInt64(2));
            }
        }

        // Aberto e Em andamento continuam correndo; Aguardando, Resolvido, Fechado e Cancelado param
        // na última movimentação.
        Assert.Null(paused[1]);
        Assert.Null(paused[2]);
        foreach (var id in new[] { 3, 4, 5, 6 })
        {
            Assert.Equal("2026-10-02 11:00:00", paused[id]);
        }
    }

    [Fact]
    public async Task MetricsMigration_BackfillsResolutionAndFirstResponseOfOldTickets()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var context = new ApplicationDbContext(options);
        var migrator = context.GetService<IMigrator>();
        var previous = context.GetService<IMigrationsAssembly>().Migrations.Keys.Single(k => k.EndsWith("_SlaInteligente"));

        await migrator.MigrateAsync(previous);

        await context.Database.ExecuteSqlRawAsync(@"
            INSERT INTO AspNetUsers (Id, FullName, AccessFailedCount, EmailConfirmed, LockoutEnabled, PhoneNumberConfirmed, TwoFactorEnabled)
            VALUES ('req', 'Solicitante', 0, 1, 1, 0, 0), ('ag', 'Atendente', 0, 1, 1, 0, 0);
            INSERT INTO Categories (Id, Name) VALUES (1, 'Geral');
            INSERT INTO Tickets (Id, Title, Description, Priority, Status, CategoryId, CreatedByUserId, CreatedAtUtc, SlaPausedSeconds)
            VALUES (1, 'Resolvido', 'D', 2, 4, 1, 'req', '2026-10-01 10:00:00', 0),
                   (2, 'Aberto', 'D', 2, 1, 1, 'req', '2026-10-01 10:00:00', 0);
            INSERT INTO TicketStatusHistory (TicketId, FromStatus, ToStatus, ChangedByUserId, ChangedAtUtc)
            VALUES (1, 1, 2, 'ag', '2026-10-01 11:00:00'), (1, 2, 4, 'ag', '2026-10-01 15:00:00'),
                   (1, 4, 2, 'ag', '2026-10-02 09:00:00'), (1, 2, 4, 'ag', '2026-10-02 12:00:00');
            INSERT INTO TicketComments (TicketId, Message, AuthorUserId, CreatedAtUtc)
            VALUES (1, 'oi', 'req', '2026-10-01 10:05:00'), (1, 'resposta', 'ag', '2026-10-01 10:30:00'),
                   (1, 'outra', 'ag', '2026-10-01 11:30:00');");

        await migrator.MigrateAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, ResolvedAtUtc, FirstResponseAtUtc FROM Tickets ORDER BY Id";
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("2026-10-02 12:00:00", reader.GetString(1)); // última vez que foi resolvido
        Assert.Equal("2026-10-01 10:30:00", reader.GetString(2)); // 1º comentário de quem não abriu o chamado

        Assert.True(await reader.ReadAsync());
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    [Fact]
    public async Task Model_HasNoPendingChangesComparedToTheMigrations()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

        await using var context = new ApplicationDbContext(options);

        // Falha se alguém mudar um modelo e esquecer de gerar a migration.
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
