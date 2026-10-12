using System.Data;
using System.Security.Cryptography;
using System.Text;
using LgsTakip.Api.Application.ParentAnalytics.Contracts;
using LgsTakip.Api.Application.ParentLinking.Contracts;
using LgsTakip.Api.Application.ParentLinking.Interfaces;
using LgsTakip.Api.Application.ParentLinking.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Infrastructure.ParentLinking;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LgsTakip.Api.Application.ParentLinking.Services;

public sealed class ParentLinkService(
    LgsTakipDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<ParentLinkingOptions> options) : IParentLinkService
{
    private const int TokenByteLength = 32;
    private const int EncodedTokenLength = 43;
    private const string ActiveInvitationIndexName =
        "UX_ParentStudentInvitations_StudentId_Active";
    private const string TokenHashIndexName =
        "UX_ParentStudentInvitations_TokenHash";
    private const string ParentStudentPrimaryKeyName = "PK_ParentStudent";

    private readonly ParentLinkingOptions _options = options.Value;

    public async Task<ParentLinkResult<CreateParentLinkInvitationResponse>>
        CreateInvitationAsync(
            string? studentIdClaim,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<CreateParentLinkInvitationResponse>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var studentIsValid = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == studentId
                && user.Role == UserRole.Student
                && user.IsActive,
                cancellationToken);

        if (!studentIsValid)
        {
            return Failure<CreateParentLinkInvitationResponse>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        var nowUtc = GetUtcNow();
        var activeInvitations = await dbContext.ParentStudentInvitations
            .Where(invitation => invitation.StudentId == studentId
                && invitation.ConsumedAtUtc == null
                && invitation.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var invitation in activeInvitations)
        {
            invitation.RevokedAtUtc = nowUtc;
        }

        try
        {
            if (activeInvitations.Count > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var rawToken = WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(TokenByteLength));
            var tokenHash = HashToken(rawToken);
            var expiresAtUtc = nowUtc.AddMinutes(
                _options.InvitationLifetimeMinutes);
            var newInvitation = new ParentStudentInvitation
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                TokenHash = tokenHash,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = expiresAtUtc
            };

            dbContext.ParentStudentInvitations.Add(newInvitation);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Success(new CreateParentLinkInvitationResponse
            {
                InvitationId = newInvitation.Id,
                LinkToken = rawToken,
                ExpiresAtUtc = expiresAtUtc
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure<CreateParentLinkInvitationResponse>(
                ParentLinkErrorCode.DataConflict);
        }
        catch (DbUpdateException exception)
            when (IsUniqueIndexViolation(
                exception,
                ActiveInvitationIndexName)
                || IsUniqueIndexViolation(exception, TokenHashIndexName))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Failure<CreateParentLinkInvitationResponse>(
                ParentLinkErrorCode.DataConflict);
        }
    }

    public async Task<ParentLinkResult<bool>> RevokeInvitationAsync(
        string? studentIdClaim,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidUserIdentity);
        }

        if (invitationId == Guid.Empty)
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidInput);
        }

        var invitation = await dbContext.ParentStudentInvitations
            .SingleOrDefaultAsync(candidate => candidate.Id == invitationId
                && candidate.StudentId == studentId,
                cancellationToken);

        if (invitation is null)
        {
            return Failure<bool>(ParentLinkErrorCode.InvitationNotFound);
        }

        if (invitation.ConsumedAtUtc.HasValue)
        {
            return Failure<bool>(
                ParentLinkErrorCode.InvitationCannotBeRevoked);
        }

        if (invitation.RevokedAtUtc.HasValue)
        {
            return Success(true);
        }

        invitation.RevokedAtUtc = GetUtcNow();

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return Success(true);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure<bool>(ParentLinkErrorCode.DataConflict);
        }
    }

    public async Task<ParentLinkResult<LinkedStudentResponse>>
        RedeemInvitationAsync(
            string? parentIdClaim,
            RedeemParentLinkRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        if (!TryNormalizeAndHashToken(request, out var tokenHash))
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var invitation = await dbContext.ParentStudentInvitations
            .Include(candidate => candidate.Student)
            .SingleOrDefaultAsync(
                candidate => candidate.TokenHash.SequenceEqual(tokenHash),
                cancellationToken);

        if (invitation is null)
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        var parent = await dbContext.Users
            .Include(user => user.Students.Where(
                student => student.Id == invitation.StudentId))
            .SingleOrDefaultAsync(user => user.Id == parentId
                && user.Role == UserRole.Parent
                && user.IsActive,
                cancellationToken);

        if (parent is null)
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        var student = invitation.Student;
        if (student.Role != UserRole.Student || !student.IsActive)
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        if (invitation.ConsumedAtUtc.HasValue)
        {
            if (invitation.ConsumedByParentId == parentId
                && parent.Students.Any(candidate => candidate.Id == student.Id))
            {
                return Success(ToLinkedStudentResponse(student));
            }

            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        var nowUtc = GetUtcNow();
        if (invitation.RevokedAtUtc.HasValue
            || invitation.ExpiresAtUtc <= nowUtc)
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        if (!parent.Students.Any(candidate => candidate.Id == student.Id))
        {
            parent.Students.Add(student);
        }

        invitation.ConsumedAtUtc = nowUtc;
        invitation.ConsumedByParentId = parentId;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Success(ToLinkedStudentResponse(student));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            return await ResolveRedeemConflictAsync(
                tokenHash,
                parentId,
                cancellationToken);
        }
        catch (DbUpdateException exception)
            when (IsUniqueIndexViolation(
                exception,
                ParentStudentPrimaryKeyName))
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            return await ResolveRedeemConflictAsync(
                tokenHash,
                parentId,
                cancellationToken);
        }
    }

    public async Task<ParentLinkResult<IReadOnlyList<LinkedParentResponse>>>
        GetLinkedParentsAsync(
            string? studentIdClaim,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<IReadOnlyList<LinkedParentResponse>>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        var studentIsValid = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.Id == studentId
                && user.Role == UserRole.Student
                && user.IsActive,
                cancellationToken);

        if (!studentIsValid)
        {
            return Failure<IReadOnlyList<LinkedParentResponse>>(
                ParentLinkErrorCode.InvalidUserIdentity);
        }

        IReadOnlyList<LinkedParentResponse> parents = await dbContext.Users
            .AsNoTracking()
            .Where(parent => parent.Role == UserRole.Parent
                && parent.Students.Any(student => student.Id == studentId))
            .OrderBy(parent => parent.DisplayName)
            .ThenBy(parent => parent.Id)
            .Select(parent => new LinkedParentResponse
            {
                Id = parent.Id,
                DisplayName = parent.DisplayName,
                Email = parent.Email
            })
            .ToListAsync(cancellationToken);

        return Success(parents);
    }

    public async Task<ParentLinkResult<bool>> RemoveStudentLinkAsync(
        string? parentIdClaim,
        Guid studentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(parentIdClaim, out var parentId))
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidUserIdentity);
        }

        if (studentId == Guid.Empty)
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidInput);
        }

        var parent = await dbContext.Users
            .Include(user => user.Students.Where(
                student => student.Id == studentId))
            .SingleOrDefaultAsync(user => user.Id == parentId
                && user.Role == UserRole.Parent
                && user.IsActive,
                cancellationToken);

        if (parent is null)
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidUserIdentity);
        }

        var student = parent.Students.SingleOrDefault();
        if (student is null)
        {
            return Success(true);
        }

        parent.Students.Remove(student);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Success(true);
    }

    public async Task<ParentLinkResult<bool>> RemoveParentLinkAsync(
        string? studentIdClaim,
        Guid parentId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(studentIdClaim, out var studentId))
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidUserIdentity);
        }

        if (parentId == Guid.Empty)
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidInput);
        }

        var student = await dbContext.Users
            .Include(user => user.Parents.Where(parent => parent.Id == parentId))
            .SingleOrDefaultAsync(user => user.Id == studentId
                && user.Role == UserRole.Student
                && user.IsActive,
                cancellationToken);

        if (student is null)
        {
            return Failure<bool>(ParentLinkErrorCode.InvalidUserIdentity);
        }

        var parent = student.Parents.SingleOrDefault();
        if (parent is null)
        {
            return Success(true);
        }

        student.Parents.Remove(parent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Success(true);
    }

    private async Task<ParentLinkResult<LinkedStudentResponse>>
        ResolveRedeemConflictAsync(
            byte[] tokenHash,
            Guid parentId,
            CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();

        var consumedInvitation = await dbContext.ParentStudentInvitations
            .AsNoTracking()
            .Where(invitation => invitation.TokenHash.SequenceEqual(tokenHash)
                && invitation.ConsumedByParentId == parentId
                && invitation.ConsumedAtUtc != null)
            .Select(invitation => new
            {
                invitation.StudentId,
                invitation.Student.DisplayName,
                invitation.Student.Email,
                invitation.Student.IsActive,
                invitation.Student.Role
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (consumedInvitation is null
            || !consumedInvitation.IsActive
            || consumedInvitation.Role != UserRole.Student)
        {
            return Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
        }

        var relationshipExists = await dbContext.Users
            .AsNoTracking()
            .AnyAsync(parent => parent.Id == parentId
                && parent.Role == UserRole.Parent
                && parent.IsActive
                && parent.Students.Any(
                    student => student.Id == consumedInvitation.StudentId),
                cancellationToken);

        return relationshipExists
            ? Success(new LinkedStudentResponse
            {
                Id = consumedInvitation.StudentId,
                DisplayName = consumedInvitation.DisplayName,
                Email = consumedInvitation.Email,
                IsActive = consumedInvitation.IsActive
            })
            : Failure<LinkedStudentResponse>(
                ParentLinkErrorCode.InvalidOrExpiredInvitation);
    }

    private static bool TryNormalizeAndHashToken(
        RedeemParentLinkRequest? request,
        out byte[] tokenHash)
    {
        tokenHash = [];
        if (request is null)
        {
            return false;
        }

        var token = request.LinkToken.Trim();
        if (token.Length != EncodedTokenLength
            || token.Any(character => !char.IsAsciiLetterOrDigit(character)
                && character is not '-' and not '_'))
        {
            return false;
        }

        try
        {
            if (WebEncoders.Base64UrlDecode(token).Length != TokenByteLength)
            {
                return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }

        tokenHash = HashToken(token);
        return true;
    }

    private static byte[] HashToken(string token)
    {
        return SHA256.HashData(Encoding.ASCII.GetBytes(token));
    }

    private static LinkedStudentResponse ToLinkedStudentResponse(User student)
    {
        return new LinkedStudentResponse
        {
            Id = student.Id,
            DisplayName = student.DisplayName,
            Email = student.Email,
            IsActive = student.IsActive
        };
    }

    private DateTimeOffset GetUtcNow()
    {
        return timeProvider.GetUtcNow().ToUniversalTime();
    }

    private static bool TryGetUserId(string? userIdClaim, out Guid userId)
    {
        return Guid.TryParse(userIdClaim, out userId)
            && userId != Guid.Empty;
    }

    private static bool IsUniqueIndexViolation(
        DbUpdateException exception,
        string databaseObjectName)
    {
        return exception.InnerException is SqlException
            {
                Number: 2601 or 2627
            } sqlException
            && sqlException.Message.Contains(
                databaseObjectName,
                StringComparison.Ordinal);
    }

    private static ParentLinkResult<T> Success<T>(T value)
    {
        return ParentLinkResult<T>.Success(value);
    }

    private static ParentLinkResult<T> Failure<T>(
        ParentLinkErrorCode errorCode)
    {
        return ParentLinkResult<T>.Failure(errorCode);
    }
}
