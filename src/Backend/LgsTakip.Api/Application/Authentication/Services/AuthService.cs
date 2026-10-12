using System.Net.Mail;
using LgsTakip.Api.Application.Authentication.Contracts;
using LgsTakip.Api.Application.Authentication.Interfaces;
using LgsTakip.Api.Application.Authentication.Results;
using LgsTakip.Api.Domain.Entities;
using LgsTakip.Api.Domain.Enums;
using LgsTakip.Api.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LgsTakip.Api.Application.Authentication.Services;

public sealed class AuthService(
    LgsTakipDbContext dbContext,
    IPasswordHasher<User> passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator) : IAuthService
{
    public async Task<AuthResult> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!HasValidRegistrationInput(request))
        {
            return AuthResult.Failure(AuthErrorCode.InvalidInput);
        }

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();
        var normalizedEmail = NormalizeEmail(email);

        var emailExists = await dbContext.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (emailExists)
        {
            return AuthResult.Failure(AuthErrorCode.EmailAlreadyExists);
        }

        if (!IsAllowedRole(request.Role))
        {
            return AuthResult.Failure(AuthErrorCode.InvalidRole);
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName,
            Role = request.Role,
            IsActive = true,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return AuthResult.Failure(AuthErrorCode.EmailAlreadyExists);
        }

        return AuthResult.Success(CreateAuthResponse(user));
    }

    public async Task<AuthResult> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrEmpty(request.Password))
        {
            return AuthResult.Failure(AuthErrorCode.InvalidInput);
        }

        var normalizedEmail = NormalizeEmail(request.Email.Trim());
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            return AuthResult.Failure(AuthErrorCode.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return AuthResult.Failure(AuthErrorCode.InactiveUser);
        }

        var verificationResult = passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            request.Password);

        if (verificationResult is PasswordVerificationResult.Failed)
        {
            return AuthResult.Failure(AuthErrorCode.InvalidCredentials);
        }

        if (verificationResult is PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return AuthResult.Success(CreateAuthResponse(user));
    }

    private AuthResponse CreateAuthResponse(User user)
    {
        var (accessToken, expiresAtUtc) = jwtTokenGenerator.Generate(user);

        return new AuthResponse
        {
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc,
            User = new AuthenticatedUserResponse
            {
                Id = user.Id,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role
            }
        };
    }

    private static bool HasValidRegistrationInput(RegisterRequest? request)
    {
        if (request is null
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.DisplayName)
            || string.IsNullOrEmpty(request.Password))
        {
            return false;
        }

        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();

        return email.Length <= 320
            && displayName.Length <= 200
            && IsValidEmail(email)
            && IsValidPassword(request.Password);
    }

    private static bool IsValidEmail(string email)
    {
        return MailAddress.TryCreate(email, out var mailAddress)
            && string.Equals(
                mailAddress.Address,
                email,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidPassword(string password)
    {
        return password.Length >= 10
            && password.Any(char.IsLetter)
            && password.Any(char.IsDigit);
    }

    private static bool IsAllowedRole(UserRole role)
    {
        return role is UserRole.Student or UserRole.Parent;
    }

    private static string NormalizeEmail(string email)
    {
        return email.ToUpperInvariant();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.InnerException is SqlException { Number: 2601 or 2627 };
    }
}
