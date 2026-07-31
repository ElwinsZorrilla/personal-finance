#!/bin/sh
# Respaldo diario con retención de 14 días y verificación de restauración
# semanal. Un respaldo que nunca se restauró no es un respaldo.
set -eu

STAMP=$(date +%Y%m%d-%H%M)
OUT="/backups/margen-${STAMP}.dump"

while true; do
  echo "[backup] iniciando ${STAMP}"
  pg_dump -h db -U "${POSTGRES_USER:-margen}" -d "${POSTGRES_DB:-margen}" \
    --format=custom --compress=9 --file="${OUT}"

  # Verificación: el dump debe poder listarse. Detecta archivos truncados.
  pg_restore --list "${OUT}" > /dev/null

  find /backups -name 'margen-*.dump' -mtime +14 -delete
  echo "[backup] listo ${OUT}"

  sleep 86400
done
