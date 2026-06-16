from __future__ import annotations

import tkinter as tk
from collections import defaultdict

from database import Measurement, SheetData


PAGE_WIDTH = 1600
PAGE_HEIGHT = 1120
BLACK = "#101010"
DATA_RED = "#e01818"
PAPER = "#ffffff"
HEADER_DATA_SIZE = 20
HEADER_DATA_X_OFFSET = 8
DIAMETER_TEXT_SIZE = 12
TRAY_NUMBER_TEXT_SIZE = 12
DIP_LOT_TEXT_SIZE = 10
HEADER_BOTTOM = 165
FIRST_TRAY_TOP = 175
SECOND_TRAY_TOP = 600
TRAY_BLOCK_HEIGHT = 410
TRAY_HEADER_HEIGHT = 24
FOOTER_TOP = 1025
SHIFT_MASK = 0x0001


class DiameterSheetView(tk.Canvas):
    def __init__(self, master: tk.Misc, **kwargs: object) -> None:
        super().__init__(
            master,
            background="#d4d7dc",
            highlightthickness=0,
            **kwargs,
        )
        self.zoom = 0.8
        self._sheet: SheetData | None = None
        self.bind("<MouseWheel>", self._on_mouse_wheel)
        self.bind("<Button-4>", self._on_mouse_wheel)
        self.bind("<Button-5>", self._on_mouse_wheel)
        self.bind("<ButtonPress-1>", self._start_pan)
        self.bind("<B1-Motion>", self._drag_pan)
        self.bind("<ButtonRelease-1>", self._stop_pan)

    def show_sheet(self, sheet: SheetData) -> None:
        self._sheet = sheet
        self.redraw()

    def set_zoom(self, zoom: float) -> None:
        self.zoom = max(0.45, min(1.5, zoom))
        self.redraw()

    def _on_mouse_wheel(self, event: tk.Event) -> str:
        direction = event.delta if event.delta else 1 if event.num == 4 else -1
        if getattr(event, "state", 0) & SHIFT_MASK:
            self.xview_scroll(-1 if direction > 0 else 1, "units")
            return "break"

        old_zoom = self.zoom
        new_zoom = max(0.45, min(1.5, old_zoom + (0.1 if direction > 0 else -0.1)))
        if new_zoom == old_zoom:
            return "break"

        logical_x = self.canvasx(event.x) / old_zoom
        logical_y = self.canvasy(event.y) / old_zoom
        self.zoom = new_zoom
        self.redraw()
        self.update_idletasks()

        scroll_width = PAGE_WIDTH * new_zoom
        scroll_height = PAGE_HEIGHT * new_zoom
        left = logical_x * new_zoom - event.x
        top = logical_y * new_zoom - event.y
        self.xview_moveto(max(0, left) / scroll_width)
        self.yview_moveto(max(0, top) / scroll_height)
        return "break"

    def _start_pan(self, event: tk.Event) -> str:
        self.scan_mark(event.x, event.y)
        self.configure(cursor="fleur")
        return "break"

    def _drag_pan(self, event: tk.Event) -> str:
        self.scan_dragto(event.x, event.y, gain=1)
        return "break"

    def _stop_pan(self, _event: tk.Event) -> str:
        self.configure(cursor="")
        return "break"

    def redraw(self) -> None:
        self.delete("all")
        if self._sheet is None:
            return

        self._draw_sheet(self._sheet)
        self.scale("all", 0, 0, self.zoom, self.zoom)
        self.configure(
            scrollregion=(0, 0, PAGE_WIDTH * self.zoom, PAGE_HEIGHT * self.zoom)
        )

    def _text(
        self,
        x: float,
        y: float,
        text: object,
        *,
        size: int = 12,
        bold: bool = False,
        color: str = BLACK,
        anchor: str = "center",
    ) -> None:
        self.create_text(
            x,
            y,
            text=str(text),
            fill=color,
            anchor=anchor,
            font=(
                "Arial",
                max(4, round(size * self.zoom)),
                "bold" if bold else "normal",
            ),
        )

    def _line(
        self, x1: float, y1: float, x2: float, y2: float, width: int = 1
    ) -> None:
        self.create_line(x1, y1, x2, y2, fill=BLACK, width=width)

    def _draw_sheet(self, sheet: SheetData) -> None:
        self.create_rectangle(
            1, 1, PAGE_WIDTH - 2, PAGE_HEIGHT - 2, fill=PAPER, outline=BLACK
        )
        self._draw_header(sheet)

        by_tray: dict[int, list[Measurement]] = defaultdict(list)
        for measurement in sheet.measurements:
            by_tray[measurement.tray_position].append(measurement)

        tray_offset = (sheet.page_number - 1) * 22
        self._draw_tray_block(FIRST_TRAY_TOP, range(1, 12), by_tray, tray_offset)
        self._draw_tray_block(SECOND_TRAY_TOP, range(12, 23), by_tray, tray_offset)
        self._draw_footer(sheet)

    def _draw_header(self, sheet: SheetData) -> None:
        self._line(0, 22, PAGE_WIDTH, 22)
        self._line(1165, 0, 1165, 22)
        self._line(1385, 0, 1385, 22)
        self._text(1175, 11, "PAGE NO", size=9, bold=True, anchor="w")
        self._text(
            1492 + HEADER_DATA_X_OFFSET,
            11,
            f"{sheet.page_number}/{sheet.total_pages}",
            size=HEADER_DATA_SIZE,
            bold=True,
            color=DATA_RED,
        )

        self._field(5, 72, "ITEM", sheet.item, 100, 430)
        self._field(455, 72, "PLATE No.", sheet.plate_no, 655, 880)
        self._text(950, 72, "Longtail", size=26, bold=True, anchor="w")
        self._text(1175, 72, "Delay", size=26, bold=True, anchor="w")

        self._field(5, 127, "DATETIME", sheet.datetime_stamp, 170, 350, label_size=22, underline=False)
        self._field(380, 127, "CAPA.", sheet.capa, 485, 625, label_size=22, underline=False)
        self._text(660, 127, "P'cs", size=22, bold=True, anchor="w")
        self._field(790, 127, "DOME Type", sheet.dome_type, 940, 990, label_size=18, underline=False)
        self._text(1040, 127, sheet.longtail_83, size=22, bold=True)
        self._text(1120, 127, sheet.longtail_95, size=22, bold=True)
        self._field(
            1175,
            127,
            "Coat Lot No.",
            sheet.coat_lot_no,
            1325,
            1598,
            label_size=18,
            underline=False,
        )
        self._line(0, HEADER_BOTTOM, PAGE_WIDTH, HEADER_BOTTOM)

    def _field(
        self,
        label_x: float,
        y: float,
        label: str,
        value: object,
        value_x: float,
        line_end: float,
        *,
        label_size: int = 26,
        underline: bool = True,
    ) -> None:
        self._text(label_x, y, label, size=label_size, bold=True, anchor="w")
        self._text(
            value_x + HEADER_DATA_X_OFFSET,
            y + 3,
            value,
            size=HEADER_DATA_SIZE,
            bold=True,
            color=DATA_RED,
            anchor="w",
        )
        if underline:
            self._line(value_x - 2, y + 16, line_end, y + 16)

    def _draw_tray_block(
        self,
        top: float,
        tray_positions: range,
        by_tray: dict[int, list[Measurement]],
        tray_offset: int,
    ) -> None:
        block_bottom = top + TRAY_BLOCK_HEIGHT
        tray_width = PAGE_WIDTH / 11
        header_1 = TRAY_HEADER_HEIGHT
        header_2 = TRAY_HEADER_HEIGHT
        row_height = (block_bottom - top - header_1 - header_2) / 6

        self._line(0, top, PAGE_WIDTH, top)
        self._line(0, block_bottom, PAGE_WIDTH, block_bottom)

        for column, tray_position in enumerate(tray_positions):
            left = column * tray_width
            middle = left + tray_width / 2
            right = left + tray_width
            measurements = by_tray.get(tray_position, [])
            lot_number = measurements[0].lot_number if measurements else ""

            self._line(left, top, left, block_bottom)
            self._line(middle, top + header_1, middle, block_bottom)
            self._line(left, top + header_1, right, top + header_1)
            self._line(left, top + header_1 + header_2, right, top + header_1 + header_2)
            self._text(left + 8, top + header_1 / 2, f"T-{tray_offset + tray_position}", size=12, bold=True, anchor="w")
            self._text(
                middle,
                top + header_1 / 2,
                lot_number,
                size=DIP_LOT_TEXT_SIZE,
                bold=True,
                color=DATA_RED,
            )
            self._text(left + tray_width * 0.25, top + header_1 + header_2 / 2, "R", size=12, bold=True)
            self._text(left + tray_width * 0.75, top + header_1 + header_2 / 2, "L", size=12, bold=True)

            for row_number in range(1, 7):
                row_top = top + header_1 + header_2 + (row_number - 1) * row_height
                self._line(left, row_top + row_height, right, row_top + row_height)
                self._text(
                    left + 5,
                    row_top + row_height / 2,
                    row_number,
                    size=12,
                    bold=True,
                    anchor="w",
                )

            for measurement in measurements:
                row_top = (
                    top
                    + header_1
                    + header_2
                    + (measurement.row_number - 1) * row_height
                )
                if measurement.r_present:
                    self._cell_value(left + tray_width * 0.25, row_top, row_height, measurement.r_diameter, measurement.tray_number)
                if measurement.l_present:
                    self._cell_value(left + tray_width * 0.75, row_top, row_height, measurement.l_diameter, measurement.tray_number)

        self._line(PAGE_WIDTH - 1, top, PAGE_WIDTH - 1, block_bottom)

    def _cell_value(
        self,
        x: float,
        row_top: float,
        row_height: float,
        diameter: float,
        tray_number: str,
    ) -> None:
        self._text(
            x,
            row_top + row_height * 0.25,
            str(round(diameter)),
            size=DIAMETER_TEXT_SIZE,
            bold=True,
            color=DATA_RED,
        )
        self._text(
            x,
            row_top + row_height * 0.75,
            tray_number,
            size=TRAY_NUMBER_TEXT_SIZE,
            bold=True,
            color=DATA_RED,
        )

    def _draw_footer(self, sheet: SheetData) -> None:
        top = FOOTER_TOP
        self._line(0, top, PAGE_WIDTH, top)
        self._text(5, top + 28, "LENS TEST", size=18, bold=True, anchor="w")
        tests = (("HC50S", 225), ("DHC50S", 515), ("THD5", 800), ("UV", 1095))
        for name, x in tests:
            self._text(x, top + 28, name, size=13, bold=True, anchor="w")
            self._line(x - 2, top + 43, x + 225, top + 43)
            self._text(x + 95, top + 28, "p'cs", size=12, bold=True, anchor="w")
        self._text(1320, top + 28, "Operator", size=13, bold=True, anchor="w")
        self._text(1400, top + 28, sheet.operator_name, size=16, bold=True, color=DATA_RED, anchor="w")
        self._line(1318, top + 43, 1598, top + 43)
