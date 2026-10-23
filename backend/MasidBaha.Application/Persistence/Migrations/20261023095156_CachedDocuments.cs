using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MasidBaha.Application.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CachedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cached_documents",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    json = table.Column<string>(type: "text", nullable: false),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cached_documents", x => x.key);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cached_documents");
        }
    }
}
