using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Persistence;

public class LgsTakipDbContext(DbContextOptions<LgsTakipDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<StudySession> StudySessions => Set<StudySession>();
    public DbSet<ZoomLesson> ZoomLessons => Set<ZoomLesson>();
    public DbSet<ExamResult> ExamResults => Set<ExamResult>();
    public DbSet<ParentStudentInvitation> ParentStudentInvitations =>
        Set<ParentStudentInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(user => user.Id);

            entity.Property(user => user.Email)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(user => user.NormalizedEmail)
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(user => user.PasswordHash)
                .HasMaxLength(512)
                .IsRequired();

            entity.Property(user => user.DisplayName)
                .HasMaxLength(200)
                .IsRequired();

            entity.HasIndex(user => user.NormalizedEmail)
                .IsUnique();

            entity.HasMany(user => user.Parents)
                .WithMany(user => user.Students)
                .UsingEntity<Dictionary<string, object>>(
                    "ParentStudent",
                    parent => parent
                        .HasOne<User>()
                        .WithMany()
                        .HasForeignKey("ParentId")
                        .OnDelete(DeleteBehavior.Restrict),
                    student => student
                        .HasOne<User>()
                        .WithMany()
                        .HasForeignKey("StudentId")
                        .OnDelete(DeleteBehavior.Restrict),
                    join =>
                    {
                        join.ToTable("ParentStudent");
                        join.HasKey("ParentId", "StudentId");
                    });
        });

        modelBuilder.Entity<StudySession>(entity =>
        {
            entity.ToTable("StudySessions", table =>
            {
                table.HasCheckConstraint(
                    "CK_StudySessions_PlannedDurationMinutes_NonNegative",
                    "[PlannedDurationMinutes] >= 0");
                table.HasCheckConstraint(
                    "CK_StudySessions_CompletedDurationSeconds_NonNegative",
                    "[CompletedDurationSeconds] >= 0");
            });

            entity.HasKey(session => session.Id);

            entity.HasIndex(session => session.StudentId,
                    "UX_StudySessions_StudentId_Running")
                .IsUnique()
                .HasFilter(
                    $"[Status] = {(int)StudySessionStatus.Running}");

            entity.HasOne(session => session.Student)
                .WithMany(user => user.StudySessions)
                .HasForeignKey(session => session.StudentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParentStudentInvitation>(entity =>
        {
            entity.ToTable("ParentStudentInvitations");
            entity.HasKey(invitation => invitation.Id);

            entity.Property(invitation => invitation.TokenHash)
                .HasColumnType("binary(32)")
                .IsFixedLength()
                .IsRequired();

            entity.Property(invitation => invitation.RowVersion)
                .IsRowVersion()
                .IsRequired();

            entity.HasIndex(invitation => invitation.TokenHash,
                    "UX_ParentStudentInvitations_TokenHash")
                .IsUnique();

            entity.HasIndex(invitation => invitation.StudentId,
                    "UX_ParentStudentInvitations_StudentId_Active")
                .IsUnique()
                .HasFilter(
                    "[ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL");

            entity.HasOne(invitation => invitation.Student)
                .WithMany()
                .HasForeignKey(invitation => invitation.StudentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(invitation => invitation.ConsumedByParent)
                .WithMany()
                .HasForeignKey(invitation => invitation.ConsumedByParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ZoomLesson>(entity =>
        {
            entity.HasKey(lesson => lesson.Id);

            entity.Property(lesson => lesson.Title)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(lesson => lesson.JoinUrl)
                .HasMaxLength(2048)
                .IsRequired();

            entity.Property(lesson => lesson.Source)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(lesson => lesson.ExternalId)
                .HasMaxLength(256);

            entity.HasIndex(lesson => new { lesson.Source, lesson.ExternalId })
                .IsUnique()
                .HasFilter("[ExternalId] IS NOT NULL");

            entity.HasOne(lesson => lesson.Student)
                .WithMany(user => user.ZoomLessons)
                .HasForeignKey(lesson => lesson.StudentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamResult>(entity =>
        {
            entity.ToTable("ExamResults", table =>
            {
                table.HasCheckConstraint(
                    "CK_ExamResults_CorrectCount_NonNegative",
                    "[CorrectCount] >= 0");
                table.HasCheckConstraint(
                    "CK_ExamResults_IncorrectCount_NonNegative",
                    "[IncorrectCount] >= 0");
                table.HasCheckConstraint(
                    "CK_ExamResults_BlankCount_NonNegative",
                    "[BlankCount] >= 0");
            });

            entity.HasKey(result => result.Id);

            entity.HasIndex(result => new
                {
                    result.StudentId,
                    result.ExamDateUtc
                },
                "IX_ExamResults_StudentId_ExamDateUtc");

            entity.Property(result => result.ExamName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(result => result.NetScore)
                .HasPrecision(8, 2);

            entity.Property(result => result.LgsScore)
                .HasPrecision(8, 2);

            entity.HasOne(result => result.Student)
                .WithMany(user => user.ExamResults)
                .HasForeignKey(result => result.StudentId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
