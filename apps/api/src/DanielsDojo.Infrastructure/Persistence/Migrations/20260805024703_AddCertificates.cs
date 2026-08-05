using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCertificates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Certificates",
            schema: "learning",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                CourseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                VerificationCode = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                CourseTitleAtIssue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                HolderNameAtIssue = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: true),
                RevocationReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", nullable: false),
                RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Certificates", x => x.Id);
                table.CheckConstraint("CK_Certificates_RevocationReason", "([RevokedAtUtc] IS NULL AND [RevocationReason] IS NULL) OR ([RevokedAtUtc] IS NOT NULL AND [RevocationReason] IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_Certificates_Courses_CourseId",
                    column: x => x.CourseId,
                    principalSchema: "catalog",
                    principalTable: "Courses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_Certificates_Users_UserId",
                    column: x => x.UserId,
                    principalSchema: "identity",
                    principalTable: "Users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Certificates_CourseId",
            schema: "learning",
            table: "Certificates",
            column: "CourseId");

        migrationBuilder.CreateIndex(
            name: "UX_Certificates_UserId_CourseId",
            schema: "learning",
            table: "Certificates",
            columns: new[] { "UserId", "CourseId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "UX_Certificates_VerificationCode",
            schema: "learning",
            table: "Certificates",
            column: "VerificationCode",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Certificates",
            schema: "learning");
    }
}
