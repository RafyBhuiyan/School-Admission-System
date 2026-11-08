using Microsoft.EntityFrameworkCore;

namespace SchoolAdmission.Api.Data;

// Entities, constraints, and migrations are introduced in step 3.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
