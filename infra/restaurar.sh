#!/bin/sh
# Restaura un respaldo **sobre la base de producción**. Se ejecuta a mano.
#
# Está separado de la verificación a propósito: aquella crea una base aparte y
# no puede hacer daño; esta destruye lo que hay. Fundirlas en un guion con una
# bandera es como se acaba escribiendo la bandera equivocada a las tres de la
# mañana.
set -eu

DUMP="${1:?uso: restaurar.sh <archivo.dump>}"
DB_HOST="${BACKUP_DB_HOST:-db}"
DB_USER="${POSTGRES_USER:-margen}"
DB_NAME="${POSTGRES_DB:-margen}"

cat <<AVISO
Esto va a REEMPLAZAR el contenido de ${DB_NAME} en ${DB_HOST}
con el de ${DUMP}.

Todo lo que haya entrado después de ese respaldo se pierde.

Antes de seguir, para el API y el worker:
  docker compose stop api worker

AVISO

printf 'Escribe el nombre de la base para confirmar: '
read -r confirmacion

if [ "${confirmacion}" != "${DB_NAME}" ]; then
  echo "No coincide. No se ha tocado nada."
  exit 1
fi

# Se verifica el archivo **antes** de tocar nada. Vaciar la base y descubrir
# después que el respaldo estaba corrupto deja sin datos y sin respaldo.
echo "[restaurar] comprobando el archivo…"
pg_restore --list "${DUMP}" > /dev/null

echo "[restaurar] restaurando…"

# `--clean --if-exists` borra los objetos antes de recrearlos, dentro de una
# sola transacción: si algo falla a mitad, la base se queda como estaba en vez
# de a medio restaurar.
pg_restore -h "${DB_HOST}" -U "${DB_USER}" -d "${DB_NAME}" \
  --clean --if-exists --no-owner --no-privileges \
  --single-transaction --exit-on-error "${DUMP}"

echo "[restaurar] hecho. Arranca de nuevo:"
echo "  docker compose start api worker"
