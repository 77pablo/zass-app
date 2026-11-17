"""Lo que el modelo dice que debe existir en la base, como una consulta que devuelve lo que FALTA.

Lee ZassDbContextModelSnapshot.cs, que EF reescribe en cada migración, y saca de ahí:

  * cada tabla        (`ToTable("x")`)          -> information_schema.tables
  * cada índice       (`HasDatabaseName("x")`)   -> pg_indexes
  * cada clave        (`HasName("x")`)           -> pg_constraint

y emite UNA consulta SQL que devuelve una fila por cada objeto que el modelo espera y la base
no tiene. Vacía = todo está. Así la comprobación del flujo «Migrar la base de produccion» no
hay que reescribirla a mano en cada migración: la anterior estaba fija a los objetos del 15-nov
y no miraba nada de lo que vino después.

Uso:  python3 esquema-esperado.py RUTA/ZassDbContextModelSnapshot.cs > faltantes.sql
"""

import re
import sys

PATRONES = {
    "tabla": r'\bToTable\("([^"]+)"',
    "indice": r'\bHasDatabaseName\("([^"]+)"\)',
    "clave": r'\bHasName\("([^"]+)"\)',
}

# Si el snapshot trae menos que esto, la lectura se rompió (un cambio de formato de EF, la
# ruta equivocada) y una consulta casi vacía «pasaría» sin comprobar nada.
MINIMO = {"tabla": 30, "indice": 70, "clave": 30}


def esperados(texto: str) -> list[tuple[str, str]]:
    filas = []
    for tipo, patron in PATRONES.items():
        nombres = sorted(set(re.findall(patron, texto)))
        if len(nombres) < MINIMO[tipo]:
            sys.exit(f"El snapshot trae solo {len(nombres)} de tipo '{tipo}' (se esperan al menos "
                     f"{MINIMO[tipo]}). La lectura está rota: no se compara nada.")
        filas += [(tipo, n) for n in nombres]
    return filas


def sql(filas: list[tuple[str, str]]) -> str:
    for _, nombre in filas:
        # Nombres de identificador, nada más: si aparece una comilla, el snapshot no es lo que
        # creemos y no se arma SQL con eso.
        if not re.fullmatch(r"[A-Za-z0-9_]+", nombre):
            sys.exit(f"Nombre inesperado en el snapshot: {nombre!r}")
    valores = ",\n  ".join(f"('{t}', '{n}')" for t, n in filas)
    return f"""WITH esperado(tipo, nombre) AS (VALUES
  {valores}
)
SELECT tipo || ' FALTA: ' || nombre
FROM esperado e
WHERE (tipo = 'tabla'  AND NOT EXISTS (SELECT 1 FROM information_schema.tables t
                                       WHERE t.table_schema = 'public' AND t.table_name = e.nombre))
   OR (tipo = 'indice' AND NOT EXISTS (SELECT 1 FROM pg_indexes i
                                       WHERE i.schemaname = 'public' AND i.indexname = e.nombre))
   OR (tipo = 'clave'  AND NOT EXISTS (SELECT 1 FROM pg_constraint c
                                       JOIN pg_namespace s ON s.oid = c.connamespace
                                       WHERE s.nspname = 'public' AND c.conname = e.nombre))
ORDER BY 1;
"""


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    with open(sys.argv[1], encoding="utf-8-sig") as f:
        filas = esperados(f.read())
    conteo = {t: sum(1 for x, _ in filas if x == t) for t in PATRONES}
    print(f"-- esperados: {conteo['tabla']} tablas, {conteo['indice']} indices, "
          f"{conteo['clave']} claves", file=sys.stderr)
    print(sql(filas))
