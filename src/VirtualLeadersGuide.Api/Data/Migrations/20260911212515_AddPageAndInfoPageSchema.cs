using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VirtualLeadersGuide.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPageAndInfoPageSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PageTypeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                    table.CheckConstraint("CK_Pages_Title_NotEmpty", "TRIM(Title) <> ''");
                    table.ForeignKey(
                        name: "FK_Pages_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Pages_PageTypes_PageTypeId",
                        column: x => x.PageTypeId,
                        principalTable: "PageTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InfoPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MarkdownContent = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InfoPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InfoPages_Pages_Id",
                        column: x => x.Id,
                        principalTable: "Pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PageTypes",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "InfoPage" });

            migrationBuilder.CreateIndex(
                name: "IX_Pages_EventId",
                table: "Pages",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_PageTypeId",
                table: "Pages",
                column: "PageTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PageTypes_Name",
                table: "PageTypes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InfoPages");

            migrationBuilder.DropTable(
                name: "Pages");

            migrationBuilder.DropTable(
                name: "PageTypes");
        }
    }
}
