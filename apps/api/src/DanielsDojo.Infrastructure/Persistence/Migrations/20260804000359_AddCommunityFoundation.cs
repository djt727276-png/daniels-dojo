using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCommunityFoundation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "community");

        migrationBuilder.CreateTable(
            name: "DirectConversations",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserLowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserHighId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LastMessageAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DirectConversations", x => x.Id);
                table.CheckConstraint("CK_DirectConversations_CanonicalPair", "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])");
                table.ForeignKey(
                    name: "FK_DirectConversations_Users_UserHighId",
                    column: x => x.UserHighId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DirectConversations_Users_UserLowId",
                    column: x => x.UserLowId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ForumCategories",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ForumCategories", x => x.Id);
                table.CheckConstraint("CK_ForumCategories_SortOrder_NonNegative", "[SortOrder] >= 0");
                table.CheckConstraint("CK_ForumCategories_Status", "[Status] IN ('Active', 'Archived')");
            });

        migrationBuilder.CreateTable(
            name: "FriendRequests",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserLowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserHighId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RespondedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FriendRequests", x => x.Id);
                table.CheckConstraint("CK_FriendRequests_CanonicalPair", "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])");
                table.CheckConstraint("CK_FriendRequests_RequesterIsParticipant", "[RequestedByUserId] = [UserLowId] OR [RequestedByUserId] = [UserHighId]");
                table.CheckConstraint("CK_FriendRequests_RespondedWhenResolved", "[Status] = 'Pending' OR [RespondedAtUtc] IS NOT NULL");
                table.CheckConstraint("CK_FriendRequests_Status", "[Status] IN ('Pending', 'Accepted', 'Declined', 'Cancelled')");
                table.ForeignKey(
                    name: "FK_FriendRequests_Users_UserHighId",
                    column: x => x.UserHighId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_FriendRequests_Users_UserLowId",
                    column: x => x.UserLowId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Friendships",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserLowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserHighId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Friendships", x => x.Id);
                table.CheckConstraint("CK_Friendships_CanonicalPair", "CONVERT(char(36), [UserLowId]) < CONVERT(char(36), [UserHighId])");
                table.ForeignKey(
                    name: "FK_Friendships_Users_UserHighId",
                    column: x => x.UserHighId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Friendships_Users_UserLowId",
                    column: x => x.UserLowId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Notifications",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                TargetType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Notifications", x => x.Id);
                table.CheckConstraint("CK_Notifications_Kind", "[Kind] IN ('FriendRequest', 'FriendAccepted', 'ThreadReply', 'PostReaction', 'DirectMessage', 'Moderation')");
                table.CheckConstraint("CK_Notifications_NoSelfNotification", "[ActorUserId] IS NULL OR [ActorUserId] <> [RecipientUserId]");
                table.ForeignKey(
                    name: "FK_Notifications_Users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Notifications_Users_RecipientUserId",
                    column: x => x.RecipientUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Profiles",
            schema: "community",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Handle = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                NormalizedHandle = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                Bio = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                AvatarStorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                IsDiscoverable = table.Column<bool>(type: "bit", nullable: false),
                FriendRequestPolicy = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                MessagePolicy = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                GuidelinesVersion = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                GuidelinesAcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                EligibilityAttestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Profiles", x => x.UserId);
                table.CheckConstraint("CK_Profiles_FriendRequestPolicy", "[FriendRequestPolicy] IN ('NoOne', 'Everyone')");
                table.CheckConstraint("CK_Profiles_GuidelinesPaired", "([GuidelinesVersion] IS NULL AND [GuidelinesAcceptedAtUtc] IS NULL) OR ([GuidelinesVersion] IS NOT NULL AND [GuidelinesAcceptedAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Profiles_MessagePolicy", "[MessagePolicy] IN ('NoOne', 'FriendsOnly')");
                table.CheckConstraint("CK_Profiles_Status", "[Status] IN ('Active', 'Suspended', 'Deactivated')");
                table.ForeignKey(
                    name: "FK_Profiles_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Reports",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TargetType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReasonCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                HandledByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Resolution = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                HandledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Reports", x => x.Id);
                table.CheckConstraint("CK_Reports_HandledWhenClosed", "[Status] IN ('Open', 'Reviewing') OR ([HandledByUserId] IS NOT NULL AND [HandledAtUtc] IS NOT NULL)");
                table.CheckConstraint("CK_Reports_ReasonCode", "[ReasonCode] IN ('Spam', 'Harassment', 'Hate', 'SexualContent', 'Violence', 'Impersonation', 'Privacy', 'Other')");
                table.CheckConstraint("CK_Reports_Status", "[Status] IN ('Open', 'Reviewing', 'Resolved', 'Dismissed')");
                table.CheckConstraint("CK_Reports_TargetType", "[TargetType] IN ('Profile', 'Thread', 'Post', 'Message')");
                table.ForeignKey(
                    name: "FK_Reports_Users_HandledByUserId",
                    column: x => x.HandledByUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Reports_Users_ReporterUserId",
                    column: x => x.ReporterUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserBlocks",
            schema: "community",
            columns: table => new
            {
                BlockerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                BlockedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReasonCategory = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerUserId, x.BlockedUserId });
                table.CheckConstraint("CK_UserBlocks_NoSelfBlock", "[BlockerUserId] <> [BlockedUserId]");
                table.CheckConstraint("CK_UserBlocks_ReasonCategory", "[ReasonCategory] IN ('Unspecified', 'Harassment', 'Spam', 'Personal')");
                table.ForeignKey(
                    name: "FK_UserBlocks_Users_BlockedUserId",
                    column: x => x.BlockedUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserBlocks_Users_BlockerUserId",
                    column: x => x.BlockerUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "DirectMessages",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                EditedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DirectMessages", x => x.Id);
                table.CheckConstraint("CK_DirectMessages_DeletedIsTombstoned", "[Status] <> 'Deleted' OR ([DeletedAtUtc] IS NOT NULL AND LEN([Body]) = 0)");
                table.CheckConstraint("CK_DirectMessages_EditedHasTimestamp", "[Status] <> 'Edited' OR [EditedAtUtc] IS NOT NULL");
                table.CheckConstraint("CK_DirectMessages_Status", "[Status] IN ('Sent', 'Edited', 'Deleted')");
                table.ForeignKey(
                    name: "FK_DirectMessages_DirectConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalSchema: "community",
                    principalTable: "DirectConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_DirectMessages_Users_SenderUserId",
                    column: x => x.SenderUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ForumThreads",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                IsPinned = table.Column<bool>(type: "bit", nullable: false),
                LastActivityAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ForumThreads", x => x.Id);
                table.CheckConstraint("CK_ForumThreads_Status", "[Status] IN ('Open', 'Locked', 'Archived', 'Removed')");
                table.ForeignKey(
                    name: "FK_ForumThreads_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumThreads_ForumCategories_CategoryId",
                    column: x => x.CategoryId,
                    principalSchema: "community",
                    principalTable: "ForumCategories",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumThreads_Users_AuthorUserId",
                    column: x => x.AuthorUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ConversationReadStates",
            schema: "community",
            columns: table => new
            {
                ConversationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LastReadMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LastReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConversationReadStates", x => new { x.ConversationId, x.UserId });
                table.ForeignKey(
                    name: "FK_ConversationReadStates_DirectConversations_ConversationId",
                    column: x => x.ConversationId,
                    principalSchema: "community",
                    principalTable: "DirectConversations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ConversationReadStates_DirectMessages_LastReadMessageId",
                    column: x => x.LastReadMessageId,
                    principalSchema: "community",
                    principalTable: "DirectMessages",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ConversationReadStates_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ForumPosts",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReplyToPostId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Body = table.Column<string>(type: "nvarchar(max)", maxLength: 10000, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                EditedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RemovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ForumPosts", x => x.Id);
                table.UniqueConstraint("AK_ForumPosts_ThreadId_Id", x => new { x.ThreadId, x.Id });
                table.CheckConstraint("CK_ForumPosts_EditedHasTimestamp", "[Status] <> 'Edited' OR [EditedAtUtc] IS NOT NULL");
                table.CheckConstraint("CK_ForumPosts_NoSelfReply", "[ReplyToPostId] IS NULL OR [ReplyToPostId] <> [Id]");
                table.CheckConstraint("CK_ForumPosts_RemovedIsTombstoned", "[Status] <> 'Removed' OR ([RemovedAtUtc] IS NOT NULL AND LEN([Body]) = 0)");
                table.CheckConstraint("CK_ForumPosts_Status", "[Status] IN ('Published', 'Edited', 'Removed')");
                table.ForeignKey(
                    name: "FK_ForumPosts_ForumThreads_ThreadId",
                    column: x => x.ThreadId,
                    principalSchema: "community",
                    principalTable: "ForumThreads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumPosts_ReplyToPost_SameThread",
                    columns: x => new { x.ThreadId, x.ReplyToPostId },
                    principalSchema: "community",
                    principalTable: "ForumPosts",
                    principalColumns: new[] { "ThreadId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumPosts_Users_AuthorUserId",
                    column: x => x.AuthorUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ForumSubscriptions",
            schema: "community",
            columns: table => new
            {
                ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                NotificationPreference = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ForumSubscriptions", x => new { x.ThreadId, x.UserId });
                table.CheckConstraint("CK_ForumSubscriptions_NotificationPreference", "[NotificationPreference] IN ('AllReplies', 'None')");
                table.ForeignKey(
                    name: "FK_ForumSubscriptions_ForumThreads_ThreadId",
                    column: x => x.ThreadId,
                    principalSchema: "community",
                    principalTable: "ForumThreads",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumSubscriptions_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "ForumPostReactions",
            schema: "community",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PostId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ReactionType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ForumPostReactions", x => x.Id);
                table.CheckConstraint("CK_ForumPostReactions_ReactionType", "[ReactionType] IN ('Like')");
                table.ForeignKey(
                    name: "FK_ForumPostReactions_ForumPosts_PostId",
                    column: x => x.PostId,
                    principalSchema: "community",
                    principalTable: "ForumPosts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_ForumPostReactions_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ConversationReadStates_LastReadMessageId",
            schema: "community",
            table: "ConversationReadStates",
            column: "LastReadMessageId");

        migrationBuilder.CreateIndex(
            name: "IX_ConversationReadStates_UserId",
            schema: "community",
            table: "ConversationReadStates",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_DirectConversations_UserHighId_LastMessageAtUtc",
            schema: "community",
            table: "DirectConversations",
            columns: new[] { "UserHighId", "LastMessageAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_DirectConversations_UserLowId_LastMessageAtUtc",
            schema: "community",
            table: "DirectConversations",
            columns: new[] { "UserLowId", "LastMessageAtUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_DirectConversations_Pair",
            schema: "community",
            table: "DirectConversations",
            columns: new[] { "UserLowId", "UserHighId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_DirectMessages_ConversationId_CreatedAtUtc_Id",
            schema: "community",
            table: "DirectMessages",
            columns: new[] { "ConversationId", "CreatedAtUtc", "Id" });

        migrationBuilder.CreateIndex(
            name: "IX_DirectMessages_SenderUserId",
            schema: "community",
            table: "DirectMessages",
            column: "SenderUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ForumCategories_Status_SortOrder",
            schema: "community",
            table: "ForumCategories",
            columns: new[] { "Status", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "UX_ForumCategories_Slug",
            schema: "community",
            table: "ForumCategories",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ForumPostReactions_UserId",
            schema: "community",
            table: "ForumPostReactions",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "UX_ForumPostReactions_PostId_UserId_ReactionType",
            schema: "community",
            table: "ForumPostReactions",
            columns: new[] { "PostId", "UserId", "ReactionType" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ForumPosts_AuthorUserId",
            schema: "community",
            table: "ForumPosts",
            column: "AuthorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ForumPosts_ThreadId_CreatedAtUtc",
            schema: "community",
            table: "ForumPosts",
            columns: new[] { "ThreadId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ForumPosts_ThreadId_ReplyToPostId",
            schema: "community",
            table: "ForumPosts",
            columns: new[] { "ThreadId", "ReplyToPostId" });

        migrationBuilder.CreateIndex(
            name: "IX_ForumSubscriptions_UserId",
            schema: "community",
            table: "ForumSubscriptions",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ForumThreads_AuthorUserId",
            schema: "community",
            table: "ForumThreads",
            column: "AuthorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ForumThreads_CategoryId_IsPinned_LastActivityAtUtc",
            schema: "community",
            table: "ForumThreads",
            columns: new[] { "CategoryId", "IsPinned", "LastActivityAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ForumThreads_CourseId",
            schema: "community",
            table: "ForumThreads",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "IX_ForumThreads_Status",
            schema: "community",
            table: "ForumThreads",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_FriendRequests_UserHighId_Status",
            schema: "community",
            table: "FriendRequests",
            columns: new[] { "UserHighId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_FriendRequests_UserLowId_Status",
            schema: "community",
            table: "FriendRequests",
            columns: new[] { "UserLowId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_FriendRequests_Pair_Pending",
            schema: "community",
            table: "FriendRequests",
            columns: new[] { "UserLowId", "UserHighId" },
            unique: true,
            filter: "[Status] = 'Pending'");

        migrationBuilder.CreateIndex(
            name: "IX_Friendships_UserHighId",
            schema: "community",
            table: "Friendships",
            column: "UserHighId");

        migrationBuilder.CreateIndex(
            name: "UX_Friendships_Pair",
            schema: "community",
            table: "Friendships",
            columns: new[] { "UserLowId", "UserHighId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_ActorUserId",
            schema: "community",
            table: "Notifications",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_RecipientUserId_CreatedAtUtc",
            schema: "community",
            table: "Notifications",
            columns: new[] { "RecipientUserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Notifications_RecipientUserId_Unread",
            schema: "community",
            table: "Notifications",
            column: "RecipientUserId",
            filter: "[ReadAtUtc] IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Profiles_IsDiscoverable_NormalizedHandle",
            schema: "community",
            table: "Profiles",
            columns: new[] { "IsDiscoverable", "NormalizedHandle" });

        migrationBuilder.CreateIndex(
            name: "UX_Profiles_NormalizedHandle",
            schema: "community",
            table: "Profiles",
            column: "NormalizedHandle",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Reports_HandledByUserId",
            schema: "community",
            table: "Reports",
            column: "HandledByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Reports_Status_CreatedAtUtc",
            schema: "community",
            table: "Reports",
            columns: new[] { "Status", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_Reports_TargetType_TargetId",
            schema: "community",
            table: "Reports",
            columns: new[] { "TargetType", "TargetId" });

        migrationBuilder.CreateIndex(
            name: "UX_Reports_Reporter_Target_Open",
            schema: "community",
            table: "Reports",
            columns: new[] { "ReporterUserId", "TargetType", "TargetId" },
            unique: true,
            filter: "[Status] IN ('Open', 'Reviewing')");

        migrationBuilder.CreateIndex(
            name: "IX_UserBlocks_BlockedUserId",
            schema: "community",
            table: "UserBlocks",
            column: "BlockedUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ConversationReadStates",
            schema: "community");

        migrationBuilder.DropTable(
            name: "ForumPostReactions",
            schema: "community");

        migrationBuilder.DropTable(
            name: "ForumSubscriptions",
            schema: "community");

        migrationBuilder.DropTable(
            name: "FriendRequests",
            schema: "community");

        migrationBuilder.DropTable(
            name: "Friendships",
            schema: "community");

        migrationBuilder.DropTable(
            name: "Notifications",
            schema: "community");

        migrationBuilder.DropTable(
            name: "Profiles",
            schema: "community");

        migrationBuilder.DropTable(
            name: "Reports",
            schema: "community");

        migrationBuilder.DropTable(
            name: "UserBlocks",
            schema: "community");

        migrationBuilder.DropTable(
            name: "DirectMessages",
            schema: "community");

        migrationBuilder.DropTable(
            name: "ForumPosts",
            schema: "community");

        migrationBuilder.DropTable(
            name: "DirectConversations",
            schema: "community");

        migrationBuilder.DropTable(
            name: "ForumThreads",
            schema: "community");

        migrationBuilder.DropTable(
            name: "ForumCategories",
            schema: "community");
    }
}
