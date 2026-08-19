# The Kng Bean / Coffee Global Hub — Plan de Trabajo
**Fecha:** 21 de julio de 2026 · **PM:** Claude · **PO:** Andrés
**Reemplaza el diagnóstico del 8-jul-2026** (`project_status_and_roadmap.md`), corregido tras revisar las entregas de Google Drive.

> **Actualización UI (19-ago-2026):** la migración visual completa a Tailwind 4.3 y su validación técnica desktop/móvil están documentadas en `Docs/plan_migracion_ui_tailwind_2026-08.md`. La dirección normativa es `Patio de origen` y sus tokens/componentes están en `DESIGN.md`; queda la aceptación visual final del PO y los E2E externos indicados en ese plan.

---

## 1. Resumen ejecutivo

The Kng Bean es un **Hub B2B2C de consolidación y exportación de café de especialidad colombiano**: compra/consigna café a marcas y fincas locales, lo centraliza en una bodega en Colombia y lo vende a consumidores y pequeños negocios en el exterior (EE.UU., Canadá, Europa) con una experiencia de compra unificada — *"el supermercado global del café colombiano"*.

El hallazgo central de esta revisión corrige mi diagnóstico anterior: **el código NO estaba construido sobre un modelo equivocado.** Las entregas en Drive (`Coffee Global Hub`) confirman un **modelo híbrido** que encaja con lo ya programado:

- **Compra directa (retail arbitrage):** Bean compra el lote por adelantado y lo revende. Margen 30-60%.
- **Consignación (incubadora):** para marcas nuevas, el proveedor mantiene la propiedad hasta que se vende y paga una comisión del 20-30%.

Es este segundo esquema el que justifica el **portal de proveedores, el flujo de aprobación de productos y los roles por compañía** que ya están en el código. No hay que descartarlos: hay que completarlos.

---

## 2. Reconciliación de los tres modelos que convivían

| Fuente | Modelo que describía | Estado |
|---|---|---|
| Trello "Fase 1" (jun 2025) | Plataforma de **subastas** (solo intermediación, sin pagos ni logística) | **Descartado.** Cards de subasta archivadas. |
| Código (ene 2026) | E-commerce/marketplace con carrito, checkout y aprobación de productos | **Vigente y alineado** al Hub. |
| Drive "Coffee Global Hub" (feb 2026) | **Hub híbrido** compra directa + consignación, con exportación | **Modelo oficial confirmado.** |

El pivote de "subastas" a "Hub" es la razón de que el Trello estuviera desactualizado. Ya quedó corregido.

---

## 3. Estado real por área

### 3.1 Producto / Código — *avanzado*
**Hecho:** ASP.NET Core MVC + EF Core + Identity. Seguridad con roles y permisos granulares (Fase 1). Catálogo de productos con workflow de estados y aprobación, `Provider`, `ProductCountry` con stock por país (Fase 2). Carrito híbrido invitado/usuario con fusión al login (Fase 3, parcial).

**Pendiente (bloqueantes para vender de verdad):** modelo `Order`/`OrderItem` + persistencia de compra + descuento de stock; pasarelas de pago reales (Wompi/PSE en COP, Stripe/PayPal en USD); detección de IP → país/moneda; cálculo de envío en tiempo real; completar el panel de proveedor (envío a bodega, inventario, pagos); filtro de admisión de proveedor (RUT, Cámara, Invima, FNC + muestra física); consolidación de envíos.

**Riesgo de proceso:** el "rediseño" está **local sin commitear** — el último commit del repo es del 30-ene-2026. Hay que commitear/pushear para no perder el trabajo y poder versionarlo. No hay tests automatizados.

### 3.2 Legal — *sin iniciar*
Estructura de dos empresas: **SAS en Colombia** (compra, calidad, empaque, exportación) + **Corporation en el país destino** (importación, venta, distribución). En Colombia: RUT/DIAN, registro de exportador, certificado de origen, agente aduanero. En Canadá (mercado piloto propuesto): licencia SFC/CFIA, Business Number, GST/HST, etiquetado bilingüe EN/FR.

### 3.3 Operaciones / Logística — *sin iniciar*
Bodega/fulfillment center en Colombia (recepción, validación, almacenamiento por SKU, picking & packing de exportación). Alianzas con couriers (DHL/FedEx) para D2C express y carga aérea B2B. Broker aduanero. 3PL en destino si aplica.

### 3.4 Finanzas — *sin definir*
El modelo de compra directa exige **capital de trabajo** para comprar inventario antes de vender. Definir monto de capital, márgenes objetivo (costo estimado ~USD 26-28/kg vs. precio de venta internacional), flujo de caja, y medios de pago multimoneda.

### 3.5 Go-to-Market — *sin definir*
Confirmar primer mercado (Canadá vs. EE.UU./Europa), perfil de cliente (B2C coffee geeks/expatriados; B2B cafeterías/tostadores), primer proveedor real (que pase el filtro de muestra) y primeros clientes. Existen cards de validación (entrevistas) aún abiertas.

---

## 4. Roadmap priorizado

**P0 — Decisiones de PO (esta semana):**
1. Confirmar mercado piloto (¿Canadá?).
2. Confirmar cuánto capital de trabajo hay disponible para el primer inventario.
3. Confirmar si el primer lanzamiento arranca con compra directa, consignación, o ambos.

**P1 — Cierre técnico para tener app vendible (Fase 2 en Trello):**
4. Commitear el rediseño + limpiar working tree y ramas.
5. `Order`/`OrderItem` + persistencia + descuento de stock.
6. Pasarelas de pago reales + detección de país/moneda.
7. Cálculo de envío en tiempo real + consolidación de envíos.
8. Completar panel de proveedor + filtro de admisión.
9. Tests de flujos críticos.

**P2 — Puesta en marcha del negocio (en paralelo):**
10. Constituir SAS Colombia + registro exportador.
11. Estructura e importación en mercado destino (licencias, etiquetado).
12. Definir bodega + alianzas de logística.
13. Modelo financiero + medios de pago.
14. Conseguir primer proveedor y primeros clientes.

---

## 5. Cómo administro esto (cadencia PM)

- **Tablero:** Trello "The kng bean Fase 1", reorganizado en listas: `✅ Logros`, `🔧 Cierre Técnico – Fase 2`, `🌐 Negocio (Legal/Ops/Fin/GTM)`, más las listas de validación/marca/recursos existentes.
- **Ritmo sugerido:** revisión semanal de estado (puedo generarla automáticamente), y actualización del tablero a medida que avanzamos.
- **Mi rol:** te haré preguntas de PO constantemente para no avanzar sobre supuestos; priorizo, documento decisiones y mantengo el tablero como fuente de verdad.

---

## 6. Preguntas abiertas para la próxima sesión

1. ¿Confirmamos Canadá como primer mercado, o evaluamos EE.UU./Europa primero?
2. ¿Cuánto capital puedes destinar al primer inventario?
3. ¿El primer lanzamiento es compra directa, consignación, o híbrido?
4. ¿Ya hay un proveedor candidato con quien se haya hablado?
5. ¿Quieres que configure un reporte de estado semanal automático del tablero?
