using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="ForumCategory"/> to <c>community.ForumCategories</c>.</summary>
internal sealed class ForumCategoryConfiguration : IEntityTypeConfiguration<ForumCategory>
{
    public void Configure(EntityTypeBuilder<ForumCategory> builder)
    {
        builder.ToTable("ForumCategories", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_ForumCategories_Status",
                ColumnTypes.EnumValues<ForumCategoryStatus>(nameof(ForumCategory.Status)));
            table.HasCheckConstraint(
                "CK_ForumCategories_SortOrder_NonNegative",
                "[SortOrder] >= 0");
        });

        builder.HasKey(category => category.Id);
        builder.Property(category => category.Id).ValueGeneratedNever();

        builder.Property(category => category.Slug).HasMaxLength(64).IsRequired();
        builder.Property(category => category.Name).HasMaxLength(100).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(500).IsRequired();
        builder.Property(category => category.SortOrder).IsRequired();
        builder.Property(category => category.Status).AsEnumString();
        builder.Property(category => category.CreatedAtUtc).AsTimestamp();
        builder.Property(category => category.UpdatedAtUtc).AsTimestamp();
        builder.Property(category => category.RowVersion).IsRowVersion();

        builder.HasIndex(category => category.Slug)
            .IsUnique()
            .HasDatabaseName("UX_ForumCategories_Slug");

        builder.HasIndex(category => new { category.Status, category.SortOrder })
            .HasDatabaseName("IX_ForumCategories_Status_SortOrder");
    }
}

/// <summary>Maps <see cref="ForumThread"/> to <c>community.ForumThreads</c>.</summary>
internal sealed class ForumThreadConfiguration : IEntityTypeConfiguration<ForumThread>
{
    public void Configure(EntityTypeBuilder<ForumThread> builder)
    {
        builder.ToTable("ForumThreads", DatabaseSchemas.Community, table =>
            table.HasCheckConstraint(
                "CK_ForumThreads_Status",
                ColumnTypes.EnumValues<ForumThreadStatus>(nameof(ForumThread.Status))));

        builder.HasKey(thread => thread.Id);
        builder.Property(thread => thread.Id).ValueGeneratedNever();

        builder.Property(thread => thread.Title).HasMaxLength(200).IsRequired();
        builder.Property(thread => thread.Status).AsEnumString();
        builder.Property(thread => thread.IsPinned).IsRequired();
        builder.Property(thread => thread.LastActivityAtUtc).AsTimestamp();
        builder.Property(thread => thread.CreatedAtUtc).AsTimestamp();
        builder.Property(thread => thread.UpdatedAtUtc).AsTimestamp();
        builder.Property(thread => thread.RowVersion).IsRowVersion();

        builder.HasOne(thread => thread.Category)
            .WithMany(category => category.Threads)
            .HasForeignKey(thread => thread.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(thread => thread.Course)
            .WithMany()
            .HasForeignKey(thread => thread.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrictive: removing a member must never silently erase their discussions.
        builder.HasOne(thread => thread.Author)
            .WithMany()
            .HasForeignKey(thread => thread.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite foreign key onto the posts' (ThreadId, Id) alternate key: the accepted
        // answer can only ever be a post of this same thread — the database rejects a
        // cross-thread answer rather than trusting application code to check.
        builder.HasOne(thread => thread.SolvedPost)
            .WithMany()
            .HasForeignKey(thread => new { thread.Id, thread.SolvedPostId })
            .HasPrincipalKey(post => new { post.ThreadId, post.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ForumThreads_SolvedPost_SameThread");

        // Category listings are ordered pinned-first then by recency.
        builder.HasIndex(thread => new { thread.CategoryId, thread.IsPinned, thread.LastActivityAtUtc })
            .HasDatabaseName("IX_ForumThreads_CategoryId_IsPinned_LastActivityAtUtc");

        builder.HasIndex(thread => thread.AuthorUserId)
            .HasDatabaseName("IX_ForumThreads_AuthorUserId");

        builder.HasIndex(thread => thread.Status)
            .HasDatabaseName("IX_ForumThreads_Status");
    }
}

/// <summary>Maps <see cref="ForumPost"/> to <c>community.ForumPosts</c>.</summary>
internal sealed class ForumPostConfiguration : IEntityTypeConfiguration<ForumPost>
{
    public void Configure(EntityTypeBuilder<ForumPost> builder)
    {
        builder.ToTable("ForumPosts", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_ForumPosts_Status",
                ColumnTypes.EnumValues<ForumPostStatus>(nameof(ForumPost.Status)));

            // A removed post keeps its row but must not keep its content.
            table.HasCheckConstraint(
                "CK_ForumPosts_RemovedIsTombstoned",
                "[Status] <> 'Removed' OR ([RemovedAtUtc] IS NOT NULL AND LEN([Body]) = 0)");

            table.HasCheckConstraint(
                "CK_ForumPosts_EditedHasTimestamp",
                "[Status] <> 'Edited' OR [EditedAtUtc] IS NOT NULL");

            // A post cannot reply to itself.
            table.HasCheckConstraint(
                "CK_ForumPosts_NoSelfReply",
                "[ReplyToPostId] IS NULL OR [ReplyToPostId] <> [Id]");
        });

        builder.HasKey(post => post.Id);
        builder.Property(post => post.Id).ValueGeneratedNever();

        builder.Property(post => post.Body).HasMaxLength(10000).IsRequired();
        builder.Property(post => post.Status).AsEnumString();
        builder.Property(post => post.EditedAtUtc).AsTimestamp();
        builder.Property(post => post.RemovedAtUtc).AsTimestamp();
        builder.Property(post => post.CreatedAtUtc).AsTimestamp();
        builder.Property(post => post.UpdatedAtUtc).AsTimestamp();
        builder.Property(post => post.RowVersion).IsRowVersion();

        // Alternate key targeted by the reply composite foreign key below.
        builder.HasAlternateKey(post => new { post.ThreadId, post.Id })
            .HasName("AK_ForumPosts_ThreadId_Id");

        builder.HasOne(post => post.Thread)
            .WithMany(thread => thread.Posts)
            .HasForeignKey(post => post.ThreadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(post => post.Author)
            .WithMany()
            .HasForeignKey(post => post.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite foreign key: a reply must target a post in the same thread. The database
        // rejects a cross-thread reply rather than trusting application code to check.
        builder.HasOne(post => post.ReplyToPost)
            .WithMany()
            .HasForeignKey(post => new { post.ThreadId, post.ReplyToPostId })
            .HasPrincipalKey(post => new { post.ThreadId, post.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_ForumPosts_ReplyToPost_SameThread");

        builder.HasIndex(post => new { post.ThreadId, post.CreatedAtUtc })
            .HasDatabaseName("IX_ForumPosts_ThreadId_CreatedAtUtc");

        builder.HasIndex(post => post.AuthorUserId)
            .HasDatabaseName("IX_ForumPosts_AuthorUserId");
    }
}

/// <summary>Maps <see cref="ForumPostReaction"/> to <c>community.ForumPostReactions</c>.</summary>
internal sealed class ForumPostReactionConfiguration : IEntityTypeConfiguration<ForumPostReaction>
{
    public void Configure(EntityTypeBuilder<ForumPostReaction> builder)
    {
        builder.ToTable("ForumPostReactions", DatabaseSchemas.Community, table =>
            table.HasCheckConstraint(
                "CK_ForumPostReactions_ReactionType",
                ColumnTypes.EnumValues<ReactionType>(nameof(ForumPostReaction.ReactionType))));

        builder.HasKey(reaction => reaction.Id);
        builder.Property(reaction => reaction.Id).ValueGeneratedNever();

        builder.Property(reaction => reaction.ReactionType).AsEnumString();
        builder.Property(reaction => reaction.CreatedAtUtc).AsTimestamp();

        builder.HasOne(reaction => reaction.Post)
            .WithMany(post => post.Reactions)
            .HasForeignKey(reaction => reaction.PostId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(reaction => reaction.User)
            .WithMany()
            .HasForeignKey(reaction => reaction.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // One reaction of each type per member per post, so a repeated tap cannot inflate a count.
        builder.HasIndex(reaction => new { reaction.PostId, reaction.UserId, reaction.ReactionType })
            .IsUnique()
            .HasDatabaseName("UX_ForumPostReactions_PostId_UserId_ReactionType");
    }
}

/// <summary>Maps <see cref="ForumSubscription"/> to <c>community.ForumSubscriptions</c>.</summary>
internal sealed class ForumSubscriptionConfiguration : IEntityTypeConfiguration<ForumSubscription>
{
    public void Configure(EntityTypeBuilder<ForumSubscription> builder)
    {
        builder.ToTable("ForumSubscriptions", DatabaseSchemas.Community, table =>
            table.HasCheckConstraint(
                "CK_ForumSubscriptions_NotificationPreference",
                ColumnTypes.EnumValues<ThreadNotificationPreference>(
                    nameof(ForumSubscription.NotificationPreference))));

        builder.HasKey(subscription => new { subscription.ThreadId, subscription.UserId });

        builder.Property(subscription => subscription.NotificationPreference).AsEnumString();
        builder.Property(subscription => subscription.CreatedAtUtc).AsTimestamp();
        builder.Property(subscription => subscription.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(subscription => subscription.Thread)
            .WithMany()
            .HasForeignKey(subscription => subscription.ThreadId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(subscription => subscription.User)
            .WithMany()
            .HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(subscription => subscription.UserId)
            .HasDatabaseName("IX_ForumSubscriptions_UserId");
    }
}
