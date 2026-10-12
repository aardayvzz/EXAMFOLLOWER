using System.ComponentModel.DataAnnotations;

namespace LgsTakip.Api.Application.StudySessions.Contracts;

public sealed class StartStudySessionRequest
{
    [Range(5, 180)]
    public int PlannedDurationMinutes { get; init; }
}
