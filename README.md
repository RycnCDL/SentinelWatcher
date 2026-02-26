# Sentinel Watcher

[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-Server-blue.svg)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Azure](https://img.shields.io/badge/Azure-Sentinel-0078D4.svg)](https://azure.microsoft.com/services/microsoft-sentinel/)

> **Real-time health monitoring and vulnerability tracking for Microsoft Sentinel workspaces.**

## 🎯 Purpose

SOC teams and Sentinel administrators need continuous visibility into:
- **Health Trends**: Is my Sentinel environment degrading over time?
- **Rule Effectiveness**: Which analytics rules are most/least active?
- **Connector Status**: Are all data sources sending logs as expected?
- **Vulnerability Exposure**: Is my environment vulnerable to newly disclosed CVEs?

Sentinel Watcher provides a real-time dashboard addressing all these needs in one place.

## ✨ Features

### 📊 Health Dashboard
- **Overall health score** (0-100) with weighted category breakdown
- **30-day trend chart** showing health over time
- **Real-time updates** via SignalR (live dashboard)
- **Multi-workspace support** (track multiple Sentinel instances)
- **Automated assessments** (configurable schedule)

### 🔍 Analytics Rules Tracking
- **Top 10/20 rules** by alert/incident count
- **Activity timeline** for each rule
- **Effectiveness metrics** (alerts → incidents conversion)
- **Identify silent rules** (rules not triggering)

### 🔌 Data Connector Health
- **Connector status grid** (connected vs. disconnected)
- **Last data received** timestamp per connector
- **Data volume tracking** (optional)
- **Historical trends** (when did connector stop sending data?)

### 🛡️ CVE Tracking & Exposure Checking
- **CISA KEV catalog** integration (Known Exploited Vulnerabilities)
- **Microsoft Security Response Center (MSRC)** integration
- **Automatic KQL query generation** for CVE exposure checks
- **One-click exposure verification** against your Sentinel data
- **Remediation tracking** (mark CVEs as patched)

### 🚨 Alerting
- **Email notifications** (SMTP)
- **Webhook integration** (Microsoft Teams, Slack)
- **Alert on health degradation** (configurable thresholds)
- **Alert on new CVE matches**

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Blazor Server UI                     │
│  (Real-time dashboard with SignalR push updates)        │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│              ASP.NET Core 8.0 API Layer                 │
│  /api/health, /api/analytics, /api/connectors, /api/cve│
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│          Business Logic (SentinelWatcher.Core)          │
│  AssessmentService, AnalyticsService, CveService, etc.  │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│        Data Layer (Azure SQL Database / SQLite)         │
│  AssessmentHistory, RuleActivity, ConnectorHealth, CVEs │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│               Background Workers                        │
│  HealthCheckWorker (periodic), CveFetchWorker (daily)   │
└─────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────┐
│                   External APIs                         │
│  Azure Monitor, CISA KEV, MSRC, Sentinel APIs          │
└─────────────────────────────────────────────────────────┘
```

## 🚀 Quick Start

### Prerequisites

- **.NET 8.0 SDK** ([Download](https://dotnet.microsoft.com/download))
- **SQL Server** (Azure SQL, SQL Express, or SQLite)
- **Azure AD App Registration** (for Sentinel API access)
- **Azure Sentinel workspace** (with Reader permissions)

### Installation

#### Option 1: Deploy to Azure App Service

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FRycnCDL%2FSentinelWatcher%2Fmain%2Fazuredeploy.json)

#### Option 2: Run Locally

```bash
# Clone repository
git clone https://github.com/RycnCDL/SentinelWatcher.git
cd SentinelWatcher

# Restore dependencies
dotnet restore

# Update connection string
# Edit src/SentinelWatcher.Web/appsettings.json

# Run migrations
cd src/SentinelWatcher.Web
dotnet ef database update

# Run application
dotnet run
# Navigate to https://localhost:5001
```

#### Option 3: Docker

```bash
# Build image
docker build -t sentinelwatcher .

# Run container
docker run -p 8080:80 \
  -e ConnectionStrings__DefaultConnection="Server=..." \
  -e AzureAd__ClientId="..." \
  -e AzureAd__ClientSecret="..." \
  sentinelwatcher
```

### Azure AD Configuration

1. **Create App Registration** in Azure AD
2. **API Permissions**: Add the following:
   - `Microsoft.SecurityInsights` (Read)
   - `Microsoft.OperationalInsights` (Read)
3. **Client Secret**: Generate and save securely
4. **Update appsettings.json**:

```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "your-tenant-id",
    "ClientId": "your-app-id",
    "ClientSecret": "your-secret"
  }
}
```

## 📖 Documentation

### Adding a Workspace

1. Navigate to **Workspaces** page
2. Click **+ Add Workspace**
3. Authenticate with Azure (device code or interactive)
4. Select Subscription → Resource Group → Workspace
5. Workspace appears in dashboard

### Running Manual Assessment

1. Select workspace from dropdown
2. Click **⚡ Run Assessment Now**
3. Wait 2-5 minutes for completion
4. View results in dashboard

### Checking CVE Exposure

1. Navigate to **Vulnerabilities** page
2. Browse CISA KEV or MSRC tabs
3. Click **Check Exposure** on any CVE
4. View KQL query and results
5. Mark as remediated if patched

### Configuring Alerts

1. Navigate to **Alerts** page
2. Click **+ New Alert**
3. Select alert type (Health Degradation, CVE Match, etc.)
4. Set threshold (e.g., score drops below 70)
5. Add email recipients or webhook URL
6. Save

## 🔧 Configuration

### appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SentinelWatcher;Integrated Security=true;"
  },
  "AzureAd": {
    "TenantId": "common",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret"
  },
  "Sentinel": {
    "AssessmentScheduleCron": "0 */6 * * *",  // Every 6 hours
    "CveSyncScheduleCron": "0 2 * * *"         // Daily at 2 AM
  },
  "Smtp": {
    "Host": "smtp.office365.com",
    "Port": 587,
    "Username": "alerts@yourcompany.com",
    "EnableSsl": true
  }
}
```

### Background Worker Schedules

| Worker | Default Schedule | Purpose |
|--------|-----------------|---------|
| HealthCheckWorker | Every 6 hours | Run health assessments |
| CveFetchWorker | Daily at 2 AM UTC | Fetch CISA KEV + MSRC updates |
| AlertWorker | Every 5 minutes | Check alert conditions |

## 📊 Database Schema

```sql
-- Workspaces
CREATE TABLE Workspaces (
    Id INT PRIMARY KEY IDENTITY,
    WorkspaceName NVARCHAR(200),
    SubscriptionId UNIQUEIDENTIFIER,
    WorkspaceId UNIQUEIDENTIFIER UNIQUE
);

-- Assessment History (Time-series)
CREATE TABLE AssessmentHistory (
    Id BIGINT PRIMARY KEY IDENTITY,
    WorkspaceId INT,
    AssessmentDate DATETIME2,
    OverallScore DECIMAL(5,2),
    CategoryScoresJson NVARCHAR(MAX)
);

-- Top Rules
CREATE TABLE RuleActivity (
    Id BIGINT PRIMARY KEY IDENTITY,
    WorkspaceId INT,
    RuleName NVARCHAR(500),
    AlertCount INT,
    IncidentCount INT,
    RecordedAt DATETIME2
);

-- Connector Health
CREATE TABLE ConnectorHealth (
    Id BIGINT PRIMARY KEY IDENTITY,
    WorkspaceId INT,
    ConnectorName NVARCHAR(200),
    IsConnected BIT,
    LastDataReceivedAt DATETIME2
);

-- CVE Database
CREATE TABLE CveDatabase (
    Id INT PRIMARY KEY IDENTITY,
    CveId NVARCHAR(50) UNIQUE,
    Severity NVARCHAR(20),
    IsCisaKev BIT,
    KqlQuery NVARCHAR(MAX)
);

-- CVE Matches
CREATE TABLE CveMatches (
    Id BIGINT PRIMARY KEY IDENTITY,
    WorkspaceId INT,
    CveId NVARCHAR(50),
    IsExposed BIT,
    MatchedAt DATETIME2
);
```

## 🎨 Screenshots

### Dashboard
![Health Dashboard](docs/images/dashboard.png)

### Analytics Rules
![Top Rules](docs/images/analytics.png)

### CVE Tracking
![CVE Exposure Check](docs/images/cve-tracking.png)

## 🐛 Troubleshooting

### "Unauthorized" Error

```bash
# Verify Azure AD permissions
az ad app permission list --id <app-id>

# Ensure Reader role on workspace
az role assignment list --scope /subscriptions/.../workspaces/...
```

### Database Connection Failed

```bash
# Test connection string
dotnet ef dbcontext info
```

### Background Worker Not Running

```bash
# Check worker logs
docker logs <container-id> --tail 100
```

## 🔐 Security Best Practices

- ✅ Store Client Secret in **Azure Key Vault** (not appsettings.json)
- ✅ Enable **Azure AD authentication** for web app users
- ✅ Use **Managed Identity** when running in Azure
- ✅ Restrict API access with **IP allowlisting**
- ✅ Enable **Application Insights** for monitoring

## 🚀 Deployment

### Azure App Service

```bash
# Publish to Azure
az webapp up --name sentinelwatcher --resource-group rg-sentinel --runtime "DOTNETCORE:8.0"

# Configure app settings
az webapp config appsettings set --name sentinelwatcher --resource-group rg-sentinel \
  --settings "AzureAd__ClientSecret=@Microsoft.KeyVault(SecretUri=https://...)"
```

### Docker Compose

```yaml
version: '3.8'
services:
  web:
    image: sentinelwatcher:latest
    ports:
      - "8080:80"
    environment:
      - ConnectionStrings__DefaultConnection=Server=sqlserver;...
    depends_on:
      - sqlserver

  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - SA_PASSWORD=YourStrong!Passw0rd
```

## 🤝 Contributing

Contributions welcome! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## 📜 License

MIT License - see [LICENSE](LICENSE) file

## 🙏 Acknowledgments

- Inspired by SOC operational challenges
- Built on Microsoft Azure SDK
- CVE data from CISA and MSRC

## 📧 Support

- **Issues**: [GitHub Issues](https://github.com/RycnCDL/SentinelWatcher/issues)
- **Discussions**: [GitHub Discussions](https://github.com/RycnCDL/SentinelWatcher/discussions)
- **Author**: [@RycnCDL](https://github.com/RycnCDL)

---

**⭐ If this tool helps your SOC operations, please star the repo!**
