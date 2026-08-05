using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DanielsDojo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendNotificationKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                schema: "community",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                schema: "community",
                table: "Notifications",
                sql: "[Kind] IN ('FriendRequest', 'FriendAccepted', 'ThreadReply', 'PostReaction', 'DirectMessage', 'Moderation', 'CourseAnnouncement', 'PurchaseCompleted', 'CourseCompleted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                schema: "community",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                schema: "community",
                table: "Notifications",
                sql: "[Kind] IN ('FriendRequest', 'FriendAccepted', 'ThreadReply', 'PostReaction', 'DirectMessage', 'Moderation')");
        }
    }
}
