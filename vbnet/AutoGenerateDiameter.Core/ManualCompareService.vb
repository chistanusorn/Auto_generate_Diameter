Option Strict On
Option Explicit On

Imports System.Diagnostics
Imports System.Globalization
Imports System.Text.RegularExpressions
Imports System.Threading.Tasks

Public NotInheritable Class CompareItem
    Public Property Category As String = ""
    Public Property Position As String = ""
    Public Property Value As String = ""
    Public Property OcrValue As String = ""
    Public Property Confidence As Double
    Public Property Comparable As Boolean = True
    Public Property Status As String = "MISSING"
    Public Property Resolution As String = ""
    Public Property EmployeeId As String = ""
    Public Property EmployeeName As String = ""
    Public Property ManualValue As String = ""
    Public Property ConfirmedAt As DateTime?
    Public Property LeftRatio As Double
    Public Property TopRatio As Double
    Public Property WidthRatio As Double
    Public Property HeightRatio As Double

    Public ReadOnly Property Found As Boolean
        Get
            Return Status = "MATCH"
        End Get
    End Property

    Public ReadOnly Property Result As String
        Get
            If Resolution <> "" Then Return Resolution
            Return Status
        End Get
    End Property

    Public ReadOnly Property NeedsReview As Boolean
        Get
            Return Comparable AndAlso Status <> "MATCH" AndAlso Resolution = ""
        End Get
    End Property
End Class

Public NotInheritable Class CompareResult
    Public Property Items As New List(Of CompareItem)
    Public Property RawText As String = ""
    Public Property ImagePath As String = ""
    Public Property DetectedFormat As String = "OLD"

    Public ReadOnly Property FoundCount As Integer
        Get
            Return Items.Where(Function(item) item.Status = "MATCH").Count()
        End Get
    End Property

    Public ReadOnly Property ManuallyConfirmedCount As Integer
        Get
            Return Items.Where(Function(item) item.Resolution <> "" AndAlso item.Resolution <> "UNRESOLVED").Count()
        End Get
    End Property

    Public ReadOnly Property PendingCount As Integer
        Get
            Return Items.Where(Function(item) item.NeedsReview OrElse item.Resolution = "UNRESOLVED").Count()
        End Get
    End Property

    Public ReadOnly Property ComparableCount As Integer
        Get
            Return Items.Where(Function(item) item.Comparable).Count()
        End Get
    End Property

    Public ReadOnly Property NotAvailableCount As Integer
        Get
            Return Items.Where(Function(item) Not item.Comparable).Count()
        End Get
    End Property

    Public ReadOnly Property Verified As Boolean
        Get
            Return ComparableCount > 0 AndAlso PendingCount = 0
        End Get
    End Property
End Class

Public NotInheritable Class ManualCompareService
    Public Function CompareImage(path As String, sheet As SheetData) As CompareResult
        If Not IO.File.Exists(path) Then Throw New IO.FileNotFoundException("Cannot read the selected image.", path)
        Dim executable = TesseractRuntime.FindExecutable()
        If executable Is Nothing Then Throw New InvalidOperationException("Tesseract OCR runtime is missing. Repair or reinstall the application.")

        Dim numericTsvs = {"3", "6", "11", "12"}.
            Select(Function(psm) RunTesseract(executable, path, psm, "0123456789")).
            ToList()
        Dim textTsvs = {"6", "11"}.
            Select(Function(psm) RunTesseract(executable, path, psm, Nothing)).
            ToList()
        Return CompareTsvs(numericTsvs, textTsvs, sheet, path)
    End Function

    ' Cell-level OCR: instead of running Tesseract on the whole sheet and then matching
    ' detected numbers to cells by distance, this crops every expected cell first and OCRs
    ' it in isolation. The geometry must be reliable, so callers should only use this on a
    ' perspective-aligned/cropped image (see LayoutFor's "aligned" branch).
    '
    ' fullPreprocessedPath : whole binarized sheet, used only for the metadata (lot/tray) text pass.
    ' cellExtractor        : given a normalized CellRegion, returns a temp PNG path of that cropped,
    '                        preprocessed cell. Implemented by the imaging layer (ImagePreprocessor.ExtractCell).
    Public Function CompareImageByCell(fullPreprocessedPath As String, sheet As SheetData, cellExtractor As Func(Of CellRegion, String)) As CompareResult
        If cellExtractor Is Nothing Then Throw New ArgumentNullException(NameOf(cellExtractor))
        If Not IO.File.Exists(fullPreprocessedPath) Then Throw New IO.FileNotFoundException("Cannot read the selected image.", fullPreprocessedPath)
        Dim executable = TesseractRuntime.FindExecutable()
        If executable Is Nothing Then Throw New InvalidOperationException("Tesseract OCR runtime is missing. Repair or reinstall the application.")

        Dim layout = LayoutFor(0, 0, fullPreprocessedPath)

        ' Build one target per expected R/L value.
        Dim targets As New List(Of CellTarget)
        For Each measurement In sheet.Measurements.OrderBy(Function(item) item.TrayPosition).ThenBy(Function(item) item.RowNumber)
            If measurement.RPresent Then targets.Add(NewCellTarget(layout, measurement, "R", measurement.RDiameter))
            If measurement.LPresent Then targets.Add(NewCellTarget(layout, measurement, "L", measurement.LDiameter))
        Next

        ' Crop serially — GDI+ on a shared source image is not thread-safe.
        For Each target In targets
            target.CellPath = cellExtractor(target.Region)
        Next

        ' OCR in parallel — each call is an independent Tesseract subprocess, so this is the
        ' part worth parallelizing (a few hundred cells would otherwise run for minutes).
        Parallel.ForEach(targets, Sub(target)
                                      Try
                                          Dim cell = OcrCell(executable, target.CellPath, target.Measurement.RowNumber)
                                          target.OcrValue = cell.Value
                                          target.OcrConfidence = cell.Confidence
                                      Catch
                                          target.OcrValue = ""
                                          target.OcrConfidence = 0
                                      End Try
                                  End Sub)

        ' Clean up cropped cell temp files.
        For Each target In targets
            DeleteCellTemp(target.CellPath)
        Next

        Dim result As New CompareResult With {
            .ImagePath = fullPreprocessedPath,
            .RawText = String.Join(" ", targets.Where(Function(t) t.OcrValue <> "").Select(Function(t) t.OcrValue)),
            .DetectedFormat = "ALIGNED"
        }

        For Each target In targets
            result.Items.Add(BuildCellItem(target))
        Next

        ' Metadata (tray lot / tray number) still needs a whole-image text pass.
        Dim textTsvs = {"6", "11"}.Select(Function(psm) RunTesseract(executable, fullPreprocessedPath, psm, Nothing)).ToList()
        Dim textTsv = String.Join(Environment.NewLine, textTsvs)
        Dim hasMetadata = HasTrayMetadata(textTsv)
        Dim expectsMetadata = sheet.Measurements.Any(Function(item) item.LotNumber <> "" OrElse item.TrayNumber <> "")
        result.DetectedFormat = If(hasMetadata, "NEW", If(expectsMetadata, "ALIGNED", "OLD"))
        AddOptionalMetadata(result, textTsv, sheet)
        Return result
    End Function

    ' Public entry point used by accuracy tests: OCR a single already-cropped cell image.
    Public Function OcrCellImage(cellPath As String, rowNumber As Integer) As String
        Dim executable = TesseractRuntime.FindExecutable()
        If executable Is Nothing Then Throw New InvalidOperationException("Tesseract OCR runtime is missing.")
        Return OcrCell(executable, cellPath, rowNumber).Value
    End Function

    Private Function NewCellTarget(layout As TableLayout, measurement As Measurement, side As String, expected As Double) As CellTarget
        Dim bounds = CellBounds(layout, measurement.TrayPosition, measurement.RowNumber, side)
        Return New CellTarget With {
            .Measurement = measurement,
            .Side = side,
            .Expected = Math.Round(expected).ToString(CultureInfo.InvariantCulture),
            .Bounds = bounds,
            .Region = New CellRegion With {
                .TrayPosition = measurement.TrayPosition,
                .RowNumber = measurement.RowNumber,
                .Side = side,
                .Left = bounds.Left,
                .Top = bounds.Top,
                .Width = bounds.Width,
                .Height = bounds.Height
            }
        }
    End Function

    Private Shared Function BuildCellItem(target As CellTarget) As CompareItem
        Dim result = CreateItem("Diameter", target.Measurement, target.Side, target.Expected, target.Bounds)
        result.OcrValue = target.OcrValue
        result.Confidence = target.OcrConfidence
        If target.OcrValue = "" Then
            result.Status = "MISSING"
        ElseIf target.OcrConfidence < 40 Then
            result.Status = "LOW CONFIDENCE"
        ElseIf target.OcrValue = target.Expected Then
            result.Status = "MATCH"
        Else
            result.Status = "MISMATCH"
        End If
        Return result
    End Function

    ' Run a few single-cell PSM modes, merge by voting, return the best numeric reading.
    Private Shared Function OcrCell(executable As String, cellPath As String, rowNumber As Integer) As (Value As String, Confidence As Double)
        If String.IsNullOrWhiteSpace(cellPath) OrElse Not IO.File.Exists(cellPath) Then Return ("", 0)
        Dim tsvs = {"7", "8", "10", "6"}.
            Select(Function(psm) RunTesseract(executable, cellPath, psm, "0123456789")).
            ToList()
        Dim words = MergeOcrPasses(tsvs)
        Dim best = words.
            Select(Function(word) New With {.Word = word, .Value = NormalizeDiameter(word.Text, rowNumber)}).
            Where(Function(item) item.Value IsNot Nothing).
            OrderByDescending(Function(item) item.Word.VoteCount).
            ThenByDescending(Function(item) item.Word.Confidence).
            FirstOrDefault()
        If best Is Nothing Then Return ("", 0)
        Return (best.Value, Math.Max(best.Word.Confidence, Math.Min(99, best.Word.VoteCount * 25)))
    End Function

    Private Shared Sub DeleteCellTemp(path As String)
        If String.IsNullOrWhiteSpace(path) Then Return
        Dim fileName = IO.Path.GetFileName(path)
        If Not fileName.StartsWith("AutoGenerateDiameter-ocr-cell-", StringComparison.OrdinalIgnoreCase) Then Return
        Try
            If IO.File.Exists(path) Then IO.File.Delete(path)
        Catch
        End Try
    End Sub

    Private Shared Function RunTesseract(executable As String, path As String, psm As String, whitelist As String) As String
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
        startInfo.ArgumentList.Add("--dpi")
        startInfo.ArgumentList.Add("300")
        startInfo.ArgumentList.Add("--oem")
        startInfo.ArgumentList.Add("1")
        startInfo.ArgumentList.Add("--psm")
        startInfo.ArgumentList.Add(psm)
        If whitelist IsNot Nothing Then
            startInfo.ArgumentList.Add("-c")
            startInfo.ArgumentList.Add($"tessedit_char_whitelist={whitelist}")
        End If
        startInfo.ArgumentList.Add("tsv")

        Using process = Diagnostics.Process.Start(startInfo)
            If process Is Nothing Then Throw New InvalidOperationException("Cannot start Tesseract OCR.")
            Dim output = process.StandardOutput.ReadToEnd()
            Dim errors = process.StandardError.ReadToEnd()
            process.WaitForExit()
            If process.ExitCode <> 0 Then Throw New InvalidOperationException($"Tesseract OCR failed: {errors}")
            Return output
        End Using
    End Function

    Public Function CompareTsv(numericTsv As String, textTsv As String, sheet As SheetData, Optional imagePath As String = "") As CompareResult
        Return CompareTsvs({numericTsv}, {textTsv}, sheet, imagePath)
    End Function

    Public Function CompareTsvs(numericTsvs As IEnumerable(Of String), textTsvs As IEnumerable(Of String), sheet As SheetData, Optional imagePath As String = "") As CompareResult
        Dim words = MergeOcrPasses(numericTsvs)
        If words.Count = 0 Then Throw New InvalidOperationException("OCR did not detect numeric values.")
        Dim layout = LayoutFor(words(0).PageWidth, words(0).PageHeight, imagePath)
        Dim textTsv = String.Join(Environment.NewLine, textTsvs)
        Dim hasMetadata = HasTrayMetadata(textTsv)
        Dim expectsMetadata = sheet.Measurements.Any(Function(item) item.LotNumber <> "" OrElse item.TrayNumber <> "")
        Dim result As New CompareResult With {
            .ImagePath = imagePath,
            .RawText = String.Join(" ", words.Select(Function(word) word.Text)),
            .DetectedFormat = If(hasMetadata, "NEW", If(expectsMetadata, "UNKNOWN", "OLD"))
        }
        Dim used As New HashSet(Of OcrWord)

        For Each measurement In sheet.Measurements.OrderBy(Function(item) item.TrayPosition).ThenBy(Function(item) item.RowNumber)
            If measurement.RPresent Then result.Items.Add(CompareDiameter(words, used, layout, measurement, "R", measurement.RDiameter))
            If measurement.LPresent Then result.Items.Add(CompareDiameter(words, used, layout, measurement, "L", measurement.LDiameter))
        Next

        AddUnexpectedDiameters(result, words, used, layout, sheet)
        AddOptionalMetadata(result, textTsv, sheet)
        Return result
    End Function

    Private Shared Function CompareDiameter(words As List(Of OcrWord), used As HashSet(Of OcrWord), layout As TableLayout, measurement As Measurement, side As String, expected As Double) As CompareItem
        Dim bounds = CellBounds(layout, measurement.TrayPosition, measurement.RowNumber, side)
        Dim expectedText = Math.Round(expected).ToString(CultureInfo.InvariantCulture)
        Dim candidates = words.
            Where(Function(word) Not used.Contains(word)).
            Select(Function(word) New With {.Word = word, .Value = NormalizeDiameter(word.Text, measurement.RowNumber), .Distance = Distance(word, bounds)}).
            Where(Function(item) item.Value IsNot Nothing AndAlso item.Distance < 1.35).
            OrderByDescending(Function(item) item.Word.VoteCount).
            ThenByDescending(Function(item) item.Word.Confidence).
            ThenBy(Function(item) item.Distance).
            ThenByDescending(Function(item) item.Value = expectedText).
            ToList()
        Dim result = CreateItem("Diameter", measurement, side, expectedText, bounds)
        If candidates.Count = 0 Then Return result

        Dim selected = candidates(0)
        used.Add(selected.Word)
        result.OcrValue = selected.Value
        result.Confidence = Math.Max(selected.Word.Confidence, Math.Min(99, selected.Word.VoteCount * 25))
        If result.Confidence < 40 Then
            result.Status = "LOW CONFIDENCE"
        ElseIf selected.Value = expectedText Then
            result.Status = "MATCH"
        Else
            result.Status = "MISMATCH"
        End If
        Return result
    End Function

    Private Shared Function MergeOcrPasses(tsvs As IEnumerable(Of String)) As List(Of OcrWord)
        Dim merged As New List(Of OcrWord)
        Dim passNumber = 0
        For Each tsv In tsvs
            passNumber += 1
            For Each word In ParseWords(tsv)
                word.PassNumber = passNumber
                Dim existing = merged.FirstOrDefault(
                    Function(candidate) candidate.Text = word.Text AndAlso
                        Math.Abs(candidate.CenterX - word.CenterX) <= 0.012 AndAlso
                        Math.Abs(candidate.CenterY - word.CenterY) <= 0.018
                )
                If existing Is Nothing Then
                    word.VoteCount = 1
                    merged.Add(word)
                Else
                    existing.VoteCount += 1
                    If word.Confidence > existing.Confidence Then
                        existing.Confidence = word.Confidence
                        existing.Left = word.Left
                        existing.Top = word.Top
                        existing.Width = word.Width
                        existing.Height = word.Height
                    End If
                End If
            Next
        Next
        Return merged
    End Function

    Private Shared Sub AddUnexpectedDiameters(result As CompareResult, words As List(Of OcrWord), used As HashSet(Of OcrWord), layout As TableLayout, sheet As SheetData)
        For Each word In words.Where(Function(candidate) Not used.Contains(candidate))
            Dim slot = NearestSlot(word, layout)
            If slot Is Nothing Then Continue For
            Dim value = NormalizeDiameter(word.Text, slot.RowNumber)
            If value Is Nothing Then Continue For
            Dim exists = sheet.Measurements.Any(
                Function(measurement) measurement.TrayPosition = slot.TrayPosition AndAlso
                    measurement.RowNumber = slot.RowNumber AndAlso
                    If(slot.Side = "R", measurement.RPresent, measurement.LPresent)
            )
            If exists Then Continue For
            Dim item = CreateItem("Diameter", slot.TrayPosition, slot.RowNumber, slot.Side, "", slot.Bounds)
            item.OcrValue = value
            item.Confidence = word.Confidence
            item.Status = If(word.Confidence < 40, "LOW CONFIDENCE", "UNEXPECTED VALUE")
            result.Items.Add(item)
        Next
    End Sub

    Private Shared Sub AddOptionalMetadata(result As CompareResult, textTsv As String, sheet As SheetData)
        Dim text = String.Join(" ", ParseWords(textTsv).Select(Function(word) word.Text))
        Dim foundLots = Regex.Matches(text, "\((\d{6,})\)").Select(Function(match) match.Groups(1).Value).ToHashSet()
        For Each lot In sheet.Measurements.Select(Function(item) item.LotNumber).Where(Function(value) value <> "").Distinct()
            Dim available = foundLots.Count > 0
            result.Items.Add(New CompareItem With {
                .Category = "Tray Lot",
                .Value = lot,
                .OcrValue = If(foundLots.Contains(lot), lot, ""),
                .Comparable = available,
                .Status = If(Not available, "NOT AVAILABLE", If(foundLots.Contains(lot), "MATCH", "MISSING"))
            })
        Next
        Dim expectedTrayNumbers = sheet.Measurements.Select(Function(item) item.TrayNumber).Where(Function(value) value <> "").Distinct().ToList()
        Dim foundTrayNumbers = expectedTrayNumbers.Where(Function(value) Regex.IsMatch(text, $"(?<!\d){Regex.Escape(value)}(?!\d)")).ToHashSet()
        For Each trayNumber In expectedTrayNumbers
            Dim available = foundTrayNumbers.Count > 0
            result.Items.Add(New CompareItem With {
                .Category = "Tray Number",
                .Value = trayNumber,
                .OcrValue = If(foundTrayNumbers.Contains(trayNumber), trayNumber, ""),
                .Comparable = available,
                .Status = If(Not available, "NOT AVAILABLE", If(foundTrayNumbers.Contains(trayNumber), "MATCH", "MISSING"))
            })
        Next
    End Sub

    Public Function CompareTokens(tokens As ISet(Of String), sheet As SheetData, Optional rawText As String = "", Optional compareTrayLots As Boolean = True, Optional compareTrayNumbers As Boolean = True) As CompareResult
        Dim result As New CompareResult With {.RawText = rawText}
        For Each measurement In sheet.Measurements
            If measurement.RPresent Then result.Items.Add(TokenItem(tokens, measurement, "R", measurement.RDiameter))
            If measurement.LPresent Then result.Items.Add(TokenItem(tokens, measurement, "L", measurement.LDiameter))
        Next
        If Not String.IsNullOrWhiteSpace(sheet.CoatLotNo) Then
            result.Items.Add(TokenMetadataItem(tokens, "Coat Lot", sheet.CoatLotNo))
        End If
        If compareTrayLots Then
            For Each lot In sheet.Measurements.Select(Function(item) item.LotNumber).Where(Function(value) value <> "").Distinct()
                result.Items.Add(TokenMetadataItem(tokens, "Tray Lot", lot))
            Next
        End If
        If compareTrayNumbers Then
            For Each trayNumber In sheet.Measurements.Select(Function(item) item.TrayNumber).Where(Function(value) value <> "").Distinct()
                result.Items.Add(TokenMetadataItem(tokens, "Tray Number", trayNumber))
            Next
        End If
        Return result
    End Function

    Private Shared Function TokenMetadataItem(tokens As ISet(Of String), category As String, value As String) As CompareItem
        Return New CompareItem With {
            .Category = category,
            .Value = value,
            .OcrValue = If(tokens.Contains(value), value, ""),
            .Comparable = True,
            .Status = If(tokens.Contains(value), "MATCH", "MISSING")
        }
    End Function

    Private Shared Function TokenItem(tokens As ISet(Of String), measurement As Measurement, side As String, value As Double) As CompareItem
        Dim expected = Math.Round(value).ToString(CultureInfo.InvariantCulture)
        Dim item = CreateItem("Diameter", measurement, side, expected, New NormalizedBounds())
        item.OcrValue = If(tokens.Contains(expected), expected, "")
        item.Status = If(item.OcrValue = "", "MISSING", "MATCH")
        Return item
    End Function

    Private Shared Function CreateItem(category As String, measurement As Measurement, side As String, value As String, bounds As NormalizedBounds) As CompareItem
        Return CreateItem(category, measurement.TrayPosition, measurement.RowNumber, side, value, bounds)
    End Function

    Private Shared Function CreateItem(category As String, tray As Integer, row As Integer, side As String, value As String, bounds As NormalizedBounds) As CompareItem
        Return New CompareItem With {
            .Category = category,
            .Position = $"T-{tray} / Row {row} / {side}",
            .Value = value,
            .Status = "MISSING",
            .LeftRatio = bounds.Left,
            .TopRatio = bounds.Top,
            .WidthRatio = bounds.Width,
            .HeightRatio = bounds.Height
        }
    End Function

    Private Shared Function NormalizeDiameter(text As String, row As Integer) As String
        Dim value As Integer
        If Integer.TryParse(text, value) AndAlso (value = 0 OrElse (value >= 20 AndAlso value <= 99)) Then Return value.ToString(CultureInfo.InvariantCulture)
        If text.Length = 3 AndAlso text(0).ToString() = row.ToString(CultureInfo.InvariantCulture) AndAlso Integer.TryParse(text.Substring(1), value) AndAlso (value = 0 OrElse (value >= 20 AndAlso value <= 99)) Then
            Return value.ToString(CultureInfo.InvariantCulture)
        End If
        Return Nothing
    End Function

    Private Shared Function HasTrayMetadata(tsv As String) As Boolean
        Return Regex.IsMatch(tsv, "\(\d{6,}\)")
    End Function

    Private Shared Function LayoutFor(width As Integer, height As Integer, Optional imagePath As String = "") As TableLayout
        If IO.Path.GetFileName(imagePath).StartsWith("AutoGenerateDiameter-aligned-", StringComparison.OrdinalIgnoreCase) Then
            ' A manually aligned image contains only the outer T-1 to T-22 table.
            Return New TableLayout With {.Left = 0, .Right = 1, .FirstTop = 0.075, .FirstBottom = 0.49, .SecondTop = 0.585, .SecondBottom = 1}
        End If
        If width / CDbl(height) < 1.5 Then
            ' Old photographed form: data rows start immediately below the R/L header.
            Return New TableLayout With {.Left = 0.075, .Right = 0.91, .FirstTop = 0.38, .FirstBottom = 0.57, .SecondTop = 0.62, .SecondBottom = 0.8}
        End If
        Return New TableLayout With {.Left = 0, .Right = 1, .FirstTop = 0.35, .FirstBottom = 0.6, .SecondTop = 0.66, .SecondBottom = 0.91}
    End Function

    Private Shared Function CellBounds(layout As TableLayout, tray As Integer, row As Integer, side As String) As NormalizedBounds
        Dim localTray = (tray - 1) Mod 11
        Dim top = If(tray <= 11, layout.FirstTop, layout.SecondTop)
        Dim bottom = If(tray <= 11, layout.FirstBottom, layout.SecondBottom)
        Dim trayWidth = (layout.Right - layout.Left) / 11
        Dim rowHeight = (bottom - top) / 6
        Return New NormalizedBounds With {
            .Left = layout.Left + localTray * trayWidth + If(side = "L", trayWidth / 2, 0),
            .Top = top + (row - 1) * rowHeight,
            .Width = trayWidth / 2,
            .Height = rowHeight
        }
    End Function

    Private Shared Function Distance(word As OcrWord, bounds As NormalizedBounds) As Double
        Dim dx = (word.CenterX - (bounds.Left + bounds.Width / 2)) / bounds.Width
        Dim dy = (word.CenterY - (bounds.Top + bounds.Height / 2)) / bounds.Height
        Return Math.Sqrt(dx * dx + dy * dy)
    End Function

    Private Shared Function NearestSlot(word As OcrWord, layout As TableLayout) As Slot
        Dim best As Slot = Nothing
        Dim bestDistance = Double.MaxValue
        For tray = 1 To 22
            For row = 1 To 6
                For Each side In {"R", "L"}
                    Dim bounds = CellBounds(layout, tray, row, side)
                    Dim candidateDistance = Distance(word, bounds)
                    If candidateDistance < bestDistance Then
                        bestDistance = candidateDistance
                        best = New Slot With {.TrayPosition = tray, .RowNumber = row, .Side = side, .Bounds = bounds}
                    End If
                Next
            Next
        Next
        Return If(bestDistance < 1.0, best, Nothing)
    End Function

    Private Shared Function ParseWords(tsv As String) As List(Of OcrWord)
        Dim result As New List(Of OcrWord)
        Dim pageWidth = 1
        Dim pageHeight = 1
        For Each line In tsv.Split({ControlChars.Cr, ControlChars.Lf}, StringSplitOptions.RemoveEmptyEntries).Skip(1)
            Dim fields = line.Split(ControlChars.Tab)
            If fields.Length < 12 Then Continue For
            If fields(0) = "1" Then
                Integer.TryParse(fields(8), pageWidth)
                Integer.TryParse(fields(9), pageHeight)
            ElseIf fields(0) = "5" AndAlso fields(11).Trim() <> "" Then
                Dim word As New OcrWord With {.Text = fields(11).Trim(), .PageWidth = pageWidth, .PageHeight = pageHeight}
                Integer.TryParse(fields(6), word.Left)
                Integer.TryParse(fields(7), word.Top)
                Integer.TryParse(fields(8), word.Width)
                Integer.TryParse(fields(9), word.Height)
                Double.TryParse(fields(10), NumberStyles.Any, CultureInfo.InvariantCulture, word.Confidence)
                result.Add(word)
            End If
        Next
        Return result
    End Function

    Private NotInheritable Class OcrWord
        Public Property Text As String = ""
        Public Property Confidence As Double
        Public Property PassNumber As Integer
        Public Property VoteCount As Integer = 1
        Public Property Left As Integer
        Public Property Top As Integer
        Public Property Width As Integer
        Public Property Height As Integer
        Public Property PageWidth As Integer
        Public Property PageHeight As Integer
        Public ReadOnly Property CenterX As Double
            Get
                Return (Left + Width / 2.0) / PageWidth
            End Get
        End Property
        Public ReadOnly Property CenterY As Double
            Get
                Return (Top + Height / 2.0) / PageHeight
            End Get
        End Property
    End Class

    Private NotInheritable Class TableLayout
        Public Property Left As Double
        Public Property Right As Double
        Public Property FirstTop As Double
        Public Property FirstBottom As Double
        Public Property SecondTop As Double
        Public Property SecondBottom As Double
    End Class

    Private NotInheritable Class NormalizedBounds
        Public Property Left As Double
        Public Property Top As Double
        Public Property Width As Double
        Public Property Height As Double
    End Class

    Private NotInheritable Class Slot
        Public Property TrayPosition As Integer
        Public Property RowNumber As Integer
        Public Property Side As String = ""
        Public Property Bounds As NormalizedBounds
    End Class

    Private NotInheritable Class CellTarget
        Public Property Measurement As Measurement
        Public Property Side As String = ""
        Public Property Expected As String = ""
        Public Property Bounds As NormalizedBounds
        Public Property Region As CellRegion
        Public Property CellPath As String = ""
        Public Property OcrValue As String = ""
        Public Property OcrConfidence As Double
    End Class
End Class
