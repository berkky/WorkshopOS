using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkshopOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInspectionMediaFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_InspectionItems_OrganizationId_InspectionId_Id",
                table: "InspectionItems",
                columns: new[] { "OrganizationId", "InspectionId", "Id" });

            migrationBuilder.CreateTable(
                name: "InspectionMediaAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MediaKind = table.Column<int>(type: "integer", nullable: false),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LengthBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Caption = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionMediaAssets", x => x.Id);
                    table.UniqueConstraint("AK_InspectionMediaAssets_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_InspectionMediaAssets_InspectionItems_OrganizationId_Inspec~",
                        columns: x => new { x.OrganizationId, x.InspectionId, x.InspectionItemId },
                        principalTable: "InspectionItems",
                        principalColumns: new[] { "OrganizationId", "InspectionId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionMediaAssets_Inspections_OrganizationId_Inspection~",
                        columns: x => new { x.OrganizationId, x.InspectionId },
                        principalTable: "Inspections",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionMediaAssets_OrganizationMemberships_OrganizationI~",
                        columns: x => new { x.OrganizationId, x.RemovedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionMediaAssets_OrganizationMemberships_Organization~1",
                        columns: x => new { x.OrganizationId, x.UploadedByUserId },
                        principalTable: "OrganizationMemberships",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InspectionMediaAssets_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionMediaAssets_OrganizationId_InspectionId_Inspectio~",
                table: "InspectionMediaAssets",
                columns: new[] { "OrganizationId", "InspectionId", "InspectionItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionMediaAssets_OrganizationId_InspectionId_RemovedAt~",
                table: "InspectionMediaAssets",
                columns: new[] { "OrganizationId", "InspectionId", "RemovedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionMediaAssets_OrganizationId_RemovedByUserId",
                table: "InspectionMediaAssets",
                columns: new[] { "OrganizationId", "RemovedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionMediaAssets_OrganizationId_UploadedByUserId",
                table: "InspectionMediaAssets",
                columns: new[] { "OrganizationId", "UploadedByUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionMediaAssets_StorageKey",
                table: "InspectionMediaAssets",
                column: "StorageKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionMediaAssets");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InspectionItems_OrganizationId_InspectionId_Id",
                table: "InspectionItems");
        }
    }
}
