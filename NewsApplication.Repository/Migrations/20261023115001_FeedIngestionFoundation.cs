using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class FeedIngestionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NewsSourceFeeds_IsActive_LastPolledAt",
                table: "NewsSourceFeeds");

            migrationBuilder.AddColumn<int>(
                name: "ConsecutiveFailures",
                table: "NewsSourceFeeds",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastCompletionKey",
                table: "NewsSourceFeeds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastCompletionLeaseId",
                table: "NewsSourceFeeds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorMessage",
                table: "NewsSourceFeeds",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastErrorType",
                table: "NewsSourceFeeds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastHttpStatus",
                table: "NewsSourceFeeds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastModified",
                table: "NewsSourceFeeds",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSuccessfulPollAt",
                table: "NewsSourceFeeds",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "NewsSourceFeeds",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LeaseId",
                table: "NewsSourceFeeds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "NewsSourceFeeds",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LeaseVersion",
                table: "NewsSourceFeeds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextPollAt",
                table: "NewsSourceFeeds",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedUrl",
                table: "NewsSourceFeeds",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "Articles",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrlHash",
                table: "Articles",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "Articles",
                type: "character varying(71)",
                maxLength: 71,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentText",
                table: "Articles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExtractedAt",
                table: "Articles",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractionStatus",
                table: "Articles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Extractor",
                table: "Articles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractorVersion",
                table: "Articles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Articles",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedTime",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArticleFeedEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsSourceFeedId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalEntryId = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EntryUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    EntryUrlHash = table.Column<string>(type: "character varying(71)", maxLength: 71, nullable: false),
                    FeedPublishedAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArticleFeedEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArticleFeedEntries_Articles_ArticleId",
                        column: x => x.ArticleId,
                        principalTable: "Articles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArticleFeedEntries_NewsSourceFeeds_NewsSourceFeedId",
                        column: x => x.NewsSourceFeedId,
                        principalTable: "NewsSourceFeeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_IsActive_NextPollAt_LeaseExpiresAt",
                table: "NewsSourceFeeds",
                columns: new[] { "IsActive", "NextPollAt", "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_LastCompletionLeaseId",
                table: "NewsSourceFeeds",
                column: "LastCompletionLeaseId",
                filter: "\"LastCompletionLeaseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_LeaseId",
                table: "NewsSourceFeeds",
                column: "LeaseId",
                unique: true,
                filter: "\"LeaseId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Articles_CanonicalUrlHash",
                table: "Articles",
                column: "CanonicalUrlHash",
                unique: true,
                filter: "\"CanonicalUrlHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleFeedEntries_ArticleId",
                table: "ArticleFeedEntries",
                column: "ArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_ArticleFeedEntries_NewsSourceFeedId_EntryUrlHash",
                table: "ArticleFeedEntries",
                columns: new[] { "NewsSourceFeedId", "EntryUrlHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArticleFeedEntries_NewsSourceFeedId_ExternalEntryId",
                table: "ArticleFeedEntries",
                columns: new[] { "NewsSourceFeedId", "ExternalEntryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArticleFeedEntries");

            migrationBuilder.DropIndex(
                name: "IX_NewsSourceFeeds_IsActive_NextPollAt_LeaseExpiresAt",
                table: "NewsSourceFeeds");

            migrationBuilder.DropIndex(
                name: "IX_NewsSourceFeeds_LastCompletionLeaseId",
                table: "NewsSourceFeeds");

            migrationBuilder.DropIndex(
                name: "IX_NewsSourceFeeds_LeaseId",
                table: "NewsSourceFeeds");

            migrationBuilder.DropIndex(
                name: "IX_Articles_CanonicalUrlHash",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ConsecutiveFailures",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastCompletionKey",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastCompletionLeaseId",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastErrorMessage",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastErrorType",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastHttpStatus",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LastSuccessfulPollAt",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "LeaseVersion",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "NextPollAt",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "ResolvedUrl",
                table: "NewsSourceFeeds");

            migrationBuilder.DropColumn(
                name: "Author",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "CanonicalUrlHash",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ContentText",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ExtractedAt",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ExtractionStatus",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "Extractor",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ExtractorVersion",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ModifiedTime",
                table: "Articles");

            migrationBuilder.CreateIndex(
                name: "IX_NewsSourceFeeds_IsActive_LastPolledAt",
                table: "NewsSourceFeeds",
                columns: new[] { "IsActive", "LastPolledAt" });
        }
    }
}
