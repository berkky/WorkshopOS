using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkshopOS.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkshopOperationsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "RepairOrders",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "RepairOrderTechnicianAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RepairOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffMemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkStatus = table.Column<int>(type: "integer", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    WorkCompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UnassignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepairOrderTechnicianAssignments", x => x.Id);
                    table.UniqueConstraint("AK_RepairOrderTechnicianAssignments_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_RepairOrderTechnicianAssignments_Organizations_Organization~",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepairOrderTechnicianAssignments_RepairOrders_OrganizationI~",
                        columns: x => new { x.OrganizationId, x.RepairOrderId },
                        principalTable: "RepairOrders",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RepairOrderTechnicianAssignments_StaffMembers_OrganizationI~",
                        columns: x => new { x.OrganizationId, x.StaffMemberId },
                        principalTable: "StaffMembers",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrderTechnicianAssignments_OrganizationId_RepairOrder~",
                table: "RepairOrderTechnicianAssignments",
                columns: new[] { "OrganizationId", "RepairOrderId" },
                unique: true,
                filter: "\"UnassignedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RepairOrderTechnicianAssignments_OrganizationId_StaffMember~",
                table: "RepairOrderTechnicianAssignments",
                columns: new[] { "OrganizationId", "StaffMemberId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RepairOrderTechnicianAssignments");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "RepairOrders");
        }
    }
}
