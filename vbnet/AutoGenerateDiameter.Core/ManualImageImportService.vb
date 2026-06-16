Option Strict On
Option Explicit On

Imports System.ComponentModel
Imports System.Diagnostics
Imports System.Globalization
Imports System.Text.RegularExpressions

Public NotInheritable Class ManualImportRow
    Public Property TrayPosition As Integer
    Public Property RowNumber As Integer
    Public Property DipLotNumber As String = ""
    Public Property RDiameter As String = ""
    Public Property LDiameter As String = ""
End Class

Public NotInheritable Class ManualImageImportResult
    Public Property Rows As New BindingList(Of ManualImportRow)
    Public Property RawTsv As String = ""
    Public Property DetectedWordCount As Integer
End Class

Public NotInheritable Class ManualImageImportService
    Public Function ReadImage(path As String) As ManualImageImportResult
        If Not IO.File.Exists(path) Then Throw New IO.FileNotFoundException("Cannot read the selected image.", path)
        Dim executable = TesseractRuntime.FindExecutable()
        If executable Is Nothing Then Throw New InvalidOperationException("Tesseract OCR runtime is missing. Repair or reinstall the application.")

        Dim numericTsv = RunTesseract(executable, path, "11", "0123456789")
        Dim headerTsv = RunTesseract(executable, path, "6", Nothing)
        Return ParseTsv(numericTsv, headerTsv)
    End Function

    Private Shared Function RunTesseract(executable As String, path As String, pageSegmentationMode As String, whitelist As String) As String
        Dim startInfo As New ProcessStartInfo With {
            .FileName = executable,
            .UseShellExecute = False,
            .RedirectStandardOutput = True,
            .RedirectStandardError = True,
            .CreateNoWindow = True
        }
        startInfo.ArgumentList.Add(path)
        TesseractRuntime.Configure(startInfo, executable)
        startInfo.ArgumentList.Add("stdout")
        startInfo.ArgumentList.Add("--psm")
        startInfo.ArgumentList.Add(pageSegmentationMode)
        If Not String.IsNullOrWhiteSpace(whitelist) Then
            startInfo.ArgumentList.Add("-c")
            startInfo.ArgumentList.Add($"tessedit_char_whitelist={whitelist}")
        End If
        startInfo.ArgumentList.Add("tsv")

        Using ocrProcess As Process = Diagnostics.Process.Start(startInfo)
            If ocrProcess Is Nothing Then Throw New InvalidOperationException("Cannot start Tesseract OCR.")
            Dim tsv = ocrProcess.StandardOutput.ReadToEnd()
            Dim errorText = ocrProcess.StandardError.ReadToEnd()
            ocrProcess.WaitForExit()
            If ocrProcess.ExitCode <> 0 Then Throw New InvalidOperationException($"Tesseract OCR failed: {errorText}")
            Return tsv
        End Using
    End Function

    Public Function ParseTsv(tsv As String) As ManualImageImportResult
        Return ParseTsv(tsv, tsv)
    End Function

    Public Function ParseTsv(tsv As String, headerTsv As String) As ManualImageImportResult
        Dim words = ParseWords(tsv)
        If words.Count = 0 Then Throw New InvalidOperationException("OCR did not detect any numeric values.")
        Dim pageWidth = words.Max(Function(word) word.PageWidth)
        Dim pageHeight = words.Max(Function(word) word.PageHeight)
        Dim candidates = words.
            Select(Function(word) MapDiameter(word, pageWidth, pageHeight)).
            Where(Function(value) value IsNot Nothing).
            Cast(Of DiameterCandidate)().
            GroupBy(Function(value) $"{value.TrayPosition}:{value.RowNumber}:{value.Side}").
            Select(Function(group) group.OrderByDescending(Function(value) value.Confidence).First()).
            ToList()
        Dim dipLots = ParseDipLots(headerTsv)

        Dim rows As New BindingList(Of ManualImportRow)
        For Each slot In candidates.
            GroupBy(Function(value) New With {Key value.TrayPosition, Key value.RowNumber}).
            OrderBy(Function(group) group.Key.TrayPosition).
            ThenBy(Function(group) group.Key.RowNumber)
            Dim right = slot.FirstOrDefault(Function(value) value.Side = "R")
            Dim left = slot.FirstOrDefault(Function(value) value.Side = "L")
            rows.Add(New ManualImportRow With {
                .TrayPosition = slot.Key.TrayPosition,
                .RowNumber = slot.Key.RowNumber,
                .DipLotNumber = If(dipLots.GetValueOrDefault(slot.Key.TrayPosition), ""),
                .RDiameter = If(right Is Nothing, "", right.Value),
                .LDiameter = If(left Is Nothing, "", left.Value)
            })
        Next

        Return New ManualImageImportResult With {
            .Rows = rows,
            .RawTsv = tsv,
            .DetectedWordCount = words.Count
        }
    End Function

    Public Function BuildReport(
        rows As IEnumerable(Of ManualImportRow),
        item As String,
        plateNo As String,
        dateTimeStamp As String,
        domeType As String,
        capa As String,
        coatLotNo As String,
        longtail83 As Integer,
        longtail95 As Integer
    ) As ImportedReport
        Dim sheet As New SheetData With {
            .PageNumber = 1,
            .TotalPages = 1,
            .Item = item.Trim(),
            .PlateNo = plateNo.Trim(),
            .DateTimeStamp = dateTimeStamp.Trim(),
            .DomeType = domeType.Trim(),
            .Capa = capa.Trim(),
            .CoatLotNo = coatLotNo.Trim(),
            .Longtail83 = If(longtail83 <= 0, "", longtail83.ToString(CultureInfo.InvariantCulture)),
            .Longtail95 = If(longtail95 <= 0, "", longtail95.ToString(CultureInfo.InvariantCulture)),
            .OperatorName = ""
        }

        For Each row In rows.OrderBy(Function(value) value.TrayPosition).ThenBy(Function(value) value.RowNumber)
            If row.TrayPosition < 1 OrElse row.TrayPosition > 22 Then
                Throw New InvalidOperationException($"Tray position must be 1-22; found {row.TrayPosition}.")
            End If
            If row.RowNumber < 1 OrElse row.RowNumber > 6 Then
                Throw New InvalidOperationException($"Row number must be 1-6; found {row.RowNumber}.")
            End If
            Dim right = ParseDiameter(row.RDiameter, "R", row)
            Dim left = ParseDiameter(row.LDiameter, "L", row)
            If right Is Nothing AndAlso left Is Nothing Then Continue For
            sheet.Measurements.Add(New Measurement With {
                .TrayPosition = row.TrayPosition,
                .RowNumber = row.RowNumber,
                .LotNumber = row.DipLotNumber.Trim(),
                .RDiameter = If(right, 0),
                .LDiameter = If(left, 0),
                .TrayNumber = "",
                .RPresent = right.HasValue,
                .LPresent = left.HasValue
            })
        Next

        If sheet.Measurements.Count = 0 Then Throw New InvalidOperationException("No diameter values are available to import.")
        Return New ImportedReport With {.Sheets = New List(Of SheetData) From {sheet}}
    End Function

    Private Shared Function ParseDiameter(text As String, side As String, row As ManualImportRow) As Double?
        If String.IsNullOrWhiteSpace(text) Then Return Nothing
        Dim value As Double
        If Not Double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, value) OrElse value < 0 OrElse value > 99 Then
            Throw New InvalidOperationException($"Invalid {side} diameter at T-{row.TrayPosition} row {row.RowNumber}: {text}")
        End If
        Return value
    End Function

    Private Shared Function NormalizeDiameter(text As String, rowNumber As Integer) As String
        Dim value As Integer
        If Integer.TryParse(text, value) AndAlso (value = 0 OrElse (value >= 20 AndAlso value <= 99)) Then Return value.ToString(CultureInfo.InvariantCulture)

        ' Tesseract sometimes joins the printed row number to the diameter: "274" = row 2, diameter 74.
        If text.Length = 3 AndAlso text(0).ToString() = rowNumber.ToString(CultureInfo.InvariantCulture) Then
            Dim diameterText = text.Substring(1)
            If Integer.TryParse(diameterText, value) AndAlso (value = 0 OrElse (value >= 20 AndAlso value <= 99)) Then Return value.ToString(CultureInfo.InvariantCulture)
        End If
        Return Nothing
    End Function

    Private Shared Function MapDiameter(word As OcrWord, pageWidth As Integer, pageHeight As Integer) As DiameterCandidate
        Dim x = (word.Left + word.Width / 2.0) / pageWidth
        Dim y = (word.Top + word.Height / 2.0) / pageHeight
        Dim firstBlock = y >= 0.35 AndAlso y <= 0.6
        Dim secondBlock = y >= 0.66 AndAlso y <= 0.91
        If Not firstBlock AndAlso Not secondBlock Then Return Nothing

        Dim dataTop = If(firstBlock, 0.35, 0.66)
        Dim dataBottom = If(firstBlock, 0.6, 0.91)
        Dim row = Math.Clamp(CInt(Math.Floor((y - dataTop) / (dataBottom - dataTop) * 6)) + 1, 1, 6)
        Dim value = NormalizeDiameter(word.Text, row)
        If value Is Nothing Then Return Nothing
        Dim column = Math.Clamp(CInt(Math.Floor(x * 11)), 0, 10)
        Dim fraction = x * 11 - column
        If fraction < 0.18 Then Return Nothing
        Return New DiameterCandidate With {
            .TrayPosition = column + 1 + If(secondBlock, 11, 0),
            .RowNumber = row,
            .Side = If(fraction < 0.58, "R", "L"),
            .Value = value,
            .Confidence = word.Confidence
        }
    End Function

    Private Shared Function ParseDipLots(tsv As String) As Dictionary(Of Integer, String)
        Dim words = ParseWords(tsv)
        Dim result As New Dictionary(Of Integer, String)
        If words.Count = 0 Then Return result
        Dim pageWidth = words.Max(Function(word) word.PageWidth)
        Dim pageHeight = words.Max(Function(word) word.PageHeight)

        For Each word In words
            Dim match = Regex.Match(word.Text, "\((\d{6,})\)")
            If Not match.Success Then Continue For
            Dim x = (word.Left + word.Width / 2.0) / pageWidth
            Dim y = (word.Top + word.Height / 2.0) / pageHeight
            Dim firstHeader = y >= 0.29 AndAlso y < 0.35
            Dim secondHeader = y >= 0.6 AndAlso y < 0.66
            If Not firstHeader AndAlso Not secondHeader Then Continue For
            Dim column = Math.Clamp(CInt(Math.Floor(x * 11)), 0, 10)
            Dim trayPosition = column + 1 + If(secondHeader, 11, 0)
            result(trayPosition) = match.Groups(1).Value
        Next
        Return result
    End Function

    Private Shared Function ParseWords(tsv As String) As List(Of OcrWord)
        Dim result As New List(Of OcrWord)
        Dim pageWidth = 0
        Dim pageHeight = 0
        For Each line In tsv.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).Skip(1)
            Dim fields = line.Split(ControlChars.Tab)
            If fields.Length < 12 Then Continue For
            Dim level As Integer
            If Not Integer.TryParse(fields(0), level) Then Continue For
            If level = 1 Then
                Integer.TryParse(fields(8), pageWidth)
                Integer.TryParse(fields(9), pageHeight)
                Continue For
            End If
            If level <> 5 OrElse String.IsNullOrWhiteSpace(fields(11)) Then Continue For
            Dim word As New OcrWord With {.PageWidth = pageWidth, .PageHeight = pageHeight, .Text = fields(11).Trim()}
            Integer.TryParse(fields(6), word.Left)
            Integer.TryParse(fields(7), word.Top)
            Integer.TryParse(fields(8), word.Width)
            Integer.TryParse(fields(9), word.Height)
            Double.TryParse(fields(10), NumberStyles.Any, CultureInfo.InvariantCulture, word.Confidence)
            result.Add(word)
        Next
        Return result
    End Function

    Private NotInheritable Class OcrWord
        Public Property PageWidth As Integer
        Public Property PageHeight As Integer
        Public Property Left As Integer
        Public Property Top As Integer
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property Confidence As Double
        Public Property Text As String = ""
    End Class

    Private NotInheritable Class DiameterCandidate
        Public Property TrayPosition As Integer
        Public Property RowNumber As Integer
        Public Property Side As String = ""
        Public Property Value As String = ""
        Public Property Confidence As Double
    End Class
End Class
