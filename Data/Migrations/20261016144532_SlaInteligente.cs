using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gestao_chamados.Data.Migrations
{
    /// <inheritdoc />
    public partial class SlaInteligente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SlaEscalatedAtUtc",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaPausedAtUtc",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SlaPausedSeconds",
                table: "Tickets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "SlaWarnedAtUtc",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            // Chamados que já estavam parados (3 Aguardando usuário, 4 Resolvido, 5 Fechado, 6 Cancelado)
            // têm o relógio parado desde a última movimentação; sem isso voltariam a "correr".
            migrationBuilder.Sql(
                "UPDATE \"Tickets\" SET \"SlaPausedAtUtc\" = COALESCE(\"UpdatedAtUtc\", \"CreatedAtUtc\") WHERE \"Status\" IN (3, 4, 5, 6);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SlaEscalatedAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SlaPausedAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SlaPausedSeconds",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SlaWarnedAtUtc",
                table: "Tickets");
        }
    }
}
