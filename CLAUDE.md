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
- **⚠️ Alertas abiertas:**
  - El "rediseño" de Andrés está **LOCAL SIN COMMITEAR** — recordarle hacer commit/push a `dev`.
  - Hay 4 logs de build borrados sin commitear en el working tree.
  - **No hay tests automatizados.**
- **Bloqueantes para vender de verdad:** no existe modelo `Order`/`OrderItem`; la compra no se persiste ni descuenta stock; pago es placeholder.

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
- Credenciales SuperAdmin de la app y detalles de instalación: ver `README.md`.
