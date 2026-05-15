using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Repos.Migrations
{
    /// <inheritdoc />
    public partial class SyncProductStockFromSteamKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration corrects Products.Stock to match actual available Steam keys.
            // Available keys = Status == 0 (Available) AND OrderId IS NULL AND InvalidatedAt IS NULL
            //
            // This fix addresses the root cause where:
            // 1. SteamKeyService.BulkUploadAsync was not updating Products.Stock
            // 2. CheckoutService.CreatePaymentAsync was incorrectly decrementing Products.Stock
            //    at payment creation time instead of fulfillment time
            //
            // The new architecture:
            // - Canonical inventory source = Steam keys (Status=0, OrderId=null, InvalidatedAt=null)
            // - Products.Stock is now kept synchronized as a derived field
            // - Stock is synced: on key upload, key deletion, key enable/disable, and order fulfillment

            // SQL to sync Products.Stock with actual available Steam key counts
            // This updates the legacy Products.Stock field to match the canonical source
            migrationBuilder.Sql(@"
                UPDATE ""Products""
                SET ""Stock"" = (
                    SELECT COUNT(*)
                    FROM ""SteamKeys""
                    WHERE ""SteamKeys"".""ProductId"" = ""Products"".""Id""
                      AND ""SteamKeys"".""Status"" = 0
                      AND ""SteamKeys"".""OrderId"" IS NULL
                      AND ""SteamKeys"".""InvalidatedAt"" IS NULL
                )
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: This migration only corrects existing data, not schema
        }
    }
}
