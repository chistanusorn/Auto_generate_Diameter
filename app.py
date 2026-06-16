from __future__ import annotations

import tkinter as tk
import sqlite3
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from database import (
    initialize_database,
    load_sheet,
    replace_with_imported_sheets,
    update_report_header,
)
from database_loader import (
    DatabaseLoadError,
    MySQLConnectionSettings,
    load_report_from_database,
)
from manual_compare import ManualCompareError, compare_manual_image
from sheet_view import DiameterSheetView
from xlsx_importer import XlsxImportError, parse_xlsx


class DiameterApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("Auto Generate Diameter Sheet")
        self.geometry("1440x850")
        self.minsize(1000, 650)
        self.configure(background="#e9eef5")

        initialize_database()
        self.page_number = 1
        self.status_value = tk.StringVar()
        self.activity_value = tk.StringVar(value="Ready")

        self._configure_styles()
        self._build_toolbar()
        self._build_preview()
        self._build_status_bar()
        self.load_page()

    def _configure_styles(self) -> None:
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure(".", font=("Segoe UI", 10))
        style.configure("App.TFrame", background="#e9eef5")
        style.configure("Toolbar.TFrame", background="#ffffff")
        style.configure(
            "Title.TLabel",
            background="#ffffff",
            foreground="#172033",
            font=("Segoe UI", 18, "bold"),
        )
        style.configure(
            "Subtitle.TLabel",
            background="#ffffff",
            foreground="#667085",
            font=("Segoe UI", 9),
        )
        style.configure(
            "Group.TLabel",
            background="#ffffff",
            foreground="#667085",
            font=("Segoe UI", 8, "bold"),
        )
        style.configure(
            "Primary.TButton",
            background="#2563eb",
            foreground="#ffffff",
            borderwidth=0,
            padding=(14, 8),
            font=("Segoe UI", 10, "bold"),
        )
        style.map(
            "Primary.TButton",
            background=[("active", "#1d4ed8"), ("pressed", "#1e40af")],
        )
        style.configure(
            "Action.TButton",
            background="#f2f4f7",
            foreground="#344054",
            borderwidth=0,
            padding=(12, 8),
        )
        style.map(
            "Action.TButton",
            background=[("active", "#e4e7ec"), ("pressed", "#d0d5dd")],
        )
        style.configure(
            "Nav.TButton",
            background="#ffffff",
            foreground="#344054",
            bordercolor="#d0d5dd",
            padding=(12, 7),
        )
        style.map(
            "Nav.TButton",
            background=[("active", "#f2f4f7"), ("disabled", "#f9fafb")],
            foreground=[("disabled", "#98a2b3")],
        )
        style.configure(
            "Status.TFrame",
            background="#172033",
        )
        style.configure(
            "Status.TLabel",
            background="#172033",
            foreground="#d0d5dd",
            font=("Segoe UI", 9),
        )
        style.configure(
            "StatusStrong.TLabel",
            background="#172033",
            foreground="#ffffff",
            font=("Segoe UI", 9, "bold"),
        )

    def _build_toolbar(self) -> None:
        toolbar = ttk.Frame(self, padding=(18, 12), style="Toolbar.TFrame")
        toolbar.pack(fill="x")

        brand = ttk.Frame(toolbar, style="Toolbar.TFrame")
        brand.pack(side="left", padx=(0, 28))
        ttk.Label(
            brand,
            text="AUTO GENERATE DIAMETER",
            style="Title.TLabel",
        ).pack(anchor="w")
        ttk.Label(
            brand,
            text="Coating tray report workspace",
            style="Subtitle.TLabel",
        ).pack(anchor="w")

        data_group = ttk.Frame(toolbar, style="Toolbar.TFrame")
        data_group.pack(side="left", padx=(0, 22))
        ttk.Label(data_group, text="DATA", style="Group.TLabel").pack(anchor="w")
        data_buttons = ttk.Frame(data_group, style="Toolbar.TFrame")
        data_buttons.pack(pady=(3, 0))
        ttk.Button(
            data_buttons,
            text="Load Database",
            command=self.load_from_database,
            style="Action.TButton",
        ).pack(side="left", padx=(0, 6))
        ttk.Button(
            data_buttons,
            text="Import XLSX",
            command=self.import_xlsx,
            style="Action.TButton",
        ).pack(side="left")

        tools_group = ttk.Frame(toolbar, style="Toolbar.TFrame")
        tools_group.pack(side="left")
        ttk.Label(tools_group, text="TOOLS", style="Group.TLabel").pack(anchor="w")
        tool_buttons = ttk.Frame(tools_group, style="Toolbar.TFrame")
        tool_buttons.pack(pady=(3, 0))
        ttk.Button(
            tool_buttons,
            text="Edit Header",
            command=self.edit_header,
            style="Action.TButton",
        ).pack(side="left", padx=(0, 6))
        ttk.Button(
            tool_buttons,
            text="Compare Manual",
            command=self.compare_manual,
            style="Action.TButton",
        ).pack(side="left")

        navigation = ttk.Frame(toolbar, style="Toolbar.TFrame")
        navigation.pack(side="right")
        ttk.Label(navigation, text="REPORT PAGE", style="Group.TLabel").pack(anchor="e")
        nav_buttons = ttk.Frame(navigation, style="Toolbar.TFrame")
        nav_buttons.pack(pady=(3, 0))
        self.previous_button = ttk.Button(
            nav_buttons,
            text="< Previous",
            command=lambda: self.change_page(-1),
            style="Nav.TButton",
        )
        self.previous_button.pack(side="left", padx=(0, 6))
        self.next_button = ttk.Button(
            nav_buttons,
            text="Next >",
            command=lambda: self.change_page(1),
            style="Nav.TButton",
        )
        self.next_button.pack(side="left")

    def _build_preview(self) -> None:
        container = ttk.Frame(self, padding=(12, 12, 12, 8), style="App.TFrame")
        container.pack(fill="both", expand=True)

        self.sheet_view = DiameterSheetView(container)
        x_scroll = ttk.Scrollbar(container, orient="horizontal", command=self.sheet_view.xview)
        y_scroll = ttk.Scrollbar(container, orient="vertical", command=self.sheet_view.yview)
        self.sheet_view.configure(xscrollcommand=x_scroll.set, yscrollcommand=y_scroll.set)

        container.columnconfigure(0, weight=1)
        container.rowconfigure(0, weight=1)
        self.sheet_view.grid(row=0, column=0, sticky="nsew")
        y_scroll.grid(row=0, column=1, sticky="ns")
        x_scroll.grid(row=1, column=0, sticky="ew")

    def _build_status_bar(self) -> None:
        status = ttk.Frame(self, padding=(14, 7), style="Status.TFrame")
        status.pack(fill="x", side="bottom")
        ttk.Label(
            status,
            textvariable=self.activity_value,
            style="StatusStrong.TLabel",
        ).pack(side="left")
        ttk.Label(
            status,
            text="Wheel: Zoom   |   Shift+Wheel: Left/Right   |   Left drag: Pan",
            style="Status.TLabel",
        ).pack(side="left", padx=(22, 0))
        ttk.Label(
            status,
            textvariable=self.status_value,
            style="Status.TLabel",
        ).pack(side="right")

    def _set_activity(self, text: str) -> None:
        self.activity_value.set(text)
        self.update_idletasks()

    def load_page(self, activity: str = "Ready") -> None:
        sheet = load_sheet(self.page_number)
        self.page_number = sheet.page_number
        self.sheet_view.show_sheet(sheet)
        self.previous_button.configure(
            state="normal" if sheet.page_number > 1 else "disabled"
        )
        self.next_button.configure(
            state="normal" if sheet.page_number < sheet.total_pages else "disabled"
        )
        self.activity_value.set(activity)
        self.status_value.set(
            f"Local cache: diameter_mock.db   |   Page {sheet.page_number}/{sheet.total_pages}   |   Records: {len(sheet.measurements)}"
        )

    def change_page(self, offset: int) -> None:
        self.page_number += offset
        self.load_page()

    def import_xlsx(self) -> None:
        path = filedialog.askopenfilename(
            title="Import diameter data from XLSX",
            filetypes=(("Excel workbook", "*.xlsx"), ("All files", "*.*")),
        )
        if not path:
            return

        self._set_activity(f"Importing {Path(path).name}...")
        try:
            report = parse_xlsx(path)
            replace_with_imported_sheets(report.sheets)
            self.page_number = 1
        except (XlsxImportError, OSError, sqlite3.Error) as error:
            self._set_activity("Import failed")
            messagebox.showerror("Import XLSX failed", str(error))
            return

        self.load_page("XLSX imported")
        messagebox.showinfo(
            "Import XLSX complete",
            f"Imported {report.measurement_count} R/L pairs into "
            f"{len(report.sheets)} page(s) from {Path(path).name}.",
        )

    def load_from_database(self) -> None:
        dialog = DatabaseCriteriaDialog(self)
        self.wait_window(dialog)
        if dialog.result is None:
            return

        settings, place_code, lot_number = dialog.result
        self._set_activity(f"Loading Coat Lot {lot_number} from MySQL...")
        try:
            report = load_report_from_database(place_code, lot_number, settings)
            replace_with_imported_sheets(report.sheets)
            self.page_number = 1
        except (DatabaseLoadError, sqlite3.Error) as error:
            self._set_activity("Database load failed")
            messagebox.showerror("Load from Database failed", str(error))
            return

        self.load_page("Database loaded")
        messagebox.showinfo(
            "Load from Database complete",
            f"Loaded {report.measurement_count} R/L pairs into "
            f"{len(report.sheets)} page(s).",
        )

    def compare_manual(self) -> None:
        path = filedialog.askopenfilename(
            title="Compare manual sheet image",
            filetypes=(
                ("Image files", "*.png *.jpg *.jpeg *.bmp *.tif *.tiff"),
                ("All files", "*.*"),
            ),
        )
        if not path:
            return

        self._set_activity(f"Comparing {Path(path).name}...")
        try:
            result = compare_manual_image(path, load_sheet(self.page_number))
        except (ManualCompareError, OSError) as error:
            self._set_activity("Manual comparison failed")
            messagebox.showerror("Manual comparison failed", str(error))
            return
        self._set_activity("Manual comparison complete")
        ManualCompareDialog(self, result, Path(path).name)

    def edit_header(self) -> None:
        dialog = HeaderEditDialog(self, load_sheet(self.page_number))
        self.wait_window(dialog)
        if dialog.result is None:
            return
        try:
            update_report_header(**dialog.result)
        except sqlite3.Error as error:
            self._set_activity("Header update failed")
            messagebox.showerror("Edit Header failed", str(error))
            return
        self.load_page("Header updated")


class DatabaseCriteriaDialog(tk.Toplevel):
    def __init__(self, parent: tk.Misc) -> None:
        super().__init__(parent)
        self.title("Load from Database")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.result: tuple[MySQLConnectionSettings, str, str] | None = None

        frame = ttk.Frame(self, padding=16)
        frame.pack(fill="both", expand=True)
        ttk.Label(
            frame,
            text="MySQL Connection",
            font=("Segoe UI", 11, "bold"),
        ).grid(row=0, column=0, columnspan=2, sticky="w", pady=(0, 8))

        connection_fields = (
            ("MySQL Host", "host"),
            ("MySQL Port", "port"),
            ("Database", "database"),
            ("Username", "user"),
            ("Password", "password"),
        )
        self.connection_entries: dict[str, ttk.Entry] = {}
        for row, (label, name) in enumerate(connection_fields, start=1):
            ttk.Label(frame, text=label).grid(row=row, column=0, sticky="w", pady=5)
            entry = ttk.Entry(frame, width=32, show="*" if name == "password" else "")
            entry.grid(row=row, column=1, padx=(12, 0), pady=5)
            self.connection_entries[name] = entry
        self.connection_entries["port"].insert(0, "3306")

        ttk.Separator(frame).grid(
            row=6,
            column=0,
            columnspan=2,
            sticky="ew",
            pady=(12, 10),
        )
        ttk.Label(
            frame,
            text="Report Criteria",
            font=("Segoe UI", 11, "bold"),
        ).grid(row=7, column=0, columnspan=2, sticky="w", pady=(0, 8))
        ttk.Label(frame, text="PPC (Production place code)").grid(row=8, column=0, sticky="w", pady=5)
        ttk.Label(frame, text="CLN (Coat Lot No.)").grid(row=9, column=0, sticky="w", pady=5)
        self.place_code = ttk.Entry(frame, width=28)
        self.lot_number = ttk.Entry(frame, width=28)
        self.place_code.grid(row=8, column=1, padx=(12, 0), pady=5)
        self.lot_number.grid(row=9, column=1, padx=(12, 0), pady=5)

        buttons = ttk.Frame(frame)
        buttons.grid(row=10, column=0, columnspan=2, sticky="e", pady=(12, 0))
        ttk.Button(buttons, text="Cancel", command=self.destroy).pack(side="right")
        ttk.Button(buttons, text="Load", command=self.submit).pack(side="right", padx=6)
        self.bind("<Return>", lambda _event: self.submit())
        self.bind("<Escape>", lambda _event: self.destroy())
        self.connection_entries["host"].focus_set()

    def submit(self) -> None:
        connection_values = {
            name: entry.get().strip()
            for name, entry in self.connection_entries.items()
        }
        place_code = self.place_code.get().strip()
        lot_number = self.lot_number.get().strip()
        required_values = (*connection_values.values(), place_code, lot_number)
        if not all(required_values):
            messagebox.showwarning(
                "Missing input",
                "Enter all MySQL connection fields, PPC and CLN.",
                parent=self,
            )
            return
        self.result = (
            MySQLConnectionSettings(**connection_values),
            place_code,
            lot_number,
        )
        self.destroy()


class ManualCompareDialog(tk.Toplevel):
    def __init__(self, parent: tk.Misc, result: object, filename: str) -> None:
        super().__init__(parent)
        self.title(f"Manual comparison - {filename}")
        self.geometry("760x620")
        self.transient(parent)

        frame = ttk.Frame(self, padding=12)
        frame.pack(fill="both", expand=True)
        ttk.Label(
            frame,
            text=f"Found {result.found_count}/{result.total_count} expected values",
            font=("Arial", 13, "bold"),
        ).pack(anchor="w", pady=(0, 8))
        ttk.Label(
            frame,
            text="OCR can misread handwriting. Review every MISSING value before action.",
        ).pack(anchor="w", pady=(0, 8))

        tree = ttk.Treeview(
            frame,
            columns=("category", "value", "status"),
            show="headings",
            height=16,
        )
        tree.heading("category", text="Category")
        tree.heading("value", text="Expected value")
        tree.heading("status", text="Result")
        tree.column("category", width=150)
        tree.column("value", width=300)
        tree.column("status", width=100, anchor="center")
        tree.tag_configure("missing", foreground="#c1121f")
        tree.tag_configure("found", foreground="#18794e")
        for item in result.items:
            status = "FOUND" if item.found else "MISSING"
            tree.insert(
                "",
                "end",
                values=(item.category, item.value, status),
                tags=("found" if item.found else "missing",),
            )
        tree.pack(fill="both", expand=True)

        ttk.Label(frame, text="Raw OCR text").pack(anchor="w", pady=(10, 3))
        raw = tk.Text(frame, height=6, wrap="word")
        raw.insert("1.0", result.raw_text)
        raw.configure(state="disabled")
        raw.pack(fill="x")
        ttk.Button(frame, text="Close", command=self.destroy).pack(anchor="e", pady=(8, 0))


class HeaderEditDialog(tk.Toplevel):
    FIELDS = (
        ("item", "ITEM"),
        ("dome_type", "DOME Type"),
        ("capa", "CAPA."),
        ("datetime_stamp", "DATETIME"),
    )

    def __init__(self, parent: tk.Misc, sheet: object) -> None:
        super().__init__(parent)
        self.title("Edit Report Header")
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()
        self.result: dict[str, str] | None = None
        self.entries: dict[str, ttk.Entry] = {}

        frame = ttk.Frame(self, padding=16)
        frame.pack(fill="both", expand=True)
        for row, (name, label) in enumerate(self.FIELDS):
            ttk.Label(frame, text=label).grid(row=row, column=0, sticky="w", pady=5)
            entry = ttk.Entry(frame, width=32)
            entry.insert(0, str(getattr(sheet, name)))
            entry.grid(row=row, column=1, padx=(12, 0), pady=5)
            self.entries[name] = entry

        buttons = ttk.Frame(frame)
        buttons.grid(row=len(self.FIELDS), column=0, columnspan=2, sticky="e", pady=(12, 0))
        ttk.Button(buttons, text="Cancel", command=self.destroy).pack(side="right")
        ttk.Button(buttons, text="Save", command=self.submit).pack(side="right", padx=6)
        self.bind("<Return>", lambda _event: self.submit())
        self.bind("<Escape>", lambda _event: self.destroy())
        self.entries["item"].focus_set()

    def submit(self) -> None:
        self.result = {name: entry.get() for name, entry in self.entries.items()}
        self.destroy()


if __name__ == "__main__":
    DiameterApp().mainloop()
