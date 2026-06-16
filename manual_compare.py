from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

import cv2
import pytesseract

from database import SheetData


TESSERACT_PATHS = (
    Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe"),
    Path(r"C:\Program Files (x86)\Tesseract-OCR\tesseract.exe"),
)


class ManualCompareError(RuntimeError):
    pass


@dataclass(frozen=True)
class CompareItem:
    category: str
    value: str
    found: bool


@dataclass(frozen=True)
class CompareResult:
    items: tuple[CompareItem, ...]
    raw_text: str

    @property
    def found_count(self) -> int:
        return sum(item.found for item in self.items)

    @property
    def total_count(self) -> int:
        return len(self.items)


def compare_manual_image(path: str | Path, sheet: SheetData) -> CompareResult:
    image_path = Path(path)
    image = cv2.imread(str(image_path))
    if image is None:
        raise ManualCompareError("Cannot read the selected image.")

    tesseract_path = next((path for path in TESSERACT_PATHS if path.exists()), None)
    if tesseract_path is None:
        raise ManualCompareError("Tesseract OCR is not installed.")
    pytesseract.pytesseract.tesseract_cmd = str(tesseract_path)

    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    enlarged = cv2.resize(gray, None, fx=2, fy=2, interpolation=cv2.INTER_CUBIC)
    binary = cv2.adaptiveThreshold(
        enlarged,
        255,
        cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
        cv2.THRESH_BINARY,
        31,
        11,
    )
    raw_text = pytesseract.image_to_string(
        binary,
        config="--psm 11 -c tessedit_char_whitelist=0123456789",
    )
    tokens = set(re.findall(r"\d+", raw_text))
    return compare_tokens(tokens, sheet, raw_text)


def compare_tokens(
    tokens: set[str],
    sheet: SheetData,
    raw_text: str = "",
) -> CompareResult:
    expected = {
        "Coat Lot": {sheet.coat_lot_no},
        "Tray Lot": {
            measurement.lot_number
            for measurement in sheet.measurements
            if measurement.lot_number
        },
        "Tray Number": {
            measurement.tray_number for measurement in sheet.measurements
        },
        "Diameter": {
            str(round(value))
            for measurement in sheet.measurements
            for value, present in (
                (measurement.r_diameter, measurement.r_present),
                (measurement.l_diameter, measurement.l_present),
            )
            if present
        },
    }
    items = tuple(
        CompareItem(category, value, value in tokens)
        for category, values in expected.items()
        for value in sorted(values, key=_natural_key)
    )
    return CompareResult(items=items, raw_text=raw_text)


def _natural_key(value: str) -> tuple[int, str]:
    return (int(value), value) if value.isdigit() else (0, value)
