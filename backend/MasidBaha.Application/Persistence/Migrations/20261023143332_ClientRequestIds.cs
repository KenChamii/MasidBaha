using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasidBaha.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientRequestIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "client_request_id",
                table: "flood_reports",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_flood_reports_client_request",
                table: "flood_reports",
                column: "client_request_id",
                unique: true,
                filter: "client_request_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_flood_reports_client_request",
                table: "flood_reports");

            migrationBuilder.DropColumn(
                name: "client_request_id",
                table: "flood_reports");
        }
    }
}
