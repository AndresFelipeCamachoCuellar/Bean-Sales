# Briefs de handoff para los agentes de desarrollo — beta agosto 2026

> Cada bloque se pega tal cual como prompt del agente. Están escritos para ejecutarse **en este orden**.
> Spec completa: `Docs/epicas_beta_2026-08.md`. Contexto del proyecto: `CLAUDE.md`.

---

## Reglas comunes a TODOS los agentes

Pegar esto al inicio de cada brief:

```
Trabajas sobre el repo Bean-Sales (ASP.NET Core MVC + EF Core + SQL Server + Identity),
rama `dev`. Lee CLAUDE.md y Docs/epicas_beta_2026-08.md antes de tocar nada.

REGLAS INNEGOCIABLES
1. NO ejecutes migraciones ni `dotnet ef database update`. Generas el código de la
   migración y Andrés la corre. La BD de desarrollo es la REMOTA de MonsterASP:
   un update mal hecho tumba el entorno de pruebas.
2. NO commitees ni hagas push. Andrés revisa y commitea.
3. Razor gotcha RZ1010: dentro de @foreach/@for/@if { } YA estás en contexto C#.
   Escribir @{ ... } ahí NO COMPILA. Usa sentencias sueltas.
4. Nada de secretos en el repo. appsettings.json lleva la sección vacía; los valores
   van en appsettings.Development.json (gitignored) y en GitHub Secrets.
5. Sigue los patrones que ya existen. No introduzcas librerías ni arquitecturas nuevas:
   - Auditoría: CreatedBy/CreatedOn/ModifiedBy/ModifiedOn/Status en cada entidad.
   - Permisos: atributo [HasPermission(Modules.X, Permissions.Y)].
   - Multi-tenant: scoping por ApplicationUser.ProviderID (ver ProductsController).
   - Degradación segura: si falta config, el sistema sigue funcionando (ver
     DisabledImageStorage y el fallback de Mipaquete).
   - Listados: paginación de 25 con tabs server-side (ver OrderManagementController).
   - Estilos: clases bean-* de wwwroot/css/bean-theme.css. NO SB-Admin.
6. Nada de Guid.ToString() ni métodos custom dentro de queries EF (no traducen a SQL).
7. Al terminar: corre la skill `engineering:code-review` sobre tu propio cambio y
   entrega un resumen con (a) archivos tocados, (b) comandos EXACTOS que debe correr
   Andrés, (c) qué debe validar a mano, (d) deuda técnica que dejaste anotada.
```

---

## AGENTE 1 — Permisos (E5.1) · rápido, desbloquea el resto

**Alcance:** un solo archivo.

```
Agrega los módulos de permisos nuevos al seed.

Archivo: Web/Data/Seeds/ContextSeed.cs, método SeedPermissionsAsync.

1. Añade al array `modules` y a la clase de constantes correspondiente:
   Inventory, Warehouses, Pricing, Agreements, Settlements, Notifications.
2. Añade sus nombres en español al switch: "Inventario", "Bodegas", "Precios",
   "Acuerdos con proveedor", "Liquidaciones", "Notificaciones".
3. Verifica que el bloque que asigna TODOS los permisos al rol SuperAdmin los recoja
   automáticamente (hoy itera sobre ParametricPermissions, así que debería).
4. NO se los asignes al rol de proveedor: Pricing y Agreements son información
   sensible de Bean.

No requiere migración (son filas de datos, no esquema). SÍ requiere reiniciar la app
una vez para que el seed corra — anótalo en tu resumen.
```

---

## AGENTE 2 — Blindaje multi-tenant (E5.2) · antes de agregar 6 controladores nuevos

```
Hoy el scoping por ProviderID está repetido a mano en cada controlador
(ProductsController, SupplierOrdersController). E1-E4 agregan al menos 6 controladores
nuevos; si uno se olvida del filtro, expone datos de otro proveedor.

1. Audita TODOS los controladores actuales y lista dónde se aplica scoping por
   ProviderID y dónde debería aplicarse pero no se aplica. Reporta los hallazgos
   ANTES de refactorizar.
2. Extrae el patrón a un servicio inyectable IProviderScope con, como mínimo:
   - CurrentProviderId (null si es staff de Bean)
   - IsStaff / IsProvider
   - ApplyTo<T>(IQueryable<T>) para entidades con ProviderID
3. Refactoriza ProductsController y SupplierOrdersController para usarlo, SIN cambiar
   su comportamiento observable.
4. Tests en Tests/ (xUnit, como ShippingPackageBuilderTests): con dos proveedores A y B
   sembrados, el usuario de A no ve productos, pedidos ni usuarios de B — ni en el
   listado ni accediendo directo por ID (IDOR). Cada caso, un test.
5. Vista para SuperAdmin: dado un usuario, mostrar sus permisos EFECTIVOS y de qué rol
   le viene cada uno. Reutiliza ManagePermissionsViewModel si encaja.

Prioriza el reporte del punto 1: si encuentras una fuga real, párate y repórtala.
```

---

## AGENTE 3 — Inventario por bodegas (E1) · LA PIEZA GRANDE

Esta épica es lo suficientemente grande como para partirla en dos tandas. **No dar las dos a la vez.**

### Tanda 3A — modelo, servicio y migración (sin UI)

```
Implementa el núcleo del inventario multi-bodega. Lee Docs/epicas_beta_2026-08.md § E1
para el detalle de campos; aquí va lo esencial y las trampas.

ENTIDADES NUEVAS (Web/Models/)
- Warehouse: WarehouseID, Code (único, 20), Name, CountryID (FK Country), City,
  DaneCode (10, nullable, enlaza con ShippingCity), Address, IsDefault, IsActive
  + auditoría del proyecto.
- StockItem: PK COMPUESTA (ProductID, WarehouseID). QuantityOnHand, QuantityReserved,
  QuantityAvailable como propiedad CALCULADA [NotMapped] (OnHand - Reserved, NUNCA
  persistida), Ownership (enum BeanOwned|Consignment), ReorderPoint (int?),
  RowVersion ([Timestamp]).
- StockMovement: libro mayor INMUTABLE. StockMovementID, ProductID, WarehouseID,
  MovementType (enum: Reception, Sale, SaleCancelled, CustomerReturn, Adjustment,
  TransferOut, TransferIn, Loss, ReturnToProvider), Quantity FIRMADO (+entra/-sale),
  ReferenceType (string 30) + ReferenceID (Guid?), Reason (300, nullable pero
  OBLIGATORIO en Adjustment y Loss), UnitCost (decimal 18,2 nullable), CreatedBy,
  CreatedOn.

SERVICIO — Web/Services/Inventory/InventoryService.cs
Método MoveStock(productId, warehouseId, movementType, quantity, reference, reason,
unitCost, user) que, EN UNA SOLA TRANSACCIÓN:
  1. escribe el StockMovement
  2. actualiza el saldo del StockItem (creándolo si no existe)
  3. recalcula Product.Stock = SUM de QuantityOnHand de todas sus bodegas
Debe ser el ÚNICO punto del código que escribe stock. Maneja
DbUpdateConcurrencyException (RowVersion) con reintento acotado.

CONTEXTO — ApplicationDbContext
DbSets + configuración. CUIDADO con "multiple cascade paths": el proyecto ya resolvió
esto usando DeleteBehavior.Restrict en Order→User y OrderItem→Product. Aplica el mismo
criterio. Índice en (ProductID, WarehouseID, CreatedOn) sobre StockMovement: el hosting
es de 256 MB y esa tabla crece sin techo.

CAMPOS EN ENTIDADES EXISTENTES
- Product.Stock SE CONSERVA como total denormalizado (lo consumen catálogo, carrito,
  checkout, confirmación, Mis pedidos, aprobaciones). NO lo borres.
- OrderItem += WarehouseID (Guid?, de dónde salió la unidad).
- Order += FulfillmentWarehouseID (Guid?).

MIGRACIÓN — genera el código, NO la ejecutes
Nombre: AddWarehouseInventory. Incluye en el Up():
  1. seed de la bodega CAL-01 "Bodega Principal Cali", IsDefault=true, DaneCode=76001
     (76001 es el DANE; 760001 es el código POSTAL — no los confundas, ya pasó antes).
  2. backfill: por cada Product con Stock > 0, un StockItem en CAL-01 con
     QuantityOnHand = Product.Stock, Ownership = BeanOwned, y un StockMovement
     tipo Adjustment con Reason "Migración inicial de stock global a bodega".
Entrega también el SQL de verificación que Andrés debe correr después:
  SUM(StockItem.QuantityOnHand) por producto == Product.Stock, para el 100%.

REFACTOR
- CartController (checkout): descuenta vía InventoryService de la bodega que despacha,
  no de Product.Stock directo. Guarda WarehouseID en cada OrderItem.
- OrderManagementController (Cancel): reintegra vía InventoryService. RESPETA la regla
  vigente: si el pedido ya estaba Shipped NO se reintegra (ya salió de bodega).
- Wompi: la reserva de stock del checkout ahora mueve QuantityReserved, no OnHand.

TESTS en Tests/
- vender descuenta la bodega correcta y genera movimiento Sale
- dos cancelaciones simultáneas del mismo pedido NO reintegran dos veces
- SUM(StockMovement.Quantity) == StockItem.QuantityOnHand siempre

NO toques vistas en esta tanda. Cero UI.
```

### Tanda 3B — UI de administración

```
Con el núcleo de la tanda 3A ya en su sitio, construye la interfaz.

1. InventoryController + Views/Inventory/
   - Index: matriz producto × bodega. Celda = disponible; tooltip con en mano y
     reservado. Filtros: bodega, proveedor, estado del producto, "solo stock bajo",
     búsqueda. Totales por fila y por columna. Paginación 25 (patrón de
     OrderManagementController). Badge rojo si disponible <= ReorderPoint, gris si 0.
   - Details/{productId}: saldo por bodega + tabla de movimientos (fecha, tipo,
     cantidad, referencia, usuario, razón), paginada.
   - Adjust: POST de ajuste manual con razón OBLIGATORIA. Permiso Inventory/Update.
   Permisos: [HasPermission(Modules.Inventory, ...)].

2. WarehousesController + vistas: CRUD completo. Regla: no se puede desactivar una
   bodega con stock > 0. Permiso Warehouses/*.

3. ProductApproval/Receive: hoy solo cambia el estado. Ahora además pide BODEGA,
   CANTIDAD y COSTO UNITARIO, y llama a InventoryService con movimiento Reception.
   Sin bodega seleccionada no se puede recibir.

4. _Sidebar.cshtml: enlaces "Inventario" y "Bodegas" (visibles según permiso).
   Dashboard: KPI de productos bajo el punto de reorden.

5. Mipaquete: el origen de la cotización deja de ser la constante de appsettings y
   pasa a ser el DaneCode de la bodega que despacha. Mantén el valor de appsettings
   como fallback si la bodega no tiene DaneCode.

Estilos: clases bean-* existentes. Referencia visual: el handoff de
Docs/design_handoff_bean_redesign/ (la vista 11 "Matriz Café×País" es el pariente más
cercano a esta matriz).
```

---

## AGENTE 4 — Pricing y margen (E2)

> ✅ **RESUELTO (decisión de PO, 07-ago-2026):** **un solo agente genera las dos migraciones** (E2 primero, que solo agrega columnas; luego E1, que agrega relaciones). En la práctica: el agente 3A y el agente 4 se fusionan en un solo encargo. Ver "Encargo fusionado" al final del documento.

```
Separa el costo del proveedor del precio de venta al público. Hoy Product.Price es un
solo campo que pone el proveedor y con el que Bean vende: el negocio no gana nada.

MODELO
- Product += SupplierPrice (decimal 18,2 — el COSTO, lo edita el proveedor),
  PriceSetAt (DateTime?), PriceSetBy (string?), MarginAlert (bool).
  Product.Price pasa a significar PVP y SOLO lo edita quien tenga Pricing/Update.
- PricingSettings: entidad de fila única. TargetMarginPercent, MinimumMarginPercent,
  RoundingStep. Valores iniciales del seed, DECIDIDOS POR EL PO: 30, 15, 50.
  (El 15% es el PISO que dispara la alerta, NO la meta. Con 15% y Wompi el neto real
  cae a ~10%. El sugerido apunta a 30%.)
- PriceChangeLog: ProductID, ChangeType (SupplierPrice|SalePrice), OldValue, NewValue,
  MarginBefore, MarginAfter, ChangedBy, ChangedOn, Reason (nullable),
  NotifiedProvider (bool).
- OrderItem += SupplierPriceSnapshot (decimal 18,2). IMPRESCINDIBLE: sin esto E3 no
  puede liquidar. El snapshot se toma al crear la orden, igual que el precio.

MIGRACIÓN AddPricing — genera el código, NO la ejecutes
BACKFILL OBLIGATORIO en el Up(): SupplierPrice = Price y MarginAlert = true para TODAS
las filas existentes. Sin esto los productos actuales quedan con costo 0 y margen
ficticio del 100%. Entrega el SQL de verificación:
  SELECT COUNT(*) FROM Products WHERE SupplierPrice = 0  →  debe dar 0.

SERVICIO PURO — Web/Services/Pricing/PricingCalculator.cs (con tests, patrón de
ShippingPackageBuilder y WompiSignature)
- CalculateMargin(price, supplierPrice) → decimal, absoluto y porcentual
- SuggestPrice(supplierPrice, targetMarginPercent, roundingStep)
  = techo(supplierPrice / (1 - target), roundingStep). Redondeo HACIA ARRIBA, igual
  que ya se hace con los precios de envío.
- IsBelowMinimum(price, supplierPrice, minimumPercent)
Casos borde a cubrir: supplierPrice = 0, price < supplierPrice (margen negativo),
target = 100% (división por cero).

REGLAS DE NEGOCIO
1. Si el proveedor cambia su SupplierPrice, el Price NO se mueve. Si el margen queda
   bajo el mínimo → MarginAlert = true. El producto SIGUE VENDIÉNDOSE: no bloquees el
   catálogo por un tema de margen.
2. Guardar un PVP bajo el mínimo SE PERMITE, con confirmación explícita y razón
   obligatoria que se persiste en PriceChangeLog (promociones, liquidaciones).
3. 🔒 CANDADO: un producto NO puede pasar a ProductStatus.Active sin Price fijado por
   alguien con Pricing/Update. Es la garantía de que Bean nunca venda al costo.
   Súmalo al flujo de aprobación existente.
4. SupplierPrice, margen y PriceChangeLog SOLO visibles con Pricing/Read.

UI
- Formularios del proveedor (Products/Create|Edit): el proveedor edita SupplierPrice.
  El campo PVP NI SE RENDERIZA sin Pricing/Update (no basta con deshabilitarlo:
  valida también en el POST).
- Panel de pricing en la aprobación: costo, PVP editable, margen en $ y % recalculado
  en vivo con JS, precio sugerido con botón "usar este precio". El sugerido NUNCA se
  aplica solo.
- Bandeja "Márgenes por revisar" (MarginAlert = true) + KPI en el Dashboard.
- Configuración de % objetivo / mínimo / redondeo (solo SuperAdmin).
- Vista del proveedor: su costo y el PVP de SUS productos. Nunca los de otro proveedor.

VERIFICACIÓN CRÍTICA: cambiar el PVP de un producto NO debe alterar el total de ningún
pedido histórico. OrderItem ya guarda snapshot de precio — confirma que sigue así.
```

---

## AGENTE 5 — Acuerdos y consignación (E3)

**Bloqueado hasta que E1 y E2 estén en `dev`.** Además necesita 4 decisiones del PO (ver final del doc de épicas).

```
Implementa los acuerdos con proveedor y las liquidaciones. Los TRES esquemas
(consignación, compra en firme, comisión) van completos: decisión de PO del 07-ago-2026.

Lee Docs/epicas_beta_2026-08.md § E3: la matriz de reglas por esquema es la
especificación exacta. No improvises sobre ella.

Lo delicado de esta épica es el dinero, así que:
1. SettlementService debe tener un núcleo de cálculo PURO y testeable, separado del
   acceso a datos. Los tests son parte del entregable, no un extra.
2. Invariantes que los tests deben demostrar:
   - una OrderItem NUNCA aparece en dos liquidaciones aprobadas
   - regenerar la liquidación sobre el mismo período da el mismo total
   - un pedido cancelado sale del período, o entra como ajuste negativo si ya se
     liquidó
3. El Ownership se CONGELA en la recepción según el acuerdo vigente ese día. Cambiar
   el acuerdo después NO reescribe stock ya recibido. Este es el error clásico de
   este modelo: no lo cometas.
4. La compra en firme genera el pasivo al RECIBIR el lote, no al vender.

YA DECIDIDO POR EL PO (07-ago-2026), no lo re-preguntes:
  - Una venta entra a la liquidación cuando OrderStatus == Delivered Y
    PaymentStatus == Approved. El disparador es la ENTREGA, no el pago del cliente.
  - SettlementFrequency por defecto: Biweekly (día 15 y último de mes), configurable
    por proveedor.

ANTES DE EMPEZAR, confirma con el PM estas 3 decisiones (siguen pendientes):
  a) ¿la comisión se calcula sobre el PVP o sobre el neto después de Wompi
     (2,65% + $700 + IVA)?
  b) ¿quién asume pasarela y envío en la liquidación de consignación?
  c) ¿qué tipo de acuerdo se le asigna por defecto a los proveedores ya existentes?
Si no tienes respuesta, PÁRATE y pregunta. No asumas.
```

---

## AGENTE 6 — Notificaciones (E4)

**Último.** Escucha eventos de E2 y E3.

```
Implementa notificaciones en panel + correo. El diseño YA EXISTE:
Docs/design_handoff_bean_redesign/screenshots/15-notificaciones.png. Impleméntalo
contra ese handoff, no inventes UI.

Modelo Notification (UserID|ProviderID, Type, Title, Body, LinkUrl, IsRead, ReadOn,
CreatedOn, EmailSentOn?) + NotificationService idempotente por (evento, entidad):
reintentar un evento NO puede crear dos notificaciones.

Correo: IEmailSender + SmtpEmailSender + DisabledEmailSender. Mismo patrón de
degradación segura que DisabledImageStorage: sin credenciales configuradas el sistema
funciona igual, solo no manda correos. El envío es ASÍNCRONO Y NO BLOQUEANTE: si el
SMTP se cae, la venta, la aprobación y la liquidación se completan igual (mismo
criterio que el fallback de Mipaquete).

Cablea los 8 eventos de la tabla en Docs/epicas_beta_2026-08.md § E4.

El aviso de PVP al proveedor lleva ESTE TEXTO, aprobado por el PO (no lo reescribas):
"Hola {Proveedor}. Tu café {Producto} ya está publicado en la tienda. Tu precio:
${SupplierPrice}. Precio de venta al público: ${Price}. La diferencia cubre el
almacenamiento, la logística, la pasarela de pago y la comercialización que hace Bean."

Absorbe la card de backlog "Notificaciones por correo (compra, aprobación, aviso al
proveedor)".
```

---

## Secuencia recomendada

| # | Agente | Depende de | Puede ir en paralelo con |
|---|---|---|---|
| 1 | Permisos (E5.1) | — | todo |
| 2 | Multi-tenant (E5.2) | 1 | 3A, 4 |
| 3A | Inventario núcleo | 1 | 4 (coordinar migración de `Product`) |
| 3B | Inventario UI | 3A | 4 |
| 4 | Pricing (E2) | 1 | 3A/3B (coordinar migración) |
| 5 | Acuerdos (E3) | 3A, 4 | — |
| 6 | Notificaciones (E4) | 4, 5 | — |

---

## Encargo fusionado 3A + 4 — "Núcleo de datos" (decisión de PO)

Como los dos tocan `Product`, **van juntos en un solo agente**, que genera las dos migraciones en este orden:

1. **`AddPricing`** — columnas nuevas en `Product` (`SupplierPrice`, `PriceSetAt`, `PriceSetBy`, `MarginAlert`), `PricingSettings` (seed 30 / 15 / 50), `PriceChangeLog`, y `OrderItem.SupplierPriceSnapshot`. Con el backfill `SupplierPrice = Price` y `MarginAlert = true`.
2. **`AddWarehouseInventory`** — `Warehouse`, `StockItem`, `StockMovement`, `OrderItem.WarehouseID`, `Order.FulfillmentWarehouseID`. Con el seed de la bodega `CAL-01` y el backfill de `Product.Stock` → `StockItem`.

Las dos migraciones se entregan **generadas pero sin ejecutar**. Andrés las corre en este orden contra la BD remota de MonsterASP, y verifica con el SQL que entregue el agente:

```sql
-- tras AddPricing
SELECT COUNT(*) FROM Products WHERE SupplierPrice = 0;   -- debe dar 0

-- tras AddWarehouseInventory
SELECT p.ProductID, p.Stock, SUM(s.QuantityOnHand) AS EnBodegas
FROM Products p LEFT JOIN StockItems s ON s.ProductID = p.ProductID
GROUP BY p.ProductID, p.Stock
HAVING p.Stock <> ISNULL(SUM(s.QuantityOnHand), 0);      -- debe dar 0 filas
```

La UI de inventario (tanda 3B) va **después**, en un agente aparte, porque ya no toca migraciones.

**Secuencia final acordada:** Agente 1 (permisos) → Agente fusionado (E2 + E1 núcleo) → Agente 3B (UI de inventario) + Agente 2 (multi-tenant) en paralelo → Agente 5 (E3) → Agente 6 (E4).
