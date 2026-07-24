# CLAUDE.md — Contexto del proyecto (leer al iniciar cada sesión)

> Este archivo es la memoria del proyecto. Actualízalo cuando cambien decisiones clave.
> Rol de Claude: **Project Manager**. Andrés es el **Product Owner**. Preguntar como PO antes de asumir; administrar TODAS las áreas (código, legal, operaciones, finanzas, GTM), no solo código.
> Idioma de trabajo: español.

---

## 1. Qué es el proyecto

**The Kng Bean / Coffee Global Hub** — Hub **B2B2C de consolidación y exportación de café de especialidad colombiano**.
Compra/consigna café a marcas y fincas locales, lo centraliza en una **bodega en Colombia**, y lo vende a consumidores y pequeños negocios en el exterior (EE.UU., Canadá, Europa) con compra unificada ("el supermercado global del café colombiano").

**Modelo de monetización híbrido:**
- **Compra directa (retail arbitrage):** Bean compra el lote por adelantado y revende. Margen 30-60%. Bean asume flete a bodega.
- **Consignación (incubadora):** para marcas nuevas, el proveedor mantiene propiedad hasta que se vende y paga comisión 20-30%. El proveedor asume el flete a bodega.

Bean actúa como **Merchant of Record / exportador legal**: el proveedor local no necesita licencia de exportación.

---

## 2. Historia importante (evita reabrir debates ya cerrados)

El proyecto **pivoteó dos veces**. Hubo tres modelos conviviendo:
1. **Subastas** (Trello jun 2025) — DESCARTADO. Cards archivadas.
2. **E-commerce/marketplace** (código ene 2026) — VIGENTE, alineado al Hub.
3. **Hub híbrido** (Drive feb 2026) — **MODELO OFICIAL CONFIRMADO** por Andrés (jul 2026).

Corrección clave: el portal de proveedores + flujo de aprobación de productos del código **SÍ sirven** (los usa el esquema de consignación). No descartarlos: completarlos.

---

## 3. Estado técnico

- **Stack:** ASP.NET Core MVC, EF Core, SQL Server, ASP.NET Core Identity, Bootstrap 5. Carpeta principal: `Web/`.
- **Repo/ramas:** git, ramas `dev` (activa) y `master`. Último commit del repo: **30-ene-2026**.
- **Hecho:** Fase 1 (seguridad, roles/permisos granulares vía atributo `HasPermission`, seed SuperAdmin). Fase 2 (catálogo `Product` con workflow de estados + aprobación, `Provider`, `ProductCountry` con stock por país). Fase 3 parcial (carrito híbrido invitado/usuario con fusión al login; checkout solo hasta vista de resumen).
- **Implementación del rediseño — Incremento 1 (jul 2026, LOCAL sin commitear):** design system `Web/wwwroot/css/bean-theme.css` (tokens del handoff + componentes), layout de tienda `Web/Views/Shared/_LayoutStore.cshtml` (nav oscura + footer + fuentes Bitter/DM Sans), `_ViewStart` en `Home` y `Cart`, y storefront reestilizado: `Home/Index` (catálogo), `Home/Details`, `Cart/Index`, `Cart/Checkout`. Verificado por subagente: seguro para `dotnet build`, sin `.cs` tocados. Pendiente: build/commit por Andrés. Nota: `Home/Privacy` ahora hereda `_LayoutStore` (cosmético). Placeholders estáticos (rating, notas de cata, altura, proceso, lote, envío fijo) requieren campos nuevos en `Product` y el ciclo `Order` para wiring real.
- **Ciclo de venta — Incremento 2 (jul 2026, LOCAL sin commitear):** modelos `Order`/`OrderItem` + enum `OrderStatus`, `DbSet`s y relaciones en `ApplicationDbContext` (Order→User y OrderItem→Product en `Restrict`, OrderItem→Order en `Cascade`, sin multiple cascade paths). `CartController`: POST `Checkout` (valida stock, crea orden con snapshot de precio, descuenta `Product.Stock`, vacía carrito, transacción) y GET `Confirmation`. Vista `Cart/Confirmation.cshtml` con línea de tiempo de seguimiento. Verificado por subagente: listo para migrar/compilar. **Andrés debe correr** (desde `Web/`): `dotnet ef migrations add AddOrders` → `dotnet ef database update` → `dotnet build`/`run`. Simplificaciones: envío fijo `18000`, pago sin pasarela (placeholder), stock global, país como string. **VALIDADO EN VIVO (22-jul-2026):** compra e2e OK — orden #DCF46AC3 creada, stock 36→34, carrito vaciado, checkout POST 200 sin errores. Único hallazgo: el bug de UI del "Total" sin envío (ver Alertas).
- **⚠️ Alertas abiertas:**
  - El "rediseño" de Andrés está **LOCAL SIN COMMITEAR** — recordarle hacer commit/push a `dev`.
  - Hay 4 logs de build borrados sin commitear en el working tree.
  - **No hay tests automatizados.**
  - **BUG (UI) del "Total" sin envío — CORREGIDO (22-jul, pendiente validar tras rebuild):** se centralizó `ShippingCost=18000m` en `CartController` (usado en las 4 rutas que renderizan Cart/Index|Checkout vía `ViewBag.Shipping`/`GrandTotal` y en `order.TotalAmount`). Vistas muestran Total con envío ($18.040) y botón "Pagar $18.040". Sin migración.
  - **BUG de aprobación — DIAGNOSTICADO Y CORREGIDO (22-jul-2026, pendiente validar tras rebuild):** al aprobar un producto salía **HTTP 405**. Causa: las acciones `Approve`/`Reject` no existían en `ProductApprovalController` (solo un comentario placeholder), pero la vista `ProductApproval/Index.cshtml` postea a ellas. Fix: se implementaron `[HttpPost] Approve` (PendingApproval→ApprovedToShip) y `[HttpPost] Reject` (→Rejected con motivo), con permiso `ProductApprovals/Update` y antiforgery. Sin cambios de modelo (no requiere migración). Falta: Andrés hace `dotnet build`/run y valida aprobar/rechazar. Observación menor: el `switch` de estados de `Index.cshtml` no muestra badge para `ApprovedToShip` (cosmético).
- **Enriquecer Product — Incremento 3 (jul 2026, LOCAL sin commitear):** 10 campos nuevos en `Product` (Origin, Farm, Altitude, Process `CoffeeProcess?`, Variety, Lot, RoastDate, TastingNotes, Rating, ReviewCount — todos nullable) + enum `CoffeeProcess`. `ProductViewModel` + `ProductsController` (mapeos Create/Edit) y formularios `Products/Create|Edit` actualizados. Storefront (`Home/Index`, `Home/Details`) cableado a datos reales null-safe; se eliminaron los placeholders estáticos. Verificado por subagente: listo para migrar/compilar. **Andrés debe correr:** `dotnet ef migrations add EnrichProduct` → `dotnet ef database update`. Nota: `ContextSeed` NO siembra productos (los datos reales entran por el flujo del proveedor); el storefront degrada bien con nulls.
- **Mis pedidos — Incremento 4 (jul 2026, LOCAL sin commitear):** `OrdersController` (`[Authorize] Index` lista los pedidos del usuario) + `Views/Orders/Index.cshtml` (historial estilo handoff vista 6: sidebar de cuenta, filtros píldora visuales, filas con estado; enlaza a `Cart/Confirmation/{id}` como detalle) + enlace "Mis pedidos" en `_LayoutStore` (solo autenticados) + estilos en `bean-theme.css`. Sin migración (usa el modelo `Order`). Verificado por subagente: compila. Placeholders: "Volver a pedir", Direcciones/Métodos de pago/Reseñas del sidebar.
- **Back-office — Incremento 5 (jul 2026, LOCAL sin commitear):** shell del panel interno reestilizado al handoff (`_Layout.cshtml` + `_Sidebar.cshtml`: sidebar oscuro `#1d1510`, badge de rol, contador de pendientes vía `CountAsync`, topbar) + bandeja de **Aprobaciones** (`ProductApproval/Index` con tabs/badges) y `Receive` reestilizadas. Se arregló el switch de estados (ahora cubre los 6 `ProductStatus`, incl. `ApprovedToShip`/`Active`). Sin `.cs` ni migración; bindings Approve/Reject/Receive preservados. Verificado por subagente con `engineering:code-review`: compila. **Pendiente:** reestilizar el resto de vistas del panel (Products, Providers, Users, Roles, Company\*, + Dashboard, Matriz Café×País, vistas de proveedor) — hoy conviven con look SB-Admin viejo dentro del shell nuevo (mezcla visual, no rompe). Limpieza opcional: JS/CSS del sidebar viejo sin uso.
- **Lado proveedor — Incremento 6 (jul 2026, LOCAL sin commitear):** `SupplierOrdersController` + `Views/SupplierOrders/Index.cshtml` + `SupplierOrderViewModel` — "Pedidos entrantes": el proveedor ve los pedidos que contienen sus productos, con **"Tu pago"** = suma de `SubTotal` de solo sus líneas (scoping por `ApplicationUser.ProviderID`, patrón de `ProductsController`, permiso `Products/Read`). `Products/Index` reestilizado al handoff vista 12 (stats reales vía `OrderItems.SumAsync`, badges de estado; bindings Create/Edit/SubmitForApproval/Ship preservados). Enlace "Pedidos entrantes" en el sidebar (solo proveedor). Sin `.cs` de modelo ni migración. Verificado por subagente con `engineering:code-review`: compila. Placeholder: CTAs "Marcar listo/Ver guía" son visuales (no hay transición de estado por-proveedor aún).
- **Páginas de acceso — Incremento 7 (jul 2026, LOCAL sin commitear):** `_LayoutAuth.cshtml` split-screen del handoff + `_ViewStart` en `Views/Account/` (solo 4 vistas auth) → Login (vista 7), Register, RegisterProvider (vista 14 onboarding, radio-cards visuales sin campo real), RegistrationSuccess reestilizados. Bindings Identity preservados (`asp-for`/`asp-validation`/`returnUrl`/antiforgery). CSS scoped `.bean-auth-body`. Verificado (grep + `engineering:code-review`). Sin `.cs` ni migración.
- **Dashboard admin — Incremento 8 (jul 2026, LOCAL sin commitear):** `DashboardController` (`[Authorize(Roles=SuperAdmin)]`) + `DashboardViewModel` + `Views/Dashboard/Index.cshtml` con KPIs REALES (ventas del mes vía `SumAsync`, pedidos, pendientes de aprobación, productos activos), gráfica "ventas por semana" (8 semanas, agrupada en memoria, barras CSS puras), lista "por aprobar" y "actividad reciente". Enlace "Dashboard" en el sidebar (solo SuperAdmin). NO cambia el landing (Home/Index sigue siendo el storefront). Verificado con `engineering:code-review`. Sin migración.
- **CRUD del panel — Incremento 9 (jul 2026, LOCAL sin commitear):** las 17 vistas de Users, Roles, Providers, CompanyProfile, CompanyUsers, CompanyRoles reestilizadas al panel (clases `bean-panel-*`, `bean-btn-pill`, `bean-form-*`, `bean-perm-grid` en `bean-theme.css` sección 21) — se eliminó el look SB-Admin viejo. Bindings (`@model`, tag helpers, forms, antiforgery, checks de permeiso) preservados y verificados. Sin `.cs` ni migración. Con esto el back-office queda visualmente coherente (falta solo Matriz Café×País, diferida).
- **Conexión a BD (jul 2026):** la BD de **desarrollo/pruebas es la de MonsterASP** (`db60805`, SQL Server remoto); la **localdb queda descartada**. La cadena vive en `Web/appsettings.Development.json` (dev) y `Web/appsettings.Production.json` (host), **ambos gitignored y untracked** — la contraseña NO se sube al repo. `dotnet run` (entorno Development por defecto) apunta a MonsterASP y auto-migra al iniciar. `appsettings.json` conserva la cadena localdb como base no usada. En el sitio desplegado, la cadena va en el panel de MonsterASP como `DefaultConnection`.
- **Despliegue — CI/CD (jul 2026):** MonsterASP.NET + GitHub Actions. **Sitio creado: `site81000` → https://kingbean.runasp.net** (plan Free, .NET x86, 256 MB RAM). Workflow `.github/workflows/deploy.yml` (push a `dev` o `master` + manual; `windows-latest`; publica **`--runtime win-x86 --self-contained false`** porque el app pool es 32 bits; inyecta la connection string de producción en `appsettings.json` en build desde el secret; despliega con `rasmusbuchholdt/simply-web-deploy@2.2.0` vía Web Deploy). `Program.cs` auto-migra al iniciar. **5 GitHub Secrets:** `MONSTERASP_WEBSITE`=site81000, `MONSTERASP_SERVER`=https://site81000.siteasp.net:8172, `MONSTERASP_USERNAME`=site81000, `MONSTERASP_PASSWORD`=(WebDeploy), `MONSTERASP_CONNECTION_STRING`=(cadena MonsterASP). Guía: `Docs/deploy_monsterasp.md`. **Riesgo clave:** las migraciones EF deben estar commiteadas o el host no las aplicará.
- **Bloqueantes para vender de verdad:** `Order`/`OrderItem` implementado (Inc. 2) y `Product` enriquecido (Inc. 3), ambos pendientes de migración+commit por Andrés. Falta: **pasarela de pago real** (Wompi/PSE COP) y **cálculo de envío real** (hoy fijo 18000).

---

## 4. Dónde está la información

- **Plan de trabajo vigente:** `Docs/plan_de_trabajo_hub_2026-07.md` (fuente de verdad del roadmap).
- **Diagnóstico anterior (obsoleto en parte):** `Docs/project_status_and_roadmap.md` (8-jul-2026, decía "compra-reventa pura"; corregido por el plan de julio).
- **Specs originales:** `Docs/design_specification.md`, `Docs/implementation_plan.md`.
- **Google Drive — carpeta `Coffee Global Hub`** (conector Drive conectado):
  - `Plan de negocios/PLAN DE NEGOCIO.docx` — modelo de negocio completo.
  - `Plan de negocios/Exportación de Café Colombia a Canadá.docx` — legal/logística/costos (~USD 26-28/kg).
  - `Flujos de comportamiento/` — 4 diagramas PNG: registro/validación de proveedor, creación/aprobación de productos, abastecimiento a bodega, comportamiento de cliente.
- **Trello — tablero "The kng bean Fase 1"** (conector Trello conectado):
  URL: https://trello.com/b/S30tmyS3/the-kng-bean-fase-1
  Listas clave creadas jul 2026: `✅ Logros alcanzados`, `🔧 Cierre Técnico – Fase 2`, `🌐 Negocio: Legal/Operaciones/Finanzas/GTM`.
- **⭐ Rediseño aprobado (FUENTE DE VERDAD de UI):** `Docs/design_handoff_bean_redesign/` — handoff hi-fi con `README.md` (design system completo + specs de 15 vistas), `screenshots/01–15` y prototipos `.dc.html`. Dirección visual "4a moderna animada": base café oscuro `#1d1510`, acento terracota gradiente `linear-gradient(135deg,#d98a5f,#B4552E)`, tipografías **Bitter** (titulares) + **DM Sans** (cuerpo), píldoras y motion sutil. Ya contempla el modelo Hub (selector Colombia/Canadá, matriz Café×País, precios COP/CAD, aprobación con comentarios). **Recrear pixel-perfect en las vistas Razor.**
- **Figma** (conector Figma conectado, cuenta de Andrés, plan Starter):
  - Diagramas de flujo (FigJam): https://www.figma.com/board/oCNE7wyX7khhsLGQoWJa65 ✅ útil.
  - UI reconstruida del Bootstrap del código: https://www.figma.com/design/weaMwgHnOu1k1CaPCKmy4L — **SUPERADA por el handoff aprobado**. No usar como referencia de diseño; el handoff manda.
  - **Figma DESCARTADO (jul 2026):** el plan Starter tiene un tope de llamadas MCP que exige upgrade; no es viable recrear el diseño ahí. El handoff en `Docs/` es la fuente de diseño para implementar en código. Retomar solo si Andrés sube de plan.

---

## 5. Roadmap resumido (detalle en el plan de julio)

- **P0 (decisiones de PO):** confirmar mercado piloto (¿Canadá?); capital de trabajo disponible; si arranca con compra directa/consignación/ambos.
- **P1 (cierre técnico – Fase 2):** commitear rediseño; `Order`/`OrderItem` + persistencia + descuento de stock; pagos reales (Wompi/PSE COP · Stripe/PayPal USD); país/moneda por IP; cálculo de envío + consolidación; completar panel de proveedor + filtro de admisión (RUT, Cámara, Invima, FNC + muestra física); tests.
- **P2 (negocio en paralelo):** SAS Colombia + registro exportador; estructura/importación en destino (SFC/CFIA, etiquetado bilingüe); bodega + alianzas de logística; modelo financiero; primer proveedor y primeros clientes.

---

## 6. Decisiones de PO

**Tomadas (21-jul-2026):**
- **Mercado de arranque: Colombia (local primero).** Sin exportación en el lanzamiento inicial → NO se necesita SFC/CFIA/etiquetado Canadá al inicio. **La exportación a Canadá pasa a Fase 2.** Foco inicial: SAS Colombia, logística nacional, pagos COP (Wompi/PSE).
- **Modelo de lanzamiento: AMBOS a la vez** (compra directa + consignación).
- **Branding: identidad propia YA aprobada** en el handoff (dirección "4a moderna animada"). No re-abrir.

**Aún pendientes:**
- Capital de trabajo para el primer inventario: **sin definir** → trabajar en el modelo financiero (con "ambos", la consignación reduce la necesidad de capital).
- ¿Ya hay proveedor candidato con quien se haya hablado?

---

## 7. Convenciones de trabajo

- Actuar como PM: mantener Trello como fuente de verdad, documentar decisiones, hacer preguntas de PO antes de avanzar sobre supuestos.
- Al cerrar trabajo relevante, actualizar este `CLAUDE.md` y el plan en `Docs/`.
- Conectores disponibles: Trello, Google Drive. (GitHub/Slack/otros requieren autorización del usuario si se necesitan.)
- **⚠️ Razor gotcha (RZ1010):** dentro de un bloque de código (`@foreach`/`@for`/`@if` `{ }`) ya se está en contexto C#; escribir `@{ ... }` ahí falla al compilar (RZ1010). Usar sentencias sueltas (`var x = ...;`) sin `@{`. Los subagentes de QA NO lo detectan (requiere `dotnet build` real). Corregido en ProductApproval/Index y Receive (jul 2026).
- **Subagentes usan skills del plugin `engineering`:** QA/verificación con `engineering:code-review`; desarrollo/diagnóstico con `engineering:debug`, `engineering:architecture`, `engineering:testing-strategy` según aplique. (Instrucción de Andrés, jul 2026.)
- Credenciales SuperAdmin de la app y detalles de instalación: ver `README.md`.
