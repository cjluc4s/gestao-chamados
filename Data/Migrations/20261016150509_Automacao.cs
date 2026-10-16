using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gestao_chamados.Data.Migrations
{
    /// <inheritdoc />
    public partial class Automacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    ConditionCategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    ConditionPriority = table.Column<int>(type: "INTEGER", nullable: true),
                    ConditionStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ConditionKeywords = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ConditionInactiveHours = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ActionAgentId = table.Column<string>(type: "TEXT", nullable: true),
                    ActionPriority = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ActionMessage = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationRules_AspNetUsers_ActionAgentId",
                        column: x => x.ActionAgentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AutomationRules_Categories_ConditionCategoryId",
                        column: x => x.ConditionCategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutomationLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RuleId = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketId = table.Column<int>(type: "INTEGER", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutomationLogs_AutomationRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "AutomationRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AutomationLogs_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_RuleId_TicketId",
                table: "AutomationLogs",
                columns: new[] { "RuleId", "TicketId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationLogs_TicketId",
                table: "AutomationLogs",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_ActionAgentId",
                table: "AutomationRules",
                column: "ActionAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRules_ConditionCategoryId",
                table: "AutomationRules",
                column: "ConditionCategoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationLogs");

            migrationBuilder.DropTable(
                name: "AutomationRules");
        }
    }
}
