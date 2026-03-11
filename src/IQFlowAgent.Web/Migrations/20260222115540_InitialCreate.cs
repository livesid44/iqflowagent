using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IQFlowAgent.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 450, nullable: false),
                    Name = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(maxLength: 450, nullable: false),
                    FullName = table.Column<string>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    LastLogin = table.Column<DateTime>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    UserName = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(maxLength: 256, nullable: true),
                    Email = table.Column<string>(maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(nullable: false),
                    PasswordHash = table.Column<string>(nullable: true),
                    SecurityStamp = table.Column<string>(nullable: true),
                    ConcurrencyStamp = table.Column<string>(nullable: true),
                    PhoneNumber = table.Column<string>(nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(nullable: false),
                    TwoFactorEnabled = table.Column<bool>(nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(nullable: true),
                    LockoutEnabled = table.Column<bool>(nullable: false),
                    AccessFailedCount = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthSettings",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AuthMode = table.Column<string>(nullable: false),
                    LdapServer = table.Column<string>(nullable: true),
                    LdapPort = table.Column<int>(nullable: false),
                    LdapBaseDn = table.Column<string>(nullable: true),
                    LdapBindDn = table.Column<string>(nullable: true),
                    LdapBindPassword = table.Column<string>(nullable: true),
                    LdapUseSsl = table.Column<bool>(nullable: false),
                    LdapSearchFilter = table.Column<string>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntakeRecords",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeId = table.Column<string>(nullable: false),
                    ProcessName = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: false),
                    BusinessUnit = table.Column<string>(nullable: false),
                    Department = table.Column<string>(nullable: false),
                    ProcessOwnerName = table.Column<string>(nullable: false),
                    ProcessOwnerEmail = table.Column<string>(nullable: false),
                    ProcessType = table.Column<string>(nullable: false),
                    EstimatedVolumePerDay = table.Column<int>(nullable: false),
                    Priority = table.Column<string>(nullable: false),
                    Country = table.Column<string>(nullable: false),
                    City = table.Column<string>(nullable: false),
                    SiteLocation = table.Column<string>(nullable: false),
                    TimeZone = table.Column<string>(nullable: false),
                    UploadedFileName = table.Column<string>(nullable: true),
                    UploadedFilePath = table.Column<string>(nullable: true),
                    UploadedFileContentType = table.Column<string>(nullable: true),
                    UploadedFileSize = table.Column<long>(nullable: true),
                    Status = table.Column<string>(nullable: false),
                    AnalysisResult = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    SubmittedAt = table.Column<DateTime>(nullable: true),
                    AnalyzedAt = table.Column<DateTime>(nullable: true),
                    CreatedByUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<string>(maxLength: 450, nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(maxLength: 450, nullable: false),
                    ClaimType = table.Column<string>(nullable: true),
                    ClaimValue = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(nullable: true),
                    UserId = table.Column<string>(maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(maxLength: 450, nullable: false),
                    RoleId = table.Column<string>(maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(maxLength: 450, nullable: false),
                    LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                    Name = table.Column<string>(maxLength: 128, nullable: false),
                    Value = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FinalReports",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    ReportFileName = table.Column<string>(nullable: false),
                    FilePath = table.Column<string>(nullable: false),
                    FileSizeBytes = table.Column<long>(nullable: false),
                    GeneratedAt = table.Column<DateTime>(nullable: false),
                    GeneratedByUserId = table.Column<string>(nullable: true),
                    GeneratedByName = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinalReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinalReports_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntakeTasks",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskId = table.Column<string>(nullable: false),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    Title = table.Column<string>(nullable: false),
                    Description = table.Column<string>(nullable: false),
                    Owner = table.Column<string>(nullable: false),
                    Priority = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    DueDate = table.Column<DateTime>(nullable: false),
                    CompletedAt = table.Column<DateTime>(nullable: true),
                    CreatedByUserId = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeTasks_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportFieldStatuses",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    FieldKey = table.Column<string>(nullable: false),
                    FieldLabel = table.Column<string>(nullable: false),
                    Section = table.Column<string>(nullable: false),
                    TemplatePlaceholder = table.Column<string>(nullable: false),
                    Status = table.Column<string>(nullable: false),
                    FillValue = table.Column<string>(nullable: true),
                    IsNA = table.Column<bool>(nullable: false),
                    Notes = table.Column<string>(nullable: true),
                    LinkedTaskId = table.Column<string>(nullable: true),
                    AnalyzedAt = table.Column<DateTime>(nullable: true),
                    UpdatedAt = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFieldStatuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportFieldStatuses_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IntakeDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeRecordId = table.Column<int>(nullable: false),
                    IntakeTaskId = table.Column<int>(nullable: true),
                    FileName = table.Column<string>(nullable: false),
                    FilePath = table.Column<string>(nullable: false),
                    ContentType = table.Column<string>(nullable: true),
                    FileSize = table.Column<long>(nullable: true),
                    DocumentType = table.Column<string>(nullable: false),
                    UploadedAt = table.Column<DateTime>(nullable: false),
                    UploadedByUserId = table.Column<string>(nullable: true),
                    UploadedByName = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeDocuments_IntakeRecords_IntakeRecordId",
                        column: x => x.IntakeRecordId,
                        principalTable: "IntakeRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IntakeDocuments_IntakeTasks_IntakeTaskId",
                        column: x => x.IntakeTaskId,
                        principalTable: "IntakeTasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TaskActionLogs",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IntakeTaskId = table.Column<int>(nullable: false),
                    ActionType = table.Column<string>(nullable: false),
                    OldStatus = table.Column<string>(nullable: true),
                    NewStatus = table.Column<string>(nullable: true),
                    Comment = table.Column<string>(nullable: true),
                    CreatedAt = table.Column<DateTime>(nullable: false),
                    CreatedByUserId = table.Column<string>(nullable: true),
                    CreatedByName = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskActionLogs_IntakeTasks_IntakeTaskId",
                        column: x => x.IntakeTaskId,
                        principalTable: "IntakeTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinalReports_IntakeRecordId",
                table: "FinalReports",
                column: "IntakeRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDocuments_IntakeRecordId",
                table: "IntakeDocuments",
                column: "IntakeRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDocuments_IntakeTaskId",
                table: "IntakeDocuments",
                column: "IntakeTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeTasks_IntakeRecordId",
                table: "IntakeTasks",
                column: "IntakeRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportFieldStatuses_IntakeRecordId",
                table: "ReportFieldStatuses",
                column: "IntakeRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActionLogs_IntakeTaskId",
                table: "TaskActionLogs",
                column: "IntakeTaskId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuthSettings");

            migrationBuilder.DropTable(
                name: "FinalReports");

            migrationBuilder.DropTable(
                name: "IntakeDocuments");

            migrationBuilder.DropTable(
                name: "ReportFieldStatuses");

            migrationBuilder.DropTable(
                name: "TaskActionLogs");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "IntakeTasks");

            migrationBuilder.DropTable(
                name: "IntakeRecords");
        }
    }
}
