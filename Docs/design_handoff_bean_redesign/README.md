# Handoff: Rediseño Bean — Plataforma de Ventas de Café

## Overview
Rediseño completo de la plataforma Bean Sales (ASP.NET Core MVC, carpeta `Bean-Sales/Web`): lado cliente (catálogo, detalle de producto, carrito, checkout) y back-office (dashboard admin, aprobación de productos, matriz de inventario Café × País, panel del proveedor). Objetivo: atraer más clientes y mejorar la experiencia con identidad de marca, storytelling de origen/trazabilidad, señales de confianza y compra con menos fricción.

## About the Design Files
Los archivos `.dc.html` de este paquete son **referencias de diseño creadas en HTML** — prototipos que muestran el look & feel e interacciones previstas, NO código de producción. La tarea es **recrear estos diseños en el entorno existente del proyecto**: vistas Razor (`.cshtml`) de ASP.NET Core MVC con su pipeline actual (reemplazando el Bootstrap por defecto con estilos propios, o sobreescribiendo variables de Bootstrap 5 si se prefiere conservarlo). Los archivos requieren un runtime propio (`support.js`) para previsualizarse; úsalos como referencia visual y de contenido.

## Fidelity
**High-fidelity (hifi).** Colores, tipografía, espaciados, radios y copys son finales. Recrear pixel-perfect usando los patrones del codebase.

## Dirección visual elegida ("4a — moderna animada")
- Base oscura café `#1d1510` para nav/hero/footer del lado cliente y sidebar del panel interno; contenido sobre crema `#FBF7F1`.
- Acento terracota en **gradiente** `linear-gradient(135deg, #d98a5f, #B4552E)` para CTAs, badge del carrito y elementos de marca.
- Botones y chips en **píldora** (border-radius 50px); tarjetas radius 14–18px.
- Motion sutil: fade-up escalonado al entrar, hover-lift en tarjetas, shimmer en el titular del hero, marquee de confianza, granos flotantes, punto pulsante en el selector de país.
- Imagen: patrón SVG de granos de café sobre tonos por origen (clase `.bean` en los archivos), como placeholder hasta tener fotografía real. Zonas de foto marcadas `[ foto ... ]`.

## Screens / Views

### Lado cliente (`Bean — Tienda Final.dc.html`)

**1. Catálogo / Home** (`Views/Home/Index.cshtml`)
- Nav oscura `#1d1510`: wordmark "Bean" (Bitter 800, 22px, blanco) con gota terracota rotada 45°; links (DM Sans 600 14px, `#b8a58f`, activo blanco con subrayado `#d98a5f`); selector de país en píldora `rgba(255,255,255,.08)` con punto pulsante `#B4552E`; toggle ES·EN; botón bolsa en gradiente terracota.
- Hero centrado (padding 66px 36px 56px): badge píldora "☕ Tueste de esta semana ya disponible" (`rgba(217,138,95,.14)`, borde `rgba(217,138,95,.35)`, texto `#e8a582`); titular Bitter 800 70px/-0.03em blanco, segunda línea con gradiente animado shimmer (`#e8a582→#d98a5f→#c9a878`); subtítulo DM Sans 17px `#c9b8a4` máx 480px; 2 CTAs píldora (primario gradiente + sombra `0 10px 30px rgba(180,85,46,.4)`, secundario borde `rgba(255,255,255,.25)`); fila de stats (14 fincas / 4.9★ / 48 h) separadas por líneas verticales.
- 3 granos decorativos flotando (`border-radius:50% 50% 50% 0`, animación floaty 4.4–6.5s, delays escalonados).
- Marquee infinito 22s con señales: "Trazabilidad finca a taza ✦ Comercio justo ✦ Tueste semanal ✦ Envío Colombia · Canadá ✦ Reseñas verificadas" (uppercase, letter-spacing .16em, `#a08b73`). Duplicar el contenido para el loop.
- Grid de productos 3 columnas gap 22px sobre `#FBF7F1`: encabezado "Lotes disponibles" (Bitter 700 28px) + chips filtro píldora (Todos activo `#241a12`; Lavado/Honey/Natural blancos, hover borde/texto `#B4552E`).
- Tarjeta de producto: imagen 150px (patrón bean por tono de origen) con badge proceso (`rgba(42,32,25,.85)`) y rating (`#fff`, estrella `#C9962F`); cuerpo padding 16px: origen uppercase 11px `#B4552E`, nombre Bitter 700 19px `#241a12`, proveedor+altura 12.5px `#8a7761`, chips de notas de cata (píldora `#F6EFE7`, borde `#EEE3D6`, texto `#7A6552` 11px), fila precio (Bitter 700 19px + "/ 340 g") y botón "Agregar" gradiente píldora (hover scale 1.06). Hover de tarjeta: translateY(-8px) + sombra `0 24px 50px rgba(60,35,15,.18)`, transición .35s. Entrada: fadeUp .6s con delay incremental 0.08s por tarjeta.
- Banner B2B: gradiente `#2b1c13→#1d1510`, radius 20px, eyebrow `#e8a582`, titular Bitter 800 29px, CTA "Registrar mi empresa →" gradiente.
- Footer `#17100b`, 4 columnas, texto `#b8a58f` 13px.

**2. Detalle de producto** (`Views/Home/Details.cshtml`)
- Grid 2 columnas (1fr / 1.05fr, gap 44px) sobre `#FBF7F1`, breadcrumb arriba.
- Izquierda: imagen cuadrada radius 20px con badge stock "● En stock · 120 bolsas" (`#4A7A54`) y badge lote; 3 thumbnails 74px (activo borde 2px `#B4552E`, resto hover scale 1.05).
- Derecha: eyebrow origen+altura; título Bitter 800 38px; fila rating ★ `#C9962F` + reseñas + finca `#4A7A54`; descripción 15.5px `#5c4a3a`; chips de notas con emoji (🌸 Jazmín, 🍑 Durazno, 🍊 Bergamota).
- **Tarjeta de trazabilidad** (blanca, borde `#EAE3D8`, radius 16px): grid 2 col con pares Finca/Altura/Proceso/Variedad/Lote/Tostado, separadores `#F0E7D8`; "Tostado: Hace 4 días" en verde `#4A7A54`.
- Compra: precio Bitter 800 30px + "COP / bolsa 340 g"; stepper cantidad en píldora (− gris / + terracota, mín 1); botón principal "🛍 Agregar al carrito · $<total>" gradiente píldora con **precio total vivo** (cantidad × $62.000, formato es-CO). Debajo: "✓ Envío a Colombia en 3–5 días · ✓ Pago directo al productor".

**3. Carrito** (`Views/Cart/Index.cshtml`)
- Título "Tu carrito · 3 bolsas · 2 lotes" (Bitter 800 30px). Grid 1.55fr/0.95fr gap 28px.
- Lista: tarjeta blanca radius 18px; cada ítem: thumb 64px, nombre Bitter 700 17px, origen+peso 12.5px, stepper píldora, subtotal Bitter 700, icono eliminar 🗑 `#c98a8a`. Fila "← Seguir comprando" en terracota.
- Resumen: Subtotal $172.000 / Envío a Colombia $18.000 / Impuestos incluidos / Total Bitter 800 20px `#8F4021` + "COP"; CTA gradiente "Proceder al pago →" (hover scale 1.02); línea "🔒 Pago seguro · Visa · Mastercard · PayPal · PSE".

**4. Checkout** (`Views/Cart/Checkout.cshtml`)
- Stepper 1 Envío → 2 Pago → 3 Confirmación (activos con círculo gradiente, inactivo `#e6ddce`).
- Formulario: labels 12px `#7A7161` 600; inputs blancos borde `#EAE3D8` radius 12px padding 11px 13px; grid Nombre/Apellido, Dirección completa, País/Departamento/Código postal. País: **solo Colombia y Canadá**.
- Métodos de pago (radio cards radius 14px): Tarjeta (seleccionada: borde `#B4552E` + sombra suave), PayPal, PSE — hover borde `#d8a88f`.
- Columna derecha "Tu orden": mini-ítems con cantidad, totales, CTA "Pagar $190.000 COP" gradiente, nota "🔒 Transacción cifrada · Devolución garantizada".

**5. Confirmación de pedido + seguimiento** (nueva vista)
- Cabecera centrada: círculo verde gradiente `#5f9a6e→#3f7a50` con ✓, titular Bitter 800 32px "¡Gracias, <nombre>! Tu café va en camino.", número de pedido + correo de comprobante.
- Izquierda: **línea de tiempo de envío** (4 hitos: Confirmado ✓ verde / Tostado y empacado ✓ verde / En camino — punto activo borde terracota / Entregado — futuro gris) con conectores verticales de color según progreso; callout `#F6EFE7` con fecha estimada y número de guía.
- Derecha: resumen del pedido (mini-ítems + total `#8F4021`, tarjeta y dirección) y card oscura "Mientras llega" con enlace a la historia de la finca.

**6. Mis pedidos (cuenta del cliente)** (nueva vista)
- Layout cuenta: sidebar claro 220px (Mis pedidos activo con fondo `#F6E9E1`/texto `#8F4021`, Direcciones, Métodos de pago, Mis reseñas, Cerrar sesión).
- Filtros píldora: Todos / En camino / Entregados.
- Fila de pedido: thumbs solapados (2 imágenes 44px margin-left -14px), id + fecha + nº ítems, total Bitter, badge estado (En camino `#F6E9E1`/`#8F4021`; Entregado `#E8F2EA`/`#3f7a50`), acción outline terracota "Seguir envío" / "Volver a pedir" (hover: fondo terracota, texto blanco).

**7. Acceso / Registro** (`Views/Account/Login.cshtml` + registro de cliente nuevo)
- Split: panel izquierdo oscuro con patrón bean, wordmark y mensaje "Tu café te está esperando"; derecho con formulario.
- Toggle píldora Entrar / Crear cuenta (activo blanco con sombra sobre track `#F0E9DD`).
- Inputs radius 12px (focus borde `#B4552E`); enlace "¿Olvidaste tu contraseña?" terracota; CTA gradiente píldora.
- Divisor "¿Compras para tu negocio?" + card "Registra tu empresa" (🏪, hover borde terracota) que lleva al onboarding B2B.

**8. Estados vacíos** (componentes)
- Patrón común: círculo 58px `#F6E9E1` con icono, título Bitter 800 20px, texto 13.5px `#7A7161`, acción.
- Carrito vacío → CTA gradiente "Explorar catálogo"; sin resultados de búsqueda → chips de sugerencias; lote sin envío al país seleccionado → CTA outline "Avisarme cuando llegue".

### Back-office (`Bean — Panel Interno.dc.html`)

Layout común: sidebar 230px `#1d1510` (wordmark + badge de rol ADMIN terracota / PROVEEDOR verde; ítem activo con fondo gradiente translúcido `rgba(217,138,95,.22)` radius 10px; badge contador `#B4552E`; usuario abajo con avatar circular en gradiente) + contenido sobre `#FBF7F1` padding 26px 30px.

**9. Dashboard admin**
- Header: saludo Bitter 800 24px + fecha; búsqueda píldora + campana con punto pulsante.
- 4 KPI cards (blancas radius 16px, fadeUp escalonado): label 12px, valor Bitter 800 26px, tendencia ▲ verde `#3f7a50` / ▼ rojo `#b04a4a`.
- Gráfica "Ventas por semana": barras flex, S1–S8, altura % (42–96%), color `#EAD9C4` y semana actual en gradiente terracota; toggle COP/CAD; fila de totales bajo la gráfica.
- Card "Por aprobar": 3 ítems con thumb + badge "Pendiente" (`#FBF3E4`/`#9a6b1f`); callout `#F6EFE7` con antigüedad de la cola.
- "Actividad reciente": filas con icono circular tintado por tipo (aprobación verde, pedido terracota, alerta ámbar, usuario azul).

**10. Aprobaciones**
- Tabs contador: Pendientes · 7 (activo oscuro) / Aprobados · 24 / Rechazados · 3.
- Layout maestro-detalle: cola izquierda (tarjetas con thumb, proveedor, antigüedad, badge; seleccionada con borde `#B4552E`) y panel derecho con: imagen 130px, datos del lote (grid: Precio COL $62.000 COP, Precio CAN $21,50 CAD, stock, peso, altura, lote), campo de comentario para el proveedor y 2 acciones: "✓ Aprobar y publicar" (gradiente verde `#5f9a6e→#3f7a50`, píldora) y "✕ Rechazar" (outline `#c98a8a`, texto `#a44949`).

**11. Matriz Café × País**
- Tabla: header oscuro `#241a12` con columnas CAFÉ / 🇨🇴 COLOMBIA · COP / 🇨🇦 CANADÁ · CAD; filas cebradas `#FDFBF7`.
- Celda por país: precio (bold) + estado de stock con punto de color — verde `#3f7a50` sano, ámbar `#c9962f` bajo (<20), rojo `#b04a4a` agotado, gris `#c4b9a5` no asignado ("— No asignado / Asignar precio y stock"); icono ✎ para edición por celda. Leyenda de colores debajo.
- CTA "+ Asignar café a país" gradiente píldora.

**12. Productos del proveedor**
- 3 stats (Ventas del mes, Bolsas vendidas, Valoración media).
- Lista de lotes: grid por fila (thumb 52px / nombre+meta / Precio COL / Stock con color de alerta / badge estado / Editar). Estados: "Pendiente aprobación" ámbar, "Publicado" verde, "Borrador" gris.
- CTA "+ Publicar nuevo lote"; callout 💡 con sugerencia accionable.
- Sidebar del proveedor: Resumen / Mis productos / Pedidos entrantes / Pagos / Mi finca; pie "Finca La Victoria · Proveedor verificado ✓".

**13. Pedidos entrantes (proveedor)** (nueva vista)
- Tabs contador: Por preparar · 3 (activo) / Listos · 1 / Completados · 12.
- Fila de pedido: avatar tipo comprador (👤 cliente `#F6E9E1` / 🏪 empresa `#EDEDF6`), id + comprador + destino + antigüedad, contenido, **"Tu pago"** en verde `#3f7a50` (Bitter 700), badge estado, CTA por estado: "Marcar listo" (gradiente) o "Ver guía" (outline). Primera fila resaltada con borde `#B4552E`.
- Callout 🚚 con horario de recogida de la transportadora.

**14. Onboarding de empresa** (nueva vista, sustituye `RegisterProvider.cshtml`)
- Split: panel izquierdo oscuro con stepper vertical de 3 pasos (Datos de la empresa ✓ verde / Tipo de cuenta activo terracota / Verificación futuro) + testimonio en card translúcida.
- Derecho: "Paso 2 de 3", pregunta "¿Cómo usará Bean tu empresa?", 2 radio-cards grandes: **Comprar al por mayor** (🏪, seleccionada borde 2px `#B4552E` + sombra) y **Vender mi café** (🌱, hover borde `#d8a88f`). Botones ← Atrás (outline) y Continuar → (gradiente).
- Verificación de documentos: 1 día hábil (copy comprometido en la UI).

**15. Notificaciones** (componente panel desplegable, todos los roles)
- Panel 420px radius 18px sombra `0 24px 60px rgba(60,35,15,.22)`: header con "Marcar todas leídas"; filas con icono circular tintado por tipo (aprobado ✓ verde, pedido ▣ terracota, rechazo ✕ rojo `#a44949`/bg `#F9E9E9`, alerta ⚠ ámbar, pago $ verde), texto 13.5px, timestamp, punto no-leído `#B4552E` y fondo `#FDF9F2` para no leídas; pie "Ver historial completo".
- Eventos por rol — Proveedor: lote aprobado/rechazado (con comentario), pedido entrante, pago liberado, stock bajo. Admin: lote por aprobar, empresa por verificar, stock agotado por país, pedido de alto valor. Cliente: confirmado/enviado/entregado, favorito de vuelta en stock, tueste nuevo de finca seguida.
- Canales: campana in-app + correo transaccional; eventos críticos (rechazo, pago) siempre por ambos.

## Interactions & Behavior
- **Animaciones** (usar exactamente):
  - `fadeUp`: opacity 0→1 + translateY(24px→0), .5–.7s ease, delays escalonados +0.07–0.08s por elemento.
  - Hover tarjeta producto: `translateY(-8px)` + sombra, transición .35s ease.
  - Botones primarios hover: `scale(1.02–1.06)`, .25s ease.
  - `shimmer` titular: background-position 200%→-200%, 4s linear infinite (gradient text con background-clip).
  - `marquee`: translateX(0→-50%), 22s linear infinite, contenido duplicado.
  - `floaty` granos: translateY 0→-14px + rotación 45°→52°, 4.4–6.5s ease-in-out infinite.
  - `pulseDot`: box-shadow 0→10px rgba(180,85,46,.45→0), 2.4s infinite.
  - Respetar `prefers-reduced-motion` en producción.
- Selector de país (Colombia/Canadá) visible en nav; al cambiar recalcula moneda, disponibilidad y costo de envío.
- Detalle: stepper actualiza el total del botón en vivo (formato `toLocaleString('es-CO')`).
- Interfaz bilingüe ES/EN (toggle en nav); los textos actuales son ES.
- Aprobaciones: seleccionar ítem de la cola carga el detalle; aprobar publica en los países solicitados; rechazar requiere/permite comentario.

## State Management
- Carrito: ítems {productId, qty}, subtotal, envío por país, total; persistir por sesión (ya existe `ShoppingCartItem`).
- País seleccionado: sesión/cookie; determina CurrencyCode y precios (modelo `Country` con ExchangeRate existente).
- Cantidad en detalle: estado local (mín 1, máx 99).
- Aprobaciones: estados Pendiente / Aprobado / Rechazado (+ Borrador en proveedor); ítem seleccionado en maestro-detalle.
- Filtros de catálogo por proceso (Todos/Lavado/Honey/Natural).

## Design Tokens
**Colores**
- Fondo oscuro marca: `#1d1510` (nav/hero/sidebar), footer `#17100b`, tabla header/alt `#241a12`
- Fondo claro contenido: `#FBF7F1`; crema chips: `#F6EFE7`; bordes: `#EAE3D8`, separadores `#F0E7D8` / `#F6EFE7`
- Terracota: `#B4552E`; gradiente CTA: `linear-gradient(135deg,#d98a5f,#B4552E)`; terracota oscuro (totales): `#8F4021`; claro (eyebrows sobre oscuro): `#e8a582`
- Tinta: `#241a12` / `#2A2019`; secundario: `#7A7161`, `#8a7761`; sobre oscuro: `#efe0cd`, `#c9b8a4`, `#b8a58f`, `#a08b73`
- Éxito: `#3f7a50` (badge bg `#E8F2EA`); alerta: `#c9962f` / `#9a6b1f` (bg `#FBF3E4`); error: `#b04a4a` / `#a44949` (borde `#c98a8a`); estrella rating: `#C9962F`; neutro no-asignado: `#c4b9a5`
- Tonos de origen (imágenes placeholder): `#c9a878`, `#b98a5e`, `#a9743f`, `#8f5a34`, `#c2925a`, `#a67c52`
**Tipografía**
- Titulares: **Bitter** (Google Fonts) 700/800; letter-spacing -0.01 a -0.03em en tamaños grandes
- UI/cuerpo: **DM Sans** 400/500/600/700
- Escala: hero 70px / h1 páginas 24–30px / card título 17–19px / cuerpo 13–15.5px / meta 11–12.5px / eyebrows 11–12px uppercase +0.12–0.22em
**Radios**: píldoras 50px; tarjetas 14–18px; inputs 12px; thumbs 8–12px
**Sombras**: CTA `0 10px 30px rgba(180,85,46,.4)`; hover card `0 24px 50px rgba(60,35,15,.18)`; frame `0 18px 50px rgba(60,35,15,.18)`
**Espaciado**: contenedor 1280px; padding secciones 36–44px; gaps de grid 14–28px

## Assets
- **Capturas de las 15 vistas** en `screenshots/` (01–15, mismo orden que la sección Screens/Views) — referencia visual directa para implementar sin abrir los HTML.
- Sin imágenes finales. Placeholder: patrón SVG inline de granos (ver clase `.bean` en los archivos — data-URI, trazo negro 13% de opacidad sobre color de fondo por origen). Reemplazar por fotografía real de producto/finca cuando exista.
- Logo: gota terracota (`border-radius:50% 50% 50% 0` rotada 45°) + wordmark "Bean" en Bitter 800. Sugerencia: producir SVG definitivo.
- Fuentes: Google Fonts — Bitter y DM Sans.

## Files
- `screenshots/01-catalogo.png` … `15-notificaciones.png` — capturas de cada vista.
- `Bean — Tienda Final.dc.html` — lado cliente: catálogo, detalle, carrito, checkout (dirección final).
- `Bean — Panel Interno.dc.html` — back-office: dashboard, aprobaciones, matriz Café × País, productos del proveedor.
- `Bean - Rediseño Tienda.dc.html` — exploraciones previas (opciones 4a/3a/2a + versión base) — solo contexto histórico.
- `Rediseño Bean Sales.dc.html` — auditoría del estado actual + 3 direcciones iniciales — solo contexto.
- `support.js` — runtime de previsualización de los `.dc.html` (no portar).

## Cobertura
Las 15 vistas cubren el flujo completo: descubrimiento → compra → confirmación/seguimiento → recompra (cliente) y publicación → aprobación → venta → pago (proveedor/admin), más acceso, onboarding B2B, notificaciones y estados vacíos. Única pieza no diseñada explícitamente: página de resultados de búsqueda con filtros avanzados (usar el grid del catálogo + chips; su estado vacío ya está diseñado).
