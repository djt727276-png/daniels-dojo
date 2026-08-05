using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMediaPipelineAndSourceVerification : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.EnsureSchema(
            name: "media");

        migrationBuilder.AlterColumn<string>(
            name: "MuxPlaybackId",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(128)",
            unicode: false,
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "MuxAssetId",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(128)",
            unicode: false,
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FailureCode",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(64)",
            unicode: false,
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(64)",
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AdminPlaybackVerifiedAtUtc",
            schema: "catalog",
            table: "LessonVideos",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AspectRatio",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(16)",
            unicode: false,
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "HumanSpotCheckAtUtc",
            schema: "catalog",
            table: "LessonVideos",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsSignedPlayback",
            schema: "catalog",
            table: "LessonVideos",
            type: "bit",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "LastKnownGoodAssetId",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(128)",
            unicode: false,
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastKnownGoodPlaybackId",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(128)",
            unicode: false,
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastProviderEventAtUtc",
            schema: "catalog",
            table: "LessonVideos",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "MuxUploadId",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(128)",
            unicode: false,
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ProviderMode",
            schema: "catalog",
            table: "LessonVideos",
            type: "varchar(32)",
            unicode: false,
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "StudentPlaybackVerifiedAtUtc",
            schema: "catalog",
            table: "LessonVideos",
            type: "datetimeoffset(7)",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "MediaSourceId",
            schema: "catalog",
            table: "LessonResources",
            type: "uniqueidentifier",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "UploadSessions",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Purpose = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ContainerName = table.Column<string>(type: "varchar(63)", unicode: false, maxLength: 63, nullable: false),
                BlobName = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                OriginalFileName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                DeclaredContentType = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                IsReplacement = table.Column<bool>(type: "bit", nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                ProviderMode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                FailureCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UploadSessions", x => x.Id);
                table.CheckConstraint("CK_UploadSessions_CompletedAt", "([Status] = 'Completed' AND [CompletedAtUtc] IS NOT NULL) OR ([Status] <> 'Completed' AND [CompletedAtUtc] IS NULL)");
                table.CheckConstraint("CK_UploadSessions_DeclaredSize_Positive", "[DeclaredSizeBytes] > 0");
                table.CheckConstraint("CK_UploadSessions_LessonScope", "([Purpose] IN ('LessonVideo', 'LessonResource', 'CaptionTrack') AND [LessonId] IS NOT NULL) OR ([Purpose] IN ('CourseImage', 'Avatar') AND [LessonId] IS NULL)");
                table.CheckConstraint("CK_UploadSessions_ProviderMode", "[ProviderMode] IN ('Disabled', 'Deterministic', 'Real')");
                table.CheckConstraint("CK_UploadSessions_Purpose", "[Purpose] IN ('LessonVideo', 'LessonResource', 'CourseImage', 'CaptionTrack', 'Avatar')");
                table.CheckConstraint("CK_UploadSessions_Status", "[Status] IN ('Requested', 'Uploading', 'Completed', 'Expired', 'Cancelled', 'Failed')");
                table.ForeignKey(
                    name: "FK_UploadSessions_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UploadSessions_Lessons_LessonId",
                    column: x => x.LessonId,
                    principalSchema: "catalog",
                    principalTable: "Lessons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_UploadSessions_Users_RequestedByUserId",
                    column: x => x.RequestedByUserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Sources",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UploadSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Purpose = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                ContainerName = table.Column<string>(type: "varchar(63)", unicode: false, maxLength: 63, nullable: false),
                BlobName = table.Column<string>(type: "varchar(512)", unicode: false, maxLength: 512, nullable: false),
                BlobVersionId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                ETag = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                ContentLength = table.Column<long>(type: "bigint", nullable: false),
                ContentType = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                ContentMd5Base64 = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                ChecksumSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: true),
                State = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                ProviderMode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                PropertiesVerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RestoreVerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RestoreVerifiedLength = table.Column<long>(type: "bigint", nullable: true),
                SupersededAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Sources", x => x.Id);
                table.CheckConstraint("CK_Sources_ContentLength_Positive", "[ContentLength] > 0");
                table.CheckConstraint("CK_Sources_LessonScope", "([Purpose] IN ('LessonVideo', 'LessonResource', 'CaptionTrack') AND [LessonId] IS NOT NULL) OR ([Purpose] IN ('CourseImage', 'Avatar') AND [LessonId] IS NULL)");
                table.CheckConstraint("CK_Sources_ProviderMode", "[ProviderMode] IN ('Disabled', 'Deterministic', 'Real')");
                table.CheckConstraint("CK_Sources_Purpose", "[Purpose] IN ('LessonVideo', 'LessonResource', 'CourseImage', 'CaptionTrack', 'Avatar')");
                table.CheckConstraint("CK_Sources_RestoreEvidenceComplete", "([RestoreVerifiedAtUtc] IS NULL AND [RestoreVerifiedLength] IS NULL) OR ([RestoreVerifiedAtUtc] IS NOT NULL AND [RestoreVerifiedLength] IS NOT NULL)");
                table.CheckConstraint("CK_Sources_RestoreVerifiedLength_NonNegative", "[RestoreVerifiedLength] IS NULL OR [RestoreVerifiedLength] >= 0");
                table.CheckConstraint("CK_Sources_State", "[State] IN ('Pending', 'Current', 'Superseded', 'Archived')");
                table.CheckConstraint("CK_Sources_SupersededAt", "([State] IN ('Superseded', 'Archived') AND [SupersededAtUtc] IS NOT NULL) OR ([State] IN ('Pending', 'Current') AND [SupersededAtUtc] IS NULL)");
                table.ForeignKey(
                    name: "FK_Sources_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Sources_Lessons_LessonId",
                    column: x => x.LessonId,
                    principalSchema: "catalog",
                    principalTable: "Lessons",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Sources_UploadSessions_UploadSessionId",
                    column: x => x.UploadSessionId,
                    principalSchema: "media",
                    principalTable: "UploadSessions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CaptionTracks",
            schema: "media",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LessonVideoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                MediaSourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                LanguageCode = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                IsDefault = table.Column<bool>(type: "bit", nullable: false),
                ProviderTrackId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                FailureCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CaptionTracks", x => x.Id);
                table.CheckConstraint("CK_CaptionTracks_LanguageCode_NotBlank", "LEN(LTRIM(RTRIM([LanguageCode]))) > 0");
                table.CheckConstraint("CK_CaptionTracks_Status", "[Status] IN ('Requested', 'Uploading', 'AzureStored', 'MuxIngesting', 'Processing', 'Ready', 'Failed', 'Replacing', 'Archived')");
                table.ForeignKey(
                    name: "FK_CaptionTracks_LessonVideos_LessonVideoId",
                    column: x => x.LessonVideoId,
                    principalSchema: "catalog",
                    principalTable: "LessonVideos",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CaptionTracks_Sources_MediaSourceId",
                    column: x => x.MediaSourceId,
                    principalSchema: "media",
                    principalTable: "Sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_LessonVideos_CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos",
            column: "CurrentSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_LessonVideos_HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos",
            column: "HumanSpotCheckByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_LessonVideos_IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos",
            column: "IncomingSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "UX_LessonVideos_MuxUploadId",
            schema: "catalog",
            table: "LessonVideos",
            column: "MuxUploadId",
            unique: true,
            filter: "[MuxUploadId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_FailureCode",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[Status] <> 'Failed' OR [FailureCode] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_ProviderMode",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[ProviderMode] IN ('Disabled', 'Deterministic', 'Real')");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_ReadyRequiresPlayback",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[Status] <> 'Ready' OR [MuxPlaybackId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_ReplacingRequiresLastKnownGood",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[Status] <> 'Replacing' OR [LastKnownGoodPlaybackId] IS NOT NULL");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_SpotCheckActor",
            schema: "catalog",
            table: "LessonVideos",
            sql: "([HumanSpotCheckAtUtc] IS NULL AND [HumanSpotCheckByUserId] IS NULL) OR ([HumanSpotCheckAtUtc] IS NOT NULL AND [HumanSpotCheckByUserId] IS NOT NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[Status] IN ('Requested', 'Uploading', 'AzureStored', 'MuxIngesting', 'Processing', 'Ready', 'Failed', 'Replacing', 'Archived')");

        migrationBuilder.CreateIndex(
            name: "IX_LessonResources_MediaSourceId",
            schema: "catalog",
            table: "LessonResources",
            column: "MediaSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_CaptionTracks_MediaSourceId",
            schema: "media",
            table: "CaptionTracks",
            column: "MediaSourceId");

        migrationBuilder.CreateIndex(
            name: "UX_CaptionTracks_LessonVideoId_LanguageCode",
            schema: "media",
            table: "CaptionTracks",
            columns: new[] { "LessonVideoId", "LanguageCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_CaptionTracks_ProviderTrackId",
            schema: "media",
            table: "CaptionTracks",
            column: "ProviderTrackId",
            unique: true,
            filter: "[ProviderTrackId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_Sources_CourseId_Purpose_State",
            schema: "media",
            table: "Sources",
            columns: new[] { "CourseId", "Purpose", "State" });

        migrationBuilder.CreateIndex(
            name: "UX_Sources_LessonId_Purpose_Current",
            schema: "media",
            table: "Sources",
            columns: new[] { "LessonId", "Purpose" },
            unique: true,
            filter: "[State] = 'Current' AND [LessonId] IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "UX_Sources_UploadSessionId",
            schema: "media",
            table: "Sources",
            column: "UploadSessionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_CourseId_Status",
            schema: "media",
            table: "UploadSessions",
            columns: new[] { "CourseId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_ExpiresAtUtc",
            schema: "media",
            table: "UploadSessions",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_LessonId",
            schema: "media",
            table: "UploadSessions",
            column: "LessonId");

        migrationBuilder.CreateIndex(
            name: "IX_UploadSessions_RequestedByUserId",
            schema: "media",
            table: "UploadSessions",
            column: "RequestedByUserId");

        migrationBuilder.CreateIndex(
            name: "UX_UploadSessions_BlobName",
            schema: "media",
            table: "UploadSessions",
            column: "BlobName",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_LessonResources_Sources_MediaSourceId",
            schema: "catalog",
            table: "LessonResources",
            column: "MediaSourceId",
            principalSchema: "media",
            principalTable: "Sources",
            principalColumn: "Id");

        migrationBuilder.AddForeignKey(
            name: "FK_LessonVideos_Sources_CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos",
            column: "CurrentSourceId",
            principalSchema: "media",
            principalTable: "Sources",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_LessonVideos_Sources_IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos",
            column: "IncomingSourceId",
            principalSchema: "media",
            principalTable: "Sources",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_LessonVideos_Users_HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos",
            column: "HumanSpotCheckByUserId",
            principalSchema: "identity",
            principalTable: "Users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_LessonResources_Sources_MediaSourceId",
            schema: "catalog",
            table: "LessonResources");

        migrationBuilder.DropForeignKey(
            name: "FK_LessonVideos_Sources_CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropForeignKey(
            name: "FK_LessonVideos_Sources_IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropForeignKey(
            name: "FK_LessonVideos_Users_HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropTable(
            name: "CaptionTracks",
            schema: "media");

        migrationBuilder.DropTable(
            name: "Sources",
            schema: "media");

        migrationBuilder.DropTable(
            name: "UploadSessions",
            schema: "media");

        migrationBuilder.DropIndex(
            name: "IX_LessonVideos_CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropIndex(
            name: "IX_LessonVideos_HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropIndex(
            name: "IX_LessonVideos_IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropIndex(
            name: "IX_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropIndex(
            name: "UX_LessonVideos_MuxUploadId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_FailureCode",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_ProviderMode",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_ReadyRequiresPlayback",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_ReplacingRequiresLastKnownGood",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_SpotCheckActor",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropCheckConstraint(
            name: "CK_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropIndex(
            name: "IX_LessonResources_MediaSourceId",
            schema: "catalog",
            table: "LessonResources");

        migrationBuilder.DropColumn(
            name: "AdminPlaybackVerifiedAtUtc",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "AspectRatio",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "CurrentSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "HumanSpotCheckAtUtc",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "HumanSpotCheckByUserId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "IncomingSourceId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "IsSignedPlayback",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "LastKnownGoodAssetId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "LastKnownGoodPlaybackId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "LastProviderEventAtUtc",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "MuxUploadId",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "ProviderMode",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "StudentPlaybackVerifiedAtUtc",
            schema: "catalog",
            table: "LessonVideos");

        migrationBuilder.DropColumn(
            name: "MediaSourceId",
            schema: "catalog",
            table: "LessonResources");

        migrationBuilder.AlterColumn<string>(
            name: "MuxPlaybackId",
            schema: "catalog",
            table: "LessonVideos",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "varchar(128)",
            oldUnicode: false,
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "MuxAssetId",
            schema: "catalog",
            table: "LessonVideos",
            type: "nvarchar(128)",
            maxLength: 128,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "varchar(128)",
            oldUnicode: false,
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "FailureCode",
            schema: "catalog",
            table: "LessonVideos",
            type: "nvarchar(64)",
            maxLength: 64,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "varchar(64)",
            oldUnicode: false,
            oldMaxLength: 64,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_LessonVideos_Status",
            schema: "catalog",
            table: "LessonVideos",
            sql: "[Status] IN ('Pending', 'Preparing', 'Ready', 'Errored', 'Disabled')");
    }
}
