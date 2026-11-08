using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SchoolAdmission.Api.Models.Entities;

namespace SchoolAdmission.Api.Data.Configurations;

public sealed class AdmissionHoldConfiguration : IEntityTypeConfiguration<AdmissionHold>
{
    public void Configure(EntityTypeBuilder<AdmissionHold> builder)
    {
        builder.ToTable("AdmissionHolds", table =>
            table.HasCheckConstraint("ck_admission_holds_status",
                "status IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')"));

        builder.HasKey(entity => entity.Id).HasName("pk_admission_holds");
        builder.Property(entity => entity.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(entity => entity.ClassId).HasColumnName("class_id").IsRequired();
        builder.Property(entity => entity.StudentId).HasColumnName("student_id").HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.Status).HasColumnName("status").HasConversion<string>()
            .HasMaxLength(16).IsRequired();
        builder.Property(entity => entity.CreatedAt).HasColumnName("created_at")
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("clock_timestamp()");
        builder.Property(entity => entity.ExpiresAt).HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(entity => entity.ConfirmedAt).HasColumnName("confirmed_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(entity => entity.CancelledAt).HasColumnName("cancelled_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne(entity => entity.AdmissionClass).WithMany(entity => entity.Holds)
            .HasForeignKey(entity => entity.ClassId).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_admission_holds_class");

        builder.HasIndex(entity => new { entity.ClassId, entity.Status, entity.ExpiresAt })
            .HasDatabaseName("ix_admission_holds_class_status_expiry");
        builder.HasIndex(entity => new { entity.StudentId, entity.CreatedAt })
            .HasDatabaseName("ix_admission_holds_student_created");
        builder.HasIndex(entity => new { entity.ClassId, entity.StudentId }).IsUnique()
            .HasFilter("status IN ('HELD', 'CONFIRMED')")
            .HasDatabaseName("ux_admission_holds_class_student_active");
    }
}
