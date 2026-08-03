#!/bin/sh
# Restaura un respaldo en una base **descartable** y comprueba que llegó entero.
#
# Es la diferencia entre tener respaldos y poder recuperarse. Un archivo que
# `pg_restore --list` puede leer todavía puede fallar al restaurar —una
# extensión que no existe, un rol que no está, un tipo que cambió— y eso solo se
# descubre el día que hace falta, que es el peor día para descubrirlo.
#
# No toca la base de producción en ningún momento: crea una aparte, restaura
# ahí, cuenta y la borra.
set -eu

DUMP="${1:?uso: verificar-restauracion.sh <archivo.dump>}"
DB_HOST="${BACKUP_DB_HOST:-db}"
DB_USER="${POSTGRES_USER:-margen}"
SCRATCH="verificacion_$(date +%s)"

echo "[verificar] restaurando ${DUMP} en ${SCRATCH}"

# La base descartable se borra pase lo que pase. Sin esto, una verificación que
# falla deja una base huérfana ocupando disco, y a la décima el disco se llena y
# se paran los respaldos: el fallo del que protege esto lo causaría esto mismo.
limpiar() {
  psql -h "${DB_HOST}" -U "${DB_USER}" -d postgres \
    -c "DROP DATABASE IF EXISTS ${SCRATCH}" > /dev/null 2>&1 || true
}
trap limpiar EXIT INT TERM

psql -h "${DB_HOST}" -U "${DB_USER}" -d postgres -c "CREATE DATABASE ${SCRATCH}" > /dev/null

# `--exit-on-error` es lo que convierte esto en una verificación. Sin él,
# pg_restore informa de los errores y termina con éxito, así que un respaldo
# irrecuperable pasaría la prueba.
pg_restore -h "${DB_HOST}" -U "${DB_USER}" -d "${SCRATCH}" \
  --no-owner --no-privileges --exit-on-error "${DUMP}"

# Que restaure sin errores no basta: un dump de una base vacía también restaura
# sin errores. Se cuenta lo que tiene que estar.
filas=$(psql -h "${DB_HOST}" -U "${DB_USER}" -d "${SCRATCH}" -tAc \
  'SELECT COUNT(*) FROM "Transactions"')

tablas=$(psql -h "${DB_HOST}" -U "${DB_USER}" -d "${SCRATCH}" -tAc \
  "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public'")

echo "[verificar] ${tablas} tabla(s), ${filas} movimiento(s)"

if [ "${tablas}" -lt 10 ]; then
  echo "[verificar] FALLO: el esquema restaurado tiene menos tablas de las que debería."
  exit 1
fi

echo "[verificar] correcto: el respaldo se puede restaurar."
