using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCourseReviews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CourseReviews",
            schema: "learning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Rating = table.Column<int>(type: "int", nullable: false),
                Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                Status = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                ModerationReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                EditedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseReviews", x => x.Id);
                table.CheckConstraint("CK_CourseReviews_ModerationReason", "([Status] = 'Hidden' AND [ModerationReason] IS NOT NULL) OR ([Status] <> 'Hidden' AND [ModerationReason] IS NULL)");
                table.CheckConstraint("CK_CourseReviews_Rating", "[Rating] BETWEEN 1 AND 5");
                table.CheckConstraint("CK_CourseReviews_Status", "[Status] IN ('Published', 'Hidden', 'Deleted')");
                table.ForeignKey(
                    name: "FK_CourseReviews_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_CourseReviews_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_CourseReviews_CourseId_Status",
            schema: "learning",
            table: "CourseReviews",
            columns: new[] { "CourseId", "Status" });

        migrationBuilder.CreateIndex(
            name: "UX_CourseReviews_UserId_CourseId",
            schema: "learning",
            table: "CourseReviews",
            columns: new[] { "UserId", "CourseId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "CourseReviews",
            schema: "learning");
    }
}
