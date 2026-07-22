# Bean-Sales — Diagnóstico de Proyecto y Roadmap
**Fecha:** 8 de julio de 2026
**Elaborado por:** Claude (Project Manager) · **PO:** Andrés

---

## 1. Resumen Ejecutivo

Bean-Sales es una startup en etapa de idea/validación (sin empresa constituida, sin proveedores ni clientes reales todavía) para la que ya existe una base de código funcional considerable: seguridad, gestión de usuarios/roles, catálogo de productos con flujo de aprobación, y un carrito de compras híbrido. El desarrollo lo lleva Andrés en solitario, sin fecha límite externa.

El hallazgo más importante de este diagnóstico es que **el modelo de negocio que se está construyendo en código no coincide con el modelo de negocio que Andrés tiene en mente**. Antes de seguir escribiendo features, esto necesita resolverse — construir más encima de un modelo equivocado es la forma más cara de generar desorden.

---

## 2. Estado Técnico Actual

Repo: `Bean-Sales`, rama `dev`, 6 commits desde la inicialización. Stack: ASP.NET Core MVC, EF Core, SQL Server, Identity, Bootstrap.

**Fase 1 — Seguridad y Usuarios: completa.**
Identity extendido (`ApplicationUser`), roles y permisos granulares vía atributo `HasPermission`, CRUD de usuarios/roles, seed de usuario SuperAdmin.

**Fase 2 — Catálogo de Productos: completa.**
Modelo `Product` con workflow de estados (Borrador → Pendiente de Aprobación → Aprobado/Rechazado → Enviado → Activo), `Provider`, `ProductCountry` (disponibilidad/stock por país), controlador de aprobación de productos.

**Fase 3 — Ventas: parcial.**
Carrito híbrido funcionando (sesión para invitado, BD para usuario autenticado, con fusión automática al iniciar sesión). El checkout llega hasta una vista de resumen — **no existe todavía un modelo `Order`/`OrderItem`**, por lo que ninguna compra se persiste ni descuenta stock realmente. Esto es infraestructura, no lógica de negocio: falta cerrarlo pero es sencillo dado lo ya construido.

**Housekeeping pendiente:**
- Hay 4 archivos de log de build (`build_log_cmd.txt` y similares) borrados en el working tree pero sin commitear — hay que decidir si se comitea esa limpieza.
- No hay tests automatizados en ningún módulo.
- Los permisos (`Permissions.cs`) son genéricos (Create/Read/Update/Delete), no hay permisos específicos por módulo de negocio.

---

## 3. Hallazgo Crítico: Marketplace vs. Compra-Reventa

El código actual implementa un modelo **multi-tenant tipo marketplace**:
- Proveedores se auto-registran (`RegisterProvider`).
- Cada proveedor tiene su propio portal: gestiona sus propios usuarios (`CompanyUsersController`), sus propios roles (`CompanyRolesController`), y sube sus propios productos, que pasan por un flujo de aprobación del Admin antes de publicarse.
- El diseño asume que el proveedor es un tercero independiente que opera dentro de la plataforma.

Lo que Andrés describe como modelo de negocio real es distinto: **Bean-Sales compra el café directamente a los proveedores y hace el envío por su cuenta** — es decir, un modelo de **compra-reventa / distribución**, donde Bean-Sales es el único vendedor de cara al cliente final, y los "proveedores" son simplemente proveedores/suppliers internos (como en cualquier negocio de importación), no usuarios activos de la plataforma con su propio portal.

**Por qué importa:** si el modelo real es compra-reventa, gran parte de la Fase 1 y 2 —portal de proveedores, self-registro, roles y permisos por compañía, flujo de aprobación proveedor→admin— es funcionalidad para un modelo de negocio distinto al que se quiere ejecutar. No necesariamente se tira a la basura (puede reconvertirse en un panel interno de "gestión de proveedores" que solo usa el Admin), pero **el alcance y la UX cambian**: no habrá cuentas de proveedor externas, ni portal para que ellos suban productos — el equipo interno de Bean-Sales cargará el inventario que compró.

**Esto requiere una decisión de producto antes de seguir codeando.**

---

## 4. Áreas de negocio sin definir (fuera del código)

Como startup en validación, esto es lo que falta trabajar en paralelo al código:

- **Legal:** ninguna entidad constituida. Para comprar café a proveedores (probablemente internacionales) y revenderlo, hay que entender: constitución de empresa, permisos de importación/exportación de café, regulaciones sanitarias/aduaneras por país destino.
- **Operaciones/Logística:** si Bean-Sales hace el envío, hace falta definir: cómo se compra (a quién, mínimos, contratos), cómo se transporta (courier, freight, tiempos), dónde se almacena el inventario antes de vender, quién maneja la logística día a día (¿Andrés solo?).
- **Finanzas:** modelo de compra-reventa implica capital de trabajo (comprar inventario antes de vender) — hay que definir cuánto capital se necesita, márgenes objetivo, manejo de múltiples monedas (el diseño ya contempla `ExchangeRate` por país), y medios de pago reales para checkout (hoy es un placeholder).
- **Mercado/GTM:** ¿qué países se atacan primero?, ¿quién es el cliente (consumidor final, tostadoras, cafeterías)?, ¿cómo se consigue el primer proveedor real y el primer cliente real?

Ninguna de estas está resuelta todavía y son igual de urgentes que el código para que esto deje de ser "una idea que surgió sin llevarse a cabo".

---

## 5. Backlog Priorizado

**Prioridad 0 — Resolver antes de seguir tocando código:**
1. Decisión de modelo de negocio: confirmar compra-reventa y qué implica para el "portal de proveedores" ya construido (¿se convierte en panel interno? ¿se descarta el self-registro?).
2. Redefinir el flujo de "alta de producto" según el modelo confirmado (¿quién carga inventario: el proveedor o el equipo de Bean-Sales?).

**Prioridad 1 — Estabilización técnica (una vez confirmado el modelo):**
3. Ajustar/recortar código de Fase 1-2 al modelo real (roles, portal de proveedor).
4. Completar el ciclo de venta: modelo `Order`/`OrderItem`, persistencia de la compra, descuento real de stock.
5. Limpiar working tree (decidir sobre los archivos de log borrados) y ordenar la estrategia de ramas (`dev`/`master`).
6. Tests automatizados mínimos sobre los flujos críticos (checkout, aprobación de producto, permisos).

**Prioridad 2 — Negocio en paralelo:**
7. Definir estructura legal necesaria para operar (constitución, importación de café).
8. Mapear la cadena de suministro real: primer proveedor candidato, costos, logística de envío.
9. Definir modelo financiero: capital de trabajo, márgenes, monedas, medios de pago reales.
10. Definir mercado inicial (países, tipo de cliente) y estrategia de primeros clientes/proveedores.

---

## 6. Preguntas abiertas para la próxima sesión

- ¿Confirmamos el pivot de "portal de proveedores" a "panel interno de gestión de proveedores"?
- ¿Cuál es el primer país/mercado a atacar?
- ¿Existe ya un proveedor de café con el que se ha hablado, aunque sea informalmente?
- ¿Cuánto capital estás dispuesto/puedes destinar a comprar el primer inventario?
