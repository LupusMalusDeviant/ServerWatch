using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Whiskers.Migrations.Postgres.Migrations
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
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TakenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Loop = table.Column<string>(type: "text", nullable: false),
                    ServerId = table.Column<string>(type: "text", nullable: false),
                    LastSuccessUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDurationMs = table.Column<double>(type: "double precision", nullable: false),
                    Cycles = table.Column<long>(type: "bigint", nullable: false),
                    Failures = table.Column<long>(type: "bigint", nullable: false),
                    Skips = table.Column<long>(type: "bigint", nullable: false),
                    SkipReason = table.Column<string>(type: "text", nullable: true),
                    ExpectedIntervalSeconds = table.Column<double>(type: "double precision", nullable: true)
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
