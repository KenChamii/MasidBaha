using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace MasidBaha.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WatchAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "area_lat",
                table: "broadcasts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "area_lng",
                table: "broadcasts",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "area_radius_meters",
                table: "broadcasts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "watch_areas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watch_areas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_watch_areas_location",
                table: "watch_areas",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_watch_areas_session_id",
                table: "watch_areas",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "watch_areas");

            migrationBuilder.DropColumn(
                name: "area_lat",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "area_lng",
                table: "broadcasts");

            migrationBuilder.DropColumn(
                name: "area_radius_meters",
                table: "broadcasts");
        }
    }
}
