using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace PacketPeek;

public record LoginRequest(string Username, string Password);
public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(1024);
    }
}

public static class Auth
{
    public static string Token(string userId, IConfiguration configuration)
    {
        var token = new JwtSecurityToken("PacketPeek", "PacketPeek",
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, "Operator")],
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]!)),
                SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static void MapAuth(this WebApplication app) => app.MapPost("/api/auth/token",
        async (LoginRequest request, IValidator<LoginRequest> validator, UserManager<User> users, IConfiguration config) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            var user = await users.FindByNameAsync(request.Username);
            if (user is null || await users.IsLockedOutAsync(user)) return Results.Unauthorized();
            if (!await users.CheckPasswordAsync(user, request.Password))
            {
                await users.AccessFailedAsync(user); return Results.Unauthorized();
            }
            if (!await users.IsInRoleAsync(user, "Operator")) return Results.Unauthorized();
            await users.ResetAccessFailedCountAsync(user);
            return Results.Ok(new { accessToken = Token(user.Id, config), expiresIn = 3600 });
        }).AllowAnonymous();

    public static async Task InitializeDatabase(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:Migrate")) return;
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        var username = app.Configuration["Operator:Username"];
        var password = app.Configuration["Operator:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            throw new InvalidOperationException("Operator:Username and Operator:Password are required when Database:Migrate is enabled.");
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        if (!await roles.RoleExistsAsync("Operator")) Check(await roles.CreateAsync(new IdentityRole("Operator")));
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByNameAsync(username);
        if (user is null)
        {
            user = new User { UserName = username, LockoutEnabled = true };
            Check(await users.CreateAsync(user, password));
        }
        if (!await users.IsInRoleAsync(user, "Operator")) Check(await users.AddToRoleAsync(user, "Operator"));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
