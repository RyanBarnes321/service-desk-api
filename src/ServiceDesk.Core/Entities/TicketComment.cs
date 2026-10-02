namespace ServiceDesk.Core.Entities;

public class TicketComment
{
    private TicketComment(
        Guid id,
        Guid ticketId,
        Guid authorUserId,
        string content,
        DateTimeOffset createdAt)
    {
        Id = id;
        TicketId = ticketId;
        AuthorUserId = authorUserId;
        Content = content;
        CreatedAt = createdAt;
    }

    public Guid Id { get; }

    public Guid TicketId { get; }

    public Guid AuthorUserId { get; }

    public string Content { get; }

    public DateTimeOffset CreatedAt { get; }

    public static TicketComment Create(
        Guid ticketId,
        Guid authorUserId,
        string content,
        DateTimeOffset createdAt)
    {
        if (ticketId == Guid.Empty)
        {
            throw new ArgumentException("Ticket ID cannot be empty.", nameof(ticketId));
        }

        if (authorUserId == Guid.Empty)
        {
            throw new ArgumentException("Author user ID cannot be empty.", nameof(authorUserId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        return new TicketComment(
            Guid.NewGuid(),
            ticketId,
            authorUserId,
            content,
            createdAt);
    }
}
