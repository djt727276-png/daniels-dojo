using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="FriendRequest"/> to <c>community.FriendRequests</c>.</summary>
internal sealed class FriendRequestConfiguration : IEntityTypeConfiguration<FriendRequest>
{
    public void Configure(EntityTypeBuilder<FriendRequest> builder)
    {
        builder.ToTable("FriendRequests", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_FriendRequests_Status",
                ColumnTypes.EnumValues<FriendRequestStatus>(nameof(FriendRequest.Status)));

            // Canonical ordering. Without this the same pair could be stored twice, once in
            // each direction, and the two members would see different friendship state.
            table.HasCheckConstraint(
                "CK_FriendRequests_CanonicalPair",
                "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])");

            // The requester must be one of the two members.
            table.HasCheckConstraint(
                "CK_FriendRequests_RequesterIsParticipant",
                "[RequestedByUserId] = [UserLowId] OR [RequestedByUserId] = [UserHighId]");

            table.HasCheckConstraint(
                "CK_FriendRequests_RespondedWhenResolved",
                "[Status] = 'Pending' OR [RespondedAtUtc] IS NOT NULL");
        });

        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).ValueGeneratedNever();

        builder.Property(request => request.Status).AsEnumString();
        builder.Property(request => request.RequestedAtUtc).AsTimestamp();
        builder.Property(request => request.RespondedAtUtc).AsTimestamp();
        builder.Property(request => request.RowVersion).IsRowVersion();

        builder.HasOne(request => request.UserLow)
            .WithMany()
            .HasForeignKey(request => request.UserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(request => request.UserHigh)
            .WithMany()
            .HasForeignKey(request => request.UserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one live request per pair. Resolved rows stay for history, so the
        // uniqueness is filtered to the pending state.
        builder.HasIndex(request => new { request.UserLowId, request.UserHighId })
            .IsUnique()
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("UX_FriendRequests_Pair_Pending");

        builder.HasIndex(request => new { request.UserHighId, request.Status })
            .HasDatabaseName("IX_FriendRequests_UserHighId_Status");

        builder.HasIndex(request => new { request.UserLowId, request.Status })
            .HasDatabaseName("IX_FriendRequests_UserLowId_Status");
    }
}

/// <summary>Maps <see cref="Friendship"/> to <c>community.Friendships</c>.</summary>
internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("Friendships", DatabaseSchemas.Community, table =>
            table.HasCheckConstraint(
                "CK_Friendships_CanonicalPair",
                "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])"));

        builder.HasKey(friendship => friendship.Id);
        builder.Property(friendship => friendship.Id).ValueGeneratedNever();

        builder.Property(friendship => friendship.AcceptedAtUtc).AsTimestamp();
        builder.Property(friendship => friendship.CreatedAtUtc).AsTimestamp();
        builder.Property(friendship => friendship.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(friendship => friendship.UserLow)
            .WithMany()
            .HasForeignKey(friendship => friendship.UserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(friendship => friendship.UserHigh)
            .WithMany()
            .HasForeignKey(friendship => friendship.UserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(friendship => new { friendship.UserLowId, friendship.UserHighId })
            .IsUnique()
            .HasDatabaseName("UX_Friendships_Pair");

        builder.HasIndex(friendship => friendship.UserHighId)
            .HasDatabaseName("IX_Friendships_UserHighId");
    }
}

/// <summary>Maps <see cref="UserBlock"/> to <c>community.UserBlocks</c>.</summary>
internal sealed class UserBlockConfiguration : IEntityTypeConfiguration<UserBlock>
{
    public void Configure(EntityTypeBuilder<UserBlock> builder)
    {
        builder.ToTable("UserBlocks", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_UserBlocks_ReasonCategory",
                ColumnTypes.EnumValues<BlockReasonCategory>(nameof(UserBlock.ReasonCategory)));

            table.HasCheckConstraint(
                "CK_UserBlocks_NoSelfBlock",
                "[BlockerUserId] <> [BlockedUserId]");
        });

        // Directed, so the composite key is the natural key — A blocking B is a different
        // fact from B blocking A.
        builder.HasKey(block => new { block.BlockerUserId, block.BlockedUserId });

        builder.Property(block => block.ReasonCategory).AsEnumString();
        builder.Property(block => block.CreatedAtUtc).AsTimestamp();

        builder.HasOne(block => block.Blocker)
            .WithMany()
            .HasForeignKey(block => block.BlockerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(block => block.Blocked)
            .WithMany()
            .HasForeignKey(block => block.BlockedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Block checks run in both directions on every contact attempt, so the reverse
        // lookup is indexed too.
        builder.HasIndex(block => block.BlockedUserId)
            .HasDatabaseName("IX_UserBlocks_BlockedUserId");
    }
}
