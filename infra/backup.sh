#!/bin/sh
# Respaldo diario, con retención y con restauración verificada de verdad.
#
# Un respaldo que nunca se restauró no es un respaldo: es un archivo. Este guion
# no se conforma con que el archivo se pueda listar —eso solo dice que no está
# truncado—; una vez por semana lo **restaura entero en una base descartable** y
# comprueba que las tablas llegaron con filas.
set -eu

DB_HOST="${BACKUP_DB_HOST:-db}"
DB_USER="${POSTGRES_USER:-margen}"
DB_NAME="${POSTGRES_DB:-margen}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"
INTERVAL="${BACKUP_INTERVAL_SECONDS:-86400}"
VERIFY_EVERY="${BACKUP_VERIFY_EVERY:-7}"

vuelta=0

while true; do
  inicio=$(date +%s)

  # El sello se calcula **dentro** del bucle. Fuera, todos los respaldos
  # escribían el mismo archivo: había uno solo, se sobrescribía cada día y la
  # retención de catorce días nunca tenía nada que borrar. Un respaldo que se
  # pisa a sí mismo protege de que se rompa el disco y de nada más: ni de un
  # borrado que se note tres días después, ni de una migración que salga mal.
  stamp=$(date +%Y%m%d-%H%M%S)
  salida="/backups/margen-${stamp}.dump"

  echo "[backup] iniciando ${stamp}"

  pg_dump -h "${DB_HOST}" -U "${DB_USER}" -d "${DB_NAME}" \
    --format=custom --compress=9 --file="${salida}"

  # Comprobación barata, en cada vuelta: el archivo se puede leer. Detecta un
  # dump truncado por disco lleno o por un corte a mitad.
  pg_restore --list "${salida}" > /dev/null
  echo "[backup] archivo íntegro: ${salida}"

  vuelta=$((vuelta + 1))

  if [ "$((vuelta % VERIFY_EVERY))" -eq 1 ]; then
    /usr/local/bin/verificar-restauracion.sh "${salida}"
  fi

  find /backups -name 'margen-*.dump' -mtime "+${RETENTION_DAYS}" -delete
  echo "[backup] listo ${salida}"

  # Sin deriva. `sleep 86400` acumula el tiempo que tarda el respaldo, así que la
  # hora se corre unos minutos cada día y en un mes cae en otro momento del que
  # se eligió. Se duerme lo que falta para completar el intervalo, contado desde
  # que empezó.
  gastado=$(( $(date +%s) - inicio ))
  resto=$(( INTERVAL - gastado ))
  [ "${resto}" -lt 60 ] && resto=60

  sleep "${resto}"
done
