using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Whiskers.Migrations
{
    /// <inheritdoc />
    public partial class AddSelfMetricSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SelfMetricSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TakenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Loop = table.Column<string>(type: "TEXT", nullable: false),
                    ServerId = table.Column<string>(type: "TEXT", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastDurationMs = table.Column<double>(type: "REAL", nullable: false),
                    Cycles = table.Column<long>(type: "INTEGER", nullable: false),
                    Failures = table.Column<long>(type: "INTEGER", nullable: false),
                    Skips = table.Column<long>(type: "INTEGER", nullable: false),
                    SkipReason = table.Column<string>(type: "TEXT", nullable: true),
                    ExpectedIntervalSeconds = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SelfMetricSamples", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SelfMetricSamples_Loop_ServerId_TakenAtUtc",
                table: "SelfMetricSamples",
                columns: new[] { "Loop", "ServerId", "TakenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SelfMetricSamples_TakenAtUtc",
                table: "SelfMetricSamples",
                column: "TakenAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SelfMetricSamples");
        }
    }
}
