using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration["FabrykaAPI:ConnectionStrings:CloudConnection"];
builder.Services.AddDbContext<FabrykaDb>(options =>
    options.UseNpgsql(connectionString));

// Add JWT configuration
builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey
            (Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true
    };
});

builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo()
    {
        Version = "v1",
        Title = "Minimal API - JWT Authentication with Swagger demo",
        Description = "Implementing JWT Authentication in Minimal API",
    });

    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme()
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JSON Web Token based security, Enter: Bearer [token_value]",
    });

    o.AddSecurityRequirement(new OpenApiSecurityRequirement()
    {
    {
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        new string[] {}
    }
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();


var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
       new WeatherForecast
       (
           DateTime.Now.AddDays(index),
           Random.Shared.Next(-20, 55),
           summaries[Random.Shared.Next(summaries.Length)]
       ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/hala", [Authorize] async (FabrykaDb db) =>
    await db.HalaSet.ToListAsync());

app.MapGet("/hala/{id}", [Authorize] async (int id, FabrykaDb db) =>
    await db.HalaSet.FindAsync(id)
        is Hala hala
            ? Results.Ok(hala)
            : Results.NotFound());

app.MapPost("/hala", [Authorize] async (Hala hala, FabrykaDb db) =>
{
    db.HalaSet.Add(hala);
    await db.SaveChangesAsync();

    return Results.Created($"/hala/{hala.Id}", hala);
});

app.MapPut("/hala/{id}", [Authorize] async (int id, Hala hala, FabrykaDb db) =>
{
    var halaStara = await db.HalaSet.FindAsync(id);

    if (halaStara is null)
        return Results.NotFound();

    halaStara.Nazwa = hala.Nazwa;
    halaStara.Adres = hala.Adres;

    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapDelete("/hala/{id}", [Authorize] async (int id, FabrykaDb db) =>
{
    if (await db.HalaSet.FindAsync(id) is Hala hala)
    {
        db.HalaSet.Remove(hala);
        await db.SaveChangesAsync();
        return Results.Ok(hala);
    }

    return Results.NotFound();
});

app.MapPost("/security/getToken", [AllowAnonymous] (UserDto user) =>
{

    if (user.UserName == "admin@fabryka.com" && user.Password == "P@ssword")
    {
        var issuer = builder.Configuration["Jwt:Issuer"];
        var audience = builder.Configuration["Jwt:Audience"];
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Now its ime to define the jwt token which will be responsible of creating our tokens
        var jwtTokenHandler = new JwtSecurityTokenHandler();

        // We get our secret from the appsettings
        var key = Encoding.ASCII.GetBytes(builder.Configuration["Jwt:Key"]);

        // we define our token descriptor
        // We need to utilise claims which are properties in our token which gives information about the token
        // which belong to the specific user who it belongs to
        // so it could contain their id, name, email the good part is that these information
        // are generated by our server and identity framework which is valid and trusted
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("Id", "1"),
                new Claim(JwtRegisteredClaimNames.Sub, user.UserName),
                new Claim(JwtRegisteredClaimNames.Email, user.UserName),
                // the JTI is used for our refresh token which we will be convering in the next video
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            }),
            // the life span of the token needs to be shorter and utilise refresh token to keep the user signedin
            // but since this is a demo app we can extend it to fit our current need
            Expires = DateTime.UtcNow.AddHours(6),
            Audience = audience,
            Issuer = issuer,
            // here we are adding the encryption alogorithim information which will be used to decrypt our token
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha512Signature)
        };

        var token = jwtTokenHandler.CreateToken(tokenDescriptor);

        var jwtToken = jwtTokenHandler.WriteToken(token);

        return Results.Ok(jwtToken);
    }
    else
    {
        return Results.Unauthorized();
    }
});

app.Run();

internal record WeatherForecast(DateTime Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public class FabrykaDb : DbContext
{
    public FabrykaDb(DbContextOptions<FabrykaDb> options)
        : base(options)
    {
    }

    public DbSet<Hala> HalaSet { get; set; }

}

public class Hala
{
    [Key]
    public int Id { get; set; }
    public string Nazwa { get; set; } = String.Empty;
    public string? Adres { get; set; }

    //public virtual ICollection<Maszyna>? Maszyny { get; set; }
}

record UserDto(string UserName, string Password);