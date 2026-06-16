from __future__ import annotations

import os
from contextlib import closing
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any

from xlsx_importer import ImportedReport, XlsxImportError, build_report


QUERY_PATH = Path(__file__).with_name("coat_lot_query.sql")


class DatabaseLoadError(RuntimeError):
    pass


@dataclass(frozen=True)
class MySQLConnectionSettings:
    host: str
    port: str
    database: str
    user: str
    password: str


def load_report_from_database(
    place_code: str,
    lot_number: str,
    settings: MySQLConnectionSettings | None = None,
) -> ImportedReport:
    place_code = place_code.strip()
    lot_number = lot_number.strip()
    if not place_code or not lot_number:
        raise DatabaseLoadError("Production place code and Coat Lot No. are required.")

    settings = settings or MySQLConnectionSettings(
        host=os.getenv("MYSQL_HOST", ""),
        port=os.getenv("MYSQL_PORT", "3306"),
        database=os.getenv("MYSQL_DATABASE", ""),
        user=os.getenv("MYSQL_USER", ""),
        password=os.getenv("MYSQL_PASSWORD", ""),
    )
    host = settings.host.strip()
    port_text = settings.port.strip()
    database = settings.database.strip()
    user = settings.user.strip()
    password = settings.password
    if not host or not database or not user or not password:
        raise DatabaseLoadError(
            "MySQL Host, Database, Username and Password are required."
        )
    try:
        port = int(port_text)
    except ValueError as error:
        raise DatabaseLoadError("MYSQL_PORT must be a number.") from error

    try:
        import pymysql
    except ImportError as error:
        raise DatabaseLoadError(
            "MySQL driver is not installed. Run: python -m pip install pymysql"
        ) from error

    try:
        connection = pymysql.connect(
            host=host,
            port=port,
            user=user,
            password=password,
            database=database,
            charset="utf8mb4",
        )
        with closing(connection):
            records = fetch_records(connection, place_code, lot_number)
    except DatabaseLoadError:
        raise
    except Exception as error:
        raise DatabaseLoadError(f"Database query failed: {error}") from error

    if not records:
        raise DatabaseLoadError(
            f"No data found for place code {place_code} and Coat Lot No. {lot_number}."
        )

    try:
        return build_report(records, datetime.now().strftime("%Y-%m-%d %H:%M"))
    except XlsxImportError as error:
        raise DatabaseLoadError(f"Database data is invalid: {error}") from error


def fetch_records(
    connection: Any,
    place_code: str,
    lot_number: str,
) -> list[dict[str, object]]:
    query = QUERY_PATH.read_text(encoding="utf-8")
    with closing(connection.cursor()) as cursor:
        cursor.execute(query, (place_code, lot_number))
        columns = [description[0].lower() for description in cursor.description]
        return [dict(zip(columns, row)) for row in cursor.fetchall()]
