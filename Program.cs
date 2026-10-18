using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

const string DevToken = "my-secret-dev-token";

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = app.Logger;
        logger.LogError(ex, "Unhandled exception occurred.");

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("{\"error\":\"An unexpected error occurred. Please try again later.\"}");
    }
});

app.Use(async (context, next) =>
{
    var logger = app.Logger;
    var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();

    if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Missing or invalid Authorization header.");
        logger.LogWarning("Authentication failed: Missing or invalid header for {Path}", context.Request.Path);
        return;
    }

    var token = authHeader.Substring("Bearer ".Length).Trim();
    if (!token.Equals(DevToken))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Invalid token.");
        logger.LogWarning("Authentication failed: Invalid token for {Path}", context.Request.Path);
        return;
    }

    await next();
});

app.Use(async (context, next) =>
{
    var logger = app.Logger;
    var stopwatch = Stopwatch.StartNew();

    await next();

    stopwatch.Stop();
    logger.LogInformation("Request {Method} {Path} responded {StatusCode} in {Elapsed}ms",
        context.Request.Method,
        context.Request.Path,
        context.Response.StatusCode,
        stopwatch.ElapsedMilliseconds);
});

var users = new List<User>();

bool IsValidUser(User user, out string errorMessage)
{
    if (string.IsNullOrWhiteSpace(user.Name) ||
        string.IsNullOrWhiteSpace(user.Email) ||
        string.IsNullOrWhiteSpace(user.Department))
    {
        errorMessage = "Name, Email, and Department are required.";
        return false;
    }

    var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase);
    if (!emailRegex.IsMatch(user.Email))
    {
        errorMessage = "Invalid email format.";
        return false;
    }

    errorMessage = string.Empty;
    return true;
}

app.MapGet("/api/users", () => Results.Ok(users));

app.MapGet("/api/users/{id}", (int id) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    return user is not null ? Results.Ok(user) : Results.NotFound();
});

app.MapPost("/api/users", (User user) =>
{
    if (!IsValidUser(user, out var errorMessage))
    {
        return Results.BadRequest(errorMessage);
    }

    if (users.Any(u => u.Email.Equals(user.Email, System.StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest("Email already exists.");
    }

    user.Id = users.Count == 0 ? 1 : users.Max(u => u.Id) + 1;
    users.Add(user);
    return Results.Created($"/api/users/{user.Id}", user);
});

app.MapPut("/api/users/{id}", (int id, User updatedUser) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    if (!IsValidUser(updatedUser, out var errorMessage))
    {
        return Results.BadRequest(errorMessage);
    }

    if (users.Any(u => u.Id != id && u.Email.Equals(updatedUser.Email, System.StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest("Email already exists.");
    }

    user.Name = updatedUser.Name;
    user.Email = updatedUser.Email;
    user.Department = updatedUser.Department;

    return Results.Ok(user);
});

app.MapDelete("/api/users/{id}", (int id) =>
{
    var user = users.FirstOrDefault(u => u.Id == id);
    if (user is null)
    {
        return Results.NotFound();
    }

    users.Remove(user);
    return Results.NoContent();
});

app.Run();

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
}
