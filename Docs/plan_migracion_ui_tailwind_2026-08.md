# Plan de migración UI a Tailwind 4.3

**Aprobado por PO:** 8 de agosto de 2026  
**Dirección visual:** `Patio de origen`  
**Fuente normativa:** `DESIGN.md`

## Reglas de ejecución

- Migrar una ruta completa por incremento.
- Priorizar el recorrido de compra del cliente.
- Todas las rutas activas usan Tailwind; Bootstrap y los CSS legacy ya fueron retirados del runtime.
- Los `_ViewStart` asignan el shell Tailwind correcto a tienda, acceso y panel.
- No cambiar contratos funcionales, nombres de campos, antiforgery, permisos, AJAX o integraciones durante una migración visual.
- jQuery permanece solo donde ASP.NET Validation Unobtrusive lo requiere; Cloudinary y la interacción propia usan JavaScript nativo.

## Estado

- **Migración de código completada:** las 49 rutas del plan, estados `AccessDenied`/`NoProvider`/403/404 y los tres shells usan Tailwind 4.3.3.
- **Validación técnica visual completada (19-ago-2026):** recorridos públicos, cuenta y panel revisados en escritorio (1440 px) y móvil (360 px), sin overflow de página, Bootstrap en runtime ni errores JavaScript.
- **Pendiente:** aceptación visual final del PO. No se ejecutaron el cobro Wompi, una subida/eliminación real en Cloudinary ni una sesión con rol proveedor para evitar transacciones externas o cambios de datos sin credenciales de prueba dedicadas.
- Fundación Tailwind cerrada y verificada.
  - Bundle general `Web/Styles/bean-tailwind.css` → `Web/wwwroot/css/bean-tailwind.css`.
  - CI ejecuta `npm ci` y `npm run css:build` antes de publicar.
  - Navegación centralizada en `StoreNavigationViewComponent`.
  - Permisos del shell consolidados en una consulta con `PermissionService.GetAllowedModulesAsync`.
  - Footer extraído a `_StoreFooterTailwind.cshtml`.
  - Selector de país Tailwind con banderas locales.
  - Caché de países de 10 minutos aceptada: el catálogo no tiene flujo de edición, solo seed.
  - Shells: `_LayoutHome`, `_LayoutAuthTailwind` y `_LayoutAdminTailwind`.
  - Gestor Cloudinary migrado de jQuery a `fetch`/XHR nativo.
  - Layouts y CSS legacy eliminados; ninguna vista activa carga Bootstrap o Font Awesome.
  - Verificación: detector sin hallazgos, `npm run css:build`, `node --check`, tests con exit 0 y `dotnet build` con 0 errores y 0 warnings.
  - Navegador real: catálogo, detalle, carrito, login/fusión, checkout con ciudades y cotización Mipaquete, formularios con Validation Unobtrusive, diálogo de cancelación, inventario responsive, gestor Cloudinary y sidebar móvil.
  - Correcciones de cierre: el checkout prioriza “Elige tu ciudad” sobre el estado técnico inválido y el cargador Cloudinary ya no expone controles interactivos anidados.

## Orden aprobado

### Fase 1 — Compra del cliente

1. `Home/Details`
2. `Cart/Index`
3. `Account/Login`
4. `Account/Register`
5. `Cart/Checkout`
6. `Cart/PayRedirect`
7. `Cart/Confirmation`
8. `Orders/Index`

### Fase 2 — Público y onboarding

9. `Home/Origenes`
10. `Home/Nosotros`
11. `Home/Privacy`
12. `Account/RegisterProvider`
13. `Account/RegistrationSuccess`
14. `Shared/Error`

También deben crearse `AccessDenied`, `NoProvider`, 403 y 404; `Lockout` solo si se habilita el bloqueo.

### Fase 3 — Shell administrativo y pedidos

15. `Dashboard/Index`
16. `OrderManagement/Index`
17. `OrderManagement/Details`

### Fase 4 — Bodegas e inventario

18. `Warehouses/Index`
19. `Warehouses/Create`
20. `Warehouses/Edit`
21. `Inventory/Index`
22. `Inventory/Details`

### Fase 5 — Pricing

23. `Pricing/Index`
24. `Pricing/History`
25. `Pricing/Settings`

### Fase 6 — Productos, Cloudinary y aprobación

26. `ProductImages/Manage`
27. `Products/Index`
28. `Products/Edit`
29. `Products/Create`
30. `SupplierOrders/Index`
31. `ProductApproval/Index`
32. `ProductApproval/Receive`

### Fase 7 — Empresas, usuarios y permisos

33. `Providers/Index`
34. `CompanyProfile/Index`
35. `CompanyProfile/Edit`
36. `CompanyUsers/Index`
37. `CompanyUsers/Create`
38. `CompanyUsers/Edit`
39. `CompanyRoles/Index`
40. `CompanyRoles/Create`
41. `CompanyRoles/Edit`
42. `CompanyRoles/ManagePermissions`
43. `Users/Index`
44. `Users/Create`
45. `Users/Edit`
46. `Roles/Index`
47. `Roles/Create`
48. `Roles/Edit`
49. `Roles/ManagePermissions`

## Criterio de aceptación por ruta

- Paridad funcional con la vista legacy.
- Responsive en móvil, tablet y escritorio.
- Navegación por teclado, foco visible y objetivos táctiles adecuados.
- Estados vacío, error, validación y contenido largo.
- Formularios, nombres, antiforgery, permisos y scoping preservados.
- Sin Bootstrap CSS ni errores JavaScript en la ruta migrada.
- `npm run css:build`, detector Impeccable y `dotnet build` correctos.
- Validación visual del PO antes de continuar.

## Integraciones de alto riesgo

- Checkout: ciudades, cotización, fallback y antiforgery.
- Wompi: formulario externo, nombres con `:`, firma y auto-submit.
- Cloudinary: ticket, subida directa, progreso, registro, portada, orden, alt y eliminación.
- Inventario: filtros, ajustes, libro mayor y concurrencia.
- Permisos: nombres indexados de `ManagePermissions` y aislamiento multi-tenant.
