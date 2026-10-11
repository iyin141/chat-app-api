using System.Collections.Concurrent;

namespace ChatApp.Api.Features.Auth;

public interface ITokenBlacklistService
{
    void RevokeToken(string jti, DateTimeOffset expiresAt);
    bool IsRevoked(string jti);
}

public class TokenBlacklistService : ITokenBlacklistService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

    public void RevokeToken(string jti, DateTimeOffset expiresAt)
    {
        if (string.IsNullOrWhiteSpace(jti)) return;
        _revokedTokens[jti] = expiresAt;
        CleanupExpired();
    }

    public bool IsRevoked(string jti)
    {
        if (string.IsNullOrWhiteSpace(jti)) return false;
        if (_revokedTokens.TryGetValue(jti, out var expiresAt))
        {
            if (expiresAt > DateTimeOffset.UtcNow)
            {
                return true;
            }
            _revokedTokens.TryRemove(jti, out _);
        }
        return false;
    }

    private void CleanupExpired()
    {
        if (_revokedTokens.Count > 1000)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _revokedTokens)
            {
                if (kvp.Value <= now)
                {
                    _revokedTokens.TryRemove(kvp.Key, out _);
                }
            }
        }
    }
}
