# Git básico: guía práctica

> Guía comprobada el **3 de octubre de 2026**. Git es estable, pero algunos detalles cambian según el sistema. Consulta la [documentación oficial](https://git-scm.com/doc).

Git guarda versiones locales del proyecto. Puedes trabajar sin GitHub, GitLab ni conexión a Internet. Un repositorio remoto es un paso aparte.

## 1. Comprobar o iniciar un repositorio

```powershell
git --version
git status
```

Si `git status` dice que no es un repositorio, entra en la carpeta correcta y crea uno con rama principal `main`:

```powershell
git init -b main
git status
```

Haz este paso solo una vez. Si la carpeta ya contiene `.git`, ya es un repositorio.

## 2. Ver qué ha cambiado

```powershell
git status
git diff
git diff --staged
```

- `status`: resumen de archivos nuevos, modificados o preparados.
- `diff`: cambios todavía no preparados.
- `diff --staged`: cambios que entrarán en el próximo commit.

## 3. Crear y cambiar de rama

Crea una rama para un cambio concreto:

```powershell
git switch -c feature/nombre-corto
```

Ver ramas y cambiar entre ellas:

```powershell
git branch
git switch main
git switch feature/nombre-corto
```

Una rama permite trabajar sin mezclar inmediatamente el cambio con `main`.

## 4. Preparar archivos (`add`)

Preparar un archivo concreto:

```powershell
git add ruta/al/archivo
```

Preparar todos los cambios visibles desde la carpeta actual:

```powershell
git add .
git status
git diff --staged
```

`git add` no guarda todavía una versión: selecciona qué irá en el siguiente commit. Revisa `git diff --staged` antes de continuar.

Para retirar un archivo del área preparada sin borrar su contenido:

```powershell
git restore --staged ruta/al/archivo
```

## 5. Crear un commit

```powershell
git commit -m "docs: explica como ejecutar el proyecto"
```

Un commit es una fotografía con mensaje. Usa mensajes breves que expliquen el resultado, por ejemplo:

- `feat: permite reasignar una tarea`
- `fix: corrige la validacion del responsable`
- `docs: agrega instrucciones de uso`
- `test: cubre la eliminacion de tareas`

Comprueba el resultado:

```powershell
git log --oneline -5
```

## 6. Fusionar una rama

Cuando la rama esté comprobada:

```powershell
git switch main
git merge feature/nombre-corto
```

Si todo sale bien, Git incorpora sus commits en `main`. Después puedes borrar la rama local:

```powershell
git branch -d feature/nombre-corto
```

### Si aparece un conflicto

1. Ejecuta `git status` para ver los archivos.
2. Abre cada archivo y elige el contenido correcto entre las marcas `<<<<<<<`, `=======` y `>>>>>>>`.
3. Borra esas marcas.
4. Ejecuta las pruebas del proyecto.
5. Prepara y termina la fusión:

```powershell
git add ruta/al/archivo-resuelto
git commit
```

Si prefieres cancelar y volver al estado anterior:

```powershell
git merge --abort
```

No elijas “mi versión” o “su versión” a ciegas; ambas pueden contener partes necesarias.

## 7. Hooks locales versionados

Un hook es un pequeño script que Git ejecuta en momentos concretos, por ejemplo antes de crear un commit. Git busca hooks en `.git/hooks` por defecto, pero esa carpeta no se comparte normalmente. Para versionarlos con el proyecto se suele usar `.githooks/`.

Activar los hooks del proyecto:

```powershell
git config core.hooksPath .githooks
git config --get core.hooksPath
```

Desactivarlos y volver al comportamiento normal de Git:

```powershell
git config --unset core.hooksPath
```

En Linux o macOS, los scripts deben tener permiso de ejecución:

```bash
chmod +x .githooks/*
```

En Windows normalmente Git Bash interpreta los scripts. Comprueba que tengan finales de línea compatibles y que las herramientas llamadas por el hook estén en `PATH`.

Los hooks son código: léelos antes de activarlos. Un hook descargado puede ejecutar comandos con tus permisos. `git commit --no-verify` omite algunos hooks; úsalo solo si entiendes por qué y deja constancia cuando corresponda.

## Prompt sencillo para pedir hooks mínimos a una IA

Copia este texto y cambia los comandos de compilación y pruebas por los de tu proyecto:

```text
Analiza este repositorio sin modificar archivos todavía. Propón los hooks Git mínimos y fáciles de mantener para detectar errores antes de un commit. Quiero guardarlos en una carpeta versionada llamada .githooks y activarlos con `git config core.hooksPath .githooks`.

Antes de escribir, dime qué comprobaciones ya existen en el proyecto y qué comandos exactos usarías. No descargues herramientas, no añadas dependencias, no uses servicios externos, no leas secretos y no cambies la configuración global de Git. Mantén los scripts compatibles con el sistema operativo del proyecto, rápidos y con mensajes claros. Como mínimo, considera un `pre-commit` que compruebe formato o compilación y ejecute las pruebas rápidas existentes. No inventes comandos: si no puedes descubrirlos en el repositorio, pregúntame.

Cuando yo apruebe la propuesta, crea solo los archivos necesarios, explica cómo activarlos y desactivarlos, y ejecuta cada comprobación manualmente para verificar que funciona. No hagas commit, push ni merge.
```

Este prompt limita a la IA, pero tú debes revisar los scripts antes de activarlos.

## Flujo diario resumido

```powershell
git switch -c feature/mi-cambio
git status
# Editar archivos y ejecutar pruebas
git add .
git diff --staged
git commit -m "feat: describe el resultado"
git switch main
git merge feature/mi-cambio
```

## Enlaces oficiales

- [Documentación de Git](https://git-scm.com/doc)
- [Libro Pro Git en español](https://git-scm.com/book/es/v2)
- [`git init`](https://git-scm.com/docs/git-init)
- [`git switch`](https://git-scm.com/docs/git-switch)
- [`git add`](https://git-scm.com/docs/git-add)
- [`git commit`](https://git-scm.com/docs/git-commit)
- [`git merge`](https://git-scm.com/docs/git-merge)
- [Hooks de Git](https://git-scm.com/docs/githooks)
- [`core.hooksPath`](https://git-scm.com/docs/git-config#Documentation/git-config.txt-corehooksPath)
