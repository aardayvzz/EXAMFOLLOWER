using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgsTakip.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRunningStudySessionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudySessions_StudentId",
                table: "StudySessions");

            migrationBuilder.CreateIndex(
                name: "UX_StudySessions_StudentId_Running",
                table: "StudySessions",
                column: "StudentId",
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_StudySessions_StudentId_Running",
                table: "StudySessions");

            migrationBuilder.CreateIndex(
                name: "IX_StudySessions_StudentId",
                table: "StudySessions",
                column: "StudentId");
        }
    }
}
