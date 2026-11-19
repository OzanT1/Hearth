using Hearth.Api.Features;
using Hearth.Domain;
using Hearth.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Hearth.Tests;

public sealed class RulesTests
{
    [Fact]
    public void Committed_migration_matches_runtime_model_without_database_connection()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=model_validation;Username=unused;Password=unused")
            .Options;
        using var db = new AppDbContext(options);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, true, false, false, true)]
    [InlineData(false, true, false, true, false)]
    [InlineData(false, false, false, false, false)]
    public void Moderation_respects_owner_and_moderator_boundaries(bool owner, bool actorModerator, bool targetOwner, bool targetModerator, bool expected)
        => Assert.Equal(expected, Rules.CanModerate(owner, actorModerator, targetOwner, targetModerator));

    [Fact]
    public void Invitations_expire_at_boundary_and_enforce_limits()
    {
        var now = DateTimeOffset.UtcNow;
        var invite = new Invitation { ExpiresAt = now.AddMinutes(1), MaxUses = 1 };
        Assert.True(Rules.CanInvite(invite, now));
        invite.Uses = 1; Assert.False(Rules.CanInvite(invite, now));
        invite.MaxUses = null; Assert.True(Rules.CanInvite(invite, now));
        Assert.False(Rules.CanInvite(invite, invite.ExpiresAt));
        invite.Revoked = true; Assert.False(Rules.CanInvite(invite, now));
    }
    [Fact]
    public void Pair_is_symmetric_and_stable()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); Assert.Equal(Rules.Pair(a, b), Rules.Pair(b, a));
    }
    [Fact]
    public void Upload_detection_rejects_binary_and_never_treats_html_as_active_content()
    {
        Assert.Null(Attachments.Detect([0, 1, 2, 3]));
        Assert.Equal("text/plain", Attachments.Detect("<script>alert(1)</script>"u8.ToArray()));
        Assert.Equal("image/png", Attachments.Detect([137, 80, 78, 71, 13, 10, 26, 10]));
        Assert.Equal("application/pdf", Attachments.Detect("%PDF-1.7"u8.ToArray()));
        Assert.Null(Attachments.Detect([0xc0, 0xaf]));
    }
}
