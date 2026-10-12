using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgsTakip.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExamResultQueryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamResults_StudentId",
                table: "ExamResults");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_StudentId_ExamDateUtc",
                table: "ExamResults",
                columns: new[] { "StudentId", "ExamDateUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ExamResults_StudentId_ExamDateUtc",
                table: "ExamResults");

            migrationBuilder.CreateIndex(
                name: "IX_ExamResults_StudentId",
                table: "ExamResults",
                column: "StudentId");
        }
    }
}
