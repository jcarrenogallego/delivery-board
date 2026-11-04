# Verificación de prioridad

Scripts reutilizables, externos a la solución principal. La evidencia de navegador es histórica: repetirla sobre la aplicación actual antes de atribuir resultados.

## PostgreSQL aislado
Program.cs exige PostgreSQL de pruebas en 127.0.0.1:55432, base deliveryboard, usuario prioritytest y contraseña priority-test-local-only. No apuntarlo a datos reales: aplica migraciones y crea/elimina una base temporal con prefijo priority_legacy_ para comprobar la migración desde NULL.

Con ese entorno aislado ya preparado, desde la raíz:
    dotnet run --project scripts/verification/priority/Verification.csproj

No crea ni elimina contenedores. Conserva los IDs de las fixtures y elimina solo sus propios datos.

## API local
API_URL debe apuntar al servicio de verificación local, por defecto http://127.0.0.1:15202:
    $env:API_URL = "http://127.0.0.1:15202"
    node scripts/verification/priority/http-contract.js

## Navegador
browser-contract.mjs exporta checkPriorityBrowserContract(tab) para un tab CUA del frontend aislado en puerto 13000. Solo modifica fixtures propias y retira intercepciones. Lee browser-verification.md para los escenarios históricos y el método de invocación. No se instala ninguna librería ni framework de metodología.