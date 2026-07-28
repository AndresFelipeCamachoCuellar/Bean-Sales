# Design Specification: Specialty Coffee Sales Platform

## 1. Architectural Overview
We will use a **Clean Architecture** approach with **ASP.NET Core MVC**.
- **Presentation Layer (Web)**: ASP.NET Core MVC. Recommendations: ViewModels, TagHelpers, Client-side validation.
- **Business Layer (Core/Logic)**: Services and Domain Logic. Interfaces for dependency injection.
- **Data Access Layer (Data)**: Entity Framework Core. Repository Pattern (optional, or direct DbContext usage if preferred for simplicity).
- **Database**: SQL Server (implied by standard .NET stack, or PostgreSQL).

## 2. Business Flows

### A. Administration (Back-office)
1.  **Global Coffee Catalog**:
    -   Admin uploads a new "Coffee Profile" (Name, Origin, Notes, Image, Base Price).
    -   This is the master list of all products the company deals with.
2.  **Country & Inventory Management**:
    -   Admin manages supported Countries.
    -   **Availability**: Admin assigns "Coffees" to "Countries".
    -   **Stock/Inventory**: Admin sets available quantity (Number of Bags) for a specific Coffee in a specific Country.
    -   *Logic*: A coffee cannot be sold in "Germany" if it's not assigned to the Germany market.
    -   *Properties*: each Coffee entry implies a specific bag weight (e.g., 1lb, 340g) defined by the Admin.

### User & Security
1.  **Public Registration**:
    -   New users can sign up (Customer Role).
    -   Default fields: Name, Email, Password.
    -   User selects their default Country on signup (optional but helpful).
2.  **User Profile**:
    -   View/Edit Personal Info.
    -   View Order History.
    -   Manage Addresses.
3.  **Admin Management**:
    -   Admin manages Users, Roles (e.g., Global Admin, Country Manager, Customer), and Permissions.

### B. Sales (Front-office)
1.  **Shopping Experience**:
    -   **Country Selection**: Critical first step. User selects their shipping country (or detected by IP).
    -   **Product Browsing**: User views valid coffees for their selected country. Shows dynamic stock status.
2.  **Purchasing Flow**:
    -   User selects a Coffee.
    -   **Packaging**:
        -   Display: Shows fixed bag weight (e.g., "1 lb bag").
    -   **Selection**: User chooses **Quantity** (Number of Bags).
    -   *Validation*: Check if requested Quantity <= Available Country Inventory (Bags).
    -   **Cart**: Persistent shopping cart.
    -   **Checkout**: Address confirmation -> Payment (Mock/Integration) -> Order Creation.
    -   **Stock Update**: Deduct quantity from `CountryCoffees` inventory.

## 3. Data Model (ER Diagram Concept)

### Security Module (Phase 1)
- **Users**: `Id`, `Username`, `Email`, `PasswordHash`, `CountryId` (optional default).
- **Roles**: `Id`, `Name` (Admin, Customer, etc.).
- **Permissions**: `Id`, `Name` (e.g., `CanManageInventory`, `CanViewSales`).
- **UserRoles**: Many-to-Many link.
- **RolePermissions**: Many-to-Many link.

### Sales & Orders (New)
- **Orders**: `Id`, `UserId` (FK), `OrderDate`, `TotalAmount`, `Status` (Pending, Shipped, etc.), `ShippingAddress`.
- **OrderItems**: `Id`, `OrderId` (FK), `CoffeeId` (FK), `Quantity` (Number of Bags), `UnitPrice`, `SubTotal`.
- **ShoppingCart**: `Id`, `UserId` (FK), `Items` (Json or related table).

### Coffee Core
- **Coffees**: `Id`, `Name`, `Description`, `Origin`, `ImageUrl`, `BagWeight` (e.g. 454g/1lb).
- **Countries**: `Id`, `Name`, `CurrencyCode`, `ExchangeRate` (vs Base).
- **CountryCoffees** (Inventory):
    -   `Id`, `CoffeeId` (FK), `CountryId` (FK).
    -   `StockQuantity` (Number of bags available).
    -   `UnitPrice` (Price per unit weight in this country).
    -   `IsActive` (Bool).

## 4. Work Plan & Phasing

### Phase 1: Foundation & Security (Current Priority)
- Setup Solution Structure (Layers).
- Database Configuration (EF Core).
- **User Management**:
    -   Entities: User, Role, Permission.
    -   Authentication (Login/Logout).
    -   Authorization (Middleware/Filters).
    -   CRUD Views for Users and Roles.

### Phase 2: Core Product Management
- Coffee Catalog (CRUD).
- Country Management.
- Country-specific Assignment (The "Matrix" of Coffee x Country).

### Phase 3: Sales Frontend
- Public Views.
- Country Selector.
- Cart & Checkout Logic.
