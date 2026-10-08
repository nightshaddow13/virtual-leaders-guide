using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLeadersGuide.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTierAndPlacementSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tabs", x => x.Id);
                    table.CheckConstraint("CK_Tabs_Name_NotEmpty", "TRIM(Name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "SubTabs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTabs", x => x.Id);
                    table.CheckConstraint("CK_SubTabs_Name_NotEmpty", "TRIM(Name) <> ''");
                    table.ForeignKey(
                        name: "FK_SubTabs_Tabs_TabId",
                        column: x => x.TabId,
                        principalTable: "Tabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentTabId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ParentSubTabId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.CheckConstraint("CK_Sections_ExactlyOneParent", "(ParentTabId IS NOT NULL AND ParentSubTabId IS NULL) OR (ParentTabId IS NULL AND ParentSubTabId IS NOT NULL)");
                    table.CheckConstraint("CK_Sections_Name_NotEmpty", "TRIM(Name) <> ''");
                    table.ForeignKey(
                        name: "FK_Sections_SubTabs_ParentSubTabId",
                        column: x => x.ParentSubTabId,
                        principalTable: "SubTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sections_Tabs_ParentTabId",
                        column: x => x.ParentTabId,
                        principalTable: "Tabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubSections", x => x.Id);
                    table.CheckConstraint("CK_SubSections_Name_NotEmpty", "TRIM(Name) <> ''");
                    table.ForeignKey(
                        name: "FK_SubSections_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActivityPlacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TabId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubTabId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPlacements", x => x.Id);
                    table.CheckConstraint("CK_ActivityPlacements_SubSectionRequiresSection", "[SubSectionId] IS NULL OR [SectionId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ActivityPlacements_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityPlacements_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityPlacements_SubSections_SubSectionId",
                        column: x => x.SubSectionId,
                        principalTable: "SubSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityPlacements_SubTabs_SubTabId",
                        column: x => x.SubTabId,
                        principalTable: "SubTabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActivityPlacements_Tabs_TabId",
                        column: x => x.TabId,
                        principalTable: "Tabs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_ActivityId",
                table: "ActivityPlacements",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_EventId",
                table: "ActivityPlacements",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_SectionId",
                table: "ActivityPlacements",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_SubSectionId",
                table: "ActivityPlacements",
                column: "SubSectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_SubTabId",
                table: "ActivityPlacements",
                column: "SubTabId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabId",
                table: "ActivityPlacements",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabOnly",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId" },
                unique: true,
                filter: "[SubTabId] IS NULL AND [SectionId] IS NULL AND [SubSectionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabSection",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId", "SectionId" },
                unique: true,
                filter: "[SubTabId] IS NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabSectionSubSection",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId", "SectionId", "SubSectionId" },
                unique: true,
                filter: "[SubTabId] IS NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabSubTab",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId", "SubTabId" },
                unique: true,
                filter: "[SubTabId] IS NOT NULL AND [SectionId] IS NULL AND [SubSectionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabSubTabSection",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId", "SubTabId", "SectionId" },
                unique: true,
                filter: "[SubTabId] IS NOT NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPlacements_TabSubTabSectionSubSection",
                table: "ActivityPlacements",
                columns: new[] { "ActivityId", "TabId", "SubTabId", "SectionId", "SubSectionId" },
                unique: true,
                filter: "[SubTabId] IS NOT NULL AND [SectionId] IS NOT NULL AND [SubSectionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_EventId",
                table: "Sections",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ParentSubTabId",
                table: "Sections",
                column: "ParentSubTabId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_ParentTabId",
                table: "Sections",
                column: "ParentTabId");

            migrationBuilder.CreateIndex(
                name: "IX_SubSections_EventId",
                table: "SubSections",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SubSections_SectionId",
                table: "SubSections",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTabs_EventId",
                table: "SubTabs",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_SubTabs_TabId",
                table: "SubTabs",
                column: "TabId");

            migrationBuilder.CreateIndex(
                name: "IX_Tabs_EventId",
                table: "Tabs",
                column: "EventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityPlacements");

            migrationBuilder.DropTable(
                name: "SubSections");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "SubTabs");

            migrationBuilder.DropTable(
                name: "Tabs");
        }
    }
}
