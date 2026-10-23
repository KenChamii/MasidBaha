using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasidBaha.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdminPortalFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_hidden",
                table: "flood_reports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reporter_ip_hash",
                table: "flood_reports",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deactivated_at",
                table: "admin_users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "must_change_password",
                table: "admin_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "admin_audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "broadcasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    body = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    url = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sent_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    sent_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    recipients = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broadcasts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reporter_bans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lifted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    lifted_by_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporter_bans", x => x.id);
                    table.CheckConstraint("ck_reporter_bans_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_reporter_bans_mode", "mode IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_flood_reports_reporter_ip_hash",
                table: "flood_reports",
                column: "reporter_ip_hash");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_action",
                table: "admin_audit_log",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_at",
                table: "admin_audit_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_log_target_type_target_id",
                table: "admin_audit_log",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_broadcasts_sent_at",
                table: "broadcasts",
                column: "sent_at");

            migrationBuilder.CreateIndex(
                name: "ix_reporter_bans_kind_value",
                table: "reporter_bans",
                columns: new[] { "kind", "value" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_log");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "broadcasts");

            migrationBuilder.DropTable(
                name: "reporter_bans");

            migrationBuilder.DropIndex(
                name: "ix_flood_reports_reporter_ip_hash",
                table: "flood_reports");

            migrationBuilder.DropColumn(
                name: "is_hidden",
                table: "flood_reports");

            migrationBuilder.DropColumn(
                name: "reporter_ip_hash",
                table: "flood_reports");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                table: "admin_users");

            migrationBuilder.DropColumn(
                name: "must_change_password",
                table: "admin_users");
        }
    }
}
