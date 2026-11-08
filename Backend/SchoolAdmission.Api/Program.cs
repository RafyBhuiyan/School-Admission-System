using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolAdmission.Api.Common;
using SchoolAdmission.Api.Configuration;
using SchoolAdmission.Api.Data;
using SchoolAdmission.Api.Middleware;
using SchoolAdmission.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<DatabaseOptions>()
    .BindConfiguration(DatabaseOptions.SectionName)
    .Validate(options => options.IsValid(),
        "Set ConnectionStrings__DefaultConnection to a valid PostgreSQL connection string with Host and Database.")
    .ValidateOnStart();

builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IOptions<DatabaseOptions>>().Value.DefaultConnection));
builder.Services.AddScoped<ClassService>();
builder.Services.AddScoped<HoldService>();
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
