Option Strict On
Option Explicit On

Imports ClosedXML.Excel
Imports System.Text.RegularExpressions

Public NotInheritable Class XlsxReportImporter
    Private Const HeaderSearchRowLimit As Integer = 50

    Private Shared ReadOnly RequiredColumns As String() = {
        "coat_lot_number", "coat_lot_seq", "dip_lot_number", "diplt_seq",
        "rl_type", "rlp_lot", "tray_number", "item_type_name", "rxarrangement_number",
        "order_route_type_name", "traylot_number", "used_flag", "diameter"
    }

    Private Shared ReadOnly ColumnAliases As Dictionary(Of String, String) = BuildColumnAliases()

    Public Function Import(path As String) As ImportedReport
        If Not IO.File.Exists(path) Then Throw New IO.FileNotFoundException("XLSX file was not found.", path)

        Using workbook As New XLWorkbook(path)
            Dim worksheet = workbook.Worksheets.First()
            Dim firstRow = worksheet.FirstRowUsed()
            If firstRow Is Nothing Then Throw New InvalidOperationException("The first worksheet is empty.")

            Dim headerRow = FindHeaderRow(worksheet, firstRow.RowNumber())
            Dim headers = BuildHeaderMap(headerRow)
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
                    .Diameter = DoubleValue(row, headers, "diameter"),
                    .CoatWorksheetName = If(headers.ContainsKey("coat_worksheet_name"), Value(row, headers, "coat_worksheet_name"), "")
                })
            Next

            If records.Count = 0 Then Throw New InvalidOperationException("The worksheet has headers but no data rows.")
            Return New ReportBuilder().Build(records, IO.File.GetLastWriteTime(path).ToString("yyyy-MM-dd HH:mm"))
        End Using
    End Function

    Private Shared Function FindHeaderRow(worksheet As IXLWorksheet, firstRowNumber As Integer) As IXLRow
        Dim lastRowNumber = worksheet.LastRowUsed().RowNumber()
        Dim searchUntil = Math.Min(lastRowNumber, firstRowNumber + HeaderSearchRowLimit - 1)
        Dim bestRow = worksheet.Row(firstRowNumber)
        Dim bestMatchCount = -1

        For rowNumber = firstRowNumber To searchUntil
            Dim row = worksheet.Row(rowNumber)
            Dim headers = BuildHeaderMap(row)
            Dim matchCount = RequiredColumns.Count(Function(column) headers.ContainsKey(column))
            If matchCount > bestMatchCount Then
                bestRow = row
                bestMatchCount = matchCount
            End If
            If matchCount = RequiredColumns.Length Then Return row
        Next

        Return bestRow
    End Function

    Private Shared Function BuildHeaderMap(row As IXLRow) As Dictionary(Of String, Integer)
        Dim headers As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
        For Each cell In row.CellsUsed()
            Dim columnName = CanonicalColumnName(cell.GetString())
            If String.IsNullOrWhiteSpace(columnName) OrElse headers.ContainsKey(columnName) Then Continue For
            headers(columnName) = cell.Address.ColumnNumber
        Next
        Return headers
    End Function

    Private Shared Function CanonicalColumnName(text As String) As String
        Dim normalized = Regex.Replace(text.Trim().ToLowerInvariant(), "[^a-z0-9]", "")
        If ColumnAliases.ContainsKey(normalized) Then Return ColumnAliases(normalized)
        Return text.Trim()
    End Function

    Private Shared Function BuildColumnAliases() As Dictionary(Of String, String)
        Dim aliases As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each column In RequiredColumns
            aliases(Regex.Replace(column.ToLowerInvariant(), "[^a-z0-9]", "")) = column
        Next

        ' Keep only non-matching alias name mapping to prevent accidental matches with short/abbreviated names
        AddAlias(aliases, "dip lot seq", "diplt_seq")
        Return aliases
    End Function

    Private Shared Sub AddAlias(aliases As Dictionary(Of String, String), aliasName As String, columnName As String)
        aliases(Regex.Replace(aliasName.ToLowerInvariant(), "[^a-z0-9]", "")) = columnName
    End Sub

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
