#!/usr/bin/env python3
"""Contrôle lecture seule des nouvelles observations SQLite multiserveurs.

N'ouvre jamais la base en écriture. Python standard uniquement.
"""

import argparse
from contextlib import closing
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import sys

TABLES = ("MarketPriceObservations", "CoefficientObservations", "CrushHistorySessions")
KIND_TABLE = {
    "prix": "MarketPriceObservations",
    "coefficients": "CoefficientObservations",
    "concassages": "CrushHistorySessions",
}


def database_path(raw):
    if raw:
        return Path(raw).expanduser().resolve()
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise ValueError("LOCALAPPDATA absent : utiliser --database <chemin>.")
    return Path(local, "BestCrush", "Data", "bestcrush.db").resolve()


def database_key(path):
    return hashlib.sha256(os.path.normcase(str(path)).encode("utf-8")).hexdigest()


def connect_readonly(path):
    if not path.is_file():
        raise FileNotFoundError(f"Base introuvable : {path}")
    # SQLite's mode=ro and query_only explicitly prevent DB mutations.
    connection = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True, timeout=5)
    connection.execute("PRAGMA query_only=ON")
    connection.execute("BEGIN")  # consistent read snapshot, including active WAL
    for table in TABLES:
        connection.execute(f'SELECT rowid FROM "{table}" LIMIT 0')
    return connection


def upper_marks(connection):
    return {
        table: int(connection.execute(
            f'SELECT COALESCE(MAX(rowid), 0) FROM "{table}"'
        ).fetchone()[0])
        for table in TABLES
    }


def checkpoint(connection, db_path, output):
    data = {
        "format": "BestCrushSqlAudit-v1",
        "taken_at_utc": datetime.now(timezone.utc).isoformat(),
        "database_key": database_key(db_path),
        "markers": upper_marks(connection),
    }
    if output.exists():
        raise FileExistsError(f"Le point de référence existe déjà : {output}")
    output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Point de référence créé : {output}")
    print("La base BestCrush n'a pas été modifiée.")


def compare(connection, db_path, baseline, kind, expected):
    data = json.loads(baseline.read_text(encoding="utf-8"))
    if data.get("format") != "BestCrushSqlAudit-v1":
        raise ValueError("Format du point de référence non reconnu.")
    if data.get("database_key") != database_key(db_path):
        raise ValueError("Le point de référence concerne un autre chemin de base SQLite.")

    table = KIND_TABLE[kind]
    initial = data["markers"]
    current = upper_marks(connection)
    if any(current[t] < initial[t] for t in TABLES):
        raise ValueError("Des rowid ont diminué : base remplacée ou vidée, recette à recommencer.")

    # Source 1 = InGameAutomatic for market prices and coefficients.
    # CrushHistorySessions are generated from completed crush sessions.
    filter_source = " AND Source = 1" if kind != "concassages" else ""
    column = "COUNT(DISTINCT DofusDbId)" if kind != "concassages" else "0"
    time_col = "ObservedAtUtc" if kind != "concassages" else "CompletedAtUtc"
    query = (f'SELECT ServerName, COUNT(*), {column}, MAX("{time_col}") '
             f'FROM "{table}" WHERE rowid > ?{filter_source} '
             'GROUP BY ServerName ORDER BY ServerName COLLATE NOCASE')
    rows = connection.execute(query, (initial[table],)).fetchall()
    print(f"Nouvelles observations : {kind} (capture automatique pour prix/coefficients)")
    if not rows:
        print("  Aucune nouvelle observation.")
    for server, count, item_count, latest in rows:
        extra = f", objets distincts : {item_count}" if kind != "concassages" else ""
        print(f"  {server}: {count} ligne(s){extra}; dernière : {latest}")

    matching = sum(count for server, count, _, _ in rows if server.casefold() == expected.casefold())
    other = sum(count for server, count, _, _ in rows if server.casefold() != expected.casefold())
    if matching == 0:
        print(f"NON CONCLUANT : aucune nouvelle écriture {kind} pour {expected}.")
        print("Vérifier la capture, les options et l'absence de déduplication (prix identique < 6 h).")
    elif other:
        print(f"À INVESTIGUER : {matching} observation(s) sur {expected}, {other} sur d'autres serveurs.")
        print("Vérifier qu'aucune action n'a été effectuée sur les autres fenêtres.")
    else:
        print(f"CONFORME POUR CETTE ÉTAPE : {matching} observation(s) sur {expected}, aucune sur les autres serveurs.")
    print("Ce constat ne prouve pas à lui seul l'identité de la connexion source.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", help="Chemin alternatif de la base bestcrush.db")
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("baseline", help="Enregistrer les rowid courants, sans copier ni modifier la base")
    p.add_argument("--output", required=True, type=Path)
    c = sub.add_parser("check", help="Comparer les nouvelles observations avec la baseline")
    c.add_argument("--baseline", required=True, type=Path)
    c.add_argument("--kind", choices=tuple(KIND_TABLE), default="prix")
    c.add_argument("--expected-server", required=True)
    args = parser.parse_args()

    try:
        path = database_path(args.database)
        with closing(connect_readonly(path)) as connection:
            if args.command == "baseline":
                checkpoint(connection, path, args.output)
            else:
                compare(connection, path, args.baseline, args.kind, args.expected_server)
    except (OSError, sqlite3.Error, ValueError, KeyError, TypeError) as exc:
        print(f"Erreur : {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
