# -*- coding: utf-8 -*-
"""Genera ZonasDeChile.cs a partir de las comunas bajadas de OpenStreetMap (reg_*.json o comunas_chile.json)."""
import glob, io, json, os, sys, unicodedata

CARPETA = os.path.dirname(os.path.abspath(__file__)) + "/"
SALIDA = "C:/Users/pdani/zass-app/backend/src/Zass.Infraestructura/Datos/ZonasDeChile.cs"

# Nombre corto por region, y el orden de norte a sur (es el orden de los ids: estable y legible).
REGIONES = [
    ("CL-AP", "Arica y Parinacota"), ("CL-TA", "Tarapacá"), ("CL-AN", "Antofagasta"), ("CL-AT", "Atacama"),
    ("CL-CO", "Coquimbo"), ("CL-VS", "Valparaíso"), ("CL-RM", "Metropolitana de Santiago"),
    ("CL-LI", "O'Higgins"), ("CL-ML", "Maule"), ("CL-NB", "Ñuble"), ("CL-BI", "Biobío"),
    ("CL-AR", "La Araucanía"), ("CL-LR", "Los Ríos"), ("CL-LL", "Los Lagos"), ("CL-AI", "Aysén"),
    ("CL-MA", "Magallanes y la Antártica Chilena"),
]
CORTO = dict(REGIONES)
ORDEN = {iso: i for i, (iso, _) in enumerate(REGIONES)}


def clave(nombre):
    return unicodedata.normalize("NFD", nombre).encode("ascii", "ignore").decode().lower()


todas = []
archivo = CARPETA + "comunas_chile.json"
if os.path.exists(archivo):
    todas = json.load(open(archivo, encoding="utf-8"))
else:
    for f in sorted(glob.glob(CARPETA + "reg_*.json")):
        todas += json.load(open(f, encoding="utf-8"))
    print("AVISO: datos parciales (", len(todas), "comunas )", file=sys.stderr)

# La comuna Antartica no existe como relacion administrativa en OpenStreetMap, pero su capital legal, Villa Las
# Estrellas, SI es un nodo (OSM node 446987858, place=village). Se usa ese punto, y se deja dicho en el archivo.
if not any(c["nombre"] == "Antártica" for c in todas):
    todas.append({"osm": 446987858, "nombre": "Antártica", "region": "Magallanes", "iso": "CL-MA",
                  "lat": -62.2004259, "lon": -58.965369, "origen": "capital"})
    NOTA_ANTARTICA = True
else:
    NOTA_ANTARTICA = False

for c in todas:
    n = c["nombre"].strip()
    for pref in ("Comuna de ", "Comuna "):
        if n.startswith(pref):
            n = n[len(pref):]
    c["nombre"] = n

nombres = [c["nombre"] for c in todas]
duplicados = sorted({n for n in nombres if nombres.count(n) > 1})
if duplicados:
    raise SystemExit(f"nombres de comuna repetidos: {duplicados}")

todas.sort(key=lambda c: (ORDEN[c["iso"]], clave(c["nombre"])))
ID_INICIAL = 101
filas = []
id_temuco = None
for i, c in enumerate(todas):
    zid = ID_INICIAL + i
    if c["nombre"] == "Temuco":
        id_temuco = zid
    region = CORTO[c["iso"]]
    nombre = c["nombre"].replace('"', '\\"')
    region_cs = region.replace('"', '\\"')
    filas.append(
        f'        new Zona {{ Id = {zid}, Nombre = "{nombre}", Sector = "{region_cs}", Region = "{region_cs}", '
        f'Comuna = "{nombre}", Tipo = TipoDeZona.Comuna, CentroLatitud = {c["lat"]!r}, CentroLongitud = {c["lon"]!r} }},'
    )

de_caja = [c["nombre"] for c in todas if c["origen"] != "capital"]
cuerpo = "\n".join(filas)
codigo = f'''using Zass.Dominio.Catalogo;

namespace Zass.Infraestructura.Datos;

/// <summary>
/// Las comunas de Chile como zonas: {len(todas)} comunas en 16 regiones, de norte a sur. GENERADO por
/// <c>generar_zonas.py</c> a partir de OpenStreetMap; no se edita a mano (se vuelve a generar).
///
/// PROCEDENCIA: cada comuna es una relacion administrativa (admin_level=8) de OpenStreetMap, consultada por
/// Overpass el 7 de noviembre de 2026. El punto de cada una es el nodo de su CAPITAL COMUNAL
/// (<c>admin_centre</c>): el lugar donde la gente dice que esta la comuna, no el centro geometrico de un
/// territorio que en el sur puede ser un fiordo. Cuando OSM no tiene ese nodo se usa el centro de su caja, y
/// son estas: {", ".join(de_caja) if de_caja else "ninguna"}.
/// {('La comuna Antártica no existe como relación en OSM: su punto es el nodo de Villa Las Estrellas (OSM node 446987858), su capital legal.' if NOTA_ANTARTICA else '')}
/// No hay ninguna coordenada inventada ni redondeada a mano. Los datos de OSM son ODbL, que pide atribucion a
/// «OpenStreetMap contributors» donde se muestren (esta en el pie del sitio).
///
/// Los ids van de {ID_INICIAL} en adelante, por region de norte a sur y por orden alfabetico dentro de cada una.
/// Quedan fijos una vez migrados: una cobertura guardada apunta a ellos.
/// </summary>
public static class ZonasDeChile
{{
    /// <summary>La comuna de Temuco: de ella cuelgan los 16 barrios que ya existian.</summary>
    public const int IdTemuco = {id_temuco};

    public static readonly Zona[] Comunas =
    {{
{cuerpo}
    }};
}}
'''
io.open(SALIDA, "w", encoding="utf-8", newline="").write(codigo)
print("generado:", len(todas), "comunas; IdTemuco =", id_temuco, "; de caja:", len(de_caja))
