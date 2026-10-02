using ServiceDesk.Core.Entities;

namespace ServiceDesk.Core.Tests.Entities;

public class TicketCommentTests
{
    private static readonly Guid TicketId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AuthorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    [Fact]
    public void Create_WithValidValues_CreatesCommentWithoutNormalizingContent()
    {
        const string content = "  Please restart the affected workstation.  ";

        var comment = TicketComment.Create(TicketId, AuthorUserId, content, CreatedAt);

        Assert.NotEqual(Guid.Empty, comment.Id);
        Assert.Equal(TicketId, comment.TicketId);
        Assert.Equal(AuthorUserId, comment.AuthorUserId);
        Assert.Equal(content, comment.Content);
        Assert.Equal(CreatedAt, comment.CreatedAt);
    }

    [Fact]
    public void Create_WithEmptyTicketId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TicketComment.Create(Guid.Empty, AuthorUserId, "Content", CreatedAt));
    }

    [Fact]
    public void Create_WithEmptyAuthorUserId_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            TicketComment.Create(TicketId, Guid.Empty, "Content", CreatedAt));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithInvalidContent_ThrowsArgumentException(string? content)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            TicketComment.Create(TicketId, AuthorUserId, content!, CreatedAt));
    }
}
