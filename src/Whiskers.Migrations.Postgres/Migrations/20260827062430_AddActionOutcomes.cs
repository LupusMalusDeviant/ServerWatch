using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Whiskers.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddActionOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionOutcomes",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    ActionKind = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<string>(type: "text", nullable: false),
                    TargetName = table.Column<string>(type: "text", nullable: false),
                    ExecutedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionOutcomes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActionOutcomes_ActionKind_ExecutedAtUtc",
                table: "ActionOutcomes",
                columns: new[] { "ActionKind", "ExecutedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ActionOutcomes_CorrelationId",
                table: "ActionOutcomes",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ActionOutcomes_Verdict_DueAtUtc",
                table: "ActionOutcomes",
                columns: new[] { "Verdict", "DueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionOutcomes");
        }
    }
}
