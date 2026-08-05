using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForumSolvedAnswer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SolvedPostId",
                schema: "community",
                table: "ForumThreads",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForumThreads_Id_SolvedPostId",
                schema: "community",
                table: "ForumThreads",
                columns: new[] { "Id", "SolvedPostId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ForumThreads_SolvedPost_SameThread",
                schema: "community",
                table: "ForumThreads",
                columns: new[] { "Id", "SolvedPostId" },
                principalSchema: "community",
                principalTable: "ForumPosts",
                principalColumns: new[] { "ThreadId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ForumThreads_SolvedPost_SameThread",
                schema: "community",
                table: "ForumThreads");

            migrationBuilder.DropIndex(
                name: "IX_ForumThreads_Id_SolvedPostId",
                schema: "community",
                table: "ForumThreads");

            migrationBuilder.DropColumn(
                name: "SolvedPostId",
                schema: "community",
                table: "ForumThreads");
        }
    }
}
