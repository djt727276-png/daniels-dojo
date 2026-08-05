using DanielsDojo.Domain.Catalog;
using DanielsDojo.Domain.Identity;
using DanielsDojo.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DanielsDojo.Infrastructure.Persistence.Configurations.Media;

/// <summary>
/// Maps <see cref="MediaUploadSession"/> to <c>media.UploadSessions</c>.
/// </summary>
/// <remarks>
/// The blob name is unique across the table, which is what makes the server-generated name a
/// real guarantee rather than a convention: two sessions can never be authorised against the
/// same object, so a second attempt cannot overwrite the first one's bytes.
/// </remarks>
internal sealed class MediaUploadSessionConfiguration : IEntityTypeConfiguration<MediaUploadSession>
{
    public void Configure(EntityTypeBuilder<MediaUploadSession> builder)
    {
        builder.ToTable("UploadSessions", DatabaseSchemas.Media, table =>
        {
            table.HasCheckConstraint(
                "CK_UploadSessions_Purpose",
                ColumnTypes.EnumValues<MediaPurpose>(nameof(MediaUploadSession.Purpose)));
            table.HasCheckConstraint(
                "CK_UploadSessions_Status",
                ColumnTypes.EnumValues<MediaUploadStatus>(nameof(MediaUploadSession.Status)));
            table.HasCheckConstraint(
                "CK_UploadSessions_ProviderMode",
                ColumnTypes.EnumValues<ProviderMode>(nameof(MediaUploadSession.ProviderMode)));
            table.HasCheckConstraint(
                "CK_UploadSessions_DeclaredSize_Positive",
                "[DeclaredSizeBytes] > 0");

            // Lesson-scoped purposes must name a lesson; course-scoped ones must not.
            table.HasCheckConstraint(
                "CK_UploadSessions_LessonScope",
                "([Purpose] IN ('LessonVideo', 'LessonResource', 'CaptionTrack') AND [LessonId] IS NOT NULL) "
                + "OR ([Purpose] IN ('CourseImage', 'Avatar') AND [LessonId] IS NULL)");

            // A completed session must record when, and only a completed one may.
            table.HasCheckConstraint(
                "CK_UploadSessions_CompletedAt",
                "([Status] = 'Completed' AND [CompletedAtUtc] IS NOT NULL) "
                + "OR ([Status] <> 'Completed' AND [CompletedAtUtc] IS NULL)");
        });

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.Purpose).AsEnumString();
        builder.Property(session => session.Status).AsEnumString();
        builder.Property(session => session.ProviderMode).AsEnumString();
        builder.Property(session => session.ContainerName).HasMaxLength(63).IsUnicode(false).IsRequired();
        builder.Property(session => session.BlobName).HasMaxLength(512).IsUnicode(false).IsRequired();
        builder.Property(session => session.OriginalFileName).HasMaxLength(256);
        builder.Property(session => session.DeclaredContentType).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(session => session.DeclaredSizeBytes).IsRequired();
        builder.Property(session => session.FailureCode).HasMaxLength(64).IsUnicode(false);
        builder.Property(session => session.ExpiresAtUtc).AsTimestamp();
        builder.Property(session => session.CompletedAtUtc).AsTimestamp();
        builder.Property(session => session.CreatedAtUtc).AsTimestamp();
        builder.Property(session => session.UpdatedAtUtc).AsTimestamp();
        builder.Property(session => session.RowVersion).IsRowVersion();

        builder.HasOne<Course>()
            .WithMany()
            .HasForeignKey(session => session.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(session => session.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(session => session.RequestedByUser)
            .WithMany()
            .HasForeignKey(session => session.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(session => session.BlobName)
            .IsUnique()
            .HasDatabaseName("UX_UploadSessions_BlobName");

        builder.HasIndex(session => new { session.CourseId, session.Status })
            .HasDatabaseName("IX_UploadSessions_CourseId_Status");

        builder.HasIndex(session => session.ExpiresAtUtc)
            .HasDatabaseName("IX_UploadSessions_ExpiresAtUtc");
    }
}

/// <summary>
/// Maps <see cref="MediaSource"/> to <c>media.Sources</c>.
/// </summary>
/// <remarks>
/// A filtered unique index allows at most one <see cref="MediaSourceState.Current"/> row per
/// lesson purpose. The database, not the application, is what guarantees a lesson cannot end up
/// serving two different masters after a half-finished replacement.
/// </remarks>
internal sealed class MediaSourceConfiguration : IEntityTypeConfiguration<MediaSource>
{
    public void Configure(EntityTypeBuilder<MediaSource> builder)
    {
        builder.ToTable("Sources", DatabaseSchemas.Media, table =>
        {
            table.HasCheckConstraint(
                "CK_Sources_Purpose",
                ColumnTypes.EnumValues<MediaPurpose>(nameof(MediaSource.Purpose)));
            table.HasCheckConstraint(
                "CK_Sources_State",
                ColumnTypes.EnumValues<MediaSourceState>(nameof(MediaSource.State)));
            table.HasCheckConstraint(
                "CK_Sources_ProviderMode",
                ColumnTypes.EnumValues<ProviderMode>(nameof(MediaSource.ProviderMode)));
            table.HasCheckConstraint(
                "CK_Sources_ContentLength_Positive",
                "[ContentLength] > 0");
            table.HasCheckConstraint(
                "CK_Sources_RestoreVerifiedLength_NonNegative",
                "[RestoreVerifiedLength] IS NULL OR [RestoreVerifiedLength] >= 0");

            // A superseded or archived object records when it stopped being current.
            table.HasCheckConstraint(
                "CK_Sources_SupersededAt",
                "([State] IN ('Superseded', 'Archived') AND [SupersededAtUtc] IS NOT NULL) "
                + "OR ([State] IN ('Pending', 'Current') AND [SupersededAtUtc] IS NULL)");

            // A restore result is only meaningful alongside the length it was measured against.
            table.HasCheckConstraint(
                "CK_Sources_RestoreEvidenceComplete",
                "([RestoreVerifiedAtUtc] IS NULL AND [RestoreVerifiedLength] IS NULL) "
                + "OR ([RestoreVerifiedAtUtc] IS NOT NULL AND [RestoreVerifiedLength] IS NOT NULL)");

            table.HasCheckConstraint(
                "CK_Sources_LessonScope",
                "([Purpose] IN ('LessonVideo', 'LessonResource', 'CaptionTrack') AND [LessonId] IS NOT NULL) "
                + "OR ([Purpose] IN ('CourseImage', 'Avatar') AND [LessonId] IS NULL)");
        });

        builder.HasKey(source => source.Id);
        builder.Property(source => source.Id).ValueGeneratedNever();

        builder.Property(source => source.Purpose).AsEnumString();
        builder.Property(source => source.State).AsEnumString();
        builder.Property(source => source.ProviderMode).AsEnumString();
        builder.Property(source => source.ContainerName).HasMaxLength(63).IsUnicode(false).IsRequired();
        builder.Property(source => source.BlobName).HasMaxLength(512).IsUnicode(false).IsRequired();
        builder.Property(source => source.BlobVersionId).HasMaxLength(64).IsUnicode(false);
        builder.Property(source => source.ETag).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(source => source.ContentType).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(source => source.ContentMd5Base64).HasMaxLength(32).IsUnicode(false);
        builder.Property(source => source.ChecksumSha256).HasMaxLength(64).IsUnicode(false).IsFixedLength();
        builder.Property(source => source.ContentLength).IsRequired();
        builder.Property(source => source.PropertiesVerifiedAtUtc).AsTimestamp();
        builder.Property(source => source.RestoreVerifiedAtUtc).AsTimestamp();
        builder.Property(source => source.SupersededAtUtc).AsTimestamp();
        builder.Property(source => source.CreatedAtUtc).AsTimestamp();
        builder.Property(source => source.UpdatedAtUtc).AsTimestamp();
        builder.Property(source => source.RowVersion).IsRowVersion();

        builder.HasOne(source => source.UploadSession)
            .WithMany()
            .HasForeignKey(source => source.UploadSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Course>()
            .WithMany()
            .HasForeignKey(source => source.CourseId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Lesson>()
            .WithMany()
            .HasForeignKey(source => source.LessonId)
            .OnDelete(DeleteBehavior.Restrict);

        // One upload attempt produces at most one stored object.
        builder.HasIndex(source => source.UploadSessionId)
            .IsUnique()
            .HasDatabaseName("UX_Sources_UploadSessionId");

        // At most one current object per lesson purpose.
        builder.HasIndex(source => new { source.LessonId, source.Purpose })
            .IsUnique()
            .HasFilter("[State] = 'Current' AND [LessonId] IS NOT NULL")
            .HasDatabaseName("UX_Sources_LessonId_Purpose_Current");

        builder.HasIndex(source => new { source.CourseId, source.Purpose, source.State })
            .HasDatabaseName("IX_Sources_CourseId_Purpose_State");
    }
}

/// <summary>Maps <see cref="MediaCaptionTrack"/> to <c>media.CaptionTracks</c>.</summary>
internal sealed class MediaCaptionTrackConfiguration : IEntityTypeConfiguration<MediaCaptionTrack>
{
    public void Configure(EntityTypeBuilder<MediaCaptionTrack> builder)
    {
        builder.ToTable("CaptionTracks", DatabaseSchemas.Media, table =>
        {
            table.HasCheckConstraint(
                "CK_CaptionTracks_Status",
                ColumnTypes.EnumValues<LessonVideoStatus>(nameof(MediaCaptionTrack.Status)));
            table.HasCheckConstraint(
                "CK_CaptionTracks_LanguageCode_NotBlank",
                "LEN(LTRIM(RTRIM([LanguageCode]))) > 0");
        });

        builder.HasKey(track => track.Id);
        builder.Property(track => track.Id).ValueGeneratedNever();

        builder.Property(track => track.LanguageCode).HasMaxLength(16).IsUnicode(false).IsRequired();
        builder.Property(track => track.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(track => track.ProviderTrackId).HasMaxLength(128).IsUnicode(false);
        builder.Property(track => track.Status).AsEnumString();
        builder.Property(track => track.FailureCode).HasMaxLength(64).IsUnicode(false);
        builder.Property(track => track.CreatedAtUtc).AsTimestamp();
        builder.Property(track => track.UpdatedAtUtc).AsTimestamp();
        builder.Property(track => track.RowVersion).IsRowVersion();

        builder.HasOne(track => track.LessonVideo)
            .WithMany(video => video.CaptionTracks)
            .HasForeignKey(track => track.LessonVideoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(track => track.MediaSource)
            .WithMany()
            .HasForeignKey(track => track.MediaSourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // One track per language per video.
        builder.HasIndex(track => new { track.LessonVideoId, track.LanguageCode })
            .IsUnique()
            .HasDatabaseName("UX_CaptionTracks_LessonVideoId_LanguageCode");

        builder.HasIndex(track => track.ProviderTrackId)
            .IsUnique()
            .HasFilter("[ProviderTrackId] IS NOT NULL")
            .HasDatabaseName("UX_CaptionTracks_ProviderTrackId");
    }
}
