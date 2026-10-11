using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ChatApp.Infrastructure.Data.Entities;
using Microsoft.IdentityModel.Tokens;

namespace ChatApp.Api.Features.Auth;

public interface ITokenService
{
    string GenerateToken(User user, Profile profile);
    string GeneratePasswordResetToken(User user);
    Guid? ValidatePasswordResetToken(string token);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateToken(User user, Profile profile)
    {
        var secret = _configuration["Authentication:Jwt:SecretKey"]
            ?? _configuration["Authentication:Supabase:JwtSecret"]
            ?? "default_super_secret_key_minimum_32_characters_long_for_chatapp_jwt_validation";

        var issuer = _configuration["Authentication:Jwt:Issuer"]
            ?? _configuration["Authentication:Supabase:Issuer"]
            ?? "ChatApp.Api";

        var audience = _configuration["Authentication:Jwt:Audience"]
            ?? _configuration["Authentication:Supabase:Audience"]
            ?? "authenticated";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.Name, profile.Username),
            new("display_name", profile.DisplayName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GeneratePasswordResetToken(User user)
    {
        var secret = _configuration["Authentication:Jwt:SecretKey"]
            ?? _configuration["Authentication:Supabase:JwtSecret"]
            ?? "default_super_secret_key_minimum_32_characters_long_for_chatapp_jwt_validation";

        var issuer = _configuration["Authentication:Jwt:Issuer"]
            ?? _configuration["Authentication:Supabase:Issuer"]
            ?? "ChatApp.Api";

        var audience = _configuration["Authentication:Jwt:Audience"]
            ?? _configuration["Authentication:Supabase:Audience"]
            ?? "authenticated";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new("type", "password_reset"),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(15),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public Guid? ValidatePasswordResetToken(string token)
    {
        var secret = _configuration["Authentication:Jwt:SecretKey"]
            ?? _configuration["Authentication:Supabase:JwtSecret"]
            ?? "default_super_secret_key_minimum_32_characters_long_for_chatapp_jwt_validation";

        var issuer = _configuration["Authentication:Jwt:Issuer"]
            ?? _configuration["Authentication:Supabase:Issuer"]
            ?? "ChatApp.Api";

        var audience = _configuration["Authentication:Jwt:Audience"]
            ?? _configuration["Authentication:Supabase:Audience"]
            ?? "authenticated";

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

        var tokenHandler = new JwtSecurityTokenHandler();
        try
        {
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            var jwtToken = (JwtSecurityToken)validatedToken;
            
            var typeClaim = jwtToken.Claims.FirstOrDefault(x => x.Type == "type")?.Value;
            if (typeClaim != "password_reset")
            {
                return null;
            }

            var userId = Guid.Parse(jwtToken.Claims.First(x => x.Type == JwtRegisteredClaimNames.Sub).Value);
            return userId;
        }
        catch
        {
            return null;
        }
    }
}
