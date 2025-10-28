using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NewsApplication.Repository.Migrations
{
    /// <inheritdoc />
    public partial class RemovedArticleScopeReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArticleCacheItem_ArticleCache_ArticleCacheId",
                table: "ArticleCacheItem");

            migrationBuilder.DropForeignKey(
                name: "FK_ArticleCacheItem_Article_ArticleId",
                table: "ArticleCacheItem");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArticleCacheItem",
                table: "ArticleCacheItem");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArticleCache",
                table: "ArticleCache");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Article",
                table: "Article");

            migrationBuilder.RenameTable(
                name: "ArticleCacheItem",
                newName: "ArticleCacheItems");

            migrationBuilder.RenameTable(
                name: "ArticleCache",
                newName: "ArticleCaches");

            migrationBuilder.RenameTable(
                name: "Article",
                newName: "Articles");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCacheItem_ArticleId",
                table: "ArticleCacheItems",
                newName: "IX_ArticleCacheItems_ArticleId");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCache_ScopeKey_Page",
                table: "ArticleCaches",
                newName: "IX_ArticleCaches_ScopeKey_Page");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCache_ExpiresAt",
                table: "ArticleCaches",
                newName: "IX_ArticleCaches_ExpiresAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "InsertedAt",
                table: "Articles",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "NOW() AT TIME ZONE 'UTC'",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<List<string>>(
                name: "Categories",
                table: "Articles",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                oldClrType: typeof(List<string>),
                oldType: "jsonb");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArticleCacheItems",
                table: "ArticleCacheItems",
                columns: new[] { "ArticleCacheId", "ArticleId" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArticleCaches",
                table: "ArticleCaches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Articles",
                table: "Articles",
                column: "ArticleId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArticleCacheItems_ArticleCaches_ArticleCacheId",
                table: "ArticleCacheItems",
                column: "ArticleCacheId",
                principalTable: "ArticleCaches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ArticleCacheItems_Articles_ArticleId",
                table: "ArticleCacheItems",
                column: "ArticleId",
                principalTable: "Articles",
                principalColumn: "ArticleId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArticleCacheItems_ArticleCaches_ArticleCacheId",
                table: "ArticleCacheItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ArticleCacheItems_Articles_ArticleId",
                table: "ArticleCacheItems");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Articles",
                table: "Articles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArticleCaches",
                table: "ArticleCaches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ArticleCacheItems",
                table: "ArticleCacheItems");

            migrationBuilder.RenameTable(
                name: "Articles",
                newName: "Article");

            migrationBuilder.RenameTable(
                name: "ArticleCaches",
                newName: "ArticleCache");

            migrationBuilder.RenameTable(
                name: "ArticleCacheItems",
                newName: "ArticleCacheItem");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCaches_ScopeKey_Page",
                table: "ArticleCache",
                newName: "IX_ArticleCache_ScopeKey_Page");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCaches_ExpiresAt",
                table: "ArticleCache",
                newName: "IX_ArticleCache_ExpiresAt");

            migrationBuilder.RenameIndex(
                name: "IX_ArticleCacheItems_ArticleId",
                table: "ArticleCacheItem",
                newName: "IX_ArticleCacheItem_ArticleId");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "InsertedAt",
                table: "Article",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "NOW() AT TIME ZONE 'UTC'");

            migrationBuilder.AlterColumn<List<string>>(
                name: "Categories",
                table: "Article",
                type: "jsonb",
                nullable: false,
                oldClrType: typeof(List<string>),
                oldType: "jsonb",
                oldDefaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Article",
                table: "Article",
                column: "ArticleId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArticleCache",
                table: "ArticleCache",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ArticleCacheItem",
                table: "ArticleCacheItem",
                columns: new[] { "ArticleCacheId", "ArticleId" });

            migrationBuilder.AddForeignKey(
                name: "FK_ArticleCacheItem_ArticleCache_ArticleCacheId",
                table: "ArticleCacheItem",
                column: "ArticleCacheId",
                principalTable: "ArticleCache",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ArticleCacheItem_Article_ArticleId",
                table: "ArticleCacheItem",
                column: "ArticleId",
                principalTable: "Article",
                principalColumn: "ArticleId",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
