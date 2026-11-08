using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Api.Data.Configurations;

public sealed class AdmissionClassConfiguration : IEntityTypeConfiguration<AdmissionClass>
{
    public void Configure(EntityTypeBuilder<AdmissionClass> builder)
    {
        builder.ToTable("AdmissionClasses", table =>
            table.HasCheckConstraint("ck_admission_classes_positive_seats", "total_seats > 0"));

        builder.HasKey(entity => entity.Id).HasName("pk_admission_classes");
        builder.Property(entity => entity.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entity => entity.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(entity => entity.TotalSeats).HasColumnName("total_seats").IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("clock_timestamp()");
        builder.Property(entity => entity.UpdatedAt).HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("clock_timestamp()");

        builder.HasIndex(entity => entity.Name).IsUnique().HasDatabaseName("ux_admission_classes_name");
    }
}
