# Implementation Plan - Phase 1: Security & User Management

## Goal Description
Initialize the ASP.NET Core MVC project with a robust security architecture using **ASP.NET Core Identity**. This phase focuses on setting up the database, defining the User/Role/Permission entities, and creating the administrative interface to manage them.

## User Review Required
> [!IMPORTANT]
> **Technology Decision**: We will use **ASP.NET Core Identity** as the foundation. It provides secure hashing, session management, and role support out of the box. We will extend it to support granular *Permissions* (Claims).

## Proposed Changes

### 1. Dependencies & Infrastructure
- Install EF Core packages (`Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`).
- Setup `ApplicationDbContext` in `Web/Data`.
- Configure Connection String in `appsettings.json`.

### 2. Domain Models (Web/Models)
#### [NEW] [ApplicationUser.cs](file:///d:/Bean-Sales/Web/Models/ApplicationUser.cs)
- Extends `IdentityUser`.
- Adds `FirstName`, `LastName`, `CountryId`.

#### [NEW] [Permission.cs](file:///d:/Bean-Sales/Web/Models/Permission.cs)
- Static list or Enum of all system permissions (e.g., `Permissions.Users.View`, `Permissions.Coffees.Edit`).
- Helper logic/Seeds to ensure these exist as Claims.

### 3. Business Logic / Services
#### [NEW] [Seeds/DefaultUsers.cs](file:///d:/Bean-Sales/Web/Data/Seeds/DefaultUsers.cs)
- Logic to seed the "SuperAdmin" user and default Roles (Admin, CountryManager, Customer) on startup.

### 4. Controllers & Views (Back-office)
#### [NEW] [UsersController.cs](file:///d:/Bean-Sales/Web/Controllers/UsersController.cs)
- `Index`: List users.
- `Edit`: Assign roles to users.

#### [NEW] [RolesController.cs](file:///d:/Bean-Sales/Web/Controllers/RolesController.cs)
- `Index`: List roles.
- `Create/Edit`: Manage Roles.
- `ManagePermissions`: Special view to check/uncheck permissions (Claims) for a specific Role.

#### [NEW] [AccountController.cs](file:///d:/Bean-Sales/Web/Controllers/AccountController.cs)
- `Register`: Public sign-up actions.
- `Login/Logout`: Standard Identity actions.
- `Profile`: User dashboard to view details and (future) order history.

## Verification Plan

### Automated Tests
- None for this initial phase (scaffolding). Database context integrity will be verified by running the migration.

### Manual Verification
1.  **Database Creation**: Run `update-database`. Verify tables (`AspNetUsers`, `AspNetRoles`) are created in SQL Server (LocalDB).
2.  **Seeding**: Run app, check if default SuperAdmin user exists in DB.
3.  **UI Testing**:
    -   **Admin**: Login as SuperAdmin -> Manage Users/Roles.
    -   **Public**: Register a new user -> Verify redirection to Profile or Home -> Check generic "Customer" role assignment (if implemented).
