using System.Security.Claims;
using ChatApp.Api.Features.Auth;
using ChatApp.Infrastructure.Data;
using ChatApp.Infrastructure.Data.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ChatApp.Tests.Unit;

public class AuthControllerTests
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Jwt:SecretKey"] = "test_super_secret_key_minimum_32_characters_for_unit_tests",
                ["Authentication:Jwt:Issuer"] = "ChatApp.Api",
                ["Authentication:Jwt:Audience"] = "authenticated"
            })
            .Build();

        _tokenService = new TokenService(config);
        _passwordHasher = new PasswordHasher<User>();
        _controller = new AuthController(_db, _tokenService, _passwordHasher, new TokenBlacklistService());
    }

    [Fact]
    public async Task Register_ValidData_CreatesUserAndProfile_ReturnsToken()
    {
        var request = new RegisterRequest("alice@example.com", "Password123!", "alice", "Alice Wonder");

        var result = await _controller.Register(request);

        result.Should().BeOfType<ObjectResult>();
        var objResult = (ObjectResult)result;
        objResult.StatusCode.Should().Be(201);

        var response = objResult.Value as AuthResponse;
        response.Should().NotBeNull();
        response!.Token.Should().NotBeNullOrEmpty();
        response.User.Email.Should().Be("alice@example.com");
        response.User.Username.Should().Be("alice");
        response.User.DisplayName.Should().Be("Alice Wonder");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == "alice@example.com");
        user.Should().NotBeNull();
        var profile = await _db.Profiles.FindAsync(user!.Id);
        profile.Should().NotBeNull();
        profile!.Username.Should().Be("alice");
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        await _controller.Register(new RegisterRequest("bob@example.com", "Pass123!", "bob"));

        var result = await _controller.Register(new RegisterRequest("bob@example.com", "Pass456!", "bob2"));

        result.Should().BeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenAndUser()
    {
        await _controller.Register(new RegisterRequest("carol@example.com", "SecretPass123!", "carol"));

        var result = await _controller.Login(new LoginRequest("carol@example.com", "SecretPass123!"));

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var response = okResult.Value as AuthResponse;
        response.Should().NotBeNull();
        response!.Token.Should().NotBeNullOrEmpty();
        response.User.Email.Should().Be("carol@example.com");
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsUnauthorized()
    {
        await _controller.Register(new RegisterRequest("dan@example.com", "CorrectPass!", "dan"));

        var result = await _controller.Login(new LoginRequest("dan@example.com", "WrongPass!"));

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task GetCurrentUser_AuthenticatedUser_ReturnsProfile()
    {
        var regResult = (ObjectResult)await _controller.Register(new RegisterRequest("eve@example.com", "Pass123!", "eve"));
        var authResponse = (AuthResponse)regResult.Value!;

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, authResponse.User.Id.ToString()),
                    new Claim("sub", authResponse.User.Id.ToString())
                }, "TestAuth"))
            }
        };

        var result = await _controller.GetCurrentUser();

        result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result;
        var userDto = okResult.Value as UserDto;
        userDto.Should().NotBeNull();
        userDto!.Email.Should().Be("eve@example.com");
        userDto.Username.Should().Be("eve");
    }
}
