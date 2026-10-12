using LgsTakip.Api.Domain.Entities;

namespace LgsTakip.Api.Application.Authentication.Interfaces;

public interface IJwtTokenGenerator
{
    (string AccessToken, DateTimeOffset ExpiresAtUtc) Generate(User user);
}
