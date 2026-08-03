# Las cabeceras de seguridad, en un solo sitio.
#
# Van en un fragmento porque hay que repetirlas en cada `location` que ponga
# cabeceras propias: en nginx, un `add_header` en un bloque hijo descarta todos
# los heredados. Escribirlas cuatro veces a mano garantiza que un día se
# actualicen tres.
#
# La PWA no carga nada de fuera: ni fuentes, ni analítica, ni imágenes. Se dice
# explícitamente para que un script inyectado no tenga a dónde mandar datos.
#
# `connect-src` nombra al API porque vive en otro subdominio, y para el
# navegador eso es otro origen. Sin esa parte la app carga bien y todas sus
# peticiones fallan, con el error solo en la consola del navegador.
#
# **`blob:` y `worker-src` no son un descuido de permisividad.** Flutter arranca
# su motor gráfico en un Web Worker que crea desde una URL `blob:`, y `'self'`
# no cubre ese esquema. Sin permitirlo, el worker no arranca, nadie pinta
# encima del HTML y la app se queda en «Cargando…» para siempre —sin error en
# el servidor, sin fallo en el proxy y con todos los archivos devolviendo 200—.
#
# Lo que sigue prohibido es lo que importa: nada de otros dominios, ningún
# `unsafe-inline` en los scripts —el `index.html` no tiene ni uno—, y nadie
# puede meter la app en un marco.
#
# `__API_ORIGIN__` lo sustituye el Dockerfile al construir.
add_header X-Content-Type-Options nosniff always;
add_header Referrer-Policy no-referrer always;
add_header Content-Security-Policy "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' blob:; worker-src 'self' blob:; child-src 'self' blob:; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self' blob: __API_ORIGIN__; frame-ancestors 'none'; base-uri 'self'" always;
