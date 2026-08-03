using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialPlatformSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "audit");

        migrationBuilder.EnsureSchema(
            name: "catalog");

        migrationBuilder.EnsureSchema(
            name: "learning");

        migrationBuilder.EnsureSchema(
            name: "commerce");

        migrationBuilder.EnsureSchema(
            name: "identity");

        migrationBuilder.CreateTable(
            name: "Courses",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Summary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                ImageStorageKey = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                ImageAltText = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                Level = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                IncludedInMembership = table.Column<bool>(type: "bit", nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Courses", x => x.Id);
                table.CheckConstraint("CK_Courses_Level", "[Level] IN ('Beginner', 'Intermediate', 'Advanced', 'AllLevels')");
                table.CheckConstraint("CK_Courses_Status", "[Status] IN ('Draft', 'Published', 'Archived')");
            });

        migrationBuilder.CreateTable(
            name: "Roles",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                IsAssignable = table.Column<bool>(type: "bit", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Roles", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Tags",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                NormalizedName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tags", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Users",
            schema: "identity",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                IdentityProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                ExternalIssuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ExternalSubjectId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.CheckConstraint("CK_Users_Status", "[Status] IN ('Active', 'Disabled')");
            });

        migrationBuilder.CreateTable(
            name: "WebhookEvents",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                ExternalEventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                EventType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                AttemptCount = table.Column<int>(type: "int", nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                ProcessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                LastError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                PayloadSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WebhookEvents", x => x.Id);
                table.CheckConstraint("CK_WebhookEvents_AttemptCount_NonNegative", "[AttemptCount] >= 0");
                table.CheckConstraint("CK_WebhookEvents_Status", "[Status] IN ('Received', 'Processing', 'Processed', 'Failed', 'Ignored')");
            });

        migrationBuilder.CreateTable(
            name: "CourseSections",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseSections", x => x.Id);
                table.UniqueConstraint("AK_CourseSections_CourseId_Id", x => new { x.CourseId, x.Id });
                table.CheckConstraint("CK_CourseSections_Status", "[Status] IN ('Draft', 'Published', 'Archived')");
                table.ForeignKey(
                    name: "FK_CourseSections_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Offers",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                StripeProductId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Offers", x => x.Id);
                table.CheckConstraint("CK_Offers_CourseLifetimeRequiresCourse", "[Kind] <> 'CourseLifetime' OR [CourseId] IS NOT NULL");
                table.CheckConstraint("CK_Offers_Kind", "[Kind] IN ('Membership', 'CourseLifetime')");
                table.CheckConstraint("CK_Offers_MembershipForbidsCourse", "[Kind] <> 'Membership' OR [CourseId] IS NULL");
                table.CheckConstraint("CK_Offers_Status", "[Status] IN ('Draft', 'Active', 'Retired')");
                table.ForeignKey(
                    name: "FK_Offers_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CourseTags",
            schema: "catalog",
            columns: table => new
            {
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                TagId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseTags", x => new { x.CourseId, x.TagId });
                table.ForeignKey(
                    name: "FK_CourseTags_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CourseTags_Tags_TagId",
                    column: x => x.TagId,
                    principalSchema: "catalog",
                    principalTable: "Tags",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            schema: "audit",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Action = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TargetType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                TargetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                MetadataJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogs", x => x.Id);
                table.ForeignKey(
                    name: "FK_AuditLogs_Users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CourseInstructors",
            schema: "catalog",
            columns: table => new
            {
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseInstructors", x => new { x.CourseId, x.UserId });
                table.ForeignKey(
                    name: "FK_CourseInstructors_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_CourseInstructors_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Enrollments",
            schema: "learning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EnrolledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                LastAccessedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Enrollments", x => x.Id);
                table.ForeignKey(
                    name: "FK_Enrollments_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Enrollments_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Orders",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                SubtotalMinor = table.Column<long>(type: "bigint", nullable: false),
                TaxMinor = table.Column<long>(type: "bigint", nullable: false),
                TotalMinor = table.Column<long>(type: "bigint", nullable: false),
                StripeCheckoutSessionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                StripePaymentIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                PaidAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.CheckConstraint("CK_Orders_Amounts_NonNegative", "[SubtotalMinor] >= 0 AND [TaxMinor] >= 0 AND [TotalMinor] >= 0");
                table.CheckConstraint("CK_Orders_Currency_Uppercase", "[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2");
                table.CheckConstraint("CK_Orders_Status", "[Status] IN ('Pending', 'Paid', 'Failed', 'PartiallyRefunded', 'Refunded', 'Disputed', 'ChargebackLost')");
                table.CheckConstraint("CK_Orders_Total_Reconciles", "[TotalMinor] = [SubtotalMinor] + [TaxMinor]");
                table.ForeignKey(
                    name: "FK_Orders_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "StripeCustomers",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StripeCustomerId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StripeCustomers", x => x.Id);
                table.ForeignKey(
                    name: "FK_StripeCustomers_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UserRoles",
            schema: "identity",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                AssignedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey(
                    name: "FK_UserRoles_Roles_RoleId",
                    column: x => x.RoleId,
                    principalSchema: "identity",
                    principalTable: "Roles",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_AssignedByUserId",
                    column: x => x.AssignedByUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UserRoles_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Lessons",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseSectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Slug = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                Summary = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                LessonType = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                BodyMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: true),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                IsPreview = table.Column<bool>(type: "bit", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                EstimatedDurationSeconds = table.Column<int>(type: "int", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Lessons", x => x.Id);
                table.CheckConstraint("CK_Lessons_EstimatedDurationSeconds_NonNegative", "[EstimatedDurationSeconds] IS NULL OR [EstimatedDurationSeconds] >= 0");
                table.CheckConstraint("CK_Lessons_LessonType", "[LessonType] IN ('Video', 'Article')");
                table.CheckConstraint("CK_Lessons_SortOrder_NonNegative", "[SortOrder] >= 0");
                table.CheckConstraint("CK_Lessons_Status", "[Status] IN ('Draft', 'Published', 'Archived')");
                table.ForeignKey(
                    name: "FK_Lessons_CourseSections_CourseId_CourseSectionId",
                    columns: x => new { x.CourseId, x.CourseSectionId },
                    principalSchema: "catalog",
                    principalTable: "CourseSections",
                    principalColumns: new[] { "CourseId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Lessons_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Prices",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                BillingInterval = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                BillingIntervalCount = table.Column<int>(type: "int", nullable: false),
                StripePriceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                EffectiveFromUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RetiredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Prices", x => x.Id);
                table.CheckConstraint("CK_Prices_AmountMinor_Positive", "[AmountMinor] > 0");
                table.CheckConstraint("CK_Prices_BillingInterval", "[BillingInterval] IN ('OneTime', 'Month')");
                table.CheckConstraint("CK_Prices_BillingIntervalCount_One", "[BillingIntervalCount] = 1");
                table.CheckConstraint("CK_Prices_Currency_Uppercase", "[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2");
                table.CheckConstraint("CK_Prices_RetiredAfterEffective", "[RetiredAtUtc] IS NULL OR [RetiredAtUtc] >= [EffectiveFromUtc]");
                table.CheckConstraint("CK_Prices_Status", "[Status] IN ('Draft', 'Active', 'Retired')");
                table.ForeignKey(
                    name: "FK_Prices_Offers_OfferId",
                    column: x => x.OfferId,
                    principalSchema: "commerce",
                    principalTable: "Offers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LessonProgress",
            schema: "learning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                LastPositionSeconds = table.Column<int>(type: "int", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LessonProgress", x => x.Id);
                table.CheckConstraint("CK_LessonProgress_CompletedAfterStarted", "[CompletedAtUtc] IS NULL OR [CompletedAtUtc] >= [StartedAtUtc]");
                table.CheckConstraint("CK_LessonProgress_CompletedRequiresStarted", "[CompletedAtUtc] IS NULL OR [StartedAtUtc] IS NOT NULL");
                table.CheckConstraint("CK_LessonProgress_LastPositionSeconds_NonNegative", "[LastPositionSeconds] >= 0");
                table.ForeignKey(
                    name: "FK_LessonProgress_Lessons_LessonId",
                    column: x => x.LessonId,
                    principalSchema: "catalog",
                    principalTable: "Lessons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_LessonProgress_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LessonResources",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                BlobObjectName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                MediaType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                SortOrder = table.Column<int>(type: "int", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LessonResources", x => x.Id);
                table.CheckConstraint("CK_LessonResources_PublishedRequiresBlob", "[Status] <> 'Published' OR [BlobObjectName] IS NOT NULL");
                table.CheckConstraint("CK_LessonResources_SizeBytes_NonNegative", "[SizeBytes] >= 0");
                table.CheckConstraint("CK_LessonResources_Status", "[Status] IN ('Draft', 'Published', 'Archived')");
                table.ForeignKey(
                    name: "FK_LessonResources_Lessons_LessonId",
                    column: x => x.LessonId,
                    principalSchema: "catalog",
                    principalTable: "Lessons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "LessonVideos",
            schema: "catalog",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MuxAssetId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                MuxPlaybackId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                DurationSeconds = table.Column<int>(type: "int", nullable: true),
                FailureCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_LessonVideos", x => x.Id);
                table.CheckConstraint("CK_LessonVideos_DurationSeconds_NonNegative", "[DurationSeconds] IS NULL OR [DurationSeconds] >= 0");
                table.CheckConstraint("CK_LessonVideos_Status", "[Status] IN ('Pending', 'Preparing', 'Ready', 'Errored', 'Disabled')");
                table.ForeignKey(
                    name: "FK_LessonVideos_Lessons_LessonId",
                    column: x => x.LessonId,
                    principalSchema: "catalog",
                    principalTable: "Lessons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "OrderItems",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OfferName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                UnitAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                Quantity = table.Column<int>(type: "int", nullable: false),
                LineTotalMinor = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrderItems", x => x.Id);
                table.CheckConstraint("CK_OrderItems_Currency_Uppercase", "[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2");
                table.CheckConstraint("CK_OrderItems_LineTotal_Reconciles", "[LineTotalMinor] = [UnitAmountMinor] * [Quantity]");
                table.CheckConstraint("CK_OrderItems_Quantity_One", "[Quantity] = 1");
                table.CheckConstraint("CK_OrderItems_UnitAmountMinor_NonNegative", "[UnitAmountMinor] >= 0");
                table.ForeignKey(
                    name: "FK_OrderItems_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderItems_Offers_OfferId",
                    column: x => x.OfferId,
                    principalSchema: "commerce",
                    principalTable: "Offers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderItems_Orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "commerce",
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_OrderItems_Prices_PriceId",
                    column: x => x.PriceId,
                    principalSchema: "commerce",
                    principalTable: "Prices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Subscriptions",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                PriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StripeSubscriptionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CurrentPeriodStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CurrentPeriodEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CancelAtPeriodEnd = table.Column<bool>(type: "bit", nullable: false),
                CanceledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                TrialStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                TrialEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                FirstPaymentFailedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                GracePeriodEndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Subscriptions", x => x.Id);
                table.CheckConstraint("CK_Subscriptions_PeriodOrdered", "[CurrentPeriodEndUtc] >= [CurrentPeriodStartUtc]");
                table.CheckConstraint("CK_Subscriptions_Status", "[Status] IN ('Incomplete', 'Trialing', 'Active', 'PastDue', 'Unpaid', 'Paused', 'Canceled', 'IncompleteExpired')");
                table.CheckConstraint("CK_Subscriptions_TrialOrdered", "[TrialStartUtc] IS NULL OR [TrialEndUtc] IS NULL OR [TrialEndUtc] >= [TrialStartUtc]");
                table.ForeignKey(
                    name: "FK_Subscriptions_Offers_OfferId",
                    column: x => x.OfferId,
                    principalSchema: "commerce",
                    principalTable: "Offers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Subscriptions_Prices_PriceId",
                    column: x => x.PriceId,
                    principalSchema: "commerce",
                    principalTable: "Prices",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Subscriptions_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Entitlements",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Scope = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Source = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                OrderItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                StartsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                EndsAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                GrantedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                GrantReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RevokedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RevocationReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Entitlements", x => x.Id);
                table.CheckConstraint("CK_Entitlements_CourseScopeRequiresCourse", "[Scope] <> 'Course' OR [CourseId] IS NOT NULL");
                table.CheckConstraint("CK_Entitlements_EndsAfterStarts", "[EndsAtUtc] IS NULL OR [EndsAtUtc] >= [StartsAtUtc]");
                table.CheckConstraint("CK_Entitlements_ManualSource", "[Source] <> 'Manual' OR ([SubscriptionId] IS NULL AND [OrderItemId] IS NULL)");
                table.CheckConstraint("CK_Entitlements_MembershipScopeForbidsCourse", "[Scope] <> 'AllMembershipCourses' OR [CourseId] IS NULL");
                table.CheckConstraint("CK_Entitlements_PurchaseSource", "[Source] <> 'Purchase' OR ([OrderItemId] IS NOT NULL AND [SubscriptionId] IS NULL)");
                table.CheckConstraint("CK_Entitlements_Scope", "[Scope] IN ('AllMembershipCourses', 'Course')");
                table.CheckConstraint("CK_Entitlements_Source", "[Source] IN ('Subscription', 'Purchase', 'Manual')");
                table.CheckConstraint("CK_Entitlements_Status", "[Status] IN ('Active', 'Revoked', 'Expired')");
                table.CheckConstraint("CK_Entitlements_SubscriptionSource", "[Source] <> 'Subscription' OR ([SubscriptionId] IS NOT NULL AND [OrderItemId] IS NULL)");
                table.ForeignKey(
                    name: "FK_Entitlements_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Entitlements_OrderItems_OrderItemId",
                    column: x => x.OrderItemId,
                    principalSchema: "commerce",
                    principalTable: "OrderItems",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Entitlements_Subscriptions_SubscriptionId",
                    column: x => x.SubscriptionId,
                    principalSchema: "commerce",
                    principalTable: "Subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Entitlements_Users_GrantedByUserId",
                    column: x => x.GrantedByUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Entitlements_Users_RevokedByUserId",
                    column: x => x.RevokedByUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Entitlements_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "PaymentDisputes",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StripeDisputeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                StripeChargeId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PaymentDisputes", x => x.Id);
                table.CheckConstraint("CK_PaymentDisputes_AmountMinor_Positive", "[AmountMinor] > 0");
                table.CheckConstraint("CK_PaymentDisputes_Currency_Uppercase", "[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2");
                table.CheckConstraint("CK_PaymentDisputes_ExactlyOneSource", "([OrderId] IS NOT NULL AND [SubscriptionId] IS NULL) OR ([OrderId] IS NULL AND [SubscriptionId] IS NOT NULL)");
                table.CheckConstraint("CK_PaymentDisputes_Status", "[Status] IN ('WarningNeedsResponse', 'NeedsResponse', 'UnderReview', 'Won', 'Lost', 'Closed')");
                table.ForeignKey(
                    name: "FK_PaymentDisputes_Orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "commerce",
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PaymentDisputes_Subscriptions_SubscriptionId",
                    column: x => x.SubscriptionId,
                    principalSchema: "commerce",
                    principalTable: "Subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Refunds",
            schema: "commerce",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                StripeRefundId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                SubscriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                StripePaymentIntentId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                Currency = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                Reason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                IsFullRefund = table.Column<bool>(type: "bit", nullable: false),
                RequiresAccessReview = table.Column<bool>(type: "bit", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Refunds", x => x.Id);
                table.CheckConstraint("CK_Refunds_AmountMinor_Positive", "[AmountMinor] > 0");
                table.CheckConstraint("CK_Refunds_Currency_Uppercase", "[Currency] = UPPER([Currency]) COLLATE Latin1_General_BIN2");
                table.CheckConstraint("CK_Refunds_ExactlyOneSource", "([OrderId] IS NOT NULL AND [SubscriptionId] IS NULL) OR ([OrderId] IS NULL AND [SubscriptionId] IS NOT NULL)");
                table.CheckConstraint("CK_Refunds_Status", "[Status] IN ('Pending', 'Succeeded', 'Failed', 'Canceled')");
                table.ForeignKey(
                    name: "FK_Refunds_Orders_OrderId",
                    column: x => x.OrderId,
                    principalSchema: "commerce",
                    principalTable: "Orders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Refunds_Subscriptions_SubscriptionId",
                    column: x => x.SubscriptionId,
                    principalSchema: "commerce",
                    principalTable: "Subscriptions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_ActorUserId",
            schema: "audit",
            table: "AuditLogs",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_OccurredAtUtc",
            schema: "audit",
            table: "AuditLogs",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogs_TargetType_TargetId",
            schema: "audit",
            table: "AuditLogs",
            columns: new[] { "TargetType", "TargetId" });

        migrationBuilder.CreateIndex(
            name: "IX_CourseInstructors_UserId",
            schema: "catalog",
            table: "CourseInstructors",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_Courses_IncludedInMembership",
            schema: "catalog",
            table: "Courses",
            column: "IncludedInMembership");

        migrationBuilder.CreateIndex(
            name: "IX_Courses_Status",
            schema: "catalog",
            table: "Courses",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_Courses_Slug",
            schema: "catalog",
            table: "Courses",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_CourseSections_CourseId_SortOrder",
            schema: "catalog",
            table: "CourseSections",
            columns: new[] { "CourseId", "SortOrder" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CourseTags_TagId",
            schema: "catalog",
            table: "CourseTags",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_Enrollments_CourseId",
            schema: "learning",
            table: "Enrollments",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "UX_Enrollments_UserId_CourseId",
            schema: "learning",
            table: "Enrollments",
            columns: new[] { "UserId", "CourseId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Entitlements_CourseId",
            schema: "commerce",
            table: "Entitlements",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "IX_Entitlements_GrantedByUserId",
            schema: "commerce",
            table: "Entitlements",
            column: "GrantedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Entitlements_RevokedByUserId",
            schema: "commerce",
            table: "Entitlements",
            column: "RevokedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_Entitlements_UserId_CourseId_Status",
            schema: "commerce",
            table: "Entitlements",
            columns: new[] { "UserId", "CourseId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_Entitlements_UserId_Status",
            schema: "commerce",
            table: "Entitlements",
            columns: new[] { "UserId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_Entitlements_OrderItemId",
            schema: "commerce",
            table: "Entitlements",
            column: "OrderItemId",
            unique: true,
            filter: "[OrderItemId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_Entitlements_SubscriptionId",
            schema: "commerce",
            table: "Entitlements",
            column: "SubscriptionId",
            unique: true,
            filter: "[SubscriptionId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_LessonProgress_LessonId",
            schema: "learning",
            table: "LessonProgress",
            column: "LessonId");

        migrationBuilder.CreateIndex(
            name: "UX_LessonProgress_UserId_LessonId",
            schema: "learning",
            table: "LessonProgress",
            columns: new[] { "UserId", "LessonId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_LessonResources_LessonId_SortOrder",
            schema: "catalog",
            table: "LessonResources",
            columns: new[] { "LessonId", "SortOrder" });

        migrationBuilder.CreateIndex(
            name: "UX_LessonResources_BlobObjectName",
            schema: "catalog",
            table: "LessonResources",
            column: "BlobObjectName",
            unique: true,
            filter: "[BlobObjectName] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Lessons_CourseId_CourseSectionId",
            schema: "catalog",
            table: "Lessons",
            columns: new[] { "CourseId", "CourseSectionId" });

        migrationBuilder.CreateIndex(
            name: "IX_Lessons_Status",
            schema: "catalog",
            table: "Lessons",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_Lessons_CourseId_Slug",
            schema: "catalog",
            table: "Lessons",
            columns: new[] { "CourseId", "Slug" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Lessons_CourseSectionId_SortOrder",
            schema: "catalog",
            table: "Lessons",
            columns: new[] { "CourseSectionId", "SortOrder" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_LessonVideos_LessonId",
            schema: "catalog",
            table: "LessonVideos",
            column: "LessonId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_LessonVideos_MuxAssetId",
            schema: "catalog",
            table: "LessonVideos",
            column: "MuxAssetId",
            unique: true,
            filter: "[MuxAssetId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_LessonVideos_MuxPlaybackId",
            schema: "catalog",
            table: "LessonVideos",
            column: "MuxPlaybackId",
            unique: true,
            filter: "[MuxPlaybackId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Offers_CourseId",
            schema: "commerce",
            table: "Offers",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "IX_Offers_Status",
            schema: "commerce",
            table: "Offers",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_Offers_Code",
            schema: "commerce",
            table: "Offers",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Offers_StripeProductId",
            schema: "commerce",
            table: "Offers",
            column: "StripeProductId",
            unique: true,
            filter: "[StripeProductId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_CourseId",
            schema: "commerce",
            table: "OrderItems",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_OfferId",
            schema: "commerce",
            table: "OrderItems",
            column: "OfferId");

        migrationBuilder.CreateIndex(
            name: "IX_OrderItems_PriceId",
            schema: "commerce",
            table: "OrderItems",
            column: "PriceId");

        migrationBuilder.CreateIndex(
            name: "UX_OrderItems_OrderId_OfferId",
            schema: "commerce",
            table: "OrderItems",
            columns: new[] { "OrderId", "OfferId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Orders_UserId_CreatedAtUtc",
            schema: "commerce",
            table: "Orders",
            columns: new[] { "UserId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_Orders_StripeCheckoutSessionId",
            schema: "commerce",
            table: "Orders",
            column: "StripeCheckoutSessionId",
            unique: true,
            filter: "[StripeCheckoutSessionId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_Orders_StripePaymentIntentId",
            schema: "commerce",
            table: "Orders",
            column: "StripePaymentIntentId",
            unique: true,
            filter: "[StripePaymentIntentId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentDisputes_OrderId",
            schema: "commerce",
            table: "PaymentDisputes",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentDisputes_Status",
            schema: "commerce",
            table: "PaymentDisputes",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_PaymentDisputes_SubscriptionId",
            schema: "commerce",
            table: "PaymentDisputes",
            column: "SubscriptionId");

        migrationBuilder.CreateIndex(
            name: "UX_PaymentDisputes_StripeDisputeId",
            schema: "commerce",
            table: "PaymentDisputes",
            column: "StripeDisputeId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Prices_OfferId_Status",
            schema: "commerce",
            table: "Prices",
            columns: new[] { "OfferId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_Prices_StripePriceId",
            schema: "commerce",
            table: "Prices",
            column: "StripePriceId",
            unique: true,
            filter: "[StripePriceId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Refunds_OrderId",
            schema: "commerce",
            table: "Refunds",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_Refunds_RequiresAccessReview",
            schema: "commerce",
            table: "Refunds",
            column: "RequiresAccessReview");

        migrationBuilder.CreateIndex(
            name: "IX_Refunds_SubscriptionId",
            schema: "commerce",
            table: "Refunds",
            column: "SubscriptionId");

        migrationBuilder.CreateIndex(
            name: "UX_Refunds_StripeRefundId",
            schema: "commerce",
            table: "Refunds",
            column: "StripeRefundId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Roles_NormalizedName",
            schema: "identity",
            table: "Roles",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_StripeCustomers_StripeCustomerId",
            schema: "commerce",
            table: "StripeCustomers",
            column: "StripeCustomerId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_StripeCustomers_UserId",
            schema: "commerce",
            table: "StripeCustomers",
            column: "UserId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_OfferId",
            schema: "commerce",
            table: "Subscriptions",
            column: "OfferId");

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_PriceId",
            schema: "commerce",
            table: "Subscriptions",
            column: "PriceId");

        migrationBuilder.CreateIndex(
            name: "IX_Subscriptions_UserId_Status",
            schema: "commerce",
            table: "Subscriptions",
            columns: new[] { "UserId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_Subscriptions_StripeSubscriptionId",
            schema: "commerce",
            table: "Subscriptions",
            column: "StripeSubscriptionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Tags_NormalizedName",
            schema: "catalog",
            table: "Tags",
            column: "NormalizedName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_AssignedByUserId",
            schema: "identity",
            table: "UserRoles",
            column: "AssignedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_UserRoles_RoleId",
            schema: "identity",
            table: "UserRoles",
            column: "RoleId");

        migrationBuilder.CreateIndex(
            name: "IX_Users_NormalizedEmail",
            schema: "identity",
            table: "Users",
            column: "NormalizedEmail");

        migrationBuilder.CreateIndex(
            name: "UX_Users_ExternalIssuer_ExternalSubjectId",
            schema: "identity",
            table: "Users",
            columns: new[] { "ExternalIssuer", "ExternalSubjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_WebhookEvents_Status_NextAttemptAtUtc",
            schema: "commerce",
            table: "WebhookEvents",
            columns: new[] { "Status", "NextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "UX_WebhookEvents_Provider_ExternalEventId",
            schema: "commerce",
            table: "WebhookEvents",
            columns: new[] { "Provider", "ExternalEventId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditLogs",
            schema: "audit");

        migrationBuilder.DropTable(
            name: "CourseInstructors",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "CourseTags",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Enrollments",
            schema: "learning");

        migrationBuilder.DropTable(
            name: "Entitlements",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "LessonProgress",
            schema: "learning");

        migrationBuilder.DropTable(
            name: "LessonResources",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "LessonVideos",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "PaymentDisputes",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Refunds",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "StripeCustomers",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "UserRoles",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "WebhookEvents",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Tags",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "OrderItems",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Lessons",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Subscriptions",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Roles",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "Orders",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "CourseSections",
            schema: "catalog");

        migrationBuilder.DropTable(
            name: "Prices",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Users",
            schema: "identity");

        migrationBuilder.DropTable(
            name: "Offers",
            schema: "commerce");

        migrationBuilder.DropTable(
            name: "Courses",
            schema: "catalog");
    }
}
