# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

El usuario principal del Home es el consumidor final en Colombia que busca descubrir y comprar cafe de especialidad colombiano con trazabilidad. Como audiencias secundarias, la plataforma atiende pequenos negocios compradores y proveedores locales que quieren vender sus lotes a traves de Bean.

## Product Purpose

The Kng Bean / Coffee Global Hub consolida cafe de especialidad colombiano de distintas marcas y fincas en una sola tienda. El Home debe permitir entender la oferta, descubrir lotes disponibles y avanzar a la compra con confianza. En esta etapa, la conversion principal es la compra B2C en Colombia.

## Positioning

Bean funciona como un hub B2B2C que combina catalogo unificado, trazabilidad del lote, operacion de bodega y venta directa. Los proveedores pueden participar mediante compra directa o consignacion sin operar por su cuenta el canal de venta y exportacion.

## Operating Context

Los visitantes exploran cafe por lote y origen, consultan su detalle, agregan productos al carrito y completan el checkout. El catalogo visible depende del pais seleccionado y de la disponibilidad real de cada producto. Proveedores y administradores gestionan productos, aprobaciones, pricing, inventario y pedidos desde paneles separados.

## Capabilities and Constraints

- El Home usa datos reales de `Product`, `Provider`, disponibilidad por pais y carrito.
- Todo el storefront, acceso, cuenta, onboarding y panel administrativo usan el sistema Tailwind compartido.
- Los shells son `_LayoutHome`, `_LayoutAuthTailwind` y `_LayoutAdminTailwind`; no se cargan Bootstrap ni CSS legacy.
- No se modificaran controladores, modelos, rutas ni reglas de negocio como parte del rediseño.
- Las metricas actuales de 14 fincas, calificacion 4.9 y 48 horas se consideran contenido aprobado para el Home.
- La aplicacion es ASP.NET Core MVC con Razor, EF Core, SQL Server e Identity.
- Todas las rutas activas usan Tailwind CSS 4.3.3; CSS manual queda limitado a normalizacion o integraciones que Tailwind no pueda expresar.
- Todos los elementos interactivos deben tener estados de teclado, tacto y `cursor-pointer` cuando corresponda.

## Brand Commitments

- Nombre visible: Bean.
- La paleta existente de cafe oscuro, crema y terracota debe mantenerse como base reconocible.
- El lenguaje debe ser profesional, directo y centrado en origen, frescura y trazabilidad.
- La identidad puede redisenarse completamente sin cambiar la paleta base ni el funcionamiento del producto.

## Evidence on Hand

- Catalogo, precios, proveedores, origenes, procesos, calificaciones, notas e imagenes se cargan desde datos reales cuando existen.
- El sistema visual anterior permanece como referencia histórica en `Docs/design_handoff_bean_redesign/`; su CSS runtime fue retirado.
- No hay fotografias estaticas propias versionadas en `wwwroot`; las imagenes reales de producto provienen de las URLs almacenadas en el catalogo.
- No se deben inventar testimonios, certificaciones ni nuevas metricas comerciales.

## Product Principles

- Hacer que descubrir y comprar cafe sea la ruta dominante.
- Mostrar procedencia y producto real antes que afirmaciones genericas.
- Mantener la operacion compleja del hub fuera del camino del comprador.
- Conservar la continuidad funcional mientras las superficies se migran por etapas.
- Tratar accesibilidad y responsive como requisitos de entrega.

## Accessibility & Inclusion

La experiencia debe funcionar con teclado, conservar foco visible, respetar contraste legible, ofrecer objetivos tactiles adecuados y adaptarse desde movil hasta escritorio.
