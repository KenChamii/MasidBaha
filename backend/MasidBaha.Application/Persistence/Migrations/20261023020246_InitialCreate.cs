using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace MasidBaha.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "flood_reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    severity = table.Column<short>(type: "smallint", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    confidence_score = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    reporter_session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    province = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flood_reports", x => x.id);
                    table.CheckConstraint("ck_flood_reports_severity", "severity BETWEEN 1 AND 4");
                    table.CheckConstraint("ck_flood_reports_status", "status BETWEEN 1 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    p256dh = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    auth = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_subscriptions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "report_votes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    flood_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    voter_session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    vote_type = table.Column<short>(type: "smallint", nullable: false),
                    voted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_votes", x => x.id);
                    table.CheckConstraint("ck_report_votes_vote_type", "vote_type IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_report_votes_flood_reports_flood_report_id",
                        column: x => x.flood_report_id,
                        principalTable: "flood_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_location",
                table: "flood_reports",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_region_province_city",
                table: "flood_reports",
                columns: new[] { "region", "province", "city" });

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_reported_at",
                table: "flood_reports",
                column: "reported_at");

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_reporter_session_id",
                table: "flood_reports",
                column: "reporter_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_status_expires_at",
                table: "flood_reports",
                columns: new[] { "status", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_push_subscriptions_endpoint",
                table: "push_subscriptions",
                column: "endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_report_votes_flood_report_id_voter_session_id",
                table: "report_votes",
                columns: new[] { "flood_report_id", "voter_session_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "report_votes");

            migrationBuilder.DropTable(
                name: "flood_reports");
        }
    }
}
