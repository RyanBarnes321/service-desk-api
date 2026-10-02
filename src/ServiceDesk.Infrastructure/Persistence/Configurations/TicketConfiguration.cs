using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence.Configurations;

public class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");

        builder.HasKey(ticket => ticket.Id)
            .HasName("pk_tickets");

        builder.Property(ticket => ticket.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(ticket => ticket.Title)
            .HasColumnName("title")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(ticket => ticket.Description)
            .HasColumnName("description")
            .HasMaxLength(10_000)
            .IsRequired();

        builder.Property(ticket => ticket.Category)
            .HasColumnName("category")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(ticket => ticket.Priority)
            .HasColumnName("priority")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(ticket => ticket.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(ticket => ticket.CreatedByUserId)
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(ticket => ticket.AssignedTechnicianId)
            .HasColumnName("assigned_technician_id");

        builder.Property(ticket => ticket.ResolutionSummary)
            .HasColumnName("resolution_summary")
            .HasMaxLength(5_000);

        builder.Property(ticket => ticket.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(ticket => ticket.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(ticket => ticket.ResolvedAt)
            .HasColumnName("resolved_at");

        builder.Property(ticket => ticket.ClosedAt)
            .HasColumnName("closed_at");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tickets_users_created_by_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedTechnicianId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tickets_users_assigned_technician_id");

        builder.HasIndex(ticket => ticket.Status)
            .HasDatabaseName("ix_tickets_status");

        builder.HasIndex(ticket => ticket.AssignedTechnicianId)
            .HasDatabaseName("ix_tickets_assigned_technician_id");

        builder.HasIndex(ticket => ticket.CreatedByUserId)
            .HasDatabaseName("ix_tickets_created_by_user_id");

        builder.HasIndex(ticket => ticket.CreatedAt)
            .HasDatabaseName("ix_tickets_created_at");
    }
}
