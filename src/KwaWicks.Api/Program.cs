using Amazon;
using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using KwaWicks.Application.Interfaces;
using KwaWicks.Application.Services;
using KwaWicks.Application.DTOs;
using KwaWicks.Infrastructure.DynamoDB;
using KwaWicks.Infrastructure.S3;
using KwaWicks.Infrastructure.Pdf;
using KwaWicks.Infrastructure.WhatsApp;
using KwaWicks.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// -------------------- CORS --------------------
const string UiCors = "UiCors";

builder.Services.AddCors(options =>
{
    options.AddPolicy(UiCors, policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:5174",
                "http://127.0.0.1:5174",
                "https://main.d137tsnrxezsdg.amplifyapp.com"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// -------------------- Swagger --------------------
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "KwaWicks API", Version = "v1" });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// -------------------- AWS --------------------
var awsRegion = builder.Configuration["Aws:Region"] ?? "af-south-1";

var tableName = builder.Configuration["Aws:DynamoTableName"]
               ?? Environment.GetEnvironmentVariable("AWS_DYNAMO_TABLE_NAME")
               ?? "kwawicks";

builder.Services.AddSingleton<IAmazonDynamoDB>(_ =>
    new AmazonDynamoDBClient(RegionEndpoint.GetBySystemName(awsRegion))
);

builder.Services.AddSingleton<IAmazonS3>(_ =>
    new AmazonS3Client(RegionEndpoint.GetBySystemName(awsRegion))
);

// Repositories (single-table PK/SK pattern)
builder.Services.AddScoped<ISpeciesRepository>(sp =>
    new SpeciesRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IClientRepository>(sp =>
    new ClientRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IInvoiceRepository>(sp =>
    new InvoiceRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IDeliveryOrderRepository>(sp =>
    new DeliveryOrderRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IHubTaskRepository>(sp =>
    new HubTaskRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<ISupplierRepository>(sp =>
    new SupplierRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IProcurementOrderRepository>(sp =>
    new ProcurementOrderRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<ICollectionRequestRepository>(sp =>
    new CollectionRequestRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IDeliveryRunRepository>(sp =>
    new DeliveryRunRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IStaffMemberRepository>(sp =>
    new StaffMemberRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IPettyCashRepository>(sp =>
    new PettyCashRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IClientCreditRepository>(sp =>
    new ClientCreditRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IHubRequestRepository>(sp =>
    new HubRequestRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<ISettingsRepository>(sp =>
    new SettingsRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<ISlaughterRepository>(sp =>
    new SlaughterRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<ICostAverageRepository>(sp =>
    new CostAverageRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IOtpRepository>(sp =>
    new OtpRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IBankStatementRepository>(sp =>
    new BankStatementRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IDriverStockAllocationRepository>(sp =>
    new DriverStockAllocationRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IStockLossRepository>(sp =>
    new StockLossRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

builder.Services.AddScoped<IVehicleTrackingRepository>(sp =>
    new VehicleTrackingRepository(sp.GetRequiredService<IAmazonDynamoDB>(), tableName));

// Services
builder.Services.AddScoped<SpeciesService>();
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IDeliveryOrderService, DeliveryOrderService>();

var receiptsBucket = builder.Configuration["Aws:S3:ReceiptsBucket"] ?? "kwawicks-receipts";
builder.Services.AddSingleton<IS3Service>(sp =>
    new S3Service(sp.GetRequiredService<IAmazonS3>(), receiptsBucket));

builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<ISupplierService, SupplierService>();
builder.Services.AddScoped<IProcurementOrderService, ProcurementOrderService>();
builder.Services.AddScoped<ICollectionRequestService, CollectionRequestService>();
builder.Services.AddScoped<IDeliveryRunService, DeliveryRunService>();
builder.Services.AddScoped<IStaffMemberService, StaffMemberService>();
builder.Services.AddScoped<IClientCreditService>(sp =>
    new ClientCreditService(
        sp.GetRequiredService<IClientCreditRepository>(),
        sp.GetRequiredService<IClientRepository>(),
        sp.GetRequiredService<IS3Service>()));
builder.Services.AddScoped<IHubRequestService>(sp =>
    new HubRequestService(
        sp.GetRequiredService<IHubRequestRepository>(),
        sp.GetRequiredService<IWhatsAppService>(),
        sp.GetRequiredService<ISettingsRepository>()));
builder.Services.AddScoped<ISettingsService, SettingsService>();
builder.Services.AddScoped<IPriceApprovalService, PriceApprovalService>();
builder.Services.AddScoped<ISlaughterService, SlaughterService>();
builder.Services.AddScoped<ICostAverageService, CostAverageService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IBankStatementService, BankStatementService>();
builder.Services.AddScoped<IDriverStockAllocationService, DriverStockAllocationService>();
builder.Services.AddScoped<IStockLossService, StockLossService>();
builder.Services.AddScoped<IVehicleTrackingService, VehicleTrackingService>();
builder.Services.AddScoped<IPettyCashService>(sp =>
    new PettyCashService(
        sp.GetRequiredService<IPettyCashRepository>(),
        sp.GetRequiredService<IS3Service>(),
        sp.GetRequiredService<IInvoiceRepository>(),
        sp.GetRequiredService<IClientCreditRepository>()));

// PDF + WhatsApp + Invoice notifications
builder.Services.AddScoped<IPdfService, PdfService>();
builder.Services.AddHttpClient<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IInvoiceNotificationService, InvoiceNotificationService>();

// -------------------- Cognito JWT Auth --------------------
var cognitoRegion = builder.Configuration["Cognito:Region"] ?? "af-south-1";
var userPoolId = builder.Configuration["Cognito:UserPoolId"]
                 ?? throw new InvalidOperationException("Missing Cognito:UserPoolId");

builder.Services.AddScoped<IUserManagementService>(sp =>
    new UserManagementService(
        sp.GetRequiredService<IAmazonCognitoIdentityProvider>(),
        userPoolId));

var authority = "https://cognito-idp." + cognitoRegion + ".amazonaws.com/" + userPoolId;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RoleClaimType = "cognito:groups",
            NameClaimType = "cognito:username"
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly",       p => p.RequireRole("Owner"));
    options.AddPolicy("FinancialAccess",  p => p.RequireRole("Owner", "Finance"));
    options.AddPolicy("PettyCashAccess", p => p.RequireRole("Owner", "Finance", "Admin"));
    options.AddPolicy("OperationalAccess", p => p.RequireRole("Owner", "Finance", "Admin", "HubStaff", "Procurement", "Driver"));
    options.AddPolicy("UserManagement", p => p.RequireRole("Owner", "Admin"));
    options.AddPolicy("DriverOnly", p => p.RequireRole("Owner", "Finance", "Admin", "Driver"));
    options.AddPolicy("AdminOnly", p => p.RequireRole("Owner", "Finance", "Admin"));
    options.AddPolicy("HubStaffOnly", p => p.RequireRole("Owner", "Finance", "Admin", "HubStaff", "Procurement"));
    options.AddPolicy("ProcurementAccess", p => p.RequireRole("Owner", "Finance", "Admin", "Procurement"));
    options.AddPolicy("SupplierManagement", p => p.RequireRole("Owner", "Admin", "Procurement"));
    options.AddPolicy("CollectionManagement", p => p.RequireRole("Owner", "Admin", "HubStaff", "Procurement"));
});

// -------------------- Cognito Client --------------------
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(_ =>
{
    var region = RegionEndpoint.GetBySystemName(cognitoRegion);
    return new AmazonCognitoIdentityProviderClient(region);
});

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseCors(UiCors);

app.UseExceptionHandler(err => err.Run(async ctx =>
{
    ctx.Response.StatusCode = 500;
    ctx.Response.ContentType = "application/json";
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
    var msg = ex?.Error?.Message ?? "An unexpected error occurred.";
    await ctx.Response.WriteAsJsonAsync(new { error = msg });
}));

app.MapMethods("{*path}", new[] { "OPTIONS" }, () => Results.Ok())
   .RequireCors(UiCors);

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => Results.Ok("KwaWicks API is running"));
app.MapGet("/health", () => Results.Ok("ok"));

app.Run();
