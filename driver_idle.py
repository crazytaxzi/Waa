"""Reusable parser and SQLite-backed database for rolling 7-day CSV files.

Another application can import this module and call ``build_database`` with
the path to its own 7-day CSV file. The public query entry points are
``lookup_truck``, ``lookup_driver``, and ``fleet_totals``.
"""

from __future__ import annotations

import argparse
import csv
import json
import sqlite3
from datetime import datetime
from pathlib import Path
from typing import Any


DEFAULT_CSV = Path(__file__).with_name("rolling 7 day_data.csv")
DEFAULT_DB = Path(__file__).with_name("driver_idle.sqlite3")


def _iso_date(value: str) -> str:
    return datetime.strptime(value, "%m/%d/%Y").date().isoformat()


def _driver_parts(value: str) -> tuple[str, str]:
    code, separator, name = value.strip().partition(" ")
    if not separator:
        raise ValueError(f"Invalid driver value: {value!r}")
    return code, name


def connect(db_path: str | Path = DEFAULT_DB) -> sqlite3.Connection:
    """Open the database with named-column access enabled."""
    connection = sqlite3.connect(db_path)
    connection.row_factory = sqlite3.Row
    return connection


def build_database(
    csv_path: str | Path = DEFAULT_CSV,
    db_path: str | Path = DEFAULT_DB,
) -> int:
    """Create/update the database from the 7-day CSV and return rows loaded.

    Only the ``Idle %`` measure row is imported. This avoids double-counting
    engine and idle hours, which are repeated on the source's ``OOR %`` row.
    """
    records: list[tuple[Any, ...]] = []
    with Path(csv_path).open(encoding="utf-8-sig", newline="") as source:
        for row in csv.DictReader(source):
            if row["Measure Names"] != "Idle %":
                continue
            driver_code, driver_name = _driver_parts(row["Group by  (copy)"])
            records.append(
                (
                    driver_code,
                    driver_name,
                    row["Unit Code"],
                    _iso_date(row["Week Start Date"]),
                    _iso_date(row["Rolling 7 Day Start Date"]),
                    float(row["[Rolling 7 Day Engine Time]/60"] or 0),
                    float(row["[Rolling 7 Day Idle Time]/60"] or 0),
                )
            )

    with connect(db_path) as db:
        db.executescript(
            """
            CREATE TABLE IF NOT EXISTS weekly_idle (
                driver_code TEXT NOT NULL,
                driver_name TEXT NOT NULL,
                unit_number TEXT NOT NULL,
                week_end TEXT NOT NULL,
                week_start TEXT NOT NULL,
                engine_hours REAL NOT NULL,
                idle_hours REAL NOT NULL,
                PRIMARY KEY (driver_code, week_end)
            );
            CREATE INDEX IF NOT EXISTS idx_weekly_idle_unit_week
                ON weekly_idle (unit_number, week_end DESC);
            CREATE INDEX IF NOT EXISTS idx_weekly_idle_week
                ON weekly_idle (week_end DESC);
            """
        )
        db.execute("DELETE FROM weekly_idle")
        db.executemany(
            """
            INSERT INTO weekly_idle (
                driver_code, driver_name, unit_number, week_end, week_start,
                engine_hours, idle_hours
            ) VALUES (?, ?, ?, ?, ?, ?, ?)
            """,
            records,
        )
    return len(records)


def _percentage(idle: float, engine: float) -> float | None:
    return round(idle / engine * 100, 2) if engine else None


def _driver_summary(db: sqlite3.Connection, driver_code: str) -> dict[str, Any] | None:
    rows = db.execute(
        """
        SELECT * FROM weekly_idle
        WHERE driver_code = ?
        ORDER BY week_end DESC
        LIMIT 4
        """,
        (driver_code,),
    ).fetchall()
    if not rows:
        return None

    latest = rows[0]
    engine_28 = sum(row["engine_hours"] for row in rows)
    idle_28 = sum(row["idle_hours"] for row in rows)
    return {
        "driver_code": latest["driver_code"],
        "driver_name": latest["driver_name"],
        "unit_number": latest["unit_number"],
        "week_end": latest["week_end"],
        "engine_hours_7d": latest["engine_hours"],
        "idle_hours_7d": latest["idle_hours"],
        "idle_percent_7d": _percentage(latest["idle_hours"], latest["engine_hours"]),
        "engine_hours_28d": round(engine_28, 1),
        "idle_hours_28d": round(idle_28, 1),
        "idle_percent_28d": _percentage(idle_28, engine_28),
        "weeks_in_28d": len(rows),
    }


def lookup_driver(
    driver_code: str, db_path: str | Path = DEFAULT_DB
) -> dict[str, Any] | None:
    """Return the latest 7- and 28-day summary for a driver."""
    with connect(db_path) as db:
        return _driver_summary(db, str(driver_code))


def lookup_truck(
    unit_number: str, db_path: str | Path = DEFAULT_DB
) -> list[dict[str, Any]]:
    """Return summaries for drivers assigned to a truck in the latest week."""
    with connect(db_path) as db:
        latest = db.execute("SELECT MAX(week_end) FROM weekly_idle").fetchone()[0]
        codes = db.execute(
            """
            SELECT driver_code FROM weekly_idle
            WHERE unit_number = ? AND week_end = ?
            ORDER BY driver_code
            """,
            (str(unit_number), latest),
        ).fetchall()
        return [_driver_summary(db, row[0]) for row in codes]


def fleet_totals(db_path: str | Path = DEFAULT_DB) -> dict[str, Any]:
    """Return correctly weighted latest 7- and 28-day fleet totals."""
    with connect(db_path) as db:
        latest = db.execute("SELECT MAX(week_end) FROM weekly_idle").fetchone()[0]
        if latest is None:
            return {
                "week_end": None,
                "driver_count": 0,
                "engine_hours_7d": 0.0,
                "idle_hours_7d": 0.0,
                "idle_percent_7d": None,
                "engine_hours_28d": 0.0,
                "idle_hours_28d": 0.0,
                "idle_percent_28d": None,
            }
        seven = db.execute(
            """
            SELECT COUNT(*), SUM(engine_hours), SUM(idle_hours)
            FROM weekly_idle WHERE week_end = ?
            """,
            (latest,),
        ).fetchone()
        twenty_eight = db.execute(
            """
            WITH ranked AS (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY driver_code ORDER BY week_end DESC
                ) AS week_rank
                FROM weekly_idle
                WHERE week_end <= ?
            )
            SELECT SUM(engine_hours), SUM(idle_hours)
            FROM ranked WHERE week_rank <= 4
            """,
            (latest,),
        ).fetchone()

    return {
        "week_end": latest,
        "driver_count": seven[0],
        "engine_hours_7d": round(seven[1], 1),
        "idle_hours_7d": round(seven[2], 1),
        "idle_percent_7d": _percentage(seven[2], seven[1]),
        "engine_hours_28d": round(twenty_eight[0], 1),
        "idle_hours_28d": round(twenty_eight[1], 1),
        "idle_percent_28d": _percentage(twenty_eight[1], twenty_eight[0]),
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Local driver idle database")
    parser.add_argument("--db", default=DEFAULT_DB, help="SQLite database path")
    commands = parser.add_subparsers(dest="command", required=True)
    build = commands.add_parser("build", help="Import the rolling 7-day CSV")
    build.add_argument("--csv", default=DEFAULT_CSV, help="Source CSV path")
    truck = commands.add_parser("truck", help="Look up a truck/unit number")
    truck.add_argument("unit_number")
    driver = commands.add_parser("driver", help="Look up a driver code")
    driver.add_argument("driver_code")
    commands.add_parser("totals", help="Show weighted fleet totals")
    args = parser.parse_args()

    if args.command == "build":
        output: Any = {"rows_loaded": build_database(args.csv, args.db)}
    elif args.command == "truck":
        output = lookup_truck(args.unit_number, args.db)
    elif args.command == "driver":
        output = lookup_driver(args.driver_code, args.db)
    else:
        output = fleet_totals(args.db)
    print(json.dumps(output, indent=2))


if __name__ == "__main__":
    main()
