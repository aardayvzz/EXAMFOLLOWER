using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LgsTakip.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringLessonSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringLessonSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsoDayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    EndLocalTime = table.Column<TimeOnly>(type: "time(0)", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NotificationEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ReminderMinutesBefore = table.Column<int>(type: "int", nullable: false),
                    LockRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringLessonSchedules", x => x.Id);
                    table.CheckConstraint("CK_RecurringLessonSchedules_IsoDayOfWeek", "[IsoDayOfWeek] BETWEEN 1 AND 7");
                    table.CheckConstraint("CK_RecurringLessonSchedules_LocalTimeRange", "[StartLocalTime] < [EndLocalTime]");
                    table.CheckConstraint("CK_RecurringLessonSchedules_MaximumDuration", "DATEDIFF(SECOND, [StartLocalTime], [EndLocalTime]) <= 28800");
                    table.CheckConstraint("CK_RecurringLessonSchedules_ReminderMinutesBefore", "[ReminderMinutesBefore] IN (0, 5, 10, 15, 30, 60)");
                    table.ForeignKey(
                        name: "FK_RecurringLessonSchedules_Users_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringLessonSchedules_StudentId_IsActive_IsoDayOfWeek",
                table: "RecurringLessonSchedules",
                columns: new[] { "StudentId", "IsActive", "IsoDayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "UX_RecurringLessonSchedules_StudentId_Source_ExternalId",
                table: "RecurringLessonSchedules",
                columns: new[] { "StudentId", "Source", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringLessonSchedules");
        }
    }
}
