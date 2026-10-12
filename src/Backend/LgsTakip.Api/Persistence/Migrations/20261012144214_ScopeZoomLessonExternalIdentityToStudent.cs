using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgsTakip.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScopeZoomLessonExternalIdentityToStudent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZoomLessons_Source_ExternalId",
                table: "ZoomLessons");

            migrationBuilder.DropIndex(
                name: "IX_ZoomLessons_StudentId",
                table: "ZoomLessons");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomLessons_StudentId_StartAtUtc",
                table: "ZoomLessons",
                columns: new[] { "StudentId", "StartAtUtc" });

            migrationBuilder.CreateIndex(
                name: "UX_ZoomLessons_StudentId_Source_ExternalId",
                table: "ZoomLessons",
                columns: new[] { "StudentId", "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ZoomLessons_StudentId_StartAtUtc",
                table: "ZoomLessons");

            migrationBuilder.DropIndex(
                name: "UX_ZoomLessons_StudentId_Source_ExternalId",
                table: "ZoomLessons");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomLessons_Source_ExternalId",
                table: "ZoomLessons",
                columns: new[] { "Source", "ExternalId" },
                unique: true,
                filter: "[ExternalId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ZoomLessons_StudentId",
                table: "ZoomLessons",
                column: "StudentId");
        }
    }
}
