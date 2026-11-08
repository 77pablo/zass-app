# -*- coding: utf-8 -*-
"""Baja las 16 regiones y las comunas de Chile desde OpenStreetMap (Overpass), con la capital comunal
(admin_centre) como punto representativo y el centro de la caja como respaldo. Guarda un archivo POR REGION:
si el servicio publico falla a la mitad, lo ya bajado no se pierde y al volver a correr se retoma.
Datos © colaboradores de OpenStreetMap (ODbL): ya se muestra la atribucion en el pie del sitio."""
import json, os, sys, time, urllib.parse, urllib.request

UA = "ZASS-datos-geograficos/1.0 (pdanielespinozavega@gmail.com)"
SERVIDORES = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter"]
CARPETA = os.path.dirname(os.path.abspath(__file__)) + "/"


def consulta(q, intentos=8):
    for i in range(intentos):
        url = SERVIDORES[i % len(SERVIDORES)]
        try:
            req = urllib.request.Request(url, data=urllib.parse.urlencode({"data": q}).encode(), headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=200) as r:
                return json.load(r)
        except Exception as e:
            print("  reintento", i + 1, url.split("/")[2], e, flush=True)
            time.sleep(20 * (i + 1))
    raise SystemExit("no se pudo consultar Overpass")


regiones = [e for e in json.load(open(CARPETA + "regiones.json", encoding="utf-8"))["elements"]
            if (e["tags"].get("ISO3166-2") or "").startswith("CL-")]
print("regiones:", len(regiones), flush=True)

todas = []
for reg in regiones:
    nombre_reg = reg["tags"]["name"]
    archivo = CARPETA + f"reg_{reg['tags']['ISO3166-2']}.json"
    if os.path.exists(archivo):
        parcial = json.load(open(archivo, encoding="utf-8"))
        print(nombre_reg, "(ya estaba)", len(parcial), flush=True)
        todas += parcial
        continue
    print(nombre_reg, "...", flush=True)
    d = consulta(f'[out:json][timeout:150];rel({reg["id"]});map_to_area->.r;'
                 f'rel(area.r)["boundary"="administrative"]["admin_level"="8"];out center tags body;')
    comunas = d["elements"]
    centros = {}
    for c in comunas:
        for m in c.get("members", []):
            if m.get("role") == "admin_centre" and m["type"] == "node":
                centros[c["id"]] = m["ref"]
    nodos = {}
    ids = sorted(set(centros.values()))
    for i in range(0, len(ids), 60):
        lote = ",".join(map(str, ids[i:i + 60]))
        for n in consulta(f"[out:json][timeout:60];node(id:{lote});out;")["elements"]:
            nodos[n["id"]] = n
        time.sleep(3)
    parcial = []
    for c in comunas:
        t = c["tags"]
        nodo = nodos.get(centros.get(c["id"]))
        if nodo:
            lat, lon, origen = nodo["lat"], nodo["lon"], "capital"
        else:
            lat, lon, origen = c["center"]["lat"], c["center"]["lon"], "caja"
        parcial.append({
            "osm": c["id"], "nombre": t.get("name"), "region": nombre_reg, "iso": reg["tags"].get("ISO3166-2"),
            "lat": lat, "lon": lon, "origen": origen,
        })
    json.dump(parcial, open(archivo, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("   ", len(parcial), "comunas;", sum(1 for r in parcial if r["origen"] == "capital"), "con capital", flush=True)
    todas += parcial
    time.sleep(8)

json.dump(todas, open(CARPETA + "comunas_chile.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print("total:", len(todas), flush=True)
