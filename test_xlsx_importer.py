import unittest
import tempfile
from pathlib import Path

import database
from database import (
    initialize_database,
    load_sheet,
    replace_with_imported_sheets,
    update_report_header,
)
from xlsx_importer import build_report, parse_xlsx


EXAMPLE_XLSX = Path(r"C:\Users\ASUS\Downloads\ExcelFile_2026-06-10T16_15_15 1.xlsx")


@unittest.skipUnless(EXAMPLE_XLSX.exists(), "Example XLSX file is not available")
class XlsxImporterTest(unittest.TestCase):
    def test_example_file_maps_to_sheet_measurements(self) -> None:
        report = parse_xlsx(EXAMPLE_XLSX)
        imported = report.sheets[0]

        self.assertEqual(imported.coat_lot_no, "80632389")
        self.assertEqual(imported.item, "Semi")
        self.assertEqual(report.measurement_count, 40)
        self.assertEqual(len({row.tray_position for row in imported.measurements}), 14)
        self.assertEqual(
            (imported.measurements[0].lot_number, imported.measurements[0].row_number),
            ("98411509", 1),
        )
        self.assertEqual(
            (imported.measurements[0].r_diameter, imported.measurements[0].l_diameter),
            (65.0, 65.0),
        )
        self.assertEqual(imported.measurements[0].tray_position, 1)
        self.assertEqual(imported.measurements[2].tray_number, "087682")

    def test_imported_sheet_replaces_existing_pages_and_can_be_loaded(self) -> None:
        report = parse_xlsx(EXAMPLE_XLSX)
        original_path = database.DB_PATH
        with tempfile.TemporaryDirectory() as directory:
            database.DB_PATH = Path(directory) / "test.db"
            try:
                initialize_database()
                page_count = replace_with_imported_sheets(report.sheets)
                loaded = load_sheet(1)
            finally:
                database.DB_PATH = original_path

        self.assertEqual(page_count, 1)
        self.assertEqual(loaded.total_pages, 1)
        self.assertEqual(loaded.coat_lot_no, "80632389")
        self.assertEqual(loaded.capa, "")
        self.assertEqual(loaded.measurements, report.sheets[0].measurements)

    def test_user_header_values_update_every_report_page(self) -> None:
        records = []
        for sequence in range(1, 24):
            records.extend(
                TrayLotGroupingTest._pair(sequence, tray_lot=f"LOT-{sequence:02d}")
            )
        report = build_report(records, "2026-06-11 12:00")
        original_path = database.DB_PATH
        with tempfile.TemporaryDirectory() as directory:
            database.DB_PATH = Path(directory) / "test.db"
            try:
                initialize_database()
                replace_with_imported_sheets(report.sheets)
                update_report_header(
                    item="Semi",
                    dome_type="HUT",
                    capa="40",
                    datetime_stamp="2026-06-11 14:30",
                )
                first_page = load_sheet(1)
                second_page = load_sheet(2)
            finally:
                database.DB_PATH = original_path

        for page in (first_page, second_page):
            self.assertEqual(page.item, "Semi")
            self.assertEqual(page.dome_type, "HUT")
            self.assertEqual(page.capa, "40")
            self.assertEqual(page.datetime_stamp, "2026-06-11 14:30")


class TrayLotGroupingTest(unittest.TestCase):
    def test_zero_traylot_groups_by_dip_lot_and_resequences_rows(self) -> None:
        records = (
            self._pair(1, tray_lot="0", dip_lot="DIP-A", dip_seq=6)
            + self._pair(2, tray_lot="0", dip_lot="DIP-A", dip_seq=2)
            + self._pair(3, tray_lot="0", dip_lot="DIP-B", dip_seq=5)
        )

        report = build_report(records, "2026-06-11 12:00")
        rows = report.sheets[0].measurements

        self.assertEqual(
            [(row.tray_position, row.row_number, row.lot_number) for row in rows],
            [(1, 1, "DIP-A"), (1, 2, "DIP-A"), (2, 1, "DIP-B")],
        )

    def test_zero_traylot_does_not_move_zero_diameter_out_of_sequence(self) -> None:
        records = (
            self._pair(
                1, tray_lot="0", dip_lot="DIP-A", dip_seq=6, diameter="0"
            )
            + self._pair(2, tray_lot="0", dip_lot="DIP-A", dip_seq=1)
        )

        rows = build_report(records, "2026-06-11 12:00").sheets[0].measurements

        self.assertEqual(
            [(row.row_number, row.tray_number) for row in rows],
            [(1, "TRAY-1"), (2, "TRAY-2")],
        )

    def test_hard_coat_uses_one_column_per_tray_lot(self) -> None:
        records = (
            self._pair(1, tray_lot="LOT-A", dip_lot="DIP-A", dip_seq=1)
            + self._pair(2, tray_lot="LOT-A", dip_lot="DIP-A", dip_seq=3)
            + self._pair(3, tray_lot="LOT-B", dip_lot="DIP-B", dip_seq=2)
        )

        rows = build_report(records, "2026-06-11 12:00").sheets[0].measurements

        self.assertEqual(
            [(row.tray_position, row.row_number, row.lot_number) for row in rows],
            [(1, 1, "DIP-A"), (1, 3, "DIP-A"), (2, 2, "DIP-B")],
        )

    def test_more_than_22_tray_lots_creates_multiple_pages(self) -> None:
        records = []
        for sequence in range(1, 24):
            records.extend(self._pair(sequence, tray_lot=f"LOT-{sequence:02d}"))

        report = build_report(records, "2026-06-11 12:00")

        self.assertEqual(len(report.sheets), 2)
        self.assertEqual(len(report.sheets[0].measurements), 22)
        self.assertEqual(report.sheets[1].measurements[0].tray_position, 1)
        self.assertEqual(report.sheets[1].measurements[0].lot_number, "DIP-23")

    def test_multiple_pages_are_saved_and_loaded_in_sequence(self) -> None:
        records = []
        for sequence in range(1, 24):
            records.extend(self._pair(sequence, tray_lot=f"LOT-{sequence:02d}"))
        report = build_report(records, "2026-06-11 12:00")

        original_path = database.DB_PATH
        with tempfile.TemporaryDirectory() as directory:
            database.DB_PATH = Path(directory) / "test.db"
            try:
                initialize_database()
                page_count = replace_with_imported_sheets(report.sheets)
                first_page = load_sheet(1)
                second_page = load_sheet(2)
            finally:
                database.DB_PATH = original_path

        self.assertEqual(page_count, 2)
        self.assertEqual((first_page.page_number, first_page.total_pages), (1, 2))
        self.assertEqual((second_page.page_number, second_page.total_pages), (2, 2))
        self.assertEqual(second_page.measurements[0].lot_number, "DIP-23")

    def test_missing_side_is_kept_blank_instead_of_failing(self) -> None:
        records = self._pair(1, tray_lot="LOT-A", dip_lot="DIP-A")
        records.pop()

        measurement = build_report(records, "2026-06-11 12:00").sheets[0].measurements[0]

        self.assertTrue(measurement.r_present)
        self.assertFalse(measurement.l_present)

    def test_zero_diameter_pair_moves_to_first_open_slot(self) -> None:
        records = (
            self._pair(1, tray_lot="LOT-A", dip_lot="DIP-A", dip_seq=1)
            + self._pair(2, tray_lot="LOT-A", dip_lot="DIP-A", dip_seq=3)
            + self._pair(
                3,
                tray_lot="LOT-A",
                dip_lot="DIP-A",
                dip_seq=5,
                diameter="0",
            )
        )

        rows = build_report(records, "2026-06-11 12:00").sheets[0].measurements

        self.assertEqual(
            [(row.row_number, row.tray_number) for row in rows],
            [(1, "TRAY-1"), (2, "TRAY-3"), (3, "TRAY-2")],
        )

    @staticmethod
    def _pair(
        sequence: int,
        *,
        tray_lot: str,
        used: int = 1,
        dip_seq: int = 1,
        dip_lot: str | None = None,
        diameter: str = "65",
    ) -> list[dict[str, object]]:
        common = {
            "coat_lot_number": "COAT-1",
            "coat_lot_seq": str(sequence),
            "dip_lot_number": dip_lot or f"DIP-{sequence}",
            "diplt_seq": str(dip_seq),
            "tray_number": f"TRAY-{sequence}",
            "item_type_name": "Semi",
            "rxarrangement_number": "PLATE-1",
            "order_route_type_name": "DVI",
            "traylot_number": tray_lot,
            "used_flag": str(used),
            "diameter": diameter,
        }
        return [{**common, "rl_type": "R"}, {**common, "rl_type": "L"}]


if __name__ == "__main__":
    unittest.main()
