# Despliegue continuo a MonsterASP.NET (GitHub Actions + Web Deploy)

Guía accionable para poner **The Kng Bean** en producción en **MonsterASP.NET** con
despliegue automático desde GitHub. La app es ASP.NET Core MVC **.NET 10** con EF Core
y SQL Server.

- **Método de deploy:** GitHub Actions -> Web Deploy (MSDeploy), según la
  [documentación oficial de MonsterASP](https://help.monsterasp.net/books/github/page/how-to-deploy-website-via-github-actions).
- **Workflow:** `.github/workflows/deploy.yml` (ya está en el repo).
- **Disparo:** cada `push` a la rama `master` (y manualmente desde la pestaña *Actions*).
- **.NET 10:** [soportado por MonsterASP](https://www.monsterasp.net/.NET10-Hosting/)
  (soportan .NET 10/9/8, MSSQL 2025 y HTTPS gratis con Let's Encrypt).

---

## Resumen del flujo automático

Cada vez que se hace push a `master`, GitHub Actions:

1. Clona el repo (`actions/checkout`).
2. Instala el SDK de **.NET 10** (`actions/setup-dotnet` con `10.0.x`).
3. `dotnet restore Web/Web.csproj`.
4. `dotnet publish Web/Web.csproj -c Release -o ./publish`.
5. Sube el contenido de `./publish` al hosting por **Web Deploy** con la action
   `rasmusbuchholdt/simply-web-deploy`.

Al **arrancar** en el host, la app ejecuta `await context.Database.MigrateAsync()`
(en `Web/Program.cs`) antes de sembrar datos, así el esquema de la base de datos se
**crea/actualiza solo** en el primer arranque y en cada deploy con migraciones nuevas.
No hace falta correr `dotnet ef` manualmente en producción.

---

## Pasos de una sola vez (los hace Andrés)

Estos pasos **no los puede hacer el agente**: requieren crear cuenta, entrar al panel
y configurar secretos en GitHub.

### 1. Crear la cuenta y el sitio en MonsterASP

1. Regístrate en https://www.monsterasp.net/ (hay plan gratuito para probar).
2. En el [Hosting Control Panel](https://admin.monsterasp.net/) crea un **Website**
   con la versión **.NET 10** (Hosting model: se recomienda *In-Process*, el default de IIS).
3. Crea una base de datos **MSSQL** (SQL Server) desde el panel. Anota:
   - Servidor / Data Source
   - Nombre de la base de datos
   - Usuario y contraseña
   El panel te muestra la **connection string** completa lista para copiar.

### 2. Activar WebDeploy y obtener credenciales

1. En el panel, en tu Website, **activa el servicio WebDeploy**.
2. Anota los datos que muestra (los necesitarás como secretos de GitHub):
   - **Website name**: `siteXXXX`
   - **Server computer name**: `https://siteXXXX.siteasp.net:8172`
   - **Username**: `siteXXXX`
   - **Password**: `********`

### 3. Cargar los secretos en GitHub (nombres EXACTOS)

En el repo de GitHub: **Settings -> Secrets and variables -> Actions -> New repository secret**.
Crea estos 4 secretos con **exactamente** estos nombres (así los lee el workflow):

| Secreto de GitHub       | Valor del panel MonsterASP        | Ejemplo                                |
| ----------------------- | --------------------------------- | -------------------------------------- |
| `MONSTERASP_WEBSITE`    | Website name                      | `site12345`                            |
| `MONSTERASP_SERVER`     | Server computer name (con :8172)  | `https://site12345.siteasp.net:8172`   |
| `MONSTERASP_USERNAME`   | Username de WebDeploy             | `site12345`                            |
| `MONSTERASP_PASSWORD`   | Password de WebDeploy             | (la contraseña)                        |

> No pongas estos valores en el código ni en el YAML. Solo como *Secrets*.

### 4. Configurar la connection string de producción en el panel

`Web/appsettings.json` tiene **solo** la cadena de LocalDB para desarrollo (sin
secretos). No la cambies ni commitees cadenas reales. En producción, la app lee
`ConnectionStrings:DefaultConnection`, que en el host se define por **fuera del código**.
Elige UNA de estas dos opciones:

- **Opción A (recomendada) — Panel IIS "Connection Strings":** en el Control Panel de
  MonsterASP, sección **Connection Strings**, agrega una entrada con nombre
  `DefaultConnection` y pega la cadena MSSQL que te dio el panel. IIS la inyecta y tiene
  prioridad sobre `appsettings.json`.
- **Opción B — Variable de entorno:** define `ConnectionStrings__DefaultConnection`
  (doble guion bajo) con la cadena MSSQL. (`appsettings.Production.json` también sirve,
  pero implicaría subir un archivo con la cadena real; evítalo salvo que lo excluyas del
  deploy — el workflow ya hace `skip-files: appsettings.Production.json` como salvaguarda).

La cadena MSSQL de MonsterASP se ve parecida a:

```
Server=dbXXXX.mssql.somee... ;Database=dbXXXX;User Id=dbXXXX;Password=****;TrustServerCertificate=True;MultipleActiveResultSets=true;
```

(usa exactamente la que te muestre tu panel).

---

## Cómo se dispara el deploy

- **Automático:** `git push` a `master`.

  ```bash
  git checkout master
  git merge dev        # o el flujo que uses para llevar cambios a master
  git push origin master
  ```

- **Manual:** GitHub -> pestaña **Actions** -> workflow "Build, publish and deploy to
  MonsterASP.NET" -> **Run workflow**.

- **Desplegar desde `dev`:** edita `.github/workflows/deploy.yml` y cambia `master` por
  `dev` (o agrega `dev`) en la sección `on: push: branches:`.

### Primer deploy

1. Asegúrate de haber commiteado los cambios locales pendientes (rediseño, `Order`/
   `OrderItem`, enriquecimiento de `Product`, etc.) y **las migraciones EF** generadas
   (`AddOrders`, `EnrichProduct`, ...). El host aplica las migraciones que existan en el
   repo; si no están commiteadas, la base no tendrá esas tablas/columnas.
2. Haz push a `master` (o corre el workflow manualmente).
3. Mira la ejecución en la pestaña **Actions**. Debe terminar en verde.

---

## Verificación post-deploy

1. Abre la URL del sitio (`https://siteXXXX.runasp.net/` o el dominio que asignes).
2. En el **primer arranque**, la app corre `MigrateAsync()` y el seeding:
   crea el esquema y siembra roles, permisos, países, tipos de documento y el
   **SuperAdmin**.
3. Entra a `/Identity/Account/Login` (o la ruta de login) y accede con las
   **credenciales de SuperAdmin** documentadas en `README.md`. Cambia la contraseña
   tras el primer ingreso.
4. Comprueba el storefront (Home/catálogo), y que la conexión a la base MSSQL funciona
   (no debe salir el error de "Connection string not found" ni error de SQL).

---

## Troubleshooting

- **Cold start (primer request lento):** en planes compartidos/gratuitos el app pool se
  suspende por inactividad; la primera petición tras un rato tarda unos segundos
  mientras arranca y corre migraciones/seed. Es normal.
- **El deploy falla en la action de Web Deploy:** revisa que los 4 secretos existan y
  sean correctos (sobre todo `MONSTERASP_SERVER` con `https://...:8172`). Verifica que
  el servicio **WebDeploy** esté activado en el panel.
- **`dotnet publish` o `restore` falla por versión de .NET:** confirma que el runner usa
  `10.0.x` (paso "Instalar .NET 10 SDK"). MonsterASP soporta .NET 10; el csproj apunta a
  `net10.0`.
- **Error de base de datos / esquema desactualizado:** confirma que las migraciones EF
  están **commiteadas** en el repo. `MigrateAsync()` aplica solo lo que exista en el
  ensamblado publicado. Si agregaste modelos sin migración, genera la migración en local
  (`dotnet ef migrations add ...`) y commitea antes del push.
- **"Connection string 'DefaultConnection' not found":** falta configurar la cadena de
  producción en el panel (paso 4). En dev, usa la de LocalDB de `appsettings.json`.
- **HTTPS:** activa el certificado gratuito **Let's Encrypt** desde el panel. El código
  ya hace `UseHttpsRedirection()` y `UseHsts()` fuera de Development.
- **El app pool tumba la app al migrar en cada arranque:** si en el futuro el seed/migración
  es pesado, considera moverlo a un paso puntual; hoy es liviano y seguro.

---

## Qué hace el agente vs. qué hace Andrés

**Ya hecho por el agente (en el repo, pendiente de commit):**
- `.github/workflows/deploy.yml` (workflow de CI/CD).
- `Web/Program.cs`: activada la migración automática (`MigrateAsync`) antes del seed.
- Esta guía.

**Pendiente de Andrés (no lo puede hacer el agente):**
1. Crear cuenta MonsterASP + Website .NET 10 + base MSSQL.
2. Activar WebDeploy y copiar sus credenciales.
3. Crear los 4 **GitHub Secrets** con los nombres exactos de la tabla.
4. Configurar la **connection string de producción** en el panel (paso 4).
5. Commitear/push (incluyendo migraciones EF) a `master` para el primer deploy y validar.

---

## Referencias

- MonsterASP — Deploy vía GitHub Actions:
  https://help.monsterasp.net/books/github/page/how-to-deploy-website-via-github-actions
- MonsterASP — .NET 10 Hosting:
  https://www.monsterasp.net/.NET10-Hosting/
- MonsterASP — modelo de hosting ASP.NET Core:
  https://help.monsterasp.net/books/websites/page/aspnet-core-hosting-model-support
- Action de deploy (Web Deploy):
  https://github.com/rasmusbuchholdt/simply-web-deploy
