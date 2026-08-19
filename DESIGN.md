---
name: "Bean"
description: "Cafe colombiano trazable, lote por lote, presentado como un patio de origen vivo."
colors:
  ink: "#1d1510"
  roast: "#2a1d16"
  clay: "#b4552e"
  clay-dark: "#8f4021"
  clay-light: "#d98a5f"
  cream: "#fbf7f1"
  oat: "#f0e7d8"
  husk: "#d7c6b2"
  leaf: "#3f6a4a"
  honey: "#c9962f"
  white: "#ffffff"
typography:
  display:
    fontFamily: "Archivo Black, Arial Narrow, sans-serif"
    fontSize: "clamp(3.4rem, 8vw, 7.5rem)"
    fontWeight: 400
    lineHeight: 0.86
    letterSpacing: "-0.055em"
  headline:
    fontFamily: "Archivo Black, Arial Narrow, sans-serif"
    fontSize: "2.25rem"
    fontWeight: 400
    lineHeight: 0.95
    letterSpacing: "-0.045em"
  title:
    fontFamily: "Manrope, Segoe UI, sans-serif"
    fontSize: "1.25rem"
    fontWeight: 800
    lineHeight: 1.25
    letterSpacing: "-0.03em"
  body:
    fontFamily: "Manrope, Segoe UI, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.75
    letterSpacing: "normal"
  label:
    fontFamily: "Azeret Mono, Consolas, monospace"
    fontSize: "0.625rem"
    fontWeight: 600
    letterSpacing: "0.16em"
  action:
    fontFamily: "Manrope, Segoe UI, sans-serif"
    fontSize: "0.875rem"
    fontWeight: 800
    lineHeight: 1.25
rounded:
  square: "0px"
  pill: "9999px"
  brand-drop: "65% 65% 65% 20%"
spacing:
  compact: "0.5rem"
  cluster: "0.75rem"
  control: "1rem"
  content: "1.5rem"
  gutter-wide: "2.5rem"
  section: "5rem"
  section-wide: "8rem"
components:
  button-primary:
    backgroundColor: "{colors.clay}"
    textColor: "{colors.white}"
    typography: "{typography.action}"
    rounded: "{rounded.square}"
    padding: "1rem 1.75rem"
    height: "3.25rem"
  button-primary-hover:
    backgroundColor: "{colors.clay-light}"
    textColor: "{colors.white}"
  button-dark:
    backgroundColor: "{colors.ink}"
    textColor: "{colors.white}"
    typography: "{typography.action}"
    rounded: "{rounded.square}"
    padding: "0.75rem 1rem"
    height: "2.75rem"
  button-dark-hover:
    backgroundColor: "{colors.clay}"
    textColor: "{colors.white}"
  button-outline:
    backgroundColor: "transparent"
    textColor: "{colors.white}"
    typography: "{typography.action}"
    rounded: "{rounded.square}"
    padding: "1rem 1.75rem"
    height: "3.25rem"
  chip-tasting:
    backgroundColor: "{colors.cream}"
    textColor: "{colors.ink}"
    rounded: "{rounded.pill}"
    padding: "0.375rem 0.75rem"
  label-lot:
    backgroundColor: "{colors.cream}"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.square}"
    padding: "0.5rem 0.75rem"
  card-lot:
    backgroundColor: "{colors.white}"
    textColor: "{colors.ink}"
    rounded: "{rounded.square}"
    padding: "1.5rem"
  selector-country:
    textColor: "{colors.white}"
    typography: "{typography.action}"
    rounded: "{rounded.pill}"
    padding: "0.5rem 0.75rem"
    height: "2.75rem"
---

# Design System: Bean

## Overview

**Creative North Star: "Patio de origen"**

Bean convierte el catalogo en un patio de secado vivo: una reticula de bandejas, lotes identificados y datos de origen donde el producto real domina sobre la decoracion comercial. Cafe tinta, crema y terracota producen una atmosfera material y colombiana; la composicion combina grandes bloques tipograficos con modulos densos, bordes finos y fotografias de lote.

El sistema es directo, trazable y orientado a compra. La voz principal hace una promesa breve, la voz de datos registra origen y proceso, y cada superficie conduce del contexto del cafe a una accion concreta. Este lenguaje gobierna todo el producto: tienda, acceso, cuenta, onboarding y panel operativo comparten los mismos tokens y primitives Tailwind.

**Key Characteristics:**
- Contraste alto entre cafe tinta y crema calida.
- Reticulas modulares inspiradas en bandejas de secado.
- Titulares grotescos compactos frente a datos monoespaciados.
- Lotes reales, origen y proceso antes que afirmaciones genericas.
- Geometria rectangular con redondeo reservado para controles compactos.

## Colors

La paleta mezcla cafe casi negro, papeles calidos y una arcilla terracota que concentra accion e identidad.

### Primary
- **Arcilla de marca:** Acciones principales, bandas de compromiso, marca y senales de seleccion.
- **Arcilla profunda:** Fondos de datos, placeholders de producto y texto de acento sobre superficies claras.
- **Arcilla al sol:** Acento legible sobre fondos oscuros y estado hover de la accion principal.

### Secondary
- **Hoja de cafetal:** Indicador puntual de disponibilidad; nunca compite con la arcilla como accion.

### Tertiary
- **Miel de cereza:** Calificacion y evidencia de calidad en cantidades pequenas.

### Neutral
- **Cafe tinta:** Fondo dominante de navegacion, hero y footer; tambien accion de compra sobre blanco.
- **Cafe tostado:** Segunda profundidad oscura para mesas modulares y menus moviles.
- **Crema:** Lienzo principal del catalogo, chips y superficies de contraste calido.
- **Avena:** Bordes y separadores sobre superficies claras.
- **Cascarilla:** Texto secundario sobre cafe y soporte neutro para imagenes ausentes.
- **Blanco:** Superficies de producto y texto de maximo contraste.

### Named Rules
**The Clay Signal Rule.** La arcilla marca accion, seleccion o una franja editorial deliberada; no reemplaza indiscriminadamente los fondos tinta, crema o blanco.

## Typography

**Display Font:** Archivo Black (con Arial Narrow y sans-serif)
**Body Font:** Manrope (con Segoe UI y sans-serif)
**Label/Mono Font:** Azeret Mono (con Consolas y monospace)

**Character:** Archivo Black comprime la promesa y da al cafe una presencia frontal; Manrope mantiene la compra clara y contemporanea. Azeret Mono convierte lote, origen, proceso, precio y disponibilidad en informacion registrada, no en adorno.

### Hierarchy
- **Display** (400, `clamp(3.4rem, 8vw, 7.5rem)`, 0.86): Promesa del hero; se usa una vez por superficie y admite saltos de linea deliberados.
- **Headline** (400, 2.25rem movil hasta 3.75rem escritorio, 0.95): Titulos de seccion y mensajes editoriales de alto impacto.
- **Title** (800, 1.25rem, 1.25): Nombre de lote y titulos de modulos; compacto y de lectura inmediata.
- **Body** (400, 1rem, 1.75): Explicacion y trazabilidad; normalmente limitada entre 32rem y 42rem.
- **Label** (600, 0.625rem, 0.16em, mayusculas): Codigos de lote, origen, proceso, disponibilidad y encabezados de navegacion secundaria.
- **Action** (800, 0.875rem, 1.25): Botones y enlaces de conversion.

### Named Rules
**The Three Voices Rule.** Archivo Black promete, Manrope explica y acciona, Azeret Mono registra datos; no intercambies sus responsabilidades.

## Layout

El lienzo se centra en un contenedor maximo de 90rem. Los gutters horizontales progresan de 1rem en movil a 1.5rem desde 640px y 2.5rem desde 1024px. En escritorio, la estructura principal usa doce columnas: el hero reparte siete para promesa y cinco para el lote destacado; las secciones posteriores reutilizan repartos 5/7 y 8/4.

El catalogo avanza de una columna a dos desde 768px y tres desde 1280px. En movil, el contenido tipografico precede a la mesa de producto, la navegacion se contrae a un menu y los compromisos forman una cuadricula 2x2. El ritmo base combina 0.75rem entre elementos compactos, 1.5rem dentro de modulos y 5rem a 8rem entre secciones; la densidad nace de bordes compartidos, no de reducir objetivos tactiles.

## Elevation & Depth

La profundidad es tonal y estructural antes que flotante. Tinta, tostado y arcilla profunda separan planos; bordes de baja opacidad construyen las bandejas. Las sombras se reservan al lote hero y a superficies realmente superpuestas, mientras las fotografias responden con una ampliacion lenta y contenida.

### Shadow Vocabulary
- **Tray** (`0 24px 70px -34px rgba(29, 21, 16, 0.5)`): Separa el lote protagonista de la mesa tostada.
- **Lift** (`0 28px 70px -28px rgba(29, 21, 16, 0.35)`): Eleva menus, selectores y el enlace de salto cuando aparecen sobre el flujo.

### Named Rules
**The Tray Before Shadow Rule.** Construye profundidad con tono, borde y reticula; usa sombra solo cuando una bandeja o capa realmente se eleva.

## Shapes

Las tarjetas, botones de conversion, etiquetas y paneles son rectangulares y de esquinas cuadradas. Las pildoras se reservan para selector de pais, cuenta, carrito, chips de sabor e indicadores compactos. La marca introduce la unica silueta organica recurrente: una gota/grano rotada con radios asimetricos, acompanada por un punto crema.

Los bordes son finos y funcionales: avena sobre blanco, tinta con baja opacidad sobre crema y blanco translucido sobre fondos oscuros. Las fotografias se recortan en proporciones fuertes, principalmente 4:5 para lote y una bandeja amplia en el hero.

## Components

### Buttons
- **Shape:** Bloques rectangulares sin radio, con altura minima de 2.75rem en tarjeta y 3.25rem en acciones principales.
- **Primary:** Arcilla con texto blanco, tipografia Action y relleno horizontal amplio; se usa para explorar o completar una accion destacada.
- **Hover / Focus:** El primario aclara a arcilla al sol; todos muestran anillo visible de 2px con contraste contextual y los CTA grandes separan el anillo 4px.
- **Secondary / Ghost / Tertiary:** El oscuro usa cafe tinta y vira a arcilla; el outline conserva el fondo y aumenta borde o velo blanco; el enlace editorial usa solo un subrayado inferior.

### Chips
- **Style:** Chips de cata en crema, texto tinta atenuado, forma de pildora y relleno compacto.
- **State:** Son metadata pasiva, no filtros; disponibilidad usa un punto hoja junto a una etiqueta monoespaciada.

### Cards / Containers
- **Corner Style:** Esquinas cuadradas.
- **Background:** Imagen o placeholder arcilla profunda arriba; cuerpo blanco abajo; pie de imagen tinta translucida para origen y proceso.
- **Shadow Strategy:** Las tarjetas de catalogo permanecen planas; solo la bandeja protagonista usa Tray.
- **Border:** Avena en laterales y base del cuerpo; los modulos oscuros usan blanco translucido.
- **Internal Padding:** 1.25rem en movil y 1.5rem desde pantallas pequenas.

### Navigation
- **Style:** Barra sticky tinta de 4.5rem en movil y 5rem en escritorio, contenida a 90rem. La marca combina gota arcilla y wordmark Archivo Black; los enlaces activos reciben una linea arcilla, no una pildora.
- **States:** Hover aclara texto o superficie; el foco siempre usa anillo visible. Pais, cuenta y carrito son controles de pildora con objetivo tactil minimo de 2.75rem.
- **Mobile:** Los enlaces y acciones pasan a un panel tostado alineado a la derecha; no se comprimen dentro de la barra.

### Lot Labels

Codigos de lote, origen y proceso usan Azeret Mono en mayusculas, tamanos de 9px a 10px y tracking amplio. Se montan como etiquetas cuadradas crema/tinta o como bandas tinta sobre fotografia para que la trazabilidad permanezca visible sin competir con el nombre del cafe.

## Do's and Don'ts

### Do:
- **Do** organiza producto, prueba y acciones como bandejas conectadas por bordes finos.
- **Do** deja que fotografia, nombre, productor, origen y proceso reales definan cada lote.
- **Do** conserva objetivos tactiles de al menos 2.75rem y foco visible de 2px.
- **Do** usa la voz monoespaciada solo para informacion corta y verificable.

### Don't:
- **Don't** conviertas el catalogo en una cuadricula de tarjetas genericas redondeadas y sombras uniformes.
- **Don't** uses arcilla, hoja o miel como color decorativo sin una funcion de accion, estado o evidencia.
- **Don't** sustituyas datos reales de lote por testimonios, certificaciones o metricas inventadas.
- **Don't** redondees botones y contenedores editoriales; las pildoras pertenecen a controles e indicadores compactos.
