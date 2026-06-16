from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from xml.etree import ElementTree as ET
from zipfile import BadZipFile, ZipFile

from database import Measurement


MAIN_NS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main"
REL_NS = "http://schemas.openxmlformats.org/officeDocument/2006/relationships"
PACKAGE_REL_NS = "http://schemas.openxmlformats.org/package/2006/relationships"
NS = {"m": MAIN_NS, "r": REL_NS}

REQUIRED_COLUMNS = {
    "coat_lot_number",
    "coat_lot_seq",
    "dip_lot_number",
    "diplt_seq",
    "rl_type",
    "tray_number",
    "item_type_name",
    "rxarrangement_number",
    "order_route_type_name",
    "traylot_number",
    "used_flag",
    "diameter",
}

COLUMN_MAP = {
    "coat_lot_number": "coat_lot_no",
    "item_type_name": "item",
    "rxarrangement_number": "plate_no",
    "order_route_type_name": "dome_type",
    "dip_lot_number": "lot_number",
    "diplt_seq": "row_number",
    "rl_type": "R/L selector",
    "tray_number": "tray_number",
    "diameter": "r_diameter/l_diameter",
    "coat_lot_seq": "source order",
    "traylot_number": "Hard Coat tray lot",
    "used_flag": "blank-position selector",
}


class XlsxImportError(ValueError):
    pass


@dataclass(frozen=True)
class ImportedSheet:
    item: str
    plate_no: str
    datetime_stamp: str
    dome_type: str
    coat_lot_no: str
    operator_name: str
    measurements: tuple[Measurement, ...]


@dataclass(frozen=True)
class ImportedReport:
    sheets: tuple[ImportedSheet, ...]

    @property
    def measurement_count(self) -> int:
        return sum(len(sheet.measurements) for sheet in self.sheets)


def parse_xlsx(path: str | Path) -> ImportedReport:
    source = Path(path)
    try:
        rows = _read_first_sheet(source)
    except (BadZipFile, KeyError, ET.ParseError, OSError) as error:
        raise XlsxImportError(f"Cannot read XLSX file: {error}") from error

    if not rows:
        raise XlsxImportError("The first worksheet is empty.")

    headers = {str(value).strip() for value in rows[0].values() if value is not None}
    missing = sorted(REQUIRED_COLUMNS - headers)
    if missing:
        raise XlsxImportError(f"Missing required columns: {', '.join(missing)}")

    header_by_index = {
        index: str(value).strip()
        for index, value in rows[0].items()
        if value is not None and str(value).strip()
    }
    records = [
        {header_by_index[index]: value for index, value in row.items() if index in header_by_index}
        for row in rows[1:]
        if row
    ]
    records = [record for record in records if any(str(value).strip() for value in record.values())]
    if not records:
        raise XlsxImportError("The worksheet has headers but no data rows.")

    modified = datetime.fromtimestamp(source.stat().st_mtime).strftime("%Y-%m-%d %H:%M")
    return build_report(records, modified)


def build_report(records: list[dict[str, object]], datetime_stamp: str) -> ImportedReport:
    records.sort(
        key=lambda record: (
            _as_int(record, "coat_lot_seq"),
            _as_int(record, "diplt_seq"),
            _required_text(record, "rl_type"),
        )
    )
    columns: list[dict[str, object]] = []
    hard_groups: dict[tuple[str, str], dict[str, object]] = {}
    blank_groups: set[tuple[str, ...]] = set()
    zero_traylot_groups: dict[tuple[str, str], dict[str, object]] = {}
    zero_traylot_slots: dict[tuple[str, ...], tuple[dict[str, object], int]] = {}
    no_hard_slots: dict[tuple[str, ...], tuple[dict[str, object], int]] = {}
    no_hard_group: dict[str, object] | None = None
    no_hard_slot = 0

    for record in records:
        side = _required_text(record, "rl_type").upper()
        if side not in {"R", "L"}:
            continue
        sequence = _as_int(record, "diplt_seq")
        lot_number = _lot_key(record)
        coat_lot_number = _required_text(record, "coat_lot_number")
        tray_number = _format_tray(record.get("tray_number"))
        used = _optional_text(record, "used_flag") == "1"
        tray_lot = _clean_lot(record.get("traylot_number"))
        dip_lot = _clean_lot(record.get("dip_lot_number"))

        unit_key = (
            _optional_text(record, "coat_lot_seq"),
            _optional_text(record, "diplt_seq"),
            lot_number,
            tray_number,
            _optional_text(record, "rxarrangement_number"),
        )

        if not tray_number:
            if unit_key not in blank_groups:
                blank_groups.add(unit_key)
                columns.append(
                    {
                        "lot_number": lot_number,
                        "coat_lot_number": coat_lot_number,
                        "blank": True,
                        "pack_slots": False,
                        "pairs": {},
                    }
                )
            continue

        if not tray_lot and dip_lot:
            group_key = (coat_lot_number, dip_lot)
            if group_key not in zero_traylot_groups:
                zero_traylot_groups[group_key] = {
                    "lot_number": dip_lot,
                    "coat_lot_number": coat_lot_number,
                    "blank": False,
                    "pack_slots": False,
                    "sequential_slots": True,
                    "pairs": {},
                }
                columns.append(zero_traylot_groups[group_key])
            group = zero_traylot_groups[group_key]
            if unit_key not in zero_traylot_slots:
                slot = len(group["pairs"]) + 1
                if slot > 6:
                    raise XlsxImportError(
                        f"Dip Lot {dip_lot} contains more than 6 Trays."
                    )
                zero_traylot_slots[unit_key] = (group, slot)
            group, slot = zero_traylot_slots[unit_key]
            _add_side(group, slot, side, record)
            continue

        if used and lot_number:
            key = (coat_lot_number, lot_number)
            if key not in hard_groups:
                hard_groups[key] = {
                    "lot_number": lot_number,
                    "coat_lot_number": coat_lot_number,
                    "blank": False,
                    "pack_slots": False,
                    "sequential_slots": False,
                    "pairs": {},
                }
                columns.append(hard_groups[key])
            group = hard_groups[key]
            _add_side(group, sequence, side, record)
            continue

        no_hard_key = (
            _optional_text(record, "coat_lot_seq"),
            _optional_text(record, "diplt_seq"),
            "",
            tray_number,
            _optional_text(record, "rxarrangement_number"),
        )
        if no_hard_key not in no_hard_slots:
            if no_hard_group is None or no_hard_slot >= 6:
                no_hard_group = {
                    "lot_number": "",
                    "coat_lot_number": coat_lot_number,
                    "blank": False,
                    "pack_slots": True,
                    "sequential_slots": False,
                    "pairs": {},
                }
                columns.append(no_hard_group)
                no_hard_slot = 0
            no_hard_slot += 1
            no_hard_slots[no_hard_key] = (no_hard_group, no_hard_slot)
        group, slot = no_hard_slots[no_hard_key]
        _add_side(group, slot, side, record)

    first = records[0]
    common = {
        "item": _required_text(first, "item_type_name"),
        "plate_no": _required_text(first, "rxarrangement_number"),
        "datetime_stamp": datetime_stamp,
        "dome_type": _required_text(first, "order_route_type_name"),
        "coat_lot_no": _required_text(first, "coat_lot_number"),
        "operator_name": "Imported XLSX",
    }
    sheets = []
    for page_start in range(0, len(columns), 22):
        measurements = []
        for local_position, column in enumerate(columns[page_start : page_start + 22], 1):
            for row_number, pair in _pair_slots(column):
                sides = pair["sides"]
                right = sides.get("R")
                left = sides.get("L")
                source = right or left
                measurements.append(
                    Measurement(
                        tray_position=local_position,
                        row_number=row_number,
                        lot_number=column["lot_number"],
                        r_diameter=_as_float(right, "diameter") if right else 0,
                        l_diameter=_as_float(left, "diameter") if left else 0,
                        tray_number=_format_tray(source.get("tray_number")),
                        r_present=right is not None,
                        l_present=left is not None,
                    )
                )
        sheets.append(ImportedSheet(**common, measurements=tuple(measurements)))
    return ImportedReport(sheets=tuple(sheets))


def _add_side(
    group: dict[str, object],
    sequence: int,
    side: str,
    record: dict[str, object],
) -> None:
    pair = group["pairs"].setdefault(sequence, {"sequence": sequence, "sides": {}})
    pair["sides"][side] = record


def _pair_slots(column: dict[str, object]) -> list[tuple[int, dict[str, object]]]:
    if column["blank"]:
        return []
    pairs = list(column["pairs"].values())
    if column.get("sequential_slots"):
        return sorted(
            ((pair["sequence"], pair) for pair in pairs),
            key=lambda item: item[0],
        )
    normal = sorted(
        (pair for pair in pairs if not _is_zero_pair(pair)),
        key=lambda pair: pair["sequence"],
    )
    zero = sorted(
        (pair for pair in pairs if _is_zero_pair(pair)),
        key=lambda pair: pair["sequence"],
    )
    if column["pack_slots"]:
        return list(enumerate(normal + zero, start=1))

    slots = {
        pair["sequence"]: pair
        for pair in normal
        if pair["sequence"] in range(1, 7)
    }
    for pair in zero:
        slot = next((candidate for candidate in range(1, 7) if candidate not in slots), None)
        if slot is not None:
            slots[slot] = pair
    return sorted(slots.items())


def _is_zero_pair(pair: dict[str, object]) -> bool:
    values = [
        _as_float(record, "diameter")
        for record in pair["sides"].values()
        if _optional_text(record, "diameter")
    ]
    return bool(values) and all(value == 0 for value in values)


def _lot_key(record: dict[str, object]) -> str:
    return _clean_lot(record.get("dip_lot_number")) or _clean_lot(
        record.get("traylot_number")
    )


def _clean_lot(value: object) -> str:
    text = "" if value is None else str(value).strip()
    return "" if _is_zero(text) else text


def _optional_text(record: dict[str, object], column: str) -> str:
    return str(record.get(column, "")).strip()


def _format_tray(value: object) -> str:
    text = "" if value is None else str(value).strip()
    if text.endswith(".0"):
        text = text[:-2]
    return text.zfill(6) if text.isdigit() else text


def _read_first_sheet(path: Path) -> list[dict[int, object]]:
    with ZipFile(path) as archive:
        shared_strings = _read_shared_strings(archive)
        workbook = ET.fromstring(archive.read("xl/workbook.xml"))
        relationships = ET.fromstring(archive.read("xl/_rels/workbook.xml.rels"))
        targets = {
            relationship.attrib["Id"]: relationship.attrib["Target"]
            for relationship in relationships.findall(
                f"{{{PACKAGE_REL_NS}}}Relationship"
            )
        }
        first_sheet = workbook.find("m:sheets/m:sheet", NS)
        if first_sheet is None:
            return []
        relationship_id = first_sheet.attrib[f"{{{REL_NS}}}id"]
        target = targets[relationship_id].lstrip("/")
        sheet_path = target if target.startswith("xl/") else f"xl/{target}"
        worksheet = ET.fromstring(archive.read(sheet_path))

        rows = []
        for row in worksheet.findall(".//m:sheetData/m:row", NS):
            values = {}
            for cell in row.findall("m:c", NS):
                index = _column_index(cell.attrib["r"])
                values[index] = _cell_value(cell, shared_strings)
            rows.append(values)
        return rows


def _read_shared_strings(archive: ZipFile) -> list[str]:
    if "xl/sharedStrings.xml" not in archive.namelist():
        return []
    root = ET.fromstring(archive.read("xl/sharedStrings.xml"))
    return [
        "".join(node.text or "" for node in item.findall(".//m:t", NS))
        for item in root.findall("m:si", NS)
    ]


def _cell_value(cell: ET.Element, shared_strings: list[str]) -> object:
    cell_type = cell.attrib.get("t")
    if cell_type == "inlineStr":
        return "".join(node.text or "" for node in cell.findall(".//m:t", NS))
    value_node = cell.find("m:v", NS)
    if value_node is None or value_node.text is None:
        return ""
    if cell_type == "s":
        return shared_strings[int(value_node.text)]
    return value_node.text


def _column_index(reference: str) -> int:
    letters = re.match(r"[A-Z]+", reference)
    if letters is None:
        raise XlsxImportError(f"Invalid cell reference: {reference}")
    result = 0
    for letter in letters.group():
        result = result * 26 + ord(letter) - ord("A") + 1
    return result - 1


def _required_text(record: dict[str, object], column: str) -> str:
    value = str(record.get(column, "")).strip()
    if not value:
        raise XlsxImportError(f"Column {column} contains an empty value.")
    return value


def _as_int(record: dict[str, object], column: str) -> int:
    value = _required_text(record, column)
    try:
        return int(float(value))
    except ValueError as error:
        raise XlsxImportError(f"Column {column} must be a number; found {value!r}.") from error


def _as_float(record: dict[str, object], column: str) -> float:
    value = _required_text(record, column)
    try:
        return float(value)
    except ValueError as error:
        raise XlsxImportError(f"Column {column} must be a number; found {value!r}.") from error


def _is_zero(value: str) -> bool:
    try:
        return float(value) == 0
    except ValueError:
        return False
