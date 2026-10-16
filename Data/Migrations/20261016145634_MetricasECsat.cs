using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gestao_chamados.Data.Migrations
{
    /// <inheritdoc />
    public partial class MetricasECsat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FirstResponseAtUtc",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "Tickets",
                type: "TEXT",
                nullable: true);

            // Chamados antigos: a resolução vem do último registro de histórico em Resolvido (4) ou
            // Fechado (5); a primeira resposta, do primeiro comentário de quem não abriu o chamado.
            migrationBuilder.Sql(
                "UPDATE \"Tickets\" SET \"ResolvedAtUtc\" = (SELECT MAX(h.\"ChangedAtUtc\") FROM \"TicketStatusHistory\" h " +
                "WHERE h.\"TicketId\" = \"Tickets\".\"Id\" AND h.\"ToStatus\" IN (4, 5)) WHERE \"Status\" IN (4, 5);");
            migrationBuilder.Sql(
                "UPDATE \"Tickets\" SET \"FirstResponseAtUtc\" = (SELECT MIN(c.\"CreatedAtUtc\") FROM \"TicketComments\" c " +
                "WHERE c.\"TicketId\" = \"Tickets\".\"Id\" AND c.\"AuthorUserId\" <> \"Tickets\".\"CreatedByUserId\");");

            migrationBuilder.CreateTable(
                name: "TicketFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TicketId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rating = table.Column<int>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketFeedbacks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketFeedbacks_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketFeedbacks_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketFeedbacks_CreatedByUserId",
                table: "TicketFeedbacks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketFeedbacks_TicketId",
                table: "TicketFeedbacks",
                column: "TicketId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketFeedbacks");

            migrationBuilder.DropColumn(
                name: "FirstResponseAtUtc",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "Tickets");
        }
    }
}
