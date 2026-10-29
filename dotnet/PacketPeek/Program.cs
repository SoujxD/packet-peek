using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PacketPeek;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, config) => config
    .WriteTo.Console(new CompactJsonFormatter())
    .WriteTo.File(new CompactJsonFormatter(), "logs/packetpeek-.json", rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7));
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Postgres") ?? "Host=localhost;Database=packetpeek;Username=packetpeek"));
builder.Services.AddIdentityCore<IdentityUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<AppDbContext>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    var signingKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrEmpty(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        throw new InvalidOperationException("Jwt:Key must contain at least 32 UTF-8 bytes.");
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = "PacketPeek",
        ValidateAudience = true, ValidAudience = "PacketPeek",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(10)
    };
});
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).ValidateOnStart();
builder.Services.AddAuthorization(options => options.AddPolicy("Operator", policy => policy.RequireAuthenticatedUser().RequireRole("Operator")));
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var app = builder.Build();
app.UseSerilogRequestLogging();
app.UseSwagger(); app.UseSwaggerUI();
app.UseAuthentication(); app.UseAuthorization();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.Run();

public partial class Program { }
