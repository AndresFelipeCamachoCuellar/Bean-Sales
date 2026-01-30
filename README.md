# Plataforma de Ventas Bean - Sistema Internacional de Comercio de Café

## 🌟 Resumen
La **Plataforma de Ventas Bean** es una aplicación web robusta y **multi-tenant** (multi-inquilino) diseñada para facilitar el comercio internacional de café. Conecta a **Proveedores** (Productores/Exportadores) con **Compradores** (Clientes Internacionales) a través de un entorno regulado y gestionado por **Administradores**.

El sistema cuenta con un ciclo de vida completo para la incorporación de productos, garantizando el cumplimiento de calidad y logística antes de que los productos lleguen al catálogo público.

---

## 🏗 Arquitectura y Tecnologías
El proyecto está construido utilizando tecnologías modernas de .NET y las mejores prácticas de la industria:

- **Framework**: ASP.NET Core MVC (.NET 8/9/10).
- **ORM (Base de Datos)**: Entity Framework Core (SQL Server).
- **Autenticación**: ASP.NET Core Identity (Soporta Sesión y Persistencia).
- **Frontend**: Vistas Razor, Bootstrap 5, FontAwesome, Vanilla JS.
- **Conceptos Clave**:
    - **Multi-tenancy**: Aislamiento entre la Administración Global y la de cada Proveedor.
    - **RBAC**: Seguridad granular basada en permisos o Roles (atributos `HasPermission`).
    - **Carrito Híbrido**: Fusiona los carritos de sesión anónima con los de la base de datos al iniciar sesión.

---

## 🚀 Características Clave

### 1. Multi-Tenancy y Gestión de Proveedores
- **Auto-Registro**: Las empresas pueden registrarse (`/Account/RegisterProvider`).
- **Flujo de Aprobación**: Los Administradores aprueban o rechazan nuevas empresas proveedoras.
- **Gestión Aislada**: Los Administradores de Proveedores *solo* pueden gestionar sus propios usuarios, roles y productos.

### 2. Flujo de Incorporación de Productos
Una estricta auditoría asegura el control de calidad:
1.  **Borrador**: El Proveedor crea el producto.
2.  **Pendiente de Aprobación**: Enviado al Administrador de la Plataforma.
3.  **Aprobado/Rechazado**: El Admin revisa el contenido.
4.  **Enviado (Shipped)**: El Proveedor confirma la logística.
5.  **Activo**: El Admin recibe el inventario -> El producto se vuelve Visible en el Catálogo.

### 3. Catálogo Público y Ventas
- **Visibilidad por País**: Los productos solo se muestran si están disponibles en el país del usuario.
- **Carrito de Compras**:
    - **Invitado**: Artículos guardados en Sesión (temporal).
    - **Usuario**: Artículos guardados en Base de Datos (permanente).
    - **Fusión Inteligente**: Los artículos de invitado se transfieren automáticamente a la cuenta del usuario al iniciar sesión.
- **Protección de Checkout**: Fuerza el registro/inicio de sesión antes de pagar sin perder los datos del carrito.

---

## 🛠 Guía de Instalación y Configuración

Sigue estos pasos para ejecutar la aplicación localmente.

### Prerrequisitos
- [.NET SDK](https://dotnet.microsoft.com/download) (Versión reciente)
- SQL Server (LocalDB, Docker o Estándar)

### 1. Clonar el Repositorio
```bash
git clone <url-del-repositorio>
cd Bean-Sales/Web
```

### 2. Configuración de Base de Datos
Asegúrate de que tu `appsettings.json` apunte a una instancia válida de SQL Server. La configuración por defecto suele ser suficiente para desarrollo local:
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=BeanSalesDB;Trusted_Connection=True;MultipleActiveResultSets=true"
}
```

### 3. Aplicar Migraciones y Datos Semilla (Seed)
Este comando crea la base de datos y la puebla con datos iniciales opcionales (Roles, Países, Usuario Admin).

> **Abre tu terminal en la carpeta `Web` y ejecuta:**

```powershell
# Restaurar dependencias
dotnet restore

# Actualizar Base de Datos (Aplica todas las migraciones pendientes)
dotnet ef database update
```

### 4. Ejecutar la Aplicación
```powershell
dotnet run
```
Accede a la aplicación en: `https://localhost:7193` (o el puerto que indique la consola).

---

## 🔐 Credenciales por Defecto (Seed)

El sistema crea automáticamente un usuario **SuperAdmin** si no existe. Usa estas credenciales para controlar toda la plataforma:

| Rol | Correo | Contraseña |
|------|-------|----------|
| **SuperAdmin** | `andres.felipe.camacho@outlook.com` | `Andipipe1*` |

---

## 🧪 Cómo Probar el Flujo
1.  **Iniciar como SuperAdmin**: Verifica el Panel de Control y "Gestionar Proveedores".
2.  **Registrar un Proveedor**: Ve a Login -> "Registrar Empresa". Crea una nueva Compañía.
3.  **Aprobar Proveedor**: Inicia como SuperAdmin -> aprueba al nuevo Proveedor.
4.  **Flujo del Proveedor**: Inicia con el nuevo Usuario Proveedor -> Crea un Producto -> Envíalo para Aprobación.
5.  **Flujo de Ventas**: Abre una ventana de Incógnito -> Navega el Catálogo como Invitado -> Agrega al Carrito -> Proceder al Pago -> Regístrate como Cliente.

---

## 📂 Estructura del Proyecto
- `Controllers/`: Controladores MVC que manejan la lógica de las peticiones.
- `Models/`: Entidades del Dominio.
- `Data/Seeds/`: Lógica para la población inicial de datos (`ContextSeed.cs`).
- `Views/`: Páginas Razor.
- `Constants/`: Definiciones estáticas para Roles, Permisos, Módulos.