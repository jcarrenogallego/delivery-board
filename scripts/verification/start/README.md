# Verificación de concurrencia PostgreSQL

Harness local de verificación, fuera de la solución y sin paquetes nuevos. Referencia la infraestructura del proyecto y el runtime ASP.NET instalado. Usa los casos/repositorio/DI reales con scopes independientes y controla el orden: ambos contextos leen Pending antes de cualquier escritura. Solo elimina los IDs que acaba de crear con el título `VERIFICACION CX inicio 20261009`; no vacía tablas ni toca tareas preexistentes.

Inicia Aspire como indica el README del repositorio. Identifica **únicamente** el contenedor PostgreSQL de Delivery Board y su puerto local publicado; no uses contenedores de otros proyectos. Desde la raíz, sustituye nombre y puerto cuando la sesión de Aspire cambie:

```powershell
dotnet run --project scripts/verification/start/Verification.csproj -- postgres-sxhrxtbh 54015
```

El harness obtiene internamente POSTGRES_USER/POSTGRES_PASSWORD de ese contenedor local explícito, sin imprimirlos. Requiere acceso al motor Docker del proyecto y PostgreSQL iniciado con la base `deliveryboard` y migraciones existentes aplicadas. La API iniciada por Aspire aplica dichas migraciones. El script no crea infraestructura ni cambia el esquema.

Salida observada el 2026-10-09 (exit 0):

```text
CX-1 PASS (two real contexts loaded before either save; second conflicts)
CX-2 start first PASS (two real contexts loaded before either save; second conflicts)
CX-2 complete first PASS (two real contexts loaded before either save; second conflicts)
CX-3 start first PASS (two real contexts loaded before either save; second conflicts)
CX-3 owner first PASS (two real contexts loaded before either save; second conflicts)
CX-4 PASS (loaded Pending, deleted in second context, stale save conflicts; fresh read null)
```

Verifica la traducción del repositorio a WorkItemConflictException y las lecturas persistidas, no la capa HTTP. El plan registra por separado la comprobación HTTP de 409 mediante el handler existente.

Para repetir el contrato HTTP sobre los servicios locales ya iniciados:

```powershell
node scripts/verification/start/http-contract.js
```

La URL opcional debe ser local. Crea dos tareas propias y limpia únicamente sus IDs al terminar. Comprueba snapshots, GET persistido, no-op/dashboard intacto, Completed→409 ProblemDetails, ausencia→404 vacío, completar directamente Pending y completar InProgress/repetición.
