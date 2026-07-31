using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Margen.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EsquemaInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    LastFour = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Balance = table.Column<long>(type: "bigint", nullable: false),
                    CreditLimit = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_LastFour_Digits", "\"LastFour\" ~ '^[0-9]{4}$'");
                });

            migrationBuilder.CreateTable(
                name: "BudgetPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedIncome = table.Column<long>(type: "bigint", nullable: false),
                    ActualIncome = table.Column<long>(type: "bigint", nullable: false),
                    SafetyFund = table.Column<long>(type: "bigint", nullable: false),
                    CommittedSavings = table.Column<long>(type: "bigint", nullable: false),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BudgetPeriods", x => x.Id);
                    table.CheckConstraint("CK_BudgetPeriods_Order", "\"EndDate\" >= \"StartDate\"");
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Icon = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Devices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PublicKeySpki = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PublicKeyFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IncomingEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageId = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    BodyHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Sender = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Body = table.Column<string>(type: "text", maxLength: 1024, nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ParserName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ParserVersion = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomingEmails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryBudgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Allocated = table.Column<long>(type: "bigint", nullable: false),
                    Adjustment = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CategoryBudgets_BudgetPeriods_BudgetPeriodId",
                        column: x => x.BudgetPeriodId,
                        principalTable: "BudgetPeriods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CategoryBudgets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MerchantRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Pattern = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MatchKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<int>(type: "integer", nullable: false),
                    IsUserDefined = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TimesApplied = table.Column<long>(type: "bigint", nullable: false),
                    LastAppliedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MerchantRules_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecurringPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedAmount = table.Column<long>(type: "bigint", nullable: false),
                    Cadence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DayOfCycle = table.Column<int>(type: "integer", nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPaidDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringPayments", x => x.Id);
                    table.CheckConstraint("CK_RecurringPayments_DiaDelCiclo", "\"DayOfCycle\" BETWEEN 1 AND 31");
                    table.CheckConstraint("CK_RecurringPayments_MontoPositivo", "\"ExpectedAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_RecurringPayments_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringPayments_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AccessTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    Scopes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessTokens_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeviceChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceChallenges_Devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "Devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    MerchantRaw = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    MerchantNormalized = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfidenceBasisPoints = table.Column<int>(type: "integer", nullable: false),
                    RefundsTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    DuplicateOfTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncomingEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    Fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount_NoNegativo", "\"Amount\" >= 0");
                    table.CheckConstraint("CK_Transactions_Confidence_Rango", "\"ConfidenceBasisPoints\" BETWEEN 0 AND 10000");
                    table.CheckConstraint("CK_Transactions_SinAutoreferencia", "\"RefundsTransactionId\" IS DISTINCT FROM \"Id\" AND \"DuplicateOfTransactionId\" IS DISTINCT FROM \"Id\"");
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Transactions_IncomingEmails_IncomingEmailId",
                        column: x => x.IncomingEmailId,
                        principalTable: "IncomingEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Transactions_Transactions_DuplicateOfTransactionId",
                        column: x => x.DuplicateOfTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Transactions_RefundsTransactionId",
                        column: x => x.RefundsTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    IsUrgent = table.Column<bool>(type: "boolean", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IncomingEmailId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecurringPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    DedupeKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alerts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Alerts_IncomingEmails_IncomingEmailId",
                        column: x => x.IncomingEmailId,
                        principalTable: "IncomingEmails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alerts_RecurringPayments_RecurringPaymentId",
                        column: x => x.RecurringPaymentId,
                        principalTable: "RecurringPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Alerts_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_DeviceId_RevokedAt",
                table: "AccessTokens",
                columns: new[] { "DeviceId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessTokens_TokenHash",
                table: "AccessTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_IsActive",
                table: "Accounts",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_DedupeKey",
                table: "Alerts",
                column: "DedupeKey",
                unique: true,
                filter: "\"ResolvedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_IncomingEmailId",
                table: "Alerts",
                column: "IncomingEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_RecurringPaymentId",
                table: "Alerts",
                column: "RecurringPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ResolvedAt_IsUrgent_CreatedAt",
                table: "Alerts",
                columns: new[] { "ResolvedAt", "IsUrgent", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_TransactionId",
                table: "Alerts",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_BudgetPeriods_StartDate",
                table: "BudgetPeriods",
                column: "StartDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Name",
                table: "Categories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_BudgetPeriodId_CategoryId",
                table: "CategoryBudgets",
                columns: new[] { "BudgetPeriodId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryBudgets_CategoryId",
                table: "CategoryBudgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChallenges_DeviceId",
                table: "DeviceChallenges",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChallenges_ExpiresAt",
                table: "DeviceChallenges",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceChallenges_Nonce",
                table: "DeviceChallenges",
                column: "Nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_PublicKeyFingerprint",
                table: "Devices",
                column: "PublicKeyFingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_BodyHash",
                table: "IncomingEmails",
                column: "BodyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_MessageId",
                table: "IncomingEmails",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IncomingEmails_Status_ReceivedAt",
                table: "IncomingEmails",
                columns: new[] { "Status", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_CategoryId",
                table: "MerchantRules",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_IsActive_Weight",
                table: "MerchantRules",
                columns: new[] { "IsActive", "Weight" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MerchantRules_Pattern_MatchKind",
                table: "MerchantRules",
                columns: new[] { "Pattern", "MatchKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPayments_AccountId",
                table: "RecurringPayments",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPayments_CategoryId",
                table: "RecurringPayments",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringPayments_IsActive_NextDueDate",
                table: "RecurringPayments",
                columns: new[] { "IsActive", "NextDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_OccurredAt",
                table: "Transactions",
                columns: new[] { "AccountId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_CategoryId_OccurredAt",
                table: "Transactions",
                columns: new[] { "CategoryId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DuplicateOfTransactionId",
                table: "Transactions",
                column: "DuplicateOfTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Fingerprint",
                table: "Transactions",
                column: "Fingerprint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_IncomingEmailId",
                table: "Transactions",
                column: "IncomingEmailId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_MerchantNormalized",
                table: "Transactions",
                column: "MerchantNormalized");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_OccurredAt",
                table: "Transactions",
                column: "OccurredAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RefundsTransactionId",
                table: "Transactions",
                column: "RefundsTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_Status",
                table: "Transactions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessTokens");

            migrationBuilder.DropTable(
                name: "Alerts");

            migrationBuilder.DropTable(
                name: "CategoryBudgets");

            migrationBuilder.DropTable(
                name: "DeviceChallenges");

            migrationBuilder.DropTable(
                name: "MerchantRules");

            migrationBuilder.DropTable(
                name: "RecurringPayments");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "BudgetPeriods");

            migrationBuilder.DropTable(
                name: "Devices");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "IncomingEmails");
        }
    }
}
