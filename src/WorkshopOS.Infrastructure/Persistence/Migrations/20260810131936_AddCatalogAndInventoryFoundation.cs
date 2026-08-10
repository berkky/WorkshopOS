using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkshopOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogAndInventoryFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartCatalogItems", x => x.Id);
                    table.UniqueConstraint("AK_PartCatalogItems_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PartCatalogItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ServiceCatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    DefaultUnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCatalogItems", x => x.Id);
                    table.UniqueConstraint("AK_ServiceCatalogItems_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_ServiceCatalogItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartInventoryBalances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkshopLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityOnHand = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartInventoryBalances", x => x.Id);
                    table.UniqueConstraint("AK_PartInventoryBalances_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.UniqueConstraint("AK_PartInventoryBalances_OrganizationId_PartCatalogItemId_Work~", x => new { x.OrganizationId, x.PartCatalogItemId, x.WorkshopLocationId });
                    table.CheckConstraint("CK_PartInventoryBalances_QuantityOnHand_NonNegative", "\"QuantityOnHand\" >= 0");
                    table.ForeignKey(
                        name: "FK_PartInventoryBalances_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartInventoryBalances_PartCatalogItems_OrganizationId_PartC~",
                        columns: x => new { x.OrganizationId, x.PartCatalogItemId },
                        principalTable: "PartCatalogItems",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartInventoryBalances_WorkshopLocations_OrganizationId_Work~",
                        columns: x => new { x.OrganizationId, x.WorkshopLocationId },
                        principalTable: "WorkshopLocations",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartInventoryMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartCatalogItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkshopLocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MovementType = table.Column<int>(type: "integer", nullable: false),
                    QuantityDelta = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartInventoryMovements", x => x.Id);
                    table.UniqueConstraint("AK_PartInventoryMovements_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_PartInventoryMovements_OrganizationMemberships_Organization~",
                        columns: x => new { x.OrganizationId, x.RecordedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartInventoryMovements_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PartInventoryMovements_PartInventoryBalances_OrganizationId~",
                        columns: x => new { x.OrganizationId, x.PartCatalogItemId, x.WorkshopLocationId },
                        principalTable: "PartInventoryBalances",
                        principalColumns: new[] { "OrganizationId", "PartCatalogItemId", "WorkshopLocationId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogItems_OrganizationId_Name",
                table: "PartCatalogItems",
                columns: new[] { "OrganizationId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_PartCatalogItems_OrganizationId_Sku",
                table: "PartCatalogItems",
                columns: new[] { "OrganizationId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartInventoryBalances_OrganizationId_PartCatalogItemId_Work~",
                table: "PartInventoryBalances",
                columns: new[] { "OrganizationId", "PartCatalogItemId", "WorkshopLocationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PartInventoryBalances_OrganizationId_WorkshopLocationId",
                table: "PartInventoryBalances",
                columns: new[] { "OrganizationId", "WorkshopLocationId" });

            migrationBuilder.CreateIndex(
                name: "IX_PartInventoryMovements_OrganizationId_PartCatalogItemId_Wor~",
                table: "PartInventoryMovements",
                columns: new[] { "OrganizationId", "PartCatalogItemId", "WorkshopLocationId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PartInventoryMovements_OrganizationId_RecordedByUserId",
                table: "PartInventoryMovements",
                columns: new[] { "OrganizationId", "RecordedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCatalogItems_OrganizationId_Code",
                table: "ServiceCatalogItems",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCatalogItems_OrganizationId_Name",
                table: "ServiceCatalogItems",
                columns: new[] { "OrganizationId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartInventoryMovements");

            migrationBuilder.DropTable(
                name: "ServiceCatalogItems");

            migrationBuilder.DropTable(
                name: "PartInventoryBalances");

            migrationBuilder.DropTable(
                name: "PartCatalogItems");
        }
    }
}
