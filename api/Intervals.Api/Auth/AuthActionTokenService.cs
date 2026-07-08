using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Intervals.Api.Data;
using Intervals.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intervals.Api.Auth;

public sealed class AuthActionTokenService(
    IntervalsDbContext db,
    TimeProvider timeProvider,
    ILogger<AuthActionTokenService> logger)
{
    private const int TokenByteLength = 32;

    public async Task<string> IssueAsync(
        Guid userId,
        string purpose,
        string? email,
        TimeSpan lifetime,
        string? correlationId = null,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var normalized = AuthEmail.Normalize(email, int.MaxValue);
        var rawBytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var publicToken = Base64UrlEncoder.Encode(rawBytes);
        var tokenHash = Base64UrlEncoder.Encode(SHA256.HashData(rawBytes));

        await RevokeActiveAsync(userId, purpose, normalized, now, ct);

        var token = new AuthActionToken
        {
            UserId = userId,
            Purpose = purpose,
            TokenHash = tokenHash,
            Email = email is null ? null : email.Trim().ToLowerInvariant(),
            EmailNormalized = normalized,
            CreatedUtc = now,
            ExpiresUtc = now + lifetime,
            CorrelationId = correlationId,
        };

        db.AuthActionTokens.Add(token);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Issued {Purpose} token for user {UserId}.",
            purpose,
            userId);

        return publicToken;
    }

    public async Task<AuthActionToken?> ValidateAsync(
        string purpose,
        string rawToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken))
        {
            return null;
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Base64UrlEncoder.Decode(rawToken);
        }
        catch (FormatException)
        {
            return null;
        }

        var tokenHash = Base64UrlEncoder.Encode(SHA256.HashData(rawBytes));
        var token = await db.AuthActionTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Purpose == purpose && t.TokenHash == tokenHash, ct);

        if (token is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (token.RevokedUtc is not null)
        {
            return null;
        }

        if (token.ConsumedUtc is not null)
        {
            return null;
        }

        if (token.ExpiresUtc < now)
        {
            return null;
        }

        return token;
    }

    public async Task<AuthActionToken?> ConsumeAsync(
        string purpose,
        string rawToken,
        CancellationToken ct = default)
    {
        var token = await ValidateAsync(purpose, rawToken, ct);
        if (token is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var affected = await db.AuthActionTokens
            .Where(t => t.Id == token.Id && t.ConsumedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedUtc, (DateTimeOffset?)now), ct);

        if (affected == 0)
        {
            return null;
        }

        token.ConsumedUtc = now;
        return token;
    }

    public async Task RevokeAsync(
        Guid userId,
        string purpose,
        string? email,
        CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow();
        var normalized = AuthEmail.Normalize(email, int.MaxValue);
        await RevokeActiveAsync(userId, purpose, normalized, now, ct);
    }

    private async Task RevokeActiveAsync(
        Guid userId,
        string purpose,
        string? normalized,
        DateTimeOffset now,
        CancellationToken ct)
    {
        IQueryable<AuthActionToken> query = db.AuthActionTokens
            .Where(t => t.UserId == userId
                && t.Purpose == purpose
                && t.ConsumedUtc == null
                && t.RevokedUtc == null);

        query = normalized is null
            ? query.Where(t => t.EmailNormalized == null)
            : query.Where(t => t.EmailNormalized == normalized);

        await query.ExecuteUpdateAsync(
            s => s.SetProperty(t => t.RevokedUtc, (DateTimeOffset?)now),
            ct);
    }

    internal static class Base64UrlEncoder
    {
        public static string Encode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public static byte[] Decode(string value)
        {
            var builder = new System.Text.StringBuilder(value.Length + 2);
            foreach (var c in value)
            {
                builder.Append(c switch
                {
                    '-' => '+',
                    '_' => '/',
                    _ => c,
                });
            }

            switch (builder.Length % 4)
            {
                case 2:
                    builder.Append("==");
                    break;
                case 3:
                    builder.Append('=');
                    break;
                case 1:
                    throw new FormatException("Invalid base64url input length.");
            }

            return Convert.FromBase64String(builder.ToString());
        }
    }
}
