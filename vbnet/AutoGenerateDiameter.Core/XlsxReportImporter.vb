Option Strict On
Option Explicit On

Imports ClosedXML.Excel

Public NotInheritable Class XlsxReportImporter
    Private Shared ReadOnly RequiredColumns As String() = {
        "coat_lot_number", "coat_lot_seq", "dip_lot_number", "diplt_seq",
        "rl_type", "rlp_lot", "tray_number", "item_type_name", "rxarrangement_number",
        "order_route_type_name", "traylot_number", "used_flag", "diameter"
    }

    Public Function Import(path As String) As ImportedReport
        If Not IO.File.Exists(path) Then Throw New IO.FileNotFoundException("XLSX file was not found.", path)

        Using workbook As New XLWorkbook(path)
            Dim worksheet = workbook.Worksheets.First()
            Dim headerRow = worksheet.FirstRowUsed()
            If headerRow Is Nothing Then Throw New InvalidOperationException("The first worksheet is empty.")

            Dim headers = headerRow.CellsUsed().ToDictionary(
                Function(cell) cell.GetString().Trim(),
                Function(cell) cell.Address.ColumnNumber,
                StringComparer.OrdinalIgnoreCase
            )
            Dim missing = RequiredColumns.Where(Function(column) Not headers.ContainsKey(column)).ToArray()
            If missing.Length > 0 Then
                Throw New InvalidOperationException($"Missing required columns: {String.Join(", ", missing)}")
            End If

            Dim records As New List(Of SourceRecord)
            Dim lastRow = worksheet.LastRowUsed().RowNumber()
            For rowNumber = headerRow.RowNumber() + 1 To lastRow
                Dim row = worksheet.Row(rowNumber)
                If row.IsEmpty() Then Continue For
                records.Add(New SourceRecord With {
                    .CoatLotNumber = Value(row, headers, "coat_lot_number"),
                    .CoatLotSeq = IntegerValue(row, headers, "coat_lot_seq"),
                    .DipLotNumber = Value(row, headers, "dip_lot_number"),
                    .DipLotSeq = IntegerValue(row, headers, "diplt_seq"),
                    .RlType = Value(row, headers, "rl_type"),
                    .RlpLot = Value(row, headers, "rlp_lot"),
                    .TrayNumber = Value(row, headers, "tray_number"),
                    .ItemTypeName = Value(row, headers, "item_type_name"),
                    .RxArrangementNumber = Value(row, headers, "rxarrangement_number"),
                    .OrderRouteTypeName = Value(row, headers, "order_route_type_name"),
                    .TrayLotNumber = Value(row, headers, "traylot_number"),
                    .UsedFlag = Value(row, headers, "used_flag"),
                    .Diameter = DoubleValue(row, headers, "diameter")
                })
            Next

            If records.Count = 0 Then Throw New InvalidOperationException("The worksheet has headers but no data rows.")
            Return New ReportBuilder().Build(records, IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"))
        End Using
    End Function

    Private Shared Function Value(row As IXLRow, headers As Dictionary(Of String, Integer), name As String) As String
        Return row.Cell(headers(name)).GetFormattedString().Trim()
    End Function

    Private Shared Function IntegerValue(row As IXLRow, headers As Dictionary(Of String, Integer), name As String) As Integer
        If String.IsNullOrWhiteSpace(Value(row, headers, name)) Then Return 0
        Return CInt(DoubleValue(row, headers, name))
    End Function

    Private Shared Function DoubleValue(row As IXLRow, headers As Dictionary(Of String, Integer), name As String) As Double
        Dim text = Value(row, headers, name)
        Dim number As Double
        If Not Double.TryParse(text, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, number) Then
            Throw New InvalidOperationException($"Column {name} must be a number; found '{text}'.")
        End If
        Return number
    End Function
End Class
