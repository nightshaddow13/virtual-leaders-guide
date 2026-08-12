using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLeadersGuide.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Passcode = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.CheckConstraint("CK_Events_Name_NotEmpty", "TRIM(Name) <> ''");
                    table.CheckConstraint("CK_Events_Slug_Format", "Slug <> '' AND Slug NOT LIKE '-%' AND Slug NOT LIKE '%-' AND Slug NOT LIKE '%--%' AND REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(Slug, 'a', ''), 'b', ''), 'c', ''), 'd', ''), 'e', ''), 'f', ''), 'g', ''), 'h', ''), 'i', ''), 'j', ''), 'k', ''), 'l', ''), 'm', ''), 'n', ''), 'o', ''), 'p', ''), 'q', ''), 'r', ''), 's', ''), 't', ''), 'u', ''), 'v', ''), 'w', ''), 'x', ''), 'y', ''), 'z', ''), '0', ''), '1', ''), '2', ''), '3', ''), '4', ''), '5', ''), '6', ''), '7', ''), '8', ''), '9', ''), '-', '') = ''");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_Name",
                table: "Events",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_Slug",
                table: "Events",
                column: "Slug",
                unique: true);

            // Events did not exist before this migration, so UserRoles.EventId - a pre-existing, previously
            // unenforced column (P2-3, #12) - cannot possibly hold a value that matches a real Events row yet.
            // Null out any non-null EventId before the FK below, rather than let AddForeignKey fail outright
            // on a database where that column was ever populated (P2-8, #17, is the only ticket that could
            // have written to it, and hasn't shipped as of this migration - so this is a no-op today, but
            // guards the "no data loss on an existing database" acceptance criterion against that assumption
            // ever turning out to be wrong, e.g. via a manual seed script). This can't be recovered any other
            // way: no Events row exists yet for an orphaned EventId to be backfilled against.
            migrationBuilder.Sql(
                "UPDATE [UserRoles] SET [EventId] = NULL WHERE [EventId] IS NOT NULL " +
                "AND NOT EXISTS (SELECT 1 FROM [Events] WHERE [Events].[Id] = [UserRoles].[EventId]);");

            migrationBuilder.AddForeignKey(
                name: "FK_UserRoles_Events_EventId",
                table: "UserRoles",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserRoles_Events_EventId",
                table: "UserRoles");

            migrationBuilder.DropTable(
                name: "Events");
        }
    }
}
