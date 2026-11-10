using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Common;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;
using SchoolAdmission.Api.BackgroundServices;

var healthCheck = args is ["--healthcheck"];
var builder = WebApplication.CreateBuilder(healthCheck ? [] : args);

if (healthCheck)
{
    // Probe the normal database-backed route without starting another API host.
    try
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            BaseAddress = ApiHealthProbe.ResolveBaseAddress(builder.Configuration),
            Timeout = TimeSpan.FromSeconds(2)
        };
        using var response = await client.GetAsync("classes", HttpCompletionOption.ResponseHeadersRead);
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or ArgumentException or FormatException)
    {
        Environment.ExitCode = 1;
    }
    return;
}

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .Validate(options => options.IsValid(),
        "Set ConnectionStrings__DefaultConnection to a valid PostgreSQL connection string with Host and Database.")
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection));
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
builder.Services.AddOptions<ExpirationOptions>()
    .BindConfiguration("Expiration")
    .Validate(options => options.IntervalMilliseconds is >= 100 and <= 60000,
        "Expiration:IntervalMilliseconds must be between 100 and 60000.")
    .Validate(options => options.BatchSize is >= 1 and <= 1000,
        "Expiration:BatchSize must be between 1 and 1000.")
    .ValidateOnStart();
builder.Services.AddScoped<HoldExpirationService>();
builder.Services.AddHostedService<HoldExpirationWorker>();
builder.Services.AddOptions<HoldOptions>()
    .Configure<IConfiguration>((options, configuration) =>
        options.DurationSeconds = int.TryParse(configuration[HoldOptions.DurationKey] ?? "600",
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var seconds) ? seconds : 0)
    .Validate(options => options.DurationSeconds > 0, "HOLD_DURATION_SECONDS must be a positive 32-bit integer.")
    .ValidateOnStart();

builder.Services.AddOptions<FrontendCorsOptions>()
    .BindConfiguration(FrontendCorsOptions.SectionName)
    .Validate(options => options.IsValid(),
        "Cors:AllowedOrigins must contain explicit HTTP(S) origins without paths, trailing slashes, or wildcards.")
    .ValidateOnStart();

builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>()
    .Configure<IOptions<FrontendCorsOptions>>((options, settings) =>
        options.AddPolicy(FrontendCorsOptions.PolicyName, policy =>
            policy.WithOrigins(settings.Value.AllowedOrigins)
                .WithMethods("GET", "POST", "DELETE")
                .WithHeaders("Content-Type", "X-Student-Id")));

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = context =>
        ApiProblemDetails.Enrich(context.ProblemDetails, context.HttpContext));
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers()
    .AddJsonOptions(options => options.AllowInputFormatterExceptionMessages = false);
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages(async (Microsoft.AspNetCore.Diagnostics.StatusCodeContext context) =>
{
    var httpContext = context.HttpContext;
    var problem = ApiProblemDetails.Create(httpContext, httpContext.Response.StatusCode);
    await httpContext.Response.WriteAsJsonAsync(problem, options: null,
        contentType: "application/problem+json", cancellationToken: httpContext.RequestAborted);
});
app.UseRouting();
app.UseCors(FrontendCorsOptions.PolicyName);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
        options.SwaggerEndpoint("/openapi/v1.json", "School Admission API v1"));
}

app.MapControllers();

app.Run();

// Expose the real application entry point to the in-process integration test host.
public partial class Program;
