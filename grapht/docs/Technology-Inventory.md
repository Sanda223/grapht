# Complete Technology Inventory

## Languages

| Language | Where Used |
|---|---|
| C# (.NET 9.0) | All backend, frontend, and library projects |
| Razor / .razor | Blazor component and page files |
| SQL (T-SQL) | All database migration scripts, stored procedures, functions |
| JavaScript | agentEvents.js, contactEvents.js, JSInteropMiddle.js, appInsights.js, AWS SDK wrappers |
| YAML | Azure Pipelines CI/CD definitions, Identity data (Client.yml, User.yml, etc.) |
| JSON | All appsettings config files, NuGet config, NSwag config, launch settings |
| PowerShell | deploy.ps1, InitLocaldb.ps1, ReplaceFileValues.ps1, SetBuildVariables.ps1, SetVersionNumber.ps1 |
| Bash | docker-entrypoint.sh (Clinician), inline scripts in Azure Pipelines |
| Bicep | All Azure infrastructure-as-code (main.bicep, modules) |
| XML | Azure B2C policy files, .nuspec package manifests, NuGet.config |
| Mermaid | Architecture/sequence diagrams (ClinicianSequence.mmd, PatientSequence.mmd) |
| Nginx config | nginx.conf, nginx.conf.template |
| CSS | app.css, blazor-ui.css, custom-validation.css, Bootstrap CSS |

---

## Frameworks & Runtimes

### Core Platform

| Tech | Version | Role |
|---|---|---|
| .NET | 9.0 | Runtime for all projects |
| ASP.NET Core | 9.0 | Web API, worker hosting, middleware |
| Blazor WebAssembly | Microsoft.NET.Sdk.BlazorWebAssembly | Clinician portal SPA |
| ASP.NET Core Razor Components | Microsoft.NET.Sdk.Razor | Shared component library |
| ASP.NET Core Worker Service | Microsoft.NET.Sdk.Worker | Worker & Integration services |

---

## NuGet Packages (by category)

### AWS

| Package | Version | Use |
|---|---|---|
| AWSSDK.Chime | 3.7.300.99 | AWS Chime classic API (meeting room creation) |
| AWSSDK.ChimeSDKMeetings | 3.7.301.77 | AWS Chime SDK Meetings (attendee management) |

### Azure

| Package | Version | Use |
|---|---|---|
| Azure.Identity | 1.13.0 | Managed Identity / Azure AD authentication |
| Azure.Messaging.ServiceBus | 7.18.2 | Azure Service Bus messaging |
| Microsoft.Azure.SignalR | 1.28.0 | Azure SignalR Service |
| Microsoft.ApplicationInsights.AspNetCore | 2.22.0 | App Insights telemetry |
| Microsoft.ApplicationInsights.WorkerService | 2.22.0 | App Insights for worker |
| BlazorApplicationInsights | 3.1.0 | Browser-side Application Insights in Blazor |

### Authentication / Authorization

| Package | Version | Use |
|---|---|---|
| Microsoft.AspNetCore.Authentication.JwtBearer | 9.x | JWT Bearer token validation |
| Microsoft.AspNetCore.Components.WebAssembly.Authentication | 9.x | OIDC/OAuth in Blazor WASM |
| Microsoft.Authentication.WebAssembly.Msal | 9.0.5 | MSAL for Blazor WASM (Azure AD B2C) |
| Microsoft.Identity.Client (via Program.cs import) | — | MSAL client library |

### Messaging

| Package | Version | Use |
|---|---|---|
| MassTransit | 8.2.0 | Distributed message bus abstraction |
| MassTransit.RabbitMQ | 8.2.1 | RabbitMQ transport for MassTransit |

### Real-Time

| Package | Version | Use |
|---|---|---|
| Microsoft.AspNetCore.SignalR.Client | 9.0.5 | SignalR client |
| Microsoft.AspNetCore.SignalR.Core | 1.1.0 | SignalR hub primitives |

### Database / ORM

| Package | Version | Use |
|---|---|---|
| Dapper | 2.1.44 | Lightweight micro-ORM for SQL queries |
| Dapper.SimpleCRUD | 2.3.0 | CRUD extension for Dapper |
| Microsoft.Data.SqlClient | 5.2.2 | SQL Server ADO.NET driver |
| Microsoft.Extensions.Caching.SqlServer | 9.0.5 | SQL-backed distributed cache |
| Oracle.ManagedDataAccess.Core | 3.21.140 | Oracle DB driver (EDIS integration in Worker.Integration) |

### Scheduling

| Package | Version | Use |
|---|---|---|
| Quartz | 3.13.1 (Worker), 3.5.0 (Integration) | Job scheduling framework |
| Quartz.Serialization.Json | 3.13.1 | JSON serialization for Quartz jobs |

### Logging

| Package | Version | Use |
|---|---|---|
| Serilog | 2.11.0 | Structured logging (Integration) |
| Serilog.Extensions.Hosting | 5.0.1 | Serilog host integration |
| Serilog.Settings.Configuration | 3.3.0 | Serilog from appsettings |
| Serilog.Sinks.Console | 4.0.1 | Console sink |
| Serilog.Sinks.Seq | 5.1.1 | Seq sink (Integration) |
| Serilog.Enrichers.Environment | 2.2.0 | Machine name enricher |
| Serilog.Enrichers.Thread | 3.1.0 | Thread enricher |
| SerilogTimings | 3.0.1 | Operation duration logging |
| Seq.Extensions.Logging | 6.1.0 / 8.0.0 | Seq via Microsoft.Extensions.Logging |

### OpenTelemetry

| Package | Version | Use |
|---|---|---|
| OpenTelemetry.Extensions.Hosting | 1.8.1 | OTel host integration |
| OpenTelemetry.Instrumentation.AspNetCore | 1.8.1 | ASP.NET Core traces |
| OpenTelemetry.Instrumentation.Http | 1.8.1 | HTTP client traces |
| OpenTelemetry.Instrumentation.SqlClient | 1.5.1-beta.1 | SQL traces |
| OpenTelemetry.Exporter.OpenTelemetryProtocol | 1.15.3 | OTLP export (e.g. to Seq/Jaeger) |

### API / Serialization

| Package | Version | Use |
|---|---|---|
| NSwag.AspNetCore | 14.4.0 | OpenAPI / Swagger UI generation |
| NSwag.MSBuild | 14.4.0 | Auto-generates API client at build time |
| Newtonsoft.Json | 13.x | JSON serialization (alternative to System.Text.Json) |
| Microsoft.AspNetCore.Mvc.NewtonsoftJson | 9.x | Newtonsoft integration for MVC |
| Microsoft.AspNetCore.JsonPatch | 9.0.11 | HTTP PATCH support |
| Microsoft.Rest.ClientRuntime | 2.3.24 | AutoRest generated client runtime |

### Validation

| Package | Version | Use |
|---|---|---|
| FluentValidation | 12.0.0 | Declarative model validation |
| FluentValidation.DependencyInjectionExtensions | 12.0.0 | DI wiring for validators |

### Resilience

| Package | Version | Use |
|---|---|---|
| Polly | 8.4.1 | Retry, circuit-breaker policies |
| Microsoft.Extensions.Http.Polly | 9.0.5 | Polly integration with IHttpClientFactory |

### UI Components (Blazor)

| Package | Version | Use |
|---|---|---|
| Blazicons | 2.0.28 | Icon component system |
| Blazicons.FontAwesome | 2.1.23 | Font Awesome icons in Blazor |
| Blazicons.MaterialDesignIcons | 2.0.19 | Material Design icons in Blazor |
| Blazored.SessionStorage | 2.2.0 | Browser session storage access |
| Microsoft.AspNetCore.Components.QuickGrid | 9.x | Data grid component |
| Majorsoft.Blazor.Components.Toggle | 1.5.0 | Toggle switch component |
| Microsoft.AspNetCore.Components.Authorization | 9.0.6 | Auth state in Blazor |

### Health Checks

| Package | Version | Use |
|---|---|---|
| AspNetCore.HealthChecks.SqlServer | 9.x | SQL Server health endpoint |

### Security Headers

| Package | Version | Use |
|---|---|---|
| NetEscapades.AspNetCore.SecurityHeaders | 0.21.0 | HTTP security headers middleware |

### HL7

| Package | Version | Use |
|---|---|---|
| HL7-dotnetcore | 2.39.1 | HL7 v2.x message parsing |

### Data Filtering

| Package | Version | Use |
|---|---|---|
| Internal.DataFiltering | 3.1.2211.131 | Custom internal data filtering library |

### Utility

| Package | Version | Use |
|---|---|---|
| Humanizer.Core | 2.14.1 | Human-readable strings (dates, enums, etc.) |
| System.Runtime.Caching | 8.0.0 | In-memory object caching |

### Internal Packages (private NuGet feed)

| Package | Use |
|---|---|
| Internal.Blazor.Chime.Component | Blazor component wrapping the AWS Chime SDK JS |
| Internal.Blazor.Metadata.Component | Internal metadata-driven UI component |
| Internal.Blazor.Metadata.Designer | Design-time support for metadata components |
| Internal.Externals.PatientSearch | Client library for external patient search service |
| Internal.Externals.QMPI.Integration | QMPI patient identity integration client |
| Internal.Symphony.Core | Core real-time/hub shared library (SignalR, roles, runtime) |
| Internal.Services.ExternalMessaging.MessageSchemas | SMS/email message contracts (Whispir integration) |

### Testing

| Package | Version | Use |
|---|---|---|
| xunit | 2.7.1 | Unit test framework |
| xunit.runner.visualstudio | 2.5.8 | xUnit VS runner |
| Xunit.Categories | 2.0.8 | Test categorisation attributes |
| NUnit | 4.1.0 | E2E test framework |
| NUnit3TestAdapter | 4.5.0 | NUnit VS adapter |
| NUnit.Analyzers | 4.1.0 | NUnit Roslyn analyzers |
| Microsoft.NET.Test.Sdk | 17.9.0 | Test runner SDK |
| moq | 4.20.70 | Mocking library |
| Testcontainers.MsSql | 3.8.0 | Docker-based SQL Server for integration tests |
| Microsoft.Playwright.NUnit | 1.42.0 | Browser automation (E2E) |
| Deque.AxeCore.Playwright | 4.9.0 | Accessibility testing (axe-core via Playwright) |
| Deque.AxeCore.Commons | 4.9.0 | Axe accessibility commons |
| coverlet.collector | 6.0.2 | Code coverage collection |
| Meziantou.Extensions.Logging.Xunit | 1.0.7 | Log output in xUnit tests |
| Flurl.Http | 4.0.2 | Fluent HTTP client for test helpers |
| Microsoft.AspNetCore.Mvc.Testing | 9.x | In-process integration test host |
| BenchmarkDotNet | 0.13.12 | Performance benchmarking |

---

## JavaScript Libraries (bundled in wwwroot)

| Library | Use |
|---|---|
| Amazon Chime SDK (amazon-chime-sdk.min.js) | Video/audio meeting client |
| Amazon Connect Streams (connect-streams-min.js) | AWS Connect CCP soft phone dialer |
| Amazon Connect Chat (amazon-connect-chat.min.js) | Chat integration with AWS Connect |
| AWS SDK for JavaScript (aws-sdk-2.1596.0.min.js) | General AWS JS SDK |
| Bootstrap 5 (bootstrap.bundle.js + CSS) | UI grid, components, layout |
| Font Awesome (CDN via kit.fontawesome.com) | Icon font |
| Azure Application Insights JS SDK (CDN ai.3.gbl.min.js) | Browser telemetry |

---

## Infrastructure & DevOps

### Containerisation

| Tech | Use |
|---|---|
| Docker | All service containers |
| Docker Compose | Local dev orchestration |
| Nginx (Alpine) | Reverse proxy / static host for Blazor WASM (Clinician) |
| Azure Container Registry (ACR) | mnitappsdev.azurecr.io — image repository |
| Azure Container Apps | Cloud container hosting (TEST/UAT/PROD) |

### CI/CD

| Tech | Use |
|---|---|
| Azure DevOps Pipelines | CI (azure-pipelines-ci.yml), Release (azure-pipelines-Release.yml), Infrastructure (azure-pipelines-infra.yml) |
| Azure CLI (az cli) | Deployment scripting in release pipeline |
| NuGet CLI | Package publishing (Database, Integration packages to MainFeed) |

### Database Tooling

| Tech | Use |
|---|---|
| Grate | SQL migration runner (PowerShell-invokable CLI: grate.exe) |
| SQL Server 2022 | Local dev (Docker image mcr.microsoft.com/mssql/server:2022-latest) |
| Azure SQL | Cloud database (TEST/UAT/PROD) |
| MSSQLLocalDB | Developer local instance for unit tests |

### Cloud Services

| Service | Use |
|---|---|
| Azure AD B2C | Production identity / SSO (policy files in b2cpolicy/) |
| Microsoft Entra ID | Managed Identities for container app authentication |
| Azure SignalR Service | Cloud-hosted WebSocket relay |
| Azure Application Insights | Monitoring and telemetry |
| Azure Service Bus | Alternative cloud messaging |
| Azure Storage / Azurite | Blob/table storage (local: Azurite emulator) |
| Azure Key Vault (referenced in config) | Secrets management |
| CloudAMQP | Managed RabbitMQ for cloud environments |
| RabbitMQ (masstransit/rabbitmq Docker image) | Local dev message broker |
| Seq (datalust/seq) | Structured log aggregation (local dev + TEST) |
| AWS Chime | Video consultation API |
| AWS Connect | Call-centre integration (CCP dialer, contact events) |

### Infrastructure-as-Code

| Tech | Use |
|---|---|
| Azure Bicep | Declarative ARM resource definitions (Container Apps, SQL, Storage, ASB permissions, Log Analytics Workspace) |

### Identity (Local Dev)

| Tech | Use |
|---|---|
| Mock Identity Server (mnitappsdev.azurecr.io/identity/server/mock:v2.3) | OIDC provider for local dev (static YAML data: clients, users, claims, roles) |

---

## External Health / Clinical System Integrations

| System | Integration Method |
|---|---|
| HL7 v2.4 (FirstNet / IEMR) | ADT-A04, A08, A02, A03 and ORU-R01 messages over MassTransit/RabbitMQ |
| EDIS (QLD Health) | Oracle DB query via Oracle.ManagedDataAccess.Core |
| QMPI | Via Internal.Externals.QMPI.Integration NuGet package |
| Patient Search Service | Via Internal.Externals.PatientSearch NuGet package |
| Health Direct Australia (HDA) | External referral token / Digital Front Door |
| Whispir | SMS & email sending via Internal.Services.ExternalMessaging.MessageSchemas + MassTransit |

---

## Protocols & Standards

- OIDC / OAuth 2.0 — authentication flows (implicit, client credentials, authorization code)
- JWT — bearer token format
- WebSockets — SignalR transport
- HTTP/2 — Nginx configured with http2
- HL7 v2.4 — clinical message format
- OpenAPI / Swagger — REST API documentation (NSwag)
- OTLP — OpenTelemetry export protocol
- AMQP — RabbitMQ messaging protocol
- UUID v7 — ID generation (SQL functions in the database)

---

## Tooling & IDE

| Tool | Use |
|---|---|
| Visual Studio / VS Code | IDEs (.vs, .vscode folders, .editorconfig) |
| ReSharper (.DotSettings file) | Code style settings |
| NuGet | Package management (public nuget.org + private Azure DevOps MainFeed) |
| dotnet CLI | Build, test, publish, tool install |
| Roboto font | UI typography (bundled in wwwroot/css/fonts/Roboto/) |
| Open Iconic | Icon set (bundled CSS open-iconic/) |

---

## File / Config Formats

`.json` `.yaml` / `.yml` `.xml` `.sql` `.ps1` `.sh` `.bicep` `.razor` `.cs` `.csproj` `.sln` `.mmd` (Mermaid) `.svg` `.bat` `.nuspec` `.nswag` `.runsettings` `.editorconfig`
