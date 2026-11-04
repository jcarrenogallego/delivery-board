# Verificación MCP con PostgreSQL aislado

Este ejecutable comprueba aprovisionamiento, lectura y restricciones de PostgreSQL, límites, cancelación, timeout y el protocolo MCP mediante el cliente oficial. Necesita un servidor MCP ya iniciado contra la misma base aislada. Nunca usa la configuración de la aplicación ni omite escenarios si falta configuración.

Configure explícitamente estas variables de entorno antes de ejecutar:

- `MCP_VERIFICATION_ADMIN_CONNECTION`: conexión administrativa de una base descartable cuyo nombre contenga `mcp_verify`.
- `MCP_VERIFICATION_READER_CONNECTION`: misma base, host y puerto, con usuario `mcp_reader` y contraseña de prueba.
- `MCP_VERIFICATION_URL`: URL loopback completa del endpoint MCP, por ejemplo `http://localhost:5091/mcp`.

El MCP debe recibir `ConnectionStrings__deliveryboard` con la conexión del lector. La API puede aprovisionar previamente el rol usando `McpReader__Password`; el ejecutable también verifica directamente la reentrada del aprovisionador.

```powershell
dotnet run --project scripts/verification/mcp/Mcp.Verification.csproj
```

El ejecutable crea un esquema con nombre aleatorio, vuelve a aprovisionar el lector y elimina exclusivamente ese esquema al terminar. Comprueba rechazo de roles ajenos e inseguros alterando temporalmente el comentario y un permiso del rol de esta base aislada, y restaura ambos en bloques `finally`. El rol del lector permanece en la base aislada para permitir repetir la verificación. No elimina bases, contenedores ni volúmenes. Una ejecución completa tarda al menos 60 segundos porque observa los timeouts reales tanto del acceso PostgreSQL como del protocolo MCP.

La salida final informa `Passed`, `Failed` y `Skipped`; una excepción produce un código de salida distinto de cero. Los fallos nunca se convierten en omisiones.

## Verificación observada el 21 de octubre de 2026

- Solución completa: compilación correcta, cero advertencias y cero errores.
- Suite xUnit/VSTest: **Passed: 149, Failed: 0, Skipped: 0**.
- Este ejecutable, PostgreSQL 17 aislado y cliente oficial por HTTP: **Passed: 7, Failed: 0, Skipped: 0**.
- Aspire 13.5.2 con PostgreSQL 18.3 y volumen exclusivo: API y MCP saludables, frontend HTTP 200, descubrimiento de exactamente dos tools y lectura real del lector.
- Reinicio completo de Aspire con el mismo volumen: la tool volvió a leer la fixture persistente con valor 42; aprovisionamiento idempotente correcto.

Aspire emitió un aviso de conexión al socket de logs DCP en Windows. El arranque,
los health checks y las llamadas MCP funcionaron; no se verificó la captura
completa de logs del dashboard. Un intento preliminar de VSTest en sandbox se
anuló por timeout de conexión con testhost; la ejecución completa posterior pasó
con acceso a los procesos locales de .NET. Estas observaciones son históricas y
deben repetirse después de modificar el código.
