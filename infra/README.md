# Despliegue

## Antes del primer arranque

1. `cp .env.example .env` y completar. Generar la contraseña con
   `openssl rand -base64 32`.
2. Confirmar el nombre de la red del proxy:

   ```bash
   docker network ls | grep -i proxy
   ```

   Ese valor va en `PROXY_NETWORK`.
3. Crear la ruta de respaldos en el host y darle permiso al UID 1001.

## En Portainer

Stacks → Add stack → Repository, apuntando a `infra/docker-compose.yml`.
Las variables se cargan desde el archivo `.env` o desde el panel de Portainer;
no se escriben en el compose.

## En Nginx Proxy Manager

| Campo | Valor |
|---|---|
| Domain | `finanzas.tudominio.com` |
| Scheme | `http` |
| Forward host | `margen-api-1` |
| Forward port | `8080` |
| Websockets | apagado |
| Block common exploits | encendido |
| SSL | Let's Encrypt, Force SSL, HTTP/2, HSTS |

En **Advanced**, limitar el ritmo de los endpoints de autenticación:

```nginx
limit_req_zone $binary_remote_addr zone=margen_auth:10m rate=10r/m;

location /api/auth/ {
    limit_req zone=margen_auth burst=5 nodelay;
    proxy_pass http://margen-api-1:8080;
}
```

## Comprobaciones después de desplegar

```bash
# La API responde solo por el proxy.
curl -I https://finanzas.tudominio.com/health/ready

# La base de datos NO responde desde fuera. Esto debe fallar.
nc -zv <ip-del-servidor> 5432

# El respaldo se escribió.
ls -lh /srv/backups/margen/
```

## Restauración

Se prueba una vez al mes, en una base descartable, no en producción:

```bash
docker exec -i margen-db-1 createdb -U margen margen_restore_test
docker exec -i margen-db-1 pg_restore -U margen -d margen_restore_test \
  /backups/margen-YYYYMMDD-HHMM.dump
docker exec -i margen-db-1 psql -U margen -d margen_restore_test \
  -c 'select count(*) from transactions;'
docker exec -i margen-db-1 dropdb -U margen margen_restore_test
```
