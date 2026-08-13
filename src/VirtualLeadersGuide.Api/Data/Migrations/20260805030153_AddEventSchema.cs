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

            // No pre-emptive cleanup of UserRoles.EventId before this FK - an earlier version of this
            // migration nulled out any non-null EventId with no matching Events row first, reasoning that
            // AddForeignKey would otherwise fail outright on a database where that column was ever populated.
            // That's backwards for "no data loss on an existing database" (AC5): AddForeignKey failing IS the
            // safe outcome - the migration aborts, nothing is touched, and a human investigates. Silently
            // nulling real column values to force the FK to succeed is the actual data-loss path, and doing it
            // unconditionally (as the removed step did) would have discarded a value on every single row that
            // had one, not just genuinely-orphaned ones. UserRoles.EventId is a pre-existing, previously
            // unenforced column (P2-3, #12); nothing in this codebase writes to it yet (P2-8, #17, the only
            // ticket that could, hasn't shipped), so AddForeignKey applying cleanly is the expected case - if
            // that assumption is ever wrong, the loud failure below is exactly what should happen.
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
