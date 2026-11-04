# Verificación backend de comentarios

El contrato conserva un único texto opcional. POST admite `comment` omitido, null o vacío. PATCH `/api/work-items/{id}/comment` sustituye el texto en cualquier estado: vacío, null o propiedad omitida borra. Espacios y saltos se conservan. Devuelve 200 con la tarea persistida, 404 si desapareció, 400 ante JSON/tipo inválido. Solo cambia Comment; no hay detección de sobrescrituras entre comentarios simultáneos. La columna nullable `text` conserva las tareas anteriores. Down elimina exclusivamente esa columna y pierde los comentarios; no usar sobre datos reales para verificar.

## Entorno exclusivo

Requiere Docker local, SDK .NET 10 y Node. Los datos y credenciales siguientes son fixtures públicas locales, sin relación con servicios reales. Crear exclusivamente este contenedor, sin volúmenes compartidos:

```powershell
docker run --detach --name deliveryboard-comments-verification --publish 127.0.0.1:55436:5432 --env POSTGRES_USER=commenttest --env POSTGRES_PASSWORD=comment-local-only --env POSTGRES_DB=comments_test postgres:17
docker exec deliveryboard-comments-verification pg_isready -U commenttest -d comments_test
```

Desde la raíz, la prueba EF crea dos bases `comments_verify_<UUID>` nuevas, prueba migración desde cero y desde la migración de dependencias, y elimina exclusivamente esas bases en finally. La conexión está fijada al puerto local exclusivo; no cambie su destino a una base compartida.

```powershell
dotnet run --project scripts/verification/comments/Verification.csproj --configuration CommentsVerification
```

Incluye lectura en otro contexto, matriz de estados/textos, comprobación de UPDATE exclusivo de Comment, fila de control y snapshots precargados antes de intercalar cambios de comentario/responsable/estado/dependencias/prioridad. Comprueba que los tokens de Owner/Status siguen rechazando escrituras obsoletas. No afirma detección de conflictos entre dos comentarios.

## API aislada

En otra terminal, iniciar API con salida separada para evitar bloquear las DLL de una API preexistente. No detener procesos ajenos:

```powershell
$env:ConnectionStrings__deliveryboard = 'Host=127.0.0.1;Port=55436;Database=comments_test;Username=commenttest;Password=comment-local-only'
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/DeliveryBoard.Api --configuration CommentsVerification --no-launch-profile --urls http://127.0.0.1:15236
```

Cuando esté escuchando:

```powershell
node scripts/verification/comments/http-contract.js
$env:API_URL = 'http://127.0.0.1:15236'
node scripts/verification/priority/http-contract.js
node scripts/verification/dependencies/http-contract.js http://127.0.0.1:15236
```

El script de comentarios acepta solo origen HTTP loopback. Crea fixtures con UUID, elimina solo sus IDs en finally y verifica OpenAPI, POST/GET/PATCH, tres estados, borrado persistido, texto literal, 400, 404 tras eliminación, campos/dependencias/métricas intactos y rechazo de asignación a campos ajenos. No es una prueba de navegador. Los otros scripts verifican regresiones HTTP de prioridad/inicio/dependencias con fixtures propias.

Finalizar únicamente la API iniciada para esta comprobación con Ctrl+C y eliminar únicamente el contenedor creado arriba:

```powershell
docker rm --force deliveryboard-comments-verification
```

No eliminar volúmenes ni contenedores ajenos. Una ejecución fallida de restore/build no acredita pruebas; registrar resultados nuevos al ejecutar. La suite unitaria usa xUnit 2 y VSTest, sin servicios remotos ni PostgreSQL; TDD no está configurado explícitamente. No se instala tooling ni se modifica configuración global.

## Evidencia observada el 16/10/2026

- `dotnet build backend/DeliveryBoard.slnx --configuration Release`: código 0; 0 advertencias, 0 errores, incluida API/AppHost.
- `dotnet test backend/DeliveryBoard.slnx --configuration Release`: código 0; Passed: 114, Failed: 0, Skipped: 0. También pasó la suite Debug con el mismo conteo.
- `dotnet run --project scripts/verification/comments/Verification.csproj --configuration CommentsVerification`: código 0; PASS fresh y PASS legacy, con SQL capturado exclusivo de Comment y relecturas tras escrituras desde snapshots obsoletos.
- `node scripts/verification/comments/http-contract.js`: código 0; PASS 83 comprobaciones HTTP, contra la API exclusiva en 15236.
- `node scripts/verification/priority/http-contract.js` con API_URL en 15236: código 0; PASS de prioridad, inicio, acciones previas y errores 400/404/409/422.
- `node scripts/verification/dependencies/http-contract.js http://127.0.0.1:15236`: código 0; PASS 34 comprobaciones, incluida concurrencia y limpieza exclusiva de fixtures.
- Desde backend: `dotnet ef migrations has-pending-model-changes --project src/DeliveryBoard.Infrastructure --startup-project src/DeliveryBoard.Api --configuration CommentsVerification --no-build`: código 0; ningún cambio pendiente entre modelo y migración.
- `git diff --check`: código 0. PostgreSQL de API quedó con 0 filas; bases temporales eliminadas en finally. API y contenedor exclusivos se retiraron al cerrar.

Incidencias corregidas: el primer contrato HTTP detectó 500 donde debía devolver 400 ante un tipo JSON inválido en Development. GlobalExceptionHandler ahora conserva el 400 de binding con mensaje seguro; la repetición pasó. Build Debug de API no pudo copiar DLL bloqueadas por un proceso preexistente: se conservó y verificó en salida separada. La solución solo define Debug/Release; un intento de configuración propia de solución fue rechazado y se repitió correctamente en Release. Intentos sandbox de Docker/dotnet/HTTP no pudieron acceder al pipe o socket local; las mismas comprobaciones con acceso local autorizado pasaron. El primer comando de generación EF usó una ruta relativa incorrecta; se corrigió el directorio y la migración se generó mediante la herramienta local existente.

No se ejecutó el harness de persistencia histórico de prioridad/inicio que tiene otra base/configuración; la regresión aquí acreditada es HTTP sobre entorno exclusivo y la prueba PostgreSQL de comentarios/tokens. Frontend, navegador, recarga real y aceptación funcional quedan fuera de esta evidencia backend.
