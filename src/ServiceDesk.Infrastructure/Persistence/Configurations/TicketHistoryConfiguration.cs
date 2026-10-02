using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence.Configurations;

public class TicketHistoryConfiguration : IEntityTypeConfiguration<TicketHistory>
{
    public void Configure(EntityTypeBuilder<TicketHistory> builder)
    {
        builder.ToTable("ticket_history");

        builder.HasKey(history => history.Id)
            .HasName("pk_ticket_history");

        builder.Property(history => history.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(history => history.TicketId)
            .HasColumnName("ticket_id")
            .IsRequired();

        builder.Property(history => history.PerformedByUserId)
            .HasColumnName("performed_by_user_id");

        builder.Property(history => history.EventType)
            .HasColumnName("event_type")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(history => history.OldValue)
            .HasColumnName("old_value")
            .HasMaxLength(5_000);

        builder.Property(history => history.NewValue)
            .HasColumnName("new_value")
            .HasMaxLength(5_000);

        builder.Property(history => history.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(history => history.TicketId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ticket_history_tickets_ticket_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(history => history.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ticket_history_users_performed_by_user_id");

        builder.HasIndex(history => new { history.TicketId, history.CreatedAt })
            .HasDatabaseName("ix_ticket_history_ticket_id_created_at");
    }
}
