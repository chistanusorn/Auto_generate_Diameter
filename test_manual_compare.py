import unittest

from database import Measurement, SheetData
from manual_compare import compare_tokens


class ManualCompareTest(unittest.TestCase):
    def test_expected_values_are_grouped_and_compared(self) -> None:
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
            coat_lot_no="80632389",
            operator_name="Test",
            measurements=(
                Measurement(1, 1, "98411573", 65.0, 66.0, "175869"),
            ),
        )

        result = compare_tokens({"80632389", "98411573", "65", "175869"}, sheet)
        status = {(item.category, item.value): item.found for item in result.items}

        self.assertTrue(status[("Coat Lot", "80632389")])
        self.assertTrue(status[("Tray Lot", "98411573")])
        self.assertTrue(status[("Tray Number", "175869")])
        self.assertTrue(status[("Diameter", "65")])
        self.assertFalse(status[("Diameter", "66")])


if __name__ == "__main__":
    unittest.main()
