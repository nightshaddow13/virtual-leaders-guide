using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLeadersGuide.Api.Data.Migrations;

/// <inheritdoc />
public partial class _20261125021227_AddInfoPagePlacementSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "InfoPagePlacements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                InfoPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SubTabId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InfoPagePlacements", x => x.Id);
                table.ForeignKey(
                    name: "FK_InfoPagePlacements_InfoPages_InfoPageId",
                    column: x => x.InfoPageId,
                    principalTable: "InfoPages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_InfoPagePlacements_SubTabs_SubTabId",
                    column: x => x.SubTabId,
                    principalTable: "SubTabs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_InfoPagePlacements_Tabs_TabId",
                    column: x => x.TabId,
                    principalTable: "Tabs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_InfoPagePlacements_EventId",
            table: "InfoPagePlacements",
            column: "EventId");

        migrationBuilder.CreateIndex(
            name: "IX_InfoPagePlacements_InfoPageId",
            table: "InfoPagePlacements",
            column: "InfoPageId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_InfoPagePlacements_SubTabId",
            table: "InfoPagePlacements",
            column: "SubTabId");

        migrationBuilder.CreateIndex(
            name: "IX_InfoPagePlacements_TabOnly",
            table: "InfoPagePlacements",
            column: "TabId",
            unique: true,
            filter: "[SubTabId] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_InfoPagePlacements_TabSubTab",
            table: "InfoPagePlacements",
            columns: new[] { "TabId", "SubTabId" },
            unique: true,
            filter: "[SubTabId] IS NOT NULL");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "InfoPagePlacements");
    }
}
