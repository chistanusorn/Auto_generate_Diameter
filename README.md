# Auto Generate Diameter Sheet

Python desktop mockup สำหรับ preview แบบฟอร์ม Diameter Sheet โดยข้อมูลสีแดงอ่านจาก SQLite

## Run

```powershell
python app.py
```

ครั้งแรกระบบจะสร้าง `diameter_mock.db` พร้อมข้อมูลตัวอย่าง 3 หน้าอัตโนมัติ

หมุน MouseWheel บน preview เพื่อ Zoom in/out. เมื่อ Import XLSX ข้อมูลเดิมทั้งหมดจะถูก
แทนที่

## Files

- `app.py` - หน้าหลัก, navigation, reload และ zoom
- `sheet_view.py` - วาดแบบฟอร์ม 22 trays × 6 rows
- `database.py` - schema, mock data และ SQL query
- `xlsx_importer.py` - อ่าน XLSX, validate columns และ map ข้อมูล R/L
- `database_loader.py` - ดึงข้อมูล MySQL ด้วย Production place code และ Coat Lot No.
- `coat_lot_query.sql` - SQL query พร้อม bind parameters

## Load from Database

กด `Load Database` แล้วกรอก MySQL Host, Port, Database, Username, Password,
PPC และ CLN ในหน้าต่างโปรแกรมได้โดยตรง

Environment variables ต่อไปนี้ยังใช้เป็น fallback สำหรับการเรียกผ่านโค้ด:

```powershell
$env:MYSQL_HOST="mysql-host"
$env:MYSQL_PORT="3306"
$env:MYSQL_DATABASE="database_name"
$env:MYSQL_USER="username"
$env:MYSQL_PASSWORD="password"
python app.py
```

กด `Load from Database` แล้วกรอก Production place code และ Coat Lot No.

หลัง Import XLSX หรือ Load from Database ค่า `ITEM`, `DOME Type`, `CAPA.` และ
`DATETIME` จะเว้นว่าง กด `Edit Header` เพื่อกรอกค่าและใช้กับทุกหน้าของ Report

## Compare Manual Image

กด `Compare Manual Image` แล้วเลือกรูปใบ Manual ระบบ OCR และเทียบค่าของหน้าปัจจุบัน:
Coat Lot, Tray Lot, Tray Number และ Diameter. ผล `MISSING` ต้องตรวจด้วยคน เพราะ OCR
ลายมืออาจอ่านผิด

## XLSX import mapping

- `coat_lot_number` -> Coat Lot No.
- `item_type_name` -> ITEM
- `rxarrangement_number` -> PLATE No.
- `order_route_type_name` -> DOME Type
- `dip_lot_number` -> lot number ของแต่ละ T
- `diplt_seq` -> แถว 1-6
- `rl_type` + `diameter` -> R/L diameter
- `tray_number` -> tray number

## Tray Lot report rules

- เรียง Tray ตาม `coat_lot_seq`
- เลือกชื่อกลุ่มจาก `dip_lot_number` ก่อน แล้วใช้ `traylot_number` เมื่อไม่มี Dip Lot
- `traylot_number = 0`: แยก T ตาม `dip_lot_number` แล้วเรียง Tray เป็นแถว 1-6
  ตาม `coat_lot_seq` โดยไม่ใช้ `diplt_seq`
- `used_flag = 1` และมี Lot: แยกหนึ่ง Column T ต่อ Lot
- กรณีอื่น: จัดทุก 6 Trays ต่อหนึ่ง Column T และยังแสดงข้อมูล
- Diameter เป็น 0: ย้ายไปช่องว่างแรกหลังข้อมูลปกติ
- R/L ขาดด้าน: แสดงเฉพาะด้านที่มี
- Tray Number ตัวเลขเติมศูนย์ซ้ายให้ครบ 6 หลัก
- มากกว่า 22 Columns: แบ่งเป็นหน้าถัดไป และใช้ Previous/Next เพื่อเปลี่ยนหน้า
- เลข T นับต่อเนื่องข้ามหน้า เช่น หน้า 2 เริ่ม T-23

ฐานข้อมูลจริงสามารถแทนที่ implementation ใน `database.py` โดยคง `SheetData` และ
`Measurement` interface เดิมไว้
