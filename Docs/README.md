# BookMyFun Documentation Index

Welcome to the comprehensive documentation suite for the **BookMyFun** multi-dashboard farm, luxury villa, and resort rental platform.

---

## Documentation Navigation

| Document | Primary Focus | Target Audience |
| :--- | :--- | :--- |
| **[`PRD.md`](./PRD.md)** | Complete 14-Module Product Requirements Document, Personas & User Journeys | Product Managers, Stakeholders, Developers |
| **[`Architecture.md`](./Architecture.md)** | Technical Architecture, Multi-Tenant RBAC Matrix, Service Layer & Data Flows | Systems Architects, Lead Engineers |
| **[`Database.md`](./Database.md)** | Complete Relational Schemas, Data Dictionary, Foreign Keys, Indexes & DDL | Database Administrators, Backend Engineers |
| **[`Design.md`](./Design.md)** | UI/UX Design System, Semantic Color Tokens (Light/Dark Mode), Component Library | Front-End Developers, UI/UX Designers |
| **[`API.md`](./API.md)** | RESTful Endpoints, MVC Routes, Request/Response Payloads & Auth Scopes | Frontend & Mobile Integrators, QA Engineers |
| **[`Rules.md`](./Rules.md)** | Security Protocols (OWASP, PCI-DSS, SSRF, Magic Bytes) & Coding Guidelines | Security Auditors, Senior Developers |
| **[`Task.md`](./Task.md)** | Master Feature Inventory, Implementation Checklist & Technical Roadmap | Project Managers, Engineering Leads |
| **[`Memory.md`](./Memory.md)** | Architectural Decision Records (ADR-01 through ADR-10) & Context Retention | Core Maintainers, System Architects |

---

## Quick Architecture Summary

```
[Public Users / Clients]      [Partner Hosts / Owners]      [Support / Operations]      [Platform Superuser]
          │                              │                             │                         │
          ▼                              ▼                             ▼                         ▼
   Client Portal                   Owner Portal                 Operations Staff            Admin Console
  (/Properties, /Booking)          (/Owner/*)                    (/Employee/*)              (/Admin/*)
          │                              │                             │                         │
          └──────────────────────────────┼─────────────────────────────┼─────────────────────────┘
                                         ▼
                        ASP.NET Core 10 MVC Architecture
                       [Security Middleware & Exception Filter]
                                         │
                        [Service & Business Logic Engines]
             (Refund Engine, Commission Engine, Damage Claims, KYC Vault)
                                         │
                       [Raw Parameterized ADO.NET Repositories]
                                         │
                             [Microsoft SQL Server 2022]
```

For complete project setup, installation instructions, and testing credentials, please refer to the root [**`README.md`**](../README.md).
