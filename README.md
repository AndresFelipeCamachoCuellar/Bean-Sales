# The Kng Bean — Coffee Global Hub

Hub **B2B2C de consolidación y exportación de café de especialidad colombiano**. Marcas, fincas y tostadores locales publican sus lotes; The Kng Bean los centraliza en una bodega en Colombia y los vende bajo una compra unificada — "el supermercado del café colombiano".

**Modelo de monetización híbrido:**

| Esquema | Cómo funciona | Quién asume el flete a bodega |
|---|---|---|
| **Compra directa** | The Kng Bean compra el lote por adelantado y lo revende. | The Kng Bean |
| **Consignación** | El proveedor conserva la propiedad hasta que se vende y paga comisión. | El proveedor |

**Mercado de arranque: Colombia** (venta nacional, pagos en COP, logística nacional). La exportación a Canadá / EE. UU. queda para una fase posterior.

**Demo en vivo:** https://kingbean.runasp.net

---

## Stack

- **Framework:** ASP.NET Core MVC — **.NET 10**
- **Datos:** Entity Framework Core 10 + SQL Server
- **Autenticación:** ASP.NET Core Identity (usuarios, roles y permisos granulares propios)
- **Frontend:** Vistas Razor + Tailwind CSS **4.3.3** con design system **`Patio de origen`** (`Web/Styles/bean-tailwind.css`) — tinta/crema/arcilla, tipografías Archivo Black, Manrope y Azeret Mono
- **Tests:** xUnit (`Tests/`)
- **CI/CD:** GitHub Actions → MonsterASP.NET (Web Deploy)

---

## Funcionalidades actuales

### Cliente (tienda)

- **Catálogo público** (`/`) con visibilidad filtrada por país: un producto solo aparece si está activo y disponible en el país seleccionado (cookie de país).
- **Detalle de producto** con trazabilidad real: origen, finca, altura, proceso (lavado / honey / natural), variedad, lote, fecha de tueste y notas de cata.
- **Carrito híbrido**: invitado en sesión, usuario autenticado en base de datos, con **fusión automática al iniciar sesión** (no se pierde nada al registrarse en mitad de la compra).
- **Checkout** con validación de stock, snapshot de precio, descuento de inventario y creación de la orden dentro de una transacción.
- **Confirmación de pedido** con línea de tiempo de seguimiento.
- **Mis pedidos** (`/Orders`): historial del usuario con estado por pedido y enlace al detalle.
- Páginas de marca: **Orígenes** (`/Home/Origenes`, agrupa los lotes activos por región con fincas, alturas y lotes) y **Nosotros** (`/Home/Nosotros`).

### Proveedor

- **Auto-registro de empresa** (`/Account/RegisterProvider`), sujeto a aprobación del administrador.
- **Mis productos** (`/Products`): crear y editar lotes (incluye atributos de café y datos de **empaque y envío**: peso y dimensiones), enviar a aprobación y marcar como enviado a bodega.
- **Pedidos entrantes** (`/SupplierOrders`): los pedidos que contienen productos propios, con **"Tu pago"** calculado solo sobre sus líneas.
- **Perfil de empresa**, **usuarios de la empresa** y **roles de la empresa**: cada proveedor administra su propio espacio, aislado del resto.

### Administrador

- **Dashboard** (`/Dashboard`) con KPIs reales: ventas del mes, pedidos, productos por aprobar, pedidos por despachar, gráfica de ventas por semana y actividad reciente.
- **Aprobación de productos** (`/ProductApproval`): aprobar o rechazar (con motivo) y **recibir** el inventario en bodega, lo que activa el producto en el catálogo.
- **Gestión de pedidos** (`/OrderManagement`): bandeja con filtros por estado, búsqueda y paginación; avance de estado por máquina de estados y **cancelación con reintegro de stock**.
- **Gestión de proveedores** (aprobar/rechazar empresas), **usuarios**, **roles** y **permisos granulares por módulo**.

---

## Arquitectura y conceptos clave

- **Multi-tenancy por `ProviderID`.** Cada usuario de proveedor lleva su `ProviderID`; los controladores acotan las consultas a ese identificador, de modo que un proveedor nunca ve productos, usuarios ni pedidos de otro.
- **Permisos granulares (RBAC).** Los permisos se definen como pares *módulo × acción* (`Create`, `Read`, `Update`, `Delete`) sobre los módulos `Users`, `Roles`, `Security`, `CompanyProfile`, `CompanyUsers`, `CompanyRoles`, `Products`, `ProductApprovals` y `Orders`. Se aplican con el atributo `HasPermission` sobre las acciones del controlador. En `Orders`, `Delete` significa *cancelar el pedido*.
- **Workflow de estados del producto:**

  ```
  Draft → PendingApproval → ApprovedToShip → Shipped → Active
                    ↘ Rejected
  ```

  El producto solo se vuelve visible en el catálogo cuando el admin lo recibe en bodega (`Active`).
- **Ciclo de vida del pedido** (`OrderWorkflow`), lineal y sin retrocesos:

  ```
  Pending → Confirmed → Processing → Shipped → Delivered
  (cancelable en cualquier punto → Cancelled)
  ```

  Al cancelar se reintegra el stock, **salvo** si el pedido ya estaba `Shipped` (la mercancía ya salió de bodega; el ajuste es manual).
- **Servicio de envío intercambiable.** `Web/Services/Shipping/` expone `IShippingQuoteService`; la implementación se elige por configuración con `Shipping:Provider`:

  | Valor | Comportamiento |
  |---|---|
  | `Fixed` | Tarifa plana de respaldo (`Shipping:FallbackCost`). Es el valor por defecto. |
  | `Mipaquete` | Cotización real con el transportador. **Aún no implementado**: hoy hace fallback a la tarifa fija y deja un warning en el log. |

  Lo acompañan `ShippingPackageBuilder` (cálculo puro de bulto y peso facturable) y `ShippingCityService` (catálogo cacheado de **municipios con código DANE / DIVIPOLA**).

---

## Puesta en marcha local

### Prerrequisitos

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Acceso a una instancia de SQL Server (la base de desarrollo del proyecto es remota; ver más abajo)

### 1. Clonar

```bash
git clone <url-del-repositorio>
cd Bean-Sales
```

### 2. Configurar la cadena de conexión

> ⚠️ **La cadena de conexión va en `Web/appsettings.Development.json`, NUNCA en `Web/appsettings.json`.**
> `appsettings.json` **sí se commitea**; `appsettings.Development.json` y `appsettings.Production.json` están en `.gitignore` precisamente para que ninguna contraseña llegue al repositorio.

Crea `Web/appsettings.Development.json` con tu cadena:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<host>;Database=<bd>;User Id=<usuario>;Password=<contraseña>;TrustServerCertificate=True;MultipleActiveResultSets=true"
  }
}
```

Pídele las credenciales de la base de desarrollo al responsable del proyecto; no están en el repositorio.

### 3. Aplicar migraciones

```powershell
cd Web
dotnet restore
dotnet ef database update
```

### 4. Ejecutar

```powershell
dotnet run
```

La consola indica la URL (por ejemplo `https://localhost:7193`).

### Qué pasa al arrancar

`Program.cs` ejecuta automáticamente, en cada inicio:

1. **`Database.MigrateAsync()`** — aplica las migraciones pendientes (por eso el paso 3 es opcional en la práctica, pero conviene verlo explícito la primera vez).
2. **Siembra de datos**: países, tipos de documento, **169 municipios colombianos con código DANE** (desde `Web/Data/Seeds/Data/colombia-cities.json`), roles, módulos y permisos, y el usuario **SuperAdmin**.

La siembra es idempotente: no duplica nada si ya existe. **No siembra productos** — el catálogo se llena por el flujo real del proveedor.

> Si añades un módulo de permisos nuevo, la app debe reiniciarse una vez para que el seed lo cree; de lo contrario los enlaces del panel no aparecen y las acciones devuelven 403.

---

## Credenciales del SuperAdmin sembrado

| Rol | Correo | Contraseña |
|---|---|---|
| **SuperAdmin** | `andres.felipe.camacho@outlook.com` | `Andipipe1*` |

> ⚠️ **Son credenciales de desarrollo.** Están en el código de siembra y en este repositorio público, así que **deben cambiarse en producción** (y la contraseña rotarse) antes de cualquier uso real. No las reutilices en otros entornos.

---

## Tests

```bash
dotnet test Tests/Tests.csproj
```

El proyecto `Tests/` **está fuera de `Bean-Sales.slnx` a propósito**: así `dotnet build` / `dotnet publish` de la solución y el workflow de despliegue (que apunta explícitamente a `Web/Web.csproj`) siguen funcionando sin cambios. Para incluirlo en la solución, si algún día se quiere: `dotnet sln Bean-Sales.slnx add Tests/Tests.csproj`.

---

## Despliegue

Hosting en **MonsterASP.NET** (`site81000` → https://kingbean.runasp.net), con despliegue continuo vía **GitHub Actions + Web Deploy**.

### Flujo de ramas

- `dev` — rama de trabajo. Los cambios se desarrollan aquí. **No dispara despliegue.**
- `qa` — **rama de despliegue**: cada push a `qa` (es decir, al mergear un PR de `dev` hacia `qa`) dispara el workflow y publica el sitio.
- El workflow también puede lanzarse a mano desde la pestaña *Actions* (`workflow_dispatch`).

### Qué hace el pipeline (`.github/workflows/deploy.yml`)

1. Corre en `windows-latest` (Web Deploy solo existe en Windows).
2. Publica con `--runtime win-x86 --self-contained false` — el app pool de MonsterASP es de **32 bits** y el host ya trae el runtime de .NET 10.
3. **Inyecta los secretos** en el `appsettings.json` publicado: la connection string siempre; la API key de Mipaquete y el origen de envío solo si los secrets existen (inyección defensiva — si faltan, el sitio sigue con tarifa fija).
4. Despliega con `rasmusbuchholdt/simply-web-deploy`.
5. Al arrancar, la app aplica migraciones y siembra sola.

> ⚠️ **Riesgo conocido:** las migraciones EF deben estar **commiteadas**, o el host no podrá aplicarlas.

### GitHub Secrets

| Secret | Obligatorio | Contenido |
|---|---|---|
| `MONSTERASP_WEBSITE` | Sí | Nombre del sitio en MonsterASP |
| `MONSTERASP_SERVER` | Sí | URL del endpoint de Web Deploy (`:8172`) |
| `MONSTERASP_USERNAME` | Sí | Usuario de Web Deploy |
| `MONSTERASP_PASSWORD` | Sí | Contraseña de Web Deploy |
| `MONSTERASP_CONNECTION_STRING` | Sí | Cadena de conexión de producción |
| `MIPAQUETE_API_KEY` | No | API key del transportador. Si está presente, el pipeline cambia `Shipping:Provider` a `Mipaquete`. |
| `SHIPPING_ORIGIN_DANE` | No | Código DANE del municipio de la bodega de origen |
| `SHIPPING_ORIGIN_CITY` | No | Nombre de la ciudad de origen |

Guía paso a paso: **`Docs/deploy_monsterasp.md`**.

---

## Seguridad y manejo de secretos

**Regla no negociable:**

- Los secretos (contraseñas de BD, API keys) viven **solo** en `Web/appsettings.Development.json` (local), `Web/appsettings.Production.json` (host) — ambos ignorados por git — y en **GitHub Secrets** (CI/CD).
- **Jamás** en `Web/appsettings.json`, que sí se commitea. Su sección `Shipping` contiene únicamente parámetros no sensibles (proveedor, tarifas de respaldo, dimensiones por defecto), con los campos de API key vacíos.
- Antes de reescribir el historial de git (`filter-repo`, `rebase`, etc.), **commitea siempre** los cambios pendientes.

---

## Estructura del proyecto

```
Bean-Sales/
├─ Web/                        # Aplicación ASP.NET Core MVC
│  ├─ Controllers/             # Controladores MVC
│  ├─ Models/                  # Entidades de dominio
│  │  ├─ Enums/                # ProductStatus, OrderStatus, CoffeeProcess
│  │  └─ ViewModels/           # ViewModels de vistas y formularios
│  ├─ Views/                   # Vistas Razor (tienda, panel y auth, con layouts propios)
│  ├─ Services/                # Lógica transversal
│  │  ├─ Shipping/             # Cotización de envío (interfaz + tarifa fija + catálogo de ciudades)
│  │  ├─ OrderWorkflow.cs      # Máquina de estados del pedido
│  │  └─ PermissionService.cs  # Evaluación de permisos
│  ├─ Data/
│  │  ├─ ApplicationDbContext.cs
│  │  └─ Seeds/                # ContextSeed.cs + Data/colombia-cities.json (169 municipios)
│  ├─ Migrations/              # Migraciones EF Core
│  ├─ Constants/               # Roles, Modules, Permissions
│  ├─ Styles/bean-tailwind.css     # Fuente del design system Tailwind
│  └─ wwwroot/css/bean-tailwind.css # Bundle generado
├─ Tests/                      # Pruebas unitarias xUnit (fuera de la solución a propósito)
├─ Docs/                       # Documentación de producto, diseño y despliegue
└─ .github/workflows/          # CI/CD
```

---

## Documentación relacionada

| Documento | Para qué |
|---|---|
| `CLAUDE.md` | Memoria del proyecto: estado actual, decisiones, alertas abiertas. **Punto de partida.** |
| `Docs/design_handoff_bean_redesign/` | **Fuente de verdad del diseño**: design system completo y specs de las 15 vistas. |
| `Docs/plan_de_trabajo_hub_2026-07.md` | Plan de trabajo y roadmap vigentes. |
| `Docs/deploy_monsterasp.md` | Guía de despliegue paso a paso. |
| `Docs/design_specification.md`, `Docs/implementation_plan.md` | Especificaciones originales (contexto histórico). |

---

## Estado y pendientes conocidos

- **No hay pasarela de pago.** El checkout crea la orden y descuenta el stock, pero **no cobra**. Falta integrar Wompi/PSE para COP.
- **Envío dinámico a medias.** La abstracción, el catálogo de municipios DANE, los campos de empaque y la persistencia del snapshot de envío ya existen; falta el cotizador real de Mipaquete y el selector de departamento/ciudad en el checkout. Hoy se cobra la tarifa fija de respaldo.
- **Cobertura de tests baja.** Existe una primera tanda de pruebas unitarias sobre el cálculo de empaque; el resto del sistema no tiene cobertura automatizada.
- **Vista pendiente de rediseño:** la matriz Café × País del panel.
