using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServiceDesk.Core.Entities;

namespace ServiceDesk.Infrastructure.Persistence.Configurations;

public class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");

        builder.HasKey(comment => comment.Id)
            .HasName("pk_ticket_comments");

        builder.Property(comment => comment.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(comment => comment.TicketId)
            .HasColumnName("ticket_id")
            .IsRequired();

        builder.Property(comment => comment.AuthorUserId)
            .HasColumnName("author_user_id")
            .IsRequired();

        builder.Property(comment => comment.Content)
            .HasColumnName("content")
            .HasMaxLength(5_000)
            .IsRequired();

        builder.Property(comment => comment.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(comment => comment.TicketId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ticket_comments_tickets_ticket_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_ticket_comments_users_author_user_id");

        builder.HasIndex(comment => new { comment.TicketId, comment.CreatedAt })
            .HasDatabaseName("ix_ticket_comments_ticket_id_created_at");
    }
}
