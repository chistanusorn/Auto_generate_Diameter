from __future__ import annotations

import sqlite3
from contextlib import closing
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path


DB_PATH = Path(__file__).with_name("diameter_mock.db")


@dataclass(frozen=True)
class Measurement:
    tray_position: int
    row_number: int
    lot_number: str
    r_diameter: float
    l_diameter: float
    tray_number: str
    r_present: bool = True
    l_present: bool = True


@dataclass(frozen=True)
class SheetData:
    page_number: int
    total_pages: int
    item: str
    plate_no: str
    datetime_stamp: str
    dome_type: str
    capa: str
    longtail_83: int
    longtail_95: int
    coat_lot_no: str
    operator_name: str
    measurements: tuple[Measurement, ...]

def connect() -> sqlite3.Connection:
    connection = sqlite3.connect(DB_PATH)
    connection.row_factory = sqlite3.Row
    return connection


def initialize_database() -> None:
    with closing(connect()) as connection:
        with connection:
            connection.executescript(
                """
                CREATE TABLE IF NOT EXISTS sheets (
                    id INTEGER PRIMARY KEY,
                    item TEXT NOT NULL,
                    plate_no TEXT NOT NULL,
                    datetime_stamp TEXT NOT NULL,
                    dome_type TEXT NOT NULL,
                    capa TEXT NOT NULL DEFAULT '',
                    longtail_83 INTEGER NOT NULL,
                    longtail_95 INTEGER NOT NULL,
                    coat_lot_no TEXT NOT NULL,
                    operator_name TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS measurements (
                    id INTEGER PRIMARY KEY,
                    sheet_id INTEGER NOT NULL,
                    tray_position INTEGER NOT NULL,
                    row_number INTEGER NOT NULL,
                    lot_number TEXT NOT NULL,
                    r_diameter REAL NOT NULL,
                    l_diameter REAL NOT NULL,
                    tray_number TEXT NOT NULL,
                    r_present INTEGER NOT NULL DEFAULT 1,
                    l_present INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (sheet_id) REFERENCES sheets(id),
                    UNIQUE(sheet_id, tray_position, row_number)
                );
                """
            )
            measurement_columns = {
                row["name"]
                for row in connection.execute("PRAGMA table_info(measurements)")
            }
            if "r_present" not in measurement_columns:
                connection.execute(
                    "ALTER TABLE measurements ADD COLUMN r_present INTEGER NOT NULL DEFAULT 1"
                )
            if "l_present" not in measurement_columns:
                connection.execute(
                    "ALTER TABLE measurements ADD COLUMN l_present INTEGER NOT NULL DEFAULT 1"
                )
            sheet_columns = {
                row["name"] for row in connection.execute("PRAGMA table_info(sheets)")
            }
            if "capa" not in sheet_columns:
                connection.execute(
                    "ALTER TABLE sheets ADD COLUMN capa TEXT NOT NULL DEFAULT ''"
                )

            count = connection.execute("SELECT COUNT(*) FROM sheets").fetchone()[0]
            if count:
                return

            for page in range(1, 4):
                cursor = connection.execute(
                    """
                    INSERT INTO sheets (
                        item, plate_no, datetime_stamp, dome_type, capa,
                        longtail_83, longtail_95, coat_lot_no, operator_name
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        f"ITEM-{page:03d}",
                        f"PL-{202600 + page}",
                        datetime.now().strftime("%Y-%m-%d %H:%M"),
                        f"DOME-{chr(64 + page)}",
                        str(22 * 6),
                        83 + page,
                        95 + page,
                        f"COAT-{page:03d}",
                        "Mock Operator",
                    ),
                )
                sheet_id = cursor.lastrowid
                rows = []
                for tray in range(1, 23):
                    lot = f"LOT-{page}{tray:02d}"
                    for row in range(1, 7):
                        rows.append(
                            (
                                sheet_id,
                                tray,
                                row,
                                lot,
                                70.0 + page / 10 + tray / 100 + row / 1000,
                                69.8 + page / 10 + tray / 100 + row / 1000,
                                f"{tray:02d}-{row:02d}",
                            )
                        )
                connection.executemany(
                    """
                    INSERT INTO measurements (
                        sheet_id, tray_position, row_number, lot_number,
                        r_diameter, l_diameter, tray_number
                    ) VALUES (?, ?, ?, ?, ?, ?, ?)
                    """,
                    rows,
                )


def load_sheet(page_number: int = 1) -> SheetData:
    with closing(connect()) as connection:
        total_pages = connection.execute("SELECT COUNT(*) FROM sheets").fetchone()[0]
        page_number = max(1, min(page_number, total_pages))
        sheet = connection.execute(
            "SELECT * FROM sheets ORDER BY id LIMIT 1 OFFSET ?",
            (page_number - 1,),
        ).fetchone()
        rows = connection.execute(
            """
            SELECT tray_position, row_number, lot_number, r_diameter,
                   l_diameter, tray_number, r_present, l_present
            FROM measurements
            WHERE sheet_id = ?
            ORDER BY tray_position, row_number
            """,
            (sheet["id"],),
        ).fetchall()

    measurements = tuple(Measurement(**dict(row)) for row in rows)
    return SheetData(
        page_number=page_number,
        total_pages=total_pages,
        item=sheet["item"],
        plate_no=sheet["plate_no"],
        datetime_stamp=sheet["datetime_stamp"],
        dome_type=sheet["dome_type"],
        capa=sheet["capa"],
        longtail_83=sheet["longtail_83"],
        longtail_95=sheet["longtail_95"],
        coat_lot_no=sheet["coat_lot_no"],
        operator_name=sheet["operator_name"],
        measurements=measurements,
    )


def replace_with_imported_sheets(sheets: tuple[object, ...]) -> int:
    if not sheets:
        raise ValueError("Imported report must contain at least one page.")

    with closing(connect()) as connection:
        with connection:
            connection.execute("DELETE FROM measurements")
            connection.execute("DELETE FROM sheets")
            for sheet in sheets:
                cursor = connection.execute(
                    """
                    INSERT INTO sheets (
                        item, plate_no, datetime_stamp, dome_type, capa,
                        longtail_83, longtail_95, coat_lot_no, operator_name
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        "",
                        sheet.plate_no,
                        "",
                        "",
                        "",
                        0,
                        0,
                        sheet.coat_lot_no,
                        sheet.operator_name,
                    ),
                )
                sheet_id = cursor.lastrowid
                connection.executemany(
                    """
                    INSERT INTO measurements (
                        sheet_id, tray_position, row_number, lot_number,
                        r_diameter, l_diameter, tray_number, r_present, l_present
                    ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                    """,
                    (
                        (
                            sheet_id,
                            row.tray_position,
                            row.row_number,
                            row.lot_number,
                            row.r_diameter,
                            row.l_diameter,
                            row.tray_number,
                            int(row.r_present),
                            int(row.l_present),
                        )
                        for row in sheet.measurements
                    ),
                )
            return len(sheets)


def update_report_header(
    *,
    item: str,
    dome_type: str,
    capa: str,
    datetime_stamp: str,
) -> None:
    with closing(connect()) as connection:
        with connection:
            connection.execute(
                """
                UPDATE sheets
                SET item = ?, dome_type = ?, capa = ?, datetime_stamp = ?
                """,
                (item.strip(), dome_type.strip(), capa.strip(), datetime_stamp.strip()),
            )
