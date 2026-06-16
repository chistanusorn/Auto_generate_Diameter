import unittest
from unittest.mock import Mock, patch

from database_loader import (
    DatabaseLoadError,
    MySQLConnectionSettings,
    fetch_records,
    load_report_from_database,
)


class DatabaseLoaderTest(unittest.TestCase):
    def test_query_uses_bound_parameters(self) -> None:
        cursor = Mock()
        cursor.description = [("COAT_LOT_NUMBER",), ("COAT_LOT_SEQ",)]
        cursor.fetchall.return_value = [("80632389", 1)]
        connection = Mock()
        connection.cursor.return_value = cursor

        records = fetch_records(connection, "28", "80632389")

        query, parameters = cursor.execute.call_args.args
        self.assertEqual(query.count("%s"), 2)
        self.assertNotIn("PARTITION (", query)
        self.assertEqual(parameters, ("28", "80632389"))
        self.assertEqual(records, [{"coat_lot_number": "80632389", "coat_lot_seq": 1}])

    @patch.dict("os.environ", {}, clear=True)
    def test_missing_connection_settings_has_clear_error(self) -> None:
        with self.assertRaisesRegex(DatabaseLoadError, "MySQL Host"):
            load_report_from_database("28", "80632389")

    @patch.dict(
        "os.environ",
        {
            "MYSQL_HOST": "localhost",
            "MYSQL_PORT": "not-a-number",
            "MYSQL_DATABASE": "diameter",
            "MYSQL_USER": "user",
            "MYSQL_PASSWORD": "password",
        },
        clear=True,
    )
    def test_invalid_mysql_port_has_clear_error(self) -> None:
        with self.assertRaisesRegex(DatabaseLoadError, "MYSQL_PORT must be a number"):
            load_report_from_database("28", "80632389")

    def test_explicit_connection_settings_are_validated(self) -> None:
        settings = MySQLConnectionSettings(
            host="mysql.local",
            port="invalid",
            database="diameter",
            user="operator",
            password="secret",
        )

        with self.assertRaisesRegex(DatabaseLoadError, "MYSQL_PORT must be a number"):
            load_report_from_database("28", "80632389", settings)


if __name__ == "__main__":
    unittest.main()
