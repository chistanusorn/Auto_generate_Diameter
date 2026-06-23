Option Strict On
Option Explicit On

Public NotInheritable Class ReportBuilder
    Private Const ColumnsPerPage As Integer = 22
    Private Const RowsPerColumn As Integer = 6

    Public Function Build(records As IEnumerable(Of SourceRecord), dateTimeStamp As String) As ImportedReport
        Dim ordered = records.
            OrderBy(Function(record) record.CoatLotSeq).
            ThenBy(Function(record) record.DipLotSeq).
            ThenBy(Function(record) record.RlType, StringComparer.OrdinalIgnoreCase).
            ToList()

        If ordered.Count = 0 Then
            Throw New ArgumentException("No records were supplied.", NameOf(records))
        End If

        Dim columns As New List(Of ColumnGroup)
        Dim hardGroups As New Dictionary(Of String, ColumnGroup)
        Dim zeroTrayLotGroups As New Dictionary(Of String, ColumnGroup)
        Dim zeroTrayLotSlots As New Dictionary(Of String, SlotReference)
        Dim noHardSlots As New Dictionary(Of String, SlotReference)
        Dim blankGroups As New HashSet(Of String)
        Dim noHardGroup As ColumnGroup = Nothing
        Dim noHardSlot = 0

        For Each record In ordered
            If Not HasDisplaySide(record) Then Continue For

            Dim lotNumber = LotKey(record)
            Dim trayNumber = FormatTray(record.TrayNumber)
            Dim trayLot = CleanLot(record.TrayLotNumber)
            Dim dipLot = CleanLot(record.DipLotNumber)
            Dim unitKey = Key(record.CoatLotSeq, record.DipLotSeq, lotNumber, trayNumber, record.RxArrangementNumber)

            If trayNumber = "" Then
                If blankGroups.Add(unitKey) Then
                    columns.Add(New ColumnGroup With {
                        .LotNumber = lotNumber,
                        .CoatLotNumber = record.CoatLotNumber,
                        .IsBlank = True
                    })
                End If
                Continue For
            End If

            If trayLot = "" AndAlso dipLot <> "" Then
                Dim groupKey = Key(record.CoatLotNumber, dipLot)
                If Not zeroTrayLotGroups.TryGetValue(groupKey, noHardGroup) Then
                    noHardGroup = New ColumnGroup With {
                        .LotNumber = dipLot,
                        .CoatLotNumber = record.CoatLotNumber,
                        .SequentialSlots = True
                    }
                    zeroTrayLotGroups.Add(groupKey, noHardGroup)
                    columns.Add(noHardGroup)
                End If

                Dim slotReference As SlotReference = Nothing
                If Not zeroTrayLotSlots.TryGetValue(unitKey, slotReference) Then
                    Dim slot = noHardGroup.Pairs.Count + 1
                    If slot > RowsPerColumn Then
                        Throw New InvalidOperationException($"Dip Lot {dipLot} contains more than 6 Trays.")
                    End If
                    slotReference = New SlotReference(noHardGroup, slot)
                    zeroTrayLotSlots.Add(unitKey, slotReference)
                End If
                AddDisplaySides(slotReference.Group, slotReference.Slot, record)
                Continue For
            End If

            If record.UsedFlag.Trim() = "1" AndAlso lotNumber <> "" Then
                Dim groupKey = Key(record.CoatLotNumber, lotNumber)
                Dim group As ColumnGroup = Nothing
                If Not hardGroups.TryGetValue(groupKey, group) Then
                    group = New ColumnGroup With {
                        .LotNumber = lotNumber,
                        .CoatLotNumber = record.CoatLotNumber
                    }
                    hardGroups.Add(groupKey, group)
                    columns.Add(group)
                End If
                AddDisplaySides(group, record.DipLotSeq, record)
                Continue For
            End If

            Dim noHardKey = Key(record.CoatLotSeq, record.DipLotSeq, "", trayNumber, record.RxArrangementNumber)
            Dim noHardReference As SlotReference = Nothing
            If Not noHardSlots.TryGetValue(noHardKey, noHardReference) Then
                If noHardGroup Is Nothing OrElse noHardSlot >= RowsPerColumn Then
                    noHardGroup = New ColumnGroup With {
                        .CoatLotNumber = record.CoatLotNumber,
                        .PackSlots = True
                    }
                    columns.Add(noHardGroup)
                    noHardSlot = 0
                End If
                noHardSlot += 1
                noHardReference = New SlotReference(noHardGroup, noHardSlot)
                noHardSlots.Add(noHardKey, noHardReference)
            End If
            AddDisplaySides(noHardReference.Group, noHardReference.Slot, record)
        Next

        Return BuildPages(columns, ordered(0), dateTimeStamp)
    End Function

    Private Shared Function BuildPages(columns As List(Of ColumnGroup), first As SourceRecord, dateTimeStamp As String) As ImportedReport
        Dim report As New ImportedReport()
        Dim totalPages = Math.Max(1, CInt(Math.Ceiling(columns.Count / CDbl(ColumnsPerPage))))

        For pageIndex = 0 To totalPages - 1
            Dim sheet As New SheetData With {
                .PageNumber = pageIndex + 1,
                .TotalPages = totalPages,
                .Item = first.CoatWorksheetName,
                .PlateNo = "",
                .DateTimeStamp = dateTimeStamp,
                .DomeType = "",
                .CoatLotNo = first.CoatLotNumber,
                .OperatorName = ""
            }

            Dim pageColumns = columns.Skip(pageIndex * ColumnsPerPage).Take(ColumnsPerPage).ToList()
            For localPosition = 1 To pageColumns.Count
                For Each pairSlot In PairSlots(pageColumns(localPosition - 1))
                    Dim right As SourceRecord = Nothing
                    Dim left As SourceRecord = Nothing
                    pairSlot.Value.Sides.TryGetValue("R", right)
                    pairSlot.Value.Sides.TryGetValue("L", left)
                    Dim source = If(right, left)
                    Dim rightDash = pairSlot.Value.DashSides.Contains("R")
                    Dim leftDash = pairSlot.Value.DashSides.Contains("L")
                    sheet.Measurements.Add(New Measurement With {
                        .TrayPosition = localPosition,
                        .RowNumber = pairSlot.Key,
                        .LotNumber = pageColumns(localPosition - 1).LotNumber,
                        .RDiameter = If(right Is Nothing, 0, right.Diameter),
                        .LDiameter = If(left Is Nothing, 0, left.Diameter),
                        .TrayNumber = FormatTray(source.TrayNumber),
                        .RPresent = right IsNot Nothing AndAlso Not rightDash,
                        .LPresent = left IsNot Nothing AndAlso Not leftDash,
                        .RDash = rightDash,
                        .LDash = leftDash
                    })
                Next
            Next
            report.Sheets.Add(sheet)
        Next

        Return report
    End Function

    Private Shared Function PairSlots(group As ColumnGroup) As IEnumerable(Of KeyValuePair(Of Integer, PairRecord))
        If group.IsBlank Then Return Enumerable.Empty(Of KeyValuePair(Of Integer, PairRecord))()
        If group.SequentialSlots Then Return group.Pairs.OrderBy(Function(item) item.Key)

        Dim normal = group.Pairs.Values.Where(Function(pair) Not IsFloatingPair(pair)).OrderBy(Function(pair) pair.Sequence).ToList()
        Dim floating = group.Pairs.Values.Where(AddressOf IsFloatingPair).OrderBy(Function(pair) pair.Sequence).ToList()
        If group.PackSlots Then
            Return normal.Concat(floating).Select(Function(pair, index) New KeyValuePair(Of Integer, PairRecord)(index + 1, pair))
        End If

        Dim slots As New SortedDictionary(Of Integer, PairRecord)
        For Each pair In normal.Where(Function(item) item.Sequence >= 1 AndAlso item.Sequence <= RowsPerColumn)
            slots(pair.Sequence) = pair
        Next
        For Each pair In floating
            Dim slot = Enumerable.Range(1, RowsPerColumn).FirstOrDefault(Function(candidate) Not slots.ContainsKey(candidate))
            If slot > 0 Then slots(slot) = pair
        Next
        Return slots
    End Function

    Private Shared Function HasDisplaySide(record As SourceRecord) As Boolean
        Dim rlType = record.RlType.Trim().ToUpperInvariant()
        Dim rlpLot = record.RlpLot.Trim().ToUpperInvariant()
        If rlpLot = "" Then Return rlType = "R" OrElse rlType = "L"
        Return (rlType = "R" OrElse rlType = "L" OrElse rlType = "P") AndAlso
            (rlpLot = "R" OrElse rlpLot = "L" OrElse rlpLot = "P")
    End Function

    Private Shared Sub AddDisplaySides(group As ColumnGroup, sequence As Integer, record As SourceRecord)
        Dim rlType = record.RlType.Trim().ToUpperInvariant()
        Dim rlpLot = record.RlpLot.Trim().ToUpperInvariant()

        If rlpLot = "" Then
            AddSide(group, sequence, rlType, record)
        ElseIf rlType = "P" AndAlso rlpLot = "P" Then
            AddSide(group, sequence, "R", record)
            AddSide(group, sequence, "L", record)
        ElseIf rlType = "R" AndAlso rlpLot = "L" Then
            AddSide(group, sequence, "R", record, True)
        ElseIf rlType = "L" AndAlso rlpLot = "R" Then
            AddSide(group, sequence, "L", record, True)
        ElseIf rlType = "R" AndAlso (rlpLot = "R" OrElse rlpLot = "P") Then
            AddSide(group, sequence, "R", record)
        ElseIf rlType = "L" AndAlso (rlpLot = "L" OrElse rlpLot = "P") Then
            AddSide(group, sequence, "L", record)
        End If
    End Sub

    Private Shared Sub AddSide(group As ColumnGroup, sequence As Integer, side As String, record As SourceRecord, Optional dash As Boolean = False)
        Dim pair As PairRecord = Nothing
        If Not group.Pairs.TryGetValue(sequence, pair) Then
            pair = New PairRecord With {.Sequence = sequence}
            group.Pairs.Add(sequence, pair)
        End If
        pair.Sides(side) = record
        If dash Then
            pair.DashSides.Add(side)
        Else
            pair.DashSides.Remove(side)
        End If
    End Sub

    Private Shared Function IsZeroPair(pair As PairRecord) As Boolean
        Return pair.Sides.Count > 0 AndAlso pair.Sides.Values.All(Function(record) record.Diameter = 0)
    End Function

    Private Shared Function IsFloatingPair(pair As PairRecord) As Boolean
        Return pair.Sequence < 1 OrElse pair.Sequence > RowsPerColumn OrElse IsZeroPair(pair)
    End Function

    Private Shared Function LotKey(record As SourceRecord) As String
        Dim dipLot = CleanLot(record.DipLotNumber)
        Return If(dipLot <> "", dipLot, CleanLot(record.TrayLotNumber))
    End Function

    Private Shared Function CleanLot(value As String) As String
        Dim text = If(value, "").Trim()
        Dim number As Double
        If Double.TryParse(text, number) AndAlso number = 0 Then Return ""
        Return text
    End Function

    Public Shared Function FormatTray(value As String) As String
        Dim text = If(value, "").Trim()
        If text.EndsWith(".0", StringComparison.Ordinal) Then text = text.Substring(0, text.Length - 2)
        Dim number As Long
        Return If(Long.TryParse(text, number), number.ToString("D6"), text)
    End Function

    Private Shared Function Key(ParamArray parts As Object()) As String
        Return String.Join(ChrW(31), parts.Select(Function(part) Convert.ToString(part, Globalization.CultureInfo.InvariantCulture)))
    End Function

    Private NotInheritable Class ColumnGroup
        Public Property LotNumber As String = ""
        Public Property CoatLotNumber As String = ""
        Public Property IsBlank As Boolean
        Public Property PackSlots As Boolean
        Public Property SequentialSlots As Boolean
        Public ReadOnly Property Pairs As New Dictionary(Of Integer, PairRecord)
    End Class

    Private NotInheritable Class PairRecord
        Public Property Sequence As Integer
        Public ReadOnly Property Sides As New Dictionary(Of String, SourceRecord)(StringComparer.OrdinalIgnoreCase)
        Public ReadOnly Property DashSides As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    End Class

    Private NotInheritable Class SlotReference
        Public Sub New(group As ColumnGroup, slot As Integer)
            Me.Group = group
            Me.Slot = slot
        End Sub

        Public ReadOnly Property Group As ColumnGroup
        Public ReadOnly Property Slot As Integer
    End Class
End Class
