import unittest
import tkinter as tk
import tempfile
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock

import database
from database import Measurement, SheetData, initialize_database, load_sheet
from sheet_view import (
    DIAMETER_TEXT_SIZE,
    DIP_LOT_TEXT_SIZE,
    FIRST_TRAY_TOP,
    HEADER_DATA_SIZE,
    TRAY_BLOCK_HEIGHT,
    TRAY_HEADER_HEIGHT,
    TRAY_NUMBER_TEXT_SIZE,
    DiameterSheetView,
)


class DatabaseSheetTest(unittest.TestCase):
    def test_mock_sheet_has_complete_tray_grid(self) -> None:
        original_path = database.DB_PATH
        with tempfile.TemporaryDirectory() as directory:
            database.DB_PATH = Path(directory) / "test.db"
            try:
                initialize_database()
                sheet = load_sheet(1)
            finally:
                database.DB_PATH = original_path

        self.assertEqual(sheet.total_pages, 3)
        self.assertEqual(sheet.capa, str(22 * 6))
        self.assertEqual(
            {(row.tray_position, row.row_number) for row in sheet.measurements},
            {(tray, row) for tray in range(1, 23) for row in range(1, 7)},
        )


class HeaderLayoutTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        initialize_database()
        cls.root = tk.Tk()
        cls.root.withdraw()

    @classmethod
    def tearDownClass(cls) -> None:
        cls.root.destroy()

    def test_header_labels_do_not_overlap_values_at_default_zoom(self) -> None:
        sheet = load_sheet(1)
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)
        self.root.update_idletasks()

        pairs = (
            ("ITEM", sheet.item),
            ("PLATE No.", sheet.plate_no),
            ("DATETIME", sheet.datetime_stamp),
            ("CAPA.", str(sheet.capa)),
            ("DOME Type", sheet.dome_type),
            ("Coat Lot No.", sheet.coat_lot_no),
        )
        for label, value in pairs:
            with self.subTest(label=label):
                if str(value) == "":
                    continue
                label_box = self._bbox_for_text(view, label)
                value_box = self._bbox_for_text(view, value)
                self.assertLessEqual(label_box[2] + 3, value_box[0])

        item_value_box = self._bbox_for_text(view, sheet.item)
        self.assertGreaterEqual(item_value_box[0], round((100 + 8) * view.zoom) - 2)

        if sheet.datetime_stamp:
            datetime_box = self._bbox_for_text(view, sheet.datetime_stamp)
            capa_label_box = self._bbox_for_text(view, "CAPA.")
            self.assertLessEqual(datetime_box[2] + 15, capa_label_box[0])

        pcs_box = self._bbox_for_text(view, "P'cs")
        dome_box = self._bbox_for_text(view, "DOME Type")
        self.assertLessEqual(pcs_box[2] + 3, dome_box[0])

        longtail_83_box = self._bbox_for_text(view, str(sheet.longtail_83))
        longtail_95_box = self._bbox_for_text(view, str(sheet.longtail_95))
        if sheet.dome_type:
            dome_value_box = self._bbox_for_text(view, sheet.dome_type)
            self.assertLessEqual(dome_value_box[2] + 3, longtail_83_box[0])
        if sheet.longtail_83 != sheet.longtail_95:
            self.assertLessEqual(longtail_83_box[2] + 3, longtail_95_box[0])

    def test_lower_header_has_only_one_horizontal_border(self) -> None:
        view = DiameterSheetView(self.root)
        view.show_sheet(load_sheet(1))
        expected_y = 165 * view.zoom
        lower_header_lines = [
            item_id
            for item_id in view.find_all()
            if view.type(item_id) == "line"
            and len(view.coords(item_id)) == 4
            and abs(view.coords(item_id)[1] - expected_y) < 0.01
            and abs(view.coords(item_id)[3] - expected_y) < 0.01
        ]

        self.assertEqual(len(lower_header_lines), 1)

    def test_mouse_wheel_changes_zoom(self) -> None:
        view = DiameterSheetView(self.root)
        view.show_sheet(load_sheet(1))

        result = view._on_mouse_wheel(
            SimpleNamespace(delta=120, num=None, state=0, x=100, y=100)
        )

        self.assertEqual(result, "break")
        self.assertAlmostEqual(view.zoom, 0.9)

    def test_shift_mouse_wheel_scrolls_horizontally_without_zooming(self) -> None:
        view = DiameterSheetView(self.root)
        view.show_sheet(load_sheet(1))
        view.xview_scroll = Mock()

        result = view._on_mouse_wheel(
            SimpleNamespace(delta=-120, num=None, state=0x0001, x=100, y=100)
        )

        self.assertEqual(result, "break")
        self.assertAlmostEqual(view.zoom, 0.8)
        view.xview_scroll.assert_called_once_with(1, "units")

    def test_left_mouse_drag_pans_canvas(self) -> None:
        view = DiameterSheetView(self.root)
        view.scan_mark = Mock()
        view.scan_dragto = Mock()

        self.assertEqual(view._start_pan(SimpleNamespace(x=10, y=20)), "break")
        self.assertEqual(view._drag_pan(SimpleNamespace(x=30, y=40)), "break")
        self.assertEqual(view._stop_pan(SimpleNamespace()), "break")

        view.scan_mark.assert_called_once_with(10, 20)
        view.scan_dragto.assert_called_once_with(30, 40, gain=1)
        self.assertEqual(view.cget("cursor"), "")

    def test_sparse_tray_still_draws_six_rows(self) -> None:
        sheet = SheetData(
            page_number=1,
            total_pages=1,
            item="Semi",
            plate_no="P1",
            datetime_stamp="2026-06-11 10:00",
            dome_type="DVI",
            capa="",
            longtail_83=0,
            longtail_95=0,
            coat_lot_no="C1",
            operator_name="Test",
            measurements=(Measurement(12, 5, "LOT-12", 65.0, 65.0, "175869"),),
        )
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)

        row_numbers = [
            view.itemcget(item_id, "text")
            for item_id in view.find_all()
            if view.type(item_id) == "text"
            and view.itemcget(item_id, "text") in {"1", "2", "3", "4", "5", "6"}
            and view.coords(item_id)[1] > 150 * view.zoom
        ]

        self.assertEqual(row_numbers.count("1"), 22)
        self.assertEqual(row_numbers.count("6"), 22)

    def test_lot_number_is_centered_in_tray_header(self) -> None:
        sheet = SheetData(
            page_number=1,
            total_pages=1,
            item="Semi",
            plate_no="P1",
            datetime_stamp="2026-06-11 10:00",
            dome_type="DVI",
            capa="",
            longtail_83=0,
            longtail_95=0,
            coat_lot_no="C1",
            operator_name="Test",
            measurements=(Measurement(1, 1, "98411509", 65.0, 65.0, "175869"),),
        )
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)

        lot_id = self._id_for_text(view, "98411509")
        tray_label_id = self._id_for_text(view, "T-1")
        lot_x = view.coords(lot_id)[0]
        expected_x = (1600 / 11 / 2) * view.zoom

        self.assertAlmostEqual(lot_x, expected_x)
        self.assertEqual(view.itemcget(lot_id, "anchor"), "center")
        self.assertEqual(
            view.itemcget(lot_id, "font").split()[1],
            str(round(DIP_LOT_TEXT_SIZE * view.zoom)),
        )
        self.assertLess(view.bbox(tray_label_id)[2] + 2, view.bbox(lot_id)[0])

    def test_empty_tray_column_does_not_show_dash_placeholder(self) -> None:
        view = DiameterSheetView(self.root)
        view.show_sheet(load_sheet(1))

        texts = [
            view.itemcget(item_id, "text")
            for item_id in view.find_all()
            if view.type(item_id) == "text"
        ]

        self.assertNotIn("-", texts)

    def test_second_page_tray_labels_continue_from_t23(self) -> None:
        sheet = SheetData(
            page_number=2,
            total_pages=2,
            item="Semi",
            plate_no="P1",
            datetime_stamp="2026-06-11 10:00",
            dome_type="DVI",
            capa="",
            longtail_83=0,
            longtail_95=0,
            coat_lot_no="C1",
            operator_name="Test",
            measurements=(),
        )
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)
        texts = [
            view.itemcget(item_id, "text")
            for item_id in view.find_all()
            if view.type(item_id) == "text"
        ]

        self.assertIn("T-23", texts)
        self.assertIn("T-44", texts)

    def test_diameter_is_displayed_without_decimal_places(self) -> None:
        sheet = SheetData(
            page_number=1,
            total_pages=1,
            item="Semi",
            plate_no="P1",
            datetime_stamp="2026-06-11 10:00",
            dome_type="DVI",
            capa="",
            longtail_83=0,
            longtail_95=0,
            coat_lot_no="C1",
            operator_name="Test",
            measurements=(Measurement(1, 1, "LOT-1", 65.0, 75.4, "175869"),),
        )
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)
        texts = [
            view.itemcget(item_id, "text")
            for item_id in view.find_all()
            if view.type(item_id) == "text"
        ]

        self.assertIn("65", texts)
        self.assertIn("75", texts)
        self.assertNotIn("65.000", texts)

        diameter_id = self._id_for_text(view, "65")
        tray_id = self._id_for_text(view, "175869")
        self.assertIn(str(round(DIAMETER_TEXT_SIZE * view.zoom)), view.itemcget(diameter_id, "font"))
        self.assertIn(str(round(TRAY_NUMBER_TEXT_SIZE * view.zoom)), view.itemcget(tray_id, "font"))

    def test_cell_values_fit_inside_row(self) -> None:
        sheet = SheetData(
            page_number=1,
            total_pages=1,
            item="Semi",
            plate_no="P1",
            datetime_stamp="2026-06-11 10:00",
            dome_type="DVI",
            capa="",
            longtail_83=0,
            longtail_95=0,
            coat_lot_no="C1",
            operator_name="Test",
            measurements=(Measurement(1, 1, "LOT-1", 65.0, 65.0, "175869"),),
        )
        view = DiameterSheetView(self.root)
        view.show_sheet(sheet)
        self.root.update_idletasks()

        row_top = (FIRST_TRAY_TOP + TRAY_HEADER_HEIGHT * 2) * view.zoom
        row_height = (
            (TRAY_BLOCK_HEIGHT - TRAY_HEADER_HEIGHT * 2) / 6 * view.zoom
        )
        diameter_box = view.bbox(self._id_for_text(view, "65"))
        tray_box = view.bbox(self._id_for_text(view, "175869"))

        self.assertGreater(DIAMETER_TEXT_SIZE, 0)
        self.assertGreater(TRAY_NUMBER_TEXT_SIZE, 0)
        self.assertGreater(HEADER_DATA_SIZE, 0)
        self.assertGreaterEqual(diameter_box[1], row_top)
        self.assertLess(diameter_box[3], tray_box[1])
        self.assertLessEqual(tray_box[3], row_top + row_height)

    @staticmethod
    def _bbox_for_text(view: DiameterSheetView, text: str) -> tuple[int, int, int, int]:
        return view.bbox(HeaderLayoutTest._id_for_text(view, text))

    @staticmethod
    def _id_for_text(view: DiameterSheetView, text: str) -> int:
        for item_id in view.find_all():
            if view.type(item_id) == "text" and view.itemcget(item_id, "text") == text:
                return item_id
        raise AssertionError(f"Text not found: {text}")


if __name__ == "__main__":
    unittest.main()
