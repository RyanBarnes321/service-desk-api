using ServiceDesk.Core.Application.Authentication;
using ServiceDesk.Core.Entities;
using ServiceDesk.Core.Enums;

namespace ServiceDesk.Core.Application.Tickets.Comments;

public sealed class CreateTicketCommentUseCase(
    ICreateTicketCommentPersistence persistence,
    TimeProvider timeProvider)
{
    public async Task<CreateTicketCommentResult> ExecuteAsync(
        Guid ticketId,
        string? content,
        RequestActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (ticketId == Guid.Empty)
        {
            throw new TicketCommentsValidationException(
                "Ticket ID cannot be empty.",
                nameof(ticketId));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var visibility = TicketVisibilityScope.For(actor);
        var target = await persistence.FindVisibleTicketAsync(
            ticketId,
            visibility,
            cancellationToken);

        if (target is null)
        {
            return new(CreateTicketCommentOutcome.TicketNotFound);
        }

        if (target.Status == TicketStatus.Closed)
        {
            return new(CreateTicketCommentOutcome.TicketClosed);
        }

        ValidateContent(content);
        var comment = TicketComment.Create(
            target.Id,
            actor.UserId,
            content!,
            timeProvider.GetUtcNow());
        await persistence.PersistAsync(comment, cancellationToken);

        return new(
            CreateTicketCommentOutcome.Success,
            new TicketCommentResult(
                comment.Id,
                comment.TicketId,
                comment.AuthorUserId,
                comment.Content,
                comment.CreatedAt));
    }

    private static void ValidateContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new TicketCommentsValidationException(
                "Comment content is required.",
                nameof(content));
        }

        if (content.Length > TicketComment.MaximumContentLength)
        {
            throw new TicketCommentsValidationException(
                $"Comment content cannot exceed {TicketComment.MaximumContentLength} characters.",
                nameof(content));
        }
    }
}
