using DanielsDojo.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Community;

/// <summary>Maps <see cref="DirectConversation"/> to <c>community.DirectConversations</c>.</summary>
internal sealed class DirectConversationConfiguration : IEntityTypeConfiguration<DirectConversation>
{
    public void Configure(EntityTypeBuilder<DirectConversation> builder)
    {
        builder.ToTable("DirectConversations", DatabaseSchemas.Community, table =>
            table.HasCheckConstraint(
                "CK_DirectConversations_CanonicalPair",
                "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])"));

        builder.HasKey(conversation => conversation.Id);
        builder.Property(conversation => conversation.Id).ValueGeneratedNever();

        builder.Property(conversation => conversation.LastMessageAtUtc).AsTimestamp();
        builder.Property(conversation => conversation.CreatedAtUtc).AsTimestamp();
        builder.Property(conversation => conversation.UpdatedAtUtc).AsTimestamp();
        builder.Property(conversation => conversation.RowVersion).IsRowVersion();

        builder.HasOne(conversation => conversation.UserLow)
            .WithMany()
            .HasForeignKey(conversation => conversation.UserLowId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(conversation => conversation.UserHigh)
            .WithMany()
            .HasForeignKey(conversation => conversation.UserHighId)
            .OnDelete(DeleteBehavior.Restrict);

        // Exactly one conversation per pair, so opening a chat from either side always
        // resolves to the same thread of messages.
        builder.HasIndex(conversation => new { conversation.UserLowId, conversation.UserHighId })
            .IsUnique()
            .HasDatabaseName("UX_DirectConversations_Pair");

        builder.HasIndex(conversation => new { conversation.UserHighId, conversation.LastMessageAtUtc })
            .HasDatabaseName("IX_DirectConversations_UserHighId_LastMessageAtUtc");

        builder.HasIndex(conversation => new { conversation.UserLowId, conversation.LastMessageAtUtc })
            .HasDatabaseName("IX_DirectConversations_UserLowId_LastMessageAtUtc");
    }
}

/// <summary>Maps <see cref="DirectMessage"/> to <c>community.DirectMessages</c>.</summary>
internal sealed class DirectMessageConfiguration : IEntityTypeConfiguration<DirectMessage>
{
    public void Configure(EntityTypeBuilder<DirectMessage> builder)
    {
        builder.ToTable("DirectMessages", DatabaseSchemas.Community, table =>
        {
            table.HasCheckConstraint(
                "CK_DirectMessages_Status",
                ColumnTypes.EnumValues<DirectMessageStatus>(nameof(DirectMessage.Status)));

            // A deleted message keeps its row for conversation continuity but must not keep
            // its content.
            table.HasCheckConstraint(
                "CK_DirectMessages_DeletedIsTombstoned",
                "[Status] <> 'Deleted' OR ([DeletedAtUtc] IS NOT NULL AND LEN([Body]) = 0)");

            table.HasCheckConstraint(
                "CK_DirectMessages_EditedHasTimestamp",
                "[Status] <> 'Edited' OR [EditedAtUtc] IS NOT NULL");
        });

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Body).HasMaxLength(4000).IsRequired();
        builder.Property(message => message.Status).AsEnumString();
        builder.Property(message => message.EditedAtUtc).AsTimestamp();
        builder.Property(message => message.DeletedAtUtc).AsTimestamp();
        builder.Property(message => message.CreatedAtUtc).AsTimestamp();
        builder.Property(message => message.UpdatedAtUtc).AsTimestamp();
        builder.Property(message => message.RowVersion).IsRowVersion();

        builder.HasOne(message => message.Conversation)
            .WithMany(conversation => conversation.Messages)
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(message => message.Sender)
            .WithMany()
            .HasForeignKey(message => message.SenderUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cursor pagination reads newest-first within a conversation.
        builder.HasIndex(message => new { message.ConversationId, message.CreatedAtUtc, message.Id })
            .HasDatabaseName("IX_DirectMessages_ConversationId_CreatedAtUtc_Id");
    }
}

/// <summary>Maps <see cref="ConversationReadState"/> to <c>community.ConversationReadStates</c>.</summary>
internal sealed class ConversationReadStateConfiguration
    : IEntityTypeConfiguration<ConversationReadState>
{
    public void Configure(EntityTypeBuilder<ConversationReadState> builder)
    {
        builder.ToTable("ConversationReadStates", DatabaseSchemas.Community);

        builder.HasKey(state => new { state.ConversationId, state.UserId });

        builder.Property(state => state.LastReadAtUtc).AsTimestamp();
        builder.Property(state => state.CreatedAtUtc).AsTimestamp();
        builder.Property(state => state.UpdatedAtUtc).AsTimestamp();

        builder.HasOne(state => state.Conversation)
            .WithMany()
            .HasForeignKey(state => state.ConversationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(state => state.User)
            .WithMany()
            .HasForeignKey(state => state.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<DirectMessage>()
            .WithMany()
            .HasForeignKey(state => state.LastReadMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(state => state.UserId)
            .HasDatabaseName("IX_ConversationReadStates_UserId");
    }
}
